using System;
using System.Diagnostics;
using Motely;

namespace Motely.Filters.Jaml;

internal static class LegendarySoulEditionPrefilter
{
    internal static int GetEditionSoulRollCount(LegendaryJokerClause clause)
    {
        int maxSlot = (clause.Sources ?? LegendaryJokerFilterDesc.DefaultSources).MaxReferencedBoosterSlot();
        int n = maxSlot >= 0 ? maxSlot + 1 : 6;
        if (n <= 0)
            n = 6;
        if (clause.SoulEditionRolls > 0)
            n = Math.Max(n, clause.SoulEditionRolls);
        return n;
    }

    internal static VectorMask Apply(
        ref MotelyVectorSearchContext ctx,
        LegendaryJokerClause clause,
        uint laneMask
    )
    {
        Debug.Assert(clause.Edition.HasValue);

        VectorMask candidateMask = new VectorMask(laneMask);
        var targetEdition = clause.Edition.Value;
        VectorMask editionOk = VectorMask.NoBitsSet;
        int maxSouls = GetEditionSoulRollCount(clause);

        foreach (var ante in clause.Antes)
        {
            var soulStream = ctx.CreateLegendaryJokerStream(
                ante,
                MotelyJokerFixedRarityStreamFlags.ExcludeJokerType
                    | MotelyJokerFixedRarityStreamFlags.ExcludeStickers,
                isCached: true
            );
            VectorMask anteAny = VectorMask.NoBitsSet;
            for (int s = 0; s < maxSouls; s++)
            {
                var editionVec = ctx.GetNextJoker(ref soulStream).Edition;
                anteAny |= VectorEnum256.Equals(editionVec, targetEdition);
            }

            editionOk |= anteAny;
        }

        candidateMask &= editionOk;
        return candidateMask;
    }
}
