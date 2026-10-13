using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Motely;

public static unsafe class MotelyVectorUtils
{
    public static bool IsAccelerated => Vector512.IsHardwareAccelerated;

    // The Avx512F.IsSupported branches follow the ISA, not DOTNET_PreferredVectorBitWidth: on
    // .NET 10 that knob clears Vector512.IsHardwareAccelerated but leaves Avx512F.IsSupported
    // true, and the JIT still emits zmm code for Vector512<T>. A 256-bit A/B arm is
    // DOTNET_EnableAVX512=0 (or an x86-64-v3 NativeAOT build), not PreferredVectorBitWidth=256.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<int> ConvertToVector256Int32(in Vector512<double> vector)
    {
        if (Avx512F.IsSupported)
            return Avx512F.ConvertToVector256Int32WithTruncation(vector);

        // x86-64-v3: two VCVTTPD2DQ, not a per-lane software conversion through int64.
        if (Avx.IsSupported)
            return Vector256.Create(
                Avx.ConvertToVector128Int32WithTruncation(vector.GetLower()),
                Avx.ConvertToVector128Int32WithTruncation(vector.GetUpper())
            );

        Vector512<long> integerVector = Vector512.ConvertToInt64(vector);
        return Vector256.Narrow(integerVector.GetLower(), integerVector.GetUpper());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<int> ShiftLeft(in Vector256<int> value, in Vector256<int> shiftCount)
    {
        if (Avx2.IsSupported)
            return Avx2.ShiftLeftLogicalVariable(value, shiftCount.AsUInt32());

        if (AdvSimd.IsSupported)
            return Vector256.Create(
                AdvSimd.ShiftLogical(value.GetLower(), shiftCount.GetLower()),
                AdvSimd.ShiftLogical(value.GetUpper(), shiftCount.GetUpper())
            );

        int* lanes = stackalloc int[Vector256<int>.Count];
        for (int i = 0; i < Vector256<int>.Count; i++)
            lanes[i] = value[i] << shiftCount[i];
        return *(Vector256<int>*)lanes;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<long> ShiftLeft(in Vector512<long> value, in Vector512<long> shiftCount)
    {
        if (Avx512F.IsSupported)
            return Avx512F.ShiftLeftLogicalVariable(value, shiftCount.AsUInt64());

        if (Avx2.IsSupported)
            return Vector512.Create(
                Avx2.ShiftLeftLogicalVariable(value.GetLower(), shiftCount.GetLower().AsUInt64()),
                Avx2.ShiftLeftLogicalVariable(value.GetUpper(), shiftCount.GetUpper().AsUInt64())
            );

        if (AdvSimd.IsSupported)
            return Vector512.Create(
                Vector256.Create(
                    AdvSimd.ShiftLogical(value.GetLower().GetLower(), shiftCount.GetLower().GetLower()),
                    AdvSimd.ShiftLogical(value.GetLower().GetUpper(), shiftCount.GetLower().GetUpper())
                ),
                Vector256.Create(
                    AdvSimd.ShiftLogical(value.GetUpper().GetLower(), shiftCount.GetUpper().GetLower()),
                    AdvSimd.ShiftLogical(value.GetUpper().GetUpper(), shiftCount.GetUpper().GetUpper())
                )
            );

        long* lanes = stackalloc long[Vector512<long>.Count];
        for (int i = 0; i < Vector512<long>.Count; i++)
            lanes[i] = value[i] << (int)shiftCount[i];
        return *(Vector512<long>*)lanes;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<double> ExtendFloatMaskToDouble(in Vector256<float> smallMask) =>
        Extend32MaskTo64<float, double>(smallMask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<long> ExtendIntMaskToLong(in Vector256<int> smallMask) =>
        Extend32MaskTo64<int, long>(smallMask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<long> ExtendFloatMaskToLong(in Vector256<int> smallMask) =>
        Extend32MaskTo64<int, long>(smallMask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<double> ExtendIntMaskToDouble(in Vector256<int> smallMask) =>
        Extend32MaskTo64<int, double>(smallMask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<TTo> Extend32MaskTo64<TFrom, TTo>(in Vector256<TFrom> smallMask)
        where TFrom : unmanaged
        where TTo : unmanaged
    {
        if (sizeof(TFrom) != 4 || sizeof(TTo) != 8)
            throw new InvalidOperationException("Extend32MaskTo64 widens 32-bit lanes to 64-bit lanes.");

        (Vector256<long> low, Vector256<long> high) = Vector256.Widen(smallMask.AsInt32());
        return Vector512.Create(low, high).As<long, TTo>();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> ShrinkDoubleMaskToFloat(in Vector512<double> smallMask) =>
        Shrink64MaskTo32<double, float>(smallMask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> ShrinkLongMaskToFloat(in Vector512<long> smallMask) =>
        Shrink64MaskTo32<long, float>(smallMask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<int> ShrinkDoubleMaskToInt(in Vector512<double> smallMask) =>
        Shrink64MaskTo32<double, int>(smallMask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<int> ShrinkLongMaskToInt(in Vector512<long> smallMask) =>
        Shrink64MaskTo32<long, int>(smallMask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<TTo> Shrink64MaskTo32<TFrom, TTo>(in Vector512<TFrom> smallMask)
        where TFrom : unmanaged
        where TTo : unmanaged
    {
        if (sizeof(TFrom) != 8 || sizeof(TTo) != 4)
            throw new InvalidOperationException("Shrink64MaskTo32 narrows 64-bit lanes to 32-bit lanes.");

        return Vector256
            .Narrow(smallMask.GetLower().AsUInt64(), smallMask.GetUpper().AsUInt64())
            .As<uint, TTo>();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint VectorMaskToIntMask<T>(in Vector256<T> vector)
        where T : unmanaged
    {
        if (sizeof(T) != 4)
            throw new InvalidOperationException("VectorMaskToIntMask takes 32-bit lanes.");
        return Vector256.ExtractMostSignificantBits(vector);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint VectorMaskToIntMask<T>(in Vector512<T> vector)
        where T : unmanaged
    {
        if (sizeof(T) != 8)
            throw new InvalidOperationException("VectorMaskToIntMask takes 64-bit lanes.");
        return (uint)Vector512.ExtractMostSignificantBits(vector);
    }

    /// <summary>
    /// A <see cref="VectorMask"/> as a ConditionalSelect mask: each set bit becomes an all-ones
    /// int lane, each clear bit a zero lane.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<int> VectorMaskToConditionalSelectMask(VectorMask mask)
    {
        var laneIndices = Vector256.Create(0u, 1u, 2u, 3u, 4u, 5u, 6u, 7u);
        var maskBits = Vector256.Create(mask.Value);

        Vector256<uint> shifted;
        if (Avx2.IsSupported)
            shifted = Avx2.ShiftRightLogicalVariable(maskBits, laneIndices);
        else
            shifted = Vector256.Create(
                mask.Value >> 0,
                mask.Value >> 1,
                mask.Value >> 2,
                mask.Value >> 3,
                mask.Value >> 4,
                mask.Value >> 5,
                mask.Value >> 6,
                mask.Value >> 7
            );

        // Bit 0 of each lane (0 or 1), negated to 0 or all ones.
        var bit = Vector256.BitwiseAnd(shifted, Vector256.Create(1u));
        return Vector256.Subtract(Vector256<uint>.Zero, bit).AsInt32();
    }

    /// <summary>A lane-wise comparison result (all ones or zero per lane) as a bitmask.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint VectorizedComparisonToMask(Vector256<int> comparison) =>
        Vector256.ExtractMostSignificantBits(comparison);
}
