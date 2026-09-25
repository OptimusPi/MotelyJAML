namespace Motely.CLI;

internal static class StickyProgress
{
    private const string EraseCurrentLine = "\x1b[2K";

    private static readonly object _gate = new();
    private static string _current = "";
    private static readonly TextWriter LiveWriter = Console.Out;

    public static readonly bool IsLive = !Console.IsOutputRedirected && !Console.IsErrorRedirected;

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
            LiveWriter.Write($"\r{EraseCurrentLine}{line}");
        }
    }

    public static void WriteResultLine(string result)
    {
        if (!IsLive)
        {
            Console.WriteLine(result);
            return;
        }

        lock (_gate)
        {
            LiveWriter.Write($"\r{EraseCurrentLine}{result}\n");
            if (_current.Length > 0)
                LiveWriter.Write($"\r{EraseCurrentLine}{_current}");
        }
    }

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
