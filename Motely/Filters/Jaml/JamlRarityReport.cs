namespace Motely.Filters.Jaml;

/// <summary>
/// The seeds a run will actually visit, and a phrase naming them. Every search mode narrows the
/// space differently — a batch range, a seed lake, an explicit list, a keyword pad — and the odds
/// of finding anything are about <em>this</em> space, never the full 35^8. Carrying the
/// description alongside the count keeps the two from drifting apart in the report.
/// </summary>
/// <param name="SeedCount">Seeds the run will visit, or -1 when the mode cannot say.</param>
/// <param name="Description">Human phrase for the space, e.g. "full sequential sweep".</param>
public readonly record struct JamlSearchSpace(long SeedCount, string Description)
{
    /// <summary>A mode that cannot state its size — a lazy generator, for instance.</summary>
    public static readonly JamlSearchSpace Unknown = new(-1, "unknown");

    /// <summary>True when the count is usable for odds arithmetic.</summary>
    public bool IsKnown => SeedCount > 0;
}

/// <summary>
/// What the rarity model could and could not account for.
/// <para>
/// <see cref="PassShare"/> composes only the clauses whose families have declared their odds.
/// Every factor it multiplies is at most 1, so <b>omitting an unknown factor can only make the
/// share larger</b>: whenever <see cref="UnknownClauses"/> is non-zero, PassShare is an
/// <b>upper bound</b> on the true pass probability. That invariant is the whole reason the report
/// can honestly say "at most 1 in N" instead of printing a lie or a NaN.
/// </para>
/// <para>
/// PassShare is never NaN. An unmodeled clause is skipped and counted, never multiplied in — a
/// NaN total tells a caller nothing, where a bound plus a count tells it everything.
/// </para>
/// </summary>
/// <param name="PassShare">Share of seeds surviving must + mustNot. 0.0 means provably impossible.</param>
/// <param name="KnownClauses">Top-level clauses that declared their odds.</param>
/// <param name="UnknownClauses">Top-level clauses that did not.</param>
/// <param name="UnknownFamilies">Distinct wire discriminators of the unknown clauses, for the report.</param>
/// <param name="HasNativeFilter">True when the config names a native filter — a gate the model cannot see.</param>
public readonly record struct JamlRarityEstimate(
    double PassShare,
    int KnownClauses,
    int UnknownClauses,
    string[] UnknownFamilies,
    bool HasNativeFilter
)
{
    /// <summary>Nothing was modeled, so no number derived from it may be printed.</summary>
    public bool IsEmpty => KnownClauses <= 0;

    /// <summary>Every clause declared its odds, so the share is an estimate rather than a bound.</summary>
    public bool IsComplete => KnownClauses > 0 && UnknownClauses == 0 && !HasNativeFilter;
}

/// <summary>
/// Turns a rarity estimate into the block printed before a search starts: how rare the filter is,
/// whether the space being searched is likely to hold a match at all, and how long the first one
/// should take.
/// <para>
/// Pure string formatting and arithmetic — no console, no filesystem, no reflection — because this
/// assembly is also consumed by Motely.Wasm, Motely.MCP and NativeAOT builds. The caller owns the
/// stream; this owns the words. It also makes the whole block unit-testable without a terminal.
/// </para>
/// <para>
/// Every rendered number is approximate and says so. Clause probabilities are composed assuming
/// independent PRNG streams, which they are not, and speed is projected from a worst-case crunch
/// model. The report's job is to stop someone burning nine hours on a filter that cannot match —
/// not to be right to three decimal places.
/// </para>
/// </summary>
public static class JamlRarityReport
{
    /// <summary>
    /// Seeds in the full 8-character sequential space, 35^8. Not the 2,318,107,019,761 in
    /// <c>SeedMath</c> — that is the global bijective end offset including every shorter seed,
    /// a different quantity that would overstate this one by 3%.
    /// </summary>
    public const long FullSequentialSeedSpace = 2_251_875_390_625L;

    private const string UnknownText = "unknown";

