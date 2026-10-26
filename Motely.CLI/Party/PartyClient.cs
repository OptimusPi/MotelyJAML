using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Motely.CLI.Party;

/// <summary>A coordinator answer that was not a 2xx: the status and the server's error text.</summary>
public sealed class PartyServerException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>
/// The HTTP half of the Search Party worker: lease, heartbeat and report calls against a
/// seedfinder.app coordinator. The <see cref="HttpClient"/> is the caller's, so tests can hand
/// in one over a fake handler.
/// </summary>
public sealed class PartyClient
{
    private readonly HttpClient _http;
    private readonly string _base;

    public PartyClient(HttpClient http, Uri server)
    {
        _http = http;
        // "https://host" and "https://host/sub/" both work: routes are appended, not resolved.
        _base = server.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    public Uri NextUri(string partyId) => new($"{_base}/api/party/next?partyId={Uri.EscapeDataString(partyId)}");

    public Uri ReportUri => new($"{_base}/api/party/report");

    public async Task<PartyNextResponse> NextAsync(string partyId, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(NextUri(partyId), cancellationToken).ConfigureAwait(false);
        return await ReadAsync(response, PartyJsonContext.Default.PartyNextResponse, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Extends the lease's TTL. False when the server no longer holds it for this token.</summary>
    public async Task<bool> HeartbeatAsync(PartyLease lease, CancellationToken cancellationToken)
    {
        var body = new PartyReportRequest
        {
            PartyId = lease.PartyId,
            WorkerToken = lease.WorkerToken,
            StartBlock = lease.StartBlock,
            HeartbeatOnly = true,
        };
        var answer = await PostAsync(body, cancellationToken).ConfigureAwait(false);
        return answer.Ok == true;
    }

    /// <summary>The one real report that completes a lease (zero seeds still completes it).</summary>
    public Task<PartyReportResponse> ReportAsync(
        PartyLease lease,
        IReadOnlyList<string> seeds,
        CancellationToken cancellationToken
    ) =>
        PostAsync(
            new PartyReportRequest
            {
                PartyId = lease.PartyId,
                WorkerToken = lease.WorkerToken,
                StartBlock = lease.StartBlock,
                Seeds = seeds,
            },
            cancellationToken
        );

    private async Task<PartyReportResponse> PostAsync(PartyReportRequest body, CancellationToken cancellationToken)
    {
        using var response = await _http
            .PostAsJsonAsync(ReportUri, body, PartyJsonContext.Default.PartyReportRequest, cancellationToken)
            .ConfigureAwait(false);
        return await ReadAsync(response, PartyJsonContext.Default.PartyReportResponse, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<T> ReadAsync<T>(
        HttpResponseMessage response,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken
    )
        where T : class
    {
        string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        T? value = null;
        try
        {
            value = text.Length > 0 ? JsonSerializer.Deserialize(text, typeInfo) : null;
        }
        catch (JsonException) when (!response.IsSuccessStatusCode)
        {
            // An HTML error page or a proxy's text: report the status below.
        }

        if (!response.IsSuccessStatusCode)
        {
            string? error = value switch
            {
                PartyNextResponse next => next.Error,
                PartyReportResponse report => report.Error,
                _ => null,
            };
            throw new PartyServerException(
                (int)response.StatusCode,
                $"HTTP {(int)response.StatusCode}: {error ?? Shorten(text)}"
            );
        }
        return value ?? throw new PartyServerException((int)response.StatusCode, "empty response body");
    }

    private static string Shorten(string text)
    {
        text = text.ReplaceLineEndings(" ").Trim();
        return text.Length == 0 ? "(no body)" : text.Length > 200 ? text[..200] + "..." : text;
    }
}
