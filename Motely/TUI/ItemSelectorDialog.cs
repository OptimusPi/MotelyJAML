using System.Collections.ObjectModel;
using Motely.Filters;
using Terminal.Gui;

namespace Motely.TUI;

public class ItemSelectorDialog : Dialog
{
    public string? SelectedItem { get; private set; }
    public bool BanItem { get; private set; } = false;

    public ItemSelectorDialog(string category)
    {
        Title = $"Select {category}";
        Width = 50;
        Height = 24;

        SetScheme(BalatroTheme.Window);

        var items = GetItemsForCategory(category);
        var itemStrings = items.Select((item, index) => $"{item}").ToArray();
        bool banToggled = false;

        // Selection preview label - shows current highlighted item
        var selectionLabel = new Label()
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill() - 2,
            Text = $"Selected: {items[0]}",
        };
        selectionLabel.SetScheme(new Scheme()
        {
            Normal = new Attribute(BalatroTheme.Green, BalatroTheme.ModalGrey),
        });
        Add(selectionLabel);

        var listView = new ListView()
        {
            X = 1,
            Y = 3,
            Width = Dim.Fill() - 2,
            Height = Dim.Fill() - 9,
            AllowsMarking = false,
            CanFocus = true,
        };
        listView.SetScheme(BalatroTheme.ListView);
        listView.SetSource(new ObservableCollection<string>(itemStrings));
        listView.SelectedItem = 0;

        // Update selection label when navigating
        listView.SelectedItemChanged += (s, e) =>
        {
            var idx = listView.SelectedItem ?? 0;
            if (idx >= 0 && idx < items.Length)
            {
                selectionLabel.Text = $"Selected: {items[idx]}";
            }
        };

        // Double-click to select
        listView.OpenSelectedItem += (s, e) =>
        {
            var selectedIndex = listView.SelectedItem ?? 0;
            if (selectedIndex >= 0 && selectedIndex < items.Length)
            {
                SelectedItem = items[selectedIndex];
                BanItem = banToggled;
                App?.RequestStop(this);
            }
        };

        Add(listView);

        // Ban Item toggle button (styled like a checkbox)
        var banBtn = new CleanButton()
        {
            X = 1,
            Y = Pos.AnchorEnd(5),
            Text = " [ ] Ban Item (B) ",
        };
        banBtn.SetScheme(new Scheme()
        {
            Normal = new Attribute(BalatroTheme.Red, BalatroTheme.ModalGrey),
            Focus = new Attribute(BalatroTheme.White, BalatroTheme.DarkRed),
            HotNormal = new Attribute(BalatroTheme.Red, BalatroTheme.ModalGrey),
            HotFocus = new Attribute(BalatroTheme.White, BalatroTheme.DarkRed),
        });
        banBtn.Accept += (s, e) =>
        {
            banToggled = !banToggled;
            banBtn.Text = banToggled ? " [X] Ban Item (B) " : " [ ] Ban Item (B) ";
        };
        Add(banBtn);

        // ADD button - blue, above Back
        var addBtn = new CleanButton()
        {
            X = 1,
            Y = Pos.AnchorEnd(3),
            Width = Dim.Fill() - 2,
            Text = "Add to Filter",
            TextAlignment = Alignment.Center,
        };
        addBtn.SetScheme(BalatroTheme.BlueButton);
        addBtn.Accept += (s, e) =>
        {
            var selectedIndex = listView.SelectedItem ?? 0;
            if (selectedIndex >= 0 && selectedIndex < items.Length)
            {
                SelectedItem = items[selectedIndex];
                BanItem = banToggled;
                App?.RequestStop(this);
            }
        };
        Add(addBtn);

        // Back button - orange
        var cancelBtn = new CleanButton()
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill() - 2,
            Text = "Back",
            TextAlignment = Alignment.Center,
        };
        cancelBtn.SetScheme(BalatroTheme.BackButton);
        cancelBtn.Accept += (s, e) =>
        {
            SelectedItem = null;
            App?.RequestStop(this);
        };
        Add(cancelBtn);

        // Handle keyboard shortcuts
        listView.KeyDown += (sender, e) =>
        {
            if (e.KeyCode == KeyCode.Enter)
            {
                var selectedIndex = listView.SelectedItem ?? 0;
                if (selectedIndex >= 0 && selectedIndex < items.Length)
                {
                    SelectedItem = items[selectedIndex];
                    BanItem = banToggled;
                    App?.RequestStop(this);
                }
                e.Handled = true;
            }
            else if (e.KeyCode == KeyCode.B)
            {
                // Quick ban: select item and mark as banned
                var selectedIndex = listView.SelectedItem ?? 0;
                if (selectedIndex >= 0 && selectedIndex < items.Length)
                {
                    SelectedItem = items[selectedIndex];
                    BanItem = true;
                    App?.RequestStop(this);
                }
                e.Handled = true;
            }
        };

        listView.SetFocus();
    }

    private string[] GetItemsForCategory(string category)
    {
        return category switch
        {
            "Joker" => GetJokers(),
            "Legendary" => GetLegendaryJokers(),
            "Card" => GetPlayingCards(),
            "Tarot" => GetTarots(),
            "Spectral" => GetSpectrals(),
            "Planet" => GetPlanets(),
            "Voucher" => GetVouchers(),
            "Boss" => GetBosses(),
            "Tags" => GetTags(),
            _ => GetJokers(),
        };
    }

    private string[] GetJokers()
    {
        // Get all joker names from MotelyJoker enum
        return Enum.GetNames(typeof(MotelyJoker)).OrderBy(x => x).ToArray();
    }

    private string[] GetLegendaryJokers()
    {
        // Soul jokers are legendary jokers (the 5 legendary souls in Balatro)
        return Enum.GetNames(typeof(MotelyJoker))
            .Where(j =>
                j == "Perkeo" || j == "Triboulet" || j == "Yorick" || j == "Chicot" || j == "Canio"
            )
            .OrderBy(x => x)
            .ToArray();
    }

    private string[] GetPlayingCards()
    {
        // Generate all playing cards (Ace through King, all suits)
        var ranks = new[]
        {
            "Ace",
            "2",
            "3",
            "4",
            "5",
            "6",
            "7",
            "8",
            "9",
            "10",
            "Jack",
            "Queen",
            "King",
        };
        var suits = new[] { "Spades", "Hearts", "Diamonds", "Clubs" };
        return (from suit in suits from rank in ranks select $"{rank} of {suit}").ToArray();
    }

    private string[] GetTarots()
    {
        return Enum.GetNames(typeof(MotelyTarotCard)).OrderBy(x => x).ToArray();
    }

    private string[] GetSpectrals()
    {
        return Enum.GetNames(typeof(MotelySpectralCard)).OrderBy(x => x).ToArray();
    }

    private string[] GetPlanets()
    {
        return Enum.GetNames(typeof(MotelyPlanetCard)).OrderBy(x => x).ToArray();
    }

    private string[] GetVouchers()
    {
        return Enum.GetNames(typeof(MotelyVoucher)).OrderBy(x => x).ToArray();
    }

    private string[] GetBosses()
    {
        return Enum.GetNames(typeof(MotelyBossBlind)).OrderBy(x => x).ToArray();
    }

    private string[] GetTags()
    {
        return Enum.GetNames(typeof(MotelyTag)).OrderBy(x => x).ToArray();
    }
}
