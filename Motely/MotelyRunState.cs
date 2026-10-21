namespace Motely;

// A plain reference type, not a ref struct — it doesn't need to be. The actual hot state in this
// engine's SIMD sweep is the PRNG stream's own double, copied by value through the vector path;
// this is small bookkeeping (two bitfields, a luck multiplier, a cached boss array) mutated
// through a shared reference, same as everywhere else in C#. One type, used identically inside
// the engine and across the WASM boundary — no separate marshalable twin, no conversion functions.
public sealed record MotelyRunState
{
    private static readonly int FinisherBossBlindMask;
    private static readonly int NormalBossBlindMask;

    static MotelyRunState()
    {
        FinisherBossBlindMask = 0;
        NormalBossBlindMask = 0;
        foreach (MotelyBossBlind bossBlind in MotelyEnum<MotelyBossBlind>.Values)
        {
            if (bossBlind.GetBossType() == MotelyBossBlindType.Finisher)
            {
                FinisherBossBlindMask |= 1 << bossBlind.GetBossIndex();
            }
            else
            {
                NormalBossBlindMask |= 1 << bossBlind.GetBossIndex();
            }
        }
    }

    // All settable only through the real methods below (ActivateVoucher, SeeBoss, etc.) — a raw
    // public setter let a caller bypass their guards (e.g. ActivateExtendedPackAnte's `ante > 0`
    // check), which a bare `{ get; set; }` field never enforced.
    public int VoucherBitfield { get; private set; }
    public int BossBitfield { get; private set; }
    public int ExtendedPackAnteBitfield { get; private set; }

    // MotelyLuck, not int — the enum exists specifically so an invalid multiplier (a stray "3")
    // can't be represented at all, the same reason MotelyWith.Luck is typed this way.
    public MotelyLuck Luck { get; set; } = MotelyLuck.X1;

    // Boss caching for scoring - generated once per seed to maintain state
    public MotelyBossBlind[]? CachedBosses { get; set; }

    public bool ShowmanActive { get; private set; }

    public void ActivateVoucher(MotelyVoucher voucher)
    {
        VoucherBitfield |= 1 << (int)voucher;
    }

    public bool IsVoucherActive(MotelyVoucher voucher)
    {
        return (VoucherBitfield & (1 << (int)voucher)) != 0;
    }

    /// <summary>Bit set of every voucher; the voucher pool is empty once all of them are active.</summary>
    internal static readonly int AllVouchersBitfield = (int)((1L << MotelyEnum<MotelyVoucher>.ValueCount) - 1);

    /// <summary>
    /// Every voucher is redeemed, so no draw can find an open one. Balatro's
    /// <c>get_current_pool</c> then falls back to Blank (<c>v_blank</c>); the draw loops use this
    /// to return it instead of resampling forever (antes run to 39, the 32 vouchers are gone by ~33).
    /// </summary>
    public bool AreAllVouchersActive => (VoucherBitfield & AllVouchersBitfield) == AllVouchersBitfield;

    public void ActivateExtendedPackAnte(int ante)
    {
        if (ante > 0)
            ExtendedPackAnteBitfield |= 1 << ante;
    }

    public bool IsExtendedPackAnteActive(int ante)
    {
        return ante > 0 && (ExtendedPackAnteBitfield & (1 << ante)) != 0;
    }

    public void SeeBoss(MotelyBossBlind boss)
    {
        BossBitfield |= 1 << boss.GetBossIndex();
    }

    public bool HasSeenBoss(MotelyBossBlind boss)
    {
        return (BossBitfield & (1 << boss.GetBossIndex())) != 0;
    }

    public void ResetFinisherBosses()
    {
        // Only allow normal boss bits to be set
        BossBitfield &= NormalBossBlindMask;
    }

    public void ResetNormalBosses()
    {
        // Only allow finisher boss bits to be set
        BossBitfield &= FinisherBossBlindMask;
    }

    public void ActivateShowman()
    {
        ShowmanActive = true;
    }
}
