using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;
using AGUI.Abstractions;
using AGUI.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Motely.MCP.Agui;

/// <summary>
/// The AG-UI endpoint. <see cref="AddAGUI"/> configures the JSON serializer with the AG-UI
/// metadata (so <see cref="BaseEvent"/> keeps its wire discriminator); <see cref="MapAGUI"/> maps
/// the canonical SSE endpoint backed by an <see cref="IChatClient"/>.
/// </summary>
internal static class AguiEndpoint
{
    public static IServiceCollection AddAGUI(this IServiceCollection services)
    {
        services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
        {
            // The AG-UI metadata is source-generated; combine it with the default resolver so the
            // framework's own types (SseItem<>) still serialize and BaseEvent keeps its `type`.
            options.SerializerOptions.TypeInfoResolver = JsonTypeInfoResolver.Combine(
                new DefaultJsonTypeInfoResolver(),
                AGUIJsonSerializerContext.Default
            );
        });
        return services;
    }

    public static IEndpointConventionBuilder MapAGUI(this IEndpointRouteBuilder endpoints, string pattern)
    {
        return endpoints.MapPost(
            pattern,
            (
                [FromBody] RunAgentInput input,
                [FromServices] IChatClient chatClient,
                [FromServices] IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions,
                CancellationToken cancellationToken
            ) =>
            {
                var jsonSerializerOptions = jsonOptions.Value.SerializerOptions;
                var ctx = input.ToChatRequestContext(jsonSerializerOptions);
                var updates = chatClient.GetStreamingResponseAsync(
                    ctx.Messages,
                    ctx.ChatOptions,
                    cancellationToken
                );
                var events = updates.AsAGUIEventStreamAsync(ctx, cancellationToken);
                return TypedResults.ServerSentEvents(WrapAsSseItems(events, cancellationToken));
            }
        );
    }

    private static async IAsyncEnumerable<SseItem<BaseEvent>> WrapAsSseItems(
        IAsyncEnumerable<BaseEvent> events,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        await foreach (var evt in events.WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return new SseItem<BaseEvent>(evt);
    }
}
