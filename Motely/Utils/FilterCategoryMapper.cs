using System;
using System.Collections.Generic;
using System.Linq;
using Motely.Filters;

namespace Motely.Utils
{
    /// <summary>
    /// Shared utility for filter category mapping and slicing
    /// </summary>
    public static class FilterCategoryMapper
    {
        /// <summary>
        /// Maps item type to optimized filter category
        /// </summary>
        public static FilterCategory GetCategory(MotelyFilterItemType itemType)
        {
            return itemType switch
            {
                MotelyFilterItemType.Voucher => FilterCategory.Voucher,
                MotelyFilterItemType.Joker => FilterCategory.Joker,
                MotelyFilterItemType.SoulJoker => FilterCategory.SoulJoker,
                MotelyFilterItemType.TarotCard => FilterCategory.TarotCard,
                MotelyFilterItemType.PlanetCard => FilterCategory.PlanetCard,
                MotelyFilterItemType.SpectralCard => FilterCategory.SpectralCard,
                MotelyFilterItemType.PlayingCard => FilterCategory.PlayingCard,
                MotelyFilterItemType.SmallBlindTag or MotelyFilterItemType.BigBlindTag =>
                    FilterCategory.Tag,
                MotelyFilterItemType.Boss => FilterCategory.Boss,
                MotelyFilterItemType.Event => FilterCategory.Event,
                MotelyFilterItemType.ErraticRank => FilterCategory.ErraticRank,
                MotelyFilterItemType.ErraticSuit => FilterCategory.ErraticSuit,
                MotelyFilterItemType.And => FilterCategory.And,
                MotelyFilterItemType.Or => FilterCategory.Or,
                _ => throw new Exception($"Unknown item type: {itemType}"),
            };
        }

        /// <summary>
        /// PROPER SLICING: Groups clauses by FilterCategory for optimal vectorization
        /// </summary>
        public static Dictionary<
            FilterCategory,
            List<MotelyJsonConfig.MotleyJsonFilterClause>
        > GroupClausesByCategory(List<MotelyJsonConfig.MotleyJsonFilterClause> clauses)
        {
            var grouped =
                new Dictionary<FilterCategory, List<MotelyJsonConfig.MotleyJsonFilterClause>>();

            foreach (var clause in clauses)
            {
                var category = GetCategory(clause.ItemTypeEnum);

                // CRITICAL OPTIMIZATION: Split SoulJoker into edition-only vs type-specific
                // Edition-only clauses (Value="Any" + edition specified) create separate filter for instant early-exit!
                if (category == FilterCategory.SoulJoker)
                {
                    bool isEditionOnly =
                        (
                            clause.Value?.Equals("Any", StringComparison.OrdinalIgnoreCase) == true
                            || clause.Values == null
                            || clause.Values.Length == 0
                        )
                        && !string.IsNullOrEmpty(clause.Edition)
                        && !clause.Edition.Equals("None", StringComparison.OrdinalIgnoreCase);

                    if (isEditionOnly)
                    {
                        category = FilterCategory.SoulJokerEditionOnly;
                    }
                }

                if (!grouped.ContainsKey(category))
                {
                    grouped[category] = new List<MotelyJsonConfig.MotleyJsonFilterClause>();
                }

                grouped[category].Add(clause);
            }

            // CRITICAL OPTIMIZATION: Combine ErraticRank and ErraticSuit into single filter for max performance
            // If we have BOTH rank and suit clauses, merge them into ErraticRankAndSuit to avoid double-looping
            if (
                grouped.ContainsKey(FilterCategory.ErraticRank)
                && grouped.ContainsKey(FilterCategory.ErraticSuit)
            )
            {
                var combinedClauses = new List<MotelyJsonConfig.MotleyJsonFilterClause>();
                combinedClauses.AddRange(grouped[FilterCategory.ErraticRank]);
                combinedClauses.AddRange(grouped[FilterCategory.ErraticSuit]);

                grouped[FilterCategory.ErraticRankAndSuit] = combinedClauses;
                grouped.Remove(FilterCategory.ErraticRank);
                grouped.Remove(FilterCategory.ErraticSuit);
            }

            return grouped;
        }
    }
}
