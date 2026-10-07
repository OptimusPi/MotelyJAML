using Motely.Filters;

namespace Motely.Analysis;

/// <summary>
/// The Jamlyzer riding a search: an <see cref="IMotelySeedAnalyzeDesc"/> that walks every seed the
/// search reports, on the very context that just filtered and scored it, and hands each breakdown
/// to <paramref name="onAnalyzed"/> as a <see cref="MotelyJamlyzerSeedResult"/>. Score and Tally are
/// copied from the search's own scored row (0 / null when the search has no score provider), so
/// what arrives is the find the scored callback just reported plus everything the seed contains.
/// <para>
/// Attach with <see cref="IMotelySearchSettings.WithSeedAnalyzeProvider"/>; build one from a JAML
/// with <see cref="MotelyJamlyzer.CreateRiderDesc"/>. It never gates: the search reports what it
/// reports, this only follows. Fires on the worker thread that found the seed, so a multi-threaded
/// native search calls <paramref name="onAnalyzed"/> concurrently; the browser's single thread
/// calls it in find order, each right after that seed's scored callback.
/// </para>
/// </summary>
public sealed class MotelyJamlyzerRiderDesc(
    int[] antesToAnalyze,
    Action<MotelyJamlyzerSeedResult> onAnalyzed,
    int eventRolls = 20,
    int shopSlots = 0
) : IMotelySeedAnalyzeDesc<MotelyJamlyzerRiderDesc.JamlyzerRider>
{
    public JamlyzerRider CreateAnalyzeProvider(ref MotelyFilterCreationContext ctx) => new(this);

    public readonly struct JamlyzerRider(MotelyJamlyzerRiderDesc desc) : IMotelySeedAnalyzeProvider
    {
        public void Analyze(
            ref MotelyVectorSearchContext ctx,
            VectorMask reportedMask,
            MotelyScoredSeedResult[]? scores
        )
        {
            var window = desc._window;
            var onAnalyzed = desc._onAnalyzed;
            ctx.SearchIndividualSeeds(
                reportedMask,
                singleCtx =>
                {
                    var result = MotelyJamlyzerSeedWalk.Walk(
                        ref singleCtx,
                        in window,
                        resumeFrom: null
                    );
                    if (scores is not null)
                    {
                        ref readonly var row = ref scores[singleCtx.VectorLane];
                        result = result with { Score = row.Score, Tally = row.Tallies };
                    }
                    onAnalyzed(result);
                    return 1;
                }
            );
        }
    }

    private readonly MotelyJamlyzerWindow _window = new(antesToAnalyze, eventRolls, shopSlots);
    private readonly Action<MotelyJamlyzerSeedResult> _onAnalyzed = onAnalyzed;
}
