using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Motely.Executors;
using Motely.Filters;
using Terminal.Gui;

namespace Motely.TUI;

public class SearchWindow : Window
{
    private TextView _outputView;
    private TableView _topResultsTable;
    private CleanButton _stopButton;
    private Label _statusLabel;
    private string _configName;
    private string _configFormat;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _searchRunning = false;
    private JsonSearchExecutor? _executor;
    private TopResultsTracker _topResults = new TopResultsTracker();
    private MotelyJsonConfig? _config;
    private List<string> _columnNames = new();

    public SearchWindow(string configName, string configFormat)
    {
        var displayName = Path.GetFileNameWithoutExtension(configName);
        _configName = configName;
        _configFormat = configFormat;

        // Compact draggable window with filter name as title
        Title = $"Search: {displayName}";
        X = Pos.Center();
        Y = Pos.Center();
        Width = 70;
        Height = 22;
        CanFocus = true;
        SetScheme(BalatroTheme.Window);

        // Top 10 Results Table - compact view showing Seed + Score
        _topResultsTable = new TableView()
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill() - 2,
            Height = 13, // Header + 10 rows + borders
            FullRowSelect = true,
            CanFocus = true,
        };
        _topResultsTable.SetScheme(
            new Scheme()
            {
                Normal = new Attribute(BalatroTheme.White, BalatroTheme.DarkGrey),
                Focus = new Attribute(BalatroTheme.Black, BalatroTheme.Red),
            }
        );

        // Handle mouse clicks on table rows to copy Blueprint URL
        _topResultsTable.MouseClick += (s, e) =>
        {
            if (_topResultsTable.Table?.Rows > 0 && _topResultsTable.SelectedRow >= 0)
            {
                CopyBlueprintUrl(_topResultsTable.SelectedRow);
                e.Handled = true;
            }
        };

        // Initialize with empty table
        var emptyTable = new System.Data.DataTable();
        emptyTable.Columns.Add("Waiting for results...");
        _topResultsTable.Table = new DataTableSource(emptyTable);

        Add(_topResultsTable);

        // Output view (scrollable text) - compact, below the table
        _outputView = new TextView()
        {
            X = 1,
            Y = Pos.Bottom(_topResultsTable),
            Width = Dim.Fill() - 2,
            Height = 4,
            ReadOnly = true,
            WordWrap = false,
            CanFocus = true,
        };
        Add(_outputView);

        // Status label
        _statusLabel = new Label()
        {
            X = 1,
            Y = Pos.AnchorEnd(3),
            Width = Dim.Fill() - 2,
            Text = "Initializing search...",
        };
        Add(_statusLabel);

