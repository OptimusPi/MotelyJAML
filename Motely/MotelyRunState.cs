namespace Motely;

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

    public int VoucherBitfield { get; private set; }
    public int BossBitfield { get; private set; }
    public int ExtendedPackAnteBitfield { get; private set; }

    public MotelyLuck Luck { get; set; } = MotelyLuck.X1;

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
        BossBitfield &= NormalBossBlindMask;
    }

    public void ResetNormalBosses()
    {
        BossBitfield &= FinisherBossBlindMask;
    }

    public void ActivateShowman()
    {
        ShowmanActive = true;
    }
}
