using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Motely.MCP.Tools;

/// <summary>
/// Turns a tool exception into an <c>isError</c> tool result that carries the exception's message:
/// the SDK's default handler swallows it into a generic "An error occurred invoking ..." line, and
/// MCP's contract asks tool errors to be corrective — name the problem, point at the fix.
/// </summary>
internal static class ToolErrorFilter
{
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next
    ) =>
        async (request, cancellationToken) =>
        {
            try
            {
                return await next(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new CallToolResult
                {
                    IsError = true,
                    Content = [new TextContentBlock { Text = ex.Message }],
                };
            }
        };
}
