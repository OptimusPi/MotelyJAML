using Motely;
using Motely.CLI;
using Motely.Filters;
using Motely.Party;

partial class Program
{
    /// <summary>
    /// <c>--party &lt;id&gt; [--server url]</c>: grind a seedfinder.app Search Party. Same engine, threads,
    /// progress line and result rows as any other CLI search; the leases decide the batch range.
    /// </summary>
    static async Task<int> RunPartyMode(string partyId, string serverUrl, int threads, bool quiet)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var worker = new MotelyPartyWorker(new HttpPartyCoordinator(http, serverUrl))
        {
            // Called once per lease. Each lease restarts at 0%, and the progress line only prints
            // when the percentage climbs, so its high-water mark resets with it.
            Configure = settings =>
            {
                _lastProgressPercent = -1;
                return settings
                    .WithThreadCount(threads)
                    .WithQuietMode(quiet)
                    .WithProgressCallback(quiet ? CaptureProgress : WriteProgressLineToStderr);
            },
            CreateSink = config =>
                new ConsoleResultSink([.. config.Should.Select(JamlSearchBuilder.DefaultTallyLabel)]),
            Log = quiet ? null : PartyLog,
        };

        if (!quiet)
            Console.Error.WriteLine($"Motely party {partyId} @ {serverUrl} | threads={threads}");

        var summary = await worker.RunAsync(partyId, _cts.Token);

        Console.Error.WriteLine(
            $"Party {partyId}: {summary.Leases} lease(s), {summary.SeedsSearched:N0} seeds, {summary.Confirmed} confirmed. Stopped: {summary.StopReason}."
        );
        if (summary.Error is { } error)
        {
            Console.Error.WriteLine($"Error: {error}");
            return 1;
        }
        return _cts.Token.IsCancellationRequested ? 1 : 0;
    }

    /// <summary>Above the sticky progress line on a terminal; stderr when piped, so stdout stays
    /// the CSV of finds.</summary>
    static void PartyLog(string line)
    {
        if (StickyProgress.IsLive)
            StickyProgress.WriteResultLine($"[party] {line}");
        else
            Console.Error.WriteLine($"[party] {line}");
    }
}
