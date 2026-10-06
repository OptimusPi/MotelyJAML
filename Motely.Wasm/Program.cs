// The browser build exports the engine's own entry points, one to one: same class names, same
// method names, same engine types in and out. The engine's statics cannot carry [Export] without
// Motely referencing Bootsharp, so each one gets a single forwarding line here (guide:
// getting-started). Everything they return crosses as the engine type itself: records by value
// (guide: serialization), classes and interfaces by reference (guide: interop-instances).

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Bootsharp;
using Motely;
using Motely.Analysis;
using Motely.Filters.Jaml;

/// <summary>Entry point; the exports below are live once <c>bootsharp.boot()</c> resolves.</summary>
public static class Program
{
    /// <summary>JS drives the engine through the exports; Main only starts recording errors.</summary>
    public static void Main() =>
        AppDomain.CurrentDomain.FirstChanceException += (_, e) => Errors.LastMessage = e.Exception.Message;
}

/// <summary>Why the last call threw. The NativeAOT runtime hands a thrown C# exception to JS as
/// "C# exception from NativeAOT" without its message, so the message is kept here instead: after
/// any export throws (a rejected with* value, a start that fails, a filter that does not load),
/// <c>Errors.last()</c> is its reason.</summary>
public static class Errors
{
    internal static string? LastMessage;

    /// <summary>The message of the most recent C# exception, or null before any.</summary>
    /// <returns>The message.</returns>
    [Export]
    public static string? Last() => LastMessage;
}

/// <summary>Engine's <see cref="Motely.Filters.Jaml.JamlConfigLoader"/>.</summary>
public static class JamlConfigLoader
{
    /// <summary>Loads a JAML filter. Throws with the loader's line-numbered message when it does not load.</summary>
    /// <param name="yaml">JAML filter text.</param>
    /// <returns>The engine's config.</returns>
    [Export]
    public static JamlConfig FromJaml(string yaml) => Motely.Filters.Jaml.JamlConfigLoader.FromJaml(yaml);

    /// <summary>The engine's TryLoad, error half: null when the filter loads, otherwise the
    /// loader's line-numbered reason. The NativeAOT runtime hands a thrown C# exception to JS as
    /// "C# exception from NativeAOT" without its message, so fromJaml's throw cannot carry it.</summary>
    /// <param name="yaml">JAML filter text.</param>
    /// <returns>Null, or why the filter does not load.</returns>
    [Export]
    public static string? Check(string yaml) =>
        Motely.Filters.Jaml.JamlConfigLoader.TryLoad(yaml, out _, out var error) ? null : error;
}

/// <summary>Engine's <see cref="Motely.Filters.JamlSearchBuilder"/>.</summary>
public static class JamlSearchBuilder
{
    /// <summary>The engine's search settings for a config, held by reference.</summary>
    /// <param name="config">Config from <see cref="JamlConfigLoader.FromJaml"/>.</param>
    /// <param name="engineCutoff">Score cutoff; 0 keeps the config's own.</param>
    /// <returns>The engine's settings; configure, then start.</returns>
    [Export]
    public static IMotelySearchSettings CreateSettings(JamlConfig config, int engineCutoff = 0) =>
        Motely.Filters.JamlSearchBuilder.CreateSettings(config, engineCutoff);
}

/// <summary>Engine's <see cref="Motely.Analysis.MotelyJamlyzer"/>.</summary>
public static class MotelyJamlyzer
{
    /// <summary>One result per seed in the config's seeds.</summary>
    /// <param name="config">Config from <see cref="JamlConfigLoader.FromJaml"/>.</param>
    /// <param name="eventRolls">Size of each roll queue.</param>
    /// <param name="shopSlots">Shop depth per ante; 0 keeps the defaults.</param>
    /// <returns>The Jamlyzer results.</returns>
    [Export]
    public static IReadOnlyList<MotelyJamlyzerSeedResult> Analyze(JamlConfig config, int eventRolls = 20, int shopSlots = 0) =>
        Motely.Analysis.MotelyJamlyzer.Analyze(config, eventRolls, shopSlots);

    /// <summary>Continues a scroll from a previous result's stream states. One seed only.</summary>
    /// <param name="config">Config from <see cref="JamlConfigLoader.FromJaml"/>.</param>
    /// <param name="resumeFrom">A previous result's stream states.</param>
    /// <param name="eventRolls">Size of each roll queue.</param>
    /// <param name="shopSlots">Shop depth per ante; 0 keeps the defaults.</param>
    /// <returns>The Jamlyzer results.</returns>
    [Export]
    public static IReadOnlyList<MotelyJamlyzerSeedResult> Analyze(
        JamlConfig config,
        MotelyJamlyzerStreamStates resumeFrom,
        int eventRolls = 20,
        int shopSlots = 0
    ) => Motely.Analysis.MotelyJamlyzer.Analyze(config, resumeFrom, eventRolls, shopSlots);

