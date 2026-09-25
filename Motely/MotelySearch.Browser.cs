using System;
using System.Threading.Tasks;

namespace Motely;

public sealed partial class MotelySearch<TBaseFilter>
{
    private async Task RunSequentialBrowserPumpAsync()
    {
        MotelySearchPlan plan = _plans[0];

        while (plan.TryExecuteSequentialBatch())
            await Task.Delay(1);

        if (Volatile.Read(ref _isDisposed) == 0)
            plan.FlushFilterBatches();
        SignalSearchCompleted();
    }

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
