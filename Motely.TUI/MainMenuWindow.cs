using System.Collections.ObjectModel;

namespace Motely.TUI;

public class MainMenuWindow : View
{
    /// <summary>Workaround for Terminal.Gui v2 issue #3989: Transparent viewport can trigger NullReferenceException in View.DoDrawBorderAndPadding. Set true when upstream is fixed.</summary>
    private static bool UseTransparentViewport => true;

    public MainMenuWindow()
    {
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;

        if (UseTransparentViewport)
            ViewportSettings |= ViewportSettings.Transparent;
        ColorScheme = BalatroTheme.Title; // Title has transparent background

        // Jimbo sprite - ADD FIRST so text layers on top! (use Width for anchor; Frame may be 0 before layout)
        var jimboView = new JimboView() { Y = 1 };
        jimboView.X = Pos.AnchorEnd(jimboView.DisplayWidth + 4);
        Add(jimboView);

        // Big logo - aligned left with padding, white text
        var logoLabel = new Label()
        {
            X = 4,
            Y = 2,
            Text = JimboArt.Logo,
        };
        logoLabel.ColorScheme = BalatroTheme.Title;
        Add(logoLabel);

        // Subtitle under logo - JAML JOTD (Joke Of The Day)
        var subtitleLabel = new Label()
        {
            X = 4,
            Y = 9,
            Text = MotelyQuips.GetRandomJamlJotd(),
        };
        subtitleLabel.ColorScheme = BalatroTheme.Title;
        Add(subtitleLabel);

        // ═══════════════════════════════════════════════════════════════
        // BUTTON DOCK AT BOTTOM - Transparent container with clean buttons
        // Layout: SEARCH(12) + DESIGNER(12) + RESULTS(12) + CONFIG(8) + HOST API(12) + WORKER(12) + EXIT(8)
        // ═══════════════════════════════════════════════════════════════

        var dockBar = new View()
        {
            X = Pos.Center(),
            Y = Pos.AnchorEnd(5),
            Width = 89,
            Height = 5,
            CanFocus = true,
        };
        if (UseTransparentViewport)
            dockBar.ViewportSettings |= ViewportSettings.Transparent;
        dockBar.ColorScheme = BalatroTheme.Title; // Transparent scheme
        Add(dockBar);

        // Buttons inside dock: All use DynamicFocusHeight for TAB navigation visual feedback
        // When focused = full height, when not focused = half-block (shorter)
        // Order: SEARCH, DESIGNER, EXIT, CONFIG, HOST API
        // Hotkeys: S, D, X, C, H (use underscore notation for hotkey)
        var btnSearch = new MenuButton("_SEARCH", BalatroTheme.GreenButton)
        {
            X = 1,
            Y = 1,
            Width = 12,
            Height = 3,
            DynamicFocusHeight = true,
        };
        btnSearch.Accept += (s, e) => ShowFilterSelect();
        dockBar.Add(btnSearch);

        var btnDesigner = new MenuButton("_DESIGNER", BalatroTheme.BlueButton)
        {
            X = 14,
            Y = 1,
            Width = 12,
            Height = 3,
            DynamicFocusHeight = true,
        };
        btnDesigner.Accept += (s, e) =>
        {
            var editor = new JamlEditorWindow();
            MotelyTUI.ShowWindow(editor);
        };
        dockBar.Add(btnDesigner);

        var btnExit = new MenuButton("E_XIT", BalatroTheme.ModalButton)
        {
            X = 27,
            Y = 1,
            Width = 8,
            Height = 3,
            DynamicFocusHeight = true,
        };
        btnExit.Accept += (s, e) => Application.RequestStop(Application.Top);
        dockBar.Add(btnExit);

        var btnConfig = new MenuButton("_CONFIG", BalatroTheme.OrangeButton)
        {
            X = 36,
            Y = 1,
            Width = 8,
            Height = 3,
            DynamicFocusHeight = true,
        };
        btnConfig.Accept += (s, e) => MotelyTUI.ShowWindow(new SettingsWindow());
        dockBar.Add(btnConfig);


        // RESULTS - browse the DuckLake results store
        var btnResults = new MenuButton("_RESULTS", BalatroTheme.OrangeButton)
        {
            X = 71,
            Y = 1,
            Width = 12,
            Height = 3,
            DynamicFocusHeight = true,
        };
        btnResults.Accept += (s, e) => MotelyTUI.ShowWindow(new ResultsBrowserWindow());
        dockBar.Add(btnResults);

        // Set focus to SEARCH
        btnSearch.SetFocus();

        // Global hotkeys (S, D, X, C, H) and ESC
        KeyDown += (sender, e) =>
        {
            switch (e.KeyCode)
            {
                case KeyCode.S:
                    btnSearch.SetFocus();
                    ShowFilterSelect();
                    e.Handled = true;
                    break;
                case KeyCode.D:
                    btnDesigner.SetFocus();
                    var editor = new JamlEditorWindow();
                    MotelyTUI.ShowWindow(editor);
                    e.Handled = true;
                    break;
                case KeyCode.X:
                    Application.RequestStop(Application.Top);
                    e.Handled = true;
                    break;
                case KeyCode.C:
                    btnConfig.SetFocus();
                    MotelyTUI.ShowWindow(new SettingsWindow());
                    e.Handled = true;
                    break;
                case KeyCode.R:
                    btnResults.SetFocus();
                    MotelyTUI.ShowWindow(new ResultsBrowserWindow());
                    e.Handled = true;
                    break;
                case KeyCode.Esc:
                    Application.RequestStop(Application.Top);
                    e.Handled = true;
                    break;
            }
        };
    }