    /// <summary>The analyze provider a search runs on every find, for its settings' analyze provider.</summary>
    /// <param name="config">Config from <see cref="JamlConfigLoader.FromJaml"/>.</param>
    /// <param name="onAnalyzed">Receives each find's result.</param>
    /// <param name="eventRolls">Size of each roll queue.</param>
    /// <param name="shopSlots">Shop depth per ante; 0 keeps the defaults.</param>
    /// <returns>The engine's rider desc.</returns>
    [Export]
    public static MotelyJamlyzerRiderDesc CreateRiderDesc(
        JamlConfig config,
        Action<MotelyJamlyzerSeedResult> onAnalyzed,
        int eventRolls = 20,
        int shopSlots = 0
    ) => Motely.Analysis.MotelyJamlyzer.CreateRiderDesc(config, onAnalyzed, eventRolls, shopSlots);
}

/// <summary>Renaming (guide: renaming).</summary>
public static class Names
{
    private const string BuildTimeOnly =
        "Bootsharp calls the renamers by reflection while it generates the bindings; they never run in the browser.";

    /// <summary>Engine and binding types span several namespaces; JS gets them in one module. BCL
    /// types keep their own (system, system/threading), where Bootsharp's bcl classes and the
    /// System.Action arities do not collide with each other.</summary>
    /// <param name="type">The renamed type.</param>
    /// <param name="default">Bootsharp's name.</param>
    /// <returns>The module name.</returns>
    [RenameModule]
    public static string Module(Type type, string @default) =>
        type.Assembly == typeof(Names).Assembly || type.Assembly == typeof(JamlConfig).Assembly ? "index" : @default;

    /// <summary>Erases types that cannot cross (see <see cref="CanCross(Type)"/>). The inspector
    /// still meets them on the way to the members <see cref="Member"/> erases; this keeps them out
    /// of the declarations, so a by-ref context or a hollow delegate never reaches the .d.mts.</summary>
    /// <param name="type">The projected type.</param>
    /// <param name="default">Bootsharp's name.</param>
    /// <returns>The name, or null to erase.</returns>
    [RenameNode]
    [RequiresUnreferencedCode(BuildTimeOnly)]
    public static string? Node(Type type, string @default) =>
        !CanCross(type) ? null
        // Bootsharp names System.Action and System.Action<T> both "Action" in the system module,
        // and TypeScript has no arity overloads: the zero-argument one carries its arity.
        : type == typeof(Action) ? "Action0"
        : @default;

    /// <summary>Erases members whose signature holds a type that cannot cross: the SIMD plumbing
    /// (filter creation contexts, vector masks) has no JS form. Their declaring types still cross
    /// by reference, so a filter desc or a Jamlyzer rider moves between engine calls as a handle.</summary>
    /// <param name="info">The projected member.</param>
    /// <param name="default">Bootsharp's name.</param>
    /// <returns>The name, or null to erase.</returns>
    [RenameMember]
    [RequiresUnreferencedCode(BuildTimeOnly)]
    public static string? Member(MemberInfo info, string @default) =>
        info switch
        {
            MethodInfo m when !CanCross(m) => null,
            PropertyInfo p when !CanCross(p.PropertyType) => null,
            EventInfo e when e.EventHandlerType is { } h && !CanCross(h) => null,
            _ => @default,
        };

    [RequiresUnreferencedCode(BuildTimeOnly)]
    private static bool CanCross(MethodInfo method) =>
        CanCross(method.ReturnType) && method.GetParameters().All(p => CanCross(p.ParameterType));

    [RequiresUnreferencedCode(BuildTimeOnly)]
    private static bool CanCross(Type type)
    {
        if (type.IsByRef || type.IsPointer || type.IsByRefLike)
            return false;
        // The scalar per-seed context: Jimmolate's argument. The engine documents it as native-only
        // (IMotelySearchSettings.WithJimmolate): a JS predicate would cross once per seed.
        if (type == typeof(MotelySingleSearchContext))
            return false;
        // A lazy sequence has no value to serialize; the engine's crossing shape is the
        // materialized list (IMotelySearchSettings.WithSeedList vs WithSeedGenerator).
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            return false;
        if (type.HasElementType)
            return CanCross(type.GetElementType()!);
        if (type.IsGenericType && !type.GetGenericArguments().All(CanCross))
            return false;
        // A delegate crosses as a function; its own signature has to cross too.
        if (typeof(Delegate).IsAssignableFrom(type) && type.GetMethod("Invoke") is { } invoke)
            return CanCross(invoke);
        return true;
    }
}
