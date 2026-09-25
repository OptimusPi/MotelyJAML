using System.Text.Json.Serialization;

namespace Motely.DistributedWorker;

[JsonSerializable(typeof(SubmitResultsDto))]
[JsonSerializable(typeof(SubmitResponseDto))]
[JsonSerializable(typeof(SeedResultDto))]
[JsonSerializable(typeof(SeedResultDto[]))]
[JsonSerializable(typeof(PoolClaimRequestDto))]
[JsonSerializable(typeof(PoolClaimResponseDto))]
[JsonSerializable(typeof(ErrorDto))]
[JsonSerializable(typeof(PartyLeaseEnvelopeDto))]
[JsonSerializable(typeof(PartyReportRequestDto))]
[JsonSerializable(typeof(PartyReportResponseDto))]
public partial class WorkerJsonContext : JsonSerializerContext { }

public sealed class SubmitResultsDto
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "submit";

    [JsonPropertyName("filterId")]
    public string FilterId { get; set; } = "";

    [JsonPropertyName("startBatch")]
    public long StartBatch { get; set; }

    [JsonPropertyName("endBatch")]
    public long EndBatch { get; set; }

    [JsonPropertyName("results")]
    public SeedResultDto[] Results { get; set; } = [];

    [JsonPropertyName("seedsSearched")]
    public long SeedsSearched { get; set; }
}

public sealed class SubmitResponseDto
{
    [JsonPropertyName("accepted")]
    public int Accepted { get; set; }
}

public sealed class SeedResultDto
{
    [JsonPropertyName("seed")]
    public string Seed { get; set; } = "";

    [JsonPropertyName("score")]
    public int Score { get; set; }
}

public sealed class ErrorDto
{
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public sealed class PoolClaimRequestDto
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "request";

    [JsonPropertyName("workerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkerId { get; set; }

    [JsonPropertyName("filterId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FilterId { get; set; }

    [JsonPropertyName("estimatedBlocks")]
    public int EstimatedBlocks { get; set; } = 1;
}

public sealed class PoolClaimResponseDto
{
    [JsonPropertyName("idle")]
    public bool Idle { get; set; }

    [JsonPropertyName("retryAfterMs")]
    public int RetryAfterMs { get; set; } = 5000;

    [JsonPropertyName("filterId")]
    public string? FilterId { get; set; }

    [JsonPropertyName("jaml")]
    public string? Jaml { get; set; }

    [JsonPropertyName("batchIndex")]
    public long BatchIndex { get; set; }

    [JsonPropertyName("remaining")]
    public long Remaining { get; set; }

    [JsonPropertyName("batchCharCount")]
    public int BatchCharCount { get; set; } = 5;
}