    private void ShowFilterSelect()
    {
        var filters = FilterLibrary.DiscoverLocalFilters();

        if (filters.Count == 0)
        {
            ShowErrorDialog(
                "No Filters Found",
                "No filter files found in JamlFilters/"
            );
            return;
        }

        var dialog = new Dialog()
        {
            Title = "Select Filter",
            Width = 60,
            Height = 20,
        };
        dialog.ColorScheme = BalatroTheme.Window;

        var instructionLabel = new Label()
        {
            X = Pos.Center(),
            Y = 1,
            Text = "Choose a filter and press ENTER to search:",
            TextAlignment = Alignment.Center,
        };
        dialog.Add(instructionLabel);

        var filterStrings = filters.Select(static filter => filter.DisplayName).ToArray();
        var filterList = new ListView()
        {
            X = 1,
            Y = 3,
            Width = Dim.Fill()! - 2,
            Height = Dim.Fill()! - 7,
            CanFocus = true,
        };
        filterList.ColorScheme = new ColorScheme()
        {
            Normal = new Attribute(BalatroTheme.White, BalatroTheme.DarkGrey),
            Focus = new Attribute(BalatroTheme.White, BalatroTheme.Blue),
            HotNormal = new Attribute(BalatroTheme.White, BalatroTheme.DarkGrey),
            HotFocus = new Attribute(BalatroTheme.White, BalatroTheme.Blue),
        };
        filterList.SetSource(new ObservableCollection<string>(filterStrings));
        filterList.SelectedItem = 0; // Select first item by default

        // Helper to start search
        void StartSearch()
        {
            var selectedIndex = filterList.SelectedItem;
            if (selectedIndex >= 0 && selectedIndex < filters.Count)
            {
                var selected = filters[selectedIndex];
                Application.RequestStop(dialog);

                var searchWindow = new SearchWindow(
                    selected.FullPath,
                    TuiSettings.DefaultSource,
                    TuiSettings.DefaultSink
                );
                MotelyTUI.ShowWindow(searchWindow);
            }
        }

        // Handle Enter key for selection
        filterList.KeyDown += (sender, e) =>
        {
            if (e.KeyCode == KeyCode.Enter)
            {
                StartSearch();
                e.Handled = true;
            }
        };

        // Handle mouse double-click
        filterList.Accepting += (sender, e) => StartSearch();

        dialog.Add(filterList);

        // Start Search button
        var searchBtn = new CleanButton()
        {
            X = 1,
            Y = Pos.AnchorEnd(3),
            Text = "Start Search",
            Width = Dim.Fill()! - 2,
            TextAlignment = Alignment.Center,
        };
        searchBtn.ColorScheme = BalatroTheme.GreenButton;
        searchBtn.Accept += (s, e) => StartSearch();
        dialog.Add(searchBtn);

        var cancelBtn = new CleanButton()
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Text = "Back",
            Width = Dim.Fill()! - 2,
            TextAlignment = Alignment.Center,
        };
        cancelBtn.ColorScheme = BalatroTheme.BackButton;
        cancelBtn.Accept += (s, e) => Application.RequestStop(dialog);
        dialog.Add(cancelBtn);

