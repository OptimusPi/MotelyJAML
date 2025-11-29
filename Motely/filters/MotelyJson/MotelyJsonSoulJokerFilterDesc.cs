using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using Motely.Filters;

namespace Motely.Filters;

/// <summary>
/// Fully vectorized soul joker filter using two-stage approach:
/// 1. Pre-filter: Fast vectorized joker matching
/// 2. Verify: Vectorized Soul card verification in packs
/// </summary>
public readonly struct MotelyJsonSoulJokerFilterDesc(MotelyJsonSoulJokerFilterCriteria criteria)
    : IMotelySeedFilterDesc<MotelyJsonSoulJokerFilterDesc.MotelyJsonSoulJokerFilter>
{
    private readonly MotelyJsonSoulJokerFilterCriteria _criteria = criteria;

    public MotelyJsonSoulJokerFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        // Use pre-calculated values from criteria
        int minAnte = _criteria.MinAnte;
        int maxAnte = _criteria.MaxAnte;

        // Cache all streams we'll need for BOTH vectorized and individual checks
        for (int ante = minAnte; ante <= maxAnte; ante++)
        {
            // For vectorized pre-filter
            ctx.CacheSoulJokerStream(ante);
        }

        return new MotelyJsonSoulJokerFilter(
            _criteria.Clauses,
            minAnte,
            maxAnte,
            _criteria.MaxPackSlotsPerAnte
        );
    }

    public struct MotelyJsonSoulJokerFilter : IMotelySeedFilter
    {
        private readonly List<MotelyJsonSoulJokerFilterClause> _clauses;
        private readonly int _minAnte;
        private readonly int _maxAnte;
        private readonly Dictionary<int, int> _maxPackSlotsPerAnte;
        private readonly int[] _lastAnteForClause;

        public MotelyJsonSoulJokerFilter(
            List<MotelyJsonSoulJokerFilterClause> clauses,
            int minAnte,
            int maxAnte,
            Dictionary<int, int> maxPackSlotsPerAnte
        )
        {
            _clauses = clauses;
            _minAnte = minAnte;
            _maxAnte = maxAnte;
            _maxPackSlotsPerAnte = maxPackSlotsPerAnte;

            // Pre-compute the last ante for each clause ONCE
            _lastAnteForClause = new int[clauses.Count];
            for (int i = 0; i < clauses.Count; i++)
            {
                _lastAnteForClause[i] = -1;
                for (int a = maxAnte; a >= minAnte; a--)
                {
                    if (a < clauses[i].WantedAntes.Length && clauses[i].WantedAntes[a])
                    {
                        _lastAnteForClause[i] = a;
                        break;
                    }
                }
            }
        }

        [MethodImpl(
            MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization
        )]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            Debug.Assert(
                _clauses != null && _clauses.Count > 0,
                "MotelyJsonSoulJokerFilter called with null or empty clauses"
            );

            // STAGE 1: Vectorized pre-filter - just detect Soul cards
            // We can't properly track soul joker sequences in vectorized mode
            // because different seeds have different Soul patterns
            VectorMask anySoulFound = VectorMask.NoBitsSet;

            // Walk through ALL antes looking for Soul cards
            for (int ante = _minAnte; ante <= _maxAnte; ante++)
            {
                // Check if any of the clauses want this ante
                bool anteWanted = false;
                for (int i = 0; i < _clauses.Count; i++)
                {
                    if (ante < _clauses[i].WantedAntes.Length && _clauses[i].WantedAntes[ante])
                    {
                        anteWanted = true;
                        break;
                    }
                }

                if (!anteWanted)
                    continue;

                // Create pack streams for this ante
                var boosterPackStream = ctx.CreateBoosterPackStream(ante, ante > 1, false);
                var tarotStream = ctx.CreateArcanaPackTarotStream(ante, false);
                var spectralStream = ctx.CreateSpectralPackSpectralStream(ante, false);
                bool tarotStreamInit = false,
                    spectralStreamInit = false;

                int maxPackSlot = _maxPackSlotsPerAnte.ContainsKey(ante)
                    ? _maxPackSlotsPerAnte[ante]
                    : 3;

                // Walk through each pack slot
                for (int packIndex = 0; packIndex < maxPackSlot; packIndex++)
                {
                    var pack = ctx.GetNextBoosterPack(ref boosterPackStream);

                    // Check if pack is Arcana type
                    VectorMask isArcana = VectorEnum256.Equals(
                        pack.GetPackType(),
                        MotelyBoosterPackType.Arcana
                    );
                    if (!isArcana.IsAllFalse())
                    {
                        if (!tarotStreamInit)
                        {
                            tarotStreamInit = true;
                            tarotStream = ctx.CreateArcanaPackTarotStream(ante, true);
                        }
                        var soulInArcana = ctx.GetNextArcanaPackHasTheSoul(
                            ref tarotStream,
                            MotelyBoosterPackSize.Mega
                        );
                        anySoulFound |= (isArcana & soulInArcana);
                    }

                    // Check if pack is Spectral type
                    VectorMask isSpectral = VectorEnum256.Equals(
                        pack.GetPackType(),
                        MotelyBoosterPackType.Spectral
                    );
                    if (!isSpectral.IsAllFalse())
                    {
                        if (!spectralStreamInit)
                        {
                            spectralStreamInit = true;
                            spectralStream = ctx.CreateSpectralPackSpectralStream(ante, true);
                        }
                        var soulInSpectral = ctx.GetNextSpectralPackHasTheSoul(
                            ref spectralStream,
                            MotelyBoosterPackSize.Mega
                        );
                        anySoulFound |= (isSpectral & soulInSpectral);
                    }
                }
            }

            // Pass seeds with Soul cards to individual validation
            // The individual validation will check the specific joker requirements
            var clauses = _clauses;
            int minAnte = _minAnte;
            int maxAnte = _maxAnte;
            var maxPackSlotsPerAnte = _maxPackSlotsPerAnte;
            var lastAnteForClause = _lastAnteForClause; // Use pre-computed values
            return ctx.SearchIndividualSeeds(
                anySoulFound,
                (ref MotelySingleSearchContext singleCtx) =>
                {
                    // Track counts for each clause
                    int[] clauseCounts = new int[clauses.Count];

                    // CRITICAL: Soul joker has TWO components with different ante-dependency behavior:
                    // 1. Face/Type (Perkeo, Canio, etc.) - NOT ante-dependent (same PRNG sequence for entire seed)
                    // 2. Edition (Negative, Polychrome, etc.) - IS ante-dependent (different per ante)
                    //
                    // Solution: Use TWO separate streams:
                    // - globalSoulFaceStream: Created once, reused across ALL antes, checks ONLY face/type
                    // - soulEditionStream: Created fresh per ante, checks ONLY edition
                    var globalSoulFaceStream = singleCtx.CreateSoulJokerStream(1);

                    // Walk through ALL antes sequentially
                    for (int ante = minAnte; ante <= maxAnte; ante++)
                    {
                        // Create per-ante edition stream for edition checks (ante-dependent)
                        var soulEditionStream = singleCtx.CreateSoulJokerStream(ante);

                        var boosterPackStream = singleCtx.CreateBoosterPackStream(
                            ante,
                            ante > 1,
                            false
                        );
                        var tarotStream = singleCtx.CreateArcanaPackTarotStream(ante, false);
                        var spectralStream = singleCtx.CreateSpectralPackSpectralStream(
                            ante,
                            false
                        );
                        bool tarotStreamInit = false,
                            spectralStreamInit = false;

                        int maxPackSlot = maxPackSlotsPerAnte.ContainsKey(ante)
                            ? maxPackSlotsPerAnte[ante]
                            : 3;
                        for (int packIndex = 0; packIndex < maxPackSlot; packIndex++)
                        {
                            var pack = singleCtx.GetNextBoosterPack(ref boosterPackStream);

                            bool hasSoul = false;
                            if (pack.GetPackType() == MotelyBoosterPackType.Arcana)
                            {
                                if (!tarotStreamInit)
                                {
                                    tarotStreamInit = true;
                                    tarotStream = singleCtx.CreateArcanaPackTarotStream(ante, true);
                                }
                                hasSoul = singleCtx.GetNextArcanaPackHasTheSoul(
                                    ref tarotStream,
                                    pack.GetPackSize()
                                );
                            }
                            else if (pack.GetPackType() == MotelyBoosterPackType.Spectral)
                            {
                                if (!spectralStreamInit)
                                {
                                    spectralStreamInit = true;
                                    spectralStream = singleCtx.CreateSpectralPackSpectralStream(
                                        ante,
                                        true
                                    );
                                }
                                hasSoul = singleCtx.GetNextSpectralPackHasTheSoul(
                                    ref spectralStream,
                                    pack.GetPackSize()
                                );
                            }

                            // If Soul found, get next joker from BOTH streams
                            if (hasSoul)
                            {
                                // Consume from BOTH streams:
                                // - Face stream for type matching (NOT ante-dependent)
                                // - Edition stream for edition matching (IS ante-dependent)
                                var soulJokerFace = singleCtx.GetNextJoker(
                                    ref globalSoulFaceStream
                                );
                                var soulJokerEdition = singleCtx.GetNextJoker(
                                    ref soulEditionStream
                                );

                                // Check this joker against ALL clauses
                                for (int clauseIdx = 0; clauseIdx < clauses.Count; clauseIdx++)
                                {
                                    var clause = clauses[clauseIdx];

                                    // Check if this ante is wanted
                                    if (
                                        ante >= clause.WantedAntes.Length
                                        || !clause.WantedAntes[ante]
                                    )
                                        continue;

                                    // Check if this pack slot is wanted
                                    if (
                                        clause.WantedPackSlots != null
                                        && clause.WantedPackSlots.Any(x => x)
                                    )
                                    {
                                        if (
                                            packIndex >= clause.WantedPackSlots.Length
                                            || !clause.WantedPackSlots[packIndex]
                                        )
                                            continue;
                                    }

                                    // Check mega requirement
                                    if (
                                        clause.RequireMega
                                        && pack.GetPackSize() != MotelyBoosterPackSize.Mega
                                    )
                                        continue;

                                    // Check joker type using FACE stream (not ante-dependent)
                                    bool typeMatches = true;
                                    if (!clause.IsWildcard)
                                    {
                                        if (
                                            clause.JokerTypes != null
                                            && clause.JokerTypes.Count > 0
                                        )
                                        {
                                            // Multiple types specified - match ANY of them (OR logic)
                                            typeMatches = false;
                                            foreach (var jokerType in clause.JokerTypes)
                                            {
                                                var expectedType = (MotelyItemType)(
                                                    (int)MotelyItemTypeCategory.Joker
                                                    | (int)jokerType
                                                );
                                                if (soulJokerFace.Type == expectedType)
                                                {
                                                    typeMatches = true;
                                                    break;
                                                }
                                            }
                                        }
                                        else if (clause.JokerType.HasValue)
                                        {
                                            // Single type specified
                                            var expectedType = (MotelyItemType)(
                                                (int)MotelyItemTypeCategory.Joker
                                                | (int)clause.JokerType.Value
                                            );
                                            typeMatches = (soulJokerFace.Type == expectedType);
                                        }
                                    }
                                    // If IsWildcard is true (e.g., "Any"), typeMatches stays true

                                    if (!typeMatches)
                                        continue;

                                    // Check edition using EDITION stream (ante-dependent)
                                    if (
                                        clause.EditionEnum.HasValue
                                        && soulJokerEdition.Edition != clause.EditionEnum.Value
                                    )
                                        continue;

                                    // This joker matches this clause!
                                    clauseCounts[clauseIdx]++;
                                }
                            }
                        }

                        // EARLY EXIT: Check if any clause is past its last ante and failed
                        for (int i = 0; i < clauses.Count; i++)
                        {
                            // If we're past this clause's last ante and it hasn't met minimum, fail
                            if (ante >= lastAnteForClause[i])
                            {
                                int minThreshold = clauses[i].Min ?? 1;
                                if (clauseCounts[i] < minThreshold)
                                {
                                    return false; // EARLY EXIT - this clause failed!
                                }
                            }
                        }
                    }

                    // Check if all clauses met their Min threshold
                    for (int i = 0; i < clauses.Count; i++)
                    {
                        int minThreshold = clauses[i].Min ?? 1;
                        if (clauseCounts[i] < minThreshold)
                            return false;
                    }

                    return true;
                }
            );
        }
    }
}
