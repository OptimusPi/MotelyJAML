using System.Text;

namespace Motely.Filters.Jaml;

// Write-side mirror of JamlClausePopulator: JamlConfig → JAML text via typed switches.
// FromJaml(ToJaml(config)) preserves clause data. Text shape may differ (e.g. smallBlindTag
// rewrites as tag + rolls; erraticRanks as or) — still valid, parseable, same meaning.
// No GetProperty / PropertyInfo — every clause family is a concrete write arm.
public static partial class JamlConfigLoader
{
    public static string ToJaml(JamlConfig config)
    {
        var root = new JMap();
        root.Set("id", new JScalar(config.Id), default);
        if (config.Name != null)
            root.Set("name", new JScalar(config.Name), default);
        if (config.Description != null)
            root.Set("description", new JScalar(config.Description), default);
        if (config.Author != null)
            root.Set("author", new JScalar(config.Author), default);
        if (config.Deck != MotelyDeck.Red)
            root.Set("deck", new JScalar(config.Deck.ToString()), default);
        if (config.Stake != MotelyStake.White)
            root.Set("stake", new JScalar(config.Stake.ToString()), default);
        if (config.Seeds.Count > 0)
            root.Set("seeds", StringArrayNode(config.Seeds), default);
        if (config.Filter is { Length: > 0 })
            root.Set("filter", new JScalar(config.Filter), default);
        if (config.Must.Count > 0)
            root.Set("must", ClauseListNode(config.Must), default);
        if (config.Should.Count > 0)
            root.Set("should", ClauseListNode(config.Should), default);
        if (config.MustNot.Count > 0)
            root.Set("mustNot", ClauseListNode(config.MustNot), default);

        var sb = new StringBuilder();
        WriteMap(sb, root, 0);
        return sb.ToString();
    }

    private static void WriteMap(StringBuilder sb, JMap map, int indent)
    {
        foreach (var key in map.Keys)
        {
            var value = map.Get(key)!;
            WriteKeyed(sb, key, value, indent);
        }
    }

    private static void WriteKeyed(StringBuilder sb, string key, JNode value, int indent)
    {
        string pad = new(' ', indent);
        switch (value)
        {
            case JScalar scalar:
                sb.Append(pad).Append(key).Append(": ").Append(ScalarText(scalar)).Append('\n');
                break;
            case JMap { Keys.Count: 0 }:
                sb.Append(pad).Append(key).Append(": {}\n");
                break;
            case JMap childMap:
                sb.Append(pad).Append(key).Append(":\n");
                WriteMap(sb, childMap, indent + 2);
                break;
            case JSeq seq when IsFlowArray(seq):
                sb.Append(pad).Append(key).Append(": [")
                    .Append(string.Join(", ", seq.Items.Select(i => ScalarText((JScalar)i))))
                    .Append("]\n");
                break;
            case JSeq seq:
                sb.Append(pad).Append(key).Append(":\n");
                WriteSequence(sb, seq, indent);
                break;
        }
    }

    private static bool IsFlowArray(JSeq seq) => seq.Items.All(i => i is JScalar);

    private static void WriteSequence(StringBuilder sb, JSeq seq, int indent)
    {
        string pad = new(' ', indent);
        int itemIndent = indent + 2;

        foreach (var item in seq.Items)
        {
            if (item is not JMap itemMap)
                throw new InvalidOperationException("ToJaml: expected a clause mapping in a block sequence.");
            var keys = itemMap.Keys;
            if (keys.Count == 0)
            {
                sb.Append(pad).Append("- {}\n");
                continue;
            }
            string firstKey = keys[0];
            var firstValue = itemMap.Get(firstKey)!;
            sb.Append(pad).Append("- ");
            WriteKeyedInline(sb, firstKey, firstValue, itemIndent);

            foreach (var key in keys.Skip(1))
                WriteKeyed(sb, key, itemMap.Get(key)!, itemIndent);
        }
    }

    private static void WriteKeyedInline(StringBuilder sb, string key, JNode value, int indent)
    {
        switch (value)
        {
            case JScalar scalar:
                sb.Append(key).Append(": ").Append(ScalarText(scalar)).Append('\n');
                break;
            case JMap { Keys.Count: 0 }:
                sb.Append(key).Append(": {}\n");
                break;
            case JMap childMap:
                sb.Append(key).Append(":\n");
                WriteMap(sb, childMap, indent + 2);
                break;
            case JSeq seq when IsFlowArray(seq):
                sb.Append(key).Append(": [")
                    .Append(string.Join(", ", seq.Items.Select(i => ScalarText((JScalar)i))))
                    .Append("]\n");
                break;
            case JSeq seq:
                sb.Append(key).Append(":\n");
                WriteSequence(sb, seq, indent);
                break;
        }
    }

