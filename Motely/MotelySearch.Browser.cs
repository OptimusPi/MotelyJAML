namespace Motely;

// The browser half of MotelySearch, in a partial without `unsafe`: C# (CS4004) forbids `await`
// in an unsafe context, and the batch code this drives stays pointer-heavy in the main file.
public sealed partial class MotelySearch<TBaseFilter>
{
    // Single-threaded WASM has no thread to offload the search to. Run one batch, hand the thread
    // back to the event loop for a repaint (Task.Delay(1)), take it back for the next. Every
    // batch runs under _inlineRunGate: a Dispose from a result callback inside it then defers
    // the native free to the gate's exit instead of pulling the buffers out from under the batch.
    private async Task RunBrowserPumpAsync(bool provider)
    {
        MotelySearchPlan plan = _plans[0];
        while (Gated(() => provider ? plan.TryExecuteProviderBatch() : plan.TryExecuteSequentialBatch()))
            await Task.Delay(1);
        Gated(() =>
        {
            plan.FlushFilterBatches();
            return false;
        });
        SignalSearchCompleted();
    }

    // Runs body under the inline gate on the pump's thread. False once the search is disposed: a
    // Dispose between batches already freed the plan, one inside a batch left the free to here.
    private bool Gated(Func<bool> body)
    {
        lock (_inlineRunGate)
        {
            if (Volatile.Read(ref _isDisposed) != 0)
                return false;
            bool more = body();
            if (!_nativeReleaseDeferred)
                return more;
            _nativeReleaseDeferred = false;
            ReleaseNativeMemory();
            return false;
        }
    }
}
