using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using Motely.Filters;

namespace Motely.Filters;

/// <summary>
/// MAXIMUM PERFORMANCE: Combined ErraticRank + ErraticSuit filter.
/// Processes BOTH rank and suit clauses in a SINGLE 52-card loop for optimal vectorization.
/// Automatically used when filter has both ErraticRank and ErraticSuit clauses.
/// </summary>
public struct MotelyJsonErraticRankAndSuitFilterDesc(
    MotelyJsonErraticRankAndSuitFilterCriteria criteria
)
    : IMotelySeedFilterDesc<
        MotelyJsonErraticRankAndSuitFilterDesc.MotelyJsonErraticRankAndSuitFilter
    >
{
    private readonly MotelyJsonErraticRankAndSuitFilterCriteria _criteria = criteria;

    public MotelyJsonErraticRankAndSuitFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        ctx.CacheErraticDeckPrngStream();
        return new MotelyJsonErraticRankAndSuitFilter(
            _criteria.RankClauses,
            _criteria.SuitClauses
        );
    }

    public struct MotelyJsonErraticRankAndSuitFilter : IMotelySeedFilter
    {
        private readonly MotelyJsonErraticRankFilterClause[] _rankClauses;
        private readonly MotelyJsonErraticSuitFilterClause[] _suitClauses;

        public MotelyJsonErraticRankAndSuitFilter(
            List<MotelyJsonErraticRankFilterClause> rankClauses,
            List<MotelyJsonErraticSuitFilterClause> suitClauses
        )
        {
            _rankClauses = [.. rankClauses];
            _suitClauses = [.. suitClauses];
        }

        [MethodImpl(
            MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization
        )]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            int totalClauses = _rankClauses.Length + _suitClauses.Length;
            if (totalClauses == 0)
                return VectorMask.AllBitsSet;

            // Stack-allocated count vectors for ALL clauses (ranks + suits)
            Span<Vector256<int>> rankCounts = stackalloc Vector256<int>[_rankClauses.Length];
            Span<Vector256<int>> suitCounts = stackalloc Vector256<int>[_suitClauses.Length];
            for (int i = 0; i < rankCounts.Length; i++)
                rankCounts[i] = Vector256<int>.Zero;
            for (int i = 0; i < suitCounts.Length; i++)
                suitCounts[i] = Vector256<int>.Zero;

            // SINGLE LOOP through all 52 cards - process BOTH rank and suit clauses!
            var stream = ctx.CreateErraticDeckPrngStream(true);
            for (int cardIndex = 0; cardIndex < 52; cardIndex++)
            {
                var card = ctx.GetNextErraticDeckCard(ref stream);

                // Check all rank clauses
                for (int rankIdx = 0; rankIdx < _rankClauses.Length; rankIdx++)
                {
                    var clause = _rankClauses[rankIdx];
                    VectorMask rankMatch = VectorEnum256.Equals(
                        card.PlayingCardRank,
                        clause.Rank
                    );
                    rankCounts[rankIdx] += Vector256.ConditionalSelect(
                        MotelyVectorUtils.VectorMaskToConditionalSelectMask(rankMatch),
                        Vector256<int>.One,
                        Vector256<int>.Zero
                    );
                }

                // Check all suit clauses
                for (int suitIdx = 0; suitIdx < _suitClauses.Length; suitIdx++)
                {
                    var clause = _suitClauses[suitIdx];
                    VectorMask suitMatch = VectorEnum256.Equals(
                        card.PlayingCardSuit,
                        clause.Suit
                    );
                    suitCounts[suitIdx] += Vector256.ConditionalSelect(
                        MotelyVectorUtils.VectorMaskToConditionalSelectMask(suitMatch),
                        Vector256<int>.One,
                        Vector256<int>.Zero
                    );
                }
            }

            // Compare all counts against min thresholds
            VectorMask finalMask = VectorMask.AllBitsSet;

            for (int i = 0; i < _rankClauses.Length; i++)
            {
                VectorMask clauseMask = Vector256.GreaterThanOrEqual(
                    rankCounts[i],
                    Vector256.Create(_rankClauses[i].MinCount)
                );
                finalMask &= clauseMask;
            }

            for (int i = 0; i < _suitClauses.Length; i++)
            {
                VectorMask clauseMask = Vector256.GreaterThanOrEqual(
                    suitCounts[i],
                    Vector256.Create(_suitClauses[i].MinCount)
                );
                finalMask &= clauseMask;
            }

            return finalMask;
        }
    }
}

/// <summary>
/// Criteria for combined ErraticRank + ErraticSuit filter
/// </summary>
public class MotelyJsonErraticRankAndSuitFilterCriteria
{
    public List<MotelyJsonErraticRankFilterClause> RankClauses { get; set; } = new();
    public List<MotelyJsonErraticSuitFilterClause> SuitClauses { get; set; } = new();
}

/// <summary>
/// Extension methods for creating combined ErraticRankAndSuit filter criteria
/// </summary>
public static partial class MotelyJsonFilterClauseExtensions
{
    public static MotelyJsonErraticRankAndSuitFilterCriteria CreateErraticRankAndSuitCriteria(
        List<MotelyJsonConfig.MotleyJsonFilterClause> clauses
    )
    {
        var criteria = new MotelyJsonErraticRankAndSuitFilterCriteria();

        foreach (var clause in clauses)
        {
            // Add to rank clauses
            if (clause.ItemTypeEnum == MotelyFilterItemType.ErraticRank && clause.RankEnum != null)
            {
                criteria.RankClauses.Add(
                    new MotelyJsonErraticRankFilterClause
                    {
                        Rank = clause.RankEnum.Value,
                        MinCount = clause.Min ?? 1,
                    }
                );
            }

            // Add to suit clauses
            if (clause.ItemTypeEnum == MotelyFilterItemType.ErraticSuit && clause.SuitEnum != null)
            {
                criteria.SuitClauses.Add(
                    new MotelyJsonErraticSuitFilterClause
                    {
                        Suit = clause.SuitEnum.Value,
                        MinCount = clause.Min ?? 1,
                    }
                );
            }
        }

        return criteria;
    }
}
