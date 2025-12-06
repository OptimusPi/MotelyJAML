using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Intrinsics;
using Motely.Filters;

namespace Motely.Filters;

/// <summary>
/// Filters seeds based on joker criteria from JSON configuration.
/// </summary>
public partial struct MotelyJsonJokerFilterDesc(MotelyJsonJokerFilterCriteria criteria)
    : IMotelySeedFilterDesc<MotelyJsonJokerFilterDesc.MotelyJsonJokerFilter>
{
    private readonly MotelyJsonJokerFilterCriteria _criteria = criteria;

    public MotelyJsonJokerFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        // Use pre-calculated values from criteria
        int minAnte = _criteria.MinAnte;
        int maxAnte = _criteria.MaxAnte;

        for (int ante = minAnte; ante <= maxAnte; ante++)
        {
            ctx.CacheShopStream(ante);
            ctx.CacheBoosterPackStream(ante);
        }

        return new MotelyJsonJokerFilter(_criteria.Clauses, minAnte, maxAnte);
    }

    public struct MotelyJsonJokerFilter(
        List<MotelyJsonJokerFilterClause> clauses,
        int minAnte,
        int maxAnte
    ) : IMotelySeedFilter
    {
        private readonly List<MotelyJsonJokerFilterClause> Clauses = clauses;
        private readonly int MinAnte = minAnte;
        private readonly int MaxAnte = maxAnte;

        [MethodImpl(
            MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization
        )]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            var _clauses = Clauses;
            int _minAnte = MinAnte;
            int _maxAnte = MaxAnte;
            if (_clauses == null || _clauses.Count == 0)
                return VectorMask.AllBitsSet;

            // Stack-allocated clause masks - accumulate results per clause across all antes
            Span<VectorMask> clauseMasks = stackalloc VectorMask[Clauses.Count];
            for (int i = 0; i < clauseMasks.Length; i++)
                clauseMasks[i] = VectorMask.NoBitsSet;

            // Initialize run state for voucher calculations
            var runState = ctx.Deck.GetDefaultRunState();

            // Walk each ante and score all clauses as we go
            for (int ante = _minAnte; ante <= _maxAnte; ante++)
            {
                // Determine max slots needed for THIS ante across ALL clauses
                int maxShopSlots = 0;
                int maxPackSlots = 0;
                for (int i = 0; i < Clauses.Count; i++)
                {
                    var clause = Clauses[i];
                    if (ante >= clause.WantedAntes.Length || !clause.WantedAntes[ante])
                        continue;

                    bool hasShop = HasShopSlots(clause.WantedShopSlots);
                    bool hasPack = HasPackSlots(clause.WantedPackSlots);
                    bool useDefaults = !hasShop && !hasPack;

                    if (hasShop || useDefaults)
                    {
                        int clauseShopMax = hasShop
                            ? FindMaxSlotIndex(clause.WantedShopSlots) + 1
                            : MotelyJsonScoring.GetDefaultShopSlotsForAnte(ante);
                        maxShopSlots = Math.Max(maxShopSlots, clauseShopMax);
                    }
                    if (hasPack || useDefaults)
                    {
                        // Packs don't have pre-calculated max (only 6 slots max), so calculate it
                        int clausePackMax = hasPack
                            ? (clause.MaxPackSlot ?? 5) + 1
                            : MotelyJsonScoring.GetDefaultPackSlotsForAnte(ante);
                        maxPackSlots = Math.Max(maxPackSlots, clausePackMax);
                    }
                }

                // Create streams ONCE for this ante
                var shopJokerStream =
                    maxShopSlots > 0 ? ctx.CreateShopJokerStreamNew(ante) : default;
                var packStream =
                    maxPackSlots > 0
                        ? ctx.CreateBoosterPackStream(
                            ante,
                            isCached: false,
                            generatedFirstPack: ante > 1
                        )
                        : default;
                var buffoonStream =
                    maxPackSlots > 0 ? ctx.CreateBuffoonPackJokerStream(ante) : default;

                // Walk shops once, check all clauses
                for (int slot = 0; slot < maxShopSlots; slot++)
                {
                    var jokerItem = shopJokerStream.GetNext(ref ctx);

                    // Check if it's an actual joker
                    var excludedValue = Vector256.Create((int)MotelyItemType.JokerExcludedByStream);
                    var isNotExcluded = ~Vector256.Equals(jokerItem.Value, excludedValue);
                    VectorMask isActualJoker = isNotExcluded;

                    if (!isActualJoker.IsAllFalse())
                    {
                        // Check this joker against ALL clauses
                        for (int clauseIndex = 0; clauseIndex < Clauses.Count; clauseIndex++)
                        {
                            var clause = Clauses[clauseIndex];
                            if (ante >= clause.WantedAntes.Length || !clause.WantedAntes[ante])
                                continue;

                            bool hasShop = HasShopSlots(clause.WantedShopSlots);
                            bool useDefaults = !hasShop && !HasPackSlots(clause.WantedPackSlots);

                            // Does this clause want this shop slot?
                            if (hasShop || useDefaults)
                            {
                                bool wantsSlot = !hasShop || clause.WantedShopSlots[slot];
                                if (wantsSlot)
                                {
                                    VectorMask matches = CheckJokerMatchesClause(jokerItem, clause);
                                    clauseMasks[clauseIndex] |= (isActualJoker & matches);
                                }
                            }
                        }
                    }
                }

                // Walk packs once, check all clauses
                if (maxPackSlots > 0)
                {
                    for (int packSlot = 0; packSlot < maxPackSlots; packSlot++)
                    {
                        var pack = ctx.GetNextBoosterPack(ref packStream);
                        VectorMask isBuffoonPack = VectorEnum256.Equals(
                            pack.GetPackType(),
                            MotelyBoosterPackType.Buffoon
                        );

                        if (!isBuffoonPack.IsAllFalse())
                        {
                            int maxPackSize = 5;
                            var packContents = ctx.GetNextBuffoonPackContents(
                                ref buffoonStream,
                                maxPackSize
                            );

                            // Check each joker in pack against ALL clauses
                            for (
                                int packJokerIndex = 0;
                                packJokerIndex < maxPackSize;
                                packJokerIndex++
                            )
                            {
                                var joker = packContents[packJokerIndex];

                                for (
                                    int clauseIndex = 0;
                                    clauseIndex < Clauses.Count;
                                    clauseIndex++
                                )
                                {
                                    var clause = Clauses[clauseIndex];
                                    if (
                                        ante >= clause.WantedAntes.Length
                                        || !clause.WantedAntes[ante]
                                    )
                                        continue;

                                    bool hasPack = HasPackSlots(clause.WantedPackSlots);
                                    bool useDefaults =
                                        !HasShopSlots(clause.WantedShopSlots) && !hasPack;

                                    if (hasPack || useDefaults)
                                    {
                                        bool wantsSlot =
                                            !hasPack || clause.WantedPackSlots[packSlot];
                                        if (wantsSlot)
                                        {
                                            VectorMask matches = CheckJokerMatchesClause(
                                                joker,
                                                clause
                                            );
                                            clauseMasks[clauseIndex] |= (isBuffoonPack & matches);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Check Judgement/Tag sources for each clause
                for (int clauseIndex = 0; clauseIndex < Clauses.Count; clauseIndex++)
                {
                    var clause = Clauses[clauseIndex];
                    if (ante >= clause.WantedAntes.Length || !clause.WantedAntes[ante])
                        continue;

                    if (clause.Sources == null)
                        continue;

                    // Check Judgement tarot joker sources
                    if (clause.Sources.Judgement != null && clause.Sources.Judgement.Length > 0)
                    {
                        var judgementStream = ctx.CreateJudgementJokerStream(ante);
                        foreach (var rollIndex in clause.Sources.Judgement)
                        {
                            if (rollIndex < 0)
                                continue;

                            // Advance stream to the desired roll index
                            for (int r = 0; r <= rollIndex; r++)
                            {
                                var jokerItem = ctx.GetNextJoker(ref judgementStream);

                                if (r == rollIndex)
                                {
                                    // Check if this is an actual joker (not excluded)
                                    var excludedValue = Vector256.Create((int)MotelyItemType.JokerExcludedByStream);
                                    var isNotExcluded = ~Vector256.Equals(jokerItem.Value, excludedValue);
                                    VectorMask isActualJoker = isNotExcluded;

                                    if (!isActualJoker.IsAllFalse())
                                    {
                                        VectorMask matches = CheckJokerMatchesClause(jokerItem, clause);
                                        clauseMasks[clauseIndex] |= (isActualJoker & matches);
                                    }
                                }
                            }
                        }
                    }

                    // Check Rare Tag joker sources
                    if (clause.Sources.RareTag != null && clause.Sources.RareTag.Length > 0)
                    {
                        var rareTagStream = ctx.CreateRareTagJokerStream(ante);
                        foreach (var rollIndex in clause.Sources.RareTag)
                        {
                            if (rollIndex < 0)
                                continue;

                            // Advance stream to the desired roll index
                            for (int r = 0; r <= rollIndex; r++)
                            {
                                var jokerItem = ctx.GetNextJoker(ref rareTagStream);

                                if (r == rollIndex)
                                {
                                    // Check if this is an actual joker (not excluded)
                                    var excludedValue = Vector256.Create((int)MotelyItemType.JokerExcludedByStream);
                                    var isNotExcluded = ~Vector256.Equals(jokerItem.Value, excludedValue);
                                    VectorMask isActualJoker = isNotExcluded;

                                    if (!isActualJoker.IsAllFalse())
                                    {
                                        VectorMask matches = CheckJokerMatchesClause(jokerItem, clause);
                                        clauseMasks[clauseIndex] |= (isActualJoker & matches);
                                    }
                                }
                            }
                        }
                    }

                    // Check Uncommon Tag joker sources
                    if (clause.Sources.UncommonTag != null && clause.Sources.UncommonTag.Length > 0)
                    {
                        var uncommonTagStream = ctx.CreateUncommonTagJokerStream(ante);
                        foreach (var rollIndex in clause.Sources.UncommonTag)
                        {
                            if (rollIndex < 0)
                                continue;

                            // Advance stream to the desired roll index
                            for (int r = 0; r <= rollIndex; r++)
                            {
                                var jokerItem = ctx.GetNextJoker(ref uncommonTagStream);

                                if (r == rollIndex)
                                {
                                    // Check if this is an actual joker (not excluded)
                                    var excludedValue = Vector256.Create((int)MotelyItemType.JokerExcludedByStream);
                                    var isNotExcluded = ~Vector256.Equals(jokerItem.Value, excludedValue);
                                    VectorMask isActualJoker = isNotExcluded;

                                    if (!isActualJoker.IsAllFalse())
                                    {
                                        VectorMask matches = CheckJokerMatchesClause(jokerItem, clause);
                                        clauseMasks[clauseIndex] |= (isActualJoker & matches);
                                    }
                                }
                            }
                        }
                    }
                }

                // Removed "early exit" - it ran at the END (ante >= maxAnte), not early!
                // The final check below already handles this correctly.
            }

            // All clauses must be satisfied (AND logic)
            // If any clause found nothing (NoBitsSet), the entire filter fails!
            var resultMask = VectorMask.AllBitsSet;
            for (int i = 0; i < clauseMasks.Length; i++)
            {
                DebugLogger.Log($"[JOKER VECTORIZED] Clause {i} mask: {clauseMasks[i].Value:X}");

                // FIX: If this clause found nothing across all antes, fail immediately
                if (clauseMasks[i].IsAllFalse())
                {
                    DebugLogger.Log(
                        $"[JOKER VECTORIZED] Clause {i} found no matches - failing all seeds"
                    );
                    return VectorMask.NoBitsSet;
                }

                resultMask &= clauseMasks[i];
                DebugLogger.Log(
                    $"[JOKER VECTORIZED] Result after clause {i}: {resultMask.Value:X}"
                );
                if (resultMask.IsAllFalse())
                    return VectorMask.NoBitsSet;
            }

            DebugLogger.Log($"[JOKER VECTORIZED] Final result mask: {resultMask.Value:X}");

            // ALWAYS verify with individual seed search to avoid SIMD bugs with pack streams
            // The vectorized search acts as a pre-filter, but we verify each passing seed individually
            if (resultMask.IsAllFalse())
            {
                return VectorMask.NoBitsSet;
            }

            // Copy struct fields to local variables for lambda
            var clauses = Clauses; // Copy _clauses to a local variable
            var minAnte = MinAnte;
            var maxAnte = MaxAnte;

            // FIX: Ensure we have clauses to check
            if (clauses == null || clauses.Count == 0)
            {
                DebugLogger.Log("[JOKER FILTER] ERROR: No clauses for individual verification!");
                return VectorMask.NoBitsSet; // NO seeds should pass without clauses!
            }

            return ctx.SearchIndividualSeeds(
                resultMask,
                (ref MotelySingleSearchContext singleCtx) =>
                {
                    // Use SHARED scoring functions to check Min threshold
                    var runState = new MotelyRunState();

                    foreach (var clause in clauses)
                    {
                        // Count total occurrences across ALL wanted antes
                        int totalCount = 0;
                        for (int ante = minAnte; ante <= maxAnte; ante++)
                        {
                            if (!clause.WantedAntes[ante])
                                continue;

                            // Use the EXACT same scoring function used in MotelyJsonScoring
                            int anteCount = MotelyJsonScoring.CountJokerOccurrences(
                                ref singleCtx,
                                clause,
                                ante,
                                ref runState,
                                earlyExit: false
                            );
                            totalCount += anteCount;
                        }

                        // Check Min threshold (if specified)
                        int minThreshold = clause.Min ?? 1; // Default to 1 if not specified
                        if (totalCount < minThreshold)
                            return false; // Doesn't meet minimum count
                    }

                    return true; // All clauses satisfied with Min thresholds
                }
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private VectorMask CheckShopVectorized(
            ref MotelyVectorSearchContext ctx,
            int ante,
            MotelyJsonJokerFilterClause clause,
            ref MotelyVectorShopItemStream shopStream,
            MotelyRunState runState
        )
        {
            VectorMask foundInShop = VectorMask.NoBitsSet;

            // Use min/max shop slot range (supports unlimited slots like 0-1000)
            int minSlot = clause.MinShopSlot ?? 0;
            int maxSlot =
                clause.MaxShopSlot
                ?? (
                    clause.MaxShopSlotsNeeded > 0 ? clause.MaxShopSlotsNeeded : (ante == 1 ? 4 : 6)
                );

            // Skip to minSlot by consuming unwanted items
            for (int skip = 0; skip < minSlot; skip++)
            {
                ctx.GetNextShopItem(ref shopStream);
            }

            // Check slots from minSlot to maxSlot (inclusive)
            for (int slot = minSlot; slot <= maxSlot; slot++)
            {
                // Get the shop item - the stream handles all rate calculations internally
                var item = ctx.GetNextShopItem(ref shopStream);

                // Check if this slot is in the bitmask
                // This check is now handled by the continue statement above
                {
                    DebugLogger.Log(
                        $"[JOKER VECTORIZED] Checking shop slot {slot}: item type category={item.TypeCategory}"
                    );

                    // Check if this slot has a joker
                    var isJoker = VectorEnum256.Equals(
                        item.TypeCategory,
                        MotelyItemTypeCategory.Joker
                    );

                    // Check if any lanes have jokers - ONLY CHECK VALID LANES!
                    uint jokerMask = 0;
                    for (int i = 0; i < 8; i++)
                        if (ctx.IsLaneValid(i) && isJoker[i] == -1)
                            jokerMask |= (1u << i);

                    if (jokerMask != 0) // Any lanes have jokers
                    {
                        DebugLogger.Log(
                            $"[JOKER VECTORIZED] Found joker at shop slot {slot}: {item.Type[0]}, expecting: {clause.JokerType}"
                        );
                        // Check if it matches our clause
                        VectorMask matches = CheckJokerMatchesClause(item, clause);
                        DebugLogger.Log($"[JOKER VECTORIZED] Matches mask={matches.Value:X}");
                        foundInShop |= matches;
                    }
                }
            }

            return foundInShop;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private VectorMask CheckShopJokerVectorizedNew(
            MotelyJsonJokerFilterClause clause,
            MotelyVectorSearchContext ctx,
            ref MotelyVectorShopJokerStream shopJokerStream,
            int ante
        )
        {
            VectorMask foundInShop = VectorMask.NoBitsSet;

            // Calculate max slots based on ante (dynamic per-ante calculation)
            int maxSlot;
            if (HasShopSlots(clause.WantedShopSlots))
            {
                // User specified slots - find the highest wanted slot
                maxSlot = 0;
                for (int i = 0; i < clause.WantedShopSlots.Length; i++)
                    if (clause.WantedShopSlots[i])
                        maxSlot = i;
                maxSlot++; // Convert index to count
            }
            else
            {
                // No slots specified - use ante-based defaults
                maxSlot = MotelyJsonScoring.GetDefaultShopSlotsForAnte(ante);
            }

            string jokerDbg =
                clause.JokerTypes?.Count > 0
                    ? string.Join("|", clause.JokerTypes)
                    : clause.JokerType?.ToString() ?? "?";
            DebugLogger.Log($"[SHOP CHECK] Ante {ante}, {jokerDbg}: checking {maxSlot} shop slots");

            // Check each shop slot using the self-contained stream
            for (int slot = 0; slot < maxSlot; slot++)
            {
                // ALWAYS get the next item to maintain stream synchronization
                var jokerItem = shopJokerStream.GetNext(ref ctx);

                // Only check/score if this slot is wanted (or if no specific slots wanted, check all)
                if (!HasShopSlots(clause.WantedShopSlots) || clause.WantedShopSlots[slot])
                {
                    // PURE VECTORIZED CHECK - no per-lane loops!
                    // Check if it's not JokerExcludedByStream using SIMD compare
                    var excludedValue = Vector256.Create((int)MotelyItemType.JokerExcludedByStream);
                    var isNotExcluded = ~Vector256.Equals(jokerItem.Value, excludedValue);
                    VectorMask isActualJoker = isNotExcluded;

                    if (!isActualJoker.IsAllFalse())
                    {
                        // Check if the joker matches our clause criteria
                        VectorMask matches = CheckJokerMatchesClause(jokerItem, clause);
                        foundInShop |= (isActualJoker & matches);
                    }
                }
            }

            return foundInShop;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private VectorMask CheckPackJokersVectorized(
            MotelyJsonJokerFilterClause clause,
            MotelyVectorSearchContext ctx,
            ref MotelyVectorBoosterPackStream packStream,
            ref MotelyVectorJokerStream buffoonStream,
            int ante
        )
        {
            VectorMask foundInPack = VectorMask.NoBitsSet;

            // Use config if provided, otherwise use default pack limits
            int actualPackLimit = clause.MaxPackSlot.HasValue
                ? clause.MaxPackSlot.Value + 1
                : MotelyJsonScoring.GetDefaultPackSlotsForAnte(ante);

            // Check enough packs to cover the slots, but never exceed actual pack limit
            bool hasSpecificSlots = HasPackSlots(clause.WantedPackSlots);
            int maxPacksToCheck = hasSpecificSlots ? actualPackLimit : actualPackLimit;

            for (int packIndex = 0; packIndex < maxPacksToCheck; packIndex++)
            {
                // Always get next pack to maintain stream sync
                var pack = ctx.GetNextBoosterPack(ref packStream);

                // Check if it's a Buffoon pack - we MUST consume jokers if it is!
                VectorMask isBuffoonPack = VectorEnum256.Equals(
                    pack.GetPackType(),
                    MotelyBoosterPackType.Buffoon
                );

                if (!isBuffoonPack.IsAllFalse())
                {
                    // FIXED: Handle different pack sizes properly per-lane
                    // Get pack sizes for each lane that has a Buffoon pack
                    var packSizes = pack.GetPackSize();

                    // Determine max pack size across all lanes to ensure we consume enough from stream
                    // Standard = 2, Jumbo = 3, Mega = 4-5 (let's use 5 to be safe)
                    int maxPackSize = 5; // Maximum possible pack size

                    // Get ALL jokers from the pack (up to max size) to keep stream in sync
                    var packContents = ctx.GetNextBuffoonPackContents(
                        ref buffoonStream,
                        maxPackSize
                    );

                    // Only SCORE if this pack slot is wanted
                    if (!hasSpecificSlots || clause.WantedPackSlots[packIndex])
                    {
                        // Check if it's a Mega pack if required
                        if (clause.Sources?.RequireMega == true)
                        {
                            VectorMask isMegaPack = VectorEnum256.Equals(
                                packSizes,
                                MotelyBoosterPackSize.Mega
                            );
                            isBuffoonPack &= isMegaPack;
                        }

                        // Check each joker in the pack for scoring
                        // We check all positions but only count valid ones based on actual pack size
                        for (int i = 0; i < maxPackSize; i++)
                        {
                            // Create mask for lanes where this card position is valid
                            // Standard (2): positions 0,1 valid
                            // Jumbo (3): positions 0,1,2 valid
                            // Mega (4-5): positions 0,1,2,3,4 valid
                            VectorMask isValidPosition = i switch
                            {
                                0 or 1 => VectorMask.AllBitsSet, // All packs have at least 2 cards
                                2 => ~VectorEnum256.Equals(packSizes, MotelyBoosterPackSize.Normal),
                                3 or 4 => VectorEnum256.Equals(
                                    packSizes,
                                    MotelyBoosterPackSize.Mega
                                ),
                                _ => VectorMask.NoBitsSet,
                            };

                            var jokerItem = packContents[i];
                            VectorMask matches = CheckJokerMatchesClause(jokerItem, clause);
                            foundInPack |= (isBuffoonPack & isValidPosition & matches);
                        }
                    }
                }
            }

            return foundInPack;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private VectorMask CheckJokerMatchesClause(
            MotelyItemVector item,
            MotelyJsonJokerFilterClause clause
        )
        {
            VectorMask matches = VectorMask.AllBitsSet;

            // Check type if specified - PURE SIMD, no loops over lanes!
            if (clause.JokerTypes != null && clause.JokerTypes.Count > 0)
            {
                VectorMask typeMatch = VectorMask.NoBitsSet;
                foreach (var jokerType in clause.JokerTypes)
                {
                    var targetType = (MotelyItemType)(
                        (int)MotelyItemTypeCategory.Joker | (int)jokerType
                    );
                    typeMatch |= VectorEnum256.Equals(item.Type, targetType);
                }
                matches &= typeMatch;
            }
            else if (clause.JokerType != null)
            {
                var targetType = (MotelyItemType)(
                    (int)MotelyItemTypeCategory.Joker | (int)clause.JokerType
                );
                matches &= VectorEnum256.Equals(item.Type, targetType);
            }
            else
            {
                // Match any joker
                matches &= VectorEnum256.Equals(item.TypeCategory, MotelyItemTypeCategory.Joker);
            }

            // Check edition if specified - PURE SIMD!
            if (clause.EditionEnum.HasValue)
            {
                VectorMask editionMatches = VectorEnum256.Equals(
                    item.Edition,
                    clause.EditionEnum.Value
                );
                DebugLogger.Log(
                    $"[JOKER EDITION CHECK] Required: {clause.EditionEnum.Value}, Item editions: {item.Edition[0]},{item.Edition[1]},{item.Edition[2]},{item.Edition[3]},{item.Edition[4]},{item.Edition[5]},{item.Edition[6]},{item.Edition[7]}"
                );
                DebugLogger.Log(
                    $"[JOKER EDITION CHECK] Edition matches mask: {editionMatches.Value:X}"
                );
                matches &= editionMatches;
            }

            return matches;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetMaxShopSlot(ulong bitmask, int ante)
        {
            if (bitmask == 0)
                return ante == 1 ? 4 : 6; // Default 4/6 concept

            // Find highest bit + 1, but ensure minimum of 6 for ante 2+ to handle extended slots
            int maxSpecified = 64 - System.Numerics.BitOperations.LeadingZeroCount(bitmask);
            return ante == 1 ? Math.Max(4, maxSpecified) : Math.Max(6, maxSpecified);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool CheckJokerTypeMatch(
            MotelyJoker joker,
            MotelyJsonJokerFilterClause clause
        )
        {
            if (!clause.IsWildcard)
            {
                if (clause.JokerTypes?.Count > 0)
                {
                    return clause.JokerTypes.Contains(joker);
                }
                else
                {
                    return clause.JokerType.HasValue && joker == clause.JokerType.Value;
                }
            }
            else
            {
                return CheckWildcardMatch(joker, clause.WildcardEnum);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool CheckWildcardMatch(
            MotelyJoker joker,
            MotelyJsonConfigWildcards? wildcard
        )
        {
            if (!wildcard.HasValue)
                return false;
            if (wildcard == MotelyJsonConfigWildcards.AnyJoker)
                return true;

            var rarity = (MotelyJokerRarity)((int)joker & Motely.JokerRarityMask);
            return wildcard switch
            {
                MotelyJsonConfigWildcards.AnyCommon => rarity == MotelyJokerRarity.Common,
                MotelyJsonConfigWildcards.AnyUncommon => rarity == MotelyJokerRarity.Uncommon,
                MotelyJsonConfigWildcards.AnyRare => rarity == MotelyJokerRarity.Rare,
                _ => false,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool CheckShopJokersSingleStatic(
            ref MotelySingleSearchContext ctx,
            MotelyJsonJokerFilterClause clause,
            int ante,
            ref MotelySingleShopItemStream shopStream
        )
        {
            DebugLogger.Log($"[SHOP CHECK] Looking for {clause.JokerType} in ante {ante}");

            // Determine how many slots to check - use config if provided
            int maxSlot = 0;
            if (clause.MaxShopSlot.HasValue)
            {
                // Use configured max shop slot
                maxSlot = clause.MaxShopSlot.Value + 1;
                DebugLogger.Log(
                    $"[SHOP CHECK] Using configured MaxShopSlot, checking {maxSlot} slots"
                );
            }
            else if (!HasShopSlots(clause.WantedShopSlots))
            {
                // No specific slots wanted - use ante-based defaults (pifreak's rules)
                maxSlot = MotelyJsonScoring.GetDefaultShopSlotsForAnte(ante);
                DebugLogger.Log(
                    $"[SHOP CHECK] No specific slots wanted, checking default {maxSlot} slots for ante {ante}"
                );
            }
            else
            {
                // Has specific shop slots - find the max
                for (int i = clause.WantedShopSlots.Length - 1; i >= 0; i--)
                {
                    if (clause.WantedShopSlots[i])
                    {
                        maxSlot = i + 1;
                        break;
                    }
                }
            }

            for (int slot = 0; slot < maxSlot; slot++)
            {
                var item = ctx.GetNextShopItem(ref shopStream);

                DebugLogger.Log(
                    $"[SHOP CHECK] Slot {slot}: {item.Type} (wanted: {(HasShopSlots(clause.WantedShopSlots) ? clause.WantedShopSlots[slot] : "all")})"
                );

                // Check if this slot is wanted (or if no specific slots wanted, check all)
                if (!HasShopSlots(clause.WantedShopSlots) || clause.WantedShopSlots[slot])
                {
                    if (item.TypeCategory == MotelyItemTypeCategory.Joker)
                    {
                        DebugLogger.Log(
                            $"[SHOP CHECK] Found item {item.Type} in slot {slot}, looking for {clause.JokerType}"
                        );
                        bool matches = false;
                        if (!clause.IsWildcard)
                        {
                            if (clause.JokerTypes?.Count > 0)
                            {
                                // Multi-value: check if item matches any of the joker types
                                foreach (var jokerType in clause.JokerTypes)
                                {
                                    var targetType = (MotelyItemType)(
                                        (int)MotelyItemTypeCategory.Joker | (int)jokerType
                                    );
                                    if (item.Type == targetType)
                                    {
                                        matches = true;
                                        break;
                                    }
                                }
                            }
                            else if (clause.JokerType.HasValue)
                            {
                                // Single value: original logic
                                matches =
                                    item.Type
                                    == (MotelyItemType)(
                                        (int)MotelyItemTypeCategory.Joker
                                        | (int)clause.JokerType.Value
                                    );
                            }
                        }
                        else
                        {
                            matches = CheckWildcardMatch(
                                (MotelyJoker)item.Type,
                                clause.WildcardEnum
                            );
                        }

                        DebugLogger.Log(
                            $"[SHOP CHECK] Type match: {matches}, item.Type={(int)item.Type}"
                        );
                        DebugLogger.Log(
                            $"[SHOP CHECK] Cast (MotelyJoker)item.Type={(int)(MotelyJoker)item.Type}"
                        );

                        if (matches && CheckEditionAndStickersSingle(item, clause))
                        {
                            DebugLogger.Log(
                                $"[SHOP CHECK] MATCH! Found {item.Type} in slot {slot}"
                            );
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool CheckPackJokersSingleStatic(
            ref MotelySingleSearchContext ctx,
            MotelyJsonJokerFilterClause clause,
            int ante,
            ref MotelySingleBoosterPackStream packStream
        )
        {
            // Use config if provided, otherwise use default pack limits
            int maxPacks = clause.MaxPackSlot.HasValue
                ? clause.MaxPackSlot.Value + 1
                : (ante == 1 ? 4 : 6);
            var buffoonStream = ctx.CreateBuffoonPackJokerStream(ante);

            for (int packIndex = 0; packIndex < maxPacks; packIndex++)
            {
                var pack = ctx.GetNextBoosterPack(ref packStream);
                var packSize = pack.GetPackSize();

                if (pack.GetPackType() == MotelyBoosterPackType.Buffoon)
                {
                    var packContents = ctx.GetNextBuffoonPackContents(ref buffoonStream, packSize);

                    // Check if this pack slot is wanted
                    if (!HasPackSlots(clause.WantedPackSlots) || clause.WantedPackSlots[packIndex])
                    {
                        if (
                            clause.Sources?.RequireMega == true
                            && pack.GetPackSize() != MotelyBoosterPackSize.Mega
                        )
                            continue;

                        for (int i = 0; i < packContents.Length; i++)
                        {
                            var item = packContents[i];
                            var joker = (MotelyJoker)item.Type;
                            bool matches = false;
                            if (!clause.IsWildcard)
                            {
                                if (clause.JokerTypes?.Count > 0)
                                {
                                    // Multi-value: check if item matches any of the joker types
                                    foreach (var jokerType in clause.JokerTypes)
                                    {
                                        var targetType = (MotelyItemType)(
                                            (int)MotelyItemTypeCategory.Joker | (int)jokerType
                                        );
                                        if (item.Type == targetType)
                                        {
                                            matches = true;
                                            break;
                                        }
                                    }
                                }
                                else if (clause.JokerType.HasValue)
                                {
                                    // Single value: original logic
                                    matches =
                                        item.Type
                                        == (MotelyItemType)(
                                            (int)MotelyItemTypeCategory.Joker
                                            | (int)clause.JokerType.Value
                                        );
                                }
                            }
                            else
                            {
                                matches = CheckWildcardMatch(joker, clause.WildcardEnum);
                            }

                            if (matches && CheckEditionAndStickersSingle(item, clause))
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool CheckEditionAndStickersSingle(
            in MotelyItem item,
            MotelyJsonJokerFilterClause clause
        )
        {
            if (clause.EditionEnum.HasValue && item.Edition != clause.EditionEnum.Value)
                return false;

            if (clause.StickerEnums?.Count > 0)
            {
                foreach (var sticker in clause.StickerEnums)
                {
                    var hasSticker = sticker switch
                    {
                        MotelyJokerSticker.Eternal => item.IsEternal,
                        MotelyJokerSticker.Perishable => item.IsPerishable,
                        MotelyJokerSticker.Rental => item.IsRental,
                        _ => true,
                    };
                    if (!hasSticker)
                        return false;
                }
            }

            return true;
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
        private static int FindMaxSlotIndex(bool[] slots)
        {
            for (int i = slots.Length - 1; i >= 0; i--)
                if (slots[i])
                    return i;
            return -1;
        }
    }
}
