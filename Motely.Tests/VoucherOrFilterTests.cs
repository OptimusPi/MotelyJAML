using System.Collections.Generic;
using System.Linq;
using Motely.Filters;
using Motely.Filters.Jaml;

namespace Motely.Tests;

/// <summary>
/// A multi-name voucher clause (<c>voucher: [A, B]</c>) is an OR over the names. The SIMD
/// filter unions per-name <c>Vector256.Equals</c> masks, whose true lanes are all-ones (-1):
/// a signed <c>Max</c> over those picks 0 over -1 and empties the union, so the must path
/// rejected every seed while scalar scoring still counted them. These pin SIMD to scalar and
/// the two-name clause to the union of its single-name clauses on a fixed seed list.
/// </summary>
public class VoucherOrFilterTests
{
    // First forty entries of JamlFilters/Zerkeo.jaml's seeds block.
    private static readonly string[] Seeds =
    [
        "F2U88X11", "JX8C8X11", "L8FJ8X11", "A68EBX11", "M2TCJX11", "BC36RX11", "4E1MRX11",
        "KWR8OX11", "BB3BOX11", "K4GBQX11", "B5BCQX11", "6E5RVX11", "ZVIBYX11", "93PK3Y11",
        "VL5LZX11", "YQKSEY11", "L9UYKY11", "3ZYULY11", "BR8KQY11", "YLUASY11", "FV12ZY11",
        "JY843Z11", "FIS83Z11", "6JYF5Z11", "Q1Z79Z11", "DWMQ6Z11", "YBG1AZ11", "PTQ2BZ11",
        "RTL6BZ11", "D5JUCZ11", "Q6SDFZ11", "P3IWFZ11", "QMIQHZ11", "EZ6ALZ11", "UZZFMZ11",
        "AVSHNZ11", "KC1UNZ11", "VJHBOZ11", "9BQHPZ11", "ZHV2SZ11",
    ];

    private static VoucherClause Clause(int[] antes, params MotelyVoucher[] vouchers) =>
        new() { Vouchers = vouchers, Antes = antes, Rolls = [0] };

    private static JamlConfig Config(string id) =>
        new() { Id = id, Deck = MotelyDeck.Red, Stake = MotelyStake.White };

    /// <summary>SIMD must path: VoucherClause is an exact filter confirm, so no scalar re-eval.</summary>
    private static HashSet<string> SimdMustMatches(VoucherClause clause)
    {
        var config = Config("voucher-or-must");
        config.Must.Add(clause);
        var matched = new HashSet<string>();
        using var search = JamlSearchBuilder
            .CreateSettings(config)
            .WithSeedGenerator(Seeds, Seeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithSeedMatchCallback(s => matched.Add(s))
            .Start();
        search.AwaitCompletion();
        return matched;
    }

    /// <summary>Scalar path: the raw JamlScoring occurrence count of the clause per seed.</summary>
    private static Dictionary<string, int> ScalarCounts(VoucherClause clause)
    {
        clause.Score = 1;
        var config = Config("voucher-or-should");
        config.Should.Add(clause);
        var counts = new Dictionary<string, int>();
        using var search = JamlSearchBuilder
            .CreateSettings(config)
            .WithSeedGenerator(Seeds, Seeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithScoredResultCallback(r => counts[r.Seed] = r.GetTally(0))
            .Start();
        search.AwaitCompletion();
        Assert.Equal(Seeds.Length, counts.Count);
        return counts;
    }

    public static IEnumerable<object[]> Pairs()
    {
        // Ante 2 award: Overstock on 4 of these seeds, Telescope on 1, disjoint.
        yield return [MotelyVoucher.Telescope, MotelyVoucher.Overstock, new[] { 2 }];
        // Spread over antes 2-3 so a seed can hit both names across antes.
        yield return [MotelyVoucher.Telescope, MotelyVoucher.Overstock, new[] { 2, 3 }];
        // Every Zerkeo seed awards Hieroglyph at ante 1: one name matches all, the other none.
        yield return [MotelyVoucher.Hieroglyph, MotelyVoucher.Telescope, new[] { 1 }];
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void TwoNameClause_SimdMatchesScalarCount(MotelyVoucher a, MotelyVoucher b, int[] antes)
    {
        var simd = SimdMustMatches(Clause(antes, a, b));
        var scalar = ScalarCounts(Clause(antes, a, b));

        var expected = scalar.Where(kv => kv.Value >= 1).Select(kv => kv.Key).ToHashSet();
        Assert.NotEmpty(expected);
        Assert.Equal(expected.OrderBy(s => s), simd.OrderBy(s => s));
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void TwoNameClause_IsUnionOfSingleNameClauses(MotelyVoucher a, MotelyVoucher b, int[] antes)
    {
        var onlyA = SimdMustMatches(Clause(antes, a));
        var onlyB = SimdMustMatches(Clause(antes, b));
        var both = SimdMustMatches(Clause(antes, a, b));

        var union = onlyA.Union(onlyB).OrderBy(s => s).ToArray();
        Assert.NotEmpty(union);
        Assert.Equal(union, both.OrderBy(s => s).ToArray());
    }

    [Fact]
    public void ThreeNameClause_IsUnionOfSingleNameClauses()
    {
        int[] antes = [2, 3, 4];
        MotelyVoucher[] names = [MotelyVoucher.Grabber, MotelyVoucher.Wasteful, MotelyVoucher.Blank];

        var union = new HashSet<string>();
        foreach (var name in names)
        {
            var single = SimdMustMatches(Clause(antes, name));
            Assert.NotEmpty(single);
            union.UnionWith(single);
        }

        var all = SimdMustMatches(Clause(antes, names));
        Assert.Equal(union.OrderBy(s => s).ToArray(), all.OrderBy(s => s).ToArray());
    }
}
