using Motely.Filters.Jaml;

public sealed class JamlModule : IJaml
{
    public string? Check(string text) => JamlConfigLoader.TryLoad(text, out _, out var error) ? null : error;
}
