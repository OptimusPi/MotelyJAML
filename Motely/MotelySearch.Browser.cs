using System;
using System.Threading.Tasks;

namespace Motely;

// Browser-only half of MotelySearch. This partial is intentionally NOT marked `unsafe`:
// the main file's class is `unsafe`, and C# (CS4004) forbids `await` in an unsafe context.
// Keeping the cooperative pump in a plain partial lets it `await` while the SIMD batch code
// it calls stays pointer-heavy and unsafe in the main file — same class, no wrapper, no glue.
public sealed partial class MotelySearch<TBaseFilter>
{
    // Single-threaded WASM has no thread to offload the CPU-bound search to. So instead of
    // blocking the one thread until the grind finishes (frozen UI), we run a batch, hand the
    // thread back to the browser event loop for a repaint (await Task.Delay(1)), then take it
    // back for the next batch. Native never gets here — it uses real (p)threads.
    private async Task RunSequentialBrowserPumpAsync()
    {
        MotelySearchPlan plan = _plans[0];

        while (plan.TryExecuteSequentialBatch())
            await Task.Delay(1);

        // Skip the flush if Dispose ran during an await — it already freed the plan's filter
        // batches, and flushing would dereference them (use-after-free, one event-loop turn
        // late). No threads here, so this gate is the browser's stand-in for Dispose's Join.
        if (Volatile.Read(ref _isDisposed) == 0)
            plan.FlushFilterBatches();
        SignalSearchCompleted();
    }

    // Provider-mode twin of the pump above (datalake re-derivation). Same shape: drive one
    // batch, yield to the event loop for a repaint, repeat — then flush (gated on !disposed
    // for the same use-after-free reason) and signal completion. Native uses RunProviderPlan.
    private async Task RunProviderBrowserPumpAsync()
    {
        MotelySearchPlan plan = _plans[0];

        while (plan.TryExecuteProviderBatch())
            await Task.Delay(1);

        if (Volatile.Read(ref _isDisposed) == 0)
            plan.FlushFilterBatches();
        SignalSearchCompleted();
    }
}
