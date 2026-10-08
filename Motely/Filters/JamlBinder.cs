using System.Globalization;

namespace Motely.Filters;

/// <summary>
/// The YAML node tree → <see cref="JamlConfig"/>. Each type binds through its own key table: no
/// reflection, so trimming and NativeAOT keep working.
/// <para>
/// Keys match their property names case-insensitively, and a key a type does not have is an
/// error. A key the document leaves out keeps the class's own initializer; a key written as null
/// is null. Scalars are text until a property gives them a type, so a seed like
/// <c>11111111</c> stays a string.
/// </para>
/// </summary>
internal static class JamlBinder
{
    internal abstract record Node;

    /// <summary>A scalar's text; null for a plain YAML null.</summary>
    internal sealed record Scalar(string? Text) : Node
    {
        public static readonly Scalar Null = new((string?)null);
    }

    internal sealed record Sequence(List<Node> Items) : Node;

    internal sealed record Mapping(List<Entry> Entries) : Node
    {
        /// <summary>The line of the first key; 0 for an empty mapping.</summary>
        public int Line => Entries.Count > 0 ? Entries[0].Line : 0;
    }

    /// <summary>
    /// One key and its value. <see cref="Key"/> picks the property; <see cref="Name"/> is what the
    /// author wrote, which differs only when a clause key's value moves to its property
    /// (<c>joker: Blueprint</c> binds <c>jokers</c>). <see cref="Line"/> is the key's line.
    /// </summary>
    internal sealed record Entry(string Key, int Line, Node Value)
    {
        public string Name { get; init; } = Key;
    }

    internal static InvalidOperationException Error(int line, string message) =>
        new($"YAML line {line}: {message}");

    internal static JamlConfig Bind(Node root) =>
        root switch
        {
            Mapping map => Document.Bind(map, "the document"),
            Scalar { Text: null } => throw new InvalidOperationException("YAML: the document is empty."),
            _ => throw new InvalidOperationException(
                "YAML: the document is not a mapping; a filter is keys like `name:` and `must:`."
            ),
        };

    // ── Values ──

    private static InvalidOperationException Wrong(Entry at, string expected, Node got) =>
        Error(at.Line, $"`{at.Name}` takes {expected}, not {Describe(got)}");

    private static string Describe(Node node) =>
        node switch
        {
            Scalar { Text: null } => "null",
            Scalar scalar => $"`{scalar.Text}`",
            Sequence => "a list",
            _ => "a mapping",
        };

    private static bool IsNull(Node node) => node is Scalar { Text: null };

    private static string? ReadText(Node node, Entry at) =>
        node is Scalar scalar ? scalar.Text : throw Wrong(at, "text", node);

