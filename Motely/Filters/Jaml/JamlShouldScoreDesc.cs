using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Motely.Filters.Jaml;

public struct JamlShouldScoreDesc
    : IMotelySeedScoreDesc<JamlShouldScoreDesc.JamlShouldScoreProvider>
{
    private readonly IJamlClause[] _mustClauses;
    private readonly IJamlClause[] _shouldClauses;
    private readonly IJamlClause[] _mustNotClauses;
    private readonly Action<string>? _seedMatchCallback;
    private readonly int _minimumTotalScore;
    private readonly bool _skipMustReeval;

    public JamlShouldScoreDesc(
        IJamlClause[] mustClauses,
        IJamlClause[] shouldClauses,
        Action<string>? seedMatchCallback = null,
        int minimumTotalScore = 0,
        IJamlClause[]? mustNotClauses = null
    )
    {
        _mustClauses = mustClauses;
        _shouldClauses = shouldClauses;
        _mustNotClauses = mustNotClauses ?? [];
        _seedMatchCallback = seedMatchCallback;
        _minimumTotalScore = minimumTotalScore;
        _skipMustReeval = JamlScoring.CanSkipMustReeval(mustClauses);
    }

    public JamlShouldScoreProvider CreateScoreProvider(ref MotelyFilterCreationContext ctx) =>
        new(
            _mustClauses,
            _shouldClauses,
            _seedMatchCallback ?? ctx.SeedMatchCallback,
            _minimumTotalScore,
            _skipMustReeval,
            _mustNotClauses
        );

    public struct JamlShouldScoreProvider : IMotelySeedScoreProvider
    {
        private readonly IJamlClause[] _mustClauses;
        private readonly IJamlClause[] _shouldClauses;
        private readonly IJamlClause[] _mustNotClauses;
        private readonly IJamlClause[] _prepareClauses;
        private readonly Action<string>? _seedMatchCallback;
        private readonly int _minimumTotalScore;
        private readonly bool _skipMustReeval;

        public JamlShouldScoreProvider(
            IJamlClause[] mustClauses,
            IJamlClause[] shouldClauses,
            Action<string>? seedMatchCallback,
            int minimumTotalScore = 0,
            bool skipMustReeval = false,
            IJamlClause[]? mustNotClauses = null
        )
        {
            mustNotClauses ??= [];
            Debug.Assert(
                mustClauses.Length + shouldClauses.Length + mustNotClauses.Length > 0,
                "Scoring pass requires at least one must, should or mustNot clause."
            );
            Debug.Assert(
                shouldClauses.Length <= MotelyScoredSeedResult.MAX_TALLY_COUNT,
                $"Should clause count {shouldClauses.Length} exceeds MotelyScoredSeedResult.MAX_TALLY_COUNT ({MotelyScoredSeedResult.MAX_TALLY_COUNT}); fix JAML / builder before search."
            );

            _mustClauses = mustClauses;
            _shouldClauses = shouldClauses;
            _mustNotClauses = mustNotClauses;
            _prepareClauses = CombineForPrepareRunState(
                skipMustReeval ? [] : mustClauses,
                shouldClauses,
                mustNotClauses
            );
            _seedMatchCallback = seedMatchCallback;
            _minimumTotalScore = minimumTotalScore;
            _skipMustReeval = skipMustReeval;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe VectorMask Score(
            ref MotelyVectorSearchContext searchContext,
            MotelyScoredSeedResult[] buffer,
            VectorMask baseFilterMask,
            int scoreThreshold = 0
        )
        {
            if (baseFilterMask.IsAllFalse())
                return VectorMask.NoBitsSet;

            var mustClauses = _mustClauses;
            var shouldClauses = _shouldClauses;
            var mustNotClauses = _mustNotClauses;
            var prepareClauses = _prepareClauses;
            var seedMatchCallback = _seedMatchCallback;
            int cutoff = Math.Max(_minimumTotalScore, scoreThreshold);
            bool skipMust = _skipMustReeval;

            if (skipMust && shouldClauses.Length == 0 && mustNotClauses.Length == 0 && cutoff <= 0)
            {
                return searchContext.SearchIndividualSeeds(
                    baseFilterMask,
                    (MotelySingleSearchContext singleCtx) =>
                    {
                        ref var tally = ref buffer[singleCtx.VectorLane];
                        tally.Reset(string.Empty);
                        tally.Score = 0;
                        char* seedPtr = stackalloc char[MotelyGlobals.MaxSeedLength];
                        int seedLength = singleCtx.GetSeed(seedPtr);
                        string seedStr = new string(seedPtr, 0, seedLength);
                        tally.Seed = seedStr;
                        seedMatchCallback?.Invoke(seedStr);
                        return 1;
                    }
                );
            }

            return searchContext.SearchIndividualSeeds(
                baseFilterMask,
                (MotelySingleSearchContext singleCtx) =>
                {
                    var runState = new MotelyRunState();
                    if (prepareClauses.Length > 0)
                        JamlScoring.PrepareRunState(ref singleCtx, prepareClauses, runState);

                    int totalScore = 0;
                    ref var tally = ref buffer[singleCtx.VectorLane];
                    tally.Reset(string.Empty);

                    if (!skipMust)
                    {
                        for (int i = 0; i < mustClauses.Length; i++)
                        {
                            int raw = JamlScoring.CountRawOccurrences(
                                ref singleCtx,
                                mustClauses[i],
                                runState
                            );

                            if (!JamlScoring.MeetsOccurrenceBounds(raw, mustClauses[i]))
                                return 0;
                        }
                    }

                    for (int i = 0; i < mustNotClauses.Length; i++)
                    {
                        int raw = JamlScoring.CountRawOccurrences(
                            ref singleCtx,
                            mustNotClauses[i],
                            runState
                        );

                        if (JamlScoring.MeetsOccurrenceBounds(raw, mustNotClauses[i]))
                            return 0;
                    }

                    for (int i = 0; i < shouldClauses.Length; i++)
                    {
                        int raw = JamlScoring.CountRawOccurrences(
                            ref singleCtx,
                            shouldClauses[i],
                            runState
                        );
                        tally.AddTally(raw);

                        if (shouldClauses[i] is not LogicClause && raw < shouldClauses[i].Min)
                            continue;
                        int weighted = JamlScoring.CountOccurrences(
                            ref singleCtx,
                            shouldClauses[i],
                            runState
                        );
                        totalScore += weighted * shouldClauses[i].Score;
                    }

                    tally.Score = totalScore;

                    bool passedCutoff = totalScore >= cutoff;
                    if (passedCutoff)
                    {
                        char* seedPtr = stackalloc char[MotelyGlobals.MaxSeedLength];
                        int seedLength = singleCtx.GetSeed(seedPtr);
                        string seedStr = new string(seedPtr, 0, seedLength);
                        tally.Seed = seedStr;
                        seedMatchCallback?.Invoke(seedStr);
                    }

                    return (passedCutoff) ? 1 : 0;
                }
            );
        }

        private static IJamlClause[] CombineForPrepareRunState(
            IJamlClause[] mustClauses,
            IJamlClause[] shouldClauses,
            IJamlClause[] mustNotClauses
        )
        {
            if (mustClauses.Length + mustNotClauses.Length == 0)
                return shouldClauses;
            if (shouldClauses.Length + mustNotClauses.Length == 0)
                return mustClauses;

            var combined = new IJamlClause[
                mustClauses.Length + shouldClauses.Length + mustNotClauses.Length
            ];
            mustClauses.CopyTo(combined, 0);
            shouldClauses.CopyTo(combined, mustClauses.Length);
            mustNotClauses.CopyTo(combined, mustClauses.Length + shouldClauses.Length);
            return combined;
        }
    }
}
