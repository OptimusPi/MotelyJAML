using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Motely.MCP;

/// <summary>
/// <c>--port N</c>: the same server over Streamable HTTP at <c>http://localhost:N/mcp</c>, for hosts
/// that can't spawn a stdio child. Aspire proxies it to coding agents. Built on the SDK's own
/// <see cref="StreamableHttpServerTransport"/> and the BCL's <see cref="HttpListener"/>, so the
/// server carries no ASP.NET Core. Stateless: each POST gets a fresh server, nothing outlives a
/// request, and GET (the server-initiated stream) is refused, which the spec allows. Loopback only
/// and no auth: a connection from any other address gets 401, and a browser page from elsewhere
/// (DNS rebinding) gets 403 on its Origin. <c>/health</c> answers 200 for health checks.
/// </summary>
internal static class McpHttp
{
    private static readonly JsonTypeInfo<JsonRpcMessage> MessageType =
        (JsonTypeInfo<JsonRpcMessage>)McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcMessage));

    public static async Task RunAsync(int port, Func<McpServerOptions> createOptions, CancellationToken cancellationToken)
    {
        using var listener = new HttpListener();
        // http.sys matches the Host header's name, not its port, so a proxied request still lands.
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        Console.Error.WriteLine($"motely mcp: http://localhost:{port}/mcp");

        using var stop = cancellationToken.Register(listener.Stop);
        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            _ = HandleAsync(context, createOptions, cancellationToken);
        }
    }

    private static async Task HandleAsync(
        HttpListenerContext context,
        Func<McpServerOptions> createOptions,
        CancellationToken cancellationToken
    )
    {
        var response = context.Response;
        try
        {
            var path = context.Request.Url?.AbsolutePath;
            // The prefixes name localhost, but http.sys routes on the Host header: a request from
            // another machine carrying "Host: localhost" would still arrive here.
            if (!IPAddress.IsLoopback(context.Request.RemoteEndPoint.Address))
                response.StatusCode = 401;
            else if (!IsLoopbackOrigin(context.Request.Headers["Origin"]))
                response.StatusCode = 403;
            else if (path == "/health")
                await WriteAsync(response, 200, "text/plain", "Healthy");
            else if (path != "/mcp")
                response.StatusCode = 404;
            else if (context.Request.HttpMethod != "POST")
            {
                response.AddHeader("Allow", "POST");
                response.StatusCode = 405;
            }
            else
                await PostAsync(context, createOptions, cancellationToken);
        }
        // The client hung up mid-response, or the server is stopping.
        catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException or OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"motely mcp: {ex}");
            TrySetStatus(response, 500);
        }
        finally
        {
            try
            {
                response.Close();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { }
        }
    }

    private static async Task PostAsync(
        HttpListenerContext context,
        Func<McpServerOptions> createOptions,
        CancellationToken cancellationToken
    )
    {
        var response = context.Response;
        JsonRpcMessage? message;
        try
        {
            message = await JsonSerializer.DeserializeAsync(context.Request.InputStream, MessageType, cancellationToken);
        }
        catch (JsonException ex)
        {
            await WriteAsync(response, 400, "text/plain", $"Not a JSON-RPC message: {ex.Message}");
            return;
        }
        if (message is null)
        {
            await WriteAsync(response, 400, "text/plain", "Empty body.");
            return;
        }

        // Only a request gets a response stream; notifications and responses get 202 Accepted.
        if (message is JsonRpcRequest)
        {
            response.ContentType = "text/event-stream";
            response.AddHeader("Cache-Control", "no-cache, no-store");
            response.SendChunked = true;
        }

        await using var transport = new StreamableHttpServerTransport { Stateless = true };
        await using var server = McpServer.Create(transport, createOptions());
        using var serving = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var running = server.RunAsync(serving.Token);
        try
        {
            if (!await transport.HandlePostRequestAsync(message, response.OutputStream, cancellationToken))
                response.StatusCode = 202;
        }
        finally
        {
            await serving.CancelAsync();
            try
            {
                await running;
            }
            catch (OperationCanceledException) { }
        }
    }

    /// <summary>No Origin (every non-browser client) or a loopback one.</summary>
    private static bool IsLoopbackOrigin(string? origin) =>
        string.IsNullOrEmpty(origin)
        || (Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback);

    private static async Task WriteAsync(HttpListenerResponse response, int status, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        response.StatusCode = status;
        response.ContentType = contentType;
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
    }

    private static void TrySetStatus(HttpListenerResponse response, int status)
    {
        try
        {
            response.StatusCode = status;
        }
        // Headers already went out; the client sees the stream end instead.
        catch (InvalidOperationException) { }
    }
}
