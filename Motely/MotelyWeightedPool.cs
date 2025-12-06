
using System;
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

    public MotelyWeightedPool(MotelyWeightedPoolItem<T>[] items)
    {
        Count = items.Length;

        if (Count == 0)
            throw new ArgumentException("Weighted pool must have at least one item.");

        _pool = (MotelyWeightedPoolItem<T>*)Marshal.AllocHGlobal(sizeof(MotelyWeightedPoolItem<T>) * Count);

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

#if !DEBUG
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
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

    // AUDIT ISSUE #3 & #5: Always inline + optimize, fix early exit check
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
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

            // AUDIT ISSUE #4: Reduce repeated Vector512.Create calls by reusing variable
            Vector512<double> weightVec = Vector512.Create(weight);
            Vector256<int> chosenMask = MotelyVectorUtils.ShrinkDoubleMaskToInt(
                Vector512.GreaterThanOrEqual(weightVec, poll)
            );

            chosenMask &= ~finishedMask;
            values = Vector256.ConditionalSelect(chosenMask, Vector256.Create(*(int*)(&current->Value)), values);
            finishedMask |= chosenMask;

            // AUDIT ISSUE #5: More efficient early exit - check if all lanes finished
            if (Vector256.ExtractMostSignificantBits(finishedMask) == 0xFF)
                return new(values);

            current += 1;

#if DEBUG
            if (current >= _pool + Count)
                throw new IndexOutOfRangeException();
#endif
        }
    }

    public void Dispose()
    {
        Marshal.FreeHGlobal((nint)_pool);
    }
}

public static partial class MotelyWeightedPools { }