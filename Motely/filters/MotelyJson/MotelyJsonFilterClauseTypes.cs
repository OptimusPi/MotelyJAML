using System;
using System.Collections.Generic;
using System.Linq;

namespace Motely.Filters;

/// <summary>
/// Base class for all JSON filter clauses
/// </summary>
public abstract class MotelyJsonFilterClause
{
    public MotelyItemEdition? EditionEnum { get; init; }
    public int? Min { get; init; } // Minimum count threshold - if total count < Min, clause fails

    /// <summary>
    /// Calculate min and max antes from a collection of clauses using their arrays
    /// </summary>
    public static (int minAnte, int maxAnte) CalculateAnteRange<T>(IEnumerable<T> clauses)
        where T : MotelyJsonFilterClause
    {
        int minAnte = int.MaxValue;
        int maxAnte = int.MinValue;

        foreach (var clause in clauses)
        {
            // Get ante array from derived class
            bool[]? wantedAntes = clause switch
            {
                MotelyJsonJokerFilterClause j => j.WantedAntes,
                MotelyJsonSoulJokerFilterClause s => s.WantedAntes,
                MotelyJsonVoucherFilterClause v => v.WantedAntes,
                MotelyJsonTarotFilterClause t => t.WantedAntes,
                MotelyJsonSpectralFilterClause sp => sp.WantedAntes,
                MotelyJsonPlanetFilterClause p => p.WantedAntes,
                _ => null,
            };

            if (wantedAntes != null)
            {
                // Find min and max wanted antes - simple and clear
                for (int ante = 0; ante < wantedAntes.Length; ante++)
                {
                    if (wantedAntes[ante])
                    {
                        if (ante < minAnte)
                            minAnte = ante;
                        if (ante > maxAnte)
                            maxAnte = ante;
                    }
                }
            }
        }

        // Handle empty case
        if (minAnte == int.MaxValue)
        {
            minAnte = 1;
            maxAnte = 1;
        }

        return (minAnte, maxAnte);
    }
}

/// <summary>
/// Specific clause type for Joker filters
/// </summary>
public class MotelyJsonJokerFilterClause : MotelyJsonFilterClause
{
    public MotelyJoker? JokerType { get; init; }
    public List<MotelyJoker>? JokerTypes { get; init; }
    public new MotelyItemEdition? EditionEnum { get; init; } // ADDED: Preserve edition requirements (new keyword to hide base class member)
    public List<MotelyJokerSticker>? StickerEnums { get; init; } // ADDED: Preserve sticker requirements
    public bool IsWildcard { get; init; }
    public MotelyJsonConfigWildcards? WildcardEnum { get; init; }
    public MotelyJsonConfig.SourcesConfig? Sources { get; init; }
    public bool[] WantedAntes { get; init; } = new bool[40];
    public bool[] WantedShopSlots { get; init; } = new bool[1024]; // Support large maxShopSlot values (600+)
    public bool[] WantedPackSlots { get; init; } = new bool[6];
    public int? MinPackSlot { get; init; }
    public int? MaxPackSlot { get; init; }
    public int? MinShopSlot { get; init; }
    public int? MaxShopSlot { get; init; }
    public int MaxShopSlotsNeeded { get; init; } // Pre-calculated max shop slot index + 1
    public int MaxPackSlotsNeeded { get; init; } // Pre-calculated max pack slot index + 1
    public bool HasShopSlots { get; init; } // PRE-COMPUTED FLAG - NO LINQ IN HOTPATH!
    public bool HasPackSlots { get; init; } // PRE-COMPUTED FLAG - NO LINQ IN HOTPATH!

    /// <summary>
    /// Create from generic JSON config clause
    /// </summary>
    public static MotelyJsonJokerFilterClause FromJsonClause(
        MotelyJsonConfig.MotleyJsonFilterClause jsonClause
    )
    {
        bool[] wantedAntes = new bool[40];
        var effectiveAntes = jsonClause.EffectiveAntes;
        foreach (var ante in effectiveAntes)
        {
            if (ante >= 0 && ante < 40)
                wantedAntes[ante] = true;
        }

        // Create shop slots bool array from explicit sources OR from min/max range
        bool[] wantedShopSlots = new bool[1024];
        DebugLogger.Log(
            $"[JOKER CONVERT] Value={jsonClause.Value}, Sources={jsonClause.Sources}, MinShop={jsonClause.MinShopSlot}, MaxShop={jsonClause.MaxShopSlot}"
        );
        if (jsonClause.Sources?.ShopSlots != null)
        {
            // Explicit shop slots specified
            DebugLogger.Log($"[JOKER CONVERT] Using explicit ShopSlots");
            foreach (var slot in jsonClause.Sources.ShopSlots)
            {
                if (slot >= 0 && slot < 1024)
                    wantedShopSlots[slot] = true;
            }
        }
        else if (jsonClause.MinShopSlot.HasValue || jsonClause.MaxShopSlot.HasValue)
        {
            // Use min/max shop slot range (min inclusive, max exclusive)
            int minSlot = jsonClause.MinShopSlot ?? 0;
            int maxSlot = jsonClause.MaxShopSlot ?? 1023;
            DebugLogger.Log(
                $"[SHOP SLOTS] MinShopSlot={jsonClause.MinShopSlot}, MaxShopSlot={jsonClause.MaxShopSlot}, range={minSlot}-{maxSlot}"
            );
            for (int i = minSlot; i < maxSlot && i < 1024; i++)
            {
                wantedShopSlots[i] = true;
            }
            DebugLogger.Log(
                $"[SHOP SLOTS] Set slots {minSlot} to {maxSlot - 1} (exclusive max). Slot[6]={wantedShopSlots[6]}"
            );
        }

        bool[] wantedPackSlots = new bool[6];
        if (jsonClause.Sources?.PackSlots != null)
        {
            DebugLogger.Log(
                $"[JOKER CONVERT] Explicit PackSlots: {string.Join(",", jsonClause.Sources.PackSlots)}"
            );
            foreach (var slot in jsonClause.Sources.PackSlots)
            {
                if (slot >= 0 && slot < 6)
                    wantedPackSlots[slot] = true;
            }
        }
        else if (
            jsonClause.Sources?.ShopSlots == null
            && !jsonClause.MinShopSlot.HasValue
            && !jsonClause.MaxShopSlot.HasValue
        )
        {
            // NO slots specified - leave arrays EMPTY so CountJokerOccurrences can apply ante-based defaults
            // This allows dynamic slot ranges per ante (e.g., ante 0 = 3 shops, ante 3+ = 6 + ante shops)
            // If user specifies ANY source (packSlots, shopSlots, etc), we respect it and DON'T add defaults!
            DebugLogger.Log(
                $"[JOKER CONVERT] No slots specified, will use ante-based defaults in scoring"
            );
            // Both wantedShopSlots and wantedPackSlots remain all-false (empty)
        }
        else
        {
            DebugLogger.Log($"[JOKER CONVERT] ShopSlots specified, NOT setting pack slots");
        }

        // Log final state
        int shopCount = wantedShopSlots.Count(s => s);
        int packCount = wantedPackSlots.Count(s => s);
        DebugLogger.Log(
            $"[JOKER CONVERT FINAL] {jsonClause.Value}: shopSlots={shopCount}, packSlots={packCount}"
        );

        // Pre-calculate MaxShopSlotsNeeded for hot path
        int maxShopSlotsNeeded = 0;
        bool hasShopSlots = false;
        for (int i = 0; i < wantedShopSlots.Length; i++)
        {
            if (wantedShopSlots[i])
            {
                hasShopSlots = true;
                maxShopSlotsNeeded = i + 1;
            }
        }
        // If no specific slots wanted, use default of 8
        if (!hasShopSlots)
            maxShopSlotsNeeded = 8;

        // Pre-calculate MaxPackSlotsNeeded AND HasPackSlots flag
        int maxPackSlotsNeeded = 0;
        bool hasPackSlots = false;
        for (int i = 0; i < wantedPackSlots.Length; i++)
        {
            if (wantedPackSlots[i])
            {
                hasPackSlots = true;
                maxPackSlotsNeeded = i + 1;
            }
        }
        if (maxPackSlotsNeeded == 0)
            maxPackSlotsNeeded = 6; // Default if no specific slots

        return new MotelyJsonJokerFilterClause
        {
            JokerType = jsonClause.JokerEnum,
            JokerTypes = jsonClause.JokerEnums?.Count > 0 ? jsonClause.JokerEnums : null,
            IsWildcard = jsonClause.IsWildcard,
            WildcardEnum = jsonClause.WildcardEnum,
            Sources = jsonClause.Sources,
            EditionEnum = jsonClause.EditionEnum,
            StickerEnums = jsonClause.StickerEnums,
            WantedAntes = wantedAntes,
            WantedShopSlots = wantedShopSlots,
            WantedPackSlots = wantedPackSlots,
            MinPackSlot = jsonClause.MinPackSlot,
            MaxPackSlot = jsonClause.MaxPackSlot,
            MinShopSlot = jsonClause.MinShopSlot,
            MaxShopSlot = jsonClause.MaxShopSlot,
            MaxShopSlotsNeeded = maxShopSlotsNeeded,
            MaxPackSlotsNeeded = maxPackSlotsNeeded,
            HasShopSlots = hasShopSlots, // PRE-COMPUTED!
            HasPackSlots = hasPackSlots, // PRE-COMPUTED!
            Min = jsonClause.Min,
        };
    }

    /// <summary>
    /// Convert a list of generic clauses to joker-specific clauses
    /// </summary>
    public static List<MotelyJsonJokerFilterClause> ConvertClauses(
        List<MotelyJsonConfig.MotleyJsonFilterClause> genericClauses
    )
    {
        return genericClauses
            .Where(c => c.ItemTypeEnum == MotelyFilterItemType.Joker)
            .Select(FromJsonClause)
            .ToList();
    }

    /// <summary>
    /// Create criteria DTO from typed clauses - pre-aggregates all cross-clause calculations
    /// </summary>
    public static MotelyJsonJokerFilterCriteria CreateCriteria(
        List<MotelyJsonJokerFilterClause> clauses
    )
    {
        if (clauses == null || clauses.Count == 0)
            throw new ArgumentException("Clauses cannot be null or empty");

        // Calculate ante range
        var (minAnte, maxAnte) = CalculateAnteRange(clauses);

        // Aggregate MaxShopSlotsNeeded across all clauses
        int maxShopSlotsNeeded = 0;
        foreach (var clause in clauses)
        {
            maxShopSlotsNeeded = Math.Max(maxShopSlotsNeeded, clause.MaxShopSlotsNeeded);
        }

        return new MotelyJsonJokerFilterCriteria
        {
            Clauses = clauses,
            MinAnte = minAnte,
            MaxAnte = maxAnte,
            MaxShopSlotsNeeded = maxShopSlotsNeeded,
        };
    }
}

