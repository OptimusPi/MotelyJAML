using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Motely;

public struct MotelyWeightedPoolItem<T>(T value, double weight)
    where T : unmanaged, Enum
{
    public T Value = value;
    public double Weight = weight;
}

public unsafe class MotelyWeightedPool<T> : IDisposable
    where T : unmanaged, Enum
{
    private readonly MotelyWeightedPoolItem<T>* _pool;
    public readonly int Count;
    public readonly double WeightSum;

    /// <summary>
    /// The items with the weights they were declared with. The native table below inflates the
    /// last item's weight as a guard; anything that wants to reason about the odds — the rarity
    /// model — must read these, not that.
    /// </summary>
    public readonly MotelyWeightedPoolItem<T>[] Items;

    public MotelyWeightedPool(MotelyWeightedPoolItem<T>[] items)
    {
        Count = items.Length;

        if (Count == 0)
            throw new ArgumentException("Weighted pool must have at least one item.");

        Items = (MotelyWeightedPoolItem<T>[])items.Clone();

        _pool = (MotelyWeightedPoolItem<T>*)
            Marshal.AllocHGlobal(sizeof(MotelyWeightedPoolItem<T>) * Count);

        double sum = 0;

        for (int i = 0; i < Count; i++)
        {
            _pool[i] = items[i];
            sum += _pool[i].Weight;
        }

        WeightSum = sum;

        // We increase the weight of the last item to make 100% double triple sure something gets picked
        //  before we hit the end of the array.
        _pool[Count - 1].Weight += WeightSum;
    }

    /// <summary>The share of draws that land on <paramref name="value"/>; zero when it is not in the pool.</summary>
    public double Probability(T value)
    {
        double weight = 0;
        foreach (var item in Items)
            if (EqualityComparer<T>.Default.Equals(item.Value, value))
                weight += item.Weight;
        return weight / WeightSum;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Choose(double poll)
    {
        // get_pack common_events.lua
        poll *= WeightSum;

        double weight = 0;
        MotelyWeightedPoolItem<T>* current = _pool;

        for (; ; )
        {
            weight += current->Weight;

            if (weight >= poll)
            {
                return current->Value;
            }

            current += 1;

#if DEBUG
            if (current >= _pool + Count)
                throw new IndexOutOfRangeException();
#endif
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VectorEnum256<T> Choose(Vector512<double> poll)
    {
        poll *= WeightSum;

        double weight = 0;
        MotelyWeightedPoolItem<T>* current = _pool;
        Vector256<int> finishedMask = Vector256<int>.Zero;
        Vector256<int> values = default;

        for (; ; )
        {
            weight += current->Weight;

            Vector512<double> weightVec = Vector512.Create(weight);
            Vector256<int> chosenMask = MotelyVectorUtils.ShrinkDoubleMaskToInt(
                Vector512.GreaterThanOrEqual(weightVec, poll)
            );

            chosenMask &= ~finishedMask;
            values = Vector256.ConditionalSelect(
                chosenMask,
                Vector256.Create(*(int*)(&current->Value)),
                values
            );
            finishedMask |= chosenMask;

            if (Vector256.ExtractMostSignificantBits(finishedMask) == 0xFF)
                return new(values);

            current += 1;

#if DEBUG
            if (current >= _pool + Count)
                throw new IndexOutOfRangeException();
#endif
        }
    }

#pragma warning disable CA1816 // Dispose methods should call SuppressFinalize
    public void Dispose()
#pragma warning restore CA1816 // Dispose methods should call SuppressFinalize
    {
        Marshal.FreeHGlobal((nint)_pool);
    }
}

public static partial class MotelyWeightedPools
{
    public static readonly MotelyWeightedPool<MotelyBoosterPack> BoosterPacks = new([
        new(MotelyBoosterPack.Arcana, 4),
        new(MotelyBoosterPack.JumboArcana, 2),
        new(MotelyBoosterPack.MegaArcana, 0.5),
        new(MotelyBoosterPack.Celestial, 4),
        new(MotelyBoosterPack.JumboCelestial, 2),
        new(MotelyBoosterPack.MegaCelestial, 0.5),
        new(MotelyBoosterPack.Standard, 4),
        new(MotelyBoosterPack.JumboStandard, 2),
        new(MotelyBoosterPack.MegaStandard, 0.5),
        new(MotelyBoosterPack.Buffoon, 1.2),
        new(MotelyBoosterPack.JumboBuffoon, 0.6),
        new(MotelyBoosterPack.MegaBuffoon, 0.15),
        new(MotelyBoosterPack.Spectral, 0.6),
        new(MotelyBoosterPack.JumboSpectral, 0.3),
        new(MotelyBoosterPack.MegaSpectral, 0.07),
    ]);
}
