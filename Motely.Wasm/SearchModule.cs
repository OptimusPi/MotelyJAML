using Motely;
using Motely.Analysis;
using Motely.Filters;
using Motely.Filters.Jaml;

public sealed class SearchModule : ISearch
{
    public event Action<MotelyProgress>? OnProgress;
    public event Action<MotelySeedScore>? OnScored;
    public event Action<MotelyJamlyzerSeedResult>? OnAnalyzed;

    public SearchSettings Settings(string jaml)
    {
        var config = JamlConfigLoader.FromJaml(jaml);
        // The ante window has to be read before CreateSettings fills unscoped clauses in place,
        // so withAnalysis walks the same antes Analyze.seeds does for this filter.
        int[] antes = MotelyJamlyzer.ComputeAntes(config);
        return new SearchSettings(JamlSearchBuilder.CreateSettings(config), antes, this);
    }

    internal void Progress(MotelyProgress p) => OnProgress?.Invoke(p);
    internal void Scored(MotelySeedScore s) => OnScored?.Invoke(s);
    internal void Analyzed(MotelyJamlyzerSeedResult r) => OnAnalyzed?.Invoke(r);
}