    /// <summary>
    /// A probability that arithmetic may be built on. Every formatter and every math helper gates
    /// on this, so a NaN arriving from a half-built model surfaces as "unknown" rather than as a
    /// confident-looking digit.
    /// </summary>
    public static bool IsUsableProbability(double p) =>
        !double.IsNaN(p) && !double.IsInfinity(p) && p > 0.0 && p <= 1.0;

    // ── notation ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Short magnitude notation. Runs to T and beyond rather than stopping at B, because a filter
    /// genuinely can be 1 in 40 trillion and "40200B" is not a number anyone can read.
    /// </summary>
    public static string Humanize(double n)
    {
        if (double.IsNaN(n) || double.IsInfinity(n))
            return UnknownText;

        double magnitude = Math.Abs(n);

        // "expect ~0.06 matches" is the whole warning; rounding it to "0" throws the point away.
        if (magnitude > 0 && magnitude < 10 && n != Math.Floor(n))
            return $"{n:0.##}";

        return magnitude >= 1e15 ? $"{n:0.##e+0}"
            : magnitude >= 1e12 ? $"{n / 1e12:0.##}T"
            : magnitude >= 1e9 ? $"{n / 1e9:0.##}B"
            : magnitude >= 1e6 ? $"{n / 1e6:0.##}M"
            : magnitude >= 1e3 ? $"{n / 1e3:0.##}K"
            : $"{n:N0}";
    }

    /// <summary>"1 in 165M" from a seeds-per-hit figure.</summary>
    public static string OneIn(double seedsPerHit) =>
        double.IsNaN(seedsPerHit) || double.IsInfinity(seedsPerHit) || seedsPerHit <= 0
            ? UnknownText
            : $"1 in {Humanize(seedsPerHit)}";

    /// <summary>
    /// "1 in 1.53M" from a measured sample. Zero matches reports a floor rather than dividing by
    /// zero or claiming impossibility — all a dry sample proves is that it is rarer than the slice.
    /// </summary>
    public static string OneInNotation(long matches, long searched)
    {
        if (searched <= 0)
            return "n/a";
        if (matches <= 0)
            return $"> 1 in {Humanize(searched)}";
        return OneIn(searched / (double)matches);
    }

    /// <summary>
    /// Wall-clock in units a person can act on. Deliberately not a TimeSpan format string: the
    /// obvious <c>hh\:mm\:ss</c> renders the hours-<em>within-day</em> component, so 34 days prints
    /// as "23:00:00" — a wrong number that looks entirely reasonable.
    /// </summary>
    public static string Duration(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
            return "effectively never";
        if (seconds < 1)
            return $"{seconds * 1000:N0} ms";
        if (seconds < 90)
            return $"{seconds:0.#}s";
        if (seconds < 5400)
            return $"{(int)(seconds / 60)}m {(int)(seconds % 60)}s";
        if (seconds < 172_800)
            return $"{(int)(seconds / 3600)}h {(int)(seconds % 3600 / 60)}m";
        if (seconds < 34_560_000)
            return $"{(int)(seconds / 86_400)}d {(int)(seconds % 86_400 / 3600)}h";

        double years = seconds / 31_557_600.0;
        return years >= 1e6 ? "effectively never" : $"~{years:0.#} years";
    }

    /// <summary>
    /// Percent, saturating at both ends so the line never claims a certainty the model cannot
    /// have — ">99.9%" is honest where "100%" is not.
    /// </summary>
    public static string Percent(double probability)
    {
        if (double.IsNaN(probability) || double.IsInfinity(probability) || probability < 0)
            return UnknownText;
        if (probability <= 0)
            return "0%";
        if (probability >= 0.9995)
            return ">99.9%";
        if (probability < 0.001)
            return "<0.1%";
        return $"{probability * 100:0.#}%";
    }

    /// <summary>
    /// Throughput in the same shape the running progress line uses, so the projection and the
    /// live figure can be compared at a glance instead of mentally rescaled.
    /// </summary>
    public static string Speed(double seedsPerSecond)
    {
        if (double.IsNaN(seedsPerSecond) || double.IsInfinity(seedsPerSecond) || seedsPerSecond <= 0)
            return UnknownText;
        return seedsPerSecond >= 1_000_000 ? $"{seedsPerSecond / 1_000_000:F2} M/s"
            : seedsPerSecond >= 1_000 ? $"{seedsPerSecond / 1_000:F1} K/s"
            : $"{seedsPerSecond:F0}/s";
    }

