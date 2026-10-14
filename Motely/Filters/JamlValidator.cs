namespace Motely.Filters;

/// <summary>
/// What a loaded filter must satisfy before the engine runs it: every bound the SIMD filters and
/// the scorer rely on, checked once here instead of asserted in each of them. A failure names the
/// clause by its place in the document (<c>should[2] (tag)</c>) so the author can go straight to
/// it, and every failure is reported, not just the first. Also turns a whole card
/// (<c>standardCard: AceOfSpades</c>) into the rank and suit the filters match on.
/// </summary>
public static class JamlValidator
{
    public static void Validate(JamlConfig config)
    {
        var errors = new List<string>();
        if (config.Should.Count > MotelyScoredSeedResult.MAX_TALLY_COUNT)
            errors.Add($"should: {config.Should.Count} clauses; a seed's tally holds at most {MotelyScoredSeedResult.MAX_TALLY_COUNT}.");
        Walk("must", config.Must, errors);
        Walk("should", config.Should, errors);
        Walk("mustNot", config.MustNot, errors);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
    }

    private static void Walk(string path, IReadOnlyList<IMotelyClause> clauses, List<string> errors)
    {
        for (int i = 0; i < clauses.Count; i++)
        {
            var clause = clauses[i];
            string at = $"{path}[{i}] ({Key(clause)})";
            Check(at, clause, errors);
            if (clause is LogicClause logic)
                Walk(at, logic.Clauses, errors);
        }
    }

    /// <summary>The clause's key as the document writes it: <c>JokerClause</c> is <c>joker</c>.</summary>
    private static string Key(IMotelyClause clause)
    {
        string name = clause.GetType().Name;
        name = name[..^"Clause".Length];
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static void Check(string at, IMotelyClause clause, List<string> errors)
    {
        void Fail(string reason) => errors.Add($"{at}: {reason}");

        if (clause.Min < 1)
            Fail($"min {clause.Min}; a clause must ask for at least 1.");
        if (clause.Max is { } max && max < clause.Min)
            Fail($"max {max} is below min {clause.Min}.");
        if (clause.Label is { } label && label.AsSpan().IndexOfAny(",\r\n") >= 0)
            Fail("label names a CSV column, so it cannot hold a comma or a line break.");

        if (clause is IAnteScopedClause scoped)
            CheckIndices(at, "antes", scoped.Antes, MotelyGlobals.MaxAnte, "the last ante", errors);
        if (clause is IRollScopedClause rolled)
            CheckIndices(at, "rolls", rolled.Rolls, MotelyGlobals.MaxRollIndex, "the last roll", errors);

        switch (clause)
        {
            case LogicClause { Clauses.Length: 0 }:
                Fail("needs at least one clause.");
                break;
            case OrClause either when either.Min > either.Clauses.Length:
                Fail($"min {either.Min} is more arms than its {either.Clauses.Length}.");
                break;
            case MisprintMultClause m when m.Mult is < MotelyGlobals.JokerMisprintMin or > MotelyGlobals.JokerMisprintMax:
                Fail($"mult {m.Mult}; Misprint rolls {MotelyGlobals.JokerMisprintMin}..{MotelyGlobals.JokerMisprintMax}.");
                break;
            case StandardCardClause c when c.Card is { } card:
                (c.Rank, c.Suit) = WholeCard(at, card, c.Rank, c.Suit, errors);
                break;
            case StartingDrawClause d when d.Card is { } card:
                (d.Rank, d.Suit) = WholeCard(at, card, d.Rank, d.Suit, errors);
                break;
            // rolls on legendaryJoker is optional (empty = every opened Soul), so it is only
            // bounds-checked when given.
            case LegendaryJokerClause l when l.Rolls.Length > 0:
                CheckIndices(at, "rolls", l.Rolls, MotelyGlobals.MaxRollIndex, "the last roll", errors);
                break;
            case LegendarySoulOrderClause so:
                CheckSoulOrder(at, so, errors);
                break;
        }
    }

    private static void CheckSoulOrder(string at, LegendarySoulOrderClause clause, List<string> errors)
    {
        if (clause.Entries.Length == 0)
        {
            errors.Add($"{at}: soulOrder needs at least one soul.");
            return;
        }

        if (clause.MaxAnte < 1 || clause.MaxAnte > MotelyGlobals.MaxAnte)
            errors.Add($"{at}: maxAnte {clause.MaxAnte} is outside 1..{MotelyGlobals.MaxAnte}.");

        for (int i = 0; i < clause.Entries.Length; i++)
        {
            var entry = clause.Entries[i];
            if (
                ((MotelyJokerRarity)((int)entry.Legendary & MotelyGlobals.JokerRarityMask))
                != MotelyJokerRarity.Legendary
            )
                errors.Add($"{at}: soul[{i}] names {entry.Legendary}, which is not a legendary.");
            if (entry.Antes.Length > 0)
                CheckIndices(at, $"soul[{i}].antes", entry.Antes, clause.MaxAnte, "maxAnte", errors);
        }

        if (clause.BoosterPacks.Length > 0)
            CheckIndices(
                at,
                "boosterPacks",
                clause.BoosterPacks,
                MotelyGlobals.LateAntesMaxPackSlot,
                "the last pack slot",
                errors
            );
    }

    private static void CheckIndices(string at, string name, int[] values, int max, string maxName, List<string> errors)
    {
        if (values.Length == 0)
        {
            errors.Add($"{at}: {name} cannot be empty.");
            return;
        }
        foreach (int value in values)
        {
            if (value < 0)
                errors.Add($"{at}: {name} {value} is negative.");
            else if (value > max)
                errors.Add($"{at}: {name} {value} is past {maxName}, {max}.");
        }
        if (values.Distinct().Count() != values.Length)
            errors.Add($"{at}: {name} lists an index twice.");
    }

    private static (MotelyStandardcardRank? Rank, MotelyStandardcardSuit? Suit) WholeCard(
        string at,
        MotelyStandardCard card,
        MotelyStandardcardRank? rank,
        MotelyStandardcardSuit? suit,
        List<string> errors
    )
    {
        if (rank is { } r && r != card.GetRank())
            errors.Add($"{at}: {card} has rank {card.GetRank()}, not {r}.");
        if (suit is { } s && s != card.GetSuit())
            errors.Add($"{at}: {card} has suit {card.GetSuit()}, not {s}.");
        return (card.GetRank(), card.GetSuit());
    }
}
