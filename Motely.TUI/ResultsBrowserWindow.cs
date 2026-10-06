using System.Data;

namespace Motely.TUI;

/// <summary>
/// Non-modal browser for the Motely DuckLake results store.
/// Layout: path bar on top, filter list on the left, results table on the right,
/// toolbar at the bottom. No modal dialogs.
/// </summary>
public class ResultsBrowserWindow : Window
{
    private enum SortMode
    {
        ScoreDesc,
        ScoreAsc,
        SeedAsc,
        SeedDesc,
    }

    private readonly TextField _pathField;
    private readonly TextField _cutoffField;
    private readonly CleanButton _sortBtn;
    private readonly ListView _filterList;
    private readonly TableView _resultsTable;
    private readonly Label _statusLabel;
    private readonly Label _countLabel;

    private DataTable _dataTable = new();
    private List<string> _filterIds = new();
    private SortMode _sortMode = SortMode.ScoreDesc;
    private int _minScore = 0;
    private int _rowLimit = 1000;
    private int _topScoreInView = int.MinValue;
    private int _secondTierThreshold = int.MinValue;

    public ResultsBrowserWindow()
    {
        Title = "Results Browser — DuckLake";
        // Tile on the right so it can sit next to the search window.
        X = Pos.Percent(55);
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill()! - 5;
        CanFocus = true;
        ColorScheme = BalatroTheme.Window;

        // ── top bar: lake path + refresh/export ─────────────────────────────
        var pathLabel = new Label
        {
            X = 1,
            Y = 1,
            Text = "Lake:",
        };
        Add(pathLabel);

        _pathField = new TextField
        {
            X = Pos.Right(pathLabel) + 1,
            Y = 1,
            Width = Dim.Fill()! - 42,
            Text = TuiSettings.DataLakePath,
        };
        _pathField.ColorScheme = new ColorScheme
        {
            Normal = new Attribute(BalatroTheme.White, BalatroTheme.DarkGrey),
            Focus = new Attribute(BalatroTheme.White, BalatroTheme.Blue),
        };
        Add(_pathField);

        var refreshBtn = new CleanButton
        {
            X = Pos.AnchorEnd(40),
            Y = 1,
            Text = " Refresh ",
        };
        refreshBtn.ColorScheme = BalatroTheme.BlueButton;
        refreshBtn.Accept += (_, _) => LoadFilterIds();
        Add(refreshBtn);

        var exportBtn = new CleanButton
        {
            X = Pos.AnchorEnd(28),
            Y = 1,
            Text = " Export .parquet ",
        };
        exportBtn.ColorScheme = BalatroTheme.GreenButton;
        exportBtn.Accept += (_, _) => ExportParquet();
        Add(exportBtn);

        var clearBtn = new CleanButton
        {
            X = Pos.AnchorEnd(10),
            Y = 1,
            Text = " Clear ",
        };
        clearBtn.ColorScheme = BalatroTheme.RedButton;
        clearBtn.Accept += (_, _) => ClearFilter();
        Add(clearBtn);

        // ── Y=3: sort + cutoff row ──────────────────────────────────────────
        _sortBtn = new CleanButton
        {
            X = 1,
            Y = 3,
            Text = SortButtonText(),
        };
        _sortBtn.ColorScheme = BalatroTheme.PurpleButton;
        _sortBtn.Accept += (_, _) => CycleSort();
        Add(_sortBtn);

        var cutoffLabel = new Label
        {
            X = Pos.Right(_sortBtn) + 2,
            Y = 3,
            Text = "Min Score:",
        };
        Add(cutoffLabel);

        _cutoffField = new TextField
        {
            X = Pos.Right(cutoffLabel) + 1,
            Y = 3,
            Width = 8,
            Text = "0",
        };
        _cutoffField.ColorScheme = new ColorScheme
        {
            Normal = new Attribute(BalatroTheme.White, BalatroTheme.DarkGrey),
            Focus = new Attribute(BalatroTheme.White, BalatroTheme.Blue),
        };
        Add(_cutoffField);

        var applyBtn = new CleanButton
        {
            X = Pos.Right(_cutoffField) + 1,
            Y = 3,
            Text = " Apply ",
        };
        applyBtn.ColorScheme = BalatroTheme.GreenButton;
        applyBtn.Accept += (_, _) => ApplyCutoff();
        Add(applyBtn);

        var limitLabel = new Label
        {
            X = Pos.Right(applyBtn) + 2,
            Y = 3,
            Text = "Limit:",
        };
        Add(limitLabel);

        var limitField = new TextField
        {
            X = Pos.Right(limitLabel) + 1,
            Y = 3,
            Width = 8,
            Text = _rowLimit.ToString(),
        };
        limitField.ColorScheme = _cutoffField.ColorScheme;
        Add(limitField);

        var limitApplyBtn = new CleanButton
        {
            X = Pos.Right(limitField) + 1,
            Y = 3,
            Text = " Set ",
        };
        limitApplyBtn.ColorScheme = BalatroTheme.BlueButton;
        limitApplyBtn.Accept += (_, _) =>
        {
            if (int.TryParse(limitField.Text, out var n) && n > 0)
            {
                _rowLimit = n;
                Reload();
            }
        };
        Add(limitApplyBtn);

        // ── left panel: filter_id list ──────────────────────────────────────
        var filterFrame = new FrameView
        {
            X = 1,
            Y = 5,
            Width = 28,
            Height = Dim.Fill()! - 7,
            Title = "Filters",
        };
        filterFrame.ColorScheme = BalatroTheme.InnerPanel;
        Add(filterFrame);

        _filterList = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
        };
        _filterList.ColorScheme = new ColorScheme
        {
            Normal = new Attribute(BalatroTheme.White, BalatroTheme.InnerPanelGrey),
            Focus = new Attribute(BalatroTheme.White, BalatroTheme.Blue),
            HotNormal = new Attribute(BalatroTheme.White, BalatroTheme.InnerPanelGrey),
            HotFocus = new Attribute(BalatroTheme.White, BalatroTheme.Blue),
        };
        _filterList.SelectedItemChanged += (_, _) => OnFilterSelected();
        _filterList.Accepting += (_, _) => OnFilterSelected();
        filterFrame.Add(_filterList);

