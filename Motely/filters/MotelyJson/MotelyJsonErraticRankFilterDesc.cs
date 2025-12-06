using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using Motely.Filters;

namespace Motely.Filters;

/// <summary>
/// Filters seeds based on Erratic Deck starting composition - RANK only.
/// Counts how many cards of specific rank(s) appear in the 52-card starting deck.
/// </summary>
public struct MotelyJsonErraticRankFilterDesc(MotelyJsonErraticRankFilterCriteria criteria)
    : IMotelySeedFilterDesc<MotelyJsonErraticRankFilterDesc.MotelyJsonErraticRankFilter>
{
    private readonly MotelyJsonErraticRankFilterCriteria _criteria = criteria;

    public MotelyJsonErraticRankFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        ctx.CacheErraticDeckPrngStream();
        return new MotelyJsonErraticRankFilter(_criteria.Clauses);
    }

    public struct MotelyJsonErraticRankFilter : IMotelySeedFilter
    {
        private readonly MotelyJsonErraticRankFilterClause[] _clauses;

        public MotelyJsonErraticRankFilter(List<MotelyJsonErraticRankFilterClause> clauses)
        {
            _clauses = [.. clauses];
        }

        [MethodImpl(
            MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization
        )]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            if (_clauses == null || _clauses.Length == 0)
                return VectorMask.AllBitsSet;

            // Stack-allocated clause masks
            Span<VectorMask> clauseMasks = stackalloc VectorMask[_clauses.Length];
            for (int i = 0; i < clauseMasks.Length; i++)
                clauseMasks[i] = VectorMask.NoBitsSet;

            // Stack-allocated count vectors for each clause
            Span<Vector256<int>> counts = stackalloc Vector256<int>[_clauses.Length];
            for (int i = 0; i < counts.Length; i++)
                counts[i] = Vector256<int>.Zero;

            // Single loop through all 52 cards
            var stream = ctx.CreateErraticDeckPrngStream(true);
            for (int cardIndex = 0; cardIndex < 52; cardIndex++)
            {
                var card = ctx.GetNextErraticDeckCard(ref stream);

                // Check each clause
                for (int clauseIndex = 0; clauseIndex < _clauses.Length; clauseIndex++)
                {
                    var clause = _clauses[clauseIndex];

                    // Check if this card's rank matches the clause
                    VectorMask rankMatch = VectorEnum256.Equals(
                        card.PlayingCardRank,
                        clause.Rank
                    );

                    // Increment count for matching cards
                    counts[clauseIndex] += Vector256.ConditionalSelect(
                        MotelyVectorUtils.VectorMaskToConditionalSelectMask(rankMatch),
                        Vector256<int>.One,
                        Vector256<int>.Zero
                    );
                }
            }

            // Compare counts against min thresholds
            for (int clauseIndex = 0; clauseIndex < _clauses.Length; clauseIndex++)
            {
                var clause = _clauses[clauseIndex];
                clauseMasks[clauseIndex] = Vector256.GreaterThanOrEqual(
                    counts[clauseIndex],
                    Vector256.Create(clause.MinCount)
                );
            }

            // Combine all clause masks (AND logic - all clauses must match)
            VectorMask finalMask = VectorMask.AllBitsSet;
            for (int i = 0; i < _clauses.Length; i++)
            {
                finalMask &= clauseMasks[i];
            }

            return finalMask;
        }
    }
}

/// <summary>
/// Criteria for ErraticRank filter
/// </summary>
public class MotelyJsonErraticRankFilterCriteria
{
    public List<MotelyJsonErraticRankFilterClause> Clauses { get; set; } = new();
}

/// <summary>
/// Individual ErraticRank clause
/// </summary>
public class MotelyJsonErraticRankFilterClause
{
    public MotelyPlayingCardRank Rank { get; set; }
    public int MinCount { get; set; }
}

/// <summary>
/// Extension methods for creating ErraticRank filter criteria
/// </summary>
public static partial class MotelyJsonFilterClauseExtensions
{
    public static MotelyJsonErraticRankFilterCriteria CreateErraticRankCriteria(
        List<MotelyJsonConfig.MotleyJsonFilterClause> clauses
    )
    {
        var criteria = new MotelyJsonErraticRankFilterCriteria();

        foreach (var clause in clauses)
        {
            if (clause.RankEnum == null)
                continue;

            criteria.Clauses.Add(
                new MotelyJsonErraticRankFilterClause
                {
                    Rank = clause.RankEnum.Value,
                    MinCount = clause.Min ?? 1,
                }
            );
        }

        return criteria;
    }
}
