using System.Collections.ObjectModel;
using Motely.Filters;
using Terminal.Gui;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Motely.TUI;

public class FilterBuilderWindow : Window
{
    private ListView _mustList;
    private ListView _shouldList;
    private List<string> _mustItems = new();
    private List<string> _shouldItems = new();
    private List<string> _mustNotItems = new();
    private Label _statusLabel;
    private CleanButton _startSearchBtn;
    private bool _isDialogOpen = false;

    public FilterBuilderWindow()
    {
        Title = "Filter Builder";
        X = Pos.Center();
        Y = Pos.Center();
        Width = 90;
        Height = 24;
        SetScheme(BalatroTheme.Window);

        // Create two columns in inner panel boxes
        var yStart = 3;

        // FILTER ITEMS panel (was MUST)
        var filterPanel = new FrameView()
        {
            X = 2,
            Y = yStart,
            Width = 40,
            Height = 13,
            Title = "Filter Items (Required)",
        };
        filterPanel.SetScheme(BalatroTheme.InnerPanel);
        Add(filterPanel);

        _mustList = new ListView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill() - 2,
            AllowsMarking = false,
            CanFocus = true,
        };
        _mustList.SetScheme(
            new Scheme()
            {
                Normal = new Attribute(BalatroTheme.White, BalatroTheme.InnerPanelGrey),
                Focus = new Attribute(BalatroTheme.White, BalatroTheme.Blue),
                HotNormal = new Attribute(BalatroTheme.White, BalatroTheme.InnerPanelGrey),
                HotFocus = new Attribute(BalatroTheme.White, BalatroTheme.Blue),
            }
        );
        _mustList.SetSource(new ObservableCollection<string>(_mustItems));
        filterPanel.Add(_mustList);

