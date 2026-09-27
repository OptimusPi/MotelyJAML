using System.Runtime.Intrinsics;

namespace Motely.Tests;

/// <summary>
/// R3 parity: <see cref="MotelyItemVector"/> is the 8-lane form of <see cref="MotelyItem"/>, so
/// every vector operation is checked lane-by-lane against the scalar operation on the same item.
/// A packing bug that shifted a field by one bit would break these without breaking a
/// vector-only round-trip, which is the point.
/// </summary>
public sealed class MotelyItemVectorParityTests
{
    /// <summary>Eight deliberately unlike items so no lane accidentally mirrors its neighbour.</summary>
    private static MotelyItem[] SampleItems() =>
        [
            new MotelyItem(MotelyItemType.Pluto),
            new MotelyItem(MotelyJoker.Blueprint),
            new MotelyItem(MotelyJoker.Perkeo, MotelyItemEdition.Negative),
            new MotelyItem(MotelyItemType.JokerExcludedByStream),
            new MotelyItem(MotelyJoker.Mime, MotelyItemEdition.Foil)
                .WithEternal(true),
            new MotelyItem(MotelyJoker.Cavendish).WithPerishable(true),
            new MotelyItem(MotelyJoker.Showman).WithRental(true),
            new MotelyItem(MotelyJoker.Canio, MotelyItemEdition.Polychrome)
                .WithEnhancement(MotelyItemEnhancement.Glass),
        ];

    private static MotelyItemVector Pack(MotelyItem[] items) =>
        new(
            Vector256.Create(
                items[0].Value,
                items[1].Value,
                items[2].Value,
                items[3].Value,
                items[4].Value,
                items[5].Value,
                items[6].Value,
                items[7].Value
            )
        );

    [Fact]
    public void Indexer_RoundTripsEveryLane()
    {
        var items = SampleItems();
        var vector = Pack(items);

        Assert.Equal(8, MotelyItemVector.Count);
        for (int lane = 0; lane < MotelyItemVector.Count; lane++)
            Assert.Equal(items[lane].Value, vector[lane].Value);
    }

    [Fact]
    public void FieldAccessors_MatchScalarPerLane()
    {
        var items = SampleItems();
        var vector = Pack(items);

        for (int lane = 0; lane < MotelyItemVector.Count; lane++)
        {
            Assert.Equal(items[lane].Type, vector.Type[lane]);
            Assert.Equal(items[lane].TypeCategory, vector.TypeCategory[lane]);
            Assert.Equal(items[lane].Seal, vector.Seal[lane]);
            Assert.Equal(items[lane].Enhancement, vector.Enhancement[lane]);
            Assert.Equal(items[lane].Edition, vector.Edition[lane]);
            Assert.Equal(items[lane].StandardcardSuit, vector.StandardcardSuit[lane]);
            Assert.Equal(items[lane].StandardcardRank, vector.StandardcardRank[lane]);
        }
    }

    [Fact]
    public void StickerMasks_MatchScalarPerLane()
    {
        var items = SampleItems();
        var vector = Pack(items);

        var perishable = vector.IsPerishable;
        var eternal = vector.IsEternal;
        var rental = vector.IsRental;

        for (int lane = 0; lane < MotelyItemVector.Count; lane++)
        {
            Assert.Equal(items[lane].IsPerishable, perishable[lane]);
            Assert.Equal(items[lane].IsEternal, eternal[lane]);
            Assert.Equal(items[lane].IsRental, rental[lane]);
        }

        // The samples are built so each sticker is set on exactly one lane.
        Assert.Equal(1, CountSetLanes(perishable));
        Assert.Equal(1, CountSetLanes(eternal));
        Assert.Equal(1, CountSetLanes(rental));
    }

    private static int CountSetLanes(VectorMask mask)
    {
        int count = 0;
        for (int lane = 0; lane < VectorMask.Length; lane++)
            if (mask[lane])
                count++;
        return count;
    }

    [Fact]
    public void Broadcast_FillsEveryLaneWithTheSameItem()
    {
        var item = new MotelyItem(MotelyJoker.Perkeo, MotelyItemEdition.Negative);
        var vector = new MotelyItemVector(item);

        for (int lane = 0; lane < MotelyItemVector.Count; lane++)
            Assert.Equal(item.Value, vector[lane].Value);

        Assert.True(((VectorMask)MotelyItemVector.Equals(vector, item)).IsAllTrue());
        Assert.True(((VectorMask)MotelyItemVector.Equals(vector, vector)).IsAllTrue());
    }

    [Fact]
    public void Equals_IsPerLaneNotWholeVector()
    {
        var items = SampleItems();
        var vector = Pack(items);

        var probe = items[2];
        var mask = (VectorMask)MotelyItemVector.Equals(vector, probe);

        Assert.False(mask.IsAllTrue());
        Assert.False(mask.IsAllFalse());
        for (int lane = 0; lane < MotelyItemVector.Count; lane++)
            Assert.Equal(items[lane].Value == probe.Value, mask[lane]);
    }

    // ── mutators: vector result must equal scalar result on every lane ──

    [Fact]
    public void AsType_MatchesScalarPerLane()
    {
        var items = SampleItems();
        var actual = Pack(items).AsType(MotelyItemType.PlanetExcludedByStream);

        for (int lane = 0; lane < MotelyItemVector.Count; lane++)
            Assert.Equal(
                items[lane].AsType(MotelyItemType.PlanetExcludedByStream).Value,
                actual[lane].Value
            );
    }

