using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Motely.Tests;

/// <summary>
/// R3 parity for the vector primitives. Every assertion recomputes the expected value with plain
/// scalar C#, so the test states the contract instead of echoing the intrinsic.
///
/// Architecture note: <see cref="MotelyVectorUtils.ShiftLeft(in Vector256{int}, in Vector256{int})"/>
/// and friends pick their branch from runtime hardware support. On x86 that is the Avx512F/Avx2
/// path; on Apple Silicon / ARM64 the very same test drives the AdvSimd path. The asserts hold
/// either way, so running this file on an ARM host covers the NEON branches with no source change.
/// </summary>
public sealed class VectorPrimitiveCoverageTests
{
    // ── MotelyVectorUtils: shifts ──

    [Fact]
    public void ShiftLeft_Int32_MatchesScalarPerLane()
    {
        var value = Vector256.Create(1, 2, 3, 4, 5, 6, 7, 8);
        var shift = Vector256.Create(0, 1, 2, 3, 4, 5, 6, 7);

        var actual = MotelyVectorUtils.ShiftLeft(value, shift);

        for (int lane = 0; lane < Vector256<int>.Count; lane++)
            Assert.Equal(value[lane] << shift[lane], actual[lane]);
    }

    [Fact]
    public void ShiftLeft_Int64_MatchesScalarPerLane()
    {
        var value = Vector512.Create(1L, 3L, 5L, 7L, 11L, 13L, 17L, 19L);
        var shift = Vector512.Create(0L, 1L, 2L, 3L, 4L, 5L, 6L, 7L);

        var actual = MotelyVectorUtils.ShiftLeft(value, shift);

        for (int lane = 0; lane < Vector512<long>.Count; lane++)
            Assert.Equal(value[lane] << (int)shift[lane], actual[lane]);
    }

    [Fact]
    public void ConvertToVector256Int32_TruncatesTowardZero()
    {
        var doubles = Vector512.Create(0.0, 1.9, -1.9, 42.5, -7.25, 100.999, -0.5, 8.0);

        var actual = MotelyVectorUtils.ConvertToVector256Int32(doubles);

        for (int lane = 0; lane < Vector256<int>.Count; lane++)
            Assert.Equal((int)doubles[lane], actual[lane]);
    }

    // ── MotelyVectorUtils: widen / narrow mask conversions ──

    [Fact]
    public void ExtendIntMaskToLong_WidensEachLaneSignExtended()
    {
        var small = Vector256.Create(0, -1, 0, -1, -1, 0, -1, 0);

        var wide = MotelyVectorUtils.ExtendIntMaskToLong(small);

        for (int lane = 0; lane < Vector512<long>.Count; lane++)
            Assert.Equal((long)small[lane], wide[lane]);
    }

    [Fact]
    public void ExtendMaskHelpers_AgreeOnBitPattern()
    {
        var small = Vector256.Create(-1, 0, -1, 0, 0, -1, 0, -1);

        var asLong = MotelyVectorUtils.ExtendIntMaskToLong(small);
        var asDouble = MotelyVectorUtils.ExtendIntMaskToDouble(small);
        var viaFloatOverload = MotelyVectorUtils.ExtendFloatMaskToLong(small);
        var fromFloatLanes = MotelyVectorUtils.ExtendFloatMaskToDouble(small.AsSingle());

        Assert.Equal(asLong, asDouble.AsInt64());
        Assert.Equal(asLong, viaFloatOverload);
        Assert.Equal(asLong, fromFloatLanes.AsInt64());
    }

