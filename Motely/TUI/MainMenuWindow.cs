using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Terminal.Gui;

namespace Motely.TUI;

public class MainMenuWindow : View
{
    public MainMenuWindow()
    {
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;

        // Enable transparency so shader background shows through!
        ViewportSettings |= ViewportSettingsFlags.Transparent;
        SetScheme(BalatroTheme.Title); // Title has transparent background

        // Jimbo sprite - ADD FIRST so text layers on top!
        var jimboView = new JimboView() { Y = 1 };
        jimboView.X = Pos.AnchorEnd(jimboView.Frame.Width + 4);
        Add(jimboView);

        // Big logo - aligned left with padding, white text
        var logoLabel = new Label()
        {
            X = 4,
            Y = 2,
            Text = JimboArt.Logo,
        };
        logoLabel.SetScheme(BalatroTheme.Title);
        Add(logoLabel);

        // Subtitle under logo - JAML JOTD (Joke Of The Day)
        var subtitleLabel = new Label()
        {
            X = 4,
            Y = 9,
            Text = MotelyQuips.GetRandomJamlJotd(),
        };
        subtitleLabel.SetScheme(BalatroTheme.Title);
        Add(subtitleLabel);

        // ═══════════════════════════════════════════════════════════════
        // BUTTON DOCK AT BOTTOM - Transparent container with clean buttons
        // Layout: SEARCH(12) + DESIGNER(12) + EXIT(11) + SETTINGS(11) + SERVER(10) + gaps = 60
        // ═══════════════════════════════════════════════════════════════

        var dockBar = new View()
        {
            X = Pos.Center(),
            Y = Pos.AnchorEnd(5),
            Width = 62,
            Height = 5,
            CanFocus = true,
            ViewportSettings = ViewportSettingsFlags.Transparent, // No grey block - transparent!
        };
        dockBar.SetScheme(BalatroTheme.Title); // Transparent scheme
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
            var filterBuilder = new FilterBuilderWindow();
            App?.Run(filterBuilder);
        };
        dockBar.Add(btnDesigner);

        var btnExit = new MenuButton("E_XIT", BalatroTheme.ModalButton)
        {
            X = 27,
            Y = 1,
            Width = 10,
            Height = 3,
            DynamicFocusHeight = true,
        };
        btnExit.Accept += (s, e) => App?.RequestStop();
        dockBar.Add(btnExit);

        var btnConfig = new MenuButton("_CONFIG", BalatroTheme.BackButton)
        {
            X = 38,
            Y = 1,
            Width = 10,
            Height = 3,
            DynamicFocusHeight = true,
        };
        btnConfig.Accept += (s, e) => ShowSettingsModal();
        dockBar.Add(btnConfig);

