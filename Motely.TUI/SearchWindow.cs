using System.Data;
using Motely;
using Motely.Filters;

namespace Motely.TUI;

public class SearchWindow : Window
{
    private readonly string _configPath;
    private readonly string? _source;
    private readonly string? _sink;
    private MotelyTopSeedSink.Collector? _collector;
    private bool _saved;
    private readonly Label _statusLabel;
    private readonly Label _progressLabel;
    private readonly TextField _cutoffField;
    private readonly TableView _resultsTable;
    private readonly DataTable _dataTable = new();
    private readonly CleanButton _stopBtn;
    private readonly SpinnerView _spinner;
    private IMotelySearch? _search;
    private int _highestScoreSeen = int.MinValue;
    private CancellationTokenSource? _cts;
    private bool _searchRunning = false;
    private int _resultCount = 0;
    private int _tallyColumnCount = 0;

    // --cutoff semantics — one shared gate with Motely.CLI (MotelyScoreCutoff):
    //   auto     → running maximum: emit every seed at-or-above the best so far.
    //   <int>    → fixed floor: skip anything below this score.
    //   off/blank→ no cutoff.
    // The field holds the *configured* cutoff; a fresh runtime gate is built per run so the
    // running-maximum state does not carry across searches.
    private MotelyScoreCutoff _cutoff = MotelyScoreCutoff.Auto();

    public SearchWindow(string configPath, string? source = null, string? sink = null)
    {
        _configPath = configPath;
        _source = string.IsNullOrWhiteSpace(source) ? null : source;
        _sink = string.IsNullOrWhiteSpace(sink) ? null : sink;

        Title = $"Search: {Path.GetFileNameWithoutExtension(configPath)}";
        // Tile on the left half so the editor / results browser can sit next to it.
        X = 0;
        Y = 0;
        Width = Dim.Percent(55);
        Height = Dim.Fill()! - 5;
        CanFocus = true;
        ColorScheme = BalatroTheme.Window;

        _spinner = new SpinnerView
        {
            X = 1,
            Y = 1,
            Style = new SpinnerStyle.Dots(),
            AutoSpin = true,
            SpinDelay = 120,
            Visible = true,
        };
        Add(_spinner);

        _statusLabel = new Label
        {
            X = Pos.Right(_spinner) + 1,
            Y = 1,
            Width = Dim.Fill()! - 4,
            Text = "Starting search...",
        };
        _statusLabel.ColorScheme = new ColorScheme
        {
            Normal = new Attribute(BalatroTheme.Orange, BalatroTheme.ModalGrey),
        };
        Add(_statusLabel);

        _progressLabel = new Label
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill()! - 2,
            Text = "",
        };
        _progressLabel.ColorScheme = new ColorScheme
        {
            Normal = new Attribute(BalatroTheme.LightGrey, BalatroTheme.ModalGrey),
        };
        Add(_progressLabel);

        // --cutoff row: matches Motely.CLI semantics. "auto" = running max, "<int>" = floor, "" = off.
        var cutoffLabel = new Label
        {
            X = 1,
            Y = 3,
            Text = "--cutoff:",
        };
        Add(cutoffLabel);

        _cutoffField = new TextField
        {
            X = Pos.Right(cutoffLabel) + 1,
            Y = 3,
            Width = 10,
            Text = "auto",
        };
        _cutoffField.ColorScheme = new ColorScheme
        {
            Normal = new Attribute(BalatroTheme.White, BalatroTheme.DarkGrey),
            Focus = new Attribute(BalatroTheme.White, BalatroTheme.Blue),
        };
        Add(_cutoffField);

        var cutoffApplyBtn = new CleanButton
        {
            X = Pos.Right(_cutoffField) + 1,
            Y = 3,
            Text = " Apply ",
        };
        cutoffApplyBtn.ColorScheme = BalatroTheme.GreenButton;
        cutoffApplyBtn.Accept += (_, _) => ApplyCutoffInput();
        Add(cutoffApplyBtn);

