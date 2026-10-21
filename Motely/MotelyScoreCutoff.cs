using System.Threading;

namespace Motely;

/// <summary>
/// The single, shared "should this scored seed be emitted / persisted?" gate. Every caller
/// (Motely.CLI, Motely.TUI, and any future GUI) routes scored results through one of these so the
/// three cutoff modes behave identically everywhere:
///
///   • Auto (running maximum): emit every seed at-or-above the best score seen so far. The bar
///     ratchets up as better seeds appear; ties at the current best are kept.
///   • Fixed floor: emit only seeds whose score is >= the floor.
///   • Off: emit everything.
///
/// This replaced two hand-kept-in-sync copies (CLI <c>ShouldEmitScore</c> and TUI
/// <c>PassesCutoff</c>) that could — and did — drift. It is thread-safe: result callbacks fire on
/// every engine worker thread with no serialization, so the auto ratchet uses a lock-free CAS loop
/// and the fixed/off paths are pure reads.
/// </summary>
public sealed class MotelyScoreCutoff
{
    private readonly bool _auto;
    private readonly int _fixedFloor;

    // Auto mode's running maximum. int.MinValue means "nothing seen yet — accept the first".
    private int _learned;

    private MotelyScoreCutoff(bool auto, int fixedFloor, int learned)
    {
        _auto = auto;
        _fixedFloor = fixedFloor;
        _learned = learned;
    }

    /// <summary>Auto: running maximum. The first seed always passes and raises the bar.</summary>
    public static MotelyScoreCutoff Auto() => new(auto: true, fixedFloor: int.MinValue, learned: int.MinValue);

    /// <summary>Fixed floor: emit only seeds scoring &gt;= <paramref name="floor"/>.</summary>
    public static MotelyScoreCutoff Fixed(int floor) => new(auto: false, fixedFloor: floor, learned: int.MinValue);

    /// <summary>Off: emit every seed.</summary>
    public static MotelyScoreCutoff Off() => new(auto: false, fixedFloor: int.MinValue, learned: int.MinValue);

    /// <summary>Whether auto (running-maximum) mode is active.</summary>
    public bool IsAuto => _auto;

    /// <summary>The current running-maximum in auto mode (int.MinValue before the first seed).</summary>
    public int CurrentHigh => Volatile.Read(ref _learned);

    /// <summary>
    /// Parse the user-facing cutoff text used by every front-end: "auto" (or empty → auto),
    /// blank/"off"/"none" → off, or an integer → fixed floor. Returns false (with the raw value
    /// echoed in <paramref name="error"/>) when the text is neither a keyword nor an integer.
    /// </summary>
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

    /// <summary>
    /// The engine-side pre-filter threshold this cutoff implies. The scorer drops seeds below this
    /// value before any callback fires, so a fixed floor can be pushed all the way into the engine;
    /// auto and off cannot (auto's bar is not known until seeds arrive), so they use 0.
    /// </summary>
    public int EngineCutoff => (!_auto && _fixedFloor > int.MinValue) ? _fixedFloor : 0;

    /// <summary>
    /// Thread-safe: does a seed with this score pass the gate? In auto mode this also ratchets the
    /// running maximum up. Called once per surviving seed on arbitrary worker threads.
    /// </summary>
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