/// <summary>
/// Specific clause type for SoulJoker filters
/// </summary>
public class MotelyJsonSoulJokerFilterClause : MotelyJsonFilterClause
{
    public MotelyJoker? JokerType { get; init; }
    public List<MotelyJoker>? JokerTypes { get; init; } // Support for values array
    public MotelyItemType? JokerItemType { get; init; } // Precomputed MotelyItemType for comparison
    public bool IsWildcard { get; init; }
    public bool[] WantedAntes { get; init; } = new bool[40];
    public bool[] WantedPackSlots { get; init; } = new bool[6]; // Track which pack slots to check
    public int? MaxPackSlot { get; init; }
    public int? MaxShopSlot { get; init; }
    public int MaxPackSlotsNeeded { get; init; } // Pre-calculated max pack slot index + 1
    public bool RequireMega { get; init; } // Extracted from Sources for optimization
    public bool Satisfied { get; set; } // Track if this clause has been satisfied


    // Parameterless constructor for init syntax
    public MotelyJsonSoulJokerFilterClause() { }

    // Helper constructor for tests - takes List<int> for antes and pack slots
    public MotelyJsonSoulJokerFilterClause(
        MotelyJoker? jokerType,
        List<int> antes,
        List<int> packSlots,
        bool requireMega = false
    )
    {
        JokerType = jokerType;
        JokerItemType = jokerType.HasValue ? (MotelyItemType)jokerType.Value : null;
        IsWildcard = !jokerType.HasValue;
        RequireMega = requireMega;

        // Convert antes list to bool array
        WantedAntes = new bool[40];
        foreach (var ante in antes)
        {
            if (ante >= 0 && ante < 40)
                WantedAntes[ante] = true;
        }

        // Convert pack slots list to bool array
        WantedPackSlots = new bool[6];
        foreach (var slot in packSlots)
        {
            if (slot >= 0 && slot < 6)
                WantedPackSlots[slot] = true;
        }
    }

    public static MotelyJsonSoulJokerFilterClause FromJsonClause(
        MotelyJsonConfig.MotleyJsonFilterClause jsonClause
    )
    {
        bool[] wantedAntes = new bool[40];
        var effectiveAntes = jsonClause.EffectiveAntes ?? Array.Empty<int>();
        foreach (var ante in effectiveAntes)
        {
            if (ante >= 0 && ante < 40)
                wantedAntes[ante] = true;
        }

        // Build pack slot array
        bool[] wantedPackSlots = new bool[6];
        if (jsonClause.Sources?.PackSlots != null)
        {
            foreach (var slot in jsonClause.Sources.PackSlots)
            {
                if (slot >= 0 && slot < 6)
                    wantedPackSlots[slot] = true;
            }
        }

        // Pre-calculate MaxPackSlotsNeeded and check if any pack slots wanted
        int maxPackSlotsNeeded = 0;
        for (int i = 0; i < wantedPackSlots.Length; i++)
        {
            if (wantedPackSlots[i])
            {
                maxPackSlotsNeeded = i + 1;
            }
        }
        if (maxPackSlotsNeeded == 0)
            maxPackSlotsNeeded = 6; // Default

        var clause = new MotelyJsonSoulJokerFilterClause
        {
            JokerType = jsonClause.JokerEnum,
            JokerTypes = jsonClause.JokerEnums?.Count > 0 ? jsonClause.JokerEnums : null,
            JokerItemType = jsonClause.JokerEnum.HasValue
                ? (MotelyItemType)jsonClause.JokerEnum.Value
                : null,
            IsWildcard = jsonClause.IsWildcard,
            EditionEnum = jsonClause.EditionEnum,
            WantedAntes = wantedAntes,
            WantedPackSlots = wantedPackSlots,
            MaxPackSlot = jsonClause.MaxPackSlot,
            MaxShopSlot = jsonClause.MaxShopSlot,
            MaxPackSlotsNeeded = maxPackSlotsNeeded,
            RequireMega = jsonClause.Sources?.RequireMega ?? false,
            Satisfied = false,
            Min = jsonClause.Min
        };

        return clause;
    }

    public static List<MotelyJsonSoulJokerFilterClause> ConvertClauses(
        List<MotelyJsonConfig.MotleyJsonFilterClause> genericClauses
    )
    {
        return genericClauses
            .Where(c => c.ItemTypeEnum == MotelyFilterItemType.SoulJoker)
            .Select(FromJsonClause)
            .ToList();
    }

