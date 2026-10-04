using Bootsharp;
using Motely.Analysis;
using Motely.Filters.Jaml;

/// <summary>The Jamlyzer: JAML text in, records out.</summary>
public static partial class Analyze
{
    [Export]
    public static IReadOnlyList<MotelyJamlyzerSeedResult> Seeds(string jaml) =>
        MotelyJamlyzer.Analyze(JamlConfigLoader.FromJaml(jaml));

    /// <summary>eventRolls sizes the roll queues; shopSlots how deep each ante's shop is walked
    /// (0 keeps the defaults).</summary>
    [Export]
    public static IReadOnlyList<MotelyJamlyzerSeedResult> SeedsPaged(string jaml, int eventRolls, int shopSlots = 0) =>
        MotelyJamlyzer.Analyze(JamlConfigLoader.FromJaml(jaml), eventRolls, shopSlots);

    /// <summary>Continue a scroll from the streamStates of a previous result. One seed only.</summary>
    [Export]
    public static IReadOnlyList<MotelyJamlyzerSeedResult> SeedsResume(
        string jaml, MotelyJamlyzerStreamStates resumeFrom, int eventRolls, int shopSlots = 0) =>
        MotelyJamlyzer.Analyze(JamlConfigLoader.FromJaml(jaml), resumeFrom, eventRolls, shopSlots);
}