    // ── math ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>How many matches the space should contain. Below 1 is the warning case.</summary>
    public static double ExpectedMatches(double p, long space) =>
        !IsUsableProbability(p) || space <= 0 ? double.NaN : space * p;

    /// <summary>
    /// The odds of at least one match in the space actually being searched — the number that
    /// answers "am I about to waste an afternoon".
    /// <para>
    /// The Poisson form is used below 1e-6 on purpose. <c>1 - p</c> rounds to exactly 1.0 once p
    /// drops under about 1e-16, at which point <c>Math.Log(1 - p)</c> returns 0 and the honest
    /// answer silently becomes "impossible". That failure fires precisely for the rarest filters,
    /// which are the ones this line exists to warn about.
    /// </para>
    /// </summary>
    public static double AtLeastOneProbability(double p, long space)
    {
        if (!IsUsableProbability(p) || space <= 0)
            return double.NaN;
        if (p >= 1.0)
            return 1.0;

        double lambda = p < 1e-6 ? space * p : -space * Math.Log(1.0 - p);

        if (lambda >= 40.0)
            return 1.0; // e^-40 < 1e-17: saturate rather than let the subtraction round it anyway
        if (lambda < 1e-12)
            return lambda; // 1 - Exp(-tiny) cancels to zero; lambda is the limit
        return 1.0 - Math.Exp(-lambda);
    }

    /// <summary>Expected wall-clock seconds to the first match, ignoring where in the space it sits.</summary>
    public static double SecondsToFirstMatch(double p, double seedsPerSecond) =>
        !IsUsableProbability(p) || !(seedsPerSecond > 0) || double.IsInfinity(seedsPerSecond)
            ? double.NaN
            : 1.0 / (p * seedsPerSecond);

    /// <summary>Seconds to exhaust the space — the other half of "you will finish empty-handed".</summary>
    public static double SecondsToSweepSpace(long space, double seedsPerSecond) =>
        space <= 0 || !(seedsPerSecond > 0) || double.IsInfinity(seedsPerSecond)
            ? double.NaN
            : space / seedsPerSecond;

    // ── the block ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Two-space indent, label padded so every value starts at column 9 — house style.</summary>
    private static string Line(string label, string value) => $"  {label,-6} {value}";

    /// <summary>At most three family names, so the line never wraps on a wide filter.</summary>
    private static string NameList(string[] families)
    {
        if (families.Length == 0)
            return "unnamed clauses";
        if (families.Length <= 3)
            return string.Join(", ", families);
        return $"{string.Join(", ", families.Take(3))}, and {families.Length - 3} more";
    }