    [Theory]
    [InlineData(MotelyItemSeal.None)]
    [InlineData(MotelyItemSeal.Gold)]
    [InlineData(MotelyItemSeal.Red)]
    [InlineData(MotelyItemSeal.Blue)]
    [InlineData(MotelyItemSeal.Purple)]
    public void WithSeal_MatchesScalarPerLane(MotelyItemSeal seal)
    {
        var items = SampleItems();
        var packed = Pack(items);

        var byValue = packed.WithSeal(seal);
        var byVector = packed.WithSeal(VectorEnum256.Create(seal));

        for (int lane = 0; lane < MotelyItemVector.Count; lane++)
        {
            Assert.Equal(items[lane].WithSeal(seal).Value, byValue[lane].Value);
            Assert.Equal(byValue[lane].Value, byVector[lane].Value);
            Assert.Equal(seal, byValue.Seal[lane]);
        }
    }

    [Theory]
    [InlineData(MotelyItemEnhancement.None)]
    [InlineData(MotelyItemEnhancement.Bonus)]
    [InlineData(MotelyItemEnhancement.Glass)]
    [InlineData(MotelyItemEnhancement.Steel)]
    [InlineData(MotelyItemEnhancement.Gold)]
    public void WithEnhancement_MatchesScalarPerLane(MotelyItemEnhancement enhancement)
    {
        var items = SampleItems();
        var packed = Pack(items);

        var byValue = packed.WithEnhancement(enhancement);
        var byVector = packed.WithEnhancement(VectorEnum256.Create(enhancement));

        for (int lane = 0; lane < MotelyItemVector.Count; lane++)
        {
            Assert.Equal(items[lane].WithEnhancement(enhancement).Value, byValue[lane].Value);
            Assert.Equal(byValue[lane].Value, byVector[lane].Value);
            Assert.Equal(enhancement, byValue.Enhancement[lane]);
        }
    }

    [Theory]
    [InlineData(MotelyItemEdition.None)]
    [InlineData(MotelyItemEdition.Foil)]
    [InlineData(MotelyItemEdition.Holographic)]
    [InlineData(MotelyItemEdition.Polychrome)]
    [InlineData(MotelyItemEdition.Negative)]
    public void WithEdition_MatchesScalarPerLane(MotelyItemEdition edition)
    {
        var items = SampleItems();
        var packed = Pack(items);

        var byValue = packed.WithEdition(edition);
        var byVector = packed.WithEdition(VectorEnum256.Create(edition));

        for (int lane = 0; lane < MotelyItemVector.Count; lane++)
        {
            Assert.Equal(items[lane].WithEdition(edition).Value, byValue[lane].Value);
            Assert.Equal(byValue[lane].Value, byVector[lane].Value);
            Assert.Equal(edition, byValue.Edition[lane]);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Stickers_ScalarBoolOverloadMatchesScalarPerLane(bool set)
    {
        var items = SampleItems();
        var packed = Pack(items);

        var perishable = packed.WithPerishable(set);
        var eternal = packed.WithEternal(set);
        var rental = packed.WithRental(set);

        for (int lane = 0; lane < MotelyItemVector.Count; lane++)
        {
            Assert.Equal(items[lane].WithPerishable(set).Value, perishable[lane].Value);
            Assert.Equal(items[lane].WithEternal(set).Value, eternal[lane].Value);
            Assert.Equal(items[lane].WithRental(set).Value, rental[lane].Value);
        }

        Assert.Equal(set ? 8 : 0, CountSetLanes(perishable.IsPerishable));
        Assert.Equal(set ? 8 : 0, CountSetLanes(eternal.IsEternal));
        Assert.Equal(set ? 8 : 0, CountSetLanes(rental.IsRental));
    }

    /// <summary>
    /// The per-lane sticker overloads are the ones the search actually uses — they must set the
    /// sticker only where the selector says, and clear it everywhere else.
    /// </summary>
    [Fact]
    public void Stickers_VectorSelectorOverloadSetsOnlySelectedLanes()
    {
        var items = SampleItems();
        var packed = Pack(items);
        var selector = MotelyVectorUtils.VectorMaskToConditionalSelectMask(
            new VectorMask(0b0011_0110)
        );

        var perishable = packed.WithPerishable(selector);
        var eternal = packed.WithEternal(selector);
        var rental = packed.WithRental(selector);

        for (int lane = 0; lane < MotelyItemVector.Count; lane++)
        {
            bool expected = selector[lane] != 0;
            Assert.Equal(expected, perishable.IsPerishable[lane]);
            Assert.Equal(expected, eternal.IsEternal[lane]);
            Assert.Equal(expected, rental.IsRental[lane]);

            // Setting one sticker leaves the item's type alone.
            Assert.Equal(items[lane].Type, perishable.Type[lane]);
        }
    }

    [Fact]
    public void ToString_ListsEveryLane()
    {
        var text = new MotelyItemVector(new MotelyItem(MotelyJoker.Blueprint)).ToString();

        Assert.StartsWith("<", text);
        Assert.EndsWith(">", text);
        Assert.Equal(7, text.Count(static c => c == ','));
    }
}
