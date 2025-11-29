using System.Collections.ObjectModel;
using Terminal.Gui;

namespace Motely.TUI;

public class CategorySelectorDialog : Dialog
{
    public string? SelectedCategory { get; private set; }

    public CategorySelectorDialog()
    {
        Title = "Select Category";
        Width = 45;
        Height = 15;

        SetScheme(BalatroTheme.Window);

        var instructionLabel = new Label()
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill() - 2,
            Text = "Choose a category (hotkey or arrow keys + Enter):",
        };
        Add(instructionLabel);

        var categories = new[]
        {
            "(J) Joker",
            "(L) Legendary Joker (Soul Joker)",
            "(C) Playing Card",
            "(T) Tarot Card",
            "(S) Spectral Card",
            "(P) Planet Card",
            "(V) Voucher",
            "(B) Boss Blind",
            "(R) Reward Tags",
        };

        var listView = new ListView()
        {
            X = 1,
            Y = 3,
            Width = Dim.Fill() - 2,
            Height = Dim.Fill() - 5,
            AllowsMarking = false,
            CanFocus = true,
        };
        listView.SetScheme(BalatroTheme.ListView);
        listView.SetSource(new ObservableCollection<string>(categories));
        listView.SelectedItem = 0; // Select first item by default

        // Handle Enter key and hotkeys for selection
        listView.KeyDown += (sender, e) =>
        {
            if (e.KeyCode == KeyCode.Enter)
            {
                SelectedCategory = GetCategoryFromIndex(listView.SelectedItem ?? 0);
                App?.RequestStop(this);
                e.Handled = true;
                return;
            }

            // Hotkeys - check for category letter keys
            var key = char.ToUpper((char)e.KeyCode);
            string? category = key switch
            {
                'J' => "Joker",
                'L' => "Legendary",
                'C' => "Card",
                'T' => "Tarot",
                'S' => "Spectral",
                'P' => "Planet",
                'V' => "Voucher",
                'B' => "Boss",
                'R' => "Tags",
                _ => null,
            };

            if (category != null)
            {
                SelectedCategory = category;
                App?.RequestStop(this);
                e.Handled = true;
            }
        };

        // Handle mouse click for selection
        listView.OpenSelectedItem += (s, e) =>
        {
            SelectedCategory = GetCategoryFromIndex(listView.SelectedItem ?? 0);
            App?.RequestStop(this);
        };

        Add(listView);

        var cancelBtn = new CleanButton()
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill() - 2,
            Text = "Back",
        };
        cancelBtn.SetScheme(BalatroTheme.BackButton);
        cancelBtn.Accept += (s, e) =>
        {
            SelectedCategory = null;
            App?.RequestStop(this);
        };
        Add(cancelBtn);

        listView.SetFocus();
    }

    private string GetCategoryFromIndex(int index)
    {
        return index switch
        {
            0 => "Joker",
            1 => "Legendary",
            2 => "Card",
            3 => "Tarot",
            4 => "Spectral",
            5 => "Planet",
            6 => "Voucher",
            7 => "Boss",
            8 => "Tags",
            _ => "Joker",
        };
    }
}
