using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Motely;
using Motely.DataLake;
using Motely.Filters;
using Motely.Filters.Jaml;

namespace Motely.DistributedWorker;

public sealed class PoolWorkerHostedService : BackgroundService
{
    private readonly PoolWorkerOptions _options;
    private readonly ILogger<PoolWorkerHostedService>? _logger;

    public PoolWorkerHostedService(IOptions<PoolWorkerOptions> options, ILogger<PoolWorkerHostedService>? logger = null)
    {
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Url))
            return;

        var workerId = string.IsNullOrWhiteSpace(_options.WorkerId)
            ? $"{Environment.MachineName}-{Environment.ProcessId}"
            : _options.WorkerId;
        var threads = Math.Clamp(_options.Threads, 1, Environment.ProcessorCount);
        var targetFilterId = string.IsNullOrWhiteSpace(_options.FilterId) ? null : _options.FilterId;
        var localDbDir = string.IsNullOrWhiteSpace(_options.LocalDbPath) ? null : _options.LocalDbPath;

        _logger?.LogInformation(
            "Pool worker starting: {PoolUrl}, WorkerId={WorkerId}, Threads={Threads}, Filter={Filter}, LocalDb={LocalDb}",
            _options.Url, workerId, threads, targetFilterId ?? "any", localDbDir ?? "disabled");

        using var pool = new PoolClient(_options.Url.TrimEnd('/'));
        long totalSeedsSearched = 0;
        long totalMatches = 0;
        int chunksCompleted = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
                PoolClaimResponseDto claim;
                try
                {
                    claim = await pool.ClaimAsync(workerId, targetFilterId, stoppingToken);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Pool claim failed, retrying in 10s");
                    try { await Task.Delay(10000, stoppingToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                    continue;
                }

                if (claim.Idle)
                {
                    try { await Task.Delay(claim.RetryAfterMs, stoppingToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                    continue;
                }

                if (string.IsNullOrEmpty(claim.Jaml) || string.IsNullOrEmpty(claim.FilterId))
                {
                    await Task.Delay(2000, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                if (!JamlConfigLoader.TryLoad(claim.Jaml, out var config, out var parseError) || config is null)
                {
                    _logger?.LogWarning("JAML parse error: {Error}", parseError);
                    await Task.Delay(2000, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                var matchResults = new ConcurrentBag<SeedResultDto>();
                long seedsSearched = 0;
                long endBatchExclusive = claim.BatchIndex + Math.Max(1, claim.Remaining);

                var plan = JamlSearchBuilder.CreatePlan(config);
                var settings = plan.Settings
                    .WithDeck(config.Deck)
                    .WithStake(config.Stake)
                    .WithThreadCount(threads)
                    .WithBatchCharacterCount(claim.BatchCharCount)
                    .WithStartBatchIndex(claim.BatchIndex)
                    .WithEndBatchIndex(endBatchExclusive)
                    .WithSequentialSearch();

                using var lake = localDbDir is null ? null
                    : new SeedLakeSink(localDbDir, claim.FilterId, plan.ScoreTallyColumnCount > 0 ? plan.TallyLabels : null);

                if (plan.ScoreTallyColumnCount > 0)
                    settings = settings.WithScoredResultCallback(tally =>
                    {
                        lake?.OnScored(in tally);
                        matchResults.Add(new SeedResultDto { Seed = tally.Seed, Score = tally.Score });
                    });
                else
                    settings = settings.WithSeedMatchCallback(seed =>
                    {
                        lake?.OnSeed(seed);
                        matchResults.Add(new SeedResultDto { Seed = seed });
                    });

                try
                {
                    using var search = settings.Start(stoppingToken);
                    await search.WaitForCompletionAsync(stoppingToken);
                    seedsSearched = search.TotalSeedsSearched;
                }
                catch (OperationCanceledException) { break; }

                totalSeedsSearched += seedsSearched;
                totalMatches += matchResults.Count;

                var results = matchResults.ToArray();

                var submitBody = new SubmitResultsDto
                {
                    StartBatch = claim.BatchIndex,
                    EndBatch = endBatchExclusive,
                    Results = results,
                    SeedsSearched = seedsSearched,
                };

                try
                {
                    await pool.SubmitResultsAsync(claim.FilterId!, submitBody, stoppingToken);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Submit failed for filter {FilterId}, retrying", claim.FilterId);
                    try
                    {
                        await Task.Delay(2000, stoppingToken);
                        await pool.SubmitResultsAsync(claim.FilterId!, submitBody, stoppingToken);
                    }
                    catch (Exception ex2) { _logger?.LogError(ex2, "Submit retry failed"); }
                }

                chunksCompleted++;
            }

        _logger?.LogInformation("Pool worker stopped. Chunks={Chunks}, Seeds={Seeds}, Matches={Matches}",
            chunksCompleted, totalSeedsSearched, totalMatches);
    }
}