    private static JSeq ClauseListNode(IEnumerable<IJamlClause> clauses)
    {
        var seq = new JSeq();
        foreach (var clause in clauses)
            seq.Items.Add(WriteClause(clause));
        return seq;
    }

    private static JMap WriteClause(IJamlClause clause) =>
        clause switch
        {
            AndClause logic => WriteLogic("and", logic),
            OrClause logic => WriteLogic("or", logic),
            JokerClause c => WriteJokerFamily("joker", c.Jokers, c.Edition, c.Stickers, c.Sources, c),
            CommonJokerClause c => WriteJokerFamily("commonJoker", c.Jokers, c.Edition, c.Stickers, c.Sources, c),
            UncommonJokerClause c => WriteJokerFamily("uncommonJoker", c.Jokers, c.Edition, c.Stickers, c.Sources, c),
            RareJokerClause c => WriteJokerFamily("rareJoker", c.Jokers, c.Edition, c.Stickers, c.Sources, c),
            LegendaryJokerClause c => WriteLegendary(c),
            VoucherClause c => WriteItems("voucher", c.Vouchers, c, rolls: c.Rolls, rollsDefault: [0]),
            TarotCardClause c => WriteConsumable(
                "tarotCard",
                c.Tarots,
                c.Sources,
                c
            ),
            SpectralCardClause c => WriteConsumable(
                "spectralCard",
                c.Spectrals,
                c.Sources,
                c
            ),
            PlanetCardClause c => WriteConsumable(
                "planetCard",
                c.Planets,
                c.Sources,
                c
            ),
            StandardCardClause c => WriteStandardCard(c),
            BossClause c => WriteItems("boss", c.Bosses, c),
            TagClause c => WriteItems("tag", c.Tags, c, rolls: c.Rolls, rollsDefault: [0, 1]),
            BoosterPackClause c => WriteItems(
                "boosterPack",
                c.Packs,
                c,
                rolls: c.Rolls,
                rollsDefault: [0, 1]
            ),
            ErraticRankClause c => WriteErraticRank(c),
            ErraticSuitClause c => WriteErraticSuit(c),
            StartingDrawClause c => WriteStartingDraw(c),
            PokerHandClause c => WriteItems("pokerHand", c.PokerHands, c),
            LuckyMoneyClause c => WriteInlineRollEvent("luckyMoney", c, c.With),
            LuckyMultClause c => WriteInlineRollEvent("luckyMult", c, c.With),
            MisprintMultClause c => WriteMisprint(c),
            WheelOfFortuneClause c => WriteInlineRollEvent("wheelOfFortune", c, c.With),
            GrosMichelExtinctClause c => WriteInlineRollEvent("grosMichelExtinct", c, c.With),
            CavendishExtinctClause c => WriteInlineRollEvent("cavendishExtinct", c, c.With),
            SpaceLevelupClause c => WriteInlineRollEvent("spaceLevelup", c, c.With),
            BusinessPayoutClause c => WriteInlineRollEvent("businessPayout", c, with: null),
            BloodstoneTriggerClause c => WriteInlineRollEvent("bloodstoneTrigger", c, with: null),
            ParkingPayoutClause c => WriteInlineRollEvent("parkingPayout", c, with: null),
            GlassDestroyClause c => WriteInlineRollEvent("glassDestroy", c, c.With),
            WheelStaysFlippedClause c => WriteInlineRollEvent("wheelStaysFlipped", c, c.With),
            _ => throw new InvalidOperationException(
                $"ToJaml: no writer for clause type '{clause.GetType().Name}'."
            ),
        };

    private static JMap WriteLogic(string discriminator, LogicClause logic)
    {
        var mapping = new JMap();
        mapping.Set(discriminator, ClauseListNode(logic.Clauses), default);
        WriteCommonKeys(mapping, logic);
        if (logic.Mode != JamlLogicScoreMode.Sum)
            mapping.Set("mode", new JScalar(logic.Mode.ToString().ToLowerInvariant()), default);
        if (logic.Antes.Length > 0)
            mapping.Set("antes", IntArrayNode(logic.Antes), default);
        return mapping;
    }

