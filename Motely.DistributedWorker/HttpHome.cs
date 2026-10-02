using System.Net;
using System.Net.Http.Json;
using Motely.Distributed;

namespace Motely.DistributedWorker;

/// <summary>MotelyHome over HTTP: <c>GET /claim?worker=</c> (204 when idle) and <c>POST /done</c>.</summary>
internal sealed class HttpHome(HttpClient http, string url) : IHome
{
    private readonly string _baseUrl = url.Trim().TrimEnd('/');

    public async Task<Work?> ClaimAsync(string worker, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync($"{_baseUrl}/claim?worker={Uri.EscapeDataString(worker)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(HomeJson.Default.Work, cancellationToken)
            ?? throw new HttpRequestException("Empty claim.");
    }

    public async Task DoneAsync(WorkDone done, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync($"{_baseUrl}/done", done, HomeJson.Default.WorkDone, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
