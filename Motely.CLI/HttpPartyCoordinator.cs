using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Motely.Party;

namespace Motely.CLI;

/// <summary>
/// seedfinder.app's Search Party protocol (server side: <c>app/api/party/*</c>, mirrored in its
/// <c>lib/party/PROTOCOL.md</c>):
/// <c>GET /api/party/next?partyId=</c> → <c>{lease}</c> | <c>{done, reason}</c> | <c>{error}</c>;
/// <c>POST /api/party/report</c> with <c>{partyId, workerToken, startBlock, seeds, heartbeatOnly?}</c>
/// → <c>{ok}</c> | <c>{confirmed, rejected, recorded}</c> | <c>{error}</c>.
/// Compiled into Motely.Tests too (linked), so it depends on nothing else in the CLI.
/// </summary>
internal sealed class HttpPartyCoordinator(HttpClient http, string serverUrl) : IPartyCoordinator
{
    private readonly string _baseUrl = serverUrl.Trim().TrimEnd('/');

    public async Task<PartyNext> NextAsync(string partyId, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(
            $"{_baseUrl}/api/party/next?partyId={Uri.EscapeDataString(partyId)}",
            cancellationToken
        );
        var body = await ReadAsync(response, PartyJsonContext.Default.LeaseEnvelope, cancellationToken);
        if (body.Error is { } error)
            throw new HttpRequestException($"Lease refused: {error}");
        if (body.Lease is not { } l)
            return new PartyNext(null, body.Reason ?? "done");
        return new PartyNext(
            new PartyLease(l.PartyId, l.Jaml, l.BatchChars, l.StartBlock, l.BlockCount, l.TotalBlocks, l.WorkerToken)
        );
    }

    public async Task HeartbeatAsync(PartyLease lease, CancellationToken cancellationToken) =>
        await PostReportAsync(new ReportRequest(lease.PartyId, lease.WorkerToken, lease.StartBlock, [], true), cancellationToken);

    public async Task<PartyReportResult> ReportAsync(
        PartyLease lease,
        IReadOnlyList<string> seeds,
        CancellationToken cancellationToken
    )
    {
        var body = await PostReportAsync(
            new ReportRequest(lease.PartyId, lease.WorkerToken, lease.StartBlock, [.. seeds], null),
            cancellationToken
        );
        return new PartyReportResult(body.Confirmed, body.Rejected);
    }

    private async Task<ReportResponse> PostReportAsync(ReportRequest request, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(
            $"{_baseUrl}/api/party/report",
            request,
            PartyJsonContext.Default.ReportRequest,
            cancellationToken
        );
        var body = await ReadAsync(response, PartyJsonContext.Default.ReportResponse, cancellationToken);
        return body.Error is { } error ? throw new HttpRequestException($"Report refused: {error}") : body;
    }

    private static async Task<T> ReadAsync<T>(
        HttpResponseMessage response,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken
    )
    {
        if (!response.IsSuccessStatusCode)
        {
            string text = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}: {text}");
        }
        return await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken)
            ?? throw new HttpRequestException("Empty response body.");
    }

    internal sealed record LeaseEnvelope(LeaseDto? Lease, bool Done, string? Reason, string? Error);

    internal sealed record LeaseDto(
        string PartyId,
        string Jaml,
        int BatchChars,
        long StartBlock,
        long BlockCount,
        long TotalBlocks,
        string WorkerToken
    );

    internal sealed record ReportRequest(
        string PartyId,
        string WorkerToken,
        long StartBlock,
        string[] Seeds,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? HeartbeatOnly
    );

    internal sealed record ReportResponse(bool Ok, int Confirmed, int Rejected, int Recorded, string? Error);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HttpPartyCoordinator.LeaseEnvelope))]
[JsonSerializable(typeof(HttpPartyCoordinator.ReportRequest))]
[JsonSerializable(typeof(HttpPartyCoordinator.ReportResponse))]
internal sealed partial class PartyJsonContext : JsonSerializerContext;