    /// <summary>
    /// Create criteria DTO from typed clauses - pre-aggregates all cross-clause calculations
    /// </summary>
    public static MotelyJsonSoulJokerFilterCriteria CreateCriteria(
        List<MotelyJsonSoulJokerFilterClause> clauses
    )
    {
        if (clauses == null || clauses.Count == 0)
            throw new ArgumentException("Clauses cannot be null or empty");

        // Calculate ante range
        var (minAnte, maxAnte) = CalculateAnteRange(clauses);

        // CRITICAL OPTIMIZATION: Sort clauses by selectivity (most restrictive first)
        // Edition-only checks (Value="Any" with edition) should run FIRST for early exit
        // Example: "Any soul joker with Negative edition in ante 8" rejects 99.99% of seeds instantly!
        var sortedClauses = clauses
            .OrderByDescending(c =>
            {
                // Priority 1: Edition-only checks (IsWildcard + has edition) - MOST restrictive
                if (c.IsWildcard && c.EditionEnum.HasValue)
                    return 1000;
                // Priority 2: Specific type with edition - moderately restrictive
                if (!c.IsWildcard && c.EditionEnum.HasValue)
                    return 500;
                // Priority 3: Everything else
                return 0;
            })
            .ToList();

        // Pre-calculate max pack slots per ante
        var maxPackSlotsPerAnte = new Dictionary<int, int>();
        for (int ante = minAnte; ante <= maxAnte; ante++)
        {
            // Determine how many pack slots exist in the shop for this ante
            // Ante 0 and Ante 1: Shop shows 4 pack slots (0-3)
            // Ante 2+: Shop shows 6 pack slots (0-5)
            int shopPackSlots = (ante == 0 || ante == 1) ? 4 : 6;

            int maxSlots = 0;
            foreach (var clause in sortedClauses)
            {
                if (ante < clause.WantedAntes.Length && clause.WantedAntes[ante])
                {
                    // Find the highest pack slot this clause wants that ALSO exists in this ante's shop
                    // Example: Clause wants slots [4,5], but ante 1 only has slots 0-3
                    // Result: No overlap, so this clause contributes 0 to maxSlots for ante 1
                    int clauseMaxSlot = 0;
                    for (
                        int slot = Math.Min(shopPackSlots - 1, clause.WantedPackSlots.Length - 1);
                        slot >= 0;
                        slot--
                    )
                    {
                        if (clause.WantedPackSlots[slot])
                        {
                            clauseMaxSlot = slot + 1; // +1 because we need count, not index
                            break;
                        }
                    }
                    maxSlots = Math.Max(maxSlots, clauseMaxSlot);
                }
            }

            // If no pack slots specified by user, use game defaults
            if (maxSlots == 0)
                maxSlots = shopPackSlots;  // 4 for ante 0/1, 6 for ante 2+

            maxPackSlotsPerAnte[ante] = maxSlots;
        }

        return new MotelyJsonSoulJokerFilterCriteria
        {
            Clauses = sortedClauses,
            MinAnte = minAnte,
            MaxAnte = maxAnte,
            MaxPackSlotsPerAnte = maxPackSlotsPerAnte,
        };
    }
}

/// <summary>
/// Specific clause type for Tarot filters
/// </summary>
public class MotelyJsonTarotFilterClause : MotelyJsonFilterClause
{
    public MotelyTarotCard? TarotType { get; init; }
    public List<MotelyTarotCard>? TarotTypes { get; init; }
    public bool IsWildcard { get; init; }
    public MotelyJsonConfig.SourcesConfig? Sources { get; init; }
    public bool[] WantedAntes { get; init; } = new bool[40];
    public bool[] WantedPackSlots { get; init; } = new bool[6];
    public bool[] WantedShopSlots { get; init; } = new bool[1024];
    public int? MaxPackSlot { get; init; }
    public int? MaxShopSlot { get; init; }
    public int MaxShopSlotsNeeded { get; init; } // Pre-calculated max shop slot index + 1
    public int MaxPackSlotsNeeded { get; init; } // Pre-calculated max pack slot index + 1

    public static MotelyJsonTarotFilterClause FromJsonClause(
        MotelyJsonConfig.MotleyJsonFilterClause jsonClause
    )
    {
        bool[] wantedAntes = new bool[40];
        var effectiveAntes = jsonClause.EffectiveAntes ?? Array.Empty<int>();
        foreach (var ante in effectiveAntes)
        {
            if (ante >= 0 && ante < 40)
                wantedAntes[ante] = true;
        }

        // Create shop slots bool array directly from source
        bool[] wantedShopSlots = new bool[1024];
        if (jsonClause.Sources?.ShopSlots != null)
        {
            foreach (var slot in jsonClause.Sources.ShopSlots)
            {
                if (slot >= 0 && slot < 1024)
                    wantedShopSlots[slot] = true;
            }
        }
        // ShopSlots null → empty (matches scoring logic: ?? Array.Empty<int>())

        bool[] wantedPackSlots = new bool[6];
        if (jsonClause.Sources?.PackSlots != null)
        {
            foreach (var slot in jsonClause.Sources.PackSlots)
            {
                if (slot >= 0 && slot < 6)
                    wantedPackSlots[slot] = true;
            }
        }
        else if (jsonClause.Sources?.ShopSlots != null)
        {
            // Default to all pack slots when only shop slots specified (matches scoring logic)
            for (int i = 0; i < 6; i++)
                wantedPackSlots[i] = true;
        }

        // Pre-calculate MaxShopSlotsNeeded
        int maxShopSlotsNeeded = 0;
        for (int i = 0; i < wantedShopSlots.Length; i++)
            if (wantedShopSlots[i])
                maxShopSlotsNeeded = i + 1;
        if (maxShopSlotsNeeded == 0)
            maxShopSlotsNeeded = 8; // Default if no specific slots

        // Pre-calculate MaxPackSlotsNeeded
        int maxPackSlotsNeeded = 0;
        for (int i = 0; i < wantedPackSlots.Length; i++)
            if (wantedPackSlots[i])
                maxPackSlotsNeeded = i + 1;
        if (maxPackSlotsNeeded == 0)
            maxPackSlotsNeeded = 6; // Default if no specific slots

        return new MotelyJsonTarotFilterClause
        {
            TarotType = jsonClause.TarotEnum,
            TarotTypes = jsonClause.TarotEnums?.Count > 0 ? jsonClause.TarotEnums : null,
            IsWildcard = jsonClause.IsWildcard,
            Sources = jsonClause.Sources,
            EditionEnum = jsonClause.EditionEnum,
            WantedAntes = wantedAntes,
            WantedPackSlots = wantedPackSlots,
            WantedShopSlots = wantedShopSlots,
            MaxPackSlot = jsonClause.MaxPackSlot,
            MaxShopSlot = jsonClause.MaxShopSlot,
            MaxShopSlotsNeeded = maxShopSlotsNeeded,
            MaxPackSlotsNeeded = maxPackSlotsNeeded,
            Min = jsonClause.Min,
        };
    }

