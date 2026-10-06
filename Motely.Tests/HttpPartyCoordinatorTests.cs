using System.Net;
using System.Text;
using System.Text.Json;
using Motely.CLI;
using Motely.Party;

namespace Motely.Tests;

/// <summary>
/// The CLI's HTTP transport for the Search Party protocol, against canned seedfinder.app
/// responses: the wire field names, the lease/done/error envelopes, and that a heartbeat is a
/// report with <c>heartbeatOnly: true</c> while a final report carries no such field.
/// </summary>
public class HttpPartyCoordinatorTests
{
    private sealed class FakeServer(Func<HttpRequestMessage, string?, (HttpStatusCode, string)> respond)
        : HttpMessageHandler
    {
        public readonly List<(string Method, string Url, string? Body)> Requests = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method.Method, request.RequestUri!.AbsoluteUri, body));
            var (status, json) = respond(request, body);
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private static (HttpPartyCoordinator Coordinator, FakeServer Server) Connect(Func<HttpRequestMessage, string?, (HttpStatusCode, string)> respond)
    {
        var server = new FakeServer(respond);
        return (new HttpPartyCoordinator(new HttpClient(server), "https://party.test/"), server);
    }

    private static readonly PartyLease SomeLease = new("p 1", "name: x", 4, 10, 2, 1_500_625, "tok");

    [Fact]
    public async Task NextParsesALease()
    {
        var (coordinator, server) = Connect((_, _) => (HttpStatusCode.OK, """
            {"lease":{"partyId":"p 1","jaml":"name: x","deck":"Red","stake":"White","batchChars":4,
             "startBlock":10,"blockCount":2,"totalBlocks":1500625,"workerToken":"tok","expiresAt":"2026-10-02T00:00:00Z"}}
            """));

        var next = await coordinator.NextAsync("p 1", CancellationToken.None);

        Assert.Equal(SomeLease, next.Lease);
        Assert.Equal("https://party.test/api/party/next?partyId=p%201", Assert.Single(server.Requests).Url);
    }

    [Fact]
    public async Task NextReportsASettledParty()
    {
        var (coordinator, _) = Connect((_, _) => (HttpStatusCode.OK, """{"done":true,"reason":"exhausted"}"""));

        var next = await coordinator.NextAsync("p", CancellationToken.None);

        Assert.Null(next.Lease);
        Assert.Equal("exhausted", next.Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"error":"unknown party"}""")]
    [InlineData(HttpStatusCode.InternalServerError, "oops")]
    public async Task NextThrowsOnAnErrorOrAFailedStatus(HttpStatusCode status, string body)
    {
        var (coordinator, _) = Connect((_, _) => (status, body));

        await Assert.ThrowsAsync<HttpRequestException>(() => coordinator.NextAsync("p", CancellationToken.None));
    }

    [Fact]
    public async Task AReportPostsTheSeedsAndReadsTheVerdict()
    {
        var (coordinator, server) = Connect((_, _) => (HttpStatusCode.OK, """{"confirmed":2,"rejected":1,"recorded":2}"""));

        var result = await coordinator.ReportAsync(SomeLease, ["ABCDEFGH", "6GR89UQF", "ZZZZZZZZ"], CancellationToken.None);

        Assert.Equal(new PartyReportResult(2, 1), result);
        var (method, url, body) = Assert.Single(server.Requests);
        Assert.Equal("POST", method);
        Assert.Equal("https://party.test/api/party/report", url);
        using var json = JsonDocument.Parse(body!);
        var root = json.RootElement;
        Assert.Equal("p 1", root.GetProperty("partyId").GetString());
        Assert.Equal("tok", root.GetProperty("workerToken").GetString());
        Assert.Equal(10, root.GetProperty("startBlock").GetInt64());
        Assert.Equal(["ABCDEFGH", "6GR89UQF", "ZZZZZZZZ"], root.GetProperty("seeds").EnumerateArray().Select(e => e.GetString()));
        Assert.False(root.TryGetProperty("heartbeatOnly", out _), "a final report must not be read as a heartbeat");
    }

    [Fact]
    public async Task AHeartbeatIsAnEmptyReportFlaggedHeartbeatOnly()
    {
        var (coordinator, server) = Connect((_, _) => (HttpStatusCode.OK, """{"ok":true}"""));

        await coordinator.HeartbeatAsync(SomeLease, CancellationToken.None);

        using var json = JsonDocument.Parse(Assert.Single(server.Requests).Body!);
        Assert.True(json.RootElement.GetProperty("heartbeatOnly").GetBoolean());
        Assert.Equal(0, json.RootElement.GetProperty("seeds").GetArrayLength());
    }
}
