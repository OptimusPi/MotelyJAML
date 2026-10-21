using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace Motely;

/// <summary>
/// Shared formatting utilities
/// </summary>
public static class FormatUtils
{
    public static string FormatItem(MotelyItem item)
    {
        var result = new StringBuilder();

        // Add stickers (Eternal, Perishable, Rental) FIRST
        if (item.IsEternal)
        {
            result.Append("Eternal ");
        }
        if (item.IsPerishable)
        {
            result.Append("Perishable ");
        }
        if (item.IsRental)
        {
            result.Append("Rental ");
        }

        // Add seal for standard cards (BEFORE edition)
        if (item.Seal != MotelyItemSeal.None)
        {
            result.Append(item.Seal.ToString().Replace("Seal", "")).Append(" Seal ");
        }

        // Add edition if present (AFTER seal)
        if (item.Edition != MotelyItemEdition.None)
        {
            result.Append(item.Edition).Append(' ');
        }

        // Add enhancement for standard cards
        if (item.Enhancement != MotelyItemEnhancement.None)
        {
            result.Append(item.Enhancement).Append(' ');
        }

        // Format based on type
        switch (item.TypeCategory)
        {
            case MotelyItemTypeCategory.Standardcard:
                var standardcard = (MotelyStandardCard)(
                    item.Value & MotelyGlobals.ItemTypeMask & ~MotelyGlobals.ItemTypeCategoryMask
                );
                result.Append(FormatStandardcard(standardcard));
                break;

            default:
                // For all other types, just use the Type enum value and format it
                result.Append(FormatDisplayName(item.Type.ToString()));
                break;
        }

        return result.ToString().Trim();
    }

    public static string FormatBoss(MotelyBossBlind boss)
    {
        return FormatDisplayName(boss.ToString());
    }

    public static string FormatVoucher(MotelyVoucher voucher)
    {
        return FormatDisplayName(voucher.ToString());
    }

    public static string FormatTag(MotelyTag tag)
    {
        var name = tag.ToString();
        // Special case for TopupTag
        if (name == "TopupTag")
            return "Top-up Tag";
        if (name.EndsWith("Tag"))
            name = name.Substring(0, name.Length - 3) + " Tag";
        return name;
    }

    public static string FormatDisplayName(string enumName)
    {
        // Special cases that need custom formatting
        var specialCases = new Dictionary<string, string>
        {
            // Numbers
            { "EightBall", "8 Ball" },
            { "Cloud9", "Cloud 9" },
            { "OopsAll6s", "Oops! All 6s" },
            // Multi-word special formatting
            { "ToTheMoon", "To the Moon" },
            { "ToDoList", "To Do List" },
            { "RiffRaff", "Riff-raff" },
            { "MailInRebate", "Mail In Rebate" },
            { "TheWheel", "The Wheel" },
            { "TheWheelOfFortune", "The Wheel of Fortune" },
            { "SockandBuskin", "Sock and Buskin" },
            { "SockAndBuskin", "Sock and Buskin" },
            { "DriversLicense", "Driver's License" },
            { "DirectorsCut", "Director's Cut" },
            { "PlanetX", "Planet X" },
            // Spectral cards
            { "Soul", "The Soul" },
            // Other special formatting
            { "MrBones", "Mr. Bones" },
            { "ChaostheClown", "Chaos the Clown" },
            // But Immolate test data has these variations we need to match:
            { "ChaosTheClown", "Chaosthe Clown" }, // Weird but matches verified output
            { "ShootTheMoon", "Shoot the Moon" },
            { "RideTheBus", "Ride the Bus" },
            { "HitTheRoad", "Hit the Road" },
            { "TheVerdant", "Verdant Leaf" },
            { "VerdantLeaf", "Verdant Leaf" },
            { "VioletVessel", "Violet Vessel" },
            { "CrimsonHeart", "Crimson Heart" },
            { "AmberAcorn", "Amber Acorn" },
            { "CeruleanBell", "Cerulean Bell" },
            { "TheFool", "The Fool" },
            { "TheMagician", "The Magician" },
            { "TheHighPriestess", "The High Priestess" },
            { "TheEmpress", "The Empress" },
            { "TheEmperor", "The Emperor" },
            { "TheHierophant", "The Hierophant" },
            { "TheLovers", "The Lovers" },
            { "TheChariot", "The Chariot" },
            { "TheHermit", "The Hermit" },
            { "Justice", "Justice" },
            { "TheJustice", "Justice" },
            { "TheHangedMan", "The Hanged Man" },
            { "Death", "Death" },
            { "TheDeath", "Death" },
            { "Temperance", "Temperance" },
            { "TheTemperance", "Temperance" },
            { "TheDevil", "The Devil" },
            { "TheTower", "The Tower" },
            { "TheStar", "The Star" },
            { "TheMoon", "The Moon" },
            { "TheSun", "The Sun" },
            { "Judgement", "Judgement" },
            { "TheWorld", "The World" },
            { "Strength", "Strength" },
        };

        if (specialCases.TryGetValue(enumName, out var special))
        {
            return special;
        }

        // Add spaces before capital letters (except the first one)
        var result = string.Empty;
        for (int i = 0; i < enumName.Length; i++)
        {
            if (i > 0 && char.IsUpper(enumName[i]) && !char.IsUpper(enumName[i - 1]))
            {
                result += " ";
            }
            result += enumName[i];
        }

        return result;
    }

