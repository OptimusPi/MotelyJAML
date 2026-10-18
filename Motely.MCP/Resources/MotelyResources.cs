using System.ComponentModel;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Server;

namespace Motely.MCP.Resources;

/// <summary>
/// The MCP Apps UI resource: the HTML the host renders inside the tool call when a client supports
/// MCP Apps. The full agent panel lives in Motely.Web; this is the in-host surface.
/// </summary>
[McpServerResourceType]
public sealed class MotelyResources
{
    [McpServerResource(
        UriTemplate = "ui://motely/search",
        Name = "motely-search-ui",
        MimeType = McpApps.HtmlMimeType
    )]
    [Description("Motely search agent panel: pick a filter, run it, watch the SIMD search stream.")]
    public static string SearchUI() =>
        """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <title>Motely Search</title>
          <style>
            :root { color-scheme: dark; }
            body { font-family: ui-sans-serif, system-ui, sans-serif; margin: 2rem; background: #0b0d12; color: #e6e8ee; }
            h1 { font-size: 1.25rem; margin: 0 0 1rem; }
            .card { background: #151924; border: 1px solid #232a3a; border-radius: 10px; padding: 1rem; max-width: 40rem; }
            label { display: block; margin: .5rem 0 .25rem; font-size: .85rem; color: #9aa4b8; }
            input, select { width: 100%; padding: .5rem; border-radius: 6px; border: 1px solid #2a3346; background: #0f131c; color: inherit; }
            button { margin-top: .75rem; padding: .5rem 1rem; border: 0; border-radius: 6px; background: #4f7cff; color: white; cursor: pointer; }
            #log { margin-top: 1rem; font-family: ui-monospace, monospace; font-size: .8rem; white-space: pre-wrap; color: #9aa4b8; }
          </style>
        </head>
        <body>
          <h1>Motely Search</h1>
          <div class="card">
            <label for="filter">Filter</label>
            <input id="filter" placeholder="perkeo" />
            <label for="mode">Mode</label>
            <select id="mode">
              <option value="replay">replay (saved seeds)</option>
              <option value="keyword">keyword</option>
              <option value="random">random</option>
              <option value="sequential">sequential</option>
            </select>
            <button id="run">Run</button>
            <div id="log"></div>
          </div>
          <script>
            // The host drives the tool; this surface mirrors the run.
            document.getElementById('run').addEventListener('click', () => {
              const filter = document.getElementById('filter').value;
              const mode = document.getElementById('mode').value;
              document.getElementById('log').textContent = `search ${filter} (${mode})`;
            });
          </script>
        </body>
        </html>
        """;
}
