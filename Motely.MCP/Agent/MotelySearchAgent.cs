using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AGUI.Abstractions;
using Microsoft.Extensions.AI;
using Motely.MCP.Search;

namespace Motely.MCP.Agent;

/// <summary>
/// The deterministic <see cref="IChatClient"/> behind the AG-UI <c>/agui</c> endpoint. It reads a
/// request like "search perkeo" (optionally "keyword joker", "random 500", "sequential"), runs the
/// real engine through a <see cref="SearchRunner"/>, and streams the run as AG-UI events: text for
/// narration, <see cref="StateSnapshotEvent"/> for live progress, and the matched seeds as a live
/// feed. No model in the loop, so a given request always produces the same run — which makes the
/// endpoint testable end to end.
/// </summary>
public sealed class MotelySearchAgent : IChatClient
{
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var text = messages.LastOrDefault(static m => m.Role == ChatRole.User)?.Text ?? "";
        var (filter, mode, parseError) = ParseMessage(text);

        if (parseError is not null)
        {
            yield return Text(parseError);
            yield break;
        }
        if (filter is null)
        {
            yield return Text("Tell me which JAML filter to search, e.g. \"search perkeo\".");
            yield break;
        }

        yield return Text($"Searching {filter} ({mode ?? "replay"})…");

        var runner = new SearchRunner();

        await foreach (var evt in runner.RunAsync(filter, mode, null, cancellationToken).ConfigureAwait(false))
        {
            switch (evt)
            {
                case SearchEvent.Progress p:
                    // Live progress as a state snapshot the UI can render as a progress widget.
                    var state = JsonSerializer.SerializeToElement(
                        new
                        {
                            percent = p.Percent,
                            searched = p.Searched,
                            matched = p.Matched,
                            rate = p.Rate
                        }
                    );
                    yield return new ChatResponseUpdate
                    {
                        RawRepresentation = new StateSnapshotEvent { Snapshot = state }
                    };
                    break;

                case SearchEvent.Match m:
                    yield return Text(m.Seed);
                    break;

                case SearchEvent.Done d:
                    yield return Text(
                        $"Found {d.Matched:N0} matching seeds out of {d.TotalSearched:N0} searched "
                            + $"in {FormatDuration(d.Elapsed)} ({d.Rate:N0} seeds/s)."
                    );
                    break;

                case SearchEvent.Error e:
                    yield return Text($"Error: {e.Message}");
                    break;
            }
        }
    }

    public void Dispose() { }

    /// <summary>The non-streaming shape of the same run: every streamed text update assembled into one response.</summary>
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var sb = new StringBuilder();
        await foreach (
            var update in GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false)
        )
        {
            foreach (var content in update.Contents)
            {
                if (content is TextContent textContent)
                    sb.Append(textContent.Text);
            }
        }
        return new ChatResponse([new ChatMessage(ChatRole.Assistant, sb.ToString())]);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    private static ChatResponseUpdate Text(string text) => new() { Contents = [new TextContent(text)] };

    /// <summary>Pulls a filter name and an optional mode out of the user's message.</summary>
    private static (string? filter, string? mode, string? error) ParseMessage(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return (null, null, null);

        string? mode = null;
        for (int i = 1; i < parts.Length; i++)
        {
            if (parts[i].Equals("keyword", StringComparison.OrdinalIgnoreCase) && i + 1 < parts.Length)
                mode = "keyword " + parts[i + 1];
            else if (parts[i].Equals("random", StringComparison.OrdinalIgnoreCase) && i + 1 < parts.Length)
                mode = "random " + parts[i + 1];
            else if (parts[i].Equals("replay", StringComparison.OrdinalIgnoreCase))
                mode = "replay";
            else if (parts[i].Equals("sequential", StringComparison.OrdinalIgnoreCase))
                mode = "sequential";
        }

        return (parts[0], mode, null);
    }

    private static string FormatDuration(TimeSpan span) =>
        span.TotalHours >= 24
            ? $"{span.Days} {(span.Days == 1 ? "day" : "days")} {span.Hours} {(span.Hours == 1 ? "hour" : "hours")}"
            : span.ToString(@"hh\:mm\:ss\.f");
}
