using System;
using System.IO;
using System.Text.Json;
using Terminal.Gui;

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
        Width = 70;
        Height = 20;
        CanFocus = true;
        SetScheme(BalatroTheme.Window);

        // Title
        var titleLabel = new Label()
        {
            X = Pos.Center(),
            Y = 1,
            Text = "SETTINGS",
            TextAlignment = Alignment.Center,
        };
        titleLabel.SetScheme(BalatroTheme.Title);
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
        threadHint.SetScheme(BalatroTheme.Hint);
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
        batchHint.SetScheme(BalatroTheme.Hint);
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
        portHint.SetScheme(BalatroTheme.Hint);
        Add(portHint);

        // Secret option - blends into background until focused!
        var secretButton = new CleanButton()
        {
            X = 2,
            Y = 15,
            Text = "          ",
            Width = 12,
        };
        secretButton.SetScheme(new Scheme()
        {
            // Invisible until focused - blends with window background
            Normal = new Attribute(BalatroTheme.ModalGrey, BalatroTheme.ModalGrey),
            Focus = new Attribute(BalatroTheme.White, BalatroTheme.Purple),
            HotNormal = new Attribute(BalatroTheme.ModalGrey, BalatroTheme.ModalGrey),
            HotFocus = new Attribute(BalatroTheme.White, BalatroTheme.DarkPurple),
        });
        secretButton.Accept += (s, e) => ShowSecretDialog();
        Add(secretButton);

        // Save button (blue like PLAY) - above Back
        var saveButton = new CleanButton()
        {
            X = Pos.Center() - 6,
            Y = Pos.AnchorEnd(3),
            Text = " _Save ",
            Width = 12,
        };
        saveButton.SetScheme(BalatroTheme.BlueButton);
        saveButton.Accept += (s, e) => SaveSettings();
        Add(saveButton);

        // Back button (orange) - FULL WIDTH at very bottom
        var cancelButton = new CleanButton()
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Text = "Back",
            Width = Dim.Fill() - 2,
            TextAlignment = Alignment.Center,
        };
        cancelButton.SetScheme(BalatroTheme.BackButton);
        cancelButton.Accept += (s, e) => App?.RequestStop();
        Add(cancelButton);

        // ESC key handler
        KeyDown += (sender, e) =>
        {
            if (e.KeyCode == KeyCode.Esc)
            {
                App?.RequestStop();
                e.Handled = true;
            }
        };

        _threadCountField.SetFocus();
    }

    private void SaveSettings()
    {
        try
        {
            // Validate and save thread count
            if (int.TryParse(_threadCountField.Text.ToString(), out int threadCount))
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
            else
            {
                ShowErrorDialog("Invalid Thread Count", "Thread count must be a valid number");
                return;
            }

            // Validate and save batch character count
            if (int.TryParse(_batchCharCountField.Text.ToString(), out int batchCharCount))
            {
                if (batchCharCount < 1 || batchCharCount > 7)
                {
                    ShowErrorDialog(
                        "Invalid Batch Character Count",
                        "Batch character count must be between 1 and 7"
                    );
                    return;
                }
                TuiSettings.BatchCharacterCount = batchCharCount;
            }
            else
            {
                ShowErrorDialog(
                    "Invalid Batch Character Count",
                    "Batch character count must be a valid number"
                );
                return;
            }

            // Validate and save API host
            var host = _apiHostField.Text.ToString();
            if (string.IsNullOrWhiteSpace(host))
            {
                ShowErrorDialog("Invalid API Host", "API host cannot be empty");
                return;
            }
            TuiSettings.ApiServerHost = host;

            // Validate and save API port
            if (int.TryParse(_apiPortField.Text.ToString(), out int port))
            {
                if (port < 1 || port > 65535)
                {
                    ShowErrorDialog("Invalid API Port", "API port must be between 1 and 65535");
                    return;
                }
                TuiSettings.ApiServerPort = port;
            }
            else
            {
                ShowErrorDialog("Invalid API Port", "API port must be a valid number");
                return;
            }

            // Save settings to tui.json
            TuiSettings.Save();

            // Success - close window
            App?.RequestStop();
        }
        catch (Exception ex)
        {
            ShowErrorDialog("Error Saving Settings", ex.Message);
        }
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

    private static void ShowSecretDialog()
    {
        var dialog = new Dialog()
        {
            Title = "Jimbo is proud of you!",
            Width = 50,
            Height = 14,
        };
        dialog.SetScheme(BalatroTheme.Window);

        var jimboLabel = new Label()
        {
            X = Pos.Center(),
            Y = 1,
            Text = "  .-\"\"\"-.\n /        \\\n|  O    O  |\n|    __    |\n \\  \\__/  /\n  '------'",
            TextAlignment = Alignment.Center,
        };
        dialog.Add(jimboLabel);

        var crudeBtn = new CleanButton()
        {
            X = Pos.Center(),
            Y = 8,
            Text = TuiSettings.CrudeSeedsEnabled ? " [X] Crude Seeds " : " [ ] Crude Seeds ",
        };
        crudeBtn.SetScheme(BalatroTheme.GrayButton);
        crudeBtn.Accept += (s, e) =>
        {
            TuiSettings.CrudeSeedsEnabled = !TuiSettings.CrudeSeedsEnabled;
            crudeBtn.Text = TuiSettings.CrudeSeedsEnabled ? " [X] Crude Seeds " : " [ ] Crude Seeds ";
        };
        dialog.Add(crudeBtn);

        var closeBtn = new CleanButton()
        {
            X = Pos.Center(),
            Y = Pos.AnchorEnd(1),
            Text = " Back ",
        };
        closeBtn.SetScheme(BalatroTheme.BackButton);
        closeBtn.Accept += (s, e) => MotelyTUI.App?.RequestStop(dialog);
        dialog.Add(closeBtn);

        MotelyTUI.App?.Run(dialog);
    }
}
