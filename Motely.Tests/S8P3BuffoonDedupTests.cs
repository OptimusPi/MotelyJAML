namespace Motely.Tests;

/// <summary>
/// S8.P3 — scalar buffoon-pack dedup-resample law: the pack the player opens never
/// contains a duplicate joker. The raw per-card stream and the deduplicated pack walk
/// the same main PRNG positions (resamples come from their own rarity-keyed streams),
/// so positions where the raw roll is fresh must agree exactly, and positions where the
/// raw roll repeats an earlier card must be replaced by a joker not already in the pack.
/// The scan must actually witness duplicates, or it proved nothing.
/// </summary>
public sealed class S8P3BuffoonDedupTests
{
    private static readonly string[] Seeds =
    [
        "ALEEB", "MOTELY77", "UNITTEST", "5X5", "616", "696", "6J6", "7H7",
        "99", "CC", "F", "Q", "R", "VV", "H", "I", "Z", "88", "AAAAAAAA", "MOTELY",
        "474", "3X3", "GHG", "4C4", "2A2", "111", "CUC", "FMF",
    ];

    private struct DedupProbeDesc : IMotelySeedFilterDesc<DedupProbeDesc.DedupProbeFilter>
    {
        public static int DuplicatesWitnessed;
        public static readonly List<string> Violations = [];

        public readonly DedupProbeFilter CreateFilter(ref MotelyFilterCreationContext ctx) =>
            new();

        public struct DedupProbeFilter : IMotelySeedFilter
        {
            public readonly VectorMask Filter(ref MotelyVectorSearchContext ctx)
            {
                return ctx.SearchIndividualSeeds(single =>
                {
                    string seed = single.GetSeed();
                    for (int ante = 1; ante <= 4; ante++)
                    {
                        var dedupStream = single.CreateBuffoonPackJokerStream(ante);
                        var rawStream = single.CreateBuffoonPackJokerStream(ante);

                        for (int pack = 0; pack < 2; pack++)
                        {
                            var contents = single.GetNextBuffoonPackContents(
                                ref dedupStream,
                                MotelyBoosterPackSize.Mega
                            );

                            var raw = new MotelyItem[contents.Length];
                            for (int i = 0; i < raw.Length; i++)
                                raw[i] = single.GetNextJoker(ref rawStream);

                            for (int i = 0; i < contents.Length; i++)
                            {
                                var card = contents.GetItem(i);

                                for (int j = 0; j < i; j++)
                                    if (contents.GetItem(j).Type == card.Type)
                                        Violations.Add(
                                            $"{seed} a{ante} p{pack}: duplicate {card.Type} at {j},{i}"
                                        );

                                bool rawIsRepeat = false;
                                for (int j = 0; j < i; j++)
                                    if (raw[j].Type == raw[i].Type)
                                        rawIsRepeat = true;

                                if (rawIsRepeat)
                                {
                                    Interlocked.Increment(ref DuplicatesWitnessed);
                                }
                                else if (card.Type != raw[i].Type)
                                {
                                    // A fresh raw roll must survive dedup untouched —
                                    // order-within-key law.
                                    Violations.Add(
                                        $"{seed} a{ante} p{pack} c{i}: fresh raw {raw[i].Type} became {card.Type}"
                                    );
                                }
                            }
                        }
                    }
                    return 0;
                });
            }
        }
    }

    [Fact]
    public void BuffoonPackContents_NeverDuplicate_AndFreshRollsSurvive()
    {
        DedupProbeDesc.DuplicatesWitnessed = 0;
        DedupProbeDesc.Violations.Clear();
        using var search = new MotelySearchSettings<DedupProbeDesc.DedupProbeFilter>(
            new DedupProbeDesc()
        )
            .WithSeedGenerator(Seeds, Seeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .Start();
        search.AwaitCompletion();

        Assert.Empty(DedupProbeDesc.Violations);
        Assert.True(
            DedupProbeDesc.DuplicatesWitnessed > 0,
            "scan never witnessed a raw duplicate — widen seeds/antes; the dedup path was not exercised"
        );
    }
}
