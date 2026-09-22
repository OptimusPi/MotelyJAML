namespace Motely.Filters.Jaml;

/// <summary>
/// Counts, not coins. Every ante-scoped family answers "how many times did my thing turn up across
/// these antes and sources", and its <c>min</c>/<c>max</c> is a window on that count. The sources
/// are not identical trials — a shop slot is one draw, a booster slot is a pack of two to five
/// cards that may or may not even be the right kind of pack — so a single binomial cannot describe
/// the total. A probability mass function can: index <c>k</c> holds <c>P(count = k)</c>.
/// <para>
/// The whole toolkit is four operations. A source yields a pmf (<see cref="Bernoulli"/>,
/// <see cref="Binomial"/>, a <see cref="Mixture"/> of those when the source's own shape is random),
/// independent sources <see cref="Convolve"/>, and the clause's gate is read off the total with
/// <see cref="Window"/>. The families contribute only their rates and pool sizes.
/// </para>
/// <para>
/// The one modelling assumption, inherited from the roll families and stated once here: distinct
/// sources are treated as independent. Distinct PRNG streams broadly are; cards within one pack
/// are not quite (a duplicate is re-rolled), which is why a pack's cards are a binomial here and
/// a hypergeometric in truth — a sub-percent difference at these pool sizes.
/// </para>
/// </summary>
internal static class JamlCountDistribution
{
    /// <summary>The count that is certainly zero — the identity for <see cref="Convolve"/>.</summary>
    public static readonly double[] Zero = [1.0];

    /// <summary>One trial at rate <paramref name="p"/>; rates outside [0,1] are clamped, NaN is zero.</summary>
    public static double[] Bernoulli(double p)
    {
        p = Clamp(p);
        return p <= 0.0 ? Zero : [1.0 - p, p];
    }

    /// <summary><paramref name="trials"/> independent trials at rate <paramref name="p"/>.</summary>
    public static double[] Binomial(int trials, double p)
    {
        p = Clamp(p);
        if (trials <= 0 || p <= 0.0)
            return Zero;
        if (p >= 1.0)
        {
            double[] certain = new double[trials + 1];
            certain[trials] = 1.0;
            return certain;
        }

        // Iterative terms, as in JamlRollRarity.Window: C(n,k) overflows long before the product.
        double[] pmf = new double[trials + 1];
        double odds = p / (1.0 - p);
        double term = Math.Pow(1.0 - p, trials);
        pmf[0] = term;
        for (int k = 0; k < trials; k++)
        {
            term *= (trials - k) / (double)(k + 1) * odds;
            pmf[k + 1] = term;
        }
        return pmf;
    }

    /// <summary>
    /// <paramref name="draws"/> cards dealt without replacement from a deck of
    /// <paramref name="population"/> holding <paramref name="successes"/> that match — the starting
    /// hand off a shuffled deck. Exact, not a binomial stand-in: eight of fifty-two is far enough
    /// from "with replacement" to matter.
    /// </summary>
    public static double[] Hypergeometric(int population, int successes, int draws)
    {
        if (population <= 0 || draws <= 0 || successes <= 0)
            return Zero;
        successes = Math.Min(successes, population);
        draws = Math.Min(draws, population);

        int maxCount = Math.Min(draws, successes);
        double[] pmf = new double[maxCount + 1];

        // P(X = k) = C(K,k) C(N−K, n−k) / C(N, n), walked as a ratio of consecutive terms so the
        // binomial coefficients never have to be formed. The walk starts at the smallest count the
        // deal allows (more draws than failures forces some successes), whose probability is one
        // particular sequence — that many successes first, then failures — times its arrangements.
        int minCount = Math.Max(0, draws - (population - successes));
        double term = 1.0;
        for (int i = 0; i < draws; i++)
        {
            term *= i < minCount
                ? (successes - i) / (double)(population - i)
                : (population - successes - (i - minCount)) / (double)(population - i);
        }
        for (int i = 0; i < minCount; i++)
            term *= (draws - i) / (double)(i + 1); // × C(draws, minCount)
        pmf[minCount] = term;

        for (int k = minCount; k < maxCount; k++)
        {
            // P(k+1) / P(k) = (K−k)(n−k) / ((k+1)(N−K−n+k+1))
            term *= (successes - k) * (double)(draws - k)
                / ((k + 1) * (double)(population - successes - draws + k + 1));
            pmf[k + 1] = term;
        }
        return pmf;
    }

    /// <summary>
    /// A source whose own shape is random: with probability <c>weight_i</c> it behaves as
    /// <c>pmf_i</c>. Any weight not accounted for is the chance the source yields nothing — a pack
    /// slot that rolled the wrong kind of pack — and lands on count zero, so callers list only the
    /// shapes that can match.
    /// </summary>
    public static double[] Mixture(IReadOnlyList<(double Weight, double[] Pmf)> parts)
    {
        int length = 1;
        double covered = 0.0;
        foreach (var (weight, pmf) in parts)
        {
            if (weight <= 0.0)
                continue;
            covered += weight;
            if (pmf.Length > length)
                length = pmf.Length;
        }

        double[] result = new double[length];
        result[0] = Math.Max(0.0, 1.0 - covered);
        foreach (var (weight, pmf) in parts)
        {
            if (weight <= 0.0)
                continue;
            for (int k = 0; k < pmf.Length; k++)
                result[k] += weight * pmf[k];
        }
        return result;
    }

    /// <summary>The count of two independent sources together.</summary>
    public static double[] Convolve(double[] a, double[] b)
    {
        if (a.Length == 1)
            return b;
        if (b.Length == 1)
            return a;

        double[] result = new double[a.Length + b.Length - 1];
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] == 0.0)
                continue;
            for (int j = 0; j < b.Length; j++)
                result[i + j] += a[i] * b[j];
        }
        return result;
    }

    /// <summary>
    /// <c>P(min ≤ count ≤ max)</c>, with exactly <c>MeetsOccurrenceBounds</c>'s reading of the
    /// gate: <c>min</c> is the lower bound, and <c>max</c> is an upper bound only when it is
    /// positive — null or zero means no ceiling. A window nothing can land in is an honest
    /// <c>0.0</c>, never NaN: asking for more matches than the sources can produce is impossible,
    /// and the report prints impossible as such.
    /// </summary>
    public static double Window(double[] pmf, int min, int? max)
    {
        int lower = Math.Max(min, 0);
        int upper = max is { } m && m > 0 ? m : int.MaxValue;
        if (lower > upper)
            return 0.0;

        double total = 0.0;
        for (int k = lower; k < pmf.Length && k <= upper; k++)
            total += pmf[k];

        return total > 1.0 ? 1.0 : total < 0.0 ? 0.0 : total;
    }

    private static double Clamp(double p) =>
        double.IsNaN(p) ? 0.0 : p < 0.0 ? 0.0 : p > 1.0 ? 1.0 : p;
}
