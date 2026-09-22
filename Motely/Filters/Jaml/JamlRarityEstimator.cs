namespace Motely.Filters.Jaml;

/// <summary>
/// Composes one config's clauses into the single share of seeds that survives it — the number the
/// pre-search block turns into "1 in 165M".
/// <para>
/// Only <c>must</c> and <c>mustNot</c> appear here. <c>should</c> clauses move a score, never a
/// gate, so a seed that fails every one of them still passes the filter; folding them in would
/// make the search look far rarer than it is.
/// </para>
/// <para>
/// Two approximations, both deliberate and both stated in the rendered block. Clauses are composed
/// as independent, which their PRNG streams broadly are but not exactly. And an unmodelled clause
/// is <em>skipped</em> rather than guessed — every factor is at most 1, so dropping one can only
/// make the share larger, which is what lets the report say "rarer than 1 in N" and be right.
/// </para>
/// </summary>
public static class JamlRarityEstimator
{
    /// <summary>What the model can account for across the whole config.</summary>
    public static JamlRarityEstimate Estimate(JamlConfig config)
    {
        var ctx = JamlRarityContext.From(config);
        double share = 1.0;
        int known = 0;
        int unknown = 0;
        List<string> families = [];

        void Contribute(IJamlClause clause, bool negated)
        {
            // A conjunction under `must` is just more must-clauses — the product is associative, so
            // flattening it keeps each child separately attributable. Collapsed into one factor, a
            // group with a single unmodelled arm would throw away its modelled arms too.
            if (!negated && clause is AndClause group)
            {
                foreach (var child in group.Clauses)
                    Contribute(child, negated: false);
                return;
            }

            double p = Probability(clause, in ctx);

            if (double.IsNaN(p))
            {
                unknown++;
                string family = WireName(clause);
                if (!families.Contains(family))
                    families.Add(family);
                return;
            }

            known++;
            share *= negated ? 1.0 - p : p;
        }

        foreach (var clause in config.Must)
            Contribute(clause, negated: false);

        foreach (var clause in config.MustNot)
            Contribute(clause, negated: true);

        return new JamlRarityEstimate(
            share,
            known,
            unknown,
            [.. families],
            HasNativeFilter: !string.IsNullOrWhiteSpace(config.Filter)
        );
    }

    /// <summary>
    /// One clause's own share. Groupings compose their children here rather than in the dispatch,
    /// because only this layer knows what "and"/"or" mean — the dispatch answers for families.
    /// </summary>
    private static double Probability(IJamlClause clause, in JamlRarityContext ctx) =>
        clause switch
        {
            AndClause c => Product(c.Clauses, in ctx),
            OrClause c => AtLeast(c.Clauses, c.Min, in ctx),
            _ => JamlClauseDescDispatch.EstimateRarity(clause, in ctx),
        };

    /// <summary>
    /// Every arm must match. NaN when any arm is unmodelled: unlike the top-level walk, a group
    /// cannot drop an arm and stay an upper bound once something outside it — a negation, an
    /// enclosing <c>or</c> — may invert the result.
    /// </summary>
    private static double Product(IJamlClause[] clauses, in JamlRarityContext ctx)
    {
        if (clauses.Length == 0)
            return double.NaN;

        double product = 1.0;
        foreach (var clause in clauses)
        {
            double p = Probability(clause, in ctx);
            if (double.IsNaN(p))
                return double.NaN;
            product *= p;
        }
        return product;
    }

    /// <summary>
    /// At least <paramref name="min"/> of the arms match — <c>or:</c>'s actual gate, which is a
    /// count and not merely "any of them".
    /// <para>
    /// The arms carry different probabilities, so this is a Poisson binomial rather than the
    /// familiar <c>1 - Π(1-p)</c>; that shortcut is only the special case min = 1. Building the
    /// whole distribution costs nothing at these sizes and answers every min in the same line.
    /// </para>
    /// </summary>
    private static double AtLeast(IJamlClause[] clauses, int min, in JamlRarityContext ctx)
    {
        if (clauses.Length == 0)
            return double.NaN;

        // exactly[k] — the chance that precisely k arms match, extended one arm at a time.
        double[] exactly = new double[clauses.Length + 1];
        exactly[0] = 1.0;
        int seen = 0;

        foreach (var clause in clauses)
        {
            double p = Probability(clause, in ctx);
            if (double.IsNaN(p))
                return double.NaN;

            seen++;
            for (int k = seen; k >= 1; k--)
                exactly[k] = exactly[k] * (1.0 - p) + exactly[k - 1] * p;
            exactly[0] *= 1.0 - p;
        }

        double total = 0.0;
        for (int k = Math.Max(min, 0); k <= seen; k++)
            total += exactly[k];

        return total > 1.0 ? 1.0 : total;
    }

    /// <summary>
    /// The wire name of a clause's family, for the report's "no model yet for: …" note.
    /// <para>
    /// Read off the type name, which every family spells as <c>&lt;wireName&gt;Clause</c>. That
    /// convention is load-bearing enough to be worth a switch except that this string is only ever
    /// shown to a person — nothing branches on it — and a test round-trips every family through
    /// <c>JamlSchema</c> to keep the convention honest.
    /// </para>
    /// </summary>
    internal static string WireName(IJamlClause clause)
    {
        string name = clause.GetType().Name;

        if (name.EndsWith("Clause", StringComparison.Ordinal))
            name = name[..^"Clause".Length];

        return name.Length == 0 ? "clause" : char.ToLowerInvariant(name[0]) + name[1..];
    }
}