    public static string FormatJokerName(MotelyJoker joker)
    {
        var name = joker.ToString();
        return FormatDisplayName(name);
    }

    public static string FormatStandardcardSuit(string suitAbbreviation)
    {
        return suitAbbreviation switch
        {
            "C" => "Clubs",
            "D" => "Diamonds",
            "H" => "Hearts",
            "S" => "Spades",
            _ => "Unknown",
        };
    }

    public static string FormatStandardcardRank(string rankAbbreviation)
    {
        return rankAbbreviation switch
        {
            "2" => "2",
            "3" => "3",
            "4" => "4",
            "5" => "5",
            "6" => "6",
            "7" => "7",
            "8" => "8",
            "9" => "9",
            "10" => "10",
            "J" => "Jack",
            "Q" => "Queen",
            "K" => "King",
            "A" => "Ace",
            _ => throw new ArgumentOutOfRangeException(
                nameof(rankAbbreviation),
                $"Invalid rank abbreviation: {rankAbbreviation}"
            ),
        };
    }

    public static string FormatStandardcard(MotelyStandardCard card)
    {
        var rank = card.GetRank();
        var suit = card.GetSuit();

        var rankStr = rank switch
        {
            MotelyStandardcardRank.Two => "2",
            MotelyStandardcardRank.Three => "3",
            MotelyStandardcardRank.Four => "4",
            MotelyStandardcardRank.Five => "5",
            MotelyStandardcardRank.Six => "6",
            MotelyStandardcardRank.Seven => "7",
            MotelyStandardcardRank.Eight => "8",
            MotelyStandardcardRank.Nine => "9",
            MotelyStandardcardRank.Ten => "10",
            MotelyStandardcardRank.Jack => "Jack",
            MotelyStandardcardRank.Queen => "Queen",
            MotelyStandardcardRank.King => "King",
            MotelyStandardcardRank.Ace => "Ace",
            _ => throw new InvalidEnumArgumentException(
                nameof(rank),
                (int)rank,
                typeof(MotelyStandardcardRank)
            ),
        };

        var suitStr = suit switch
        {
            MotelyStandardcardSuit.Clubs => "Clubs",
            MotelyStandardcardSuit.Diamonds => "Diamonds",
            MotelyStandardcardSuit.Hearts => "Hearts",
            MotelyStandardcardSuit.Spades => "Spades",
            _ => throw new InvalidEnumArgumentException(
                nameof(rank),
                (int)rank,
                typeof(MotelyStandardcardRank)
            ),
        };

        return $"{rankStr} of {suitStr}";
    }