    [Fact]
    public void ShrinkMaskHelpers_RoundTripExtend()
    {
        var small = Vector256.Create(-1, 0, 0, -1, -1, -1, 0, 0);
        var wide = MotelyVectorUtils.ExtendIntMaskToLong(small);

        Assert.Equal(small, MotelyVectorUtils.ShrinkLongMaskToInt(wide));
        Assert.Equal(small, MotelyVectorUtils.ShrinkDoubleMaskToInt(wide.AsDouble()));
        Assert.Equal(small, MotelyVectorUtils.ShrinkLongMaskToFloat(wide).AsInt32());
        Assert.Equal(small, MotelyVectorUtils.ShrinkDoubleMaskToFloat(wide.AsDouble()).AsInt32());
    }

    /// <summary>The size guards are the contract: 32-bit in, 64-bit out, and nothing else.</summary>
    [Fact]
    public void Extend32MaskTo64_RejectsWrongLaneWidths()
    {
        Assert.Throws<InvalidOperationException>(
            () => MotelyVectorUtils.Extend32MaskTo64<byte, long>(Vector256<byte>.Zero)
        );
        Assert.Throws<InvalidOperationException>(
            () => MotelyVectorUtils.Extend32MaskTo64<int, int>(Vector256<int>.Zero)
        );
    }

    [Fact]
    public void Shrink64MaskTo32_RejectsWrongLaneWidths()
    {
        Assert.Throws<InvalidOperationException>(
            () => MotelyVectorUtils.Shrink64MaskTo32<long, long>(Vector512<long>.Zero)
        );
        Assert.Throws<InvalidOperationException>(
            () => MotelyVectorUtils.Shrink64MaskTo32<int, int>(Vector512<int>.Zero)
        );
    }

    // ── MotelyVectorUtils: bitmask bridges ──

    [Fact]
    public void VectorMaskToIntMask_ExtractsSignBitPerLane()
    {
        var lanes = Vector256.Create(-1, 0, -1, 0, 0, 0, -1, -1);

        uint mask = MotelyVectorUtils.VectorMaskToIntMask(lanes);

        uint expected = 0;
        for (int lane = 0; lane < Vector256<int>.Count; lane++)
            if (lanes[lane] < 0)
                expected |= 1u << lane;
        Assert.Equal(expected, mask);
        Assert.Equal(expected, MotelyVectorUtils.VectorizedComparisonToMask(lanes));

        var wide = MotelyVectorUtils.ExtendIntMaskToLong(lanes);
        Assert.Equal(expected, MotelyVectorUtils.VectorMaskToIntMask(wide));
    }

    [Fact]
    public void VectorMaskToIntMask_RejectsWrongLaneWidths()
    {
        Assert.Throws<InvalidOperationException>(
            () => MotelyVectorUtils.VectorMaskToIntMask(Vector256<byte>.Zero)
        );
        Assert.Throws<InvalidOperationException>(
            () => MotelyVectorUtils.VectorMaskToIntMask(Vector512<int>.Zero)
        );
    }

    [Fact]
    public void VectorMaskToConditionalSelectMask_IsMinusOneOnSetLanes()
    {
        var mask = new VectorMask(0b1011_0010);

        var selector = MotelyVectorUtils.VectorMaskToConditionalSelectMask(mask);

        for (int lane = 0; lane < Vector256<int>.Count; lane++)
            Assert.Equal(mask[lane] ? -1 : 0, selector[lane]);
    }

    [Fact]
    public void VectorMaskToConditionalSelectMask_RoundTripsThroughVectorMask()
    {
        for (uint raw = 0; raw <= 0xFF; raw++)
        {
            var selector = MotelyVectorUtils.VectorMaskToConditionalSelectMask(new VectorMask(raw));
            Assert.Equal(raw, MotelyVectorUtils.VectorizedComparisonToMask(selector));
        }
    }

    [Fact]
    public void IsAccelerated_ReportsVector512Support() =>
        Assert.Equal(Vector512.IsHardwareAccelerated, MotelyVectorUtils.IsAccelerated);

    // ── VectorMask ──

