namespace Motely.Filters.Jaml;

/// <summary>
/// The odds the ante-scoped families share but none of them owns: which booster pack a shop slot
/// rolls, how many cards it holds, which edition band a joker lands in, which stickers a stake
/// hands out. Each is the engine's own roll read forward — the weighted pool the pack stream
/// chooses from, the bands <c>GetNextEdition</c> compares against, the stake gates
/// <c>ApplyNextStickers</c> checks — so a family that needs one of these asks here rather than
/// restating a constant it does not own.
/// </summary>
internal static class JamlPoolRarity
{
    // ── editions ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The share of joker rolls that land in <paramref name="edition"/>, at edition rate 1 —
    /// the rate every stream the clauses read uses. Mirrors <c>GetNextEdition</c>'s ordered,
    /// disjoint bands: Negative above 0.997, Polychrome above 0.994, Holographic above 0.98, Foil
    /// above 0.96, None below. Note Polychrome is 0.003, not 0.006 — Negative eats the top of its
    /// band. Null means the clause does not care, which is a factor of one.
    /// </summary>
    public static double JokerEditionShare(MotelyItemEdition? edition) =>
        edition switch
        {
            null => 1.0,
            MotelyItemEdition.Negative => 0.003,
            MotelyItemEdition.Polychrome => 0.003,
            MotelyItemEdition.Holographic => 0.014,
            MotelyItemEdition.Foil => 0.02,
            MotelyItemEdition.None => 0.96,
            _ => 0.0,
        };

    // ── stickers ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The share of stickered rolls that satisfy every sticker the clause asks for, on a stream
    /// that applies stickers at all. Mirrors <c>ApplyNextStickers</c>: nothing below Black; one
    /// poll decides Eternal (above 0.7) or, from Orange, Perishable (0.4–0.7) — so asking for both
    /// is a modelled zero; Rental is its own poll from Gold. A joker that <c>CanBeEternal</c>
    /// rejects never gets Eternal however the poll falls.
    /// </summary>
    public static double StickerShare(
        MotelyJokerSticker[] stickers,
        MotelyStake stake,
        bool canBeEternal
    )
    {
        bool eternal = false, perishable = false, rental = false;
        foreach (var sticker in stickers)
        {
            switch (sticker)
            {
                case MotelyJokerSticker.Eternal: eternal = true; break;
                case MotelyJokerSticker.Perishable: perishable = true; break;
                case MotelyJokerSticker.Rental: rental = true; break;
            }
        }

        if (eternal && perishable)
            return 0.0;

        double share = 1.0;
        if (eternal)
            share *= stake >= MotelyStake.Black && canBeEternal ? 0.3 : 0.0;
        if (perishable)
            share *= stake >= MotelyStake.Orange ? 0.3 : 0.0;
        if (rental)
            share *= stake >= MotelyStake.Gold ? 0.3 : 0.0;
        return share;
    }

    /// <summary>True when the clause asks for any sticker a sticker-less stream can never supply.</summary>
    public static bool WantsAnySticker(MotelyJokerSticker[] stickers)
    {
        foreach (var sticker in stickers)
            if (sticker != MotelyJokerSticker.None)
                return true;
        return false;
    }

    // ── booster packs ──────────────────────────────────────────────────────────────────────────

    /// <summary>The share of weighted pack rolls that land on exactly <paramref name="pack"/>.</summary>
    public static double PackShare(MotelyBoosterPack pack) =>
        MotelyWeightedPools.BoosterPacks.Probability(pack);

    /// <summary>
    /// Whether a shop ever offers pack slot <paramref name="slot"/> in <paramref name="ante"/>:
    /// four packs in ante 1, six after. Ante 1 can reach slots 4–5 only under Hieroglyph or
    /// Petroglyph, which the model does not follow; those slots are left out, which understates
    /// a clause that asks for them at ante 1 by the small chance that voucher was awarded.
    /// </summary>
    public static bool SlotIsReachable(int ante, int slot) =>
        slot >= 0
        && slot <= (ante == 1 ? MotelyGlobals.EarlyAnteMaxPackSlot : MotelyGlobals.LateAntesMaxPackSlot);