        // HOST API on far right - purple
        var btnHostApi = new MenuButton("_HOST API", BalatroTheme.PurpleButton)
        {
            X = 49,
            Y = 1,
            Width = 12,
            Height = 3,
            DynamicFocusHeight = true,
        };
        btnHostApi.Accept += (s, e) =>
        {
            MotelyTUI.ShaderBackground?.Stop();
            var serverWindow = new ApiServerWindow(
                TuiSettings.ApiServerHost,
                TuiSettings.ApiServerPort
            );
            App?.Run(serverWindow);
            MotelyTUI.ShaderBackground?.Start();
        };
        dockBar.Add(btnHostApi);

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
                    var fb = new FilterBuilderWindow();
                    App?.Run(fb);
                    e.Handled = true;
                    break;
                case KeyCode.X:
                    App?.RequestStop();
                    e.Handled = true;
                    break;
                case KeyCode.C:
                    btnConfig.SetFocus();
                    ShowSettingsModal();
                    e.Handled = true;
                    break;
                case KeyCode.H:
                    btnHostApi.SetFocus();
                    MotelyTUI.ShaderBackground?.Stop();
                    var srv = new ApiServerWindow(
                        TuiSettings.ApiServerHost,
                        TuiSettings.ApiServerPort
                    );
                    App?.Run(srv);
                    MotelyTUI.ShaderBackground?.Start();
                    e.Handled = true;
                    break;
                case KeyCode.Esc:
                    App?.RequestStop();
                    e.Handled = true;
                    break;
            }
        };
    }

    private void ShowFilterSelect()
    {
        var filters = new System.Collections.Generic.List<(
            string name,
            string format,
            string fullPath
        )>();

        // Scan for available filters
        var currentDir = Directory.GetCurrentDirectory();

        if (Directory.Exists(Path.Combine(currentDir, "JsonItemFilters")))
        {
            var jsonFiles = Directory.GetFiles(
                Path.Combine(currentDir, "JsonItemFilters"),
                "*.json"
            );
            foreach (var file in jsonFiles)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                filters.Add((name, "json", file));
            }
        }

        // Check for JAML filters (new format)
        if (Directory.Exists(Path.Combine(currentDir, "JamlFilters")))
        {
            var jamlFiles = Directory.GetFiles(
                Path.Combine(currentDir, "JamlFilters"),
                "*.jaml"
            );
            foreach (var file in jamlFiles)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                filters.Add((name, "jaml", file));
            }
        }

        if (filters.Count == 0)
        {
            ShowErrorDialog(
                "No Filters Found",
                "No filter files found in JsonItemFilters/ or JamlFilters/"
            );
            return;
        }

        var dialog = new Dialog()
        {
            Title = "Select Filter",
            Width = 60,
            Height = 20,
        };
        dialog.SetScheme(BalatroTheme.Window);

        var instructionLabel = new Label()
        {
            X = Pos.Center(),
            Y = 1,
            Text = "Choose a filter and press ENTER to search:",
            TextAlignment = Alignment.Center,
        };
        dialog.Add(instructionLabel);

        var filterStrings = filters.Select(f => $"{f.name}.{f.format}").ToArray();
        var filterList = new ListView()
        {
            X = 1,
            Y = 3,
            Width = Dim.Fill() - 2,
            Height = Dim.Fill() - 7,
            AllowsMarking = false,
            CanFocus = true,
        };
        filterList.SetScheme(
            new Scheme()
            {
                Normal = new Attribute(BalatroTheme.White, BalatroTheme.DarkGrey),
                Focus = new Attribute(BalatroTheme.White, BalatroTheme.Blue),
                HotNormal = new Attribute(BalatroTheme.White, BalatroTheme.DarkGrey),
                HotFocus = new Attribute(BalatroTheme.White, BalatroTheme.Blue),
            }
        );
        filterList.SetSource(new ObservableCollection<string>(filterStrings));
        filterList.SelectedItem = 0; // Select first item by default

        // Helper to start search
        void StartSearch()
        {
            var selectedIndex = filterList.SelectedItem ?? 0;
            if (selectedIndex >= 0 && selectedIndex < filters.Count)
            {
                var selected = filters[selectedIndex];
                App?.RequestStop(dialog);

                var searchWindow = new SearchWindow(selected.fullPath, selected.format);
                App?.Run(searchWindow);
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
        filterList.OpenSelectedItem += (sender, e) => StartSearch();

        dialog.Add(filterList);

        // Start Search button
        var searchBtn = new CleanButton()
        {
            X = 1,
            Y = Pos.AnchorEnd(3),
            Text = "Start Search",
            Width = Dim.Fill() - 2,
            TextAlignment = Alignment.Center,
        };
        searchBtn.SetScheme(BalatroTheme.GreenButton);
        searchBtn.Accept += (s, e) => StartSearch();
        dialog.Add(searchBtn);

        var cancelBtn = new CleanButton()
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Text = "Back",
            Width = Dim.Fill() - 2,
            TextAlignment = Alignment.Center,
        };
        cancelBtn.SetScheme(BalatroTheme.BackButton);
        cancelBtn.Accept += (s, e) => App?.RequestStop(dialog);
        dialog.Add(cancelBtn);

        filterList.SetFocus();
        App?.Run(dialog);
    }

    private void ShowSettingsModal()
    {
        var dialog = new Dialog()
        {
            Title = "Settings",
            Width = 50,
            Height = 14,
        };
        dialog.SetScheme(BalatroTheme.Window);

        var btnSearchSettings = new CleanButton()
        {
            X = Pos.Center(),
            Y = 2,
            Text = " Seed Search Settings ",
            Width = 30,
            TextAlignment = Alignment.Center,
        };
        btnSearchSettings.SetScheme(BalatroTheme.ModalButton);
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
        btnServerSettings.SetScheme(BalatroTheme.ModalButton);
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
        btnCredits.SetScheme(BalatroTheme.ModalButton);
        btnCredits.Accept += (s, e) => ShowCredits();
        dialog.Add(btnCredits);

        var backBtn = new CleanButton()
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Text = "Back",
            Width = Dim.Fill() - 2,
            TextAlignment = Alignment.Center,
        };
        backBtn.SetScheme(BalatroTheme.BackButton);
        backBtn.Accept += (s, e) => App?.RequestStop(dialog);
        dialog.Add(backBtn);

        btnSearchSettings.SetFocus();
        App?.Run(dialog);
    }

    private void ShowSearchSettings()
    {
        var dialog = new Dialog()
        {
            Title = "Seed Search Settings",
            Width = 55,
            Height = 12,
        };
        dialog.SetScheme(BalatroTheme.Window);

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

        // Hidden secret button - invisible until focused
        var secretBtn = new CleanButton()
        {
            X = Pos.Center() - 18,
            Y = 7,
            Text = "       ",
            Width = 9,
        };
        secretBtn.SetScheme(
            new Scheme()
            {
                Normal = new Attribute(BalatroTheme.ModalGrey, BalatroTheme.ModalGrey),
                Focus = new Attribute(BalatroTheme.White, BalatroTheme.DarkPurple),
                HotNormal = new Attribute(BalatroTheme.ModalGrey, BalatroTheme.ModalGrey),
                HotFocus = new Attribute(BalatroTheme.White, BalatroTheme.DarkPurple),
            }
        );
        secretBtn.Accept += (s, e) => ShowSecretDialog();
        dialog.Add(secretBtn);

        var saveBtn = new CleanButton()
        {
            X = Pos.Center() - 5,
            Y = 7,
            Text = " Save ",
        };
        saveBtn.SetScheme(BalatroTheme.GreenButton);
        saveBtn.Accept += (s, e) =>
        {
            if (int.TryParse(threadsField.Text, out int threads) && threads > 0)
                TuiSettings.ThreadCount = threads;
            if (int.TryParse(batchField.Text, out int batch) && batch >= 1 && batch <= 7)
                TuiSettings.BatchCharacterCount = batch;

            TuiSettings.Save();
            App?.RequestStop(dialog);
        };
        dialog.Add(saveBtn);

        var cancelBtn = new CleanButton()
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Text = "Back",
            Width = Dim.Fill() - 2,
            TextAlignment = Alignment.Center,
        };
        cancelBtn.SetScheme(BalatroTheme.BackButton);
        cancelBtn.Accept += (s, e) => App?.RequestStop(dialog);
        dialog.Add(cancelBtn);

        threadsField.SetFocus();
        App?.Run(dialog);
    }

    private void ShowServerSettings()
    {
        var dialog = new Dialog()
        {
            Title = "Server Host Settings",
            Width = 55,
            Height = 14,
        };
        dialog.SetScheme(BalatroTheme.Window);

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
        saveBtn.SetScheme(BalatroTheme.GreenButton);
        saveBtn.Accept += (s, e) =>
        {
            TuiSettings.ApiServerHost = hostField.Text ?? "localhost";
            if (int.TryParse(portField.Text, out int port) && port > 0)
                TuiSettings.ApiServerPort = port;

            TuiSettings.Save();
            App?.RequestStop(dialog);
        };
        dialog.Add(saveBtn);

        var cancelBtn = new CleanButton()
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Text = "Back",
            Width = Dim.Fill() - 2,
            TextAlignment = Alignment.Center,
        };
        cancelBtn.SetScheme(BalatroTheme.BackButton);
        cancelBtn.Accept += (s, e) => App?.RequestStop(dialog);
        dialog.Add(cancelBtn);

        hostField.SetFocus();
        App?.Run(dialog);
    }

    private void ShowCredits()
    {
        var dialog = new Dialog()
        {
            Title = "Motely Credits",
            Width = 62,
            Height = 22,
        };
        dialog.SetScheme(BalatroTheme.Window);

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
            Width = Dim.Fill() - 2,
            TextAlignment = Alignment.Center,
        };
        backBtn.SetScheme(BalatroTheme.BackButton);
        backBtn.Accept += (s, e) => App?.RequestStop(dialog);
        dialog.Add(backBtn);

        backBtn.SetFocus();
        App?.Run(dialog);
    }

    private static void ShowSecretDialog()
    {
        var dialog = new Dialog()
        {
            Title = "????",
            Width = 50,
            Height = 14,
        };
        dialog.SetScheme(BalatroTheme.Window);

        // The mystery message that gets revealed
        var messageLabel = new Label()
        {
            X = Pos.Center(),
            Y = 3,
            Text = "??????? ????? ????",
            TextAlignment = Alignment.Center,
        };
        dialog.Add(messageLabel);

        // Crude Seeds toggle - starts hidden until animation completes
        var crudeBtn = new CleanButton()
        {
            X = Pos.Center(),
            Y = 7,
            Text = TuiSettings.CrudeSeedsEnabled
                ? " [X] Crude Seeds (NSFW) "
                : " [ ] Crude Seeds (NSFW) ",
            Width = 28,
            Visible = false,
        };
        crudeBtn.SetScheme(BalatroTheme.GrayButton);
        crudeBtn.Accept += (s, e) =>
        {
            TuiSettings.CrudeSeedsEnabled = !TuiSettings.CrudeSeedsEnabled;
            crudeBtn.Text = TuiSettings.CrudeSeedsEnabled
                ? " [X] Crude Seeds (NSFW) "
                : " [ ] Crude Seeds (NSFW) ";
        };
        dialog.Add(crudeBtn);

        var backBtn = new CleanButton()
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Text = "Back",
            Width = Dim.Fill() - 2,
            TextAlignment = Alignment.Center,
        };
        backBtn.SetScheme(BalatroTheme.BackButton);
        backBtn.Accept += (s, e) => MotelyTUI.App?.RequestStop(dialog);
        dialog.Add(backBtn);

        // Run the scratch-off animation
        string mystery = "??????? ????? ????";
        string reveal = "pifreak loves you!";
        char[] current = mystery.ToCharArray();
        var random = new Random(314); // pifreak's lucky number!

        // Use a timeout to animate the reveal
        int iteration = 0;
        MotelyTUI.App?.AddTimeout(
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
                    crudeBtn.Visible = true;
                    crudeBtn.SetFocus();
                    return false; // Stop timer
                }
            }
        );

        MotelyTUI.App?.Run(dialog);
    }

    private static bool ShowConfirmDialog(string title, string message)
    {
        var dialog = new Dialog()
        {
            Title = title,
            Width = Math.Min(60, message.Length + 10),
            Height = 9,
        };
        dialog.SetScheme(BalatroTheme.Window);

        var label = new Label()
        {
            X = Pos.Center(),
            Y = 2,
            Text = message,
            TextAlignment = Alignment.Center,
        };
        dialog.Add(label);

        bool result = false;
        var yesBtn = new CleanButton() { Text = " Yes " };
        yesBtn.SetScheme(BalatroTheme.ModalButton);
        yesBtn.Accept += (s, e) =>
        {
            result = true;
            MotelyTUI.App?.RequestStop(dialog);
        };

        var noBtn = new CleanButton()
        {
            X = Pos.Right(yesBtn) + 2,
            Y = Pos.AnchorEnd(1),
            Text = " No ",
        };
        noBtn.SetScheme(BalatroTheme.BackButton);
        noBtn.Accept += (s, e) =>
        {
            result = false;
            MotelyTUI.App?.RequestStop(dialog);
        };

        yesBtn.X = 2;
        yesBtn.Y = Pos.AnchorEnd(1);
        dialog.Add(yesBtn);
        dialog.Add(noBtn);

        MotelyTUI.App?.Run(dialog);
        return result;
    }

    private static void ShowErrorDialog(string title, string message)
    {
        var dialog = new Dialog()
        {
            Title = title,
            Width = Math.Min(70, message.Length + 10),
            Height = 10,
        };
        dialog.SetScheme(BalatroTheme.Window);

        var label = new Label()
        {
            X = Pos.Center(),
            Y = 2,
            Text = message,
            TextAlignment = Alignment.Center,
        };
        label.SetScheme(BalatroTheme.ErrorText);
        dialog.Add(label);

        var okBtn = new CleanButton()
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Text = "Back",
            Width = Dim.Fill() - 2,
            TextAlignment = Alignment.Center,
        };
        okBtn.SetScheme(BalatroTheme.BackButton);
        okBtn.Accept += (s, e) => MotelyTUI.App?.RequestStop(dialog);
        dialog.Add(okBtn);

        MotelyTUI.App?.Run(dialog);
    }
}