        filterList.SetFocus();
        Application.Run(dialog);
    }

    private void ShowSettingsModal()
    {
        var dialog = new Dialog()
        {
            Title = "Settings",
            Width = 50,
            Height = 14,
        };
        dialog.ColorScheme = BalatroTheme.Window;

        var btnSearchSettings = new CleanButton()
        {
            X = Pos.Center(),
            Y = 2,
            Text = " Seed Search Settings ",
            Width = 30,
            TextAlignment = Alignment.Center,
        };
        btnSearchSettings.ColorScheme = BalatroTheme.ModalButton;
        btnSearchSettings.Accept += (s, e) => ShowSearchSettings();
        dialog.Add(btnSearchSettings);

        var btnServerSettings = new CleanButton()
        {
            X = Pos.Center(),
            Y = 4,
            Text = " Server Host Settings ",
            Width = 30,
            TextAlignment = Alignment.Center,
        };
        btnServerSettings.ColorScheme = BalatroTheme.ModalButton;
        btnServerSettings.Accept += (s, e) => ShowServerSettings();
        dialog.Add(btnServerSettings);

        var btnCredits = new CleanButton()
        {
            X = Pos.Center(),
            Y = 6,
            Text = " Credits ",
            Width = 30,
            TextAlignment = Alignment.Center,
        };
        btnCredits.ColorScheme = BalatroTheme.ModalButton;
        btnCredits.Accept += (s, e) => ShowCredits();
        dialog.Add(btnCredits);

        var backBtn = new CleanButton()
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Text = "Back",
            Width = Dim.Fill()! - 2,
            TextAlignment = Alignment.Center,
        };
        backBtn.ColorScheme = BalatroTheme.BackButton;
        backBtn.Accept += (s, e) => Application.RequestStop(dialog);
        dialog.Add(backBtn);

        btnSearchSettings.SetFocus();
        Application.Run(dialog);
    }

    private void ShowSearchSettings()
    {
        var dialog = new Dialog()
        {
            Title = "Seed Search Settings",
            Width = 55,
            Height = 16,
        };
        dialog.ColorScheme = BalatroTheme.Window;

        // CPU Threads
        var threadsLabel = new Label()
        {
            X = 2,
            Y = 2,
            Text = "CPU Threads:",
        };
        dialog.Add(threadsLabel);

        var threadsField = new TextField()
        {
            X = Pos.Right(threadsLabel) + 2,
            Y = 2,
            Width = 10,
            Text = TuiSettings.ThreadCount.ToString(),
        };
        dialog.Add(threadsField);

        // Batch Size
        var batchLabel = new Label()
        {
            X = 2,
            Y = 4,
            Text = "Batch Size (1-7):",
        };
        dialog.Add(batchLabel);

        var batchField = new TextField()
        {
            X = Pos.Right(batchLabel) + 2,
            Y = 4,
            Width = 10,
            Text = TuiSettings.BatchCharacterCount.ToString(),
        };
        dialog.Add(batchField);

        var sourceLabel = new Label()
        {
            X = 2,
            Y = 6,
            Text = "Default Source:",
        };
        dialog.Add(sourceLabel);

        var sourceField = new TextField()
        {
            X = Pos.Right(sourceLabel) + 2,
            Y = 6,
            Width = 24,
            Text = TuiSettings.DefaultSource,
        };
        dialog.Add(sourceField);

        var sinkLabel = new Label()
        {
            X = 2,
            Y = 8,
            Text = "Default Sink:",
        };
        dialog.Add(sinkLabel);

        var sinkField = new TextField()
        {
            X = Pos.Right(sinkLabel) + 2,
            Y = 8,
            Width = 24,
            Text = TuiSettings.DefaultSink,
        };
        dialog.Add(sinkField);

        // Hidden secret button - invisible until focused
        var secretBtn = new CleanButton()
        {
            X = Pos.Center() - 18,
            Y = 11,
            Text = "       ",
            Width = 9,
        };
        secretBtn.ColorScheme = new ColorScheme()
        {
            Normal = new Attribute(BalatroTheme.ModalGrey, BalatroTheme.ModalGrey),
            Focus = new Attribute(BalatroTheme.White, BalatroTheme.DarkPurple),
            HotNormal = new Attribute(BalatroTheme.ModalGrey, BalatroTheme.ModalGrey),
            HotFocus = new Attribute(BalatroTheme.White, BalatroTheme.DarkPurple),
        };
        secretBtn.Accept += (s, e) => ShowSecretDialog();
        dialog.Add(secretBtn);

        var saveBtn = new CleanButton()
        {
            X = Pos.Center() - 5,
            Y = 11,
            Text = " Save ",
        };
        saveBtn.ColorScheme = BalatroTheme.GreenButton;
        saveBtn.Accept += (s, e) =>
        {
            if (int.TryParse(threadsField.Text, out int threads) && threads > 0)
                TuiSettings.ThreadCount = threads;
            if (int.TryParse(batchField.Text, out int batch) && batch >= 1 && batch <= 7)
                TuiSettings.BatchCharacterCount = batch;
            TuiSettings.DefaultSource = sourceField.Text?.ToString() ?? string.Empty;
            TuiSettings.DefaultSink = sinkField.Text?.ToString() ?? string.Empty;

            TuiSettings.Save();
            Application.RequestStop(dialog);
        };
        dialog.Add(saveBtn);

        var cancelBtn = new CleanButton()
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Text = "Back",
            Width = Dim.Fill()! - 2,
            TextAlignment = Alignment.Center,
        };
        cancelBtn.ColorScheme = BalatroTheme.BackButton;
        cancelBtn.Accept += (s, e) => Application.RequestStop(dialog);
        dialog.Add(cancelBtn);

        threadsField.SetFocus();
        Application.Run(dialog);
    }

    private void ShowServerSettings()
    {
        var dialog = new Dialog()
        {
            Title = "Server Host Settings",
            Width = 55,
            Height = 14,
        };
        dialog.ColorScheme = BalatroTheme.Window;

        // Host
        var hostLabel = new Label()
        {
            X = 2,
            Y = 2,
            Text = "Hostname:",
        };
        dialog.Add(hostLabel);

        var hostField = new TextField()
        {
            X = Pos.Right(hostLabel) + 2,
            Y = 2,
            Width = 25,
            Text = TuiSettings.ApiServerHost,
        };
        dialog.Add(hostField);

        // Port
        var portLabel = new Label()
        {
            X = 2,
            Y = 4,
            Text = "Port:",
        };
        dialog.Add(portLabel);

        var portField = new TextField()
        {
            X = Pos.Right(portLabel) + 2,
            Y = 4,
            Width = 10,
            Text = TuiSettings.ApiServerPort.ToString(),
        };
        dialog.Add(portField);

        var saveBtn = new CleanButton()
        {
            X = Pos.Center() - 10,
            Y = 8,
            Text = " Save ",
        };
        saveBtn.ColorScheme = BalatroTheme.GreenButton;
        saveBtn.Accept += (s, e) =>
        {
            TuiSettings.ApiServerHost = hostField.Text ?? "localhost";
            if (int.TryParse(portField.Text, out int port) && port > 0)
                TuiSettings.ApiServerPort = port;

            TuiSettings.Save();
            Application.RequestStop(dialog);
        };
        dialog.Add(saveBtn);

        var cancelBtn = new CleanButton()
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Text = "Back",
            Width = Dim.Fill()! - 2,
            TextAlignment = Alignment.Center,
        };
        cancelBtn.ColorScheme = BalatroTheme.BackButton;
        cancelBtn.Accept += (s, e) => Application.RequestStop(dialog);
        dialog.Add(cancelBtn);

        hostField.SetFocus();
        Application.Run(dialog);
    }

    private void ShowCredits()
    {
        var dialog = new Dialog()
        {
            Title = "Motely Credits",
            Width = 62,
            Height = 22,
        };
        dialog.ColorScheme = BalatroTheme.Window;

        var credits = new Label()
        {
            X = 1,
            Y = 1,
            Text =
                @"
 ███╗   ███╗ ██████╗ ████████╗███████╗██╗  ██╗   ██╗
 ████╗ ████║██╔═══██╗╚══██╔══╝██╔════╝██║  ╚██╗ ██╔╝
 ██╔████╔██║██║   ██║   ██║   █████╗  ██║   ╚████╔╝ 
 ██║╚██╔╝██║██║   ██║   ██║   ██╔══╝  ██║    ╚██╔╝  
 ██║ ╚═╝ ██║╚██████╔╝   ██║   ███████╗███████╗██║   
 ╚═╝     ╚═╝ ╚═════╝    ╚═╝   ╚══════╝╚══════╝╚═╝   
    Balatro Seed Searcher - Powered by CPU SIMD     

        Created/Adapted by: @OptimusPi              
        Original Motely by: @tacodiva               

    Not affiliated with LocalThunk or PlayStack.    
    Made with ♥️for the Balatro Community.       

",
        };
        dialog.Add(credits);

        var backBtn = new CleanButton()
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Text = "Back",
            Width = Dim.Fill()! - 2,
            TextAlignment = Alignment.Center,
        };
        backBtn.ColorScheme = BalatroTheme.BackButton;
        backBtn.Accept += (s, e) => Application.RequestStop(dialog);
        dialog.Add(backBtn);

        backBtn.SetFocus();
        Application.Run(dialog);
    }

    private static void ShowSecretDialog()
    {
        var dialog = new Dialog()
        {
            Title = "????",
            Width = 50,
            Height = 14,
        };
        dialog.ColorScheme = BalatroTheme.Window;

        // The mystery message that gets revealed
        var messageLabel = new Label()
        {
            X = Pos.Center(),
            Y = 3,
            Text = "??????? ????? ????",
            TextAlignment = Alignment.Center,
        };
        dialog.Add(messageLabel);

        var backBtn = new CleanButton()
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Text = "Back",
            Width = Dim.Fill()! - 2,
            TextAlignment = Alignment.Center,
        };
        backBtn.ColorScheme = BalatroTheme.BackButton;
        backBtn.Accept += (s, e) => Application.RequestStop(dialog);
        dialog.Add(backBtn);

        // Run the scratch-off animation
        string mystery = "??????? ????? ????";
        string reveal = "pifreak loves you!";
        char[] current = mystery.ToCharArray();
        var random = new Random(314); // pifreak's lucky number!

        // Use a timeout to animate the reveal
        int iteration = 0;
        Application.AddTimeout(
            TimeSpan.FromMilliseconds(10),
            () =>
            {
                if (iteration < 314)
                {
                    // Randomly reveal a character
                    if (iteration < 18)
                    {
                        // Pick a random unrevealed position
                        var unrevealed = new System.Collections.Generic.List<int>();
                        for (int i = 0; i < current.Length; i++)
                        {
                            if (current[i] == '?')
                                unrevealed.Add(i);
                        }
                        if (unrevealed.Count > 0)
                        {
                            int idx = unrevealed[random.Next(unrevealed.Count)];
                            current[idx] = reveal[idx];
                            messageLabel.Text = new string(current);
                        }
                    }
                    iteration++;
                    return true; // Continue timer
                }
                else
                {
                    // Final snap to complete message
                    messageLabel.Text = reveal;
                    dialog.Title = "pifreak loves you!";
                    dialog.SetNeedsDraw();
                    return false; // Stop timer
                }
            }
        );

        Application.Run(dialog);
    }

    private static void ShowErrorDialog(string title, string message)
    {
        var dialog = new Dialog()
        {
            Title = title,
            Width = Math.Min(70, message.Length + 10),
            Height = 10,
        };
        dialog.ColorScheme = BalatroTheme.Window;

        var label = new Label()
        {
            X = Pos.Center(),
            Y = 2,
            Text = message,
            TextAlignment = Alignment.Center,
        };
        label.ColorScheme = BalatroTheme.ErrorText;
        dialog.Add(label);

        var okBtn = new CleanButton()
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Text = "Back",
            Width = Dim.Fill()! - 2,
            TextAlignment = Alignment.Center,
        };
        okBtn.ColorScheme = BalatroTheme.BackButton;
        okBtn.Accept += (s, e) => Application.RequestStop(dialog);
        dialog.Add(okBtn);

        Application.Run(dialog);
    }
}