    public static List<MotelyJsonTarotFilterClause> ConvertClauses(
        List<MotelyJsonConfig.MotleyJsonFilterClause> genericClauses
    )
    {
        return genericClauses
            .Where(c => c.ItemTypeEnum == MotelyFilterItemType.TarotCard)
            .Select(FromJsonClause)
            .ToList();
    }

    /// <summary>
    /// Create criteria DTO from typed clauses - pre-aggregates all cross-clause calculations
    /// </summary>
    public static MotelyJsonTarotFilterCriteria CreateCriteria(
        List<MotelyJsonTarotFilterClause> clauses
    )
    {
        if (clauses == null || clauses.Count == 0)
            throw new ArgumentException("Clauses cannot be null or empty");

        // Calculate ante range
        var (minAnte, maxAnte) = CalculateAnteRange(clauses);

        // Aggregate MaxShopSlotsNeeded across all clauses
        int maxShopSlotsNeeded = 0;
        foreach (var clause in clauses)
        {
            if (HasShopSlots(clause.WantedShopSlots))
            {
                int clauseMaxSlot = 0;
                for (int i = clause.WantedShopSlots.Length - 1; i >= 0; i--)
                {
                    if (clause.WantedShopSlots[i])
                    {
                        clauseMaxSlot = i + 1;
                        break;
                    }
                }
                maxShopSlotsNeeded = Math.Max(maxShopSlotsNeeded, clauseMaxSlot);
            }
            else
            {
                maxShopSlotsNeeded = Math.Max(maxShopSlotsNeeded, 16);
            }
        }

        return new MotelyJsonTarotFilterCriteria
        {
            Clauses = clauses,
            MinAnte = minAnte,
            MaxAnte = maxAnte,
            MaxShopSlotsNeeded = maxShopSlotsNeeded,
        };
    }

    private static bool HasShopSlots(bool[] slots)
    {
        for (int i = 0; i < slots.Length; i++)
            if (slots[i])
                return true;
        return false;
    }
}

/// <summary>
/// Specific clause type for Voucher filters
/// </summary>
public class MotelyJsonVoucherFilterClause : MotelyJsonFilterClause
{
    public MotelyVoucher VoucherType { get; init; }
    public List<MotelyVoucher>? VoucherTypes { get; init; }
    public bool[] WantedAntes { get; init; } = new bool[40];
    public int[] EffectiveAntes { get; init; } = Array.Empty<int>(); // Pre-computed for SIMD hotpath!

    public static MotelyJsonVoucherFilterClause FromJsonClause(
        MotelyJsonConfig.MotleyJsonFilterClause jsonClause
    )
    {
        bool[] wantedAntes = new bool[40];
        var effectiveAntesList = new List<int>(); // Build once during pre-optimization
        var effectiveAntes = jsonClause.EffectiveAntes ?? Array.Empty<int>();
        foreach (var ante in effectiveAntes)
        {
            if (ante >= 0 && ante < 40)
            {
                wantedAntes[ante] = true;
                effectiveAntesList.Add(ante); // Store in array for hotpath
            }
        }

        return new MotelyJsonVoucherFilterClause
        {
            VoucherType = jsonClause.VoucherEnum ?? MotelyVoucher.Overstock,
            VoucherTypes = jsonClause.VoucherEnums?.Count > 0 ? jsonClause.VoucherEnums : null,
            WantedAntes = wantedAntes,
            EffectiveAntes = effectiveAntesList.ToArray(), // Pre-computed once!
            Min = jsonClause.Min,
        };
    }

    public static List<MotelyJsonVoucherFilterClause> ConvertClauses(
        List<MotelyJsonConfig.MotleyJsonFilterClause> genericClauses
    )
    {
        var converted = genericClauses
            .Where(c => c.ItemTypeEnum == MotelyFilterItemType.Voucher)
            .Select(FromJsonClause)
            .ToList();

        DebugLogger.Log(
            $"[VOUCHER CONVERT] Converted {converted.Count} voucher clauses from {genericClauses.Count} total clauses"
        );
        foreach (var clause in converted)
        {
            var antesStr = string.Join(
                ",",
                Enumerable.Range(0, clause.WantedAntes.Length).Where(i => clause.WantedAntes[i])
            );
            DebugLogger.Log($"[VOUCHER CONVERT] Clause: {clause.VoucherType}, Antes=[{antesStr}]");
        }

        return converted;
    }

    /// <summary>
    /// Create criteria DTO from typed clauses - pre-aggregates all cross-clause calculations
    /// </summary>
    public static MotelyJsonVoucherFilterCriteria CreateCriteria(
        List<MotelyJsonVoucherFilterClause> clauses
    )
    {
        if (clauses == null || clauses.Count == 0)
            throw new ArgumentException("Clauses cannot be null or empty");

        // Calculate ante range from WantedAntes arrays
        int minAnte = int.MaxValue;
        int maxAnte = 0;
        foreach (var clause in clauses)
        {
            for (int ante = 0; ante < clause.WantedAntes.Length; ante++)
            {
                if (clause.WantedAntes[ante])
                {
                    minAnte = Math.Min(minAnte, ante);
                    maxAnte = Math.Max(maxAnte, ante);
                }
            }
        }
        if (minAnte == int.MaxValue)
            minAnte = 1;
        if (maxAnte == 0)
            maxAnte = 8;

        DebugLogger.Log(
            $"[VOUCHER CRITERIA] Calculated MinAnte={minAnte}, MaxAnte={maxAnte} from {clauses.Count} clauses"
        );

        return new MotelyJsonVoucherFilterCriteria
        {
            Clauses = clauses,
            MinAnte = minAnte,
            MaxAnte = maxAnte,
        };
    }
}

