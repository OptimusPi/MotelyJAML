using System.Text.Json.Serialization;

namespace Motely.CLI.Party;

// Wire shapes of the seedfinder.app Search Party coordinator (lib/party/PROTOCOL.md there):
//   GET  /api/party/next?partyId=   -> {lease} | {done, reason} | {error}
//   POST /api/party/report          -> {ok} (heartbeat) | {confirmed, rejected, recorded} | {error}

/// <summary>
/// One block range of a party's sweep. The range is <c>[StartBlock, StartBlock + BlockCount)</c>,
/// end exclusive, and a block is an engine sequential batch index at <see cref="BatchChars"/>.
/// </summary>
public sealed class PartyLease
{
    public string PartyId { get; init; } = "";
    public string Jaml { get; init; } = "";

    /// <summary>Overrides the JAML's deck when non-null.</summary>
    public string? Deck { get; init; }

    /// <summary>Overrides the JAML's stake when non-null.</summary>
    public string? Stake { get; init; }

    public int BatchChars { get; init; }
    public long StartBlock { get; init; }
    public long BlockCount { get; init; }
    public long TotalBlocks { get; init; }
    public string WorkerToken { get; init; } = "";
    public string? ExpiresAt { get; init; }

    public long EndBlock => StartBlock + BlockCount;
}

public sealed class PartyNextResponse
{
    public PartyLease? Lease { get; init; }
    public bool Done { get; init; }
    public string? Reason { get; init; }
    public string? Error { get; init; }
}

public sealed class PartyReportRequest
{
    public string PartyId { get; init; } = "";
    public string WorkerToken { get; init; } = "";
    public long StartBlock { get; init; }

    /// <summary>Candidate seeds, at most 1000. Null (omitted) on a heartbeat.</summary>
    public IReadOnlyList<string>? Seeds { get; init; }

    /// <summary>True on a heartbeat; omitted on the real report.</summary>
    public bool? HeartbeatOnly { get; init; }
}

public sealed class PartyReportResponse
{
    /// <summary>Heartbeat answer: false when the lease is no longer this worker's.</summary>
    public bool? Ok { get; init; }
    public string[]? Confirmed { get; init; }
    public string[]? Rejected { get; init; }
    public int? Recorded { get; init; }
    public string? Error { get; init; }
}

// Source-generated: Motely.CLI is NativeAOT-published, so no reflection-based JSON.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true
)]
[JsonSerializable(typeof(PartyNextResponse))]
[JsonSerializable(typeof(PartyReportRequest))]
[JsonSerializable(typeof(PartyReportResponse))]
public sealed partial class PartyJsonContext : JsonSerializerContext;
