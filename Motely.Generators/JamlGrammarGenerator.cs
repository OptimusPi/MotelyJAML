using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Motely.Generators;

/// <summary>
/// One grammar index: <c>[JamlDiscriminator]</c> wires + enum-typed properties on clause /
/// root / with shapes. Property type is the vocabulary fact — no hand kind→enum table.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class JamlGrammarGenerator : IIncrementalGenerator
{
    private const string AttributeFullName = "Motely.Filters.Jaml.JamlDiscriminatorAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var batches = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) => ReadBatch(ctx))
            .Where(static b => b.Entries.Length > 0 || b.Keys.Length > 0);

        var collected = batches.Collect()
            .Select(static (batches, _) =>
            {
                var entries = batches
                    .SelectMany(b => b.Entries)
                    .OrderBy(e => e.Normalized.First(), StringComparer.Ordinal)
                    .ToArray();
                // Last write wins only when enum types agree; conflicts reported at emit.
                var keys = batches
                    .SelectMany(b => b.Keys)
                    .OrderBy(k => k.Key, StringComparer.Ordinal)
                    .ThenBy(k => k.EnumTypeFull, StringComparer.Ordinal)
                    .ToArray();
                return new SchemaModel(
                    new EquatableArray<Entry>(entries),
                    new EquatableArray<KeyEnum>(keys));
            });

        context.RegisterSourceOutput(collected, static (spc, model) => Emit(spc, model));
    }

    private static Batch ReadBatch(GeneratorSyntaxContext ctx)
    {
        if (ctx.Node is not TypeDeclarationSyntax typeDecl)
            return Batch.Empty;
        if (ctx.SemanticModel.GetDeclaredSymbol(typeDecl) is not INamedTypeSymbol type)
            return Batch.Empty;

        var attrs = type.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == AttributeFullName)
            .ToArray();

        // Key→enum only from grammar shapes: disc clauses (keys ⊆ ClauseKeys), root, with.
        // Not every enum property in the assembly (MotelyItem.Type, rarity Jokers payloads, …).
        ImmutableArray<KeyEnum> keys;
        if (attrs.Length > 0)
            keys = ExtractEnumKeyProperties(type, allowedKeys: TryReadClauseKeyNames(type));
        else if (type.Name is "JamlConfig" or "JamlWith"
                 && type.ContainingNamespace?.ToDisplayString() == "Motely.Filters.Jaml")
            keys = ExtractEnumKeyProperties(type, allowedKeys: null);
        else
            keys = ImmutableArray<KeyEnum>.Empty;

        if (attrs.Length == 0)
            return new Batch(ImmutableArray<Entry>.Empty, keys);

        var clauseTypeFull = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var clauseKeysMember = FindClauseKeysForClause(type);
        var entryBuilder = ImmutableArray.CreateBuilder<Entry>(attrs.Length);

        foreach (var attr in attrs)
        {
            var wires = ExtractWires(attr);
            if (wires.Length == 0)
                continue;

            var normalized = wires.Select(Normalize).ToArray();

            var sourceConfigType = attr.NamedArguments
                .FirstOrDefault(a => a.Key == "SourceConfigType").Value;
            var sourceConfigFull = sourceConfigType.Kind == TypedConstantKind.Type
                ? ((INamedTypeSymbol?)sourceConfigType.Value)?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                : null;
            var sourceKeysMember = sourceConfigFull is not null
                ? FindSourceKeysMember(sourceConfigType)
                : null;

            var valueEnumType = attr.NamedArguments
                .FirstOrDefault(a => a.Key == "ValueEnum").Value;
            var valueEnumFull = valueEnumType.Kind == TypedConstantKind.Type
                ? ((INamedTypeSymbol?)valueEnumType.Value)?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                : null;

            var rollsInline = attr.NamedArguments
                .FirstOrDefault(a => a.Key == "RollsAreInlineValue").Value;
            bool rollsAreInlineValue = rollsInline.Kind == TypedConstantKind.Primitive
                && rollsInline.Value is true;

            var rollsDefault = attr.NamedArguments
                .FirstOrDefault(a => a.Key == "RollsDefault").Value;
            int[]? rollsDefaultArr = rollsDefault.Kind == TypedConstantKind.Array
                ? rollsDefault.Values.Select(v => (int)v.Value!).ToArray()
                : null;

            entryBuilder.Add(new Entry(
                wires, normalized, clauseTypeFull, clauseKeysMember,
                sourceConfigFull, sourceKeysMember,
                valueEnumFull, rollsAreInlineValue, rollsDefaultArr));
        }

        return new Batch(entryBuilder.ToImmutable(), keys);
    }

    /// <summary>
    /// Enum-typed instance properties are wire keys: <c>Edition</c> → <c>edition</c> →
    /// <c>typeof(MotelyItemEdition)</c>. When <paramref name="allowedKeys"/> is set (clause
    /// shapes), only properties whose name is a declared ClauseKeys entry are indexed —
    /// disc payloads like <c>Jokers</c> stay on ValueEnum, not here.
    /// </summary>
    private static ImmutableArray<KeyEnum> ExtractEnumKeyProperties(
        INamedTypeSymbol type,
        HashSet<string>? allowedKeys)
    {
        var builder = ImmutableArray.CreateBuilder<KeyEnum>();
        foreach (var member in type.GetMembers())
        {
            if (member is not IPropertySymbol { IsStatic: false, GetMethod: not null } prop)
                continue;
            if (prop.DeclaredAccessibility is not Accessibility.Public)
                continue;
            if (UnwrapEnum(prop.Type) is not { } enumType)
                continue;

            var baseKey = Normalize(prop.Name);
            if (allowedKeys is not null && !allowedKeys.Contains(baseKey))
                continue;

            var enumFull = enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var label = prop.Name;

            foreach (var key in KeyAliases(baseKey))
                builder.Add(new KeyEnum(key, enumFull, label));
        }
        return builder.ToImmutable();
    }

    /// <summary>Read FilterDesc.ClauseKeys string literals (or a reference to another desc's keys).</summary>
    private static HashSet<string>? TryReadClauseKeyNames(INamedTypeSymbol clauseType)
    {
        var desc = FindFilterDescType(clauseType);
        if (desc is null)
            return null;
        var raw = TryReadStringArrayMember(desc, "ClauseKeys", new HashSet<ISymbol>(SymbolEqualityComparer.Default));
        if (raw is null || raw.Count == 0)
            return null;
        return new HashSet<string>(raw.Select(Normalize), StringComparer.Ordinal);
    }

    private static INamedTypeSymbol? FindFilterDescType(INamedTypeSymbol clauseType)
    {
        if (clauseType.Name is "AndClause" or "OrClause")
            return clauseType;
        if (!clauseType.Name.EndsWith("Clause", StringComparison.Ordinal)
            || clauseType.Name.Length <= "Clause".Length)
            return null;
        var descName = clauseType.Name.Substring(0, clauseType.Name.Length - "Clause".Length) + "FilterDesc";
        return clauseType.ContainingNamespace.GetTypeMembers(descName).FirstOrDefault();
    }

    private static HashSet<string>? TryReadStringArrayMember(
        INamedTypeSymbol type,
        string memberName,
        HashSet<ISymbol> visiting)
    {
        ISymbol? member = type.GetMembers(memberName)
            .FirstOrDefault(m => m is IPropertySymbol or IFieldSymbol);
        if (member is null || !visiting.Add(member))
            return null;

        foreach (var reference in member.DeclaringSyntaxReferences)
        {
            var syntax = reference.GetSyntax();
            ExpressionSyntax? expr = syntax switch
            {
                PropertyDeclarationSyntax p => p.ExpressionBody?.Expression
                    ?? p.AccessorList?.Accessors
                        .FirstOrDefault(a => a.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.GetAccessorDeclaration))
                        ?.ExpressionBody?.Expression
                    ?? p.Initializer?.Value,
                FieldDeclarationSyntax f => f.Declaration.Variables.FirstOrDefault()?.Initializer?.Value,
                VariableDeclaratorSyntax v => v.Initializer?.Value,
                _ => null,
            };
            if (expr is null)
                continue;

            if (TryExtractStringLiterals(expr) is { Count: > 0 } literals)
                return literals;

            // ClauseKeys => JokerFilterDesc.ClauseKeys
            if (expr is MemberAccessExpressionSyntax ma
                && ma.Expression is IdentifierNameSyntax typeName
                && type.ContainingNamespace.GetTypeMembers(typeName.Identifier.Text).FirstOrDefault()
                    is { } otherType)
            {
                var nested = TryReadStringArrayMember(otherType, ma.Name.Identifier.Text, visiting);
                if (nested is not null)
                    return nested;
            }
        }
        return null;
    }

    private static HashSet<string>? TryExtractStringLiterals(ExpressionSyntax expr)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        switch (expr)
        {
            case CollectionExpressionSyntax coll:
                foreach (var element in coll.Elements)
                {
                    if (element is ExpressionElementSyntax { Expression: LiteralExpressionSyntax lit }
                        && lit.Token.Value is string s)
                        set.Add(s);
                }
                break;
            case ImplicitArrayCreationExpressionSyntax arr:
                foreach (var e in arr.Initializer.Expressions)
                {
                    if (e is LiteralExpressionSyntax lit && lit.Token.Value is string s)
                        set.Add(s);
                }
                break;
            case ArrayCreationExpressionSyntax arr when arr.Initializer is not null:
                foreach (var e in arr.Initializer.Expressions)
                {
                    if (e is LiteralExpressionSyntax lit && lit.Token.Value is string s)
                        set.Add(s);
                }
                break;
            default:
                return null;
        }
        return set.Count > 0 ? set : null;
    }

    /// <summary>Property name plus singular/plural twin so agents can say <c>deck</c> or <c>decks</c>.</summary>
    private static IEnumerable<string> KeyAliases(string key)
    {
        yield return key;
        if (key.Length > 1 && key.EndsWith("s", StringComparison.Ordinal))
            yield return key.Substring(0, key.Length - 1);
        else
            yield return key + "s";
    }

    private static INamedTypeSymbol? UnwrapEnum(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
            return UnwrapEnum(array.ElementType);

        if (type is not INamedTypeSymbol named)
            return null;

        if (named.TypeKind == TypeKind.Enum)
            return named;

        if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            && named.TypeArguments.Length == 1)
            return UnwrapEnum(named.TypeArguments[0]);

        return null;
    }

    /// <summary>
    /// Resolve ClauseKeys from the FilterDesc that owns the grammar.
    /// Convention: <c>FooClause</c> → sibling <c>FooFilterDesc.ClauseKeys</c>
    /// (IJamlClauseDesc). And/Or stay on <c>LogicClause</c>.
    /// </summary>
    private static string? FindClauseKeysForClause(INamedTypeSymbol clauseType)
    {
        if (clauseType.Name is "AndClause" or "OrClause")
            return FindClauseKeysMember(clauseType);

        if (clauseType.Name.EndsWith("Clause", StringComparison.Ordinal)
            && clauseType.Name.Length > "Clause".Length)
        {
            var descName = clauseType.Name.Substring(0, clauseType.Name.Length - "Clause".Length) + "FilterDesc";
            foreach (var desc in clauseType.ContainingNamespace.GetTypeMembers(descName))
            {
                var keys = FindClauseKeysMember(desc);
                if (keys is not null)
                    return keys;
            }
        }

        return FindClauseKeysMember(clauseType);
    }

    private static string[] ExtractWires(AttributeData attr)
    {
        if (attr.ConstructorArguments.Length == 0)
            return [];
        var arg = attr.ConstructorArguments[0];
        if (arg.Kind == TypedConstantKind.Array)
            return arg.Values.Select(v => (string)v.Value!).ToArray();
        if (arg.Kind == TypedConstantKind.Primitive && arg.Value is string single)
            return [single];
        return [];
    }

    private static string? FindClauseKeysMember(INamedTypeSymbol type)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            var field = t.GetMembers("ClauseKeys")
                .FirstOrDefault(m => m is IFieldSymbol f && f.IsStatic && f.Type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_String });
            if (field is not null)
                return t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ".ClauseKeys";

            var prop = t.GetMembers("ClauseKeys")
                .FirstOrDefault(m => m is IPropertySymbol p && p.IsStatic && p.Type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_String });
            if (prop is not null)
                return t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ".ClauseKeys";
        }
        return null;
    }

    private static string? FindSourceKeysMember(TypedConstant sourceConfigType)
    {
        if (sourceConfigType.Value is not INamedTypeSymbol type)
            return null;
        var field = type.GetMembers("SourceKeys")
            .FirstOrDefault(m => m is IFieldSymbol f && f.IsStatic && f.Type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_String });
        if (field is not null)
            return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ".SourceKeys";
        var prop = type.GetMembers("SourceKeys")
            .FirstOrDefault(m => m is IPropertySymbol p && p.IsStatic && p.Type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_String });
        if (prop is not null)
            return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ".SourceKeys";
        return null;
    }

    private static void Emit(SourceProductionContext spc, SchemaModel model)
    {
        var all = model.Entries.Items;
        var keyMap = MergeKeyEnums(spc, model.Keys.Items);

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/> Written by Motely.Generators from [JamlDiscriminator] + enum-typed properties.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace Motely.Filters.Jaml;");
        sb.AppendLine();
        sb.AppendLine("public static partial class JamlSchema");
        sb.AppendLine("{");

        if (all.Length == 0)
        {
            sb.AppendLine("    public static string[] ClauseKeysFor(string discriminator) => [];");
            sb.AppendLine("    public static string[]? SourceKeysFor(string discriminator) => null;");
            sb.AppendLine("    public static System.Type ClauseTypeFor(string discriminator) => throw new System.ArgumentException($\"Unknown discriminator: {discriminator}\");");
            sb.AppendLine("    public static System.Type? SourceConfigTypeFor(string discriminator) => null;");
            sb.AppendLine("    public static System.Type? ValueEnumTypeFor(string discriminator) => null;");
            sb.AppendLine("    public static System.Type? KeyValueEnumTypeFor(string key) => null;");
            sb.AppendLine("    public static System.Type? EnumTypeForKind(string kind) => null;");
            sb.AppendLine("    public static bool RollsAreInlineFor(string discriminator) => false;");
            sb.AppendLine("    public static int[]? RollsDefaultFor(string discriminator) => null;");
            sb.AppendLine("    public static bool IsKnownDiscriminator(string discriminator) => false;");
            sb.AppendLine("    public static string[] Discriminators => [];");
            sb.AppendLine("    public static string[] NormalizedDiscriminators => [];");
            sb.AppendLine("    public static System.Collections.Generic.IReadOnlyList<(System.Type EnumType, string Kind)> ValueEnumKinds => [];");
            sb.AppendLine("    public static IJamlClause CreateClause(string discriminator) =>");
            sb.AppendLine("        throw new System.InvalidOperationException($\"Cannot construct clause for discriminator '{discriminator}'.\");");
            EmitListItems(sb);
            sb.AppendLine("}");
            spc.AddSource("JamlSchema.g.cs", sb.ToString());
            return;
        }

        sb.AppendLine("    private static string N(string d) =>");
        sb.AppendLine("        d.ToLowerInvariant().Replace(\" \", \"\").Replace(\"-\", \"\").Replace(\"_\", \"\");");
        sb.AppendLine();

        // CreateClause — one factory from [JamlDiscriminator] wires. Empty shells; Populate fills them.
        sb.AppendLine("    /// <summary>Construct an empty clause shell for a wire name. Generated from disc attributes.</summary>");
        sb.AppendLine("    public static IJamlClause CreateClause(string discriminator) => N(discriminator) switch");
        sb.AppendLine("    {");
        foreach (var group in all.GroupBy(e => e.ClauseTypeFull).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var patterns = string.Join(
                " or ",
                group.SelectMany(e => e.Normalized)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .Select(n => $"\"{n}\""));
            sb.AppendLine($"        {patterns} => new {group.Key}(),");
        }
        sb.AppendLine("        _ => throw new System.InvalidOperationException(");
        sb.AppendLine("            $\"Cannot construct clause for discriminator '{discriminator}'.\"),");
        sb.AppendLine("    };");
        sb.AppendLine();

        // ClauseKeysFor
        sb.AppendLine("    public static string[] ClauseKeysFor(string discriminator) => N(discriminator) switch");
        sb.AppendLine("    {");
        foreach (var e in all)
        {
            var patterns = string.Join(" or ", e.Normalized.Select(n => $"\"{n}\""));
            var member = e.ClauseKeysMember ?? "[]";
            sb.AppendLine($"        {patterns} => {member},");
        }
        sb.AppendLine("        _ => [],");
        sb.AppendLine("    };");
        sb.AppendLine();

        // SourceKeysFor
        sb.AppendLine("    public static string[]? SourceKeysFor(string discriminator) => N(discriminator) switch");
        sb.AppendLine("    {");
        foreach (var e in all)
        {
            if (e.SourceKeysMember is { } sk)
                foreach (var n in e.Normalized)
                    sb.AppendLine($"        \"{n}\" => {sk},");
        }
        sb.AppendLine("        _ => null,");
        sb.AppendLine("    };");
        sb.AppendLine();

        // ClauseTypeFor
        sb.AppendLine("    public static System.Type ClauseTypeFor(string discriminator) => N(discriminator) switch");
        sb.AppendLine("    {");
        foreach (var e in all)
            foreach (var n in e.Normalized)
                sb.AppendLine($"        \"{n}\" => typeof({e.ClauseTypeFull}),");
        sb.AppendLine("        _ => throw new System.ArgumentException($\"Unknown discriminator: {discriminator}\")");
        sb.AppendLine("    };");
        sb.AppendLine();

        // SourceConfigTypeFor
        sb.AppendLine("    public static System.Type? SourceConfigTypeFor(string discriminator) => N(discriminator) switch");
        sb.AppendLine("    {");
        foreach (var e in all.Where(e => e.SourceConfigFull is not null))
            foreach (var n in e.Normalized)
                sb.AppendLine($"        \"{n}\" => typeof({e.SourceConfigFull}),");
        sb.AppendLine("        _ => null,");
        sb.AppendLine("    };");
        sb.AppendLine();

        // ValueEnumTypeFor — discriminator value enums from [JamlDiscriminator(ValueEnum=…)]
        sb.AppendLine("    public static System.Type? ValueEnumTypeFor(string discriminator) => N(discriminator) switch");
        sb.AppendLine("    {");
        foreach (var e in all.Where(e => e.ValueEnumFull is not null))
            foreach (var n in e.Normalized)
                sb.AppendLine($"        \"{n}\" => typeof({e.ValueEnumFull}),");
        sb.AppendLine("        _ => null,");
        sb.AppendLine("    };");
        sb.AppendLine();

        // KeyValueEnumTypeFor — property types on clause/root/with shapes
        sb.AppendLine("    public static System.Type? KeyValueEnumTypeFor(string key) => N(key) switch");
        sb.AppendLine("    {");
        var byEnum = keyMap
            .GroupBy(k => k.EnumTypeFull)
            .OrderBy(g => g.Min(x => x.Key), StringComparer.Ordinal);
        foreach (var group in byEnum)
        {
            var patterns = string.Join(" or ", group.Select(k => k.Key).Distinct().OrderBy(k => k, StringComparer.Ordinal).Select(k => $"\"{k}\""));
            sb.AppendLine($"        {patterns} => typeof({group.Key}),");
        }
        sb.AppendLine("        _ => null,");
        sb.AppendLine("    };");
        sb.AppendLine();

        // EnumTypeForKind — property keys first, then discriminator wires
        sb.AppendLine("    /// <summary>Property/root key enum, else discriminator <see cref=\"ValueEnumTypeFor\"/>.</summary>");
        sb.AppendLine("    public static System.Type? EnumTypeForKind(string kind) =>");
        sb.AppendLine("        KeyValueEnumTypeFor(kind) ?? ValueEnumTypeFor(kind);");
        sb.AppendLine();

        // RollsAreInlineFor
        sb.AppendLine("    public static bool RollsAreInlineFor(string discriminator) => N(discriminator) switch");
        sb.AppendLine("    {");
        foreach (var e in all.Where(e => e.RollsAreInlineValue))
            foreach (var n in e.Normalized)
                sb.AppendLine($"        \"{n}\" => true,");
        sb.AppendLine("        _ => false,");
        sb.AppendLine("    };");
        sb.AppendLine();

        // RollsDefaultFor
        sb.AppendLine("    public static int[]? RollsDefaultFor(string discriminator) => N(discriminator) switch");
        sb.AppendLine("    {");
        foreach (var e in all.Where(e => e.RollsDefault is not null))
        {
            var arr = string.Join(", ", e.RollsDefault!);
            foreach (var n in e.Normalized)
                sb.AppendLine($"        \"{n}\" => [{arr}],");
        }
        sb.AppendLine("        _ => null,");
        sb.AppendLine("    };");
        sb.AppendLine();

        var allNormalized = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in all)
            foreach (var n in e.Normalized)
                allNormalized.Add(n);

        sb.AppendLine("    public static bool IsKnownDiscriminator(string discriminator) => N(discriminator) switch");
        sb.AppendLine("    {");
        if (allNormalized.Count > 0)
        {
            var patterns = string.Join(" or ", allNormalized.OrderBy(n => n, StringComparer.Ordinal).Select(n => $"\"{n}\""));
            sb.AppendLine($"        {patterns} => true,");
        }
        sb.AppendLine("        _ => false,");
        sb.AppendLine("    };");
        sb.AppendLine();

        sb.AppendLine("    public static string[] Discriminators =>");
        sb.AppendLine("    [");
        foreach (var e in all)
            foreach (var w in e.Wires)
                sb.AppendLine($"        \"{w}\",");
        sb.AppendLine("    ];");
        sb.AppendLine();

        sb.AppendLine("    public static string[] NormalizedDiscriminators =>");
        sb.AppendLine("    [");
        foreach (var n in allNormalized.OrderBy(x => x, StringComparer.Ordinal))
            sb.AppendLine($"        \"{n}\",");
        sb.AppendLine("    ];");
        sb.AppendLine();

        // ValueEnumKinds — distinct enums for hover, from disc ValueEnum + property keys
        var kinds = new List<(string EnumFull, string Label)>();
        var seenEnum = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in all.Where(e => e.ValueEnumFull is not null))
        {
            if (seenEnum.Add(e.ValueEnumFull!))
                kinds.Add((e.ValueEnumFull!, e.Wires[0]));
        }
        foreach (var k in keyMap.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (seenEnum.Add(k.EnumTypeFull))
                kinds.Add((k.EnumTypeFull, k.Label));
        }

        sb.AppendLine("    public static System.Collections.Generic.IReadOnlyList<(System.Type EnumType, string Kind)> ValueEnumKinds =>");
        sb.AppendLine("    [");
        foreach (var (enumFull, label) in kinds)
            sb.AppendLine($"        (typeof({enumFull}), \"{Escape(label)}\"),");
        sb.AppendLine("    ];");
        sb.AppendLine();

        EmitListItems(sb);

        sb.AppendLine("}");
        spc.AddSource("JamlSchema.g.cs", sb.ToString());
    }

    private static void EmitListItems(StringBuilder sb)
    {
        sb.AppendLine("    /// <summary>Engine enum names for a kind; optional case-insensitive substring filter.</summary>");
        sb.AppendLine("    public static string[] ListItems(string kind, string? query = null)");
        sb.AppendLine("    {");
        sb.AppendLine("        System.Type enumType =");
        sb.AppendLine("            EnumTypeForKind(kind)");
        sb.AppendLine("            ?? throw new System.ArgumentException(");
        sb.AppendLine("                $\"Unknown vocabulary kind '{kind}'. Use a discriminator wire (e.g. tarotCard) or a property/root key whose type is an engine enum.\");");
        sb.AppendLine("        string[] names = System.Enum.GetNames(enumType);");
        sb.AppendLine("        if (string.IsNullOrWhiteSpace(query))");
        sb.AppendLine("            return names;");
        sb.AppendLine("        return System.Linq.Enumerable.ToArray(");
        sb.AppendLine("            System.Linq.Enumerable.Where(names, n => n.Contains(query, System.StringComparison.OrdinalIgnoreCase)));");
        sb.AppendLine("    }");
    }

    private static List<KeyEnum> MergeKeyEnums(SourceProductionContext spc, KeyEnum[] raw)
    {
        var map = new Dictionary<string, KeyEnum>(StringComparer.Ordinal);
        foreach (var k in raw)
        {
            if (map.TryGetValue(k.Key, out var existing))
            {
                if (!string.Equals(existing.EnumTypeFull, k.EnumTypeFull, StringComparison.Ordinal))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "MOTJAML001",
                            "Conflicting key value enums",
                            "JAML key '{0}' maps to both {1} and {2}",
                            "Jaml",
                            DiagnosticSeverity.Error,
                            isEnabledByDefault: true),
                        Location.None,
                        k.Key, existing.EnumTypeFull, k.EnumTypeFull));
                }
                continue;
            }
            map[k.Key] = k;
        }
        return map.Values.OrderBy(k => k.Key, StringComparer.Ordinal).ToList();
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string Normalize(string d) =>
        d.ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");

    private readonly struct Batch(ImmutableArray<Entry> Entries, ImmutableArray<KeyEnum> Keys)
    {
        public static Batch Empty { get; } = new(ImmutableArray<Entry>.Empty, ImmutableArray<KeyEnum>.Empty);
        public ImmutableArray<Entry> Entries { get; } = Entries;
        public ImmutableArray<KeyEnum> Keys { get; } = Keys;
    }

    private readonly struct SchemaModel(EquatableArray<Entry> Entries, EquatableArray<KeyEnum> Keys)
    {
        public EquatableArray<Entry> Entries { get; } = Entries;
        public EquatableArray<KeyEnum> Keys { get; } = Keys;
    }

    private readonly struct KeyEnum(string Key, string EnumTypeFull, string Label) : System.IEquatable<KeyEnum>
    {
        public string Key { get; } = Key;
        public string EnumTypeFull { get; } = EnumTypeFull;
        public string Label { get; } = Label;

        public bool Equals(KeyEnum other) =>
            Key == other.Key && EnumTypeFull == other.EnumTypeFull && Label == other.Label;
        public override bool Equals(object? obj) => obj is KeyEnum o && Equals(o);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (Key?.GetHashCode() ?? 0);
                hash = hash * 31 + (EnumTypeFull?.GetHashCode() ?? 0);
                hash = hash * 31 + (Label?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }

    private readonly struct Entry(
        string[] Wires,
        string[] Normalized,
        string ClauseTypeFull,
        string? ClauseKeysMember,
        string? SourceConfigFull,
        string? SourceKeysMember,
        string? ValueEnumFull,
        bool RollsAreInlineValue,
        int[]? RollsDefault
    ) : System.IEquatable<Entry>
    {
        public string[] Wires { get; } = Wires;
        public string[] Normalized { get; } = Normalized;
        public string ClauseTypeFull { get; } = ClauseTypeFull;
        public string? ClauseKeysMember { get; } = ClauseKeysMember;
        public string? SourceConfigFull { get; } = SourceConfigFull;
        public string? SourceKeysMember { get; } = SourceKeysMember;
        public string? ValueEnumFull { get; } = ValueEnumFull;
        public bool RollsAreInlineValue { get; } = RollsAreInlineValue;
        public int[]? RollsDefault { get; } = RollsDefault;

        public bool Equals(Entry other) =>
            System.Linq.Enumerable.SequenceEqual(Wires, other.Wires)
            && System.Linq.Enumerable.SequenceEqual(Normalized, other.Normalized)
            && ClauseTypeFull == other.ClauseTypeFull
            && ClauseKeysMember == other.ClauseKeysMember
            && SourceConfigFull == other.SourceConfigFull
            && SourceKeysMember == other.SourceKeysMember
            && ValueEnumFull == other.ValueEnumFull
            && RollsAreInlineValue == other.RollsAreInlineValue
            && (RollsDefault == other.RollsDefault || (RollsDefault is not null && other.RollsDefault is not null && System.Linq.Enumerable.SequenceEqual(RollsDefault, other.RollsDefault)));

        public override bool Equals(object? obj) => obj is Entry o && Equals(o);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                foreach (var w in Wires) hash = hash * 31 + (w?.GetHashCode() ?? 0);
                foreach (var n in Normalized) hash = hash * 31 + (n?.GetHashCode() ?? 0);
                hash = hash * 31 + (ClauseTypeFull?.GetHashCode() ?? 0);
                hash = hash * 31 + (ClauseKeysMember?.GetHashCode() ?? 0);
                hash = hash * 31 + (SourceConfigFull?.GetHashCode() ?? 0);
                hash = hash * 31 + (SourceKeysMember?.GetHashCode() ?? 0);
                hash = hash * 31 + (ValueEnumFull?.GetHashCode() ?? 0);
                hash = hash * 31 + RollsAreInlineValue.GetHashCode();
                if (RollsDefault is not null)
                    foreach (var r in RollsDefault) hash = hash * 31 + r.GetHashCode();
                return hash;
            }
        }
    }
}

internal readonly struct EquatableArray<T>(T[] items) : System.IEquatable<EquatableArray<T>>
    where T : System.IEquatable<T>
{
    public T[] Items { get; } = items;

    public bool Equals(EquatableArray<T> other) => Items.AsSpan().SequenceEqual(other.Items.AsSpan());
    public override bool Equals(object? obj) => obj is EquatableArray<T> o && Equals(o);
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            foreach (var item in Items)
                hash = hash * 31 + (item?.GetHashCode() ?? 0);
            return hash;
        }
    }
}
