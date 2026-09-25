using System.Threading;

namespace Motely;

public sealed class MotelyScoreCutoff
{
    private readonly bool _auto;
    private readonly int _fixedFloor;

    private int _learned;

    private MotelyScoreCutoff(bool auto, int fixedFloor, int learned)
    {
        _auto = auto;
        _fixedFloor = fixedFloor;
        _learned = learned;
    }

    public static MotelyScoreCutoff Auto() => new(auto: true, fixedFloor: int.MinValue, learned: int.MinValue);

    public static MotelyScoreCutoff Fixed(int floor) => new(auto: false, fixedFloor: floor, learned: int.MinValue);

    public static MotelyScoreCutoff Off() => new(auto: false, fixedFloor: int.MinValue, learned: int.MinValue);

    public bool IsAuto => _auto;

    public int CurrentHigh => Volatile.Read(ref _learned);

    public static bool TryParse(string? text, out MotelyScoreCutoff cutoff, out string? error)
    {
        error = null;
        var raw = (text ?? string.Empty).Trim();

        if (raw.Length == 0 || raw.Equals("auto", System.StringComparison.OrdinalIgnoreCase))
        {
            cutoff = Auto();
            return true;
        }

        if (raw.Equals("off", System.StringComparison.OrdinalIgnoreCase)
            || raw.Equals("none", System.StringComparison.OrdinalIgnoreCase))
        {
            cutoff = Off();
            return true;
        }

        if (int.TryParse(raw, out var n))
        {
            cutoff = Fixed(n);
            return true;
        }

        error = $"Invalid cutoff '{raw}' — use 'auto', an integer, or 'off'.";
        cutoff = Off();
        return false;
    }

    public int EngineCutoff => (!_auto && _fixedFloor > int.MinValue) ? _fixedFloor : 0;

    public bool ShouldEmit(int score)
    {
        if (!_auto)
            return _fixedFloor == int.MinValue || score >= _fixedFloor;

        int observed = Volatile.Read(ref _learned);
        while (true)
        {
            if (score < observed)
                return false;

            if (score == observed)
                return true;

            int original = Interlocked.CompareExchange(ref _learned, score, observed);
            if (original == observed)
                return true;

            observed = original;
        }
    }
}