        var mustAddBtn = new CleanButton()
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Text = " + Add ",
        };
        mustAddBtn.SetScheme(BalatroTheme.GreenButton);
        mustAddBtn.Accept += (s, e) => AddItem("must");
        filterPanel.Add(mustAddBtn);

        var mustRemoveBtn = new CleanButton()
        {
            X = Pos.Right(mustAddBtn) + 1,
            Y = Pos.AnchorEnd(1),
            Text = " - Remove ",
        };
        mustRemoveBtn.SetScheme(BalatroTheme.ModalButton);
        mustRemoveBtn.Accept += (s, e) => RemoveItem("must");
        filterPanel.Add(mustRemoveBtn);

        // SCORE ITEMS panel (was SHOULD)
        var scorePanel = new FrameView()
        {
            X = Pos.Right(filterPanel) + 2,
            Y = yStart,
            Width = 40,
            Height = 13,
            Title = "Score Items (Bonus Points)",
        };
        scorePanel.SetScheme(BalatroTheme.InnerPanel);
        Add(scorePanel);

        _shouldList = new ListView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill() - 2,
            AllowsMarking = false,
            CanFocus = true,
        };
        _shouldList.SetScheme(
            new Scheme()
            {
                Normal = new Attribute(BalatroTheme.White, BalatroTheme.InnerPanelGrey),
                Focus = new Attribute(BalatroTheme.White, BalatroTheme.Blue),
                HotNormal = new Attribute(BalatroTheme.White, BalatroTheme.InnerPanelGrey),
                HotFocus = new Attribute(BalatroTheme.White, BalatroTheme.Blue),
            }
        );
        _shouldList.SetSource(new ObservableCollection<string>(_shouldItems));
        scorePanel.Add(_shouldList);

        var shouldAddBtn = new CleanButton()
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Text = " + Add ",
        };
        shouldAddBtn.SetScheme(BalatroTheme.GreenButton);
        shouldAddBtn.Accept += (s, e) => AddItem("should");
        scorePanel.Add(shouldAddBtn);

        var shouldRemoveBtn = new CleanButton()
        {
            X = Pos.Right(shouldAddBtn) + 1,
            Y = Pos.AnchorEnd(1),
            Text = " - Remove ",
        };
        shouldRemoveBtn.SetScheme(BalatroTheme.ModalButton);
        shouldRemoveBtn.Accept += (s, e) => RemoveItem("should");
        scorePanel.Add(shouldRemoveBtn);


        // Action buttons row (above Back)
        // Start Search button - initially disabled until filter is saved
        _startSearchBtn = new CleanButton()
        {
            X = 2,
            Y = Pos.AnchorEnd(4),
            Text = " save first... ",
            Enabled = false,
        };
        _startSearchBtn.SetScheme(new Scheme()
        {
            Normal = new Attribute(BalatroTheme.Red, BalatroTheme.DarkGrey),
            Focus = new Attribute(BalatroTheme.Red, BalatroTheme.DarkGrey),
            HotNormal = new Attribute(BalatroTheme.Red, BalatroTheme.DarkGrey),
            HotFocus = new Attribute(BalatroTheme.Red, BalatroTheme.DarkGrey),
        });
        _startSearchBtn.Accept += (s, e) => StartSearch();
        Add(_startSearchBtn);

        var saveBtn = new CleanButton()
        {
            X = Pos.Right(_startSearchBtn) + 2,
            Y = Pos.AnchorEnd(4),
            Text = " Save Filter ",
        };
        saveBtn.SetScheme(BalatroTheme.GreenButton);
        saveBtn.Accept += (s, e) => SaveFilter();
        Add(saveBtn);

        // Load Filter button - purple for "import" feel
        var loadBtn = new CleanButton()
        {
            X = Pos.Right(saveBtn) + 2,
            Y = Pos.AnchorEnd(4),
            Text = " Load Filter ",
        };
        loadBtn.SetScheme(BalatroTheme.PurpleButton);
        loadBtn.Accept += (s, e) => LoadFilter();
        Add(loadBtn);

        // Status label (same row as action buttons)
        _statusLabel = new Label()
        {
            X = Pos.Right(loadBtn) + 4,
            Y = Pos.AnchorEnd(4),
            Width = Dim.Fill(),
            Text = "",
        };
        Add(_statusLabel);

        // Back button - FULL WIDTH at very bottom
        var backBtn = new CleanButton()
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Text = "Back",
            Width = Dim.Fill() - 2,
            TextAlignment = Alignment.Center,
        };
        backBtn.SetScheme(BalatroTheme.BackButton);
        backBtn.Accept += (s, e) => App?.RequestStop();
        Add(backBtn);

        // Keyboard shortcuts
        KeyDown += (sender, e) =>
        {
            if (e.KeyCode == KeyCode.A)
            {
                // Determine which list has focus
                if (_mustList.HasFocus)
                    AddItem("must");
                else if (_shouldList.HasFocus)
                    AddItem("should");
                e.Handled = true;
            }
            else if (e.KeyCode == KeyCode.J)
            {
                AddItemQuick("Joker");
                e.Handled = true;
            }
            else if (e.KeyCode == KeyCode.L)
            {
                AddItemQuick("Legendary");
                e.Handled = true;
            }
            else if (e.KeyCode == KeyCode.T)
            {
                AddItemQuick("Tarot");
                e.Handled = true;
            }
            else if (e.KeyCode == KeyCode.S && !e.IsCtrl)
            {
                AddItemQuick("Spectral");
                e.Handled = true;
            }
            else if (e.KeyCode == KeyCode.P)
            {
                AddItemQuick("Planet");
                e.Handled = true;
            }
            else if (e.KeyCode == KeyCode.V)
            {
                AddItemQuick("Voucher");
                e.Handled = true;
            }
            else if (e.KeyCode == KeyCode.B)
            {
                AddItemQuick("Boss");
                e.Handled = true;
            }
            else if (e.KeyCode == KeyCode.R)
            {
                AddItemQuick("Tags");
                e.Handled = true;
            }
            else if (e.KeyCode == KeyCode.C && !e.IsCtrl)
            {
                AddItemQuick("Card");
                e.Handled = true;
            }
            else if (e.KeyCode == KeyCode.Esc)
            {
                // Show menu with options
                var choice = ShowChoiceDialog(
                    "ESC Menu",
                    "What would you like to do?",
                    "Main Menu",
                    "Exit",
                    "Cancel"
                );

                if (choice == 0) // Main Menu
                {
                    // Return to main menu
                    App?.RequestStop();
                }
                else if (choice == 1) // Exit
                {
                    // Exit the entire application
                    App?.RequestStop();
                    Environment.Exit(0);
                }
                // choice == 2 (Cancel) - do nothing, stay in filter builder

                e.Handled = true;
            }
        };

        _mustList.SetFocus();
    }

    private void AddItem(string listType)
    {
        if (_isDialogOpen)
            return; // Prevent re-entrant dialog spawning
        _isDialogOpen = true;

        try
        {
            // Show category selector
            var categoryDialog = new CategorySelectorDialog();
            App?.Run(categoryDialog);

            if (categoryDialog.SelectedCategory != null)
            {
                ShowItemSelectorAndAdd(categoryDialog.SelectedCategory, listType);
            }
        }
        finally
        {
            _isDialogOpen = false;
        }
    }

    private void AddItemQuick(string category)
    {
        if (_isDialogOpen)
            return; // Prevent re-entrant dialog spawning
        _isDialogOpen = true;

        try
        {
            // Determine which list has focus
            string listType = "must"; // default to Filter Items
            if (_shouldList.HasFocus)
                listType = "should";

            ShowItemSelectorAndAdd(category, listType);
        }
        finally
        {
            _isDialogOpen = false;
        }
    }

    private void ShowItemSelectorAndAdd(string category, string listType, bool banItem = false)
    {
        var itemDialog = new ItemSelectorDialog(category);
        App?.Run(itemDialog);

        if (itemDialog.SelectedItem != null)
        {
            var displayText = $"{itemDialog.SelectedItem} ({category})";

            // Check if ban item was selected in dialog
            if (itemDialog.BanItem || banItem)
            {
                _mustNotItems.Add(displayText);
                _statusLabel.Text = $"Banned '{itemDialog.SelectedItem}'";
            }
            else
            {
                switch (listType)
                {
                    case "must":
                        _mustItems.Add(displayText);
                        _mustList.SetSource(new ObservableCollection<string>(_mustItems));
                        _statusLabel.Text = $"Added '{itemDialog.SelectedItem}' to Filter Items";
                        break;
                    case "should":
                        _shouldItems.Add(displayText);
                        _shouldList.SetSource(new ObservableCollection<string>(_shouldItems));
                        _statusLabel.Text = $"Added '{itemDialog.SelectedItem}' to Score Items";
                        break;
                }
            }
        }
    }

    private void RemoveItem(string listType)
    {
        switch (listType)
        {
            case "must":
                var mustSelectedIndex = _mustList.SelectedItem ?? 0;
                if (mustSelectedIndex >= 0 && mustSelectedIndex < _mustItems.Count)
                {
                    _mustItems.RemoveAt(mustSelectedIndex);
                    _mustList.SetSource(new ObservableCollection<string>(_mustItems));
                    _statusLabel.Text = "Item removed from Filter Items";
                }
                break;
            case "should":
                var shouldSelectedIndex = _shouldList.SelectedItem ?? 0;
                if (shouldSelectedIndex >= 0 && shouldSelectedIndex < _shouldItems.Count)
                {
                    _shouldItems.RemoveAt(shouldSelectedIndex);
                    _shouldList.SetSource(new ObservableCollection<string>(_shouldItems));
                    _statusLabel.Text = "Item removed from Score Items";
                }
                break;
        }
    }

    private MotelyJsonConfig.MotleyJsonFilterClause ParseDisplayTextToClause(string displayText)
    {
        // Parse format: "ItemName (Category)"
        var lastParenIndex = displayText.LastIndexOf('(');
        if (lastParenIndex < 0)
            return new MotelyJsonConfig.MotleyJsonFilterClause
            {
                Type = "Joker",
                Value = displayText,
                Antes = new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, // Default: search all antes
            };

        var itemName = displayText.Substring(0, lastParenIndex).Trim();
        var category = displayText.Substring(lastParenIndex + 1).TrimEnd(')').Trim();

        // Map category to type
        var type = category switch
        {
            "Joker" => "Joker",
            "Legendary" => "SoulJoker",
            "Card" => "PlayingCard",
            "Tarot" => "TarotCard",
            "Spectral" => "SpectralCard",
            "Planet" => "PlanetCard",
            "Voucher" => "Voucher",
            "Boss" => "BossBlind",
            "Tags" => "Tag",
            _ => "Joker",
        };

        return new MotelyJsonConfig.MotleyJsonFilterClause
        {
            Type = type,
            Value = itemName,
            Antes = new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, // Default: search all antes
        };
    }

    private string? _loadedFilterPath; // Track loaded filter path for Start Search

    private void LoadFilter()
    {
        var filters = new List<(string name, string format, string fullPath)>();

        // Scan for available filters
        var currentDir = Directory.GetCurrentDirectory();

        // JAML files first (promoted format!)
        if (Directory.Exists(Path.Combine(currentDir, "JamlFilters")))
        {
            var jamlFiles = Directory.GetFiles(Path.Combine(currentDir, "JamlFilters"), "*.jaml");
            foreach (var file in jamlFiles)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                filters.Add((name, "jaml", file));
            }
        }

        // JSON files second
        if (Directory.Exists(Path.Combine(currentDir, "JsonItemFilters")))
        {
            var jsonFiles = Directory.GetFiles(Path.Combine(currentDir, "JsonItemFilters"), "*.json");
            foreach (var file in jsonFiles)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                filters.Add((name, "json", file));
            }
        }

        if (filters.Count == 0)
        {
            ShowErrorDialog("No Filters Found", "No filter files found in JamlFilters/ or JsonItemFilters/");
            return;
        }

        var dialog = new Dialog()
        {
            Title = "Load Filter",
            Width = 60,
            Height = 20,
        };
        dialog.SetScheme(BalatroTheme.Window);

        var instructionLabel = new Label()
        {
            X = Pos.Center(),
            Y = 1,
            Text = "Select a filter to load (JAML first, then JSON):",
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
        filterList.SetScheme(new Scheme()
        {
            Normal = new Attribute(BalatroTheme.White, BalatroTheme.DarkGrey),
            Focus = new Attribute(BalatroTheme.White, BalatroTheme.Blue),
            HotNormal = new Attribute(BalatroTheme.White, BalatroTheme.DarkGrey),
            HotFocus = new Attribute(BalatroTheme.White, BalatroTheme.Blue),
        });
        filterList.SetSource(new ObservableCollection<string>(filterStrings));
        filterList.SelectedItem = 0;

        void DoLoad()
        {
            var selectedIndex = filterList.SelectedItem ?? 0;
            if (selectedIndex >= 0 && selectedIndex < filters.Count)
            {
                var selected = filters[selectedIndex];
                try
                {
                    // Load the config
                    var content = File.ReadAllText(selected.fullPath);
                    MotelyJsonConfig? config = selected.format.ToLower() switch
                    {
                        "json" => ConfigFormatConverter.LoadFromJsonString(content),
                        "jaml" => ConfigFormatConverter.LoadFromJamlString(content),
                        _ => ConfigFormatConverter.LoadFromJsonString(content),
                    };

                    if (config == null)
                    {
                        ShowErrorDialog("Load Error", "Failed to parse filter file");
                        return;
                    }

                    // Clear current items
                    _mustItems.Clear();
                    _shouldItems.Clear();
                    _mustNotItems.Clear();

                    // Load MUST items
                    if (config.Must != null)
                    {
                        foreach (var clause in config.Must)
                        {
                            var category = clause.Type switch
                            {
                                "Joker" => "Joker",
                                "SoulJoker" => "Legendary",
                                "PlayingCard" => "Card",
                                "TarotCard" => "Tarot",
                                "SpectralCard" => "Spectral",
                                "PlanetCard" => "Planet",
                                "Voucher" => "Voucher",
                                "BossBlind" => "Boss",
                                "Tag" => "Tags",
                                _ => "Joker",
                            };
                            _mustItems.Add($"{clause.Value} ({category})");
                        }
                    }

                    // Load SHOULD items
                    if (config.Should != null)
                    {
                        foreach (var clause in config.Should)
                        {
                            var category = clause.Type switch
                            {
                                "Joker" => "Joker",
                                "SoulJoker" => "Legendary",
                                "PlayingCard" => "Card",
                                "TarotCard" => "Tarot",
                                "SpectralCard" => "Spectral",
                                "PlanetCard" => "Planet",
                                "Voucher" => "Voucher",
                                "BossBlind" => "Boss",
                                "Tag" => "Tags",
                                _ => "Joker",
                            };
                            _shouldItems.Add($"{clause.Value} ({category})");
                        }
                    }

                    // Update list views
                    _mustList.SetSource(new ObservableCollection<string>(_mustItems));
                    _shouldList.SetSource(new ObservableCollection<string>(_shouldItems));

                    // Enable Start Search since filter is now loaded
                    _loadedFilterPath = selected.fullPath;
                    _startSearchBtn.Text = " Start Search ";
                    _startSearchBtn.Enabled = true;
                    _startSearchBtn.SetScheme(BalatroTheme.BlueButton);

                    _statusLabel.Text = $"Loaded: {selected.name}.{selected.format}";
                    App?.RequestStop(dialog);
                }
                catch (Exception ex)
                {
                    ShowErrorDialog("Load Error", $"Failed to load filter: {ex.Message}");
                }
            }
        }

        filterList.KeyDown += (sender, e) =>
        {
            if (e.KeyCode == KeyCode.Enter)
            {
                DoLoad();
                e.Handled = true;
            }
        };

        filterList.OpenSelectedItem += (sender, e) => DoLoad();
        dialog.Add(filterList);

        var loadBtn = new CleanButton()
        {
            X = 1,
            Y = Pos.AnchorEnd(3),
            Text = "Load Filter",
            Width = Dim.Fill() - 2,
            TextAlignment = Alignment.Center,
        };
        loadBtn.SetScheme(BalatroTheme.BlueButton);
        loadBtn.Accept += (s, e) => DoLoad();
        dialog.Add(loadBtn);

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

    private void SaveFilter()
    {
        var dialog = new Dialog()
        {
            Title = "Save Filter",
            X = Pos.Center(),
            Y = Pos.Center(),
            Width = 60,
            Height = 10,
        };

        var nameLabel = new Label()
        {
            X = 1,
            Y = 1,
            Text = "Filter Name:",
        };
        dialog.Add(nameLabel);

        var nameField = new TextField()
        {
            X = Pos.Right(nameLabel) + 1,
            Y = 1,
            Width = 30,
            Text = "",
        };
        dialog.Add(nameField);

        var saveBtn = new CleanButton() { Text = " Save " };
        saveBtn.SetScheme(BalatroTheme.BlueButton);
        saveBtn.Accept += (s, e) =>
        {
            var name = nameField.Text;
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowErrorDialog("Error", "Please enter a filter name");
                return;
            }

            try
            {
                // Build MotelyJsonConfig
                var config = new MotelyJsonConfig
                {
                    Name = name,
                    Description = "Created with Filter Builder TUI",
                    Author = Environment.UserName,
                    DateCreated = DateTime.UtcNow,
                    Must = _mustItems.Select(ParseDisplayTextToClause).ToList(),
                    Should = _shouldItems.Select(ParseDisplayTextToClause).ToList(),
                    MustNot = _mustNotItems.Select(ParseDisplayTextToClause).ToList(),
                };

                // Serialize to JAML - skip nulls and empty values for clean output
                var serializer = new SerializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .DisableAliases() // Prevent &o0/*o0 anchor/alias references
                    .ConfigureDefaultValuesHandling(
                        DefaultValuesHandling.OmitNull
                            | DefaultValuesHandling.OmitEmptyCollections
                            | DefaultValuesHandling.OmitDefaults
                    )
                    .Build();
                var jaml = serializer.Serialize(config);

                // Save to file
                var fileName = $"{name.Replace(" ", "_")}.jaml";
                var filePath = Path.Combine(Directory.GetCurrentDirectory(), fileName);
                File.WriteAllText(filePath, jaml);

                _statusLabel.Text = $"Filter '{name}' saved to {fileName}";

                // Enable Start Search button now that filter is saved
                _startSearchBtn.Text = " Start Search ";
                _startSearchBtn.Enabled = true;
                _startSearchBtn.SetScheme(BalatroTheme.BlueButton);

                App?.RequestStop(dialog);
            }
            catch (Exception ex)
            {
                ShowErrorDialog("Error", $"Failed to save filter: {ex.Message}");
            }
        };

        var cancelBtn = new CleanButton()
        {
            X = Pos.Right(saveBtn) + 2,
            Y = Pos.AnchorEnd(1),
            Text = " Back ",
        };
        cancelBtn.SetScheme(BalatroTheme.BackButton);
        cancelBtn.Accept += (s, e) => App?.RequestStop(dialog);

        saveBtn.X = 2;
        saveBtn.Y = Pos.AnchorEnd(1);
        dialog.Add(saveBtn);
        dialog.Add(cancelBtn);

        App?.Run(dialog);
    }

    private void StartSearch()
    {
        // If we loaded a filter directly, use that file
        if (!string.IsNullOrEmpty(_loadedFilterPath) && File.Exists(_loadedFilterPath))
        {
            var format = _loadedFilterPath.EndsWith(".jaml", StringComparison.OrdinalIgnoreCase) ? "jaml" : "json";
            _statusLabel.Text = $"Starting search with loaded filter...";
            var searchWindow = new SearchWindow(_loadedFilterPath, format);
            App?.Run(searchWindow);
            return;
        }

        if (_mustItems.Count == 0 && _shouldItems.Count == 0)
        {
            ShowErrorDialog(
                "Empty Filter",
                "Please add at least one item to MUST or SHOULD lists before starting a search."
            );
            return;
        }

        // Build MotelyJsonConfig
        var config = new MotelyJsonConfig
        {
            Name = "TUI_QuickFilter",
            Description = "Quick filter from TUI",
            Author = Environment.UserName,
            DateCreated = DateTime.UtcNow,
            Must = _mustItems.Select(ParseDisplayTextToClause).ToList(),
            Should = _shouldItems.Select(ParseDisplayTextToClause).ToList(),
            MustNot = _mustNotItems.Select(ParseDisplayTextToClause).ToList(),
        };

        // Serialize to JAML - skip nulls and empty values for clean output
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .DisableAliases() // Prevent &o0/*o0 anchor/alias references
            .ConfigureDefaultValuesHandling(
                DefaultValuesHandling.OmitNull
                    | DefaultValuesHandling.OmitEmptyCollections
                    | DefaultValuesHandling.OmitDefaults
            )
            .Build();
        var jaml = serializer.Serialize(config);

        try
        {
            // Save to temporary file in JamlFilters
            var jamlDir = Path.Combine(Directory.GetCurrentDirectory(), "JamlFilters");
            if (!Directory.Exists(jamlDir))
            {
                Directory.CreateDirectory(jamlDir);
            }

            var fileName = "TUI_QuickFilter.jaml";
            var filePath = Path.Combine(jamlDir, fileName);
            File.WriteAllText(filePath, jaml);

            _statusLabel.Text = $"Starting search with quick filter...";

            // Launch search window with full file path
            var searchWindow = new SearchWindow(filePath, "jaml");
            App?.Run(searchWindow);
        }
        catch (Exception ex)
        {
            ShowErrorDialog("Error", $"Failed to start search: {ex.Message}");
        }
    }

    // Balatro-styled confirmation dialog (Yes/No)
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

    // Balatro-styled error dialog (OK button)
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
}