/// <summary>
/// Specific clause type for Spectral filters
/// </summary>
public class MotelyJsonSpectralFilterClause : MotelyJsonFilterClause
{
    public MotelySpectralCard? SpectralType { get; init; }
    public List<MotelySpectralCard>? SpectralTypes { get; init; }
    public bool IsWildcard { get; init; }
    public MotelyJsonConfig.SourcesConfig? Sources { get; init; }
    public bool[] WantedAntes { get; init; } = new bool[40];
    public bool[] WantedShopSlots { get; init; } = new bool[1024];
    public bool[] WantedPackSlots { get; init; } = new bool[6];
    public int? MaxPackSlot { get; init; }
    public int? MaxShopSlot { get; init; }
    public int MaxShopSlotsNeeded { get; init; } // Pre-calculated max shop slot index + 1
    public int MaxPackSlotsNeeded { get; init; } // Pre-calculated max pack slot index + 1

    public static MotelyJsonSpectralFilterClause FromJsonClause(
        MotelyJsonConfig.MotleyJsonFilterClause jsonClause
    )
    {
        bool[] wantedAntes = new bool[40];
        var effectiveAntes = jsonClause.EffectiveAntes ?? Array.Empty<int>();
        foreach (var ante in effectiveAntes)
        {
            if (ante >= 0 && ante < 40)
                wantedAntes[ante] = true;
        }

        // Create shop slots bool array directly from source
        bool[] wantedShopSlots = new bool[1024];
        if (jsonClause.Sources?.ShopSlots != null)
        {
            foreach (var slot in jsonClause.Sources.ShopSlots)
            {
                if (slot >= 0 && slot < 1024)
                    wantedShopSlots[slot] = true;
            }
        }
        // ShopSlots null → empty (matches scoring logic: ?? Array.Empty<int>())

        bool[] wantedPackSlots = new bool[6];
        if (jsonClause.Sources?.PackSlots != null)
        {
            foreach (var slot in jsonClause.Sources.PackSlots)
            {
                if (slot >= 0 && slot < 6)
                    wantedPackSlots[slot] = true;
            }
        }
        else if (jsonClause.Sources?.ShopSlots != null)
        {
            // Default to all pack slots when only shop slots specified (matches scoring logic)
            for (int i = 0; i < 6; i++)
                wantedPackSlots[i] = true;
        }

        // Pre-calculate MaxShopSlotsNeeded
        int maxShopSlotsNeeded = 0;
        for (int i = 0; i < wantedShopSlots.Length; i++)
            if (wantedShopSlots[i])
                maxShopSlotsNeeded = i + 1;
        if (maxShopSlotsNeeded == 0)
            maxShopSlotsNeeded = 8; // Default

        // Pre-calculate MaxPackSlotsNeeded
        int maxPackSlotsNeeded = 0;
        for (int i = 0; i < wantedPackSlots.Length; i++)
            if (wantedPackSlots[i])
                maxPackSlotsNeeded = i + 1;
        if (maxPackSlotsNeeded == 0)
            maxPackSlotsNeeded = 6; // Default

        return new MotelyJsonSpectralFilterClause
        {
            SpectralType = jsonClause.SpectralEnum,
            SpectralTypes = jsonClause.SpectralEnums?.Count > 0 ? jsonClause.SpectralEnums : null,
            IsWildcard = jsonClause.IsWildcard,
            Sources = jsonClause.Sources,
            EditionEnum = jsonClause.EditionEnum,
            WantedAntes = wantedAntes,
            WantedShopSlots = wantedShopSlots,
            WantedPackSlots = wantedPackSlots,
            MaxPackSlot = jsonClause.MaxPackSlot,
            MaxShopSlot = jsonClause.MaxShopSlot,
            MaxShopSlotsNeeded = maxShopSlotsNeeded,
            MaxPackSlotsNeeded = maxPackSlotsNeeded,
            Min = jsonClause.Min,
        };
    }

    public static List<MotelyJsonSpectralFilterClause> ConvertClauses(
        List<MotelyJsonConfig.MotleyJsonFilterClause> genericClauses
    )
    {
        return genericClauses
            .Where(c => c.ItemTypeEnum == MotelyFilterItemType.SpectralCard)
            .Select(FromJsonClause)
            .ToList();
    }

    /// <summary>
    /// Create criteria DTO from typed clauses - pre-aggregates all cross-clause calculations
    /// </summary>
    public static MotelyJsonSpectralFilterCriteria CreateCriteria(
        List<MotelyJsonSpectralFilterClause> clauses
    )
    {
        if (clauses == null || clauses.Count == 0)
            throw new ArgumentException("Clauses cannot be null or empty");

        // Calculate ante range
        var (minAnte, maxAnte) = CalculateAnteRange(clauses);

        // Aggregate MaxShopSlotsNeeded across all clauses (same logic as Tarot)
        int maxShopSlotsNeeded = 0;
        foreach (var clause in clauses)
        {
            bool hasShopSlots = false;
            for (int i = 0; i < clause.WantedShopSlots.Length; i++)
            {
                if (clause.WantedShopSlots[i])
                {
                    hasShopSlots = true;
                    break;
                }
            }

            if (hasShopSlots)
            {
                int clauseMaxSlot = 0;
                for (int i = clause.WantedShopSlots.Length - 1; i >= 0; i--)
                {
                    if (clause.WantedShopSlots[i])
                    {
                        clauseMaxSlot = i + 1;
                        break;
                    }
                }
                maxShopSlotsNeeded = Math.Max(maxShopSlotsNeeded, clauseMaxSlot);
            }
            else
            {
                maxShopSlotsNeeded = Math.Max(maxShopSlotsNeeded, 6);
            }
        }

        return new MotelyJsonSpectralFilterCriteria
        {
            Clauses = clauses,
            MinAnte = minAnte,
            MaxAnte = maxAnte,
            MaxShopSlotsNeeded = maxShopSlotsNeeded,
        };
    }
}

