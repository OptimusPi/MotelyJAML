namespace Motely.Tests;

public sealed class MotelyErrorsTests
{
    [Fact]
    public void Report_WithNoSubscriber_HandsBackTheSameException()
    {
        var exception = new InvalidOperationException("x");

        Assert.Same(exception, MotelyErrors.Report(exception));
    }

    [Fact]
    public void EngineThrowSite_TellsSubscribersBeforeThrowing()
    {
        // Thrown is a static event and tests run in parallel: collect everything, assert on ours.
        var seen = new List<Exception>();
        void OnThrown(Exception e)
        {
            lock (seen)
                seen.Add(e);
        }

        MotelyErrors.Thrown += OnThrown;
        try
        {
            var thrown = Assert.Throws<ArgumentOutOfRangeException>(() => MotelyGlobals.SequentialBatchCount(99));

            Assert.StartsWith("Batch character count must be 1-", thrown.Message);
            lock (seen)
                Assert.Contains(thrown, seen);
        }
        finally
        {
            MotelyErrors.Thrown -= OnThrown;
        }
    }
}
