using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using Motely.Filters;

namespace Motely.Filters;

/// <summary>
/// Filters seeds based on planet card criteria from JSON configuration.
/// REVERTED: Simple version that compiles - shop detection removed for now
/// </summary>
public struct MotelyJsonPlanetFilterDesc(MotelyJsonPlanetFilterCriteria criteria)
    : IMotelySeedFilterDesc<MotelyJsonPlanetFilterDesc.MotelyJsonPlanetFilter>
{
    private readonly MotelyJsonPlanetFilterCriteria _criteria = criteria;

    public MotelyJsonPlanetFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        // Use pre-calculated values from criteria
        int minAnte = _criteria.MinAnte;
        int maxAnte = _criteria.MaxAnte;

        for (int ante = minAnte; ante <= maxAnte; ante++)
        {
            ctx.CacheShopStream(ante);
            ctx.CacheBoosterPackStream(ante);
        }

        return new MotelyJsonPlanetFilter(
            _criteria.Clauses,
            minAnte,
            maxAnte,
            _criteria.MaxShopSlotsNeeded
        );
    }

    public struct MotelyJsonPlanetFilter(
        List<MotelyJsonPlanetFilterClause> clauses,
        int minAnte,
        int maxAnte,
        int maxShopSlotsNeeded
    ) : IMotelySeedFilter
    {
        private readonly List<MotelyJsonPlanetFilterClause> _clauses = clauses;
        private readonly int _minAnte = minAnte;
        private readonly int _maxAnte = maxAnte;
        private readonly int _maxShopSlotsNeeded = maxShopSlotsNeeded;

        [MethodImpl(
            MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization
        )]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            if (_clauses == null || _clauses.Count == 0)
                return VectorMask.AllBitsSet;

            // Stack-allocated clause masks - accumulate results per clause across all antes
            Span<VectorMask> clauseMasks = stackalloc VectorMask[_clauses.Count];
            for (int i = 0; i < clauseMasks.Length; i++)
                clauseMasks[i] = VectorMask.NoBitsSet;

            // Initialize run state for voucher calculations
            var runState = ctx.Deck.GetDefaultRunState();

            // Loop antes first, then clauses - ensures one stream per ante!
            for (int ante = _minAnte; ante <= _maxAnte; ante++)
            {
                for (int clauseIndex = 0; clauseIndex < _clauses.Count; clauseIndex++)
                {
                    var clause = _clauses[clauseIndex];

                    // Skip ante if not wanted
                    if (!clause.WantedAntes[ante])
                        continue;

                    VectorMask clauseResult = VectorMask.NoBitsSet;

                    // Determine if we should use ante-based defaults
                    bool hasShopSlots = HasShopSlots(clause.WantedShopSlots);
                    bool hasPackSlots = HasPackSlots(clause.WantedPackSlots);
                    bool useDefaults = !hasShopSlots && !hasPackSlots;

                    // Check shops if explicitly wanted OR if using defaults
                    if (hasShopSlots || useDefaults)
                    {
                        // Use the self-contained shop planet stream - NO SYNCHRONIZATION ISSUES!
                        var shopPlanetStream = ctx.CreateShopPlanetStream(ante);
                        clauseResult |= CheckShopPlanetVectorized(
                            clause,
                            ctx,
                            ref shopPlanetStream,
                            ante
                        );
                    }

                    // Check packs if explicitly wanted OR if using defaults
                    if (hasPackSlots || useDefaults)
                    {
                        clauseResult |= CheckPacksVectorized(clause, ctx, ante);
                    }

                    // Accumulate results for this clause across all antes (OR logic)
                    clauseMasks[clauseIndex] |= clauseResult;
                }
            }

            // All clauses must be satisfied (AND logic)
            // If any clause found nothing (NoBitsSet), the entire filter fails!
            var resultMask = VectorMask.AllBitsSet;
            for (int i = 0; i < clauseMasks.Length; i++)
            {
                // FIX: If this clause found nothing across all antes, fail immediately
                if (clauseMasks[i].IsAllFalse())
                {
                    return VectorMask.NoBitsSet;
                }

                resultMask &= clauseMasks[i];
                if (resultMask.IsAllFalse())
                    return VectorMask.NoBitsSet;
            }

            // USE THE SHARED FUNCTION - same logic as scoring!
            var clauses = _clauses;
            return ctx.SearchIndividualSeeds(
                resultMask,
                (ref MotelySingleSearchContext singleCtx) =>
                {
                    // Check all clauses using the SAME shared function used in scoring
                    foreach (var clause in clauses)
                    {
                        // Count total occurrences across ALL wanted antes
                        int totalCount = 0;
                        for (int ante = 0; ante < clause.WantedAntes.Length; ante++)
                        {
                            if (!clause.WantedAntes[ante])
                                continue;

                            int anteCount = MotelyJsonScoring.CountPlanetOccurrences(
                                ref singleCtx,
                                clause,
                                ante,
                                earlyExit: false
                            );
                            totalCount += anteCount;
                        }

                        // Check Min threshold (default to 1 if not specified)
                        int minThreshold = clause.Min ?? 1;
                        if (totalCount < minThreshold)
                            return false;
                    }

                    return true;
                }
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static VectorMask CheckShopPlanetVectorized(
            MotelyJsonPlanetFilterClause clause,
            MotelyVectorSearchContext ctx,
            ref MotelyVectorPlanetStream shopPlanetStream,
            int ante
        )
        {
            VectorMask foundInShop = VectorMask.NoBitsSet;

            // Calculate max slot we need to check based on ante
            int maxSlot;
            if (!HasShopSlots(clause.WantedShopSlots))
            {
                // No slots specified - use ante-based defaults
                maxSlot = MotelyJsonScoring.GetDefaultShopSlotsForAnte(ante);
            }
            else
            {
                // User specified slots - find the highest wanted slot
                maxSlot = 0;
                for (int i = clause.WantedShopSlots.Length - 1; i >= 0; i--)
                {
                    if (clause.WantedShopSlots[i])
                    {
                        maxSlot = i + 1;
                        break;
                    }
                }
            }

            // NOTE: Shop planet checking not yet implemented (requires full stream availability)
            return foundInShop;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static VectorMask CheckPacksVectorized(
            MotelyJsonPlanetFilterClause clause,
            MotelyVectorSearchContext ctx,
            int ante
        )
        {
            VectorMask foundInPacks = VectorMask.NoBitsSet;

            // Create pack streams
            var packStream = ctx.CreateBoosterPackStream(ante);
            var celestialStream = ctx.CreateCelestialPackPlanetStream(ante);

            // Determine max pack slot to check - use config if provided or ante-based defaults
            bool hasSpecificSlots = HasPackSlots(clause.WantedPackSlots);
            int maxPackSlot = clause.MaxPackSlot.HasValue
                ? clause.MaxPackSlot.Value + 1
                : MotelyJsonScoring.GetDefaultPackSlotsForAnte(ante);

            for (int packSlot = 0; packSlot < maxPackSlot; packSlot++)
            {
                var pack = ctx.GetNextBoosterPack(ref packStream);

                // Check if this pack slot should be evaluated for scoring
                bool shouldEvaluateThisSlot = !hasSpecificSlots || clause.WantedPackSlots[packSlot];

                var packType = pack.GetPackType();

                // Check Celestial packs with vectorized method
                VectorMask isCelestialPack = VectorEnum256.Equals(
                    packType,
                    MotelyBoosterPackType.Celestial
                );
                if (isCelestialPack.IsPartiallyTrue())
                {
                    // FIXED: Always consume maximum pack size (5) to avoid stream desync
                    var contents = ctx.GetNextCelestialPackContents(
                        ref celestialStream,
                        MotelyBoosterPackSize.Mega
                    );

                    // Only evaluate/score if this slot should be checked
                    if (!shouldEvaluateThisSlot)
                        continue;

                    // Check each card in the pack
                    for (int cardIndex = 0; cardIndex < contents.Length; cardIndex++)
                    {
                        var card = contents[cardIndex];

                        // Check if this is a planet card that matches our clause
                        VectorMask isPlanetCard = VectorEnum256.Equals(
                            card.TypeCategory,
                            MotelyItemTypeCategory.PlanetCard
                        );

                        if (isPlanetCard.IsPartiallyTrue())
                        {
                            VectorMask typeMatches = VectorMask.AllBitsSet;
                            if (clause.PlanetTypes?.Count > 0)
                            {
                                VectorMask anyTypeMatch = VectorMask.NoBitsSet;
                                foreach (var planetType in clause.PlanetTypes)
                                {
                                    var targetType = (MotelyItemType)(
                                        (int)MotelyItemTypeCategory.PlanetCard | (int)planetType
                                    );
                                    anyTypeMatch |= VectorEnum256.Equals(card.Type, targetType);
                                }
                                typeMatches = anyTypeMatch;
                            }
                            else if (clause.PlanetType.HasValue)
                            {
                                var targetPlanetType = (MotelyItemType)(
                                    (int)MotelyItemTypeCategory.PlanetCard
                                    | (int)clause.PlanetType.Value
                                );
                                typeMatches = VectorEnum256.Equals(card.Type, targetPlanetType);
                            }

                            VectorMask editionMatches = VectorMask.AllBitsSet;
                            if (clause.EditionEnum.HasValue)
                            {
                                editionMatches = VectorEnum256.Equals(
                                    card.Edition,
                                    clause.EditionEnum.Value
                                );
                            }

                            VectorMask matches = (
                                isCelestialPack & isPlanetCard & typeMatches & editionMatches
                            );
                            foundInPacks |= matches;
                        }
                    }
                }
            }

            return foundInPacks;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HasShopSlots(bool[] slots)
        {
            for (int i = 0; i < slots.Length; i++)
                if (slots[i])
                    return true;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HasPackSlots(bool[] slots)
        {
            for (int i = 0; i < slots.Length; i++)
                if (slots[i])
                    return true;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool CheckPlanetTypeMatch(
            MotelyItem item,
            MotelyJsonPlanetFilterClause clause
        )
        {
            if (clause.PlanetTypes?.Count > 0)
            {
                foreach (var planetType in clause.PlanetTypes)
                {
                    if (
                        item.Type
                        == (MotelyItemType)(
                            (int)MotelyItemTypeCategory.PlanetCard | (int)planetType
                        )
                    )
                    {
                        return true;
                    }
                }
                return false;
            }
            else if (clause.PlanetType.HasValue)
            {
                return item.Type
                    == (MotelyItemType)(
                        (int)MotelyItemTypeCategory.PlanetCard | (int)clause.PlanetType.Value
                    );
            }
            else
            {
                return item.TypeCategory == MotelyItemTypeCategory.PlanetCard;
            }
        }
    }
}