/// <summary>
/// Specific clause type for Planet filters
/// </summary>
public class MotelyJsonPlanetFilterClause : MotelyJsonFilterClause
{
    public MotelyPlanetCard? PlanetType { get; init; }
    public List<MotelyPlanetCard>? PlanetTypes { get; init; }
    public bool IsWildcard { get; init; }
    public MotelyJsonConfig.SourcesConfig? Sources { get; init; }
    public bool[] WantedAntes { get; init; } = new bool[40];
    public bool[] WantedShopSlots { get; init; } = new bool[1024];
    public bool[] WantedPackSlots { get; init; } = new bool[6];
    public int? MaxPackSlot { get; init; }
    public int? MaxShopSlot { get; init; }
    public int MaxShopSlotsNeeded { get; init; } // Pre-calculated max shop slot index + 1
    public int MaxPackSlotsNeeded { get; init; } // Pre-calculated max pack slot index + 1

    public static MotelyJsonPlanetFilterClause FromJsonClause(
        MotelyJsonConfig.MotleyJsonFilterClause jsonClause
    )
    {
        bool[] wantedAntes = new bool[40];
        var effectiveAntes = jsonClause.EffectiveAntes ?? Array.Empty<int>();
        foreach (var ante in effectiveAntes)
        {
            if (ante >= 0 && ante < 40)
                wantedAntes[ante] = true;
        }

        // Create shop slots bool array directly from source
        bool[] wantedShopSlots = new bool[1024];
        if (jsonClause.Sources?.ShopSlots != null)
        {
            foreach (var slot in jsonClause.Sources.ShopSlots)
            {
                if (slot >= 0 && slot < 1024)
                    wantedShopSlots[slot] = true;
            }
        }
        // ShopSlots null → empty (matches scoring logic: ?? Array.Empty<int>())

        bool[] wantedPackSlots = new bool[6];
        if (jsonClause.Sources?.PackSlots != null)
        {
            foreach (var slot in jsonClause.Sources.PackSlots)
            {
                if (slot >= 0 && slot < 6)
                    wantedPackSlots[slot] = true;
            }
        }
        else if (jsonClause.Sources?.ShopSlots != null)
        {
            // Default to all pack slots when only shop slots specified (matches scoring logic)
            for (int i = 0; i < 6; i++)
                wantedPackSlots[i] = true;
        }

        // Pre-calculate MaxShopSlotsNeeded
        int maxShopSlotsNeeded = 0;
        for (int i = 0; i < wantedShopSlots.Length; i++)
            if (wantedShopSlots[i])
                maxShopSlotsNeeded = i + 1;
        if (maxShopSlotsNeeded == 0)
            maxShopSlotsNeeded = 8; // Default

        // Pre-calculate MaxPackSlotsNeeded
        int maxPackSlotsNeeded = 0;
        for (int i = 0; i < wantedPackSlots.Length; i++)
            if (wantedPackSlots[i])
                maxPackSlotsNeeded = i + 1;
        if (maxPackSlotsNeeded == 0)
            maxPackSlotsNeeded = 6; // Default

        return new MotelyJsonPlanetFilterClause
        {
            PlanetType = jsonClause.PlanetEnum,
            PlanetTypes = jsonClause.PlanetEnums?.Count > 0 ? jsonClause.PlanetEnums : null,
            IsWildcard = jsonClause.IsWildcard,
            Sources = jsonClause.Sources,
            EditionEnum = jsonClause.EditionEnum,
            WantedAntes = wantedAntes,
            WantedShopSlots = wantedShopSlots,
            WantedPackSlots = wantedPackSlots,
            MaxPackSlot = jsonClause.MaxPackSlot,
            MaxShopSlot = jsonClause.MaxShopSlot,
            MaxShopSlotsNeeded = maxShopSlotsNeeded,
            MaxPackSlotsNeeded = maxPackSlotsNeeded,
            Min = jsonClause.Min,
        };
    }

    public static List<MotelyJsonPlanetFilterClause> ConvertClauses(
        List<MotelyJsonConfig.MotleyJsonFilterClause> genericClauses
    )
    {
        return genericClauses
            .Where(c => c.ItemTypeEnum == MotelyFilterItemType.PlanetCard)
            .Select(FromJsonClause)
            .ToList();
    }

    /// <summary>
    /// Create criteria DTO from typed clauses - pre-aggregates all cross-clause calculations
    /// </summary>
    public static MotelyJsonPlanetFilterCriteria CreateCriteria(
        List<MotelyJsonPlanetFilterClause> clauses
    )
    {
        if (clauses == null || clauses.Count == 0)
            throw new ArgumentException("Clauses cannot be null or empty");

        // Calculate ante range
        var (minAnte, maxAnte) = CalculateAnteRange(clauses);

        // Aggregate MaxShopSlotsNeeded across all clauses
        int maxShopSlotsNeeded = 0;
        foreach (var clause in clauses)
        {
            bool hasShopSlots = false;
            for (int i = 0; i < clause.WantedShopSlots.Length; i++)
            {
                if (clause.WantedShopSlots[i])
                {
                    hasShopSlots = true;
                    break;
                }
            }

            if (hasShopSlots)
            {
                for (int i = clause.WantedShopSlots.Length - 1; i >= 0; i--)
                {
                    if (clause.WantedShopSlots[i])
                    {
                        maxShopSlotsNeeded = Math.Max(maxShopSlotsNeeded, i + 1);
                        break;
                    }
                }
            }
            else
            {
                maxShopSlotsNeeded = Math.Max(maxShopSlotsNeeded, 16);
            }
        }

        return new MotelyJsonPlanetFilterCriteria
        {
            Clauses = clauses,
            MinAnte = minAnte,
            MaxAnte = maxAnte,
            MaxShopSlotsNeeded = maxShopSlotsNeeded,
        };
    }
}

