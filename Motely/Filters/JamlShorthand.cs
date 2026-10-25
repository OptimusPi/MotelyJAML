using System.Globalization;
using System.Text;

namespace Motely.Filters;

/// <summary>
/// A clause written as one line of text instead of a mapping. A list item in <c>must</c>,
/// <c>should</c>, <c>mustNot</c> (or an <c>and:</c>/<c>or:</c> list) that is a plain string is
/// read here and becomes exactly the clause its mapping twin would:
/// <code>
/// [Edition] &lt;Name&gt; in ante|antes &lt;ante list&gt;
///
/// - Perkeo in ante 1 or 2              → legendaryJoker: Perkeo,  antes: [1, 2]
/// - The Eye in ante 3                  → boss: TheEye,            antes: [3]
/// - Negative Blueprint in antes 1-3    → joker: Blueprint, edition: Negative, antes: [1, 2, 3]
/// - Black Hole in ante 2, 3 and 4      → spectralCard: BlackHole, antes: [2, 3, 4]
/// </code>
/// <para>
/// <b>Name</b>: an engine name from one of the families jokers, bosses, vouchers, tags, tarot,
/// planet and spectral cards, matched ignoring case, spaces and punctuation ("the eye" is TheEye,
/// "Charm Tag" is CharmTag). A legendary joker becomes a <c>legendaryJoker</c> clause, any other
/// joker a <c>joker</c> clause. <b>Edition</b> (Foil, Holographic, Polychrome, Negative) is an
/// optional first word, for jokers only. <b>Ante list</b>: numbers and inclusive ranges
/// (<c>1-3</c>) separated by commas, <c>or</c> or <c>and</c>; <c>in ante</c> is required.
/// The antes come out ascending, without repeats. Every other key keeps its default (score 1,
/// min 1, default sources); anything more needs the mapping form.
/// </para>
/// No reflection and no Regex: the names come from <see cref="Enum.GetValues{TEnum}"/> per
/// family, so it runs unchanged under NativeAOT.
/// </summary>
internal static class JamlShorthand
{
    internal enum Family { Joker, Boss, Voucher, Tag, Tarot, Planet, Spectral }

    /// <summary>One name the shorthand can resolve: its family, engine name and enum value.</summary>
    internal readonly record struct Entry(Family Family, string Name, int Value);

    private const string Forms = "`ante 1`, `antes 1 or 2`, `antes 1, 2 and 3` or `antes 1-3`";

    /// <summary>Largest ante the shorthand reads; it keeps a typo'd range (<c>1-1000000</c>) from allocating.</summary>
    private const int MaxAnte = 999;

    private static readonly MotelyItemEdition[] Editions =
        [MotelyItemEdition.Foil, MotelyItemEdition.Holographic, MotelyItemEdition.Polychrome, MotelyItemEdition.Negative];

    private static readonly Dictionary<string, Entry[]> EngineIndex = BuildIndex(EngineEntries());

    /// <summary>Every name of every family the shorthand knows, straight off the engine's enums.</summary>
    internal static IEnumerable<Entry> EngineEntries()
    {
        foreach (var v in Enum.GetValues<MotelyJoker>()) yield return new(Family.Joker, v.ToString(), (int)v);
        foreach (var v in Enum.GetValues<MotelyBossBlind>()) yield return new(Family.Boss, v.ToString(), (int)v);
        foreach (var v in Enum.GetValues<MotelyVoucher>()) yield return new(Family.Voucher, v.ToString(), (int)v);
        foreach (var v in Enum.GetValues<MotelyTag>()) yield return new(Family.Tag, v.ToString(), (int)v);
        foreach (var v in Enum.GetValues<MotelyTarotCard>()) yield return new(Family.Tarot, v.ToString(), (int)v);
        foreach (var v in Enum.GetValues<MotelyPlanetCard>()) yield return new(Family.Planet, v.ToString(), (int)v);
        foreach (var v in Enum.GetValues<MotelySpectralCard>()) yield return new(Family.Spectral, v.ToString(), (int)v);
    }

