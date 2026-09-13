using System.Text;

namespace Motely.Filters.Jaml;

// Write-side mirror of JamlClausePopulator: JamlConfig → JAML text via typed switches.
// FromJaml(ToJaml(config)) preserves clause data. Text shape may differ (e.g. `tags:` rewrites
// as `tag:`; erraticRanks as or) — still valid, parseable, same meaning. Defaults the loader
// fills in (rolls per wire, antes hoisted from an enclosing and/or) are elided again on the way
// out, so a second write reproduces the first instead of growing.
// No GetProperty / PropertyInfo — every clause family is a concrete write arm.
public static partial class JamlConfigLoader
{
    public static string ToJaml(JamlConfig config)
    {
        var root = new JMap();
        root.Set("id", TextScalar(config.Id), default);
        if (config.Name != null)
            root.Set("name", TextScalar(config.Name), default);
        if (config.Description != null)
            root.Set("description", TextScalar(config.Description), default);
        if (config.Author != null)
            root.Set("author", TextScalar(config.Author), default);
        if (config.Deck != MotelyDeck.Red)
            root.Set("deck", new JScalar(config.Deck.ToString()), default);
        if (config.Stake != MotelyStake.White)
            root.Set("stake", new JScalar(config.Stake.ToString()), default);
        if (config.Seeds.Count > 0)
            root.Set("seeds", StringArrayNode(config.Seeds), default);
        if (config.Filter is { Length: > 0 })
            root.Set("filter", TextScalar(config.Filter), default);
        if (config.Must.Count > 0)
            root.Set("must", ClauseListNode(config.Must, scope: []), default);
        if (config.Should.Count > 0)
            root.Set("should", ClauseListNode(config.Should, scope: []), default);
        if (config.MustNot.Count > 0)
            root.Set("mustNot", ClauseListNode(config.MustNot, scope: []), default);

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
                sb.Append(pad);
                WriteScalarEntry(sb, key, scalar, indent);
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
                WriteScalarEntry(sb, key, scalar, indent);
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

    // `scope` is the ante window an enclosing and/or would hoist into this list on reload
    // (JamlConfigLoader.HoistAntes); empty at the root.
    private static JSeq ClauseListNode(IEnumerable<IJamlClause> clauses, int[] scope)
    {
        var seq = new JSeq();
        foreach (var clause in clauses)
            seq.Items.Add(WriteClause(clause, scope));
        return seq;
    }

    private static JMap WriteClause(IJamlClause clause, int[] scope) =>
        clause switch
        {
            AndClause logic => WriteLogic("and", logic, scope),
            OrClause logic => WriteLogic("or", logic, scope),
            JokerClause c => WriteJokerFamily("joker", c.Jokers, c.Edition, c.Stickers, c.Sources, c, scope),
            CommonJokerClause c => WriteJokerFamily("commonJoker", c.Jokers, c.Edition, c.Stickers, c.Sources, c, scope),
            UncommonJokerClause c => WriteJokerFamily("uncommonJoker", c.Jokers, c.Edition, c.Stickers, c.Sources, c, scope),
            RareJokerClause c => WriteJokerFamily("rareJoker", c.Jokers, c.Edition, c.Stickers, c.Sources, c, scope),
            LegendaryJokerClause c => WriteLegendary(c, scope),
            VoucherClause c => WriteItems("voucher", c.Vouchers, c, scope, rolls: c.Rolls),
            TarotCardClause c => WriteConsumable("tarotCard", c.Tarots, WriteTarotSources(c.Sources), c, scope),
            SpectralCardClause c => WriteConsumable("spectralCard", c.Spectrals, WriteSpectralSources(c.Sources), c, scope),
            PlanetCardClause c => WriteConsumable("planetCard", c.Planets, WritePlanetSources(c.Sources), c, scope),
            StandardCardClause c => WriteStandardCard(c, scope),
            BossClause c => WriteItems("boss", c.Bosses, c, scope),
            TagClause c => WriteItems(TagWire(c.Rolls), c.Tags, c, scope, rolls: c.Rolls),
            BoosterPackClause c => WriteItems("boosterPack", c.Packs, c, scope, rolls: c.Rolls),
            ErraticRankClause c => WriteErraticRank(c, scope),
            ErraticSuitClause c => WriteErraticSuit(c, scope),
            StartingDrawClause c => WriteStartingDraw(c, scope),
            PokerHandClause c => WriteItems("pokerHand", c.PokerHands, c, scope, rolls: c.Rolls),
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

    // TagClause has no wire-name field; the blind-specific spellings are exactly the rolls their
    // attributes default to, so the rolls pick the wire back (same rule as JamlLine.FromTag).
    private static string TagWire(int[] rolls)
    {
        foreach (var wire in TagFilterDesc.Discriminators)
        {
            if (JamlSchema.RollsDefaultFor(wire) is { } wireDefault && wireDefault.SequenceEqual(rolls))
                return wire;
        }
        return TagFilterDesc.Discriminators[0];
    }

    private static JMap WriteLogic(string discriminator, LogicClause logic, int[] scope)
    {
        var mapping = new JMap();
        // HoistAntes fills an empty logic window from the parent, then hoists the logic's own
        // window into its arms — so the arms see this window whether or not it is written.
        int[] childScope = logic.Antes.Length > 0 ? logic.Antes : scope;
        mapping.Set(discriminator, ClauseListNode(logic.Clauses, childScope), default);
        WriteCommonKeys(mapping, logic);
        if (logic.Mode != JamlLogicScoreMode.Sum)
            mapping.Set("mode", new JScalar(logic.Mode.ToString().ToLowerInvariant()), default);
        WriteAntes(mapping, logic.Antes, scope);
        return mapping;
    }

    private static JMap WriteJokerFamily<TEnum>(
        string discriminator,
        TEnum[] jokers,
        MotelyItemEdition? edition,
        MotelyJokerSticker[] stickers,
        JokerSourceConfig? sources,
        IJamlClause clause,
        int[] scope
    )
        where TEnum : struct, Enum
    {
        var mapping = new JMap();
        // Empty jokers = category any → `joker: ""` (empty scalar), not `joker: []`.
        mapping.Set(discriminator, DiscValueNode(jokers), default);
        WriteCommonKeys(mapping, clause);
        WriteAntes(mapping, clause, scope);
        if (edition is { } ed)
            mapping.Set("edition", new JScalar(ed.ToString()), default);
        if (stickers.Length > 0)
            mapping.Set("stickers", EnumArrayNode(stickers), default);
        if (WriteJokerSources(sources) is { } sourcesNode)
            mapping.Set("sources", sourcesNode, default);
        return mapping;
    }

    private static JMap WriteLegendary(LegendaryJokerClause c, int[] scope)
    {
        var mapping = new JMap();
        mapping.Set("legendaryJoker", DiscValueNode(c.Jokers), default);
        WriteCommonKeys(mapping, c);
        WriteAntes(mapping, c, scope);
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
        int[] scope,
        int[]? rolls = null
    )
        where TEnum : struct, Enum
    {
        var mapping = new JMap();
        mapping.Set(discriminator, DiscValueNode(items), default);
        WriteCommonKeys(mapping, clause);
        WriteAntes(mapping, clause, scope);
        // Populate reads a missing rolls: key back as the wire's RollsDefault, so only a
        // departure from it needs to be on the page.
        if (rolls is { } r && !r.SequenceEqual(JamlSchema.RollsDefaultFor(discriminator) ?? []))
            mapping.Set("rolls", IntArrayNode(r), default);
        return mapping;
    }

    private static JMap WriteConsumable<TEnum>(
        string discriminator,
        TEnum[] items,
        JMap? sources,
        IJamlClause clause,
        int[] scope
    )
        where TEnum : struct, Enum
    {
        var mapping = new JMap();
        mapping.Set(discriminator, DiscValueNode(items), default);
        WriteCommonKeys(mapping, clause);
        WriteAntes(mapping, clause, scope);
        if (sources is not null)
            mapping.Set("sources", sources, default);
        return mapping;
    }

    private static JMap WriteStandardCard(StandardCardClause c, int[] scope)
    {
        var mapping = new JMap();
        mapping.Set("standardCard", new JMap(), default);
        WriteCommonKeys(mapping, c);
        WriteAntes(mapping, c, scope);
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

    private static JMap WriteErraticRank(ErraticRankClause c, int[] scope)
    {
        var mapping = new JMap();
        mapping.Set("erraticRank", new JScalar(c.Rank.ToString()), default);
        WriteCommonKeys(mapping, c);
        WriteAntes(mapping, c, scope);
        return mapping;
    }

    private static JMap WriteErraticSuit(ErraticSuitClause c, int[] scope)
    {
        var mapping = new JMap();
        mapping.Set("erraticSuit", new JScalar(c.Suit.ToString()), default);
        WriteCommonKeys(mapping, c);
        WriteAntes(mapping, c, scope);
        return mapping;
    }

    private static JMap WriteStartingDraw(StartingDrawClause c, int[] scope)
    {
        var mapping = new JMap();
        mapping.Set("startingDraw", new JMap(), default);
        WriteCommonKeys(mapping, c);
        WriteAntes(mapping, c, scope);
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
            mapping.Set("label", TextScalar(clause.Label), default);
        if (clause.Min != 1)
            mapping.Set("min", JScalar.Of(clause.Min), default);
        if (clause.Max.HasValue)
            mapping.Set("max", JScalar.Of(clause.Max.Value), default);
        if (clause.Score != 1)
            mapping.Set("score", JScalar.Of(clause.Score), default);
    }

    private static void WriteAntes(JMap mapping, IJamlClause clause, int[] scope)
    {
        if (clause is IAnteScopedClause anteScoped)
            WriteAntes(mapping, anteScoped.Antes, scope);
    }

    // A window equal to the enclosing scope is what HoistAntes would fill in anyway; writing it
    // back would make every trip through the writer restate the parent's antes on each arm.
    private static void WriteAntes(JMap mapping, int[] antes, int[] scope)
    {
        if (antes.Length > 0 && !antes.SequenceEqual(scope))
            mapping.Set("antes", IntArrayNode(antes), default);
    }

    private static JMap? WriteWith(JamlWith with)
    {
        var mapping = new JMap();
        if (with.Luck != MotelyLuck.X1)
            mapping.Set("luck", new JScalar(with.Luck.ToString()), default);
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
        return mapping;
    }

    private static void WriteIntArrayIfAny(JMap mapping, string key, int[] values)
    {
        if (values.Length > 0)
            mapping.Set(key, IntArrayNode(values), default);
    }

    private static JNode DiscValueNode<TEnum>(TEnum[] values)
        where TEnum : struct, Enum =>
        values.Length == 0 ? new JScalar("", JScalarKind.Quoted) : EnumArrayNode(values);

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

    // Author-written text (id, name, description, author, filter, label). Enum names, ints and
    // bools go through the other node builders as Bare/Integer and are never quoted; the quoting
    // decision for free text is made once, here, and carried on the node as its kind.
    private static JScalar TextScalar(string value) =>
        new(value, NeedsQuotes(value) ? JScalarKind.Quoted : JScalarKind.Bare);

    // What the parser would misread inline: an empty value opens a nested block, a leading '['
    // opens a flow array, "{}" is an explicit empty map, '|' / '>' open a block scalar, an outer
    // quote pair is stripped, and Trim() eats padding. ": " and leading indicator characters are
    // quoted for the YAML reader the block form stays compatible with.
    private static bool NeedsQuotes(string value) =>
        value.Length == 0
        || value.Trim() != value
        || value == "{}"
        || value.Contains(": ")
        || value.EndsWith(':')
        || "[]{}|>-?!&*%@`'\"".Contains(value[0])
        || (value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\''));

    private static void WriteScalarEntry(StringBuilder sb, string key, JScalar scalar, int indent)
    {
        if (NeedsBlockScalar(scalar))
        {
            // Literal block: the only inline-proof spelling. Every line is taken raw by
            // ParseBlockScalar, so neither a newline nor a '#' the line-level comment stripper
            // would otherwise cut at (even inside quotes) can reach the value.
            string pad = new(' ', indent + 2);
            sb.Append(key).Append(": |\n");
            foreach (var line in scalar.Value.Split('\n'))
            {
                if (line.Length > 0)
                    sb.Append(pad).Append(line);
                sb.Append('\n');
            }
            return;
        }
        sb.Append(key).Append(": ").Append(ScalarText(scalar)).Append('\n');
    }

    private static bool NeedsBlockScalar(JScalar scalar) =>
        scalar.Kind != JScalarKind.Integer
        && (scalar.Value.Contains('\n') || HasCommentStart(scalar.Value));

    // Mirror of JamlDocumentParser.StripComment: a '#' at the start or after whitespace ends the
    // line before any quote handling runs.
    private static bool HasCommentStart(string value)
    {
        for (int j = 0; j < value.Length; j++)
        {
            if (value[j] == '#' && (j == 0 || char.IsWhiteSpace(value[j - 1])))
                return true;
        }
        return false;
    }

    private static string ScalarText(JScalar scalar) =>
        scalar.Kind == JScalarKind.Quoted ? Quote(scalar.Value) : scalar.Value;

    // The parser strips one matching outer pair and honors no escapes, so a value holding '"' is
    // wrapped in single quotes instead of escaped — the inner characters are never inspected.
    private static string Quote(string value) =>
        value.Contains('"') ? "'" + value + "'" : "\"" + value + "\"";
}
