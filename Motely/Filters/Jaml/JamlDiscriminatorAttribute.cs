namespace Motely.Filters.Jaml;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class JamlDiscriminatorAttribute : Attribute
{
    public string[] Wires { get; }
    public Type? ValueEnum { get; init; }
    public Type? SourceConfigType { get; init; }
    public bool RollsAreInlineValue { get; init; }
    public int[]? RollsDefault { get; init; }

    public JamlDiscriminatorAttribute(params string[] wires) => Wires = wires;
}