    private static JMap WriteJokerFamily<TEnum>(
        string discriminator,
        TEnum[] jokers,
        MotelyItemEdition? edition,
        MotelyJokerSticker[] stickers,
        JokerSourceConfig? sources,
        IJamlClause clause
    )
        where TEnum : struct, Enum
    {
        var mapping = new JMap();
        // Empty jokers = category any → `joker:` (empty scalar), not `joker: []`.
        mapping.Set(discriminator, DiscValueNode(jokers), default);
        WriteCommonKeys(mapping, clause);
        WriteAntes(mapping, clause);
        if (edition is { } ed)
            mapping.Set("edition", new JScalar(ed.ToString()), default);
        if (stickers.Length > 0)
            mapping.Set("stickers", EnumArrayNode(stickers), default);
        if (WriteJokerSources(sources) is { } sourcesNode)
            mapping.Set("sources", sourcesNode, default);
        return mapping;
    }

    private static JMap WriteLegendary(LegendaryJokerClause c)
    {
        var mapping = new JMap();
        mapping.Set("legendaryJoker", DiscValueNode(c.Jokers), default);
        WriteCommonKeys(mapping, c);
        WriteAntes(mapping, c);
        if (c.Edition is { } ed)
            mapping.Set("edition", new JScalar(ed.ToString()), default);
        if (c.SoulCardOnly)
            mapping.Set("soulCardOnly", JScalar.Of(true), default);
        if (c.SoulEditionRolls != 0)
            mapping.Set("soulEditionRolls", JScalar.Of(c.SoulEditionRolls), default);
        if (WriteLegendarySources(c.Sources) is { } sourcesNode)
            mapping.Set("sources", sourcesNode, default);
        return mapping;
    }

    private static JMap WriteItems<TEnum>(
        string discriminator,
        TEnum[] items,
        IJamlClause clause,
        int[]? rolls = null,
        int[]? rollsDefault = null
    )
        where TEnum : struct, Enum
    {
        var mapping = new JMap();
        mapping.Set(discriminator, DiscValueNode(items), default);
        WriteCommonKeys(mapping, clause);
        WriteAntes(mapping, clause);
        if (rolls is { } r && (rollsDefault is null || !r.SequenceEqual(rollsDefault)))
            mapping.Set("rolls", IntArrayNode(r), default);
        return mapping;
    }

    private static JMap WriteConsumable<TEnum>(
        string discriminator,
        TEnum[] items,
        object? sources,
        IJamlClause clause
    )
        where TEnum : struct, Enum
    {
        var mapping = new JMap();
        mapping.Set(discriminator, DiscValueNode(items), default);
        WriteCommonKeys(mapping, clause);
        WriteAntes(mapping, clause);
        var sourcesNode = sources switch
        {
            TarotCardSourceConfig t => WriteTarotSources(t),
            SpectralCardSourceConfig s => WriteSpectralSources(s),
            PlanetSourceConfig p => WritePlanetSources(p),
            _ => null,
        };
        if (sourcesNode is not null)
            mapping.Set("sources", sourcesNode, default);
        return mapping;
    }

    private static JMap WriteStandardCard(StandardCardClause c)
    {
        var mapping = new JMap();
        mapping.Set("standardCard", new JMap(), default);
        WriteCommonKeys(mapping, c);
        WriteAntes(mapping, c);
        if (c.Rank is { } rank)
            mapping.Set("rank", new JScalar(rank.ToString()), default);
        if (c.Suit is { } suit)
            mapping.Set("suit", new JScalar(suit.ToString()), default);
        if (c.Enhancement is { } enh)
            mapping.Set("enhancement", new JScalar(enh.ToString()), default);
        if (c.Seal is { } seal)
            mapping.Set("seal", new JScalar(seal.ToString()), default);
        if (c.Edition is { } ed)
            mapping.Set("edition", new JScalar(ed.ToString()), default);
        if (WriteStandardSources(c.Sources) is { } sourcesNode)
            mapping.Set("sources", sourcesNode, default);
        return mapping;
    }

    private static JMap WriteErraticRank(ErraticRankClause c)
    {
        var mapping = new JMap();
        mapping.Set("erraticRank", new JScalar(c.Rank.ToString()), default);
        WriteCommonKeys(mapping, c);
        WriteAntes(mapping, c);
        return mapping;
    }

    private static JMap WriteErraticSuit(ErraticSuitClause c)
    {
        var mapping = new JMap();
        mapping.Set("erraticSuit", new JScalar(c.Suit.ToString()), default);
        WriteCommonKeys(mapping, c);
        WriteAntes(mapping, c);
        return mapping;
    }

