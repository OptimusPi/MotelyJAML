using Motely.Filters;

namespace Motely;

public interface IMotelyResultSink : IDisposable
{
    void OnSeed(string seed);
    void OnScored(in MotelyScoredSeedResult tally);
    void Flush();
}

public sealed class CompositeMotelyResultSink : IMotelyResultSink
{
    private readonly IMotelyResultSink[] sinks;

    public CompositeMotelyResultSink(IEnumerable<IMotelyResultSink> sinks)
    {
        this.sinks = [.. sinks];
    }

    public void OnSeed(string seed)
    {
        for (int i = 0; i < sinks.Length; i++)
            sinks[i].OnSeed(seed);
    }

    public void OnScored(in MotelyScoredSeedResult tally)
    {
        for (int i = 0; i < sinks.Length; i++)
            sinks[i].OnScored(in tally);
    }

    public void Flush()
    {
        for (int i = 0; i < sinks.Length; i++)
            sinks[i].Flush();
    }

    public void Dispose()
    {
        for (int i = sinks.Length - 1; i >= 0; i--)
        {
            if (sinks[i] is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