        var cutoffHint = new Label
        {
            X = Pos.Right(cutoffApplyBtn) + 2,
            Y = 3,
            Text = "(auto | <int> | blank)",
        };
        cutoffHint.ColorScheme = new ColorScheme
        {
            Normal = new Attribute(BalatroTheme.LightGrey, BalatroTheme.ModalGrey),
        };
        Add(cutoffHint);

        var resultsFrame = new FrameView
        {
            X = 1,
            Y = 5,
            Width = Dim.Fill()! - 2,
            Height = Dim.Fill()! - 9,
            Title = "Results (live)",
        };
        resultsFrame.ColorScheme = BalatroTheme.InnerPanel;
        Add(resultsFrame);

        _dataTable.Columns.Add("#", typeof(int));
        _dataTable.Columns.Add("Seed", typeof(string));
        _dataTable.Columns.Add("Score", typeof(int));

        _resultsTable = new TableView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            FullRowSelect = true,
            CanFocus = true,
        };
        _resultsTable.Style.AlwaysShowHeaders = true;
        _resultsTable.Style.ShowHorizontalHeaderUnderline = true;
        // Top-score rows get the gold treatment — row's score == best score so far = highlight.
        var goldScheme = new ColorScheme
        {
            Normal = new Attribute(BalatroTheme.Orange, BalatroTheme.DarkGrey),
            Focus = new Attribute(BalatroTheme.Orange, BalatroTheme.Blue),
            HotNormal = new Attribute(BalatroTheme.Orange, BalatroTheme.DarkGrey),
            HotFocus = new Attribute(BalatroTheme.Orange, BalatroTheme.Blue),
        };
        _resultsTable.Style.RowColorGetter = args =>
        {
            if (args.Table is not DataTableSource src)
                return null;
            if (
                src.DataTable.Columns.Count < 3
                || args.RowIndex < 0
                || args.RowIndex >= src.DataTable.Rows.Count
            )
                return null;
            var v = src.DataTable.Rows[args.RowIndex][2];
            if (v is int score && score == _highestScoreSeen && _highestScoreSeen > int.MinValue)
                return goldScheme;
            return null;
        };
        _resultsTable.ColorScheme = new ColorScheme
        {
            Normal = new Attribute(BalatroTheme.White, BalatroTheme.InnerPanelGrey),
            Focus = new Attribute(BalatroTheme.White, BalatroTheme.Blue),
            HotNormal = new Attribute(BalatroTheme.Orange, BalatroTheme.InnerPanelGrey),
            HotFocus = new Attribute(BalatroTheme.Orange, BalatroTheme.Blue),
        };
        _resultsTable.Table = new DataTableSource(_dataTable);
        resultsFrame.Add(_resultsTable);

        _stopBtn = new CleanButton
        {
            X = 1,
            Y = Pos.AnchorEnd(3),
            Text = "Stop Search",
            Width = Dim.Fill()! - 2,
            TextAlignment = Alignment.Center,
        };
        _stopBtn.ColorScheme = BalatroTheme.RedButton;
        _stopBtn.Accept += (_, _) => StopSearch();
        Add(_stopBtn);

        var backBtn = new CleanButton
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Text = "Back",
            Width = Dim.Fill()! - 2,
            TextAlignment = Alignment.Center,
        };
        backBtn.ColorScheme = BalatroTheme.BackButton;
        backBtn.Accept += (_, _) => AttemptClose();
        Add(backBtn);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == KeyCode.Esc)
            {
                AttemptClose();
                e.Handled = true;
            }
        };

        _ = Task.Run(RunSearch);
    }

    private void EnsureTallyColumns(int count)
    {
        if (_tallyColumnCount >= count)
            return;
        for (int i = _tallyColumnCount; i < count; i++)
            _dataTable.Columns.Add($"t{i}", typeof(int));
        _tallyColumnCount = count;
        _resultsTable.Table = new DataTableSource(_dataTable);
    }

    private void RunSearch()
    {
        try
        {
            _cts = new CancellationTokenSource();
            _searchRunning = true;

            if (
                !JamlConfigLoader.TryLoad(File.ReadAllText(_configPath), out var config, out var configError)
            )
                throw new InvalidOperationException(configError ?? "Failed to load search config.");

            // One runtime gate per run so auto's running-maximum starts fresh. Fixed floors are
            // pushed into the engine (the scorer drops below-threshold seeds before any callback);
            // auto/off gate caller-side because the engine threshold is fixed per-plan.
            var cutoff = _cutoff.IsAuto
                ? MotelyScoreCutoff.Auto()
                : _cutoff.EngineCutoff > 0
                    ? MotelyScoreCutoff.Fixed(_cutoff.EngineCutoff)
                    : MotelyScoreCutoff.Off();
            int engineCutoff = cutoff.EngineCutoff;
            var plan = MotelySearchBuilder.CreatePlan(config, engineCutoff);
            var settings = MotelySearchBuilder
                .CreateSettings(config, engineCutoff)
                .WithDeck(config.Deck)
                .WithStake(config.Stake)
                .WithThreadCount(TuiSettings.ThreadCount)
                .WithQuietMode(true)
                .WithAutoScoreCutoff(cutoff.IsAuto);

            switch (TuiSettings.SearchMode)
            {
                case SearchMode.FileSource:
                    if (!string.IsNullOrWhiteSpace(_source))
                    {
                        var seeds = File.ReadLines(_source)
                            .Select(static line => line.Trim())
                            .Where(static line => line.Length > 0)
                            .ToList();
                        if (seeds.Count == 0)
                            throw new InvalidOperationException("The source file contains no seeds.");
                        settings.WithProviderSearch(new MotelySeedListProvider(seeds));
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            "FileSource mode requires a source file."
                        );
                    }
                    break;

                case SearchMode.Random:
                    settings.WithProviderSearch(
                        new MotelyRandomSeedProvider(TuiSettings.RandomSeedCount)
                    );
                    break;

                case SearchMode.Palindrome:
                    settings.WithProviderSearch(new MotelyPalindromeSeedProvider());
                    break;

                case SearchMode.Psychosis:
                    settings.WithProviderSearch(new MotelyPsychosisSeedProvider());
                    break;

                case SearchMode.Keyword:
                    if (string.IsNullOrWhiteSpace(TuiSettings.Keywords))
                        throw new InvalidOperationException(
                            "Keyword mode requires keywords to be set in settings."
                        );

                    var keywords = TuiSettings.Keywords.Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                    );
                    char[]? paddingChars = string.IsNullOrWhiteSpace(TuiSettings.PaddingChars)
                        ? null
                        : TuiSettings.PaddingChars.ToCharArray();

                    settings.WithProviderSearch(
                        new MotelyKeywordSeedProvider(keywords, paddingChars)
                    );
                    break;

                case SearchMode.Sequential:
                default:
                    settings
                        .WithSequentialSearch()
                        .WithBatchCharacterCount(TuiSettings.BatchCharacterCount);
                    if (
                        TuiSettings.SequentialStartSeedSearchIndex.HasValue
                        || TuiSettings.SequentialStopSeedSearchIndex.HasValue
                    )
                    {
                        long startIdx = TuiSettings.SequentialStartSeedSearchIndex ?? 0;
                        long stopIdx =
                            TuiSettings.SequentialStopSeedSearchIndex
                            ?? SeedMath.MaxSearchIndexInclusive(MotelyGlobals.MaxSeedLength);
                        var (sb, ebExclusive) = SeedMath.SearchIndexRangeToBatchRange(
                            startIdx,
                            stopIdx,
                            TuiSettings.BatchCharacterCount
                        );
                        settings.WithStartBatchIndex(sb).WithEndBatchIndex(ebExclusive);
                    }
                    break;
            }

            int scoreTallyColumns = plan.ScoreTallyColumnCount;
            bool hasStructuredScores = scoreTallyColumns > 0;

            Application.Invoke(() => EnsureTallyColumns(scoreTallyColumns));

            // Finds go on screen and into the collector; the collector's seeds are merged into the
            // JAML seeds: block when the run ends (SaveSeedsBack), as Motely.CLI does.
            var collector = new MotelyTopSeedSink.Collector(int.MaxValue);
            _collector = collector;

            if (hasStructuredScores)
            {
                settings.WithScoredResultCallback(tally =>
                {
                    if (!cutoff.ShouldEmit(tally.Score))
                        return;
                    var resultCount = Interlocked.Increment(ref _resultCount);
                    var seed = tally.Seed;
                    var score = tally.Score;
                    var tallyValues = tally.TallyValuesSpan.ToArray();
                    collector.Consider(seed, score);
                    Application.Invoke(() => AppendRow(resultCount, seed, score, tallyValues));
                });
            }
            else
            {
                settings.WithSeedMatchCallback(seed =>
                {
                    var resultCount = Interlocked.Increment(ref _resultCount);
                    collector.Consider(seed, 0);
                    Application.Invoke(() => AppendRow(resultCount, seed, 0, System.Array.Empty<int>()));
                });
            }

            Application.Invoke(() =>
            {
                _statusLabel.Text = "Running";
                _statusLabel.ColorScheme = new ColorScheme
                {
                    Normal = new Attribute(BalatroTheme.Green, BalatroTheme.ModalGrey),
                };
            });

            var search = settings.Start(_cts.Token);
            _search = search;

            // Start is non-blocking: workers run on engine threads. Completion is driven only by
            // this poll (or cancel). Do not call OnSearchComplete here — that disposed the search
            // at ~1% and painted green "Completed" while work was still running (G01).
            Application.AddTimeout(
                TimeSpan.FromMilliseconds(1000),
                () =>
                {
                    if (!_searchRunning)
                        return false;

                    var searched = search.TotalSeedsSearched;
                    var matches = search.MatchingSeeds;
                    var elapsedMs = search.ElapsedMs;
                    var speed = elapsedMs > 0 ? searched / (double)elapsedMs * 1000.0 : 0;
                    var elapsed = TimeSpan.FromMilliseconds(elapsedMs);

                    _progressLabel.Text =
                        $"{searched:N0} seeds | {matches} matches | {speed:N0} seeds/sec | {elapsed:hh\\:mm\\:ss}";

                    if (search.IsCompleted)
                    {
                        OnSearchComplete();
                        return false;
                    }
                    return true;
                }
            );
        }
        catch (OperationCanceledException)
        {
            Application.Invoke(OnSearchStopped);
        }
        catch (Exception ex)
        {
            Application.Invoke(() =>
            {
                _statusLabel.Text = "Error";
                _statusLabel.ColorScheme = new ColorScheme
                {
                    Normal = new Attribute(BalatroTheme.Red, BalatroTheme.ModalGrey),
                };
                _progressLabel.Text = ex.Message;
                _stopBtn.Visible = false;
                _searchRunning = false;
            });
        }
    }

    private void ApplyCutoffInput()
    {
        var raw = _cutoffField.Text?.ToString() ?? "";
        if (MotelyScoreCutoff.TryParse(raw, out var parsed, out var error))
        {
            _cutoff = parsed;
            _statusLabel.Text = parsed.IsAuto
                ? "Cutoff: auto (running max). Applies on next search."
                : parsed.EngineCutoff > 0
                    ? $"Cutoff: ≥ {parsed.EngineCutoff}. Applies on next search."
                    : "Cutoff: off. Applies on next search.";
        }
        else
        {
            _statusLabel.Text = error ?? "Invalid cutoff.";
        }
    }

    private void AppendRow(int idx, string seed, int score, int[] tallies)
    {
        EnsureTallyColumns(tallies.Length);
        var row = _dataTable.NewRow();
        row[0] = idx;
        row[1] = seed;
        row[2] = score;
        for (int i = 0; i < tallies.Length && i < _tallyColumnCount; i++)
            row[3 + i] = tallies[i];
        _dataTable.Rows.Add(row);

        if (score > _highestScoreSeen)
            _highestScoreSeen = score;

        // Scroll to show the newest row.
        _resultsTable.SelectedRow = _dataTable.Rows.Count - 1;
        _resultsTable.EnsureSelectedCellIsVisible();
        _resultsTable.SetNeedsDraw();
    }

    private void OnSearchComplete()
    {
        _searchRunning = false;
        _statusLabel.Text = "Completed";
        _statusLabel.ColorScheme = new ColorScheme
        {
            Normal = new Attribute(BalatroTheme.Green, BalatroTheme.ModalGrey),
        };
        _spinner.AutoSpin = false;
        _spinner.Visible = false;
        _stopBtn.Visible = false;

        SaveSeedsBack();

        if (_search is null)
            return;

        // Snapshot before Dispose — counters stay valid post-dispose today, but hosts must not
        // depend on that. Surface dispose failures (no empty catch).
        var searched = _search.TotalSeedsSearched;
        var matches = _search.MatchingSeeds;
        var elapsed = TimeSpan.FromMilliseconds(_search.ElapsedMs);
        _progressLabel.Text =
            $"Done: {searched:N0} seeds | {matches} matches | {elapsed:hh\\:mm\\:ss}";

        try
        {
            _search.Dispose();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Completed (dispose error)";
            _statusLabel.ColorScheme = new ColorScheme
            {
                Normal = new Attribute(BalatroTheme.Red, BalatroTheme.ModalGrey),
            };
            _progressLabel.Text = $"{_progressLabel.Text} | dispose: {ex.Message}";
            try
            {
                File.WriteAllText(
                    Motely.Program.CrashLogPath,
                    $"{DateTime.UtcNow:O} [SearchWindow.OnSearchComplete.Dispose]\n{ex}"
                );
            }
            catch (Exception logEx)
            {
                Console.Error.WriteLine(
                    $"Search dispose failed and crash log write failed: {ex}; log: {logEx}"
                );
            }
        }
    }

    private void OnSearchStopped()
    {
        _searchRunning = false;
        _statusLabel.Text = "Stopped";
        _statusLabel.ColorScheme = new ColorScheme
        {
            Normal = new Attribute(BalatroTheme.Gray, BalatroTheme.ModalGrey),
        };
        _spinner.AutoSpin = false;
        _spinner.Visible = false;
        _stopBtn.Visible = false;

        SaveSeedsBack();
    }

    /// <summary>
    /// Merge the seeds found this run into the JAML seeds: block on disk. Called on both completion
    /// and stop so a cancelled sweep never loses what it already found. Idempotent (guarded by
    /// <see cref="_saved"/> and de-duped in the seeds: block); seeds also already reached the seed
    /// lake as they were found, so this is the second, curated layer of durability.
    /// </summary>
    private void SaveSeedsBack()
    {
        if (_saved || _collector is null)
            return;
        _saved = true;

        var seeds = _collector.GetSeeds();
        if (seeds.Count == 0)
            return;

        string? error;
        try
        {
            if (MotelyTopSeedSink.TryRewriteAndValidate(File.ReadAllText(_configPath), seeds, out var updated, out error))
                File.WriteAllText(_configPath, updated);
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        _progressLabel.Text = error is null
            ? $"{_progressLabel.Text} | saved {seeds.Count:N0} seed(s) to JAML"
            : $"{_progressLabel.Text} | save-back failed: {error}";
    }

    private void StopSearch()
    {
        if (!_searchRunning)
            return;

        _stopBtn.Enabled = false;
        _stopBtn.Text = "Stopping...";

        _cts?.Cancel();

        OnSearchStopped();
    }

    private void AttemptClose()
    {
        if (_searchRunning)
            StopSearch();

        _search?.Dispose();

        MotelyTUI.CloseWindow(this);
    }

    protected override void Dispose(bool disposing)
    {
        // Last-resort save-back: if the window is torn down without a clean Complete/Stop (e.g. the
        // app is shutting down), still flush the finds to the JAML seeds: block before the lake sink
        // closes. No-op when already saved.
        try
        {
            SaveSeedsBack();
        }
        catch
        {
            // Never throw from Dispose during teardown.
        }

        _search?.Dispose();
        _cts?.Dispose();

        base.Dispose(disposing);
    }

}