    [Fact]
    public void VectorMask_IndexerSetsAndClearsIndividualLanes()
    {
        var mask = VectorMask.NoBitsSet;
        for (int lane = 0; lane < VectorMask.Length; lane++)
            Assert.False(mask[lane]);

        mask[3] = true;
        mask[7] = true;
        Assert.Equal(0b1000_1000u, mask.Value);
        Assert.True(mask[3] && mask[7]);
        Assert.True(mask.IsPartiallyTrue());
        Assert.False(mask.IsAllTrue());
        Assert.False(mask.IsAllFalse());

        mask[3] = false;
        Assert.Equal(0b1000_0000u, mask.Value);

        // Clearing a lane that is already clear is a no-op, not a toggle.
        mask[3] = false;
        Assert.Equal(0b1000_0000u, mask.Value);
    }

    [Fact]
    public void VectorMask_OperatorsFollowBooleanAlgebra()
    {
        var a = new VectorMask(0b1100_1010);
        var b = new VectorMask(0b1010_0110);

        Assert.Equal(0b1000_0010u, (a & b).Value);
        Assert.Equal(0b1110_1110u, (a | b).Value);
        Assert.Equal(0b0110_1100u, (a ^ b).Value);

        // Complement stays inside the 8 lanes — the high 24 bits are masked off.
        Assert.Equal(0b0011_0101u, (~a).Value);
        Assert.Equal(VectorMask.AllBitsSet.Value, (~VectorMask.NoBitsSet).Value);
        Assert.True((a & ~a).IsAllFalse());
        Assert.True((a | ~a).IsAllTrue());
    }

    [Fact]
    public void VectorMask_ToStringIsLaneZeroFirst()
    {
        Assert.Equal("00000000", VectorMask.NoBitsSet.ToString());
        Assert.Equal("11111111", VectorMask.AllBitsSet.ToString());
        // Lane 0 prints first, so 0b0000_0011 reads "11000000".
        Assert.Equal("11000000", new VectorMask(0b0000_0011).ToString());
    }

    [Fact]
    public void VectorMask_ImplicitConversionsAgreeAcrossLaneWidths()
    {
        var lanes = Vector256.Create(-1, 0, -1, -1, 0, 0, 0, -1);
        uint expected = 0b1000_1101;

        VectorMask fromInt = lanes;
        VectorMask fromUint = lanes.AsUInt32();
        VectorMask fromFloat = lanes.AsSingle();
        var wide = MotelyVectorUtils.ExtendIntMaskToLong(lanes);
        VectorMask fromLong = wide;
        VectorMask fromULong = wide.AsUInt64();
        VectorMask fromDouble = wide.AsDouble();

        Assert.Equal(expected, fromInt.Value);
        Assert.Equal(expected, fromUint.Value);
        Assert.Equal(expected, fromFloat.Value);
        Assert.Equal(expected, fromLong.Value);
        Assert.Equal(expected, fromULong.Value);
        Assert.Equal(expected, fromDouble.Value);
    }

    // ── VectorEnum256 ──

    [Fact]
    public void VectorEnum256_BroadcastAndCompare()
    {
        var all = VectorEnum256.Create(MotelyVoucher.Telescope);

        for (int lane = 0; lane < Vector256<int>.Count; lane++)
            Assert.Equal(MotelyVoucher.Telescope, all[lane]);

        Assert.True(((VectorMask)VectorEnum256.Equals(all, MotelyVoucher.Telescope)).IsAllTrue());
        Assert.True(((VectorMask)VectorEnum256.Equals(all, MotelyVoucher.Overstock)).IsAllFalse());
        Assert.True(((VectorMask)VectorEnum256.Equals(all, all)).IsAllTrue());
    }

