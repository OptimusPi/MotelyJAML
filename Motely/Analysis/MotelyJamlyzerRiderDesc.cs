using Motely.Filters;

namespace Motely.Analysis;

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
