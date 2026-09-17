using Motely.Config;

var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "smoke-config.yaml");
if (!YamlConfigLoader.TryLoadFile(fixture, out var config, out var error))
{
    Console.Error.WriteLine(error);
    return 1;
}

Console.WriteLine($"ok id={config!.Id} must={config.Must.Count} should={config.Should.Count}");
return 0;
