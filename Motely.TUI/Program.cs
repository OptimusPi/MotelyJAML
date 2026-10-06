using Motely.TUI;

namespace Motely
{
    partial class Program
    {
        /// <summary>Crash log path next to the executable.</summary>
        internal static string CrashLogPath =>
            Path.Combine(AppContext.BaseDirectory, "motely-tui-crash.txt");

        static int Main(string[] args)
        {
            // Log unhandled exceptions from any thread so we see native/callback failures
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                try
                {
                    File.WriteAllText(
                        CrashLogPath,
                        $"{DateTime.UtcNow:O} [Unhandled]\n{(Exception)e.ExceptionObject}"
                    );
                }
                catch { }
            };

            try
            {
                File.WriteAllText(CrashLogPath, $"{DateTime.UtcNow:O} Main entered\n");
            }
            catch { }

            try
            {
                if (args.Length == 0)
                    return MotelyTUI.Run();

                // If args provided, show message that CLI is separate
                Console.WriteLine("CLI functionality has been moved to Motely.CLI");
                Console.WriteLine("Run: dotnet run --project Motely.CLI -- [args]");
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Motely TUI failed to start:");
                Console.Error.WriteLine(ex.ToString());
                try
                {
                    File.WriteAllText(CrashLogPath, $"{DateTime.UtcNow:O}\n{ex}");
                }
                catch { }
                return 1;
            }
        }
    }
}
