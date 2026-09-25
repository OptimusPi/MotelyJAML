namespace Motely.Filters.Jaml;

public interface IJamlClause
{
    string? Label { get; set; }
    int Min { get; set; }
    int? Max { get; set; }
    int Score { get; set; }
}

public interface IAnteScopedClause : IJamlClause
{
    int[] Antes { get; set; }
}

public interface IRollScopedClause : IJamlClause
{
    int[] Rolls { get; set; }
}

public interface IWithScopedClause : IJamlClause
{
    JamlWith With { get; set; }
}

public static class JamlClause
{
    public static readonly string[] WithBlockKeys = ["luck"];
}