    [Fact]
    public void VectorEnum256_GatherSelectsPerLaneValues()
    {
        MotelyVoucher[] table =
        [
            MotelyVoucher.Overstock,
            MotelyVoucher.Grabber,
            MotelyVoucher.Telescope,
            MotelyVoucher.Wasteful,
        ];
        var indices = Vector256.Create(0, 1, 2, 3, 3, 2, 1, 0);

        var gathered = VectorEnum256.Create(indices, table);

        for (int lane = 0; lane < Vector256<int>.Count; lane++)
            Assert.Equal(table[indices[lane]], gathered[lane]);

        var mixed = VectorEnum256.Create(indices, table);
        Assert.True(((VectorMask)VectorEnum256.Equals(gathered, mixed)).IsAllTrue());
        Assert.Contains(nameof(MotelyVoucher.Overstock), gathered.ToString());
    }

    // ── MotelyVectorRunState ──

    [Fact]
    public void RunState_ActivateVoucher_SetsEveryLane()
    {
        MotelyVectorRunState state = default;
        Assert.True(((VectorMask)state.IsVoucherActive(MotelyVoucher.Telescope)).IsAllFalse());

        state.ActivateVoucher(MotelyVoucher.Telescope);

        Assert.True(((VectorMask)state.IsVoucherActive(MotelyVoucher.Telescope)).IsAllTrue());
        // Activating one voucher leaves the others alone.
        Assert.True(((VectorMask)state.IsVoucherActive(MotelyVoucher.Overstock)).IsAllFalse());
    }

    [Fact]
    public void RunState_ActivateVoucherForMask_OnlyTouchesSelectedLanes()
    {
        MotelyVectorRunState state = default;
        var mask = new VectorMask(0b0101_0101);

        state.ActivateVoucherForMask(MotelyVoucher.Grabber, mask);

        var active = (VectorMask)state.IsVoucherActive(MotelyVoucher.Grabber);
        Assert.Equal(mask.Value, active.Value);
    }

    [Fact]
    public void RunState_ActivateVoucherVector_SetsPerLaneVoucher()
    {
        MotelyVoucher[] table = [MotelyVoucher.Overstock, MotelyVoucher.Grabber];
        var perLane = VectorEnum256.Create(Vector256.Create(0, 1, 0, 1, 0, 1, 0, 1), table);

        MotelyVectorRunState state = default;
        state.ActivateVoucher(perLane);

        Assert.Equal(0b0101_0101u, ((VectorMask)state.IsVoucherActive(MotelyVoucher.Overstock)).Value);
        Assert.Equal(0b1010_1010u, ((VectorMask)state.IsVoucherActive(MotelyVoucher.Grabber)).Value);
        Assert.True(((VectorMask)state.IsVoucherActive(perLane)).IsAllTrue());
    }

    [Fact]
    public void RunState_ActivateVoucherVectorWithMask_IntersectsLanes()
    {
        MotelyVoucher[] table = [MotelyVoucher.Overstock];
        var perLane = VectorEnum256.Create(Vector256<int>.Zero, table);

        MotelyVectorRunState state = default;
        state.ActivateVoucher(perLane, new VectorMask(0b0000_1111));

        Assert.Equal(
            0b0000_1111u,
            ((VectorMask)state.IsVoucherActive(MotelyVoucher.Overstock)).Value
        );
    }

    [Fact]
    public void RunState_ActivateShowman_SetsEveryLane()
    {
        MotelyVectorRunState state = default;
        Assert.True(((VectorMask)state.ShowmanActive).IsAllFalse());

        state.ActivateShowman();

        for (int lane = 0; lane < Vector256<int>.Count; lane++)
            Assert.Equal(1, state.ShowmanActive[lane]);
    }

    // ── Hardware inventory ──

    /// <summary>
    /// Records which SIMD branch this host drives, so a coverage report read on another
    /// architecture is interpretable rather than mysterious.
    /// </summary>
    [Fact]
    public void HostDrivesExactlyOneShiftBranch()
    {
        bool x86 = Avx2.IsSupported;
        bool arm = AdvSimd.IsSupported;
        Assert.False(x86 && arm);
        Assert.True(x86 || arm || !Vector256.IsHardwareAccelerated);
    }
}
