using System.Text.Json.Serialization;

namespace Motely.DistributedWorker;

public sealed class PartyLeaseEnvelopeDto
{
    [JsonPropertyName("lease")] public PartyLeaseDto? Lease { get; set; }

    [JsonPropertyName("done")] public bool Done { get; set; }

    [JsonPropertyName("reason")] public string? Reason { get; set; }

    [JsonPropertyName("error")] public string? Error { get; set; }
}

public sealed class PartyLeaseDto
{
    [JsonPropertyName("partyId")] public string PartyId { get; set; } = "";

    [JsonPropertyName("jaml")] public string Jaml { get; set; } = "";

    [JsonPropertyName("deck")] public string? Deck { get; set; }

    [JsonPropertyName("stake")] public string? Stake { get; set; }

    [JsonPropertyName("batchChars")] public int BatchChars { get; set; }

    [JsonPropertyName("startBlock")] public long StartBlock { get; set; }

    [JsonPropertyName("blockCount")] public long BlockCount { get; set; }

    [JsonPropertyName("totalBlocks")] public long TotalBlocks { get; set; }

    [JsonPropertyName("workerToken")] public string WorkerToken { get; set; } = "";

    [JsonPropertyName("expiresAt")] public string? ExpiresAt { get; set; }
}

public sealed class PartyReportRequestDto
{
    [JsonPropertyName("partyId")] public string PartyId { get; set; } = "";

    [JsonPropertyName("workerToken")] public string WorkerToken { get; set; } = "";

    [JsonPropertyName("startBlock")] public long StartBlock { get; set; }

    [JsonPropertyName("seeds")] public string[] Seeds { get; set; } = [];

    [JsonPropertyName("heartbeatOnly")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? HeartbeatOnly { get; set; }
}

public sealed class PartyReportResponseDto
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }

    [JsonPropertyName("confirmed")] public int Confirmed { get; set; }

    [JsonPropertyName("rejected")] public int Rejected { get; set; }

    [JsonPropertyName("recorded")] public int Recorded { get; set; }

    [JsonPropertyName("error")] public string? Error { get; set; }
}
