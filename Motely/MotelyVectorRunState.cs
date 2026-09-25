using System.Diagnostics;
using System.Runtime.Intrinsics;

namespace Motely;

public ref struct MotelyVectorRunState
{
    static MotelyVectorRunState()
    {
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
        var voucherBit = Vector256.Create(1 << (int)voucher);

        var maskVector = MotelyVectorUtils.VectorMaskToConditionalSelectMask(mask);

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
