using System.Runtime.Intrinsics;

namespace Motely.Tests;

/// <summary>
/// Pins the packed-int contract of <see cref="MotelyItem"/> and its SIMD twin
/// <see cref="MotelyItemVector"/>: every facet reads and writes through one int, facets never
/// disturb each other, With* never mutates, and FormatItem/Parse round-trips the whole facet
/// set. These laws are what the WASM boundary and the vector lane extraction both rely on.
/// </summary>
public sealed class MotelyItemContractTests
{
    // ── MotelyItem: facet packing ───────────────────────────────────────────────

    [Fact]
    public void Facets_SetOne_DoesNotDisturbOthers()
    {
        var item = new MotelyItem(MotelyJoker.Blueprint, MotelyItemEdition.Foil)
        {
            Seal = MotelyItemSeal.Red,
            Enhancement = MotelyItemEnhancement.Glass,
            IsEternal = true,
            IsPerishable = true,
            IsRental = true,
        };

        Assert.Equal(MotelyItemTypeCategory.Joker, item.TypeCategory);
        Assert.Equal(MotelyItemEdition.Foil, item.Edition);
        Assert.Equal(MotelyItemSeal.Red, item.Seal);
        Assert.Equal(MotelyItemEnhancement.Glass, item.Enhancement);
        Assert.True(item.IsEternal);
        Assert.True(item.IsPerishable);
        Assert.True(item.IsRental);

        // Clearing one sticker leaves the rest of the word intact.
        item.IsPerishable = false;
        Assert.False(item.IsPerishable);
        Assert.True(item.IsEternal);
        Assert.Equal(MotelyItemEdition.Foil, item.Edition);
    }

    [Fact]
    public void StandardCardFacets_RankAndSuit_RoundTrip()
    {
        var card = new MotelyItem((int)MotelyItemTypeCategory.Standardcard)
        {
            StandardcardRank = MotelyStandardcardRank.Ten,
            StandardcardSuit = MotelyStandardcardSuit.Hearts,
        };

        Assert.Equal(MotelyStandardcardRank.Ten, card.StandardcardRank);
        Assert.Equal(MotelyStandardcardSuit.Hearts, card.StandardcardSuit);
        Assert.Equal(MotelyItemTypeCategory.Standardcard, card.TypeCategory);

        card.StandardcardRank = MotelyStandardcardRank.Ace;
        Assert.Equal(MotelyStandardcardRank.Ace, card.StandardcardRank);
        Assert.Equal(MotelyStandardcardSuit.Hearts, card.StandardcardSuit);
    }

    [Fact]
    public void WithMethods_ReturnNewValue_WithoutMutating()
    {
        var original = new MotelyItem(MotelyItemType.TheSoul);
        var decorated = original
            .WithEdition(MotelyItemEdition.Negative)
            .WithSeal(MotelyItemSeal.Gold)
            .WithEnhancement(MotelyItemEnhancement.Lucky)
            .WithEternal(true)
            .WithPerishable(true)
            .WithRental(true);

        Assert.Equal(MotelyItemEdition.None, original.Edition);
        Assert.Equal(MotelyItemEdition.Negative, decorated.Edition);
        Assert.Equal(MotelyItemSeal.Gold, decorated.Seal);
        Assert.Equal(MotelyItemEnhancement.Lucky, decorated.Enhancement);
        Assert.True(decorated.IsEternal && decorated.IsPerishable && decorated.IsRental);
        Assert.Equal(original.Type, decorated.Type);

        var stripped = decorated
            .WithEternal(false)
            .WithPerishable(false)
            .WithRental(false)
            .WithEdition(MotelyItemEdition.None);
        Assert.False(stripped.IsEternal || stripped.IsPerishable || stripped.IsRental);
        Assert.Equal(MotelyItemEdition.None, stripped.Edition);
    }