    public static string FormatPackName(MotelyBoosterPack pack)
    {
        return pack switch
        {
            MotelyBoosterPack.Arcana => "Arcana Pack",
            MotelyBoosterPack.JumboArcana => "Jumbo Arcana Pack",
            MotelyBoosterPack.MegaArcana => "Mega Arcana Pack",
            MotelyBoosterPack.Celestial => "Celestial Pack",
            MotelyBoosterPack.JumboCelestial => "Jumbo Celestial Pack",
            MotelyBoosterPack.MegaCelestial => "Mega Celestial Pack",
            MotelyBoosterPack.Spectral => "Spectral Pack",
            MotelyBoosterPack.JumboSpectral => "Jumbo Spectral Pack",
            MotelyBoosterPack.MegaSpectral => "Mega Spectral Pack",
            MotelyBoosterPack.Buffoon => "Buffoon Pack",
            MotelyBoosterPack.JumboBuffoon => "Jumbo Buffoon Pack",
            MotelyBoosterPack.MegaBuffoon => "Mega Buffoon Pack",
            MotelyBoosterPack.Standard => "Standard Pack",
            MotelyBoosterPack.JumboStandard => "Jumbo Standard Pack",
            MotelyBoosterPack.MegaStandard => "Mega Standard Pack",
            _ => pack.ToString(),
        };
    }

    /// <summary>
    /// Parses a string produced by <see cref="FormatItem"/>: sticker order
    /// <see cref="FormatItem"/>, then optional seal, then edition, enhancement, then type
    /// (<see cref="FormatDisplayName"/> for non–playing-card types, <see cref="FormatStandardcard"/> for cards).
    /// </summary>
    public static MotelyItem ParseMotelyItem(string s)
    {
        if (!TryParseMotelyItem(s, out var item))
            throw new FormatException($"Unrecognized motely item string: '{s}'.");
        return item;
    }

    /// <inheritdoc cref="ParseMotelyItem"/>
    public static bool TryParseMotelyItem(string s, out MotelyItem item)
    {
        item = default;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        var str = s.Trim();

        // Stickers — same order as FormatItem: Eternal, Perishable, Rental
        var eternal = TryStripPrefix(ref str, "Eternal ", StringComparison.OrdinalIgnoreCase);
        var perishable = TryStripPrefix(ref str, "Perishable ", StringComparison.OrdinalIgnoreCase);
        var rental = TryStripPrefix(ref str, "Rental ", StringComparison.OrdinalIgnoreCase);

        var seal = MotelyItemSeal.None;
        foreach (MotelyItemSeal se in Enum.GetValues<MotelyItemSeal>())
        {
            if (se == MotelyItemSeal.None)
                continue;
            var p = se.ToString().Replace("Seal", "", StringComparison.Ordinal) + " Seal ";
            if (!str.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                continue;
            seal = se;
            str = str[p.Length..].TrimStart();
            break;
        }

        // Pure type (no edition/enhancement prefix) — e.g. "2 of Clubs", "The World"
        if (
            TryResolveMotelyTypeTail(
                str,
                MotelyItemEdition.None,
                MotelyItemEnhancement.None,
                seal,
                eternal,
                perishable,
                rental,
                out item
            )
        )
            return true;

        var words = str.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count == 0)
            return false;

        var edition = MotelyItemEdition.None;
        var enhancement = MotelyItemEnhancement.None;

        if (
            words.Count > 0
            && TryParseEnumMemberName(words[0], out MotelyItemEdition ed)
            && ed != MotelyItemEdition.None
        )
        {
            edition = ed;
            words.RemoveAt(0);
        }

        // Before treating the next word as an enhancement, try the remaining tail as a
        // type: display names can start with an enhancement word ("Lucky Cat"), and the
        // greedy strip would otherwise leave an unresolvable "Cat".
        if (
            edition != MotelyItemEdition.None
            && TryResolveMotelyTypeTail(
                string.Join(' ', words),
                edition,
                MotelyItemEnhancement.None,
                seal,
                eternal,
                perishable,
                rental,
                out item
            )
        )
            return true;

        if (
            words.Count > 0
            && TryParseEnumMemberName(words[0], out MotelyItemEnhancement eh)
            && eh != MotelyItemEnhancement.None
        )
        {
            enhancement = eh;
            words.RemoveAt(0);
        }

        var typeTail = string.Join(' ', words);
        if (string.IsNullOrWhiteSpace(typeTail))
            return false;

        return TryResolveMotelyTypeTail(
            typeTail,
            edition,
            enhancement,
            seal,
            eternal,
            perishable,
            rental,
            out item
        );
    }

