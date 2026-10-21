namespace Motely.CLI;

/// <summary>
/// Keeps one progress line "sticky" at the bottom of the terminal by overwriting
/// it in place using ANSI escape sequences.
///
/// Works when neither stdout nor stderr is redirected (i.e. both go to a real
/// terminal that supports VT100 escapes — Windows Terminal, PowerShell 7, macOS
/// Terminal, etc.).  Falls back to plain newline-per-update when either stream
/// is redirected (piped / file).
/// </summary>
internal static class StickyProgress
{
    // ESC[2K  = erase entire current line
    private const string EraseCurrentLine = "\x1b[2K";

    private static readonly object _gate = new();
    private static string _current = "";
    private static readonly TextWriter LiveWriter = Console.Out;

    /// <summary>True when sticky mode is active (both streams are live TTYs).</summary>
    public static readonly bool IsLive = !Console.IsOutputRedirected && !Console.IsErrorRedirected;

    /// <summary>
    /// Update the sticky progress line.
    /// In non-live mode this emits a plain stderr newline (old behaviour).
    /// </summary>
    public static void Update(string line)
    {
        if (!IsLive)
        {
            Console.Error.WriteLine(line);
            return;
        }

        lock (_gate)
        {
            _current = line;
            // Overwrite the current line in place — no newline.
            LiveWriter.Write($"\r{EraseCurrentLine}{line}");
        }
    }

    /// <summary>
    /// Write a result line to stdout while keeping the progress line at the bottom.
    /// Clears progress, prints the result (with newline), then redraws progress.
    /// </summary>
    public static void WriteResultLine(string result)
    {
        if (!IsLive)
        {
            Console.WriteLine(result);
            return;
        }

        lock (_gate)
        {
            // Erase the sticky progress line, print the result above it, redraw progress.
            LiveWriter.Write($"\r{EraseCurrentLine}{result}\n");
            if (_current.Length > 0)
                LiveWriter.Write($"\r{EraseCurrentLine}{_current}");
        }
    }

    /// <summary>
    /// Clear the sticky line (call on exit / after the final summary is printed).
    /// </summary>
    public static void Clear()
    {
        if (!IsLive)
            return;
        lock (_gate)
        {
            _current = "";
            LiveWriter.Write($"\r{EraseCurrentLine}");
        }
    }
}
