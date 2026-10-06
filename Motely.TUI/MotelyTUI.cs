using Terminal.Gui;

namespace Motely.TUI;

public static class MotelyTUI
{
    private static string CrashLogPath => global::Motely.Program.CrashLogPath;

    private static void LogCrash(string phase, Exception ex)
    {
        var msg = $"{phase}: {ex.Message}";
        Console.WriteLine(msg);
        Console.WriteLine(ex.StackTrace);
        try
        {
            File.WriteAllText(CrashLogPath, $"{DateTime.UtcNow:O} [{phase}]\n{ex}");
        }
        catch { }
    }

    private static View MakeErrorFallback(string message)
    {
        var v = new View()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
        };
        var label = new Label()
        {
            X = 2,
            Y = 2,
            Width = Dim.Fill()! - 4,
            Height = 5,
            Text = message,
        };
        var exitBtn = new CleanButton()
        {
            X = Pos.Center(),
            Y = 8,
            Text = " Exit ",
        };
        exitBtn.Accept += (_, _) => Application.RequestStop(Application.Top);
        v.Add(label, exitBtn);
        exitBtn.SetFocus();
        return v;
    }

    private static BalatroShaderBackground? _shaderBackground;
    private static Window? _desktop;
    private static MainMenuWindow? _mainMenu;

    /// <summary>Window stack — open overlay windows in order they were opened.</summary>
    private static readonly List<Window> _windowStack = [];

    /// <summary>Cascade offset applied to each new window so stacked windows are distinguishable.</summary>
    private static readonly (int X, int Y)[] _cascadeOffsets =
    [
        (0, 0),
        (2, 1),
        (4, 2),
        (6, 3),
        (8, 4),
        (10, 5),
    ];

    /// <summary>Number of overlay windows currently open.</summary>
    public static int OpenWindowCount => _windowStack.Count;

    /// <summary>
    /// Show a window as a non-modal overlay on the desktop.
    /// Multiple windows can be open AND VISIBLE simultaneously — each window
    /// is responsible for its own X/Y/Width/Height so windows can be tiled
    /// side-by-side. Alt+Tab cycles focus.
    /// </summary>
    public static void ShowWindow(Window window)
    {
        if (_desktop == null)
            return;

        _windowStack.Add(window);
        _desktop.Add(window);
        window.SetFocus();
    }

    /// <summary>
    /// Close an overlay window. Focus returns to the previous window in the stack,
    /// or to the main menu if no other windows are open.
    /// </summary>
    public static void CloseWindow(Window window)
    {
        if (_desktop == null)
            return;
        _windowStack.Remove(window);
        _desktop.Remove(window);

        if (_windowStack.Count > 0)
        {
            _windowStack[^1].SetFocus();
        }
        else if (_mainMenu != null)
        {
            _mainMenu.SetFocus();
        }
    }

    /// <summary>
    /// Cycle focus to the next open window (wraps around).
    /// Called by Alt+Tab global hotkey on the desktop.
    /// </summary>
    private static void CycleWindowFocus()
    {
        if (_windowStack.Count == 0)
        {
            _mainMenu?.SetFocus();
            return;
        }

        var focused = _windowStack.FirstOrDefault(w => w.HasFocus);
        if (focused == null)
        {
            _windowStack[^1].SetFocus();
            return;
        }

        var idx = _windowStack.IndexOf(focused);
        _windowStack[(idx + 1) % _windowStack.Count].SetFocus();
    }

    public static int Run(string? configName = null, string? configFormat = null)
    {
        try
        {
            TuiSettings.Load();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load TUI settings: {ex.Message}");
        }

        try
        {
            Application.Init();
        }
        catch (Exception ex)
        {
            LogCrash("Terminal.Gui init", ex);
            return 1;
        }

        try
        {
            // Desktop: full-screen container for shader + overlapping windows
            _desktop = new Window()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                CanFocus = true,
            };

            try
            {
                _shaderBackground = new BalatroShaderBackground();
                _desktop.Add(_shaderBackground);
                _shaderBackground.Start();
            }
            catch (Exception ex)
            {
                LogCrash("Shader init (continuing without shader)", ex);
            }

            // Global Alt+Tab: cycle focus between open windows
            _desktop.KeyDown += (_, e) =>
            {
                if (e.KeyCode == (KeyCode.Tab | KeyCode.AltMask))
                {
                    CycleWindowFocus();
                    e.Handled = true;
                }
            };

            // Status bar: v2 Bar + Shortcut idiom. Informational only (each window
            // handles its own keys); shows the discoverable global hotkeys.
            var statusBar = new Bar
            {
                X = 0,
                Y = Pos.AnchorEnd(1),
                Width = Dim.Fill(),
                Height = 1,
            };
            statusBar.ColorScheme = BalatroTheme.Title;
            foreach (
                var s in new[]
                {
                    new Shortcut(Key.F1, "Help", () => { }, "F1"),
                    new Shortcut(Key.F5, "Refresh", () => { }, "F5"),
                    new Shortcut(Key.F7, "Compile", () => { }, "F7"),
                    new Shortcut(Key.Esc, "Back", () => { }, "Esc"),
                    new Shortcut(Key.Tab.WithAlt, "Cycle", () => { }, "Alt+Tab"),
                    new Shortcut((KeyCode)'S', "Search", () => { }, "S"),
                    new Shortcut((KeyCode)'D', "Designer", () => { }, "D"),
                    new Shortcut((KeyCode)'R', "Results", () => { }, "R"),
                    new Shortcut((KeyCode)'C', "Config", () => { }, "C"),
                }
            )
            {
                statusBar.Add(s);
            }
            _desktop.Add(statusBar);

            if (!string.IsNullOrEmpty(configName))
            {
                // Direct search mode (CLI arg) — show search window immediately
                var searchWindow = new SearchWindow(configName);
                _desktop.Add(searchWindow);
            }
            else
            {
                try
                {
                    _mainMenu = new MainMenuWindow();
                    _desktop.Add(_mainMenu);
                    _mainMenu.SetFocus();
                }
                catch (Exception ex)
                {
                    LogCrash("Main menu init", ex);
                    _desktop.Add(MakeErrorFallback($"Main menu failed: {ex.Message}"));
                }
            }

            Application.Run(_desktop);
            return 0;
        }
        catch (Exception ex)
        {
            LogCrash("TUI run", ex);
            return 1;
        }
        finally
        {
            _shaderBackground?.Stop();
            _windowStack.Clear();
            try
            {
                Application.Shutdown();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: Application dispose failed: {ex.Message}");
            }

            // Wipe terminal so shell prompt isn't drawn over TUI remnants
            try
            {
                Console.Clear();
            }
            catch { }
        }
    }

    public static BalatroShaderBackground? ShaderBackground => _shaderBackground;
}
