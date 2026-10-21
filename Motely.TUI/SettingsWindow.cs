namespace Motely.TUI;

public class SettingsWindow : Window
{
    private TextField _threadCountField;
    private TextField _batchCharCountField;
    private TextField _apiHostField;
    private TextField _apiPortField;

    public SettingsWindow()
    {
        Title = "Settings";
        X = Pos.Center();
        Y = Pos.Center();
        Width = 78;
        Height = 24;
        CanFocus = true;
        ColorScheme = BalatroTheme.Window;

        // Title
        var titleLabel = new Label()
        {
            X = Pos.Center(),
            Y = 1,
            Text = "SETTINGS",
            TextAlignment = Alignment.Center,
        };
        titleLabel.ColorScheme = BalatroTheme.Title;
        Add(titleLabel);

        // Thread Count
        var threadLabel = new Label()
        {
            X = 2,
            Y = 3,
            Text = "Thread Count:",
        };
        Add(threadLabel);

        _threadCountField = new TextField()
        {
            X = 2,
            Y = 4,
            Width = 20,
            Text = TuiSettings.ThreadCount.ToString(),
        };
        Add(_threadCountField);

        var threadHint = new Label()
        {
            X = 24,
            Y = 4,
            Text = $"(1-{Environment.ProcessorCount}, default: {Environment.ProcessorCount})",
        };
        threadHint.ColorScheme = BalatroTheme.Hint;
        Add(threadHint);

        // Batch Character Count
        var batchLabel = new Label()
        {
            X = 2,
            Y = 6,
            Text = "Batch Character Count:",
        };
        Add(batchLabel);

        _batchCharCountField = new TextField()
        {
            X = 2,
            Y = 7,
            Width = 20,
            Text = TuiSettings.BatchCharacterCount.ToString(),
        };
        Add(_batchCharCountField);

        var batchHint = new Label()
        {
            X = 24,
            Y = 7,
            Text = "(1-7, default: 2, recommended: 2-4)",
        };
        batchHint.ColorScheme = BalatroTheme.Hint;
        Add(batchHint);

        // API Server Host
        var hostLabel = new Label()
        {
            X = 2,
            Y = 9,
            Text = "API Server Host:",
        };
        Add(hostLabel);

        _apiHostField = new TextField()
        {
            X = 2,
            Y = 10,
            Width = 40,
            Text = TuiSettings.ApiServerHost,
        };
        Add(_apiHostField);

        // API Server Port
        var portLabel = new Label()
        {
            X = 2,
            Y = 12,
            Text = "API Server Port:",
        };
        Add(portLabel);

        _apiPortField = new TextField()
        {
            X = 2,
            Y = 13,
            Width = 20,
            Text = TuiSettings.ApiServerPort.ToString(),
        };
        Add(_apiPortField);

        var portHint = new Label()
        {
            X = 24,
            Y = 13,
            Text = "(1-65535, default: 3141)",
        };
        portHint.ColorScheme = BalatroTheme.Hint;
        Add(portHint);

        // Search Mode
        var modeLabel = new Label()
        {
            X = 2,
            Y = 15,
            Text = "Search Mode:",
        };
        Add(modeLabel);

        var modeRadio = new RadioGroup()
        {
            X = 2,
            Y = 16,
            RadioLabels = new string[]
            {
                "Sequential",
                "Random",
                "Palindrome",
                "Psychosis",
                "Keyword",
                "FileSource",
            },
            SelectedItem = (int)TuiSettings.SearchMode,
        };
        Add(modeRadio);

        // Keywords
        var keywordsLabel = new Label()
        {
            X = 30,
            Y = 15,
            Text = "Keywords:",
        };
        Add(keywordsLabel);

        var keywordsField = new TextField()
        {
            X = 30,
            Y = 16,
            Width = 20,
            Text = TuiSettings.Keywords,
        };
        Add(keywordsField);

        // Padding Chars
        var paddingLabel = new Label()
        {
            X = 30,
            Y = 18,
            Text = "Padding Chars:",
        };
        Add(paddingLabel);

        var paddingField = new TextField()
        {
            X = 30,
            Y = 19,
            Width = 20,
            Text = TuiSettings.PaddingChars,
        };
        Add(paddingField);

        // Data Lake Path (for Results Browser)
        var lakeLabel = new Label()
        {
            X = 2,
            Y = 18,
            Text = "Data Lake Path:",
        };
        Add(lakeLabel);

        var lakeField = new TextField()
        {
            X = 2,
            Y = 19,
            Width = 25,
            Text = TuiSettings.DataLakePath,
        };
        Add(lakeField);

        // Save button (blue like PLAY) - above Back
        var saveButton = new CleanButton()
        {
            X = Pos.Center() - 6,
            Y = Pos.AnchorEnd(3),
            Text = " _Save ",
            Width = 12,
        };
        saveButton.ColorScheme = BalatroTheme.BlueButton;
        saveButton.Accept += (s, e) =>
        {
            try
            {
                if (int.TryParse(_threadCountField.Text, out int threadCount))
                {
                    if (threadCount < 1 || threadCount > Environment.ProcessorCount)
                    {
                        ShowErrorDialog(
                            "Invalid Thread Count",
                            $"Thread count must be between 1 and {Environment.ProcessorCount}"
                        );
                        return;
                    }
                    TuiSettings.ThreadCount = threadCount;
                }

                if (int.TryParse(_batchCharCountField.Text, out int batchCount))
                {
                    if (batchCount < 1 || batchCount > 7)
                    {
                        ShowErrorDialog(
                            "Invalid Batch Count",
                            "Batch character count must be between 1 and 7"
                        );
                        return;
                    }
                    TuiSettings.BatchCharacterCount = batchCount;
                }

                TuiSettings.ApiServerHost = _apiHostField.Text?.ToString() ?? "localhost";

                if (int.TryParse(_apiPortField.Text, out int port))
                {
                    if (port < 1 || port > 65535)
                    {
                        ShowErrorDialog("Invalid Port", "Port must be between 1 and 65535");
                        return;
                    }
                    TuiSettings.ApiServerPort = port;
                }

                TuiSettings.SearchMode = (SearchMode)modeRadio.SelectedItem;
                TuiSettings.Keywords = keywordsField.Text?.ToString() ?? string.Empty;
                TuiSettings.PaddingChars = paddingField.Text?.ToString() ?? string.Empty;

                var lakeText = lakeField.Text?.ToString();
                TuiSettings.DataLakePath = string.IsNullOrWhiteSpace(lakeText) ? "seeds" : lakeText;

                TuiSettings.Save();
                Application.RequestStop(this);
            }
            catch (Exception ex)
            {
                ShowErrorDialog("Error Saving Settings", ex.Message);
            }
        };
        Add(saveButton);

        // Back button (orange) - FULL WIDTH at very bottom
        var cancelButton = new CleanButton()
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Text = "Back",
            Width = Dim.Fill()! - 2,
            TextAlignment = Alignment.Center,
        };
        cancelButton.ColorScheme = BalatroTheme.BackButton;
        cancelButton.Accept += (s, e) => Application.RequestStop(this);
        Add(cancelButton);

        // ESC key handler
        KeyDown += (sender, e) =>
        {
            if (e.KeyCode == KeyCode.Esc)
            {
                Application.RequestStop(this);
                e.Handled = true;
            }
        };

        _threadCountField.SetFocus();
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

    private static void ShowSecretDialog()
    {
        var dialog = new Dialog()
        {
            Title = "Jimbo is proud of you!",
            Width = 50,
            Height = 14,
        };
        dialog.ColorScheme = BalatroTheme.Window;

        var jimboLabel = new Label()
        {
            X = Pos.Center(),
            Y = 1,
            Text =
                "  .-\"\"\"-.\n /        \\\n|  O    O  |\n|    __    |\n \\  \\__/  /\n  '------'",
            TextAlignment = Alignment.Center,
        };
        dialog.Add(jimboLabel);

        var closeBtn = new CleanButton()
        {
            X = Pos.Center(),
            Y = Pos.AnchorEnd(1),
            Text = " Back ",
        };
        closeBtn.ColorScheme = BalatroTheme.BackButton;
        closeBtn.Accept += (s, e) => Application.RequestStop(dialog);
        dialog.Add(closeBtn);

        Application.Run(dialog);
    }
}
