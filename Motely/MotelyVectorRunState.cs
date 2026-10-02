using System.Diagnostics;
using System.Runtime.Intrinsics;

namespace Motely;

public ref struct MotelyVectorRunState
{
    static MotelyVectorRunState()
    {
        // Check that we can fit all the voucher state in an int
        if (MotelyEnum<MotelyVoucher>.ValueCount > 32)
            throw new UnreachableException();
    }

    public Vector256<int> VoucherStateBitfield;
    public Vector256<int> ShowmanActive;

    public void ActivateShowman()
    {
        ShowmanActive |= Vector256.Create(1);
    }

    public void ActivateVoucher(MotelyVoucher voucher)
    {
        VoucherStateBitfield |= Vector256.Create(1 << (int)voucher);
    }

    public void ActivateVoucherForMask(MotelyVoucher voucher, VectorMask mask)
    {
        // Only activate voucher for lanes where mask is true
        // Create a vector with the voucher bit in each lane
        var voucherBit = Vector256.Create(1 << (int)voucher);

        // Create mask vector: -1 (all bits set) for true lanes, 0 for false lanes
        var maskVector = MotelyVectorUtils.VectorMaskToConditionalSelectMask(mask);

        // AND the voucher bit with the mask to only set it for true lanes
        VoucherStateBitfield |= Vector256.BitwiseAnd(voucherBit, maskVector);
    }

    public void ActivateVoucher(VectorEnum256<MotelyVoucher> voucherVector)
    {
        VoucherStateBitfield |= MotelyVectorUtils.ShiftLeft(
            Vector256<int>.One,
            voucherVector.HardwareVector
        );
    }

    public void ActivateVoucher(VectorEnum256<MotelyVoucher> voucherVector, VectorMask mask)
    {
        var voucherBits = MotelyVectorUtils.ShiftLeft(
            Vector256<int>.One,
            voucherVector.HardwareVector
        );
        var maskVector = MotelyVectorUtils.VectorMaskToConditionalSelectMask(mask);
        VoucherStateBitfield |= Vector256.BitwiseAnd(voucherBits, maskVector);
    }

    /// <summary>
    /// Lanes (all-ones) where every voucher is redeemed and the pool falls back to Blank — see
    /// <see cref="MotelyRunState.AreAllVouchersActive"/>.
    /// </summary>
    public readonly Vector256<int> AreAllVouchersActive()
    {
        var all = Vector256.Create(MotelyRunState.AllVouchersBitfield);
        return Vector256.Equals(VoucherStateBitfield & all, all);
    }

    public readonly Vector256<int> IsVoucherActive(MotelyVoucher voucher)
    {
        return Vector256.OnesComplement(
            Vector256.IsZero(VoucherStateBitfield & Vector256.Create(1 << (int)voucher))
        );
    }

    public readonly Vector256<int> IsVoucherActive(VectorEnum256<MotelyVoucher> voucherVector)
    {
        return Vector256.OnesComplement(
            Vector256.IsZero(
                VoucherStateBitfield
                    & MotelyVectorUtils.ShiftLeft(Vector256<int>.One, voucherVector.HardwareVector)
            )
        );
    }
}