    /// <summary>
    /// The pre-search block.
    /// <para>
    /// This re-derives whether the share is usable rather than trusting the estimate it was handed.
    /// A PassShare of NaN alongside a non-zero KnownClauses is a bug in the model, and it must
    /// surface as "unknown" — never as "1 in NaN". Nothing downstream of an unusable share is
    /// rendered at all: the Odds/Find/Note group is omitted, not zero-filled.
    /// </para>
    /// </summary>
    /// <param name="rarity">What the model could account for.</param>
    /// <param name="space">The seeds this run will actually visit.</param>
    /// <param name="seedsPerSecond">Projected or measured throughput; null when the machine has never been timed.</param>
    /// <param name="speedIsMeasured">True once a live rate is in hand, which changes "(est.)" to "(measured)".</param>
    /// <param name="simdCostPerSeed">From <see cref="JamlCostModelSimdExtensions.SimdCostPerSeed"/>.</param>
    /// <param name="filterCrunchesPerBatch">From <see cref="JamlCostModelSimdExtensions.EstimateFilterCrunches"/>.</param>
    /// <param name="collectLimit">Matches the run stops after, or 0 when it sweeps the space.</param>
    public static IReadOnlyList<string> Render(
        in JamlRarityEstimate rarity,
        in JamlSearchSpace space,
        double? seedsPerSecond,
        bool speedIsMeasured,
        double simdCostPerSeed,
        int filterCrunchesPerBatch,
        long collectLimit
    )
    {
        List<string> lines =
        [
            Line(
                "Cost:",
                $"~{simdCostPerSeed:0.#} crunches/seed "
                    + $"({filterCrunchesPerBatch:N0} per {MotelyGlobals.MaxVectorWidth}-seed batch, worst case)"
            ),
        ];

        double p = rarity.PassShare;
        bool usable = IsUsableProbability(p) && !rarity.IsEmpty;
        bool bounded = usable && !rarity.IsComplete;

        // "Rare:" — three shapes, and the impossible case is its own, because a filter that cannot
        // match is a different problem from one nobody has modeled yet.
        if (rarity.IsEmpty)
            lines.Add(Line("Rare:", $"unknown — no rarity model yet for: {NameList(rarity.UnknownFamilies)}"));
        else if (p <= 0.0)
            lines.Add(Line("Rare:", "impossible — this filter can never match on this deck and stake"));
        else if (!usable)
            lines.Add(Line("Rare:", "unknown — the model returned no usable share"));
        else if (bounded)
            lines.Add(
                Line(
                    "Rare:",
                    $"rarer than ~{OneIn(1.0 / p)}  (model: {rarity.KnownClauses}/"
                        + $"{rarity.KnownClauses + rarity.UnknownClauses}"
                        + (rarity.UnknownClauses > 0 ? $" — no model yet for: {NameList(rarity.UnknownFamilies)}" : "")
                        + (rarity.HasNativeFilter ? " — plus a native filter" : "")
                        + ")"
                )
            );
        else
            lines.Add(Line("Rare:", $"~{OneIn(1.0 / p)}  (model: {rarity.KnownClauses}/{rarity.KnownClauses} clauses)"));

        lines.Add(
            Line(
                "Space:",
                space.IsKnown
                    ? $"{(space.SeedCount < 10_000_000 ? space.SeedCount.ToString("N0") : Humanize(space.SeedCount))} seeds — {space.Description}"
                    : space.Description
            )
        );

        if (!usable)
            return lines;

        // "Odds:" is about a fixed space. A run that stops after N matches has no such space, so
        // the line is omitted rather than answered about a sweep that will not happen.
        double atLeastOne = double.NaN;
        if (space.IsKnown && collectLimit <= 0)
        {
            atLeastOne = AtLeastOneProbability(p, space.SeedCount);
            double expected = ExpectedMatches(p, space.SeedCount);
            if (!double.IsNaN(atLeastOne))
                lines.Add(
                    Line(
                        "Odds:",
                        (bounded ? "at most " : "")
                            + $"{Percent(atLeastOne)} at least one here"
                            + (double.IsNaN(expected) ? "" : $" — expect ~{Humanize(expected)}")
                    )
                );
        }

        double secondsToFirst = double.NaN;
        if (seedsPerSecond is { } speed && speed > 0)
        {
            secondsToFirst = SecondsToFirstMatch(p, speed);
            string suffix = speedIsMeasured ? "(measured)" : "(est.)";
            string body =
                collectLimit > 1
                    ? $"~{Duration(secondsToFirst)} to the first, ~{Duration(secondsToFirst * collectLimit)} for all {collectLimit}"
                    : $"~{Duration(secondsToFirst)} to the first match";
            lines.Add(Line("Find:", (bounded ? "at least " : "") + $"{body} @ {Speed(speed)} {suffix}"));
        }
        else
        {
            lines.Add(Line("Find:", "unknown until a run has been timed on this machine"));
        }

        // The warning the whole feature exists for: the sweep runs out before the odds turn over.
        if (space.IsKnown && collectLimit <= 0 && !double.IsNaN(atLeastOne) && atLeastOne < 0.5)
        {
            string sweep =
                seedsPerSecond is { } v && v > 0
                    ? $"this sweep exhausts the space in ~{Duration(SecondsToSweepSpace(space.SeedCount, v))}, so "
                    : "";
            lines.Add(Line("Note:", $"{sweep}you will most likely finish empty-handed."));
            lines.Add(Line("", "Relax the filter, or search a different deck/stake."));
        }

        return lines;
    }
}

