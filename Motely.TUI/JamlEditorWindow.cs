using System.Collections.ObjectModel;
using Motely.Filters;

namespace Motely.TUI;

public class JamlEditorWindow : Window
{
    private readonly TextView _editor;
    private readonly Label _statusLabel;
    private readonly Label _modeLabel;
    private readonly ListView _filterList;
    private readonly FrameView _editorFrame;
    private IReadOnlyList<FilterLibraryEntry> _localFilters = Array.Empty<FilterLibraryEntry>();
    private string? _filePath;

    public JamlEditorWindow(string? filePath = null)
    {
        _filePath = filePath;

        Title = string.IsNullOrWhiteSpace(filePath)
            ? "JAML Editor"
            : $"JAML Editor: {Path.GetFileName(filePath)}";
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;
        ColorScheme = BalatroTheme.Window;

        // ── toolbar ─────────────────────────────────────────────────────────
        var toolbar = new View
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill()! - 2,
            Height = 1,
        };
        Add(toolbar);

        var saveButton = new CleanButton
        {
            X = 0,
            Y = 0,
            Text = " Save ",
        };
        saveButton.ColorScheme = BalatroTheme.GreenButton;
        saveButton.Accept += (_, _) => SaveEditorContent();
        toolbar.Add(saveButton);

        var runButton = new CleanButton
        {
            X = Pos.Right(saveButton) + 1,
            Y = 0,
            Text = " Save + Search ",
        };
        runButton.ColorScheme = BalatroTheme.BlueButton;
        runButton.Accept += (_, _) => SaveAndSearch();
        toolbar.Add(runButton);

        var compileButton = new CleanButton
        {
            X = Pos.Right(runButton) + 1,
            Y = 0,
            Text = " Compile ",
        };
        compileButton.ColorScheme = BalatroTheme.OrangeButton;
        compileButton.Accept += (_, _) => CompileAndValidate();
        toolbar.Add(compileButton);

        var refreshButton = new CleanButton
        {
            X = Pos.Right(compileButton) + 1,
            Y = 0,
            Text = " Refresh List ",
        };
        refreshButton.ColorScheme = BalatroTheme.PurpleButton;
        refreshButton.Accept += (_, _) => ReloadFilters();
        toolbar.Add(refreshButton);

        _modeLabel = new Label
        {
            X = Pos.Right(refreshButton) + 2,
            Y = 0,
            Text = "MODE: JAML",
        };
        _modeLabel.ColorScheme = new ColorScheme
        {
            Normal = new Attribute(BalatroTheme.Orange, BalatroTheme.DarkGrey),
        };
        toolbar.Add(_modeLabel);

