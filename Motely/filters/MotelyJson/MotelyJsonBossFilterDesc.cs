using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Motely.Filters;

namespace Motely.Filters;

/// <summary>
/// Filters seeds based on boss blind criteria from JSON configuration.
/// </summary>
public struct MotelyJsonBossFilterDesc(MotelyJsonBossFilterCriteria criteria)
    : IMotelySeedFilterDesc<MotelyJsonBossFilterDesc.MotelyJsonBossFilter>
{
    private readonly MotelyJsonBossFilterCriteria _criteria = criteria;

    public MotelyJsonBossFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        // Use pre-calculated values from criteria
        return new MotelyJsonBossFilter(_criteria.Clauses, _criteria.MinAnte, _criteria.MaxAnte);
    }

    public struct MotelyJsonBossFilter(
        List<MotelyJsonConfig.MotleyJsonFilterClause> clauses,
        int minAnte,
        int maxAnte
    ) : IMotelySeedFilter
    {
        private readonly List<MotelyJsonConfig.MotleyJsonFilterClause> _clauses = clauses;
        private readonly int _minAnte = minAnte;
        private readonly int _maxAnte = maxAnte;

        [MethodImpl(
            MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization
        )]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            if (_clauses == null || _clauses.Count == 0)
                return VectorMask.AllBitsSet;

            // Copy struct members to locals to avoid CS1673
            var clauses = _clauses;
            var maxAnte = _maxAnte;

            // USE THE SHARED FUNCTION - same logic as scoring!
            return ctx.SearchIndividualSeeds(
                (ref MotelySingleSearchContext singleCtx) =>
                {
                    var state = new MotelyRunState();

                    // Initialize boss cache - CheckBossSingle requires it!
                    // Game starts at Ante 1, so bosses are generated from ante 1 onwards
                    if (maxAnte > 0)
                    {
                        var cachedBosses = new MotelyBossBlind[maxAnte + 1];
                        var bossStream = singleCtx.CreateBossStream();
                        for (int ante = 1; ante <= maxAnte; ante++)
                        {
                            cachedBosses[ante] = singleCtx.GetBossForAnte(
                                ref bossStream,
                                ante,
                                ref state
                            );
                        }
                        state.CachedBosses = cachedBosses;
                    }

                    // Check all clauses using the SAME shared function used in scoring
                    foreach (var clause in clauses)
                    {
                        // Count total occurrences across ALL wanted antes
                        int totalCount = 0;
                        foreach (var ante in clause.EffectiveAntes)
                        {
                            if (
                                MotelyJsonScoring.CheckBossSingle(
                                    ref singleCtx,
                                    clause,
                                    ante,
                                    ref state
                                )
                            )
                                totalCount++;
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
    }
}
