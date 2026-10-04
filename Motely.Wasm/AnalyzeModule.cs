using Motely.Analysis;
using Motely.Filters.Jaml;

public sealed class AnalyzeModule : IAnalyze
{
    public IReadOnlyList<MotelyJamlyzerSeedResult> Seeds(string jaml) =>
        MotelyJamlyzer.Analyze(JamlConfigLoader.FromJaml(jaml));

    public IReadOnlyList<MotelyJamlyzerSeedResult> SeedsPaged(string jaml, int eventRolls, int shopSlots = 0) =>
        MotelyJamlyzer.Analyze(JamlConfigLoader.FromJaml(jaml), eventRolls, shopSlots);

    public IReadOnlyList<MotelyJamlyzerSeedResult> SeedsResume(
        string jaml,
        MotelyJamlyzerStreamStates resumeFrom,
        int eventRolls,
        int shopSlots = 0
    ) => MotelyJamlyzer.Analyze(JamlConfigLoader.FromJaml(jaml), resumeFrom, eventRolls, shopSlots);
}
