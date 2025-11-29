using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Motely.Filters;

namespace Motely.Filters;

/// <summary>
/// ULTRA-FAST edition-only soul joker filter (Value="Any" + edition specified)
/// CRITICAL OPTIMIZATION: Skips ALL soul card detection in vectorized mode!
/// Only peeks edition stream (1-2 PRNG calls per ante) for instant early-exit
/// </summary>
public readonly struct MotelyJsonSoulJokerEditionOnlyFilterDesc(
    MotelyJsonSoulJokerFilterCriteria criteria
)
    : IMotelySeedFilterDesc<MotelyJsonSoulJokerEditionOnlyFilterDesc.MotelyJsonSoulJokerEditionOnlyFilter>
{
    private readonly MotelyJsonSoulJokerFilterCriteria _criteria = criteria;

    public MotelyJsonSoulJokerEditionOnlyFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        // Cache soul joker edition streams for all antes
        for (int ante = _criteria.MinAnte; ante <= _criteria.MaxAnte; ante++)
        {
            ctx.CacheSoulJokerStream(ante);
        }

        return new MotelyJsonSoulJokerEditionOnlyFilter(
            _criteria.Clauses,
            _criteria.MinAnte,
            _criteria.MaxAnte
        );
    }

    public struct MotelyJsonSoulJokerEditionOnlyFilter : IMotelySeedFilter
    {
        private readonly List<MotelyJsonSoulJokerFilterClause> _clauses;
        private readonly int _minAnte;
        private readonly int _maxAnte;

        public MotelyJsonSoulJokerEditionOnlyFilter(
            List<MotelyJsonSoulJokerFilterClause> clauses,
            int minAnte,
            int maxAnte
        )
        {
            _clauses = clauses;
            _minAnte = minAnte;
            _maxAnte = maxAnte;
        }

        [MethodImpl(
            MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization
        )]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            Debug.Assert(
                _clauses != null && _clauses.Count > 0,
                "Edition-only filter called with empty clauses"
            );

            // For edition-only checks, we DON'T need to detect soul cards!
            // We just check the edition stream for the specific ante(s) required
            // This is BLAZING FAST (1-2 PRNG calls per ante)

            VectorMask resultMask = VectorMask.AllBitsSet;

            // Process clauses in order (most restrictive first, already sorted in criteria)
            foreach (var clause in _clauses)
            {
                VectorMask clauseMatched = VectorMask.NoBitsSet;

                // Check each ante this clause wants
                for (int ante = _minAnte; ante <= _maxAnte; ante++)
                {
                    if (ante >= clause.WantedAntes.Length || !clause.WantedAntes[ante])
                        continue;

                    // Create edition stream for this ante
                    var editionStream = ctx.CreateSoulJokerStream(ante);

                    // Check first soul joker edition
                    clauseMatched |= VectorEnum256.Equals(
                        ctx.GetNextJoker(ref editionStream).Edition,
                        clause.EditionEnum!.Value
                    );

                    // Check second soul joker edition (in case ante has 2 soul jokers)
                    //clauseMatched |= VectorEnum256.Equals(ctx.GetNextJoker(ref editionStream).Edition, clause.EditionEnum!.Value);
                }

                // If clause requires Min count, handle in individual scoring
                // For vectorized mode, we just need to know if ANY match exists
                resultMask &= clauseMatched;

                // EARLY EXIT: If entire vector failed, no point checking other clauses
                if (resultMask.IsAllFalse())
                    return VectorMask.NoBitsSet;
            }

            // ALWAYS call individual checking to verify soul cards exist in packs!
            var clauses = _clauses;
            return ctx.SearchIndividualSeeds(
                resultMask,
                (ref MotelySingleSearchContext singleCtx) =>
                {
                    // Check if any clause has Min parameter - if so, we CANNOT early exit!
                    // We need to count ALL occurrences to verify minimum threshold
                    bool hasMinRequirement = false;
                    foreach (var clause in clauses)
                    {
                        if (clause.Min.HasValue && clause.Min.Value > 1)
                        {
                            hasMinRequirement = true;
                            break;
                        }
                    }

                    // Use earlyExit only if NO clause has a Min requirement
                    return MotelyJsonScoring.CheckSoulJokerForSeed(
                        clauses,
                        ref singleCtx,
                        earlyExit: !hasMinRequirement
                    );
                }
            );
        }
    }
}
