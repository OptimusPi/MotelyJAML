using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Configuration;

namespace Motely.Tests;

/// <summary>
/// The Aspire composition in <c>Motely.AppHost</c>, read from the model it builds and never
/// started: no DCP, no ports. What is pinned is what the AppHost's header comment promises: which
/// resources exist, what each runs, where filters come from, and which one waits for a person.
/// </summary>
public sealed class AppHostModelTests
{
    private static Task<IDistributedApplicationTestingBuilder> CreateAsync() =>
        DistributedApplicationTestingBuilder.CreateAsync<Projects.Motely_AppHost>();

    private static string RepoFilters()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Motely.slnx")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("Motely.slnx not found above the test output."), "YamlFilters");
    }

    /// <summary>The arguments as the AppHost declared them: strings, parameters and endpoint expressions, unresolved.</summary>
    private static async Task<List<object>> ArgsOf(IResource resource)
    {
        List<object> args = [];
        var context = new CommandLineArgsCallbackContext(args, resource);
        foreach (var annotation in resource.Annotations.OfType<CommandLineArgsCallbackAnnotation>())
            await annotation.Callback(context);
        return args;
    }

    /// <summary>A value the AppHost set as a plain environment variable (not one Aspire computes).</summary>
    private static async Task<object?> EnvOf(IResource resource, string name)
    {
        Dictionary<string, object> env = [];
        var context = new EnvironmentCallbackContext(new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run), resource, env);
        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            try
            {
                await annotation.Callback(context);
            }
            // Aspire's own callbacks (OTLP, service discovery) want a running app's services.
            catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException) { }
        }
        return env.GetValueOrDefault(name);
    }

    [Fact]
    public async Task Composes_check_mcp_and_search_from_the_cli()
    {
        await using var appHost = await CreateAsync();
        var projects = appHost.Resources.OfType<ProjectResource>().Select(static r => r.Name).Order().ToArray();

        Assert.Equal(["check", "mcp", "search"], projects);
        Assert.Contains(appHost.Resources, static r => r is ParameterResource { Name: "filter" });
        // No Neon string in the test configuration, so this is the container branch.
        Assert.Contains(appHost.Resources, static r => r is IResourceWithConnectionString { Name: "db" });
    }

    [Fact]
    public async Task Check_runs_motely_check_over_the_repo_filters_at_startup()
    {
        await using var appHost = await CreateAsync();
        var check = appHost.Resources.Single(static r => r.Name == "check");

        Assert.Equal(["check", RepoFilters()], await ArgsOf(check));
        Assert.True(Directory.Exists(RepoFilters()));
        Assert.Empty(check.Annotations.OfType<ExplicitStartupAnnotation>());
    }

    [Fact]
    public async Task Mcp_serves_http_on_its_target_port_with_a_health_check_and_an_mcp_annotation()
    {
        await using var appHost = await CreateAsync();
        var mcp = appHost.Resources.Single(static r => r.Name == "mcp");

        var args = await ArgsOf(mcp);
        Assert.Equal("--port", args[0]);
        // The server binds the target port; the proxy in front of it owns the public one.
        var port = Assert.IsType<EndpointReferenceExpression>(args[1]);
        Assert.Equal(EndpointProperty.TargetPort, port.Property);
        Assert.Equal("http", port.Endpoint.EndpointName);

        var http = Assert.Single(mcp.Annotations.OfType<EndpointAnnotation>());
        Assert.Equal("http", http.UriScheme);
        Assert.NotEmpty(mcp.Annotations.OfType<HealthCheckAnnotation>());
#pragma warning disable ASPIREMCP001 // Resource MCP servers are experimental.
        var server = Assert.Single(mcp.Annotations.OfType<McpServerEndpointAnnotation>());
#pragma warning restore ASPIREMCP001
        Assert.NotNull(server);
        Assert.Equal(RepoFilters(), await EnvOf(mcp, "MOTELY_FILTERS_DIR"));
        // The seed database reaches it the Aspire way: ConnectionStrings__db from the db resource.
        Assert.NotNull(await EnvOf(mcp, "ConnectionStrings__db"));
    }

    [Fact]
    public async Task Search_waits_to_be_started_and_runs_the_filter_parameter_quietly()
    {
        await using var appHost = await CreateAsync();
        var search = appHost.Resources.Single(static r => r.Name == "search");

        Assert.NotEmpty(search.Annotations.OfType<ExplicitStartupAnnotation>());
        var args = await ArgsOf(search);
        Assert.Equal(3, args.Count);
        Assert.Equal("--yaml", args[0]);
        // WithArgs keeps the builder it was given; the parameter is inside it.
        Assert.Equal("filter", Assert.IsAssignableFrom<IResourceBuilder<ParameterResource>>(args[1]).Resource.Name);
        Assert.Equal("--quiet", args[2]);
        Assert.Equal(RepoFilters(), await EnvOf(search, "MOTELY_FILTERS_DIR"));
    }

    [Fact]
    public async Task The_default_filter_parameter_names_a_filter_that_loads()
    {
        await using var appHost = await CreateAsync();
        var name = appHost.Configuration["Parameters:filter"];

        Assert.False(string.IsNullOrWhiteSpace(name));
        var path = Path.Combine(RepoFilters(), name + ".yaml");
        Assert.True(Filters.YamlConfigLoader.TryLoad(File.ReadAllText(path), out _, out var error), error);
    }
}
