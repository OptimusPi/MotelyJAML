using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Motely.Tests;

/// <summary>
/// The Aspire composition in <c>Motely.AppHost</c>, built in-process but never started: no DCP, no
/// Docker, no ports. What is pinned is the resource set, the configuration helper-api and the
/// worker actually receive, and the start policy — the things that decide whether
/// <c>aspire run</c> does what the AppHost comments say it does.
/// </summary>
public class AppHostModelTests
{
    private static async Task<DistributedApplication> BuildAppHostAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Motely_AppHost>();
        return await appHost.BuildAsync();
    }

    private static DistributedApplicationModel Model(DistributedApplication app) =>
        app.Services.GetRequiredService<DistributedApplicationModel>();

    /// <summary>The env and args a resource would be launched with in run mode.</summary>
    private static async Task<IExecutionConfigurationResult> LaunchConfigOf(DistributedApplication app, IResource resource)
    {
        var context = app.Services.GetRequiredService<DistributedApplicationExecutionContext>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(AppHostModelTests));
        var result = await ExecutionConfigurationBuilder.Create(resource)
            .WithEnvironmentVariablesConfig()
            .WithArgumentsConfig()
            .BuildAsync(context, logger, CancellationToken.None);
        Assert.Null(result.Exception);
        return result;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Motely.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Motely.slnx not found above the test output.");
    }

    [Fact]
    public async Task Composes_helper_api_and_worker()
    {
        await using var app = await BuildAppHostAsync();
        var names = Model(app).Resources.Select(r => r.Name).ToHashSet();

        Assert.Contains("helper-api", names);
        Assert.Contains("distributed-worker", names);
        Assert.DoesNotContain("postgres", names);
    }

    [Fact]
    public async Task Helper_api_gets_repo_paths_an_idle_pool_and_a_health_check()
    {
        await using var app = await BuildAppHostAsync();
        var api = Model(app).Resources.OfType<ProjectResource>().Single(r => r.Name == "helper-api");

        var env = (await LaunchConfigOf(app, api)).EnvironmentVariables.ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.Equal("", env["Pool__Url"]);
        Assert.Equal(Path.Combine(RepoRoot(), "JamlFilters"), env["MOTELY_FILTERS_DIR"]);
        Assert.Equal(Path.Combine(RepoRoot(), "Seeds"), env["Pool__LocalDbPath"]);
        Assert.True(Directory.Exists(env["MOTELY_FILTERS_DIR"]), "MOTELY_FILTERS_DIR must point at the repo's JamlFilters/");

        Assert.NotEmpty(api.Annotations.OfType<HealthCheckAnnotation>());
        // The one http endpoint comes from the launch profile (3141); an explicit WithHttpEndpoint in the
        // AppHost would add a second "http" and collide with it.
        var http = Assert.Single(api.Annotations.OfType<EndpointAnnotation>(), e => e.Name == "http");
        Assert.Equal(3141, http.Port);
    }

    [Fact]
    public async Task Worker_is_explicit_start_in_party_mode_and_not_wired_to_helper_api()
    {
        await using var app = await BuildAppHostAsync();
        var worker = Model(app).Resources.OfType<ProjectResource>().Single(r => r.Name == "distributed-worker");

        Assert.NotEmpty(worker.Annotations.OfType<ExplicitStartupAnnotation>());

        var args = (await LaunchConfigOf(app, worker)).Arguments.Select(a => a.Value).ToList();
        Assert.Equal("--party", args[0]);
        Assert.Contains("--server", args);
        Assert.Contains("https://www.seedfinder.app", args);
        Assert.Contains(Path.Combine(RepoRoot(), "Seeds"), args);
        Assert.DoesNotContain("--pool", args);
        Assert.DoesNotContain(worker.Annotations.OfType<ResourceRelationshipAnnotation>(), r => r.Resource.Name == "helper-api");
    }

    [Fact]
    public async Task Jaml_ui_is_present_exactly_when_the_sibling_checkout_is()
    {
        await using var app = await BuildAppHostAsync();
        var present = Model(app).Resources.Any(r => r.Name == "jaml-ui");
        var sibling = Path.GetFullPath(Path.Combine(RepoRoot(), "..", "jaml-ui"));

        Assert.Equal(Directory.Exists(sibling), present);
        if (!present) return;

        var ui = Model(app).Resources.Single(r => r.Name == "jaml-ui");
        var endpoint = Assert.Single(ui.Annotations.OfType<EndpointAnnotation>());
        Assert.Equal(6006, endpoint.Port);
        Assert.False(endpoint.IsProxied, "Storybook binds the declared port itself; a proxy on 6006 would fight it.");
    }
}