        // Stop button (full width)
        _stopButton = new CleanButton()
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill() - 2,
            Text = "Stop Search | ESC for Menu",
            TextAlignment = Alignment.Center,
        };
        _stopButton.SetScheme(BalatroTheme.BackButton);
        _stopButton.Accept += (s, e) => StopSearch();
        Add(_stopButton);

        // Keyboard shortcuts
        KeyDown += (sender, e) =>
        {
            if (e.KeyCode == (KeyCode.C | KeyCode.CtrlMask))
            {
                StopSearch();
                e.Handled = true;
            }
            else if (e.KeyCode == KeyCode.Esc)
            {
                // Stop search if running
                if (_searchRunning)
                {
                    StopSearch();
                }

                // Show menu with options
                var choice = ShowFourChoiceDialog(
                    "ESC Menu",
                    "What would you like to do?",
                    "Save Top 10",
                    "Main Menu",
                    "Exit",
                    "Cancel"
                );

                if (choice == 0) // Save Top 10
                {
                    SaveTopResults();
                }
                else if (choice == 1) // Main Menu
                {
                    // Return to main menu
                    App?.RequestStop();
                }
                else if (choice == 2) // Exit
                {
                    // Exit the entire application
                    App?.RequestStop();
                    // Set a flag to indicate full exit
                    Environment.Exit(0);
                }
                // choice == 3 (Cancel) - do nothing, stay in search window

                e.Handled = true;
            }
        };

        // Start search automatically on background thread
        Task.Run(async () => await RunSearchAsync());
    }

    private async Task RunSearchAsync()
    {
        var originalOut = Console.Out;
        try
        {
            _searchRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            // Load config for Blueprint URL generation
            var configContent = File.ReadAllText(_configName);
            _config = _configFormat.ToLower() switch
            {
                "json" => ConfigFormatConverter.LoadFromJsonString(configContent),
                "jaml" => ConfigFormatConverter.LoadFromJamlString(configContent),
                _ => ConfigFormatConverter.LoadFromJsonString(configContent),
            };

            // Get column names from config ONCE (includes labels for SHOULD clauses)
            // Capitalize first letter for better UI presentation
            _columnNames = (_config?.GetColumnNames() ?? new List<string> { "Seed", "Score" })
                .Select(name =>
                    string.IsNullOrEmpty(name) ? name : char.ToUpper(name[0]) + name.Substring(1)
                )
                .ToList();

            // Initialize the results tracker with column names (set once, not per-result!)
            _topResults.Initialize(_columnNames);

            // Create callback to receive typed results directly (NO CSV PARSING!)
            // With AutoCutoff enabled, only top results are reported - no flooding!
            Action<MotelySeedScoreTally> resultCallback = (tally) =>
            {
                App?.Invoke(() =>
                {
                    _topResults.AddResult(tally, _topResultsTable);
                });
            };

            // Redirect console output to TextView (for logging only, not parsing!)
            var writer = new TextViewWriter(_outputView);
            Console.SetOut(writer);

            App?.Invoke(() =>
            {
                _statusLabel.Text = "Search running...";
            });

            // Build search parameters
            // If crude seeds mode is enabled, search the sick.txt wordlist first!
            var parameters = new JsonSearchParams
            {
                Threads = TuiSettings.ThreadCount,
                BatchSize = TuiSettings.BatchCharacterCount,
                StartBatch = 0,
                EndBatch = 0,
                EnableDebug = false,
                NoFancy = true, // Disable fancy output in TUI
                Quiet = false,
                SpecificSeed = null,
                Wordlist = TuiSettings.CrudeSeedsEnabled ? "sick" : null,
                RandomSeeds = null,
                Cutoff = 0,
                AutoCutoff = true, // Prevents flooding UI with low-score results
            };

            // Execute search with callback to receive typed results directly
            _executor = new JsonSearchExecutor(
                _configName,
                parameters,
                _configFormat,
                resultCallback
            );
            var result = await Task.Run(() => _executor.Execute(), _cancellationTokenSource.Token);

            // Restore original console output ALWAYS
            Console.SetOut(originalOut);

            if (_searchRunning) // Only update UI if window still active
            {
                App?.Invoke(() =>
                {
                    _statusLabel.Text =
                        result == 0
                            ? "Search completed successfully!"
                            : $"Search exited with code {result}";
                    _stopButton.Text = "Back";
                    _searchRunning = false;
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Search was cancelled - restore console output
            Console.SetOut(originalOut);

            if (_searchRunning)
            {
                App?.Invoke(() =>
                {
                    _statusLabel.Text = "Search cancelled";
                    _stopButton.Text = "Back";
                    _searchRunning = false;
                });
            }
        }
        catch (Exception ex)
        {
            // Restore console output on error too
            Console.SetOut(originalOut);

            if (_searchRunning) // Only update UI if window still active
            {
                App?.Invoke(() =>
                {
                    _outputView.Text += $"\n\nError: {ex.Message}\n{ex.StackTrace}";
                    _statusLabel.Text = "Search failed!";
                    _stopButton.Text = "Back";
                    _searchRunning = false;
                });
            }
        }
        finally
        {
            // ALWAYS restore console output
            try
            {
                Console.SetOut(originalOut);
            }
            catch { }
            _searchRunning = false;
        }
    }

    private void StopSearch()
    {
        if (_searchRunning)
        {
            _cancellationTokenSource?.Cancel();
            _executor?.Cancel();
            _searchRunning = false;
            _statusLabel.Text = "Search stopped by user";
            _stopButton.Text = "Back";
            _stopButton.Enabled = true;

            try
            {
                Console.SetOut(Console.Out);
            }
            catch { }

            // Force UI refresh so changes are visible immediately
            SetNeedsDraw();
        }
        else
        {
            App?.RequestStop();
        }
    }

    private void SaveTopResults()
    {
        if (_topResults.Count == 0)
        {
            var noResultsDialog = new Dialog()
            {
                Title = "No Results",
                Width = 40,
                Height = 9,
            };
            noResultsDialog.SetScheme(BalatroTheme.Window);
            var noResultsLabel = new Label()
            {
                X = Pos.Center(),
                Y = 2,
                Text = "No results to save yet!",
                TextAlignment = Alignment.Center,
            };
            noResultsLabel.SetScheme(BalatroTheme.ErrorText);
            noResultsDialog.Add(noResultsLabel);
            var okBtn = new CleanButton()
            {
                X = 1,
                Y = Pos.AnchorEnd(1),
                Text = "Back",
                Width = Dim.Fill() - 2,
                TextAlignment = Alignment.Center,
            };
            okBtn.SetScheme(BalatroTheme.BackButton);
            okBtn.Accept += (s, e) => App?.RequestStop(noResultsDialog);
            noResultsDialog.Add(okBtn);
            App?.Run(noResultsDialog);
            return;
        }

        // Ask for filename
        var dialog = new Dialog()
        {
            Title = "Save Top 10 Results",
            X = Pos.Center(),
            Y = Pos.Center(),
            Width = 60,
            Height = 10,
        };

        var label = new Label()
        {
            X = 1,
            Y = 1,
            Text = "Filename (without extension):",
        };
        dialog.Add(label);

        var textField = new TextField()
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill() - 2,
            Text = $"top10_{_configName}_{DateTime.Now:yyyyMMdd_HHmmss}",
        };
        dialog.Add(textField);

        var formatLabel = new Label()
        {
            X = 1,
            Y = 3,
            Text = "Format: CSV",
        };
        dialog.Add(formatLabel);

        var okButton = new CleanButton() { Text = " Save " };
        okButton.SetScheme(BalatroTheme.BlueButton);
        var cancelButton = new CleanButton() { Text = " Back " };
        cancelButton.SetScheme(BalatroTheme.BackButton);

        okButton.Accept += (s, e) =>
        {
            var filename = textField.Text.ToString();
            if (string.IsNullOrWhiteSpace(filename))
            {
                var errorDialog = new Dialog()
                {
                    Title = "Error",
                    Width = 45,
                    Height = 9,
                };
                errorDialog.SetScheme(BalatroTheme.Window);
                var errorLabel = new Label()
                {
                    X = Pos.Center(),
                    Y = 2,
                    Text = "Filename cannot be empty!",
                    TextAlignment = Alignment.Center,
                };
                errorLabel.SetScheme(BalatroTheme.ErrorText);
                errorDialog.Add(errorLabel);
                var okBtn = new CleanButton()
                {
                    X = 1,
                    Y = Pos.AnchorEnd(1),
                    Text = "Back",
                    Width = Dim.Fill() - 2,
                    TextAlignment = Alignment.Center,
                };
                okBtn.SetScheme(BalatroTheme.BackButton);
                okBtn.Accept += (s, e) => App?.RequestStop(errorDialog);
                errorDialog.Add(okBtn);
                App?.Run(errorDialog);
                return;
            }

            try
            {
                var filepath = System.IO.Path.Combine(
                    Directory.GetCurrentDirectory(),
                    filename + ".csv"
                );
                using (var writer = new System.IO.StreamWriter(filepath))
                {
                    // Write header
                    writer.WriteLine(string.Join(",", _topResults._columnHeaders));

                    // Write top 10 results (already sorted descending)
                    foreach (var result in _topResults._topResults)
                    {
                        writer.WriteLine(string.Join(",", result.Data));
                    }
                }

                var successDialog = new Dialog()
                {
                    Title = "Success",
                    Width = Math.Min(70, filepath.Length + 20),
                    Height = 9,
                };
                successDialog.SetScheme(BalatroTheme.Window);
                var successLabel = new Label()
                {
                    X = Pos.Center(),
                    Y = 2,
                    Text = $"Saved to:\n{filepath}",
                    TextAlignment = Alignment.Center,
                };
                successDialog.Add(successLabel);
                var okBtn = new CleanButton()
                {
                    X = 1,
                    Y = Pos.AnchorEnd(1),
                    Text = "Back",
                    Width = Dim.Fill() - 2,
                    TextAlignment = Alignment.Center,
                };
                okBtn.SetScheme(BalatroTheme.BackButton);
                okBtn.Accept += (s, e) => App?.RequestStop(successDialog);
                successDialog.Add(okBtn);
                App?.Run(successDialog);
                App?.RequestStop(dialog);
            }
            catch (Exception ex)
            {
                var errorDialog = new Dialog()
                {
                    Title = "Error",
                    Width = Math.Min(60, ex.Message.Length + 25),
                    Height = 10,
                };
                errorDialog.SetScheme(BalatroTheme.Window);
                var errorLabel = new Label()
                {
                    X = Pos.Center(),
                    Y = 2,
                    Text = $"Failed to save:\n{ex.Message}",
                    TextAlignment = Alignment.Center,
                };
                errorLabel.SetScheme(BalatroTheme.ErrorText);
                errorDialog.Add(errorLabel);
                var okBtn = new CleanButton()
                {
                    X = 1,
                    Y = Pos.AnchorEnd(1),
                    Text = "Back",
                    Width = Dim.Fill() - 2,
                    TextAlignment = Alignment.Center,
                };
                okBtn.SetScheme(BalatroTheme.BackButton);
                okBtn.Accept += (s, e) => App?.RequestStop(errorDialog);
                errorDialog.Add(okBtn);
                App?.Run(errorDialog);
            }
        };

        cancelButton.Accept += (s, e) => App?.RequestStop(dialog);

        okButton.X = 2;
        okButton.Y = Pos.AnchorEnd(1);
        cancelButton.X = Pos.Right(okButton) + 2;
        cancelButton.Y = Pos.AnchorEnd(1);
        dialog.Add(okButton);
        dialog.Add(cancelButton);

        App?.Run(dialog);
    }

    private void CopyBlueprintUrl(int rowIndex)
    {
        try
        {
            // Get the seed from the selected row
            var table = _topResultsTable.Table;
            if (table == null || rowIndex >= table.Rows || rowIndex < 0)
                return;

            var seed = table[rowIndex, 0]?.ToString();
            if (string.IsNullOrWhiteSpace(seed))
                return;

            // Build Blueprint URL
            var deckName = _config?.Deck ?? "Red";
            var stakeName = _config?.Stake ?? "White";
            var maxAnte = _config?.MaxBossAnte ?? 8;

            var url =
                $"https://miaklwalker.github.io/Blueprint/?seed={seed}&deck={deckName}+Deck&antes={maxAnte}&stake={stakeName}+Stake";

            // Copy to clipboard using platform-specific command
            CopyToClipboard(url);

            var successDialog = new Dialog()
            {
                Title = "Blueprint URL Copied!",
                Width = Math.Min(120, url.Length + 10),  // Wider to fit full URLs
                Height = 12,
            };
            successDialog.SetScheme(BalatroTheme.Window);
            var successLabel = new Label()
            {
                X = Pos.Center(),
                Y = 2,
                Text = $"Seed: {seed}\n\nURL copied to clipboard:\n{url}",
                TextAlignment = Alignment.Center,
            };
            successDialog.Add(successLabel);
            var okBtn = new CleanButton()
            {
                X = 1,
                Y = Pos.AnchorEnd(1),
                Text = "Back",
                Width = Dim.Fill() - 2,
                TextAlignment = Alignment.Center,
            };
            okBtn.SetScheme(BalatroTheme.BackButton);
            okBtn.Accept += (s, e) => App?.RequestStop(successDialog);
            successDialog.Add(okBtn);
            App?.Run(successDialog);
        }
        catch (Exception ex)
        {
            var errorDialog = new Dialog()
            {
                Title = "Error",
                Width = Math.Min(60, ex.Message.Length + 35),
                Height = 10,
            };
            errorDialog.SetScheme(BalatroTheme.Window);
            var errorLabel = new Label()
            {
                X = Pos.Center(),
                Y = 2,
                Text = $"Failed to copy Blueprint URL:\n{ex.Message}",
                TextAlignment = Alignment.Center,
            };
            errorLabel.SetScheme(BalatroTheme.ErrorText);
            errorDialog.Add(errorLabel);
            var okBtn2 = new CleanButton()
            {
                X = 1,
                Y = Pos.AnchorEnd(1),
                Text = "Back",
                Width = Dim.Fill() - 2,
                TextAlignment = Alignment.Center,
            };
            okBtn2.SetScheme(BalatroTheme.BackButton);
            okBtn2.Accept += (s, e) => App?.RequestStop(errorDialog);
            errorDialog.Add(okBtn2);
            App?.Run(errorDialog);
        }
    }

    private void CopyToClipboard(string text)
    {
        try
        {
            // Use platform-specific commands directly (Terminal.Gui clipboard may truncate long URLs)
            if (OperatingSystem.IsWindows())
            {
                var psi = new System.Diagnostics.ProcessStartInfo("clip")
                {
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                var proc = System.Diagnostics.Process.Start(psi);
                proc?.StandardInput.Write(text);
                proc?.StandardInput.Close();
                proc?.WaitForExit();
            }
            else if (OperatingSystem.IsMacOS())
            {
                var psi = new System.Diagnostics.ProcessStartInfo("pbcopy")
                {
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                var proc = System.Diagnostics.Process.Start(psi);
                proc?.StandardInput.Write(text);
                proc?.StandardInput.Close();
                proc?.WaitForExit();
            }
            else
            {
                var psi = new System.Diagnostics.ProcessStartInfo("xclip", "-selection clipboard")
                {
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                var proc = System.Diagnostics.Process.Start(psi);
                proc?.StandardInput.Write(text);
                proc?.StandardInput.Close();
                proc?.WaitForExit();
            }
        }
        catch
        {
            throw new Exception("Clipboard not available. Please copy manually.");
        }
    }

    // Helper class to redirect Console.WriteLine to TextView (logging only, NO CSV PARSING!)
    private class TextViewWriter : System.IO.TextWriter
    {
        private TextView _textView;

        public TextViewWriter(TextView textView)
        {
            _textView = textView;
        }

        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        public override void Write(char value)
        {
            try
            {
                MotelyTUI.App?.Invoke(() =>
                {
                    if (_textView != null)
                    {
                        _textView.Text += value.ToString();
                        _textView.MoveEnd();
                    }
                });
            }
            catch (ObjectDisposedException)
            {
                // Window closed while writing - ignore
            }
        }

        public override void Write(string? value)
        {
            if (value == null)
                return;
            try
            {
                MotelyTUI.App?.Invoke(() =>
                {
                    if (_textView != null)
                    {
                        _textView.Text += value;
                        _textView.MoveEnd();
                    }
                });
            }
            catch (ObjectDisposedException)
            {
                // Window closed while writing - ignore
            }
        }

        public override void WriteLine(string? value)
        {
            try
            {
                MotelyTUI.App?.Invoke(() =>
                {
                    if (_textView != null)
                    {
                        _textView.Text += (value ?? string.Empty) + "\n";
                        _textView.MoveEnd();
                    }
                });
            }
            catch (ObjectDisposedException)
            {
                // Window closed while writing - ignore
            }
        }
    }

    // Tracks and displays top 10 search results using TYPED data (no CSV parsing!)
    private class TopResultsTracker
    {
        internal List<string> _columnHeaders = new List<string>();
        internal SortedSet<SeedResult> _topResults = new SortedSet<SeedResult>(
            new ScoreDescendingComparer()
        );
        private const int MAX_RESULTS = 10;

        public int Count => _topResults.Count;

        // Initialize column headers ONCE from config (not per-result!)
        public void Initialize(List<string> columnNames)
        {
            _columnHeaders.Clear();
            _columnHeaders.AddRange(columnNames);
        }

        public void AddResult(MotelySeedScoreTally tally, TableView tableView)
        {
            // AutoCutoff in executor already filters - just add and display
            _topResults.Add(
                new SeedResult
                {
                    Seed = tally.Seed,
                    Score = tally.Score,
                    Data = new List<string> { tally.Seed, tally.Score.ToString() }
                        .Concat(tally.TallyColumns.Select(v => v.ToString()))
                        .ToList(),
                }
            );

            // Trim to top 10 (defensive)
            if (_topResults.Count > MAX_RESULTS)
                _topResults.Remove(_topResults.Min!);

            UpdateTable(tableView);
        }

        private void UpdateTable(TableView tableView)
        {
            if (tableView == null)
                return;

            try
            {
                // Create DataTable
                var dt = new DataTable();

                // Add columns
                if (_columnHeaders.Count > 0)
                {
                    foreach (var header in _columnHeaders)
                    {
                        dt.Columns.Add(header);
                    }
                }
                else
                {
                    // No headers yet, show waiting message
                    dt.Columns.Add("Waiting for results...");
                }

                // Add rows (top 10, already sorted by score descending via ScoreDescendingComparer)
                foreach (var result in _topResults)
                {
                    var row = dt.NewRow();
                    for (int i = 0; i < Math.Min(result.Data.Count, dt.Columns.Count); i++)
                    {
                        row[i] = result.Data[i];
                    }
                    dt.Rows.Add(row);
                }

                // Update table
                tableView.Table = new DataTableSource(dt);
            }
            catch (Exception)
            {
                // Silently ignore table update errors during rapid updates
            }
        }

        internal class SeedResult
        {
            public string Seed { get; set; } = "";
            public int Score { get; set; }
            public List<string> Data { get; set; } = new List<string>();

            public override bool Equals(object? obj)
            {
                return obj is SeedResult other && Seed == other.Seed;
            }

            public override int GetHashCode()
            {
                return Seed.GetHashCode();
            }
        }

        private class ScoreDescendingComparer : IComparer<SeedResult>
        {
            public int Compare(SeedResult? x, SeedResult? y)
            {
                if (x == null || y == null)
                    return 0;

                // First compare by score (descending - highest first)
                int scoreCompare = x.Score.CompareTo(y.Score);
                if (scoreCompare != 0)
                    return -scoreCompare; // Negate to get descending order

                // If scores are equal, compare by seed name (ascending) to maintain uniqueness
                return string.Compare(x.Seed, y.Seed, StringComparison.Ordinal);
            }
        }
    }

    // Balatro-styled choice dialog (3 buttons)
    private static int ShowChoiceDialog(
        string title,
        string message,
        string button1,
        string button2,
        string button3
    )
    {
        var dialog = new Dialog()
        {
            Title = title,
            Width = Math.Min(60, Math.Max(message.Length + 10, 50)),
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

        int result = -1;

        var btn1 = new CleanButton() { Text = $" {button1} " };
        btn1.SetScheme(BalatroTheme.ModalButton);
        btn1.Accept += (s, e) =>
        {
            result = 0;
            MotelyTUI.App?.RequestStop(dialog);
        };

        var btn2 = new CleanButton() { Text = $" {button2} " };
        btn2.SetScheme(BalatroTheme.ModalButton);
        btn2.Accept += (s, e) =>
        {
            result = 1;
            MotelyTUI.App?.RequestStop(dialog);
        };

        var btn3 = new CleanButton()
        {
            X = Pos.Right(btn2) + 2,
            Y = Pos.AnchorEnd(1),
            Text = $" {button3} ",
        };
        btn3.SetScheme(BalatroTheme.BackButton);
        btn3.Accept += (s, e) =>
        {
            result = 2;
            MotelyTUI.App?.RequestStop(dialog);
        };

        btn1.X = 2;
        btn1.Y = Pos.AnchorEnd(1);
        btn2.X = Pos.Right(btn1) + 2;
        btn2.Y = Pos.AnchorEnd(1);
        dialog.Add(btn1);
        dialog.Add(btn2);
        dialog.Add(btn3);

        MotelyTUI.App?.Run(dialog);
        return result;
    }

    // Balatro-styled choice dialog (4 buttons stacked vertically)
    private static int ShowFourChoiceDialog(
        string title,
        string message,
        string button1,
        string button2,
        string button3,
        string button4
    )
    {
        var dialog = new Dialog()
        {
            Title = title,
            Width = 45,
            Height = 14,
        };
        dialog.SetScheme(BalatroTheme.Window);

        var label = new Label()
        {
            X = Pos.Center(),
            Y = 1,
            Text = message,
            TextAlignment = Alignment.Center,
        };
        dialog.Add(label);

        int result = -1;

        // Stack buttons vertically
        var btn1 = new CleanButton()
        {
            X = 1,
            Y = 3,
            Width = Dim.Fill() - 2,
            Text = button1,
            TextAlignment = Alignment.Center,
        };
        btn1.SetScheme(BalatroTheme.BlueButton);
        btn1.Accept += (s, e) =>
        {
            result = 0;
            MotelyTUI.App?.RequestStop(dialog);
        };
        dialog.Add(btn1);

        var btn2 = new CleanButton()
        {
            X = 1,
            Y = 5,
            Width = Dim.Fill() - 2,
            Text = button2,
            TextAlignment = Alignment.Center,
        };
        btn2.SetScheme(BalatroTheme.ModalButton);
        btn2.Accept += (s, e) =>
        {
            result = 1;
            MotelyTUI.App?.RequestStop(dialog);
        };
        dialog.Add(btn2);

        var btn3 = new CleanButton()
        {
            X = 1,
            Y = 7,
            Width = Dim.Fill() - 2,
            Text = button3,
            TextAlignment = Alignment.Center,
        };
        btn3.SetScheme(BalatroTheme.ModalButton);
        btn3.Accept += (s, e) =>
        {
            result = 2;
            MotelyTUI.App?.RequestStop(dialog);
        };
        dialog.Add(btn3);

        // Orange Back button at bottom (full width)
        var btn4 = new CleanButton()
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill() - 2,
            Text = button4,
            TextAlignment = Alignment.Center,
        };
        btn4.SetScheme(BalatroTheme.BackButton);
        btn4.Accept += (s, e) =>
        {
            result = 3;
            MotelyTUI.App?.RequestStop(dialog);
        };
        dialog.Add(btn4);

        btn1.SetFocus();
        MotelyTUI.App?.Run(dialog);
        return result;
    }
}