/// <summary>
/// Extension methods for creating criteria from generic clause lists (Boss, Tag, PlayingCard)
/// </summary>
public static partial class MotelyJsonFilterClauseExtensions
{
    /// <summary>
    /// Create Boss filter criteria from generic clauses
    /// </summary>
    public static MotelyJsonBossFilterCriteria CreateBossCriteria(
        List<MotelyJsonConfig.MotleyJsonFilterClause> clauses
    )
    {
        if (clauses == null || clauses.Count == 0)
            throw new ArgumentException("Clauses cannot be null or empty");

        int minAnte = int.MaxValue;
        int maxAnte = int.MinValue;
        foreach (var clause in clauses)
        {
            if (clause.EffectiveAntes != null)
            {
                foreach (var ante in clause.EffectiveAntes)
                {
                    if (ante < minAnte)
                        minAnte = ante;
                    if (ante > maxAnte)
                        maxAnte = ante;
                }
            }
        }
        if (minAnte == int.MaxValue)
            minAnte = 1;
        if (maxAnte == int.MinValue)
            maxAnte = 8;

        return new MotelyJsonBossFilterCriteria
        {
            Clauses = clauses,
            MinAnte = minAnte,
            MaxAnte = maxAnte,
        };
    }

    /// <summary>
    /// Create Tag filter criteria from generic clauses
    /// </summary>
    public static MotelyJsonTagFilterCriteria CreateTagCriteria(
        List<MotelyJsonConfig.MotleyJsonFilterClause> clauses
    )
    {
        if (clauses == null || clauses.Count == 0)
            throw new ArgumentException("Clauses cannot be null or empty");

        int minAnte = int.MaxValue;
        int maxAnte = int.MinValue;
        foreach (var clause in clauses)
        {
            if (clause.EffectiveAntes != null)
            {
                foreach (var ante in clause.EffectiveAntes)
                {
                    minAnte = Math.Min(minAnte, ante);
                    maxAnte = Math.Max(maxAnte, ante);
                }
            }
        }
        if (minAnte == int.MaxValue)
            minAnte = 1;
        if (maxAnte == int.MinValue)
            maxAnte = 8;

        return new MotelyJsonTagFilterCriteria
        {
            Clauses = clauses,
            MinAnte = minAnte,
            MaxAnte = maxAnte,
        };
    }

    /// <summary>
    /// Create PlayingCard filter criteria from generic clauses
    /// </summary>
    public static MotelyJsonPlayingCardFilterCriteria CreatePlayingCardCriteria(
        List<MotelyJsonConfig.MotleyJsonFilterClause> clauses
    )
    {
        if (clauses == null || clauses.Count == 0)
            throw new ArgumentException("Clauses cannot be null or empty");

        int minAnte = int.MaxValue;
        int maxAnte = int.MinValue;
        foreach (var clause in clauses)
        {
            if (clause.EffectiveAntes != null)
            {
                foreach (var ante in clause.EffectiveAntes)
                {
                    if (ante < minAnte)
                        minAnte = ante;
                    if (ante > maxAnte)
                        maxAnte = ante;
                }
            }
        }
        if (minAnte == int.MaxValue)
            minAnte = 1;
        if (maxAnte == int.MinValue)
            maxAnte = 8;

        return new MotelyJsonPlayingCardFilterCriteria
        {
            Clauses = clauses,
            MinAnte = minAnte,
            MaxAnte = maxAnte,
        };
    }

    /// <summary>
    /// Create Event filter criteria from generic clauses
    /// </summary>
    public static MotelyJsonEventFilterCriteria CreateEventCriteria(
        List<MotelyJsonConfig.MotleyJsonFilterClause> clauses
    )
    {
        if (clauses == null || clauses.Count == 0)
            throw new ArgumentException("Clauses cannot be null or empty");

        var eventClauses = new List<MotelyJsonEventFilterClause>();
        foreach (var clause in clauses)
        {
            if (clause.EventTypeEnum.HasValue)
            {
                bool[] wantedAntes = new bool[40];
                foreach (var ante in clause.EffectiveAntes)
                {
                    if (ante >= 0 && ante < wantedAntes.Length)
                        wantedAntes[ante] = true;
                }

                eventClauses.Add(
                    new MotelyJsonEventFilterClause
                    {
                        EventTypeEnum = clause.EventTypeEnum.Value,
                        Rolls = clause.Rolls ?? Array.Empty<int>(),
                        WantedAntes = wantedAntes,
                    }
                );
            }
        }

        if (eventClauses.Count == 0)
            throw new ArgumentException("No event clauses found");

        return new MotelyJsonEventFilterCriteria { Clauses = eventClauses };
    }
}

/// <summary>
/// Specific clause type for Event filters
/// </summary>
public class MotelyJsonEventFilterClause
{
    public MotelyEventType EventTypeEnum { get; init; }
    public int[] Rolls { get; init; } = Array.Empty<int>();
    public bool[] WantedAntes { get; init; } = new bool[40];
}

/// <summary>
/// Criteria DTO for Event filters
/// </summary>
public class MotelyJsonEventFilterCriteria
{
    public List<MotelyJsonEventFilterClause> Clauses { get; init; } = new();
}
