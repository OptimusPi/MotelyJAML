using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Motely.MCP.Data;

public sealed record SavedFilterRecord(
    string Slug,
    string Name,
    string? Author,
    string? Description,
    string Deck,
    string Stake,
    string JamlContent,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc
);

public sealed record DiscoveredSeedRecord(
    string Seed,
    string FilterSlug,
    string FilterName,
    int Score,
    int[] Tallies,
    string Deck,
    string Stake,
    string? PartyId,
    string? WorkerId,
    DateTimeOffset FoundAtUtc
);

public sealed record BlockRangeRecord(long StartBlock, long BlockCount);

public sealed record PartyRecord(
    string PartyId,
    string Name,
    string Jaml,
    string? Deck,
    string? Stake,
    int BatchChars,
    long BlockSize,
    long TotalBlocks,
    long NextUnassignedBlock,
    long CompletedBlocks,
    int? TargetMatches,
    int MatchesFound,
    string Status,
    IReadOnlyList<BlockRangeRecord> CompletedRanges,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc
);

/// <summary>
/// Persistent storage abstraction for saved JAML filters, discovered seeds / leaderboards,
/// and Community Search Party state.
/// </summary>
public interface IMotelyStore
{
    Task InitializeAsync(string? filtersDirectory = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedFilterRecord>> GetFiltersAsync(CancellationToken cancellationToken = default);
    Task<SavedFilterRecord?> GetFilterAsync(string slug, CancellationToken cancellationToken = default);
    Task<SavedFilterRecord> SaveFilterAsync(SavedFilterRecord filter, CancellationToken cancellationToken = default);

    Task<int> RecordSeedsAsync(
        IEnumerable<DiscoveredSeedRecord> seeds,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<DiscoveredSeedRecord>> GetRecentFindsAsync(
        string? filterSlug = null,
        int limit = 50,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<DiscoveredSeedRecord>> GetLeaderboardAsync(
        string? filterSlug = null,
        string? deck = null,
        string? stake = null,
        int limit = 50,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<PartyRecord>> GetPartiesAsync(CancellationToken cancellationToken = default);
    Task<PartyRecord?> GetPartyAsync(string partyId, CancellationToken cancellationToken = default);
    Task<PartyRecord> SavePartyAsync(PartyRecord party, CancellationToken cancellationToken = default);
}
