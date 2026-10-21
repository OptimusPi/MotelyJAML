using System.Runtime.InteropServices;

internal static partial class ConsoleKeyMonitor
{
    public static void Run(Action onEsc, Action onProgress, CancellationToken stopToken)
    {
        if (Console.IsInputRedirected)
            return;
        try
        {
            if (OperatingSystem.IsWindows())
                RunWindows(onEsc, onProgress, stopToken);
            else
                RunFallback(onEsc, onProgress, stopToken);
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) { }
        catch (IOException) { }
    }

    // Win32 raw console input — completely independent of the managed Console lock.
    private static void RunWindows(Action onEsc, Action onProgress, CancellationToken stopToken)
    {
        nint hInput = GetStdHandle(STD_INPUT_HANDLE);
        if (hInput == 0 || hInput == -1)
        {
            RunFallback(onEsc, onProgress, stopToken);
            return;
        }

        while (!stopToken.IsCancellationRequested)
        {
            uint wait = WaitForSingleObject(hInput, 200);
            if (wait == WAIT_TIMEOUT)
                continue;
            if (wait != WAIT_OBJECT_0)
                break;

            if (
                ReadConsoleInput(hInput, out var record, 1, out var eventsRead) == 0
                || eventsRead == 0
            )
                continue;
            if (record.EventType != KEY_EVENT)
                continue;
            if (record.KeyEvent.bKeyDown == 0)
                continue;

            if (record.KeyEvent.wVirtualKeyCode == VK_ESCAPE)
            {
                onEsc();
                return;
            }

            char ch = (char)record.KeyEvent.UnicodeChar;
            if (ch is 'p' or 'P')
                onProgress();
        }
    }

    // Managed fallback for non-Windows (Console lock contention is less of an issue on Unix).
    private static void RunFallback(Action onEsc, Action onProgress, CancellationToken stopToken)
    {
        if (Console.IsInputRedirected)
            return;
        while (!stopToken.IsCancellationRequested)
        {
            Thread.Sleep(100);
            if (!Console.KeyAvailable)
                continue;
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Escape)
            {
                onEsc();
                return;
            }
            if (key.KeyChar is 'p' or 'P')
                onProgress();
        }
    }

    // ── Win32 P/Invoke (source-generated, AOT-safe) ──

    private const int STD_INPUT_HANDLE = -10;
    private const uint WAIT_OBJECT_0 = 0;
    private const uint WAIT_TIMEOUT = 0x102;
    private const ushort KEY_EVENT = 0x0001;
    private const ushort VK_ESCAPE = 0x1B;

    [LibraryImport("kernel32", SetLastError = true)]
    private static partial nint GetStdHandle(int nStdHandle);

    [LibraryImport("kernel32", SetLastError = true)]
    private static partial uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);

    [LibraryImport("kernel32", EntryPoint = "ReadConsoleInputW", SetLastError = true)]
    private static partial int ReadConsoleInput(
        nint hConsoleInput,
        out INPUT_RECORD lpBuffer,
        uint nLength,
        out uint lpNumberOfEventsRead
    );

    [StructLayout(LayoutKind.Explicit, Size = 20)]
    private struct INPUT_RECORD
    {
        [FieldOffset(0)]
        public ushort EventType;

        [FieldOffset(4)]
        public KEY_EVENT_RECORD KeyEvent;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEY_EVENT_RECORD
    {
        public int bKeyDown; // Win32 BOOL = 4 bytes, NOT C# bool
        public ushort wRepeatCount;
        public ushort wVirtualKeyCode;
        public ushort wVirtualScanCode;
        public ushort UnicodeChar;
        public uint dwControlKeyState;
    }
}