    [Fact]
    public void AsType_ReplacesTypeKeepsDecoration()
    {
        var item = new MotelyItem(MotelyJoker.Blueprint, MotelyItemEdition.Holographic);
        var swapped = item.AsType(MotelyItemType.TheSoul);
        Assert.Equal(MotelyItemType.TheSoul, swapped.Type);
        Assert.Equal(MotelyItemEdition.Holographic, swapped.Edition);
    }

    [Fact]
    public void Equality_IsValueEquality()
    {
        var a = new MotelyItem(MotelyJoker.Blueprint, MotelyItemEdition.Foil);
        var b = new MotelyItem(MotelyJoker.Blueprint, MotelyItemEdition.Foil);
        var c = b.WithEdition(MotelyItemEdition.None);

        Assert.True(a == b);
        Assert.True(a.Equals((object)b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a != c);
        Assert.False(a.Equals("not an item"));

        MotelyItem implicitItem = MotelyItemType.BlackHole;
        Assert.Equal(MotelyItemType.BlackHole, implicitItem.Type);
    }

    [Fact]
    public void Invalid_IsDetectedByCategory()
    {
        Assert.False(new MotelyItem(MotelyItemType.TheSoul).IsInvalid);
        Assert.True(new MotelyItem((int)MotelyItemTypeCategory.Invalid).IsInvalid);
    }

    [Fact]
    public void ToString_StacksDecorationsInContractOrder()
    {
        var item = new MotelyItem(MotelyJoker.Blueprint, MotelyItemEdition.Foil)
            .WithSeal(MotelyItemSeal.Blue)
            .WithEnhancement(MotelyItemEnhancement.Glass)
            .WithEternal(true);

        var text = item.ToString();
        Assert.Contains("Blueprint", text);
        Assert.Contains("Foil", text);
        Assert.Contains("Glass", text);
        Assert.Contains("Eternal", text);
        Assert.Contains("Blue Seal", text);
    }

    // ── FormatItem / Parse round-trip ───────────────────────────────────────────

    public static TheoryData<int> RoundTripItems() =>
        new()
        {
            new MotelyItem(MotelyJoker.Blueprint, MotelyItemEdition.Negative).Value,
            new MotelyItem(MotelyJoker.Perkeo, MotelyItemEdition.None).WithEternal(true).Value,
            new MotelyItem(MotelyItemType.TheSoul).Value,
            new MotelyItem(MotelyItemType.BlackHole).Value,
            new MotelyItem((int)MotelyItemTypeCategory.Standardcard)
            {
                StandardcardRank = MotelyStandardcardRank.Queen,
                StandardcardSuit = MotelyStandardcardSuit.Spades,
            }.Value,
            new MotelyItem((int)MotelyItemTypeCategory.Standardcard)
            {
                StandardcardRank = MotelyStandardcardRank.Seven,
                StandardcardSuit = MotelyStandardcardSuit.Clubs,
                Seal = MotelyItemSeal.Purple,
                Enhancement = MotelyItemEnhancement.Lucky,
                Edition = MotelyItemEdition.Polychrome,
            }.Value,
        };

    [Theory]
    [MemberData(nameof(RoundTripItems))]
    public void FormatItem_Parse_RoundTrips(int packedValue)
    {
        var item = new MotelyItem(packedValue);
        var formatted = FormatUtils.FormatItem(item);
        var parsed = MotelyItem.Parse(formatted);
        Assert.Equal(item, parsed);
    }

    [Fact]
    public void TryParse_Garbage_ReturnsFalse()
    {
        Assert.False(MotelyItem.TryParse("Absolutely Not A Real Item", out _));
        Assert.True(MotelyItem.TryParse(FormatUtils.FormatItem(new(MotelyItemType.TheSoul)), out var soul));
        Assert.Equal(MotelyItemType.TheSoul, soul.Type);
    }

    // ── MotelyItemVector: lane laws ─────────────────────────────────────────────

    [Fact]
    public void Vector_Broadcast_PutsItemInEveryLane()
    {
        var item = new MotelyItem(MotelyJoker.Blueprint, MotelyItemEdition.Foil);
        var vector = new MotelyItemVector(item);

        for (int lane = 0; lane < MotelyItemVector.Count; lane++)
            Assert.Equal(item, vector[lane]);
    }

    [Fact]
    public void Vector_FacetVectors_MatchScalarFacets()
    {
        var item = new MotelyItem(MotelyJoker.Blueprint, MotelyItemEdition.Holographic)
            .WithSeal(MotelyItemSeal.Red)
            .WithEnhancement(MotelyItemEnhancement.Steel)
            .WithEternal(true)
            .WithPerishable(true)
            .WithRental(true);
        var vector = new MotelyItemVector(item);

        for (int lane = 0; lane < MotelyItemVector.Count; lane++)
        {
            Assert.Equal(item.Type, vector.Type[lane]);
            Assert.Equal(item.TypeCategory, vector.TypeCategory[lane]);
            Assert.Equal(item.Seal, vector.Seal[lane]);
            Assert.Equal(item.Enhancement, vector.Enhancement[lane]);
            Assert.Equal(item.Edition, vector.Edition[lane]);
        }

        Assert.True(vector.IsEternal.IsAllTrue());
        Assert.True(vector.IsPerishable.IsAllTrue());
        Assert.True(vector.IsRental.IsAllTrue());
    }

    [Fact]
    public void Vector_WithMethods_MatchScalarWithMethods()
    {
        var item = new MotelyItem(MotelyJoker.Blueprint);
        var vector = new MotelyItemVector(item)
            .WithSeal(MotelyItemSeal.Gold)
            .WithEnhancement(MotelyItemEnhancement.Glass)
            .WithEdition(MotelyItemEdition.Negative);

        var scalar = item
            .WithSeal(MotelyItemSeal.Gold)
            .WithEnhancement(MotelyItemEnhancement.Glass)
            .WithEdition(MotelyItemEdition.Negative);

        for (int lane = 0; lane < MotelyItemVector.Count; lane++)
            Assert.Equal(scalar, vector[lane]);
    }

    [Fact]
    public void Vector_Equals_ComparesPerLane()
    {
        var a = new MotelyItemVector(new MotelyItem(MotelyItemType.TheSoul));
        var b = new MotelyItemVector(new MotelyItem(MotelyItemType.TheSoul));
        var c = new MotelyItemVector(new MotelyItem(MotelyItemType.BlackHole));

        Assert.Equal(Vector256<int>.AllBitsSet, MotelyItemVector.Equals(a, b));
        Assert.Equal(Vector256<int>.Zero, MotelyItemVector.Equals(a, c));
        Assert.Equal(
            Vector256<int>.AllBitsSet,
            MotelyItemVector.Equals(a, new MotelyItem(MotelyItemType.TheSoul))
        );
    }

    // ── MotelyVectorItemSet ─────────────────────────────────────────────────────

    [Fact]
    public void ItemSet_AppendContainsAndExtract()
    {
        var soul = new MotelyItem(MotelyItemType.TheSoul);
        var joker = new MotelyItem(MotelyJoker.Blueprint);

        MotelyVectorItemSet set = new();
        set.Append(new MotelyItemVector(soul));
        set.Append(new MotelyItemVector(joker));

        Assert.Equal(2, set.Length);
        Assert.Equal(Vector256<int>.AllBitsSet, set.Contains(MotelyItemType.TheSoul));
        Assert.Equal(Vector256<int>.AllBitsSet, set.Contains(joker));
        Assert.Equal(Vector256<int>.AllBitsSet, set.Contains(new MotelyItemVector(soul)));
        Assert.Equal(Vector256<int>.Zero, set.Contains(MotelyItemType.BlackHole));

        var array = set.AsArray();
        Assert.Equal(2, array.Length);
        Assert.Equal(soul, array[0][0]);
        Assert.Equal(joker, set[1][0]);

        set[0] = new MotelyItemVector(new MotelyItem(MotelyItemType.BlackHole));
        Assert.Equal(Vector256<int>.AllBitsSet, set.Contains(MotelyItemType.BlackHole));

        Assert.Contains("BlackHole", set.ToString());
    }
}