    /// <summary>Normalized name → every entry spelled that way (more than one is an ambiguous name).</summary>
    internal static Dictionary<string, Entry[]> BuildIndex(IEnumerable<Entry> entries) =>
        entries
            .GroupBy(e => Normalize(e.Name), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);

    /// <summary>Reads one shorthand clause. A <see cref="FormatException"/> carries the one-line correction.</summary>
    public static IMotelyClause Parse(string text) => Parse(text, EngineIndex);

    internal static IMotelyClause Parse(string text, Dictionary<string, Entry[]> index)
    {
        var line = text.Trim();
        if (line.Length == 0)
            throw new FormatException("an empty clause: write one like `Perkeo in ante 1`, or a mapping like `joker: Perkeo`.");

        var words = Words(line);
        int at = -1;
        for (int i = words.Count - 2; i >= 0; i--)
        {
            if (Is(line, words[i], "in") && IsAnteWord(line, words[i + 1]))
            {
                at = i;
                break;
            }
        }

        if (at < 0)
        {
            int ante = words.FindIndex(w => IsAnteWord(line, w));
            if (ante > 0)
            {
                var before = line[..words[ante].Start].TrimEnd();
                throw new FormatException(
                    $"'{line}': write `in` before the antes, as in `{before} in {line[words[ante].Start..]}`.");
            }
            int bareIn = words.FindLastIndex(w => Is(line, w, "in"));
            if (bareIn > 0 && bareIn + 1 < words.Count && char.IsAsciiDigit(line[words[bareIn + 1].Start]))
            {
                var before = line[..words[bareIn].Start].TrimEnd();
                throw new FormatException(
                    $"'{line}': write `ante` after `in`, as in `{before} in ante {line[words[bareIn + 1].Start..]}`.");
            }
            throw new FormatException(
                $"'{line}' says no antes: add `in ante N`, as in `{line} in ante 1`, or `in antes 1-8` for any ante.");
        }

        var name = line[..words[at].Start].TrimEnd();
        if (name.Length == 0)
            throw new FormatException($"'{line}' names nothing before `in`: write `<name> in ante N`, as in `Perkeo in ante 1`.");
        var antes = ParseAntes(line[(words[at + 1].Start + words[at + 1].Length)..]);

        var (entry, edition) = Resolve(name, index);
        return Build(entry, edition, antes);
    }

    // ── Name ─────────────────────────────────────────────────────────────────────────────

