using Motely;
using Motely.Analysis;

// The JS surface. Each interface is an exported module (guide: interop-modules): JS sees it
// without the I, so ISearch is `Search`. Filters cross as JAML text, never as JamlConfig: a class
// on the boundary is passed by reference (guide: interop-instances) and would drag every clause
// type into JS.

/// <summary>Seed search over a JAML filter.</summary>
public interface ISearch
{
    /// <summary>Progress of the running search, at the settings' report interval.</summary>
    event Action<MotelyProgress> OnProgress;

    /// <summary>One per find, with its score and per-clause tally.</summary>
    event Action<MotelySeedScore> OnScored;

    /// <summary>A find's Jamlyzer breakdown, right after its OnScored. Only with
    /// <see cref="SearchSettings.WithAnalysis"/>.</summary>
    event Action<MotelyJamlyzerSeedResult> OnAnalyzed;

    /// <summary>The engine's settings for this filter. Throws when the JAML does not load;
    /// <see cref="IJaml.Check"/> says why.</summary>
    SearchSettings Settings(string jaml);
}

/// <summary>The Jamlyzer: what a seed holds, ante by ante.</summary>
public interface IAnalyze
{
    /// <summary>One result per seed in the filter's <c>seeds:</c>.</summary>
    IReadOnlyList<MotelyJamlyzerSeedResult> Seeds(string jaml);

    /// <summary>eventRolls sizes the roll queues; shopSlots is how deep each ante's shop is walked
    /// (0 keeps the defaults).</summary>
    IReadOnlyList<MotelyJamlyzerSeedResult> SeedsPaged(string jaml, int eventRolls, int shopSlots = 0);

    /// <summary>Continues a scroll from a previous result's streamStates. One seed only.</summary>
    IReadOnlyList<MotelyJamlyzerSeedResult> SeedsResume(
        string jaml,
        MotelyJamlyzerStreamStates resumeFrom,
        int eventRolls,
        int shopSlots = 0
    );
}

/// <summary>Filter text checks for editors.</summary>
public interface IJaml
{
    /// <summary>Null when the text loads, otherwise the loader's message naming the line.</summary>
    string? Check(string text);
}

/// <summary>.jaml files in a folder the user picks. Needs Bootsharp.FileSystem; without it
/// <see cref="IsSupported"/> is false and every file call rejects.</summary>
public interface IJamlFiles
{
    /// <summary>A filter file was added, removed, modified or moved under the folder.</summary>
    event Action<JamlFileChange> OnChange;

    bool IsSupported();
    bool IsMounted();

    /// <summary>Every filter file under the folder, sorted.</summary>
    string[] List();

    /// <summary>Asks the user for a folder. False when they cancel.</summary>
    Task<bool> PickFolder();
    Task Unmount();
    Task<string> Load(string name);
    Task Save(string name, string jaml);
    Task Delete(string name);
    Task Rename(string fromName, string toName);
}

/// <summary>Kind is added, removed, modified or moved. Name is what <see cref="IJamlFiles.Load"/>
/// takes: <c>sub/filter</c> for <c>sub/filter.jaml</c>, other extensions kept.</summary>
public readonly record struct JamlFileChange(string Kind, string Name, string? FromName);
