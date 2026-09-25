using System.Runtime.CompilerServices;

namespace Motely;

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public struct MotelyItem(int value) : IEquatable<MotelyItem>
{
    public int Value { get; set; } = value;

    public MotelyItemType Type
    {
        readonly get { return (MotelyItemType)(Value & MotelyGlobals.ItemTypeMask); }
        set { Value = (Value & ~MotelyGlobals.ItemTypeMask) | (int)value; }
    }
    public MotelyItemTypeCategory TypeCategory
    {
        readonly get { return (MotelyItemTypeCategory)(Value & MotelyGlobals.ItemTypeCategoryMask); }
        set { Value = (Value & ~MotelyGlobals.ItemTypeCategoryMask) | (int)value; }
    }
    public MotelyItemSeal Seal
    {
        readonly get { return (MotelyItemSeal)(Value & MotelyGlobals.ItemSealMask); }
        set { Value = (Value & ~MotelyGlobals.ItemSealMask) | (int)value; }
    }
    public MotelyItemEnhancement Enhancement
    {
        readonly get { return (MotelyItemEnhancement)(Value & MotelyGlobals.ItemEnhancementMask); }
        set { Value = (Value & ~MotelyGlobals.ItemEnhancementMask) | (int)value; }
    }
    public MotelyItemEdition Edition
    {
        readonly get { return (MotelyItemEdition)(Value & MotelyGlobals.ItemEditionMask); }
        set { Value = (Value & ~MotelyGlobals.ItemEditionMask) | (int)value; }
    }

    public MotelyStandardcardSuit StandardcardSuit
    {
        readonly get { return (MotelyStandardcardSuit)(Value & MotelyGlobals.StandardcardSuitMask); }
        set { Value = (Value & ~MotelyGlobals.StandardcardSuitMask) | (int)value; }
    }
    public MotelyStandardcardRank StandardcardRank
    {
        readonly get { return (MotelyStandardcardRank)(Value & MotelyGlobals.StandardcardRankMask); }
        set { Value = (Value & ~MotelyGlobals.StandardcardRankMask) | (int)value; }
    }

    public bool IsPerishable
    {
        readonly get { return (Value & (1 << MotelyGlobals.PerishableStickerOffset)) != 0; }
        set
        {
            int mask = 1 << MotelyGlobals.PerishableStickerOffset;
            Value = value ? (Value | mask) : (Value & ~mask);
        }
    }
    public bool IsEternal
    {
        readonly get { return (Value & (1 << MotelyGlobals.EternalStickerOffset)) != 0; }
        set
        {
            int mask = 1 << MotelyGlobals.EternalStickerOffset;
            Value = value ? (Value | mask) : (Value & ~mask);
        }
    }
    public bool IsRental
    {
        readonly get { return (Value & (1 << MotelyGlobals.RentalStickerOffset)) != 0; }
        set
        {
            int mask = 1 << MotelyGlobals.RentalStickerOffset;
            Value = value ? (Value | mask) : (Value & ~mask);
        }
    }

    public readonly bool IsInvalid
    {
        get { return TypeCategory == MotelyItemTypeCategory.Invalid; }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MotelyItem(MotelyItemType type)
        : this((int)type) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MotelyItem(MotelyStandardCard card)
        : this((int)card | (int)MotelyItemTypeCategory.Standardcard) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MotelyItem(MotelyJoker joker, MotelyItemEdition edition = MotelyItemEdition.None)
        : this((int)joker | (int)MotelyItemTypeCategory.Joker | (int)edition) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly MotelyItem AsType(MotelyItemType type)
    {
        return new((Value & ~MotelyGlobals.ItemTypeMask) | (int)type);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly MotelyItem WithSeal(MotelyItemSeal seal)
    {
        return new((Value & ~MotelyGlobals.ItemSealMask) | (int)seal);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly MotelyItem WithEnhancement(MotelyItemEnhancement enhancement)
    {
        return new((Value & ~MotelyGlobals.ItemEnhancementMask) | (int)enhancement);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly MotelyItem WithEdition(MotelyItemEdition edition)
    {
        return new((Value & ~MotelyGlobals.ItemEditionMask) | (int)edition);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly MotelyItem WithPerishable(bool isPerishable)
    {
        int mask = 1 << MotelyGlobals.PerishableStickerOffset;
        return new(isPerishable ? (Value | mask) : (Value & ~mask));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly MotelyItem WithEternal(bool isEternal)
    {
        int mask = 1 << MotelyGlobals.EternalStickerOffset;
        return new(isEternal ? (Value | mask) : (Value & ~mask));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly MotelyItem WithRental(bool isRental)
    {
        int mask = 1 << MotelyGlobals.RentalStickerOffset;
        return new(isRental ? (Value | mask) : (Value & ~mask));
    }

    public override readonly string ToString()
    {
        string stringified = Type.ToString();

        if (Enhancement != MotelyItemEnhancement.None)
        {
            stringified = Enhancement + " " + stringified;
        }

        if (Edition != MotelyItemEdition.None)
        {
            stringified = Edition + " " + stringified;
        }

        if (IsPerishable)
            stringified = "Perishable " + stringified;
        if (IsEternal)
            stringified = "Eternal " + stringified;
        if (IsRental)
            stringified = "Rental " + stringified;

        if (Seal != MotelyItemSeal.None)
        {
            stringified = Seal + " Seal " + stringified;
        }

        return stringified;
    }

    public static MotelyItem Parse(string formatted)
    {
        return FormatUtils.ParseMotelyItem(formatted);
    }

    public static bool TryParse(string formatted, out MotelyItem item)
    {
        return FormatUtils.TryParseMotelyItem(formatted, out item);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Equals(MotelyItem other)
    {
        return Value == other.Value;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is MotelyItem item && Equals(item);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(MotelyItem a, MotelyItem b)
    {
        return a.Equals(b);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(MotelyItem a, MotelyItem b)
    {
        return !a.Equals(b);
    }

    public override readonly int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public static implicit operator MotelyItem(MotelyItemType type)
    {
        return new(type);
    }
}
