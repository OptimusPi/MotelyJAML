// Motely under Aspire. `aspire run` (or `dotnet run --project Motely.AppHost`) starts Motely's
// pieces behind one dashboard, with their console logs, traces and metrics:
//
//   check    `motely check` over JamlFilters/. Runs once: Finished when every filter loads, the
//            failures in its console log when one doesn't.
//   search   `motely --jaml <filter>`, started by hand from the dashboard or with
//            `aspire resource search start`. The `filter` parameter picks the filter; its progress
//            is on the Metrics page (motely.search.*) and its seeds are in the console log.
//   web      Motely.Web's Vite dev server: jaml-ui's SeedLab on the motely-wasm package (publish
//            Motely.Wasm first; Motely.Web links it with file:../Motely.Wasm).
//
// Resources run in their own project directory, not the repo root, so each one that takes a
// filter by name gets the repo's JamlFilters/ through MOTELY_FILTERS_DIR.

var builder = DistributedApplication.CreateBuilder(args);

var filters = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "JamlFilters"));

builder.AddProject<Projects.Motely_CLI>("check").WithArgs("check", filters);

// A bare name means JamlFilters/<name>.yaml; a path is used as given. The filter's own root
// `cutoff:` applies, as it does on the command line.
var filter = builder
    .AddParameter("filter")
    .WithDescription("The filter `search` runs: a name in JamlFilters/ (no extension) or a path to one.");

builder
    .AddProject<Projects.Motely_CLI>("search")
    // --quiet: the progress line goes to the Metrics page, not one log line every 800 ms.
    .WithArgs("--jaml", filter, "--quiet")
    .WithEnvironment("MOTELY_FILTERS_DIR", filters)
    .WithExplicitStart();

builder.AddViteApp("web", "../Motely.Web");

builder.Build().Run();
