using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Motely.Filters;
using Motely.Utils;

namespace Motely.Filters;

/// <summary>
/// Composite filter that directly calls multiple filters and combines their results
/// BYPASSES the broken batching system entirely!
/// </summary>
public struct MotelyCompositeFilterDesc(List<MotelyJsonConfig.MotleyJsonFilterClause> mustClauses)
    : IMotelySeedFilterDesc<MotelyCompositeFilterDesc.MotelyCompositeFilter>
{
    private readonly List<MotelyJsonConfig.MotleyJsonFilterClause> _mustClauses = mustClauses;

    public MotelyCompositeFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        var clausesByCategory = FilterCategoryMapper.GroupClausesByCategory(_mustClauses);

        // Create individual filters for each category, tracking which are inverted
        var filterEntries = new List<(IMotelySeedFilter filter, bool isInverted)>();

        foreach (var kvp in clausesByCategory)
        {
            var category = kvp.Key;
            var clauses = kvp.Value;

            // Check if ALL clauses in this category are inverted (mustNot)
            bool isInverted = clauses.All(c => c.IsInverted);

            IMotelySeedFilter filter = category switch
            {
                FilterCategory.Joker => new MotelyJsonJokerFilterDesc(
                    MotelyJsonJokerFilterClause.CreateCriteria(
                        MotelyJsonJokerFilterClause.ConvertClauses(clauses)
                    )
                ).CreateFilter(ref ctx),
                FilterCategory.SpectralCard => new MotelyJsonSpectralCardFilterDesc(
                    MotelyJsonSpectralFilterClause.CreateCriteria(
                        MotelyJsonSpectralFilterClause.ConvertClauses(clauses)
                    )
                ).CreateFilter(ref ctx),
                FilterCategory.SoulJoker => new MotelyJsonSoulJokerFilterDesc(
                    MotelyJsonSoulJokerFilterClause.CreateCriteria(
                        MotelyJsonSoulJokerFilterClause.ConvertClauses(clauses)
                    )
                ).CreateFilter(ref ctx),
                FilterCategory.SoulJokerEditionOnly => new MotelyJsonSoulJokerEditionOnlyFilterDesc(
                    MotelyJsonSoulJokerFilterClause.CreateCriteria(
                        MotelyJsonSoulJokerFilterClause.ConvertClauses(clauses)
                    )
                ).CreateFilter(ref ctx),
                FilterCategory.TarotCard => new MotelyJsonTarotCardFilterDesc(
                    MotelyJsonTarotFilterClause.CreateCriteria(
                        MotelyJsonTarotFilterClause.ConvertClauses(clauses)
                    )
                ).CreateFilter(ref ctx),
                FilterCategory.PlanetCard => new MotelyJsonPlanetFilterDesc(
                    MotelyJsonPlanetFilterClause.CreateCriteria(
                        MotelyJsonPlanetFilterClause.ConvertClauses(clauses)
                    )
                ).CreateFilter(ref ctx),
                FilterCategory.PlayingCard => new MotelyJsonPlayingCardFilterDesc(
                    MotelyJsonFilterClauseExtensions.CreatePlayingCardCriteria(clauses)
                ).CreateFilter(ref ctx),
                FilterCategory.Voucher => new MotelyJsonVoucherFilterDesc(
                    MotelyJsonVoucherFilterClause.CreateCriteria(
                        MotelyJsonVoucherFilterClause.ConvertClauses(clauses)
                    )
                ).CreateFilter(ref ctx),
                FilterCategory.Boss => new MotelyJsonBossFilterDesc(
                    MotelyJsonFilterClauseExtensions.CreateBossCriteria(clauses)
                ).CreateFilter(ref ctx),
                FilterCategory.Tag => new MotelyJsonTagFilterDesc(
                    MotelyJsonFilterClauseExtensions.CreateTagCriteria(clauses)
                ).CreateFilter(ref ctx),
                FilterCategory.And => CreateAndFilter(clauses, ref ctx),
                FilterCategory.Or => CreateOrFilter(clauses, ref ctx),
                _ => throw new ArgumentException($"Unsupported filter category: {category}"),
            };
            filterEntries.Add((filter, isInverted));
        }

        return new MotelyCompositeFilter(filterEntries);
    }

    // Helper method to recursively clone a clause with a specific ante, propagating to ALL descendants
    private static MotelyJsonConfig.MotleyJsonFilterClause CloneClauseWithAnte(
        MotelyJsonConfig.MotleyJsonFilterClause source,
        int ante
    )
    {
        var cloned = new MotelyJsonConfig.MotleyJsonFilterClause
        {
            Type = source.Type,
            Value = source.Value,
            Values = source.Values,
            Label = source.Label,
            Antes = new[] { ante }, // SINGLE ante! Override with the propagated ante
            AntesWasExplicitlySet = true, // Mark as explicitly set since we're propagating from parent
            IsInverted = source.IsInverted,
            Score = source.Score,
            Mode = source.Mode,
            Min = source.Min,
            FilterOrder = source.FilterOrder,
            Edition = source.Edition,
            Stickers = source.Stickers,
            Suit = source.Suit,
            Rank = source.Rank,
            Seal = source.Seal,
            Enhancement = source.Enhancement,
            Sources = source.Sources,
            PackSlots = source.PackSlots,
            ShopSlots = source.ShopSlots,
            MinShopSlot = source.MinShopSlot,
            MaxShopSlot = source.MaxShopSlot,
            MinPackSlot = source.MinPackSlot,
            MaxPackSlot = source.MaxPackSlot,
        };

        // Copy parsed enums (already initialized by parent)
        cloned.CopyParsedEnumsFrom(source);

        // Recursively clone nested clauses with the same ante!
        if (source.Clauses != null && source.Clauses.Count > 0)
        {
            cloned.Clauses = new List<MotelyJsonConfig.MotleyJsonFilterClause>();
            foreach (var nestedClause in source.Clauses)
            {
                cloned.Clauses.Add(CloneClauseWithAnte(nestedClause, ante));
            }
        }

        return cloned;
    }

    private static IMotelySeedFilter CreateAndFilter(
        List<MotelyJsonConfig.MotleyJsonFilterClause> andClauses,
        ref MotelyFilterCreationContext ctx
    )
    {
        // AND filter: ALL nested clauses must pass
        var nestedFilters = new List<IMotelySeedFilter>();

        foreach (var andClause in andClauses)
        {
            if (andClause.Clauses == null || andClause.Clauses.Count == 0)
                continue; // Skip empty And clause

            // Check if Antes was EXPLICITLY SET (not just defaulted)
            // If explicitly set, use helper behavior (propagate to children)
            // If defaulted, respect individual child Antes
            if (
                andClause.AntesWasExplicitlySet
                && andClause.Antes != null
                && andClause.Antes.Length > 0
            )
            {
                // YES! Create separate AND groups for EACH ante, then OR them together
                // So: (child1[ante4] AND child2[ante4]) OR (child1[ante5] AND child2[ante5]) OR ...
                var anteSpecificAndFilters = new List<IMotelySeedFilter>();

                foreach (var ante in andClause.Antes)
                {
                    // Clone each child clause with this specific ante (RECURSIVELY!)
                    var clonedChildren = new List<MotelyJsonConfig.MotleyJsonFilterClause>();
                    foreach (var child in andClause.Clauses)
                    {
                        // Use the recursive helper to propagate ante to ALL descendants
                        var clonedChild = CloneClauseWithAnte(child, ante);
                        clonedChildren.Add(clonedChild);
                    }

                    // Create AND filter for this specific ante
                    var anteComposite = new MotelyCompositeFilterDesc(clonedChildren);
                    anteSpecificAndFilters.Add(anteComposite.CreateFilter(ref ctx));
                }

                // Wrap all ante-specific ANDs in an OR
                nestedFilters.Add(new OrFilter(anteSpecificAndFilters));
            }
            else
            {
                // No antes array on parent - just process normally
                var nestedComposite = new MotelyCompositeFilterDesc(andClause.Clauses);
                nestedFilters.Add(nestedComposite.CreateFilter(ref ctx));
            }
        }

        return new AndFilter(nestedFilters);
    }

    private static IMotelySeedFilter CreateOrFilter(
        List<MotelyJsonConfig.MotleyJsonFilterClause> orClauses,
        ref MotelyFilterCreationContext ctx
    )
    {
        // OR filter: at least ONE nested clause must pass
        var nestedFilters = new List<IMotelySeedFilter>();

        foreach (var orClause in orClauses)
        {
            if (orClause.Clauses == null || orClause.Clauses.Count == 0)
                continue; // Skip empty Or clause

            // Check if parent OR clause has Antes EXPLICITLY SET (not just defaulted)
            // If Antes was explicitly set, use helper behavior (propagate to children)
            // If Antes was defaulted (not explicitly set), respect individual child Antes
            if (
                orClause.AntesWasExplicitlySet
                && orClause.Antes != null
                && orClause.Antes.Length > 0
            )
            {
                // YES! Clone each child clause for each ante, then OR them all together
                // So: (child1[ante4]) OR (child2[ante4]) OR (child1[ante5]) OR (child2[ante5]) OR ...
                foreach (var ante in orClause.Antes)
                {
                    foreach (var child in orClause.Clauses)
                    {
                        // Use the recursive helper to propagate ante to ALL descendants
                        var clonedChild = CloneClauseWithAnte(child, ante);

                        // Create a composite filter with just this one cloned clause
                        var singleClauseList = new List<MotelyJsonConfig.MotleyJsonFilterClause>
                        {
                            clonedChild,
                        };
                        var nestedComposite = new MotelyCompositeFilterDesc(singleClauseList);
                        nestedFilters.Add(nestedComposite.CreateFilter(ref ctx));
                    }
                }
            }
            else
            {
                // No antes array on parent - process normally
                // Each clause in the OR should be its own branch
                // If we have ["King", "Queen", "Jack"], we want "King OR Queen OR Jack"
                // NOT "(King AND Queen AND Jack) as one group"
                // So we create a separate filter for EACH individual clause
                foreach (var individualClause in orClause.Clauses)
                {
                    // Create a composite filter with just this one clause
                    // This prevents same-type items from being grouped together
                    var singleClauseList = new List<MotelyJsonConfig.MotleyJsonFilterClause>
                    {
                        individualClause,
                    };
                    var nestedComposite = new MotelyCompositeFilterDesc(singleClauseList);
                    nestedFilters.Add(nestedComposite.CreateFilter(ref ctx));
                }
            }
        }

        return new OrFilter(nestedFilters);
    }

    public struct MotelyCompositeFilter : IMotelySeedFilter
    {
        private readonly List<(IMotelySeedFilter filter, bool isInverted)> _filterEntries;

        public MotelyCompositeFilter(
            List<(IMotelySeedFilter filter, bool isInverted)> filterEntries
        )
        {
            _filterEntries = filterEntries;
        }

        [MethodImpl(
            MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization
        )]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            // Start with all bits set
            VectorMask result = VectorMask.AllBitsSet;

            // Call each filter directly and AND the results (Must logic)
            foreach (var (filter, isInverted) in _filterEntries)
            {
                var filterMask = filter.Filter(ref ctx);

                // If this is a mustNot filter (inverted), negate the mask
                // Only invert VALID lanes - invalid lanes must stay rejected
                if (isInverted)
                {
                    uint validLaneMask = 0;
                    for (int lane = 0; lane < 8; lane++)
                    {
                        if (ctx.IsLaneValid(lane))
                        {
                            validLaneMask |= (1u << lane);
                        }
                    }
                    // Invert only valid lanes: valid lanes with 0 become 1, valid lanes with 1 become 0
                    filterMask = new VectorMask((~filterMask.Value) & validLaneMask);
                }

                result &= filterMask;

                // Early exit if no seeds pass
                if (result.IsAllFalse())
                    return VectorMask.NoBitsSet;
            }

            return result;
        }
    }

    /// <summary>
    /// AND Filter - ALL nested filters must pass
    /// </summary>
    public struct AndFilter : IMotelySeedFilter
    {
        private readonly List<IMotelySeedFilter> _nestedFilters;

        public AndFilter(List<IMotelySeedFilter> nestedFilters)
        {
            _nestedFilters = nestedFilters;
        }

        [MethodImpl(
            MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization
        )]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            if (_nestedFilters == null || _nestedFilters.Count == 0)
                return VectorMask.NoBitsSet; // Empty AND fails all

            // Start with all bits set, AND together all nested results
            VectorMask result = VectorMask.AllBitsSet;

            foreach (var filter in _nestedFilters)
            {
                result &= filter.Filter(ref ctx);

                if (result.IsAllFalse())
                    return VectorMask.NoBitsSet; // Early exit
            }

            return result;
        }
    }

    /// <summary>
    /// OR Filter - at least ONE nested filter must pass
    /// </summary>
    public struct OrFilter : IMotelySeedFilter
    {
        private readonly List<IMotelySeedFilter> _nestedFilters;

        public OrFilter(List<IMotelySeedFilter> nestedFilters)
        {
            _nestedFilters = nestedFilters;
        }

        [MethodImpl(
            MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization
        )]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            if (_nestedFilters == null || _nestedFilters.Count == 0)
                return VectorMask.NoBitsSet; // Empty OR fails all

            // Start with no bits set, OR together all nested results
            VectorMask result = VectorMask.NoBitsSet;

            foreach (var filter in _nestedFilters)
            {
                result |= filter.Filter(ref ctx);
            }

            return result;
        }
    }
}