    private static JMap WriteStartingDraw(StartingDrawClause c)
    {
        var mapping = new JMap();
        mapping.Set("startingDraw", new JMap(), default);
        WriteCommonKeys(mapping, c);
        WriteAntes(mapping, c);
        if (c.Rank is { } rank)
            mapping.Set("rank", new JScalar(rank.ToString()), default);
        if (c.Suit is { } suit)
            mapping.Set("suit", new JScalar(suit.ToString()), default);
        return mapping;
    }

    private static JMap WriteInlineRollEvent(string discriminator, IRollScopedClause clause, JamlWith? with)
    {
        var mapping = new JMap();
        mapping.Set(discriminator, IntArrayNode(clause.Rolls), default);
        WriteCommonKeys(mapping, clause);
        if (with is { } w)
        {
            var withNode = WriteWith(w);
            if (withNode != null)
                mapping.Set("with", withNode, default);
        }
        return mapping;
    }

    private static JMap WriteMisprint(MisprintMultClause c)
    {
        var mapping = WriteInlineRollEvent("misprintMult", c, with: null);
        if (c.Mult != 0)
            mapping.Set("mult", JScalar.Of(c.Mult), default);
        return mapping;
    }

    private static void WriteCommonKeys(JMap mapping, IJamlClause clause)
    {
        if (clause.Label != null)
            mapping.Set("label", new JScalar(clause.Label), default);
        if (clause.Min != 1)
            mapping.Set("min", JScalar.Of(clause.Min), default);
        if (clause.Max.HasValue)
            mapping.Set("max", JScalar.Of(clause.Max.Value), default);
        if (clause.Score != 1)
            mapping.Set("score", JScalar.Of(clause.Score), default);
    }

    private static void WriteAntes(JMap mapping, IJamlClause clause)
    {
        if (clause is IAnteScopedClause { Antes.Length: > 0 } anteScoped)
            mapping.Set("antes", IntArrayNode(anteScoped.Antes), default);
    }

    private static JMap? WriteWith(JamlWith with)
    {
        var mapping = new JMap();
        if (with.Luck != MotelyLuck.X1)
            mapping.Set("luck", new JScalar(with.Luck.ToString()), default);
        if (with.Vouchers.Length > 0)
            mapping.Set("vouchers", StringArrayNode(with.Vouchers.Select(v => v.ToString())), default);
        return mapping.Keys.Count == 0 ? null : mapping;
    }

    // null sources → omit. Non-null empty → `sources: {}` (explicit empty ≠ default).
    private static JMap? WriteJokerSources(JokerSourceConfig? sources)
    {
        if (sources is null)
            return null;
        var mapping = new JMap();
        WriteIntArrayIfAny(mapping, "shopItems", sources.ShopItems);
        WriteIntArrayIfAny(mapping, "boosterPacks", sources.BoosterPacks);
        WriteIntArrayIfAny(mapping, "judgement", sources.Judgement);
        WriteIntArrayIfAny(mapping, "wraith", sources.Wraith);
        WriteIntArrayIfAny(mapping, "riffRaff", sources.RiffRaff);
        WriteIntArrayIfAny(mapping, "rareTag", sources.RareTag);
        WriteIntArrayIfAny(mapping, "uncommonTag", sources.UncommonTag);
        WriteIntArrayIfAny(mapping, "commonShopJokers", sources.CommonShopJokers);
        WriteIntArrayIfAny(mapping, "uncommonShopJokers", sources.UncommonShopJokers);
        WriteIntArrayIfAny(mapping, "rareShopJokers", sources.RareShopJokers);
        WriteIntArrayIfAny(mapping, "allShopJokers", sources.AllShopJokers);
        if (sources.RequireMegaPack)
            mapping.Set("requireMegaPack", JScalar.Of(true), default);
        return mapping;
    }

    private static JMap? WriteLegendarySources(LegendaryJokerSourceConfig? sources)
    {
        if (sources is null)
            return null;
        var mapping = new JMap();
        WriteIntArrayIfAny(mapping, "boosterPacks", sources.BoosterPacks);
        WriteIntArrayIfAny(mapping, "arcanaPacks", sources.ArcanaPacks);
        WriteIntArrayIfAny(mapping, "spectralPacks", sources.SpectralPacks);
        WriteIntArrayIfAny(mapping, "soulCard", sources.SoulCard);
        if (sources.RequireMegaPack)
            mapping.Set("requireMegaPack", JScalar.Of(true), default);
        return mapping;
    }