    private static (Entry Entry, MotelyItemEdition? Edition) Resolve(string name, Dictionary<string, Entry[]> index)
    {
        // The whole name first: "Negative Tag" is a tag, not a Negative "Tag".
        if (index.TryGetValue(Normalize(name), out var whole))
            return (Single(name, whole), null);

        MotelyItemEdition? edition = null;
        var bare = name;
        int space = name.IndexOf(' ');
        if (space > 0)
        {
            foreach (var e in Editions)
            {
                if (name.AsSpan(0, space).Equals(e.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    edition = e;
                    bare = name[(space + 1)..].Trim();
                    break;
                }
            }
        }

        if (edition is { } ed && index.TryGetValue(Normalize(bare), out var found))
        {
            var entry = Single(bare, found);
            if (entry.Family != Family.Joker)
                throw new FormatException(
                    $"'{name}': only a joker takes an edition, and {entry.Name} is {Article(entry.Family)}; drop `{ed}`.");
            return (entry, ed);
        }

        var suggestions = Closest(Normalize(bare), index);
        throw new FormatException(
            suggestions.Count == 0
                ? $"'{bare}' is not a joker, boss, voucher, tag, tarot, planet or spectral card name."
                : $"'{bare}' is not a known name; did you mean {JoinOr(suggestions)}?");
    }

    private static Entry Single(string name, Entry[] entries)
    {
        if (entries.Length == 1)
            return entries[0];
        var each = entries.Select(e => $"{Article(e.Family)} (`{MappingKey(e)}: {e.Name}`)");
        throw new FormatException(
            $"'{name}' is {string.Join(" and ", each)}: use the mapping form to say which, e.g. `{MappingKey(entries[0])}: {entries[0].Name}`.");
    }

    /// <summary>Up to three names nearest by edit distance, then by those that contain the text.</summary>
    private static List<string> Closest(string key, Dictionary<string, Entry[]> index)
    {
        if (key.Length == 0)
            return [];
        int limit = Math.Max(2, key.Length / 3);
        return index
            .Select(kv => (kv.Value[0].Name, Distance: kv.Key.Contains(key, StringComparison.Ordinal) || key.Contains(kv.Key, StringComparison.Ordinal)
                ? Math.Min(limit, Levenshtein(key, kv.Key))
                : Levenshtein(key, kv.Key)))
            .Where(c => c.Distance <= limit)
            .OrderBy(c => c.Distance)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .Take(3)
            .Select(c => c.Name)
            .ToList();
    }

    private static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
            previous[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }

    /// <summary>Letters and digits only, lower case: "The Eye", "the_eye" and "TheEye" are one name.</summary>
    internal static string Normalize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    // ── Antes ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>item (sep item)*</c>, where an item is <c>N</c> or <c>N-M</c> and a separator is
    /// <c>,</c>, <c>or</c>, <c>and</c>, <c>, or</c> or <c>, and</c>.
    /// </summary>
    internal static int[] ParseAntes(string text)
    {
        var list = text.Trim();
        if (list.Length == 0)
            throw new FormatException($"no ante numbers after `ante`: write {Forms}.");
        var antes = new SortedSet<int>();
        int pos = 0;
        bool expectItem = true;
        bool sawComma = false;

        while (true)
        {
            SkipSpaces(list, ref pos);
            if (pos >= list.Length)
                break;

            if (expectItem)
            {
                int lo = ReadNumber(list, ref pos, text);
                int hi = lo;
                SkipSpaces(list, ref pos);
                if (pos < list.Length && list[pos] is '-' or '–')
                {
                    pos++;
                    SkipSpaces(list, ref pos);
                    hi = ReadNumber(list, ref pos, text);
                    if (hi < lo)
                        throw new FormatException($"ante range '{lo}-{hi}' runs backwards: write {hi}-{lo}.");
                }
                for (int a = lo; a <= hi; a++)
                    antes.Add(a);
                expectItem = false;
                sawComma = false;
                continue;
            }

            if (list[pos] == ',' && !sawComma)
            {
                pos++;
                sawComma = true;
                // A comma alone separates; ", or" / ", and" too.
                SkipSpaces(list, ref pos);
                if (TryWord(list, ref pos, "or") || TryWord(list, ref pos, "and")) { }
                expectItem = true;
                continue;
            }
            if (TryWord(list, ref pos, "or") || TryWord(list, ref pos, "and"))
            {
                expectItem = true;
                continue;
            }
            throw BadAntes(text);
        }

        if (antes.Count == 0 || expectItem)
            throw BadAntes(text);
        return [.. antes];
    }

    private static int ReadNumber(string list, ref int pos, string text)
    {
        int start = pos;
        while (pos < list.Length && char.IsAsciiDigit(list[pos]))
            pos++;
        if (pos == start)
            throw BadAntes(text);
        if (pos - start > 3)
            throw new FormatException($"ante '{list[start..pos]}' is out of range: antes run from 0 to {MaxAnte}.");
        // A number glued to letters ("3rd", "1x") is not an ante.
        if (pos < list.Length && char.IsLetter(list[pos]))
            throw BadAntes(text);
        return int.Parse(list.AsSpan(start, pos - start), NumberStyles.None, CultureInfo.InvariantCulture);
    }

    private static bool TryWord(string list, ref int pos, string word)
    {
        if (pos + word.Length > list.Length
            || !list.AsSpan(pos, word.Length).Equals(word, StringComparison.OrdinalIgnoreCase)
            || pos + word.Length < list.Length && char.IsLetter(list[pos + word.Length]))
            return false;
        pos += word.Length;
        return true;
    }

    private static void SkipSpaces(string list, ref int pos)
    {
        while (pos < list.Length && char.IsWhiteSpace(list[pos]))
            pos++;
    }

    private static FormatException BadAntes(string text) =>
        new($"ante list '{text.Trim()}' is not one the shorthand reads: write {Forms}.");

    // ── Words ────────────────────────────────────────────────────────────────────────────

    private readonly record struct Word(int Start, int Length);

    private static List<Word> Words(string line)
    {
        var words = new List<Word>();
        int i = 0;
        while (i < line.Length)
        {
            while (i < line.Length && char.IsWhiteSpace(line[i]))
                i++;
            int start = i;
            while (i < line.Length && !char.IsWhiteSpace(line[i]))
                i++;
            if (i > start)
                words.Add(new Word(start, i - start));
        }
        return words;
    }

    private static bool Is(string line, Word word, string text) =>
        line.AsSpan(word.Start, word.Length).Equals(text, StringComparison.OrdinalIgnoreCase);

    private static bool IsAnteWord(string line, Word word) => Is(line, word, "ante") || Is(line, word, "antes");

    // ── Clause ───────────────────────────────────────────────────────────────────────────

    private static IMotelyClause Build(Entry entry, MotelyItemEdition? edition, int[] antes) =>
        entry.Family switch
        {
            Family.Joker when IsLegendary((MotelyJoker)entry.Value) =>
                new LegendaryJokerClause { Jokers = [(MotelyJoker)entry.Value], Edition = edition, Antes = antes },
            Family.Joker => new JokerClause { Jokers = [(MotelyJoker)entry.Value], Edition = edition, Antes = antes },
            Family.Boss => new BossClause { Bosses = [(MotelyBossBlind)entry.Value], Antes = antes },
            Family.Voucher => new VoucherClause { Vouchers = [(MotelyVoucher)entry.Value], Antes = antes },
            Family.Tag => new TagClause { Tags = [(MotelyTag)entry.Value], Antes = antes },
            Family.Tarot => new TarotCardClause { Tarots = [(MotelyTarotCard)entry.Value], Antes = antes },
            Family.Planet => new PlanetCardClause { Planets = [(MotelyPlanetCard)entry.Value], Antes = antes },
            Family.Spectral => new SpectralCardClause { Spectrals = [(MotelySpectralCard)entry.Value], Antes = antes },
            _ => throw new InvalidOperationException($"No shorthand clause for family {entry.Family}."),
        };

    private static bool IsLegendary(MotelyJoker joker) =>
        (MotelyJokerRarity)((int)joker & MotelyGlobals.JokerRarityMask) == MotelyJokerRarity.Legendary;

    /// <summary>The mapping key that names an entry, for messages that point at the mapping form.</summary>
    private static string MappingKey(Entry entry) =>
        entry.Family switch
        {
            Family.Joker when IsLegendary((MotelyJoker)entry.Value) => "legendaryJoker",
            Family.Joker => "joker",
            Family.Boss => "boss",
            Family.Voucher => "voucher",
            Family.Tag => "tag",
            Family.Tarot => "tarotCard",
            Family.Planet => "planetCard",
            _ => "spectralCard",
        };

    private static string Article(Family family) =>
        family switch
        {
            Family.Joker => "a joker",
            Family.Boss => "a boss",
            Family.Voucher => "a voucher",
            Family.Tag => "a tag",
            Family.Tarot => "a tarot card",
            Family.Planet => "a planet card",
            _ => "a spectral card",
        };

    private static string JoinOr(List<string> names) =>
        names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " or " + names[^1];
}