        _statusLabel = new Label
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill()! - 2,
            Text = string.IsNullOrWhiteSpace(filePath) ? "New document" : filePath,
        };
        Add(_statusLabel);

        // ── left: filter list (inline, always visible) ──────────────────────
        var listFrame = new FrameView
        {
            X = 1,
            Y = 3,
            Width = 30,
            Height = Dim.Fill()! - 5,
            Title = "Local Filters",
        };
        listFrame.ColorScheme = BalatroTheme.InnerPanel;
        Add(listFrame);

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
            HotNormal = new Attribute(BalatroTheme.Orange, BalatroTheme.InnerPanelGrey),
            HotFocus = new Attribute(BalatroTheme.Orange, BalatroTheme.Blue),
        };
        _filterList.Accepting += (_, _) => LoadSelectedFilter();
        listFrame.Add(_filterList);

        // ── right: editor frame ─────────────────────────────────────────────
        _editorFrame = new FrameView
        {
            X = Pos.Right(listFrame) + 1,
            Y = 3,
            Width = Dim.Fill()! - 2,
            Height = Dim.Fill()! - 5,
            Title = EditorFrameTitle(),
        };
        _editorFrame.ColorScheme = BalatroTheme.InnerPanel;
        Add(_editorFrame);

        _editor = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = false,
            WordWrap = false,
            CanFocus = true,
            Text = LoadInitialText(filePath),
        };
        if (_editor.Autocomplete is { } ac)
            ac.SuggestionGenerator = new SingleWordSuggestionGenerator
            {
                AllSuggestions = BuildSuggestionList(),
            };
        _editorFrame.Add(_editor);

        var backButton = new CleanButton
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill()! - 2,
            Text = "Back",
            TextAlignment = Alignment.Center,
        };
        backButton.ColorScheme = BalatroTheme.BackButton;
        backButton.Accept += (_, _) => MotelyTUI.CloseWindow(this);
        Add(backButton);

        KeyDown += (_, e) =>
        {
            if (e.IsCtrl && e.KeyCode == KeyCode.S)
            {
                SaveEditorContent();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == KeyCode.F5)
            {
                SaveAndSearch();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == KeyCode.F7)
            {
                CompileAndValidate();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == KeyCode.Esc)
            {
                MotelyTUI.CloseWindow(this);
                e.Handled = true;
            }
        };

        ReloadFilters();
        _editor.SetFocus();
    }

    private static string EditorFrameTitle() => "JAML";

    private void ReloadFilters()
    {
        _localFilters = FilterLibrary.DiscoverLocalFilters();
        _filterList.SetSource(
            new ObservableCollection<string>(_localFilters.Select(f => f.DisplayName).ToList())
        );
        SetNeedsDraw();
    }

    private void LoadSelectedFilter()
    {
        var idx = _filterList.SelectedItem;
        if (idx < 0 || idx >= _localFilters.Count)
            return;

        var selected = _localFilters[idx];
        try
        {
            _filePath = selected.FullPath;
            _editor.Text = File.ReadAllText(selected.FullPath);
            _modeLabel.Text = "MODE: JAML";
            _editorFrame.Title = EditorFrameTitle();
            Title = $"JAML Editor: {Path.GetFileName(_filePath)}";
            _statusLabel.Text = _filePath;
            _editor.SetFocus();
            SetNeedsDraw();
        }
        catch (Exception ex)
        {
            ShowMessage($"Load failed: {ex.Message}", isError: true);
        }
    }

    private static string LoadInitialText(string? filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            return File.ReadAllText(filePath);

        return "name: New Filter\ndescription: Created in Motely.TUI\ndeck: Red\nstake: White\nmust:\n";
    }

    private bool TryGetValidatedJaml(string source, out string jaml, out string? error)
    {
        jaml = source;
        error = null;

        if (!JamlConfigLoader.TryLoad(jaml, out _, out var loadError))
        {
            error = loadError ?? "Failed to parse JAML.";
            return false;
        }
        return true;
    }

    private void CompileAndValidate()
    {
        var content = _editor.Text?.ToString() ?? string.Empty;
        if (!TryGetValidatedJaml(content, out _, out var error))
        {
            ShowMessage(error ?? "Validation failed.", isError: true);
            return;
        }

        ShowMessage("JAML validated OK.");
    }

    private void SaveEditorContent()
    {
        try
        {
            var content = _editor.Text?.ToString() ?? string.Empty;
            if (!TryGetValidatedJaml(content, out _, out var error))
            {
                ShowMessage(error ?? "Save blocked by validation.", isError: true);
                return;
            }

            if (string.IsNullOrWhiteSpace(_filePath))
            {
                var requestedName = PromptForName();
                if (string.IsNullOrWhiteSpace(requestedName))
                    return;

                _filePath = FilterLibrary.SaveJamlFilter(requestedName, content);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                File.WriteAllText(_filePath, content);
            }

            Title = $"JAML Editor: {Path.GetFileName(_filePath)}";
            _statusLabel.Text = _filePath;
            ShowMessage($"Saved {_filePath}");
            ReloadFilters();
        }
        catch (Exception ex)
        {
            ShowMessage($"Save failed: {ex.Message}", isError: true);
        }
    }

    private void SaveAndSearch()
    {
        var content = _editor.Text?.ToString() ?? string.Empty;
        if (!TryGetValidatedJaml(content, out _, out var error))
        {
            ShowMessage(error ?? "Compile failed.", isError: true);
            return;
        }

        SaveEditorContent();
        if (string.IsNullOrWhiteSpace(_filePath) || !File.Exists(_filePath))
            return;

        var searchWindow = new SearchWindow(
            _filePath,
            TuiSettings.DefaultSource,
            TuiSettings.DefaultSink
        );
        MotelyTUI.ShowWindow(searchWindow);
    }

    private string? PromptForName()
    {
        // Still a dialog for name entry — one of the surviving modals. Reasonable
        // for a transient input field; can be replaced with an inline footer prompt later.
        string? result = null;

        var dialog = new Dialog
        {
            Title = "Save JAML Filter",
            Width = 50,
            Height = 10,
        };
        dialog.ColorScheme = BalatroTheme.Window;

        var nameLabel = new Label
        {
            X = 1,
            Y = 1,
            Text = "Filter name:",
        };
        dialog.Add(nameLabel);

        var nameField = new TextField
        {
            X = Pos.Right(nameLabel) + 1,
            Y = 1,
            Width = 26,
            Text = _filePath is null ? string.Empty : Path.GetFileNameWithoutExtension(_filePath),
        };
        dialog.Add(nameField);

        var saveButton = new CleanButton
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Text = "Save",
        };
        saveButton.ColorScheme = BalatroTheme.GreenButton;
        saveButton.Accept += (_, _) =>
        {
            result = nameField.Text?.ToString();
            Application.RequestStop(dialog);
        };
        dialog.Add(saveButton);

        var backButton = new CleanButton
        {
            X = Pos.Right(saveButton) + 1,
            Y = Pos.AnchorEnd(1),
            Text = "Back",
        };
        backButton.ColorScheme = BalatroTheme.BackButton;
        backButton.Accept += (_, _) => Application.RequestStop(dialog);
        dialog.Add(backButton);

        Application.Run(dialog);
        return result;
    }

    private void ShowMessage(string message, bool isError = false)
    {
        _statusLabel.Text = message;
        _statusLabel.ColorScheme = new ColorScheme
        {
            Normal = new Attribute(
                isError ? BalatroTheme.Red : BalatroTheme.Green,
                BalatroTheme.DarkGrey
            ),
        };
    }

    private static List<string> BuildSuggestionList()
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);

        foreach (
            var k in new[]
            {
                "name",
                "description",
                "author",
                "deck",
                "stake",
                "antes",
                "must",
                "should",
                "mustNot",
                "type",
                "value",
                "edition",
                "enhancement",
                "sticker",
                "seal",
                "suit",
                "rank",
                "shopItems",
                "boosterPacks",
                "shop",
                "booster",
                "packs",
                "Tarot",
                "Planet",
                "Spectral",
                "joker",
                "tag",
                "first",
                "second",
                "third",
                "fourth",
                "fifth",
                "score",
                "sources",
                "tallyConfig",
                "cutoff",
            }
        )
            set.Add(k);

        foreach (var name in typeof(MotelyDeck).GetEnumNames())
            set.Add(name);
        foreach (var name in typeof(MotelyStake).GetEnumNames())
            set.Add(name);
        foreach (var name in typeof(MotelyJoker).GetEnumNames())
            set.Add(name);
        foreach (var name in typeof(MotelyTarotCard).GetEnumNames())
            set.Add(name);
        foreach (var name in typeof(MotelyPlanetCard).GetEnumNames())
            set.Add(name);
        foreach (var name in typeof(MotelySpectralCard).GetEnumNames())
            set.Add(name);
        foreach (var name in typeof(MotelyTag).GetEnumNames())
            set.Add(name);

        foreach (var w in new[] { "Eternal", "Perishable", "Rental", "Pinned" })
            set.Add(w);
        foreach (var w in new[] { "foil", "holographic", "polychrome", "negative" })
            set.Add(w);
        foreach (var w in new[] { "in Ante", "by Ante", "Ante" })
            set.Add(w);

        return set.ToList();
    }
}
