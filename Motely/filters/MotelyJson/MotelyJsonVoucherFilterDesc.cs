using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Motely.Filters;

namespace Motely.Filters;

/// <summary>
/// Filters seeds based on voucher criteria from JSON configuration.
/// </summary>
public struct MotelyJsonVoucherFilterDesc(MotelyJsonVoucherFilterCriteria criteria)
    : IMotelySeedFilterDesc<MotelyJsonVoucherFilterDesc.MotelyJsonVoucherFilter>
{
    private readonly MotelyJsonVoucherFilterCriteria _criteria = criteria;

    public MotelyJsonVoucherFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        foreach (var clause in _criteria.Clauses)
        {
            DebugLogger.Log(
                $"[VOUCHER] Clause: VoucherType={clause.VoucherType}, VoucherTypes={clause.VoucherTypes?.Count ?? 0}"
            );

            for (int anteIndex = 0; anteIndex < 40; anteIndex++)
            {
                if (clause.WantedAntes[anteIndex])
                {
                    DebugLogger.Log(
                        $"[VOUCHER] Caching ante {anteIndex} for voucher {clause.VoucherType}"
                    );
                    ctx.CacheAnteFirstVoucher(anteIndex);
                }
            }
        }

        return new MotelyJsonVoucherFilter(_criteria.Clauses, _criteria.MinAnte, _criteria.MaxAnte);
    }

    public struct MotelyJsonVoucherFilter : IMotelySeedFilter
    {
        private readonly MotelyJsonVoucherFilterClause[] _clauses;
        private readonly int _minAnte;
        private readonly int _maxAnte;
        private readonly bool _lookingForPetroglyph;

        public MotelyJsonVoucherFilter(
            List<MotelyJsonVoucherFilterClause> clauses,
            int minAnte,
            int maxAnte
        )
        {
            _clauses = [.. clauses];
            _minAnte = minAnte;
            _maxAnte = maxAnte;
            _lookingForPetroglyph = _clauses.Any(clause =>
                clause.VoucherType == MotelyVoucher.Petroglyph
            );

            DebugLogger.Log(
                $"[VOUCHER FILTER] Created filter with minAnte={minAnte}, maxAnte={maxAnte}, {clauses.Count} clauses"
            );
        }

        [MethodImpl(
            MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization
        )]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            if (_clauses == null || _clauses.Length == 0)
                return VectorMask.AllBitsSet;

            // Stack-allocated clause masks - no heap allocation!
            Span<VectorMask> clauseMasks = stackalloc VectorMask[_clauses.Length];
            for (int i = 0; i < clauseMasks.Length; i++)
            {
                clauseMasks[i] = VectorMask.NoBitsSet;
            }
            var voucherState = new MotelyVectorRunState();

            // Check vouchers from minAnte to maxAnte (ante 0 exists with Hieroglyph/Petroglyph) but game starts at ante 1.
            for (int ante = 1; ante <= _maxAnte && ante < _clauses[0].WantedAntes.Length; ante++)
            {
                var vouchers = ctx.GetAnteFirstVoucher(ante, voucherState);

                DebugLogger.Log(
                    $"[VOUCHER VECTORIZED] Ante {ante}: Checking vouchers, lane 0 has {vouchers[0]}"
                );

                // Check all clauses for this ante (first voucher)
                for (int i = 0; i < _clauses.Length; i++)
                {
                    // Check if this ante is wanted
                    if (_clauses[i].WantedAntes[ante])
                    {
                        if (_clauses[i].VoucherTypes?.Count > 1)
                        {
                            // Multi-value: OR logic - match any voucher in the list
                            foreach (var voucherType in _clauses[i].VoucherTypes!)
                            {
                                clauseMasks[i] |= VectorEnum256.Equals(vouchers, voucherType);
                            }
                        }
                        else
                        {
                            clauseMasks[i] |= VectorEnum256.Equals(
                                vouchers,
                                _clauses[i].VoucherType
                            );
                        }
                    }
                }

                voucherState.ActivateVoucher(vouchers);

                // Handle Hieroglyph bonus voucher (replay ante = second voucher in SAME ante!)
                // This allows filters like "Hieroglyph at ante 1 AND Petroglyph at ante 1" to work correctly
                // when Hieroglyph gives Petroglyph as the bonus voucher
                VectorMask isHieroglyph = VectorEnum256.Equals(vouchers, MotelyVoucher.Hieroglyph);
                if (!isHieroglyph.IsAllFalse())
                {
                    // Some lanes have Hieroglyph - get the bonus voucher for those lanes
                    var voucherStream = ctx.CreateVoucherStream(ante, isCached: true);
                    var bonusVouchers = ctx.GetNextVoucher(ref voucherStream, voucherState);

                    // Check all clauses against the bonus voucher FOR THE SAME ANTE
                    for (int i = 0; i < _clauses.Length; i++)
                    {
                        // Check if this ante is wanted (must be same ante as Hieroglyph!)
                        if (_clauses[i].WantedAntes[ante])
                        {
                            // Only check lanes that actually have Hieroglyph
                            // For other lanes, the bonus voucher doesn't matter
                            VectorMask bonusMatches = VectorMask.NoBitsSet;

                            if (_clauses[i].VoucherTypes?.Count > 1)
                            {
                                // Multi-value: OR logic - match any voucher in the list
                                foreach (var voucherType in _clauses[i].VoucherTypes!)
                                {
                                    bonusMatches |= VectorEnum256.Equals(
                                        bonusVouchers,
                                        voucherType
                                    );
                                }
                            }
                            else
                            {
                                bonusMatches = VectorEnum256.Equals(
                                    bonusVouchers,
                                    _clauses[i].VoucherType
                                );
                            }

                            // Only count matches in lanes that have Hieroglyph (bonus is only valid there)
                            clauseMasks[i] |= bonusMatches & isHieroglyph;
                        }
                    }

                    voucherState.ActivateVoucher(bonusVouchers);
                }
            }

            // Combine clause masks with AND logic (all clauses must match)
            var resultMask = VectorMask.AllBitsSet;
            for (int i = 0; i < clauseMasks.Length; i++)
            {
                if (clauseMasks[i].IsAllFalse())
                {
                    DebugLogger.Log($"[VOUCHER] Clause {i} found no matches - failing");
                    return VectorMask.NoBitsSet;
                }

                resultMask &= clauseMasks[i];
                if (resultMask.IsAllFalse())
                    return VectorMask.NoBitsSet;
            }

            if (resultMask.IsAllFalse() && !_lookingForPetroglyph)
                return VectorMask.NoBitsSet;

            // Use the SHARED scoring function - NO CONVERSION NEEDED! Uses typed clause directly!
            var clauses = _clauses;
            var maxAnte = _maxAnte; // Capture for lambda
            return ctx.SearchIndividualSeeds(
                resultMask,
                (ref MotelySingleSearchContext singleCtx) =>
                {
                    var voucherState = new MotelyRunState();

                    // Walk ALL antes from 1 to maxAnte ONCE, checking ALL clauses as we go!
                    // This builds voucher state progressively (like the vectorized filter does)
                    int[] clauseCounts = new int[clauses.Length];
                    int clausesSatisfied = 0; // Track how many clauses have met their Min threshold

                    for (int ante = 1; ante <= maxAnte; ante++)
                    {
                        var voucherAtAnte = singleCtx.GetAnteFirstVoucher(ante, voucherState);

                        // Check each clause for this ante
                        for (int i = 0; i < clauses.Length; i++)
                        {
                            var clause = clauses[i];

                            // Check if this clause wants this ante
                            if (clause.WantedAntes[ante])
                            {
                                // Check single voucher OR multi-voucher array
                                bool matches = false;
                                if (clause.VoucherTypes != null && clause.VoucherTypes.Count > 0)
                                {
                                    // Multi-value: Check if voucher matches ANY in the list
                                    foreach (var voucherType in clause.VoucherTypes)
                                    {
                                        if (voucherAtAnte == voucherType)
                                        {
                                            matches = true;
                                            break;
                                        }
                                    }
                                }
                                else
                                {
                                    // Single value
                                    matches = voucherAtAnte == clause.VoucherType;
                                }

                                if (matches)
                                {
                                    int previousCount = clauseCounts[i];
                                    clauseCounts[i]++;

                                    // EARLY EXIT OPTIMIZATION: Check if this clause just became satisfied
                                    int minThreshold = clause.Min ?? 1;
                                    if (
                                        previousCount < minThreshold
                                        && clauseCounts[i] >= minThreshold
                                    )
                                    {
                                        clausesSatisfied++;

                                        // ALL CLAUSES SATISFIED - EARLY EXIT!
                                        if (clausesSatisfied == clauses.Length)
                                            return true;
                                    }
                                }
                            }
                        }

                        // ALWAYS activate voucher to update state for next ante
                        voucherState.ActivateVoucher(voucherAtAnte);

                        // Handle Hieroglyph bonus voucher (replay ante = second voucher from same ante!)
                        // This allows Hieroglyph + Petroglyph both at ante 1
                        if (voucherAtAnte == MotelyVoucher.Hieroglyph)
                        {
                            var voucherStream = singleCtx.CreateVoucherStream(ante, isCached: true);
                            var bonusVoucher = singleCtx.GetNextVoucher(
                                ref voucherStream,
                                voucherState
                            );

                            // Check the bonus voucher against all clauses for THIS SAME ANTE!
                            for (int i = 0; i < clauses.Length; i++)
                            {
                                var clause = clauses[i];

                                // Check if this clause wants this ante
                                if (clause.WantedAntes[ante])
                                {
                                    // Check single voucher OR multi-voucher array
                                    bool matches = false;
                                    if (
                                        clause.VoucherTypes != null
                                        && clause.VoucherTypes.Count > 0
                                    )
                                    {
                                        // Multi-value: Check if bonus voucher matches ANY in the list
                                        foreach (var voucherType in clause.VoucherTypes)
                                        {
                                            if (bonusVoucher == voucherType)
                                            {
                                                matches = true;
                                                break;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // Single value
                                        matches = bonusVoucher == clause.VoucherType;
                                    }

                                    if (matches)
                                    {
                                        int previousCount = clauseCounts[i];
                                        clauseCounts[i]++;

                                        // EARLY EXIT OPTIMIZATION: Check if this clause just became satisfied
                                        int minThreshold = clause.Min ?? 1;
                                        if (
                                            previousCount < minThreshold
                                            && clauseCounts[i] >= minThreshold
                                        )
                                        {
                                            clausesSatisfied++;

                                            // ALL CLAUSES SATISFIED - EARLY EXIT!
                                            if (clausesSatisfied == clauses.Length)
                                                return true;
                                        }
                                    }
                                }
                            }

                            voucherState.ActivateVoucher(bonusVoucher);
                        }

                        // CRITICAL EARLY EXIT: Check all clauses AFTER processing both main voucher AND Hieroglyph bonus
                        for (int i = 0; i < clauses.Length; i++)
                        {
                            var clause = clauses[i];

                            // Check if this is the FINAL ante for this clause
                            if (clause.WantedAntes[ante])
                            {
                                var effectiveAntes = clause.EffectiveAntes;
                                if (
                                    effectiveAntes.Length > 0
                                    && ante == effectiveAntes[effectiveAntes.Length - 1]
                                )
                                {
                                    int minThreshold = clause.Min ?? 1;
                                    if (clauseCounts[i] < minThreshold)
                                    {
                                        // This clause FAILED at its final ante - no point continuing!
                                        return false;
                                    }
                                }
                            }
                        }
                    }

                    // Check if all clauses met their Min thresholds (shouldn't get here if early exit worked)
                    for (int i = 0; i < clauses.Length; i++)
                    {
                        int minThreshold = clauses[i].Min ?? 1;
                        if (clauseCounts[i] < minThreshold)
                            return false;
                    }

                    return true;
                }
            );
        }
    }
}