        // ── right panel: results table ──────────────────────────────────────
        var resultsFrame = new FrameView
        {
            X = Pos.Right(filterFrame) + 1,
            Y = 5,
            Width = Dim.Fill()! - 2,
            Height = Dim.Fill()! - 7,
            Title = "Results (select a filter)",
        };
        resultsFrame.ColorScheme = BalatroTheme.InnerPanel;
        Add(resultsFrame);

        _resultsTable = new TableView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            FullRowSelect = true,
            CanFocus = true,
        };
        _resultsTable.Style.ShowHorizontalHeaderOverline = false;
        _resultsTable.Style.ShowHorizontalHeaderUnderline = true;
        _resultsTable.Style.AlwaysShowHeaders = true;
        // Top-score row → gold, ≥90% of top → orange, everything else = default.
        var topScheme = new ColorScheme
        {
            Normal = new Attribute(BalatroTheme.Orange, BalatroTheme.DarkGrey),
            Focus = new Attribute(BalatroTheme.Orange, BalatroTheme.Blue),
            HotNormal = new Attribute(BalatroTheme.Orange, BalatroTheme.DarkGrey),
            HotFocus = new Attribute(BalatroTheme.Orange, BalatroTheme.Blue),
        };
        var tierScheme = new ColorScheme
        {
            Normal = new Attribute(BalatroTheme.Green, BalatroTheme.InnerPanelGrey),
            Focus = new Attribute(BalatroTheme.Green, BalatroTheme.Blue),
            HotNormal = new Attribute(BalatroTheme.Green, BalatroTheme.InnerPanelGrey),
            HotFocus = new Attribute(BalatroTheme.Green, BalatroTheme.Blue),
        };
        _resultsTable.Style.RowColorGetter = args =>
        {
            if (args.Table is not DataTableSource src)
                return null;
            if (args.RowIndex < 0 || args.RowIndex >= src.DataTable.Rows.Count)
                return null;
            if (src.DataTable.Columns.Count < 2)
                return null;
            var v = src.DataTable.Rows[args.RowIndex][1]; // column 1 = Score
            if (v is not int score)
                return null;
            if (score == _topScoreInView && _topScoreInView > int.MinValue)
                return topScheme;
            if (score >= _secondTierThreshold && _secondTierThreshold > int.MinValue)
                return tierScheme;
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
        _resultsTable.CellActivated += (_, e) => OnCellActivated(e.Col);
        resultsFrame.Add(_resultsTable);

        // ── bottom bar: status + back ───────────────────────────────────────
        _statusLabel = new Label
        {
            X = 1,
            Y = Pos.AnchorEnd(2),
            Text = "Idle",
        };
        _statusLabel.ColorScheme = new ColorScheme
        {
            Normal = new Attribute(BalatroTheme.LightGrey, BalatroTheme.ModalGrey),
        };
        Add(_statusLabel);

        _countLabel = new Label
        {
            X = Pos.Right(_statusLabel) + 2,
            Y = Pos.AnchorEnd(2),
            Text = "",
        };
        _countLabel.ColorScheme = new ColorScheme
        {
            Normal = new Attribute(BalatroTheme.Orange, BalatroTheme.ModalGrey),
        };
        Add(_countLabel);

        var backBtn = new CleanButton
        {
            X = Pos.AnchorEnd(10),
            Y = Pos.AnchorEnd(2),
            Text = " Back ",
        };
        backBtn.ColorScheme = BalatroTheme.BackButton;
        backBtn.Accept += (_, _) => MotelyTUI.CloseWindow(this);
        Add(backBtn);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == KeyCode.Esc)
            {
                MotelyTUI.CloseWindow(this);
                e.Handled = true;
            }
            else if (e.KeyCode == KeyCode.F5)
            {
                LoadFilterIds();
                e.Handled = true;
            }
        };

        LoadFilterIds();
    }

    private string ResolveLakePath()
    {
        var raw = _pathField.Text;
        if (string.IsNullOrWhiteSpace(raw))
            raw = "seeds";
        TuiSettings.DataLakePath = raw;
        TuiSettings.Save();
        return Path.GetFullPath(raw);
    }

    private void LoadFilterIds()
    {
        _filterIds.Clear();
        _filterList.SetSource(
            new System.Collections.ObjectModel.ObservableCollection<string>(new List<string>())
        );
        _dataTable = new DataTable();
        _dataTable.Columns.Add("Seed", typeof(string));
        _dataTable.Columns.Add("Score", typeof(int));
        _resultsTable.Table = new DataTableSource(_dataTable);
        _countLabel.Text = "";

        _topScoreInView = int.MinValue;
        _secondTierThreshold = int.MinValue;

        var path = ResolveLakePath();
        _statusLabel.Text =
            $"DuckLake browser unavailable. Use --save-seeds on JAML files. Saved path setting: {path}";
        _statusLabel.ColorScheme = new ColorScheme
        {
            Normal = new Attribute(BalatroTheme.Orange, BalatroTheme.ModalGrey),
        };

        SetNeedsDraw();
    }

    private void OnFilterSelected()
    {
        _statusLabel.Text = "No DuckLake results available in the no-DB build.";
        _statusLabel.ColorScheme = new ColorScheme
        {
            Normal = new Attribute(BalatroTheme.Orange, BalatroTheme.ModalGrey),
        };
    }

    private string SortClause() =>
        _sortMode switch
        {
            SortMode.ScoreDesc => "score DESC",
            SortMode.ScoreAsc => "score ASC",
            SortMode.SeedAsc => "seed ASC",
            SortMode.SeedDesc => "seed DESC",
            _ => "score DESC",
        };

    private string SortButtonText() =>
        _sortMode switch
        {
            SortMode.ScoreDesc => " Sort: Score ↓ ",
            SortMode.ScoreAsc => " Sort: Score ↑ ",
            SortMode.SeedAsc => " Sort: Seed A→Z ",
            SortMode.SeedDesc => " Sort: Seed Z→A ",
            _ => " Sort: Score ↓ ",
        };

    private void CycleSort()
    {
        _sortMode = (SortMode)(((int)_sortMode + 1) % 4);
        _sortBtn.Text = SortButtonText();
        Reload();
    }

    /// <summary>Press Enter on a cell → sort by that column (toggles asc/desc on repeat).</summary>
    private void OnCellActivated(int col)
    {
        var next = col switch
        {
            0 => _sortMode == SortMode.SeedAsc ? SortMode.SeedDesc : SortMode.SeedAsc,
            1 => _sortMode == SortMode.ScoreDesc ? SortMode.ScoreAsc : SortMode.ScoreDesc,
            _ => _sortMode, // tally cols — not currently sortable
        };
        if (next == _sortMode)
            return;
        _sortMode = next;
        _sortBtn.Text = SortButtonText();
        Reload();
    }

    private void ApplyCutoff()
    {
        var text = _cutoffField.Text?.ToString() ?? "0";
        if (int.TryParse(text, out var n) && n >= 0)
        {
            _minScore = n;
            Reload();
        }
        else
        {
            _statusLabel.Text = $"Invalid cutoff '{text}' — must be a non-negative integer.";
        }
    }

    private void Reload()
    {
        LoadFilterIds();
    }

    private void ExportParquet()
    {
        _statusLabel.Text = "Parquet export unavailable without DuckLake.";
        _statusLabel.ColorScheme = new ColorScheme
        {
            Normal = new Attribute(BalatroTheme.Red, BalatroTheme.ModalGrey),
        };
    }

    private void ClearFilter()
    {
        _statusLabel.Text = "Clear unavailable without DuckLake.";
        _statusLabel.ColorScheme = new ColorScheme
        {
            Normal = new Attribute(BalatroTheme.Red, BalatroTheme.ModalGrey),
        };
    }
}
