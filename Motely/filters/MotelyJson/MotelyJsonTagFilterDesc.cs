using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Motely.Filters;

namespace Motely.Filters;

/// <summary>
/// Filters seeds based on tag criteria from JSON configuration.
/// </summary>
public struct MotelyJsonTagFilterDesc(MotelyJsonTagFilterCriteria criteria)
    : IMotelySeedFilterDesc<MotelyJsonTagFilterDesc.MotelyJsonTagFilter>
{
    private readonly MotelyJsonTagFilterCriteria _criteria = criteria;

    public MotelyJsonTagFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        Debug.Assert(_criteria.Clauses != null, "Tag filter clauses should not be null");
        Debug.Assert(_criteria.Clauses.Count > 0, "Tag filter clauses should not be empty");

        // Tags don't use pack streams themselves, but we need to cache them
        // in case this is the base filter and subsequent filters need them
        if (_criteria.Clauses != null && _criteria.Clauses.Count > 0)
        {
            // Find all antes used by tag clauses
            var allAntes = new HashSet<int>();
            foreach (var clause in _criteria.Clauses)
            {
                if (clause.EffectiveAntes != null)
                {
                    foreach (var ante in clause.EffectiveAntes)
                    {
                        allAntes.Add(ante);
                    }
                }
            }

            // Cache pack streams for all antes to support chained filters
            // This prevents NullReferenceException when Tag is the base filter
            foreach (var ante in allAntes)
            {
                ctx.CacheBoosterPackStream(ante);
                ctx.CacheTagStream(ante); // Also cache tag stream for efficiency
            }
        }

        return new MotelyJsonTagFilter(_criteria.Clauses!, _criteria.MinAnte, _criteria.MaxAnte);
    }

    public struct MotelyJsonTagFilter : IMotelySeedFilter
    {
        private readonly List<MotelyJsonConfig.MotleyJsonFilterClause> _clauses;
        private readonly int _minAnte;
        private readonly int _maxAnte;

        public MotelyJsonTagFilter(
            List<MotelyJsonConfig.MotleyJsonFilterClause> clauses,
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
            DebugLogger.Log($"[TAG FILTER] Called with {_clauses?.Count ?? 0} clauses");

            if (_clauses == null || _clauses.Count == 0)
            {
                DebugLogger.Log("[TAG FILTER] No clauses - returning AllBitsSet");
                return VectorMask.AllBitsSet;
            }

            // Use pre-calculated ante range for maximum performance
            // Stack-allocated clause masks - accumulate results per clause across all antes
            Span<VectorMask> clauseMasks = stackalloc VectorMask[_clauses.Count];
            for (int i = 0; i < clauseMasks.Length; i++)
                clauseMasks[i] = VectorMask.NoBitsSet;

            // OPTIMIZED: Loop antes first (like joker filter), then clauses - ensures one stream per ante!
            for (int ante = _minAnte; ante <= _maxAnte; ante++)
            {
                // Use non-cached tag stream (working version)
                var tagStream = ctx.CreateTagStream(ante);
                var smallTag = ctx.GetNextTag(ref tagStream);
                var bigTag = ctx.GetNextTag(ref tagStream);

                // Log tag generation for specific seed
                if (DebugLogger.IsEnabled)
                {
                    DebugLogger.Log(
                        $"[TAG FILTER] Ante {ante}: smallTag={smallTag}, bigTag={bigTag}"
                    );
                }

                for (int clauseIndex = 0; clauseIndex < _clauses.Count; clauseIndex++)
                {
                    var clause = _clauses[clauseIndex];

                    // Skip if this ante isn't in clause's effective antes
                    // PERF: Avoid LINQ .Contains() in SIMD hotpath - use direct array check
                    if (clause.EffectiveAntes != null)
                    {
                        bool foundAnte = false;
                        for (int i = 0; i < clause.EffectiveAntes.Length; i++)
                        {
                            if (clause.EffectiveAntes[i] == ante)
                            {
                                foundAnte = true;
                                break;
                            }
                        }
                        if (!foundAnte)
                            continue;
                    }

                    if (
                        clause.TagEnum.HasValue
                        || (clause.TagEnums != null && clause.TagEnums.Count > 0)
                    )
                    {
                        VectorMask tagMatches;

                        // Handle multiple values (OR logic) or single value
                        if (clause.TagEnums != null && clause.TagEnums.Count > 0)
                        {
                            // Multi-value: any tag in the list matches (OR logic)
                            tagMatches = VectorMask.NoBitsSet;
                            foreach (var tagEnum in clause.TagEnums)
                            {
                                var singleTagMatches = clause.TagTypeEnum switch
                                {
                                    MotelyTagType.SmallBlind => VectorEnum256.Equals(
                                        smallTag,
                                        tagEnum
                                    ),
                                    MotelyTagType.BigBlind => VectorEnum256.Equals(bigTag, tagEnum),
                                    _ => VectorEnum256.Equals(smallTag, tagEnum)
                                        | VectorEnum256.Equals(bigTag, tagEnum),
                                };
                                tagMatches |= singleTagMatches;
                            }
                        }
                        else if (clause.TagEnum.HasValue)
                        {
                            // Single value: original logic
                            tagMatches = clause.TagTypeEnum switch
                            {
                                MotelyTagType.SmallBlind => VectorEnum256.Equals(
                                    smallTag,
                                    clause.TagEnum.Value
                                ),
                                MotelyTagType.BigBlind => VectorEnum256.Equals(
                                    bigTag,
                                    clause.TagEnum.Value
                                ),
                                _ => VectorEnum256.Equals(smallTag, clause.TagEnum.Value)
                                    | VectorEnum256.Equals(bigTag, clause.TagEnum.Value),
                            };
                        }
                        else
                        {
                            tagMatches = VectorMask.NoBitsSet;
                        }

                        // Accumulate results for this clause across all antes (OR logic)
                        clauseMasks[clauseIndex] |= tagMatches;
                    }
                }

                // OPTIMIZED: Early exit check after each ante (like joker filter)
                bool canEarlyExit = false;
                for (int i = 0; i < _clauses.Count; i++)
                {
                    var clause = _clauses[i];
                    // Check if this clause has any antes left to check
                    bool hasAntesRemaining = false;
                    if (clause.EffectiveAntes != null)
                    {
                        foreach (var futureAnte in clause.EffectiveAntes)
                        {
                            if (futureAnte > ante)
                            {
                                hasAntesRemaining = true;
                                break;
                            }
                        }
                    }

                    // If this clause has no matches and no antes left to check, we can exit
                    if (clauseMasks[i].IsAllFalse() && !hasAntesRemaining)
                    {
                        canEarlyExit = true;
                        break;
                    }
                }

                if (canEarlyExit)
                {
                    DebugLogger.Log("[TAG FILTER] Early exit - clause cannot be satisfied");
                    return VectorMask.NoBitsSet;
                }
            }

            // All clauses must be satisfied (AND logic)
            // If any clause found nothing (NoBitsSet), the entire filter fails!
            var resultMask = VectorMask.AllBitsSet;
            for (int i = 0; i < clauseMasks.Length; i++)
            {
                DebugLogger.Log($"[TAG FILTER] Clause {i} mask: {clauseMasks[i].Value:X}");

                // FIX: If this clause found nothing across all antes, fail immediately
                if (clauseMasks[i].IsAllFalse())
                {
                    DebugLogger.Log(
                        $"[TAG FILTER] Clause {i} found no matches - failing all seeds"
                    );
                    return VectorMask.NoBitsSet;
                }

                resultMask &= clauseMasks[i];
                DebugLogger.Log($"[TAG FILTER] Result after clause {i}: {resultMask.Value:X}");
                if (resultMask.IsAllFalse())
                {
                    DebugLogger.Log("[TAG FILTER] All false after AND - returning NoBitsSet");
                    return VectorMask.NoBitsSet;
                }
            }

            DebugLogger.Log($"[TAG FILTER] FULLY VECTORIZED result: {resultMask.Value:X}");

            // PHASE 2: Scalar verification with min checking (like Joker filter)
            // Ensures min thresholds are respected in MUST clauses
            if (resultMask.IsAllFalse())
            {
                return VectorMask.NoBitsSet;
            }

            // Copy struct fields to local variables for lambda (required for struct members)
            var clauses = _clauses;

            return ctx.SearchIndividualSeeds(
                resultMask,
                (ref MotelySingleSearchContext singleCtx) =>
                {
                    // Use SHARED scoring functions to check Min threshold
                    foreach (var clause in clauses)
                    {
                        // Count total occurrences across ALL wanted antes
                        int totalCount = 0;
                        foreach (var ante in clause.EffectiveAntes)
                        {
                            int anteCount = MotelyJsonScoring.CountTagOccurrences(
                                ref singleCtx,
                                clause,
                                ante
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
    }
}
