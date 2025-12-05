using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using Motely;

namespace Motely.Filters;

public unsafe struct MotelySeedScoreTally : IMotelySeedScore
{
    public int Score { get; set; } // Made mutable for easier scoring logic
    public string Seed { get; }

    private fixed int _tallyValues[1024];
    private int _tallyCount;

    public MotelySeedScoreTally(string seed, int score)
    {
        Seed = seed;
        Score = score;
        _tallyCount = 0;
    }

    public void AddTally(int value)
    {
        if (_tallyCount < 1024)
        {
            _tallyValues[_tallyCount++] = value;
        }
    }

    public int GetTally(int index)
    {
        // Return 0 for out-of-bounds indices (graceful degradation)
        if (index < 0 || index >= _tallyCount)
            return 0;
        return _tallyValues[index];
    }

    public int TallyCount => _tallyCount;

    public List<int> TallyColumns
    {
        get
        {
            var list = new List<int>(_tallyCount);
            for (int i = 0; i < _tallyCount; i++)
            {
                list.Add(_tallyValues[i]);
            }
            return list;
        }
    }
}

/// <summary>
/// Clean filter descriptor for MongoDB-style queries
/// </summary>
public struct MotelyJsonSeedScoreDesc(
    MotelyJsonConfig Config,
    int Cutoff,
    bool AutoCutoff,
    Action<MotelySeedScoreTally> OnResultFound
) : IMotelySeedScoreDesc<MotelyJsonSeedScoreDesc.MotelyJsonSeedScoreProvider>
{
    // Auto cutoff state
    private static int _learnedCutoff = 0;

    // Track seeds that passed filter (before cutoff check)
    private static long _seedsFiltered = 0;

    // Callback to return the score object to (the caller can print, send to a db, I don't care)
    private readonly Action<MotelySeedScoreTally> _onResultFound = OnResultFound;

    public MotelyJsonSeedScoreProvider CreateScoreProvider(ref MotelyFilterCreationContext ctx)
    {
        // Reset for new search
        // Auto-cutoff starts at 0, manual cutoff uses specified value
        _learnedCutoff = AutoCutoff ? 0 : Cutoff;
        _seedsFiltered = 0;
        return new MotelyJsonSeedScoreProvider(Config, Cutoff, AutoCutoff, _onResultFound);
    }

    public static long FilteredSeedCount => _seedsFiltered;

    public struct MotelyJsonSeedScoreProvider(
        MotelyJsonConfig Config,
        int Cutoff,
        bool AutoCutoff,
        Action<MotelySeedScoreTally> OnResultFound
    ) : IMotelySeedScoreProvider
    {
        public static bool IsCancelled;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Score(
            ref MotelyVectorSearchContext searchContext,
            MotelySeedScoreTally[] buffer,
            VectorMask baseFilterMask = default,
            int scoreThreshold = 0
        )
        {
            // Base filter already checked MUST clauses - we only score seeds that passed
            // If no seeds passed the base filter, exit early
            if (baseFilterMask.IsAllFalse())
                return VectorMask.NoBitsSet;

            if (IsCancelled)
                return VectorMask.NoBitsSet;

            // Copy fields to local variables to avoid struct closure issues
            var config = Config;
            var cutoff = scoreThreshold > 0 ? scoreThreshold : Cutoff;
            var autoCutoff = AutoCutoff;
            var onResultFound = OnResultFound;


            // Score individual seeds that passed the base filter
            // NOTE: Scoring is intentionally SCALAR - we don't need vectorized performance here
            // Track filtered count locally to batch Interlocked operation
            int localFiltered = 0;

            var resultMask = searchContext.SearchIndividualSeeds(
                baseFilterMask,
                (ref MotelySingleSearchContext singleCtx) =>
                {
                    var runState = new MotelyRunState();

                    // Activate all vouchers for scoring
                    if (config.MaxVoucherAnte > 0)
                    {
                        MotelyJsonScoring.ActivateAllVouchers(
                            ref singleCtx,
                            ref runState,
                            config.MaxVoucherAnte
                        );
                    }

                    // Use pre-computed MaxBossAnte from PostProcess()
                    int maxBossAnte = config.MaxBossAnte;



                    // Generate and cache all bosses if needed
                    // Game starts at Ante 1, so bosses are generated from ante 1 onwards
                    MotelyBossBlind[]? cachedBosses = null;
                    if (maxBossAnte > 0)
                    {
                        cachedBosses = new MotelyBossBlind[maxBossAnte + 1]; // +1 to handle 1-based indexing
                        var bossStream = singleCtx.CreateBossStream();
                        var bossState = new MotelyRunState(); // Separate state for boss generation
                        for (int ante = 1; ante <= maxBossAnte; ante++)
                        {
                            cachedBosses[ante] = singleCtx.GetBossForAnte(
                                ref bossStream,
                                ante,
                                ref bossState
                            );
                        }

                        // Store cached bosses in runState for use by scoring functions
                        runState.CachedBosses = cachedBosses;
                    }

                    // Always validate Must clauses - either as the only filter (scoreOnlyMode)
                    // or as additional requirements on top of the base filter
                    if (config.Must?.Count > 0)
                    {
                        // SMART: Process vouchers FIRST in order, then other requirements

                        // Step 1: Check all voucher requirements (they depend on each other)
                        // PERFORMANCE: Use pre-partitioned array (no filtering needed!)
                        foreach (var clause in config.MustVouchers)
                        {

                            bool clauseSatisfied = false;

                            // Check if voucher is already active from ActivateAllVouchers
                            if (
                                clause.VoucherEnum.HasValue
                                && runState.IsVoucherActive(clause.VoucherEnum.Value)
                            )
                            {
                                clauseSatisfied = true;
                            }
                            else
                            {
                                // Check if it appears in any required ante
                                foreach (var ante in clause.EffectiveAntes ?? [])
                                {
                                    if (
                                        MotelyJsonScoring.CheckVoucherSingle(
                                            ref singleCtx,
                                            clause,
                                            ante,
                                            ref runState
                                        )
                                    )
                                    {
                                        clauseSatisfied = true;
                                        break;
                                    }
                                }
                            }

                            if (!clauseSatisfied)
                            {
                                // DebugLogger.Log($"[Score] Voucher clause not satisfied: {clause.Value}"); // DISABLED FOR PERFORMANCE
                                return false;
                            }
                            else
                            {
                                // DebugLogger.Log($"[Score] Voucher clause satisfied: {clause.Value}"); // DISABLED FOR PERFORMANCE
                            }
                        }

                        // Step 2: Check all other requirements
                        // PERFORMANCE: Use pre-partitioned array (no filtering needed!)
                        foreach (var clause in config.MustNonVouchers)
                        {

                            bool clauseSatisfied = false;

                            // Check if this requirement appears in ANY of its required antes
                            foreach (var ante in clause.EffectiveAntes ?? [])
                            {
                                switch (clause.ItemTypeEnum)
                                {
                                    case MotelyFilterItemType.SoulJoker:
                                        if (
                                            MotelyJsonScoring.CheckSoulJokerForSeed(
                                                new List<MotelyJsonSoulJokerFilterClause>
                                                {
                                                    MotelyJsonSoulJokerFilterClause.FromJsonClause(
                                                        clause
                                                    ),
                                                },
                                                ref singleCtx,
                                                earlyExit: true
                                            )
                                        )
                                        {
                                            clauseSatisfied = true;
                                            break;
                                        }
                                        break;

                                    case MotelyFilterItemType.Joker:
                                        if (
                                            MotelyJsonScoring.CountJokerOccurrences(
                                                ref singleCtx,
                                                MotelyJsonJokerFilterClause.FromJsonClause(clause),
                                                ante,
                                                ref runState,
                                                earlyExit: true,
                                                originalClause: clause
                                            ) > 0
                                        )
                                        {
                                            clauseSatisfied = true;
                                            break;
                                        }
                                        break;

                                    case MotelyFilterItemType.TarotCard:
                                        if (
                                            MotelyJsonScoring.TarotCardsTally(
                                                ref singleCtx,
                                                MotelyJsonTarotFilterClause.FromJsonClause(clause),
                                                ante,
                                                ref runState,
                                                earlyExit: true
                                            ) > 0
                                        )
                                        {
                                            clauseSatisfied = true;
                                            break;
                                        }
                                        break;

                                    case MotelyFilterItemType.PlanetCard:
                                        if (
                                            MotelyJsonScoring.CountPlanetOccurrences(
                                                ref singleCtx,
                                                MotelyJsonPlanetFilterClause.FromJsonClause(clause),
                                                ante,
                                                earlyExit: true
                                            ) > 0
                                        )
                                        {
                                            clauseSatisfied = true;
                                            break;
                                        }
                                        break;

                                    case MotelyFilterItemType.SpectralCard:
                                        if (
                                            MotelyJsonScoring.CountSpectralOccurrences(
                                                ref singleCtx,
                                                MotelyJsonSpectralFilterClause.FromJsonClause(clause),
                                                ante,
                                                earlyExit: true
                                            ) > 0
                                        )
                                        {
                                            clauseSatisfied = true;
                                            break;
                                        }
                                        break;

                                    case MotelyFilterItemType.PlayingCard:
                                        if (
                                            MotelyJsonScoring.CountPlayingCardOccurrences(
                                                ref singleCtx,
                                                clause,
                                                ante,
                                                earlyExit: true
                                            ) > 0
                                        )
                                        {
                                            clauseSatisfied = true;
                                            break;
                                        }
                                        break;

                                    case MotelyFilterItemType.SmallBlindTag:
                                    case MotelyFilterItemType.BigBlindTag:
                                        if (
                                            MotelyJsonScoring.CheckTagSingle(
                                                ref singleCtx,
                                                clause,
                                                ante
                                            )
                                        )
                                        {
                                            clauseSatisfied = true;
                                            break;
                                        }
                                        break;
                                }

                                if (clauseSatisfied)
                                    break; // Found in one ante, move to next clause
                            }

                            // If this Must clause wasn't satisfied, seed fails
                            if (!clauseSatisfied)
                            {
                                // DebugLogger.Log($"[Score] Non-voucher clause not satisfied: {clause.ItemTypeEnum} {clause.Value}"); // DISABLED FOR PERFORMANCE
                                return false;
                            }
                            else
                            {
                                // DebugLogger.Log($"[Score] Non-voucher clause satisfied: {clause.ItemTypeEnum} {clause.Value}"); // DISABLED FOR PERFORMANCE
                            }
                        }
                    }

                    // Get seed string first
                    string seedStr;
                    unsafe
                    {
                        char* seedPtr = stackalloc char[9];
                        int length = singleCtx.GetSeed(seedPtr);
                        seedStr = new string(seedPtr, 0, length);
                    }

                    // Score Should clauses and add tallies (aggregation controlled by top-level mode)
                    int totalScore = 0;
                    var seedScore = new MotelySeedScoreTally(seedStr, 0);

                    if (config.Should?.Count > 0)
                    {
                        switch (config.ScoreAggregationMode)
                        {
                            case MotelyScoreAggregationMode.Sum:
                                foreach (var should in config.Should)
                                {
                                    int count = MotelyJsonScoring.CountOccurrences(
                                        ref singleCtx,
                                        should,
                                        ref runState
                                    );
                                    int score = count * should.Score;
                                    totalScore += score;
                                    seedScore.AddTally(count);
                                }
                                break;
                            case MotelyScoreAggregationMode.MaxCount:
                            {
                                int maxCount = 0;
                                foreach (var should in config.Should)
                                {
                                    int count = MotelyJsonScoring.CountOccurrences(
                                        ref singleCtx,
                                        should,
                                        ref runState
                                    );
                                    if (count > maxCount)
                                        maxCount = count;
                                    seedScore.AddTally(count);
                                }
                                totalScore = maxCount;
                                break;
                            }
                            default:
                                // Future-proofing: default to Sum behavior for unknown modes
                                DebugLogger.Log(
                                    $"[Score] Unknown ScoreAggregationMode: {config.ScoreAggregationMode}; defaulting to Sum"
                                );
                                foreach (var should in config.Should)
                                {
                                    int count = MotelyJsonScoring.CountOccurrences(
                                        ref singleCtx,
                                        should,
                                        ref runState
                                    );
                                    int score = count * should.Score;
                                    totalScore += score;
                                    seedScore.AddTally(count);
                                }
                                break;
                        }
                    }

                    // Set final score
                    seedScore.Score = totalScore;
                    buffer[singleCtx.VectorLane] = seedScore;

                    // Increment local counter (batch Interlocked operation later)
                    localFiltered++;

                    // Apply cutoff filtering - return true/false, caller will count results
                    var currentCutoff = GetCurrentCutoff(totalScore, autoCutoff, cutoff);
                    bool passedCutoff = totalScore >= currentCutoff;

                    // Invoke callback for seeds that passed cutoff
                    if (passedCutoff && onResultFound != null)
                    {
                        onResultFound(seedScore);
                    }

                    return passedCutoff;
                }
            );

            // Batch update filtered counter ONCE per vector (instead of 8 times per seed!)
            if (localFiltered > 0)
            {
                Interlocked.Add(ref _seedsFiltered, localFiltered);
            }

            // Return the mask - caller will count how many passed and invoke callbacks
            return resultMask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetCurrentCutoff(int currentScore, bool autoCutoff, int cutoff)
        {
            if (!autoCutoff)
                return cutoff;

            // Thread-safe auto cutoff: Start at 1, raise to highest score found
            if (currentScore > _learnedCutoff)
            {
                var oldCutoff = Interlocked.Exchange(ref _learnedCutoff, currentScore);
                // DebugLogger.Log($"[AutoCutoff] Raised cutoff from {oldCutoff} to {currentScore}"); // DISABLED FOR PERFORMANCE
            }

            return _learnedCutoff;
        }
    }
}