    /// <summary>
    /// True for the one pack the engine hands out without rolling: a stream opened with
    /// <c>CreateBoosterPackStream(ante)</c> returns a plain Buffoon pack as ante 1's first offer
    /// before touching the PRNG. Every family that reads packs that way must treat slot 0 of
    /// ante 1 as a certainty, not a draw. (The legendary path opens its stream differently and
    /// rolls that slot — see <see cref="JamlJokerRarity.LegendaryDistribution"/>.)
    /// </summary>
    public static bool SlotIsFixedBuffoon(int ante, int slot) => ante == 1 && slot == 0;

    /// <summary>
    /// The count of matching cards one weighted pack slot yields when it is opened card by card:
    /// for each size of <paramref name="type"/>, the chance the slot rolled that pack times a
    /// binomial over its cards at <paramref name="perCard"/>. Any other pack kind contributes
    /// nothing, which is the mass <see cref="JamlCountDistribution.Mixture"/> leaves on zero.
    /// </summary>
    public static double[] PackSlotCards(
        MotelyBoosterPackType type,
        double perCard,
        bool requireMega
    )
    {
        List<(double, double[])> parts = [];
        foreach (var pack in MotelyEnum<MotelyBoosterPack>.Values)
        {
            if (pack.GetPackType() != type)
                continue;
            var size = pack.GetPackSize();
            if (requireMega && size != MotelyBoosterPackSize.Mega)
                continue;
            parts.Add(
                (PackShare(pack), JamlCountDistribution.Binomial(type.GetCardCount(size), perCard))
            );
        }
        return JamlCountDistribution.Mixture(parts);
    }

    /// <summary>
    /// The chance one weighted pack slot holds at least one card that fires an independent
    /// per-card roll at <paramref name="perCard"/> — The Soul in an arcana pack, Black Hole in a
    /// celestial one — summed over the sizes of <paramref name="type"/> the slot could roll.
    /// </summary>
    public static double PackSlotHasAny(
        MotelyBoosterPackType type,
        double perCard,
        bool requireMega
    )
    {
        double share = 0.0;
        foreach (var pack in MotelyEnum<MotelyBoosterPack>.Values)
        {
            if (pack.GetPackType() != type)
                continue;
            var size = pack.GetPackSize();
            if (requireMega && size != MotelyBoosterPackSize.Mega)
                continue;
            share += PackShare(pack) * (1.0 - Math.Pow(1.0 - perCard, type.GetCardCount(size)));
        }
        return share;
    }

    /// <summary>The share of a uniform pool of <paramref name="poolSize"/> that <paramref name="wanted"/> distinct members cover; a category-any clause covers it all.</summary>
    public static double PoolShare(int wanted, int poolSize, bool any) =>
        any ? 1.0 : poolSize <= 0 ? 0.0 : Math.Min(wanted, poolSize) / (double)poolSize;

    /// <summary>How many distinct values an index list names — the same dedup the roll families use.</summary>
    public static int Distinct(int[] indices) => JamlRollRarity.DistinctRolls(indices);

    /// <summary>How many distinct members an enum list names.</summary>
    public static int Distinct<T>(T[] values)
        where T : struct, Enum
    {
        if (values.Length <= 1)
            return values.Length;
        HashSet<T> seen = [];
        foreach (var value in values)
            seen.Add(value);
        return seen.Count;
    }

    /// <summary>Whether <paramref name="index"/> is named in <paramref name="indices"/>.</summary>
    public static bool Contains(int[] indices, int index)
    {
        foreach (int candidate in indices)
            if (candidate == index)
                return true;
        return false;
    }
}
