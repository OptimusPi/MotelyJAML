using System.Diagnostics;
using Motely.Filters;

namespace Motely.Filters.Jaml;

/// <summary>
/// Projects a config's raw crunch estimate onto the SIMD execution model: one crunch retires
/// <see cref="MotelyGlobals.MaxVectorWidth"/> seeds at once, so a config's wall-clock speed is
/// the crunch rate spread across the lanes. Everything here is worst-case (every filter runs on
/// every batch, no lane short-circuit credit) — a floor on speed, never a promise. Estimates
/// never change results.
///
/// The per-clause estimate is the clause's ante span — how many antes its PRNG streams must
/// walk — with no attribute lookup and no reflection, so the whole path stays AOT-clean. Ante
/// span is the dominant cost driver; a clause that scans one ante is far cheaper than one that
/// scans all eight, and that ratio is what the projection captures.
///
/// Deliberately extension methods over existing types — no new instance member and no new type
/// crosses the Bootsharp / NativeAOT-LLVM boundary. The declaration scanner only sees static
/// call sites, so the marshaled interop surface is byte-identical with or without this file.
/// </summary>
public static class JamlCostModelSimdExtensions
{
    /// <summary>
    /// Raw crunches for one clause, recursing through and/or groups: a logic clause costs the
    /// sum of its children (worst case: nothing short-circuits), never less than one crunch. A
    /// flat clause costs its ante span — the antes it walks, where an empty ante list means
    /// "anywhere" and normalizes to all 8, matching how JamlSearchBuilder expands it. Clauses
    /// that aren't ante-scoped (e.g. roll-scoped events) cost one crunch.
    /// </summary>
    public static int EstimateCrunches(this IJamlClause clause)
    {
        if (clause is not LogicClause logic)
            return clause is IAnteScopedClause ante
                ? Math.Max(1, ante.Antes.Length > 0 ? ante.Antes.Length : 8)
                : 1;

        int total = 0;
        foreach (var child in logic.Clauses)
            total += child.EstimateCrunches();
        return Math.Max(1, total);
    }

    /// <summary>
    /// Crunches one 8-seed batch spends in the SIMD filter pass: every must and mustNot clause,
    /// worst case. This is the cost the searcher pays per batch before scoring ever runs.
    /// </summary>
    public static int EstimateFilterCrunches(this JamlConfig config)
    {
        int total = 0;
        foreach (var clause in config.Must)
            total += clause.EstimateCrunches();
        foreach (var clause in config.MustNot)
            total += clause.EstimateCrunches();
        return Math.Max(1, total);
    }

    /// <summary>
    /// Effective crunches per *seed*: the batch cost amortized across the lanes that retire
    /// together. This is the number to compare between two configs — "config A costs 3.5
    /// crunches a seed, config B costs 41" — independent of any machine's crunch rate.
    /// </summary>
    public static double SimdCostPerSeed(this JamlConfig config) =>
        (double)config.EstimateFilterCrunches() / MotelyGlobals.MaxVectorWidth;

    /// <summary>
    /// Projected search speed in seeds/second given a measured crunch rate (crunches/second —
    /// calibrate once per machine by timing any known filter). Worst-case floor: real searches
    /// run faster as early clauses kill whole batches and skip the rest of the chain.
    /// </summary>
    public static double ProjectedSeedsPerSecond(this JamlConfig config, double crunchesPerSecond) =>
        crunchesPerSecond / config.EstimateFilterCrunches() * MotelyGlobals.MaxVectorWidth;

    /// <summary>Estimate a clause and amortize it across the SIMD lanes in one call.</summary>
    public static double SimdProjectedCost(this IJamlClause clause) =>
        (double)clause.EstimateCrunches() / MotelyGlobals.MaxVectorWidth;

    /// <summary>
    /// The odds that at least <paramref name="atLeast"/> of <paramref name="trials"/> independent
    /// rolls come up, each with probability <paramref name="p"/>. Every Events family is this one
    /// shape — n roll indices, a per-roll chance, and a <c>min</c> — so the twelve of them differ
    /// only in which <c>MotelyGlobals.*Chance</c> they divide by.
    /// <para>
    /// <c>with.luck</c> divides into the chance, so <paramref name="p"/> genuinely can arrive
    /// above 1 — that one is clamped, because certainty is what a lucky enough clause means.
    /// A clause asking for more hits than it has rolls can never match; that is the validator's
    /// to reject, and asserting here keeps the hole visible instead of returning a quiet zero.
    /// </para>
    /// </summary>
    public static double AtLeastOf(int atLeast, int trials, double p)
    {
        Debug.Assert(atLeast >= 1, "Clause min must be at least 1.");
        Debug.Assert(atLeast <= trials, "Clause min exceeds its roll count — invalid filter.");

        p = Math.Min(p, 1.0);
        if (p == 1.0)
            return 1.0;

        // Sum the tail directly: term_k marches from k to k+1 by the standard ratio, so no
        // factorials are ever formed and nothing overflows on the way.
        double q = 1.0 - p;
        double term = Math.Pow(q, trials); // k = 0
        double tail = 0.0;

        for (int k = 0; k <= trials; k++)
        {
            if (k >= atLeast)
                tail += term;
            if (k < trials)
                term *= (double)(trials - k) / (k + 1) * (p / q);
        }

        return tail;
    }

    /// <summary>
    /// The must/mustNot chains reordered cheapest-first, without mutating the config.
    /// <see cref="JamlSearchBuilder.CreateSettings"/> feeds this order into
    /// <c>WithAdditionalFilter</c> so trivial SIMD clauses kill lanes before expensive scalar
    /// ones run. Stable sort: equal-cost clauses keep their authored order.
    /// </summary>
    public static IEnumerable<IJamlClause> CheapestFirst(this IReadOnlyList<IJamlClause> clauses) =>
        clauses.OrderBy(EstimateCrunches);
}
