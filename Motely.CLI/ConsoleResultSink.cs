using Motely.Filters;

namespace Motely.CLI;

internal sealed class ConsoleResultSink : IMotelyResultSink
{
    private const string Reset = "\x1b[0m";
    private const string Dim = "\x1b[2m";
    private const string Cyan = "\x1b[96m";
    private const string Yellow = "\x1b[93m";
    private const string Green = "\x1b[92m";
    private const string BrGreen = "\x1b[32;1m";

    private static readonly bool _color = !Console.IsOutputRedirected;

    public ConsoleResultSink(IReadOnlyList<string>? tallyLabels = null)
    {
        var header =
            tallyLabels is { Count: > 0 }
                ? $"seed,score,{string.Join(",", tallyLabels)}"
                : "seed,score";
        StickyProgress.WriteResultLine(_color ? $"{Dim}{header}{Reset}" : header);
    }

    public void OnSeed(string seed)
    {
        var row = new MotelyScoredSeedResult();
        row.Reset(seed, 0);
        OnScored(in row);
    }

    public void Flush() { }

    public void OnScored(in MotelyScoredSeedResult tally)
    {
        int score = tally.Score;
        var span = tally.TallyValuesSpan;

        if (!_color)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(tally.Seed);
            sb.Append(',').Append(score.ToString().PadLeft(3));
            foreach (int v in span)
                sb.Append(',').Append(v.ToString().PadLeft(2));
            StickyProgress.WriteResultLine(sb.ToString());
            return;
        }

        var csb = new System.Text.StringBuilder();
        csb.Append(Cyan).Append(tally.Seed).Append(Reset);
        csb.Append(',');

        string scoreColor =
            score >= 30 ? BrGreen
            : score >= 20 ? Green
            : score >= 10 ? Yellow
            : Dim;
        csb.Append(scoreColor).Append(score.ToString().PadLeft(3)).Append(Reset);

        for (int i = 0; i < span.Length; i++)
        {
            csb.Append(',');
            int v = span[i];
            string col = v == 0 ? Dim : Yellow;
            csb.Append(col).Append(v.ToString().PadLeft(2)).Append(Reset);
        }

        StickyProgress.WriteResultLine(csb.ToString());
    }

    public void Dispose() { }
}