    /// <summary>Enum.TryParse by member name only (rejects numeric strings like &quot;7&quot;).</summary>
    private static bool TryParseEnumMemberName<TEnum>(string word, out TEnum value)
        where TEnum : struct, Enum
    {
        value = default;
        foreach (var name in Enum.GetNames<TEnum>())
        {
            if (!string.Equals(name, word, StringComparison.OrdinalIgnoreCase))
                continue;
            return Enum.TryParse(name, ignoreCase: false, out value);
        }

        return false;
    }

    private static bool TryStripPrefix(ref string str, string prefix, StringComparison comparison)
    {
        if (!str.StartsWith(prefix, comparison))
            return false;
        str = str[prefix.Length..].TrimStart();
        return true;
    }

    private static bool TryResolveMotelyTypeTail(
        string typeTail,
        MotelyItemEdition edition,
        MotelyItemEnhancement enhancement,
        MotelyItemSeal seal,
        bool eternal,
        bool perishable,
        bool rental,
        out MotelyItem item
    )
    {
        item = default;

        foreach (MotelyStandardCard card in Enum.GetValues<MotelyStandardCard>())
        {
            if (
                string.Equals(
                    FormatStandardcard(card),
                    typeTail,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                item = new MotelyItem(card);
                ApplyMotelyItemModifiers(
                    ref item,
                    seal,
                    edition,
                    enhancement,
                    eternal,
                    perishable,
                    rental
                );
                return true;
            }
        }

        foreach (MotelyItemType t in Enum.GetValues<MotelyItemType>())
        {
            if (
                string.Equals(
                    FormatDisplayName(t.ToString()),
                    typeTail,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                item = new MotelyItem(t);
                ApplyMotelyItemModifiers(
                    ref item,
                    seal,
                    edition,
                    enhancement,
                    eternal,
                    perishable,
                    rental
                );
                return true;
            }
        }

        if (Enum.TryParse<MotelyItemType>(typeTail, ignoreCase: true, out var raw))
        {
            item = new MotelyItem(raw);
            ApplyMotelyItemModifiers(
                ref item,
                seal,
                edition,
                enhancement,
                eternal,
                perishable,
                rental
            );
            return true;
        }

        return false;
    }

    private static void ApplyMotelyItemModifiers(
        ref MotelyItem item,
        MotelyItemSeal seal,
        MotelyItemEdition edition,
        MotelyItemEnhancement enhancement,
        bool eternal,
        bool perishable,
        bool rental
    )
    {
        if (seal != MotelyItemSeal.None)
            item = item.WithSeal(seal);
        if (edition != MotelyItemEdition.None)
            item = item.WithEdition(edition);
        if (enhancement != MotelyItemEnhancement.None)
            item = item.WithEnhancement(enhancement);
        if (perishable)
            item = item.WithPerishable(true);
        if (eternal)
            item = item.WithEternal(true);
        if (rental)
            item = item.WithRental(true);
    }
}
