using System.Text;
using System.Text.Json.Nodes;

namespace Motely.Lsp;

/// <summary>
/// Content-Length framed JSON-RPC 2.0 over a stream pair — the entire LSP transport.
/// Reading and writing are the only jobs here; what the messages mean lives in
/// <see cref="LspServer"/>, and tests drive this over in-memory streams.
/// </summary>
public sealed class JsonRpcChannel(Stream input, Stream output)
{
    private readonly object _writeLock = new();

    /// <summary>Read one framed message, or null when the peer closed the stream.</summary>
    public JsonNode? Read()
    {
        var contentLength = -1;
        while (true)
        {
            var line = ReadHeaderLine();
            if (line is null)
                return null;
            if (line.Length == 0)
                break;
            const string Prefix = "Content-Length:";
            if (line.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                contentLength = int.Parse(line[Prefix.Length..].Trim());
        }
        if (contentLength < 0)
            throw new InvalidDataException("JSON-RPC frame arrived without Content-Length.");

        var buffer = new byte[contentLength];
        var read = 0;
        while (read < contentLength)
        {
            var chunk = input.Read(buffer, read, contentLength - read);
            if (chunk == 0)
                return null;
            read += chunk;
        }
        return JsonNode.Parse(buffer);
    }

    /// <summary>Write one framed message. Safe from any thread.</summary>
    public void Write(JsonNode message)
    {
        var payload = Encoding.UTF8.GetBytes(message.ToJsonString());
        var header = Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");
        lock (_writeLock)
        {
            output.Write(header);
            output.Write(payload);
            output.Flush();
        }
    }

    private string? ReadHeaderLine()
    {
        var bytes = new List<byte>(64);
        while (true)
        {
            var b = input.ReadByte();
            if (b < 0)
                return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());
            if (b == '\n')
            {
                if (bytes.Count > 0 && bytes[^1] == '\r')
                    bytes.RemoveAt(bytes.Count - 1);
                return Encoding.ASCII.GetString(bytes.ToArray());
            }
            bytes.Add((byte)b);
        }
    }
}
