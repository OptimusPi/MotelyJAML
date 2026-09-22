namespace Motely.Filters.Jaml;

/// <summary>
/// Closed-form rarity for the twelve roll-scoped event families.
/// <para>
/// Every one of them has the same shape: a fixed list of PRNG roll indices, each an independent
/// Bernoulli trial at a rate the game states as a constant, and a <c>min</c>/<c>max</c> window on
/// how many of them fire. So there is exactly one piece of maths here — the binomial window — and
/// each family contributes only its rate. No seeds are visited: <c>luckyMoney: [0]</c> is one draw
/// against <c>1/15</c>, and the answer is a division.
/// </para>
/// <para>
/// Nothing in here is an approximation of the runtime; it is the runtime's own arithmetic read
/// forward instead of sampled. The one modelling assumption is that distinct rolls off the same
/// stream are independent, which is what a PRNG stream is for.
/// </para>
/// </summary>
internal static class JamlRollRarity
{
    /// <summary>
    /// Per-trial probability from the way the game actually spells these odds: a
    /// <c>MotelyGlobals.*Chance</c> denominator, divided into an Oops! All 6s multiplier. The
    /// runtime is literally <c>GetNextRandom() &lt; luck / chance</c>, so this is that expression.
    /// <para>
    /// Clamped to 1, and that clamp is not defensive tidying — it is the runtime's behaviour.
    /// Business Card is <c>chance = 2</c>, so a single Oops makes the comparison <c>&lt; 1.0</c>
    /// and a second makes it <c>&lt; 2.0</c>: always true, guaranteed, not "200% likely". Letting
    /// the ratio past 1 would hand the composer a factor that makes a filter look <em>more</em>
    /// likely than certain and inflate every product it lands in.
    /// </para>
    /// </summary>
    /// <param name="chance">The <c>1 in N</c> denominator, e.g. 15 for Lucky money.</param>
    /// <param name="luck">The clause's Oops! multiplier; 1 when the family has no <c>with:</c>.</param>
    public static double Rate(double chance, double luck = 1.0)
    {
        if (double.IsNaN(chance) || double.IsNaN(luck) || chance <= 0.0 || luck <= 0.0)
            return 0.0;

        double rate = luck / chance;
        return rate >= 1.0 ? 1.0 : rate;
    }

    /// <summary>
    /// The share of seeds whose trigger count lands in the clause's <c>min</c>/<c>max</c> window.
    /// </summary>
    /// <param name="clause">Supplies the rolls and the window.</param>
    /// <param name="perTrial">Probability that any one roll fires.</param>
    public static double Window(IRollScopedClause clause, double perTrial) =>
        Window(DistinctRolls(clause.Rolls), clause.Min, clause.Max, perTrial);

    /// <summary>
    /// <c>P(min ≤ X ≤ max)</c> for <c>X ~ Binomial(trials, perTrial)</c>.
    /// <para>
    /// Returns an honest <c>0.0</c> — never NaN — for a clause that cannot match, such as
    /// <c>min</c> above the number of rolls offered. Zero is a *modelled* answer meaning
    /// "impossible", which the report prints as such; NaN would demote a fact to "nobody knows".
    /// </para>
    /// </summary>
    public static double Window(int trials, int min, int? max, double perTrial)
    {
        if (double.IsNaN(perTrial) || trials < 0)
            return double.NaN;

        // Mirrors MeetsOccurrenceBounds exactly, including that a Max of 0 means "no upper gate"
        // rather than "at most zero" — the two readings differ on every clause that sets it.
        int upper = max is { } m && m > 0 ? Math.Min(m, trials) : trials;
        int lower = Math.Max(min, 0);

        if (lower > upper)
            return 0.0;
        if (lower <= 0 && upper >= trials)
            return 1.0; // the window covers every outcome; the rate never enters into it

        // Both degenerate rates put all mass on one outcome, and both would divide by zero in the
        // recurrence below. Saturated luck reaches the first case in real configs, not just theory.
        if (perTrial >= 1.0)
            return lower <= trials && trials <= upper ? 1.0 : 0.0;
        if (perTrial <= 0.0)
            return lower <= 0 && 0 <= upper ? 1.0 : 0.0;

        // Iterative terms rather than factorials: C(n,k) overflows a double long before the
        // product does, and the ratio form stays exact at rates like Cavendish's 1/1000.
        double odds = perTrial / (1.0 - perTrial);
        double term = Math.Pow(1.0 - perTrial, trials); // k = 0
        double total = lower == 0 ? term : 0.0;

        for (int k = 0; k < upper; k++)
        {
            term *= (trials - k) / (double)(k + 1) * odds;
            if (k + 1 >= lower)
                total += term;
        }

        return total > 1.0 ? 1.0 : total;
    }

    /// <summary>
    /// How many trials a roll list really represents. Repeats are one draw, not two: the filters
    /// walk the stream once and step past a duplicate index without re-rolling it, so
    /// <c>[0, 0, 1]</c> is two trials. Counting the array length instead would quietly make every
    /// duplicated roll look like a fresh chance to succeed.
    /// </summary>
    public static int DistinctRolls(int[] rolls)
    {
        if (rolls.Length <= 1)
            return rolls.Length;

        HashSet<int> seen = [];
        foreach (int roll in rolls)
            seen.Add(roll);
        return seen.Count;
    }
}