    // AllowLeadingSign and nothing else: `+5` and `-3` read, ` 5 `, `5.0` and `0x10` do not.
    private static int ReadInt(Node node, Entry at) =>
        node is Scalar { Text: { } text }
            ? int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value)
                ? value
                : throw Error(at.Line, $"`{text}` is not an integer (key `{at.Name}`)")
            : throw Wrong(at, "an integer", node);

    private static bool ReadBool(Node node, Entry at) =>
        node is Scalar { Text: { } text }
            ? text switch
            {
                "true" or "True" or "TRUE" => true,
                "false" or "False" or "FALSE" => false,
                _ => throw Error(at.Line, $"`{text}` is not true or false (key `{at.Name}`)"),
            }
            : throw Wrong(at, "true or false", node);

    private static T ReadEnum<T>(Node node, Entry at)
        where T : struct, Enum =>
        node is Scalar { Text: { } text }
            ? Enum.TryParse(text, ignoreCase: true, out T value)
                ? value
                : throw Error(at.Line, $"`{text}` is not a {typeof(T).Name} (key `{at.Name}`)")
            : throw Wrong(at, $"a {typeof(T).Name}", node);

    private static int? ReadNullableInt(Entry at) => IsNull(at.Value) ? null : ReadInt(at.Value, at);

    private static T? ReadNullableEnum<T>(Entry at)
        where T : struct, Enum => IsNull(at.Value) ? null : ReadEnum<T>(at.Value, at);

    private static T[]? ReadList<T>(Entry at, Func<Node, Entry, T> item) =>
        at.Value switch
        {
            Scalar { Text: null } => null,
            Sequence sequence => [.. sequence.Items.Select(node => item(node, at))],
            var other => throw Wrong(at, "a list", other),
        };

    private static List<T>? ToList<T>(T[]? items) => items is null ? null : [.. items];

    private static T? ReadMapping<T>(Entry at, Shape<T> shape)
        where T : class =>
        at.Value switch
        {
            Scalar { Text: null } => null,
            Mapping map => shape.Bind(map, $"`{at.Name}`"),
            var other => throw Wrong(at, "a mapping", other),
        };

    /// <summary>A type's keys, camelCase, each with the setter that reads its value.</summary>
    private sealed class Shape<T>(Func<T> create, (string Key, Action<T, Entry> Set)[] keys)
        where T : class
    {
        private readonly Dictionary<string, Action<T, Entry>> setters = keys.ToDictionary(
            k => k.Key,
            k => k.Set,
            StringComparer.OrdinalIgnoreCase
        );
        private readonly string names = string.Join(", ", keys.Select(k => k.Key));

        public T Bind(Mapping map, string where)
        {
            var target = create();
            foreach (var entry in map.Entries)
            {
                if (!setters.TryGetValue(entry.Key, out var set))
                    throw Error(entry.Line, $"unknown key '{entry.Name}' in {where}; it takes {names}.");
                set(target, entry);
            }
            return target;
        }
    }

    // ── Shapes ──
    //
    // Exactly the public settable properties of each type. The values are null-forgiven because a
    // key written as null is null, as it always was, even where the property says otherwise.

    private static (string, Action<T, Entry>)[] ClauseKeysOf<T>()
        where T : IMotelyClause =>
        [
            ("label", (c, e) => c.Label = ReadText(e.Value, e)),
            ("min", (c, e) => c.Min = ReadInt(e.Value, e)),
            ("max", (c, e) => c.Max = ReadNullableInt(e)),
            ("score", (c, e) => c.Score = ReadInt(e.Value, e)),
        ];

    private static (string, Action<T, Entry>) Antes<T>()
        where T : IAnteScopedClause => ("antes", (c, e) => c.Antes = ReadList(e, ReadInt)!);

    private static (string, Action<T, Entry>) Rolls<T>()
        where T : IRollScopedClause => ("rolls", (c, e) => c.Rolls = ReadList(e, ReadInt)!);

    private static (string, Action<T, Entry>) With<T>()
        where T : IWithScopedClause => ("with", (c, e) => c.With = ReadMapping(e, LuckShape)!);

    private static readonly Shape<MotelyWith> LuckShape = new(
        () => new(),
        [("luck", (w, e) => w.Luck = ReadEnum<MotelyLuck>(e.Value, e))]
    );

    private static readonly Shape<JokerSourceConfig> JokerSources = new(
        () => new(),
        [
            ("shopItems", (s, e) => s.ShopItems = ReadList(e, ReadInt)!),
            ("boosterPacks", (s, e) => s.BoosterPacks = ReadList(e, ReadInt)!),
            ("requireMegaPack", (s, e) => s.RequireMegaPack = ReadBool(e.Value, e)),
            ("judgement", (s, e) => s.Judgement = ReadList(e, ReadInt)!),
            ("wraith", (s, e) => s.Wraith = ReadList(e, ReadInt)!),
            ("riffRaff", (s, e) => s.RiffRaff = ReadList(e, ReadInt)!),
            ("rareTag", (s, e) => s.RareTag = ReadList(e, ReadInt)!),
            ("uncommonTag", (s, e) => s.UncommonTag = ReadList(e, ReadInt)!),
            ("commonShopJokers", (s, e) => s.CommonShopJokers = ReadList(e, ReadInt)!),
            ("uncommonShopJokers", (s, e) => s.UncommonShopJokers = ReadList(e, ReadInt)!),
            ("rareShopJokers", (s, e) => s.RareShopJokers = ReadList(e, ReadInt)!),
            ("allShopJokers", (s, e) => s.AllShopJokers = ReadList(e, ReadInt)!),
        ]
    );

    private static readonly Shape<LegendaryJokerSourceConfig> LegendarySources = new(
        () => new(),
        [
            ("boosterPacks", (s, e) => s.BoosterPacks = ReadList(e, ReadInt)!),
            ("arcanaPacks", (s, e) => s.ArcanaPacks = ReadList(e, ReadInt)!),
            ("spectralPacks", (s, e) => s.SpectralPacks = ReadList(e, ReadInt)!),
            ("requireMegaPack", (s, e) => s.RequireMegaPack = ReadBool(e.Value, e)),
        ]
    );

    private static readonly Shape<PlanetSourceConfig> PlanetSources = new(
        () => new(),
        [
            ("shopItems", (s, e) => s.ShopItems = ReadList(e, ReadInt)!),
            ("boosterPacks", (s, e) => s.BoosterPacks = ReadList(e, ReadInt)!),
            ("requireMegaPack", (s, e) => s.RequireMegaPack = ReadBool(e.Value, e)),
        ]
    );

    private static readonly Shape<SpectralCardSourceConfig> SpectralSources = new(
        () => new(),
        [
            ("shopItems", (s, e) => s.ShopItems = ReadList(e, ReadInt)!),
            ("boosterPacks", (s, e) => s.BoosterPacks = ReadList(e, ReadInt)!),
            ("sixthSense", (s, e) => s.SixthSense = ReadList(e, ReadInt)!),
            ("seance", (s, e) => s.Seance = ReadList(e, ReadInt)!),
            ("requireMegaPack", (s, e) => s.RequireMegaPack = ReadBool(e.Value, e)),
            ("etherealTag", (s, e) => s.EtherealTag = ReadBool(e.Value, e)),
            ("omenGlobe", (s, e) => s.OmenGlobe = ReadBool(e.Value, e)),
        ]
    );

    private static readonly Shape<TarotCardSourceConfig> TarotSources = new(
        () => new(),
        [
            ("shopItems", (s, e) => s.ShopItems = ReadList(e, ReadInt)!),
            ("boosterPacks", (s, e) => s.BoosterPacks = ReadList(e, ReadInt)!),
            ("emperor", (s, e) => s.Emperor = ReadList(e, ReadInt)!),
            ("purpleSealOrEightBall", (s, e) => s.PurpleSealOrEightBall = ReadList(e, ReadInt)!),
            ("charmTag", (s, e) => s.CharmTag = ReadBool(e.Value, e)),
            ("requireMegaPack", (s, e) => s.RequireMegaPack = ReadBool(e.Value, e)),
        ]
    );

    private static readonly Shape<StandardCardSourceConfig> StandardSources = new(
        () => new(),
        [
            ("shopItems", (s, e) => s.ShopItems = ReadList(e, ReadInt)!),
            ("boosterPacks", (s, e) => s.BoosterPacks = ReadList(e, ReadInt)!),
            ("requireMegaPack", (s, e) => s.RequireMegaPack = ReadBool(e.Value, e)),
        ]
    );

    private static readonly Shape<JokerClause> Joker = new(
        () => new(),
        [
            .. ClauseKeysOf<JokerClause>(),
            Antes<JokerClause>(),
            ("jokers", (c, e) => c.Jokers = ReadList(e, ReadEnum<MotelyJoker>)!),
            ("edition", (c, e) => c.Edition = ReadNullableEnum<MotelyItemEdition>(e)),
            ("stickers", (c, e) => c.Stickers = ReadList(e, ReadEnum<MotelyJokerSticker>)!),
            ("sources", (c, e) => c.Sources = ReadMapping(e, JokerSources)),
            ("legendarySources", (c, e) => c.LegendarySources = ReadMapping(e, LegendarySources)),
        ]
    );

    private static readonly Shape<CommonJokerClause> CommonJoker = new(
        () => new(),
        [
            .. ClauseKeysOf<CommonJokerClause>(),
            Antes<CommonJokerClause>(),
            ("jokers", (c, e) => c.Jokers = ReadList(e, ReadEnum<MotelyJokerCommon>)!),
            ("edition", (c, e) => c.Edition = ReadNullableEnum<MotelyItemEdition>(e)),
            ("stickers", (c, e) => c.Stickers = ReadList(e, ReadEnum<MotelyJokerSticker>)!),
            ("sources", (c, e) => c.Sources = ReadMapping(e, JokerSources)),
        ]
    );

    private static readonly Shape<UncommonJokerClause> UncommonJoker = new(
        () => new(),
        [
            .. ClauseKeysOf<UncommonJokerClause>(),
            Antes<UncommonJokerClause>(),
            ("jokers", (c, e) => c.Jokers = ReadList(e, ReadEnum<MotelyJokerUncommon>)!),
            ("edition", (c, e) => c.Edition = ReadNullableEnum<MotelyItemEdition>(e)),
            ("stickers", (c, e) => c.Stickers = ReadList(e, ReadEnum<MotelyJokerSticker>)!),
            ("sources", (c, e) => c.Sources = ReadMapping(e, JokerSources)),
        ]
    );

    private static readonly Shape<RareJokerClause> RareJoker = new(
        () => new(),
        [
            .. ClauseKeysOf<RareJokerClause>(),
            Antes<RareJokerClause>(),
            ("jokers", (c, e) => c.Jokers = ReadList(e, ReadEnum<MotelyJokerRare>)!),
            ("edition", (c, e) => c.Edition = ReadNullableEnum<MotelyItemEdition>(e)),
            ("stickers", (c, e) => c.Stickers = ReadList(e, ReadEnum<MotelyJokerSticker>)!),
            ("sources", (c, e) => c.Sources = ReadMapping(e, JokerSources)),
        ]
    );

    private static readonly Shape<LegendaryJokerClause> LegendaryJoker = new(
        () => new(),
        [
            .. ClauseKeysOf<LegendaryJokerClause>(),
            Antes<LegendaryJokerClause>(),
            ("jokers", (c, e) => c.Jokers = ReadList(e, ReadEnum<MotelyJoker>)!),
            ("edition", (c, e) => c.Edition = ReadNullableEnum<MotelyItemEdition>(e)),
            ("sources", (c, e) => c.Sources = ReadMapping(e, LegendarySources)),
            ("soulCardOnly", (c, e) => c.SoulCardOnly = ReadBool(e.Value, e)),
            ("soulEditionRolls", (c, e) => c.SoulEditionRolls = ReadInt(e.Value, e)),
        ]
    );

    private static readonly Shape<PlanetCardClause> PlanetCard = new(
        () => new(),
        [
            .. ClauseKeysOf<PlanetCardClause>(),
            Antes<PlanetCardClause>(),
            ("planets", (c, e) => c.Planets = ReadList(e, ReadEnum<MotelyPlanetCard>)!),
            ("sources", (c, e) => c.Sources = ReadMapping(e, PlanetSources)),
        ]
    );

    private static readonly Shape<SpectralCardClause> SpectralCard = new(
        () => new(),
        [
            .. ClauseKeysOf<SpectralCardClause>(),
            Antes<SpectralCardClause>(),
            ("spectrals", (c, e) => c.Spectrals = ReadList(e, ReadEnum<MotelySpectralCard>)!),
            ("sources", (c, e) => c.Sources = ReadMapping(e, SpectralSources)),
        ]
    );

    private static readonly Shape<TarotCardClause> TarotCard = new(
        () => new(),
        [
            .. ClauseKeysOf<TarotCardClause>(),
            Antes<TarotCardClause>(),
            ("tarots", (c, e) => c.Tarots = ReadList(e, ReadEnum<MotelyTarotCard>)!),
            ("sources", (c, e) => c.Sources = ReadMapping(e, TarotSources)),
        ]
    );

    private static readonly Shape<StandardCardClause> StandardCard = new(
        () => new(),
        [
            .. ClauseKeysOf<StandardCardClause>(),
            Antes<StandardCardClause>(),
            ("rank", (c, e) => c.Rank = ReadNullableEnum<MotelyStandardcardRank>(e)),
            ("suit", (c, e) => c.Suit = ReadNullableEnum<MotelyStandardcardSuit>(e)),
            ("enhancement", (c, e) => c.Enhancement = ReadNullableEnum<MotelyItemEnhancement>(e)),
            ("seal", (c, e) => c.Seal = ReadNullableEnum<MotelyItemSeal>(e)),
            ("edition", (c, e) => c.Edition = ReadNullableEnum<MotelyItemEdition>(e)),
            ("sources", (c, e) => c.Sources = ReadMapping(e, StandardSources)),
        ]
    );

    private static readonly Shape<ErraticRankClause> ErraticRank = new(
        () => new(),
        [
            .. ClauseKeysOf<ErraticRankClause>(),
            Antes<ErraticRankClause>(),
            ("rank", (c, e) => c.Rank = ReadEnum<MotelyStandardcardRank>(e.Value, e)),
        ]
    );

    private static readonly Shape<ErraticSuitClause> ErraticSuit = new(
        () => new(),
        [
            .. ClauseKeysOf<ErraticSuitClause>(),
            Antes<ErraticSuitClause>(),
            ("suit", (c, e) => c.Suit = ReadEnum<MotelyStandardcardSuit>(e.Value, e)),
        ]
    );

    private static readonly Shape<VoucherClause> Voucher = new(
        () => new(),
        [
            .. ClauseKeysOf<VoucherClause>(),
            Antes<VoucherClause>(),
            ("vouchers", (c, e) => c.Vouchers = ReadList(e, ReadEnum<MotelyVoucher>)!),
            Rolls<VoucherClause>(),
        ]
    );

    private static Shape<T> Tag<T>(Func<T> create)
        where T : TagClause =>
        new(
            create,
            [
                .. ClauseKeysOf<T>(),
                Antes<T>(),
                ("tags", (c, e) => c.Tags = ReadList(e, ReadEnum<MotelyTag>)!),
                Rolls<T>(),
            ]
        );

    private static readonly Shape<TagClause> AnyBlindTag = Tag(() => new TagClause());

    private static readonly Shape<BossClause> Boss = new(
        () => new(),
        [
            .. ClauseKeysOf<BossClause>(),
            Antes<BossClause>(),
            ("bosses", (c, e) => c.Bosses = ReadList(e, ReadEnum<MotelyBossBlind>)!),
        ]
    );

    private static readonly Shape<BoosterPackClause> BoosterPack = new(
        () => new(),
        [
            .. ClauseKeysOf<BoosterPackClause>(),
            Antes<BoosterPackClause>(),
            ("packs", (c, e) => c.Packs = ReadList(e, ReadEnum<MotelyBoosterPack>)!),
            Rolls<BoosterPackClause>(),
        ]
    );

    private static readonly Shape<PokerHandClause> PokerHand = new(
        () => new(),
        [
            .. ClauseKeysOf<PokerHandClause>(),
            Antes<PokerHandClause>(),
            ("pokerHands", (c, e) => c.PokerHands = ReadList(e, ReadEnum<MotelyPokerHand>)!),
            Rolls<PokerHandClause>(),
        ]
    );

    private static readonly Shape<StartingDrawClause> StartingDraw = new(
        () => new(),
        [
            .. ClauseKeysOf<StartingDrawClause>(),
            Antes<StartingDrawClause>(),
            ("rank", (c, e) => c.Rank = ReadNullableEnum<MotelyStandardcardRank>(e)),
            ("suit", (c, e) => c.Suit = ReadNullableEnum<MotelyStandardcardSuit>(e)),
        ]
    );

    private static Shape<T> Event<T>(Func<T> create)
        where T : class, IRollScopedClause => new(create, [.. ClauseKeysOf<T>(), Rolls<T>()]);

    private static Shape<T> LuckEvent<T>(Func<T> create)
        where T : class, IRollScopedClause, IWithScopedClause =>
        new(create, [.. ClauseKeysOf<T>(), Rolls<T>(), With<T>()]);

    private static readonly Shape<MisprintMultClause> MisprintMult = new(
        () => new(),
        [
            .. ClauseKeysOf<MisprintMultClause>(),
            Rolls<MisprintMultClause>(),
            ("mult", (c, e) => c.Mult = ReadInt(e.Value, e)),
        ]
    );

    private static Shape<T> Logic<T>(Func<T> create)
        where T : LogicClause =>
        new(
            create,
            [
                .. ClauseKeysOf<T>(),
                ("mode", (c, e) => c.Mode = ReadEnum<LogicScoreMode>(e.Value, e)),
                ("clauses", (c, e) => c.Clauses = ReadList(e, ReadClause)!),
            ]
        );

    private static readonly Shape<JamlConfig> Document = new(
        () => new(),
        [
            ("id", (c, e) => c.Id = ReadText(e.Value, e)!),
            ("name", (c, e) => c.Name = ReadText(e.Value, e)),
            ("description", (c, e) => c.Description = ReadText(e.Value, e)),
            ("author", (c, e) => c.Author = ReadText(e.Value, e)),
            ("deck", (c, e) => c.Deck = ReadEnum<MotelyDeck>(e.Value, e)),
            ("stake", (c, e) => c.Stake = ReadEnum<MotelyStake>(e.Value, e)),
            ("seeds", (c, e) => c.Seeds = ToList(ReadList(e, (node, at) => ReadText(node, at)!))!),
            ("must", (c, e) => c.Must = ToList(ReadList(e, ReadClause))!),
            ("should", (c, e) => c.Should = ToList(ReadList(e, ReadClause))!),
            ("mustNot", (c, e) => c.MustNot = ToList(ReadList(e, ReadClause))!),
        ]
    );

    // ── Clauses ──
    //
    // `- uncommonJoker: Blueprint` → UncommonJokerClause. The first clause key in the mapping
    // picks the type; its value moves to the property it names (`jokers`, the event clauses'
    // `rolls`, a logic clause's `clauses`), wrapped into a list when the author wrote a single
    // value. Everything else in the mapping is that type's own keys.

    private readonly record struct ClauseKey(Func<Mapping, string, IMotelyClause> Bind, string ValueProperty, bool ValueIsList);

    private static ClauseKey Key<T>(Shape<T> shape, string valueProperty, bool valueIsList)
        where T : class, IMotelyClause => new((map, where) => shape.Bind(map, where), valueProperty, valueIsList);

    private static readonly Dictionary<string, ClauseKey> ClauseKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["joker"] = Key(Joker, "jokers", true),
        ["jokers"] = Key(Joker, "jokers", true),
        ["commonJoker"] = Key(CommonJoker, "jokers", true),
        ["commonJokers"] = Key(CommonJoker, "jokers", true),
        ["uncommonJoker"] = Key(UncommonJoker, "jokers", true),
        ["uncommonJokers"] = Key(UncommonJoker, "jokers", true),
        ["rareJoker"] = Key(RareJoker, "jokers", true),
        ["rareJokers"] = Key(RareJoker, "jokers", true),
        ["legendaryJoker"] = Key(LegendaryJoker, "jokers", true),
        ["legendaryJokers"] = Key(LegendaryJoker, "jokers", true),
        ["planetCard"] = Key(PlanetCard, "planets", true),
        ["planetCards"] = Key(PlanetCard, "planets", true),
        ["spectralCard"] = Key(SpectralCard, "spectrals", true),
        ["spectralCards"] = Key(SpectralCard, "spectrals", true),
        ["tarotCard"] = Key(TarotCard, "tarots", true),
        ["tarotCards"] = Key(TarotCard, "tarots", true),
        ["standardCard"] = Key(StandardCard, "label", false),
        ["standardCards"] = Key(StandardCard, "label", false),
        ["erraticRank"] = Key(ErraticRank, "rank", false),
        ["erraticRanks"] = Key(ErraticRank, "rank", false),
        ["erraticSuit"] = Key(ErraticSuit, "suit", false),
        ["erraticSuits"] = Key(ErraticSuit, "suit", false),
        ["voucher"] = Key(Voucher, "vouchers", true),
        ["vouchers"] = Key(Voucher, "vouchers", true),
        ["tag"] = Key(AnyBlindTag, "tags", true),
        ["tags"] = Key(AnyBlindTag, "tags", true),
        ["smallBlindTag"] = Key(Tag(() => new SmallBlindTagClause()), "tags", true),
        ["bigBlindTag"] = Key(Tag(() => new BigBlindTagClause()), "tags", true),
        ["boss"] = Key(Boss, "bosses", true),
        ["bosses"] = Key(Boss, "bosses", true),
        ["boosterPack"] = Key(BoosterPack, "packs", true),
        ["boosterPacks"] = Key(BoosterPack, "packs", true),
        ["pokerHand"] = Key(PokerHand, "pokerHands", true),
        ["pokerHands"] = Key(PokerHand, "pokerHands", true),
        ["startingDraw"] = Key(StartingDraw, "label", false),
        ["luckyMoney"] = Key(LuckEvent(() => new LuckyMoneyClause()), "rolls", true),
        ["luckyMult"] = Key(LuckEvent(() => new LuckyMultClause()), "rolls", true),
        ["misprintMult"] = Key(MisprintMult, "rolls", true),
        ["wheelOfFortune"] = Key(LuckEvent(() => new WheelOfFortuneClause()), "rolls", true),
        ["wheelStaysFlipped"] = Key(LuckEvent(() => new WheelStaysFlippedClause()), "rolls", true),
        ["glassDestroy"] = Key(LuckEvent(() => new GlassDestroyClause()), "rolls", true),
        ["cavendishExtinct"] = Key(LuckEvent(() => new CavendishExtinctClause()), "rolls", true),
        ["grosMichelExtinct"] = Key(LuckEvent(() => new GrosMichelExtinctClause()), "rolls", true),
        ["spaceLevelup"] = Key(LuckEvent(() => new SpaceLevelupClause()), "rolls", true),
        ["businessPayout"] = Key(Event(() => new BusinessPayoutClause()), "rolls", true),
        ["parkingPayout"] = Key(Event(() => new ParkingPayoutClause()), "rolls", true),
        ["bloodstoneTrigger"] = Key(Event(() => new BloodstoneTriggerClause()), "rolls", true),
        ["and"] = Key(Logic(() => new AndClause()), "clauses", true),
        ["or"] = Key(Logic(() => new OrClause()), "clauses", true),
    };

    /// <param name="owner">The key whose list holds this clause: an error with no key of its own
    /// is reported on that key's line.</param>
    private static IMotelyClause ReadClause(Node node, Entry owner)
    {
        if (node is not Mapping map)
            throw Error(owner.Line, "a clause is a mapping like `- joker: Blueprint`.");
        RejectRepeatedKeys(map);

        int index = map.Entries.FindIndex(e => ClauseKeys.ContainsKey(e.Key));
        if (index < 0)
            throw Error(
                map.Line > 0 ? map.Line : owner.Line,
                $"no clause key among: {string.Join(", ", map.Entries.Select(e => e.Name))}."
            );

        var wire = map.Entries[index];
        var key = ClauseKeys[wire.Key];
        var entries = new List<Entry>(map.Entries);
        entries.RemoveAt(index);

        if (wire.Value is Mapping block)
        {
            // `- or: { mode: max, clauses: [...] }`: the block is the clause's own keys.
            foreach (var child in block.Entries)
                AddOnce(entries, child, wire);
        }
        else if (!IsNull(wire.Value))
        {
            var value = wire.Value;
            // `joker: any` is the engine's own "whole category": an empty list.
            if (key.ValueIsList && value is Scalar { Text: { } text }
                && string.Equals(text.Trim(), "any", StringComparison.OrdinalIgnoreCase))
                value = new Sequence([]);
            if (key.ValueIsList && value is not Sequence)
                value = new Sequence([value]);
            AddOnce(entries, new Entry(key.ValueProperty, wire.Line, value) { Name = wire.Name }, wire);
        }

        ExpandIntLists(entries);
        return key.Bind(new Mapping(entries), $"`{wire.Name}` clause");
    }

    // Same-spelling checks, as they always were: `Jokers:` beside `joker:` passes here, and
    // binding keeps the last one written, which is the clause key's value.
    private static void AddOnce(List<Entry> entries, Entry entry, Entry wire)
    {
        if (entries.Find(e => e.Key == entry.Key) is { } own)
            throw Error(own.Line, $"`{wire.Name}:` already sets `{entry.Key}`; it is also written as its own key.");
        entries.Add(entry);
    }

    /// <summary>
    /// A key written twice inside a clause, or inside one of its blocks, is an error. The
    /// document's own keys keep last-one-wins, as they always did.
    /// </summary>
    private static void RejectRepeatedKeys(Mapping map)
    {
        for (int i = 0; i < map.Entries.Count; i++)
        {
            var entry = map.Entries[i];
            for (int j = 0; j < i; j++)
                if (map.Entries[j].Key == entry.Key)
                    throw Error(entry.Line, $"`{entry.Name}` is written twice (first on line {map.Entries[j].Line}).");
            if (entry.Value is Mapping child)
                RejectRepeatedKeys(child);
        }
    }

    // ── Integer lists: ranges and ante bounds (ADR-001 revisit) ──
    //
    // Every int list takes range shorthand: `1-8`, `1..8`, `1 to 8`, alone or as list items
    // (`[1..3, 7]`). No enum value looks like that, so a scalar shaped as a range is expanded
    // wherever it appears in the clause, sources: included. `antes` is always an int list: a
    // bare `antes: 4` is [4], and every ante must be 0-39.

    internal const int MaxExpandedLength = 1024;
    internal const int MinAnte = 0;
    internal const int MaxAnte = 39;

    private static void ExpandIntLists(List<Entry> entries)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Value is Mapping child)
            {
                ExpandIntLists(child.Entries);
                continue;
            }
            bool isAntes = entry.Key.Equals("antes", StringComparison.OrdinalIgnoreCase);
            Node[] items = entry.Value switch
            {
                Sequence sequence => [.. sequence.Items],
                Scalar { Text: not null } => [entry.Value],
                _ => [],
            };
            if (items.Length == 0 || (!isAntes && !items.Any(item => TryParseRange(Text(item), out _, out _))))
                continue;
            entries[i] = entry with { Value = IntList(items, entry, isAntes) };
        }
    }

    private static Sequence IntList(Node[] items, Entry at, bool isAntes)
    {
        string key = at.Name;
        var values = new List<int>();
        foreach (var item in items)
        {
            string text = Text(item);
            if (TryParseRange(text, out int from, out int to))
            {
                if (from > to)
                    throw Error(at.Line, $"`{text.Trim()}` is a descending range; write `{to}-{from}` (key `{key}`)");
                // long: 0-2147483647 is 2^31 values. The cap is on the whole list once expanded.
                long span = (long)to - from + 1;
                if (span > MaxExpandedLength - values.Count)
                    throw Error(at.Line, values.Count == 0
                        ? $"`{text.Trim()}` spans {span} values; ranges cover at most {MaxExpandedLength} values per key (key `{key}`)"
                        : $"`{text.Trim()}` brings the list to {values.Count + span} values; ranges cover at most {MaxExpandedLength} values per key (key `{key}`)");
                // Count, not `v <= to; v++`: with `to == int.MaxValue` that counter wraps.
                for (int k = 0; k < (int)span; k++)
                    values.Add(from + k);
            }
            else if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int single))
                values.Add(single);
            else
                throw Error(at.Line, $"`{text}` is not an integer (key `{key}`)");
        }
        // Bounds after the whole list is expanded, so an oversized list reports its size first.
        foreach (int value in values)
            CheckAnte(value, at, isAntes);
        return new Sequence([.. values.Select(v => (Node)new Scalar(v.ToString(CultureInfo.InvariantCulture)))]);
    }

    private static int CheckAnte(int value, Entry at, bool isAntes) =>
        isAntes && (value < MinAnte || value > MaxAnte)
            ? throw Error(at.Line, $"ante `{value}` is out of range; antes run {MinAnte}-{MaxAnte} (key `{at.Name}`)")
            : value;

    private static string Text(Node node) =>
        node switch
        {
            Scalar scalar => scalar.Text ?? "",
            Sequence => "[...]",
            _ => "{...}",
        };

    private static bool TryParseRange(string scalar, out int from, out int to)
    {
        from = to = 0;
        var s = scalar.AsSpan().Trim();
        int sep, sepLength;
        if ((sep = s.IndexOf("..", StringComparison.Ordinal)) >= 0) sepLength = 2;
        else if ((sep = s.IndexOf(" to ", StringComparison.OrdinalIgnoreCase)) >= 0) sepLength = 4;
        else if ((sep = s.IndexOf('-')) > 0) sepLength = 1; // > 0: a leading '-' is a sign
        else return false;
        // NumberStyles.None: digits only, so neither side can smuggle a sign or a second range.
        return int.TryParse(s[..sep].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out from)
            && int.TryParse(s[(sep + sepLength)..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out to);
    }
}