    private static JMap? WriteTarotSources(TarotCardSourceConfig? sources)
    {
        if (sources is null)
            return null;
        var mapping = new JMap();
        WriteIntArrayIfAny(mapping, "shopItems", sources.ShopItems);
        WriteIntArrayIfAny(mapping, "boosterPacks", sources.BoosterPacks);
        WriteIntArrayIfAny(mapping, "emperor", sources.Emperor);
        WriteIntArrayIfAny(mapping, "purpleSealOrEightBall", sources.PurpleSealOrEightBall);
        if (sources.CharmTag)
            mapping.Set("charmTag", JScalar.Of(true), default);
        if (sources.RequireMegaPack)
            mapping.Set("requireMegaPack", JScalar.Of(true), default);
        return mapping;
    }

    private static JMap? WriteSpectralSources(SpectralCardSourceConfig? sources)
    {
        if (sources is null)
            return null;
        var mapping = new JMap();
        WriteIntArrayIfAny(mapping, "shopItems", sources.ShopItems);
        WriteIntArrayIfAny(mapping, "boosterPacks", sources.BoosterPacks);
        WriteIntArrayIfAny(mapping, "sixthSense", sources.SixthSense);
        WriteIntArrayIfAny(mapping, "seance", sources.Seance);
        if (sources.EtherealTag)
            mapping.Set("etherealTag", JScalar.Of(true), default);
        if (sources.RequireMegaPack)
            mapping.Set("requireMegaPack", JScalar.Of(true), default);
        if (sources.OmenGlobe)
            mapping.Set("omenGlobe", JScalar.Of(true), default);
        return mapping;
    }

    private static JMap? WritePlanetSources(PlanetSourceConfig? sources)
    {
        if (sources is null)
            return null;
        var mapping = new JMap();
        WriteIntArrayIfAny(mapping, "shopItems", sources.ShopItems);
        WriteIntArrayIfAny(mapping, "boosterPacks", sources.BoosterPacks);
        if (sources.RequireMegaPack)
            mapping.Set("requireMegaPack", JScalar.Of(true), default);
        return mapping;
    }

    private static JMap? WriteStandardSources(StandardCardSourceConfig? sources)
    {
        if (sources is null)
            return null;
        var mapping = new JMap();
        WriteIntArrayIfAny(mapping, "shopItems", sources.ShopItems);
        WriteIntArrayIfAny(mapping, "boosterPacks", sources.BoosterPacks);
        if (sources.RequireMegaPack)
            mapping.Set("requireMegaPack", JScalar.Of(true), default);
        WriteIntArrayIfAny(mapping, "certificate", sources.Certificate);
        WriteIntArrayIfAny(mapping, "incantation", sources.Incantation);
        WriteIntArrayIfAny(mapping, "familiar", sources.Familiar);
        WriteIntArrayIfAny(mapping, "grim", sources.Grim);
        WriteIntArrayIfAny(mapping, "deckDraw", sources.DeckDraw);
        return mapping;
    }

    private static void WriteIntArrayIfAny(JMap mapping, string key, int[] values)
    {
        if (values.Length > 0)
            mapping.Set(key, IntArrayNode(values), default);
    }

    private static JNode DiscValueNode<TEnum>(TEnum[] values)
        where TEnum : struct, Enum =>
        values.Length == 0 ? new JScalar("") : EnumArrayNode(values);

    private static JSeq EnumArrayNode<TEnum>(IEnumerable<TEnum> values)
        where TEnum : struct, Enum
    {
        var seq = new JSeq();
        foreach (var v in values)
            seq.Items.Add(new JScalar(v.ToString()!));
        return seq;
    }

    private static JSeq IntArrayNode(IEnumerable<int> values)
    {
        var seq = new JSeq();
        foreach (var v in values)
            seq.Items.Add(JScalar.Of(v));
        return seq;
    }

    private static JSeq StringArrayNode(IEnumerable<string> values)
    {
        var seq = new JSeq();
        foreach (var v in values)
            seq.Items.Add(new JScalar(v));
        return seq;
    }

    private static string ScalarText(JScalar scalar) =>
        scalar.Kind == JScalarKind.Integer ? scalar.Value : ScalarText(scalar.Value);

    private static string ScalarText(string value)
    {
        bool needsQuote =
            value.Length == 0
            || value.Contains(':')
            || value.Contains('#')
            || value.StartsWith('-')
            || value.StartsWith('[')
            || value.Trim() != value;
        return needsQuote ? "\"" + value.Replace("\"", "\\\"") + "\"" : value;
    }
}
