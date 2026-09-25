using System.Net.Http.Json;

namespace Motely.DistributedWorker;

internal sealed class PartyClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public PartyClient(string serverUrl)
    {
        _baseUrl = serverUrl.Trim().TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<PartyLeaseEnvelopeDto> LeaseNextAsync(string partyId, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/api/party/next?partyId={Uri.EscapeDataString(partyId)}";
        var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var errorBody = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Lease failed with {(int)resp.StatusCode} {resp.ReasonPhrase}: {errorBody}");
        }
        var result = await resp.Content.ReadFromJsonAsync(WorkerJsonContext.Default.PartyLeaseEnvelopeDto, ct);
        return result ?? throw new InvalidOperationException("Null lease response");
    }

    public async Task<PartyReportResponseDto> ReportAsync(PartyReportRequestDto report, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync(
            $"{_baseUrl}/api/party/report", report, WorkerJsonContext.Default.PartyReportRequestDto, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var errorBody = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Report failed with {(int)resp.StatusCode} {resp.ReasonPhrase}: {errorBody}");
        }
        var result = await resp.Content.ReadFromJsonAsync(WorkerJsonContext.Default.PartyReportResponseDto, ct);
        return result ?? throw new InvalidOperationException("Null report response");
    }

    public void Dispose() => _http.Dispose();
}
