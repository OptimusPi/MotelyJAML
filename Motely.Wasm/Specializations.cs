// The engine's SIMD hooks (guide: specialization). Each interface is one member over a ref struct,
// so a JS object can never implement one. These pairs make them handles: an engine-made filter desc,
// score desc or Jamlyzer rider crosses to JS and back to the settings as itself; a JS-made one is
// a proxy that throws if the engine ever runs it.

using Bootsharp;
using Motely;
using Motely.Filters;

/// <summary>Why a JS object cannot stand in for an engine SIMD hook.</summary>
internal static class SimdHook
{
    internal static NotSupportedException FromJs(string hook) =>
        new($"{hook} runs per SIMD batch over engine memory; only engine-made instances can be used.");
}

/// <summary>JS side of <see cref="IMotelySeedFilterDesc"/>.</summary>
/// <param name="id">Interop id.</param>
[SpecializeImport(typeof(IMotelySeedFilterDesc))]
public abstract class SeedFilterDescImport(int id) : SpecializedImport(id), IMotelySeedFilterDesc
{
    /// <inheritdoc/>
    public IMotelySeedFilter CreateFilter(ref MotelyFilterCreationContext ctx) =>
        throw SimdHook.FromJs(nameof(IMotelySeedFilterDesc));
}

/// <summary>Engine <see cref="IMotelySeedFilterDesc"/> as a JS handle.</summary>
/// <param name="it">The engine instance.</param>
[SpecializeExport(typeof(IMotelySeedFilterDesc))]
public sealed class SeedFilterDescExport(IMotelySeedFilterDesc it) : SpecializedExport(it);

/// <summary>JS side of <see cref="IMotelySeedFilter"/>.</summary>
/// <param name="id">Interop id.</param>
[SpecializeImport(typeof(IMotelySeedFilter))]
public abstract class SeedFilterImport(int id) : SpecializedImport(id), IMotelySeedFilter
{
    /// <inheritdoc/>
    public VectorMask Filter(ref MotelyVectorSearchContext searchContext) =>
        throw SimdHook.FromJs(nameof(IMotelySeedFilter));
}

/// <summary>Engine <see cref="IMotelySeedFilter"/> as a JS handle.</summary>
/// <param name="it">The engine instance.</param>
[SpecializeExport(typeof(IMotelySeedFilter))]
public sealed class SeedFilterExport(IMotelySeedFilter it) : SpecializedExport(it);

/// <summary>JS side of <see cref="IMotelySeedScoreDesc"/>.</summary>
/// <param name="id">Interop id.</param>
[SpecializeImport(typeof(IMotelySeedScoreDesc))]
public abstract class SeedScoreDescImport(int id) : SpecializedImport(id), IMotelySeedScoreDesc
{
    /// <inheritdoc/>
    public IMotelySeedScoreProvider CreateScoreProvider(ref MotelyFilterCreationContext ctx) =>
        throw SimdHook.FromJs(nameof(IMotelySeedScoreDesc));
}

/// <summary>Engine <see cref="IMotelySeedScoreDesc"/> as a JS handle.</summary>
/// <param name="it">The engine instance.</param>
[SpecializeExport(typeof(IMotelySeedScoreDesc))]
public sealed class SeedScoreDescExport(IMotelySeedScoreDesc it) : SpecializedExport(it);

/// <summary>JS side of <see cref="IMotelySeedScoreProvider"/>.</summary>
/// <param name="id">Interop id.</param>
[SpecializeImport(typeof(IMotelySeedScoreProvider))]
public abstract class SeedScoreProviderImport(int id) : SpecializedImport(id), IMotelySeedScoreProvider
{
    /// <inheritdoc/>
    public VectorMask Score(
        ref MotelyVectorSearchContext searchContext,
        MotelyScoredSeedResult[] buffer,
        VectorMask baseFilterMask,
        int scoreThreshold = 0
    ) => throw SimdHook.FromJs(nameof(IMotelySeedScoreProvider));
}

/// <summary>Engine <see cref="IMotelySeedScoreProvider"/> as a JS handle.</summary>
/// <param name="it">The engine instance.</param>
[SpecializeExport(typeof(IMotelySeedScoreProvider))]
public sealed class SeedScoreProviderExport(IMotelySeedScoreProvider it) : SpecializedExport(it);

/// <summary>JS side of <see cref="IMotelySeedAnalyzeDesc"/>.</summary>
/// <param name="id">Interop id.</param>
[SpecializeImport(typeof(IMotelySeedAnalyzeDesc))]
public abstract class SeedAnalyzeDescImport(int id) : SpecializedImport(id), IMotelySeedAnalyzeDesc
{
    /// <inheritdoc/>
    public IMotelySeedAnalyzeProvider CreateAnalyzeProvider(ref MotelyFilterCreationContext ctx) =>
        throw SimdHook.FromJs(nameof(IMotelySeedAnalyzeDesc));
}

/// <summary>Engine <see cref="IMotelySeedAnalyzeDesc"/> as a JS handle.</summary>
/// <param name="it">The engine instance.</param>
[SpecializeExport(typeof(IMotelySeedAnalyzeDesc))]
public sealed class SeedAnalyzeDescExport(IMotelySeedAnalyzeDesc it) : SpecializedExport(it);

/// <summary>JS side of <see cref="IMotelySeedAnalyzeProvider"/>.</summary>
/// <param name="id">Interop id.</param>
[SpecializeImport(typeof(IMotelySeedAnalyzeProvider))]
public abstract class SeedAnalyzeProviderImport(int id) : SpecializedImport(id), IMotelySeedAnalyzeProvider
{
    /// <inheritdoc/>
    public void Analyze(
        ref MotelyVectorSearchContext searchContext,
        VectorMask reportedMask,
        MotelyScoredSeedResult[]? scores
    ) => throw SimdHook.FromJs(nameof(IMotelySeedAnalyzeProvider));
}

/// <summary>Engine <see cref="IMotelySeedAnalyzeProvider"/> as a JS handle.</summary>
/// <param name="it">The engine instance.</param>
[SpecializeExport(typeof(IMotelySeedAnalyzeProvider))]
public sealed class SeedAnalyzeProviderExport(IMotelySeedAnalyzeProvider it) : SpecializedExport(it);

/// <summary>JS side of <see cref="IMotelySeedRouterDesc"/>.</summary>
/// <param name="id">Interop id.</param>
[SpecializeImport(typeof(IMotelySeedRouterDesc))]
public abstract class SeedRouterDescImport(int id) : SpecializedImport(id), IMotelySeedRouterDesc
{
    /// <inheritdoc/>
    public IMotelySeedRouter CreateSeedRouter(ref MotelyFilterCreationContext ctx) =>
        throw SimdHook.FromJs(nameof(IMotelySeedRouterDesc));
}

/// <summary>Engine <see cref="IMotelySeedRouterDesc"/> as a JS handle.</summary>
/// <param name="it">The engine instance.</param>
[SpecializeExport(typeof(IMotelySeedRouterDesc))]
public sealed class SeedRouterDescExport(IMotelySeedRouterDesc it) : SpecializedExport(it);

/// <summary>JS side of <see cref="IMotelySeedRouter"/>.</summary>
/// <param name="id">Interop id.</param>
[SpecializeImport(typeof(IMotelySeedRouter))]
public abstract class SeedRouterImport(int id) : SpecializedImport(id), IMotelySeedRouter
{
    /// <inheritdoc/>
    public void InjectSingleSeedContext(in MotelySingleSearchContext ctx) =>
        throw SimdHook.FromJs(nameof(IMotelySeedRouter));
}

/// <summary>Engine <see cref="IMotelySeedRouter"/> as a JS handle.</summary>
/// <param name="it">The engine instance.</param>
[SpecializeExport(typeof(IMotelySeedRouter))]
public sealed class SeedRouterExport(IMotelySeedRouter it) : SpecializedExport(it);

/// <summary>JS side of <see cref="MotelySingleSearchContext"/>. Every member that takes one is
/// erased (see Names.Member); this keeps the inspector from crawling the scalar context's surface.</summary>
/// <param name="id">Interop id.</param>
[SpecializeImport(typeof(MotelySingleSearchContext))]
public abstract class SingleSearchContextImport(int id) : SpecializedImport(id)
{
    /// <inheritdoc/>
    protected override object Unwrap() => throw SimdHook.FromJs(nameof(MotelySingleSearchContext));
}

/// <summary>Engine <see cref="MotelySingleSearchContext"/> as a JS handle.</summary>
/// <param name="it">The engine instance.</param>
[SpecializeExport(typeof(MotelySingleSearchContext))]
public sealed class SingleSearchContextExport(MotelySingleSearchContext it) : SpecializedExport(it);

/// <summary>JS side of a lazy <see cref="IEnumerable{T}"/>. Every member that takes one is erased
/// (see Names.Member); this keeps the serializer from trying to build one by value.</summary>
/// <typeparam name="T">Element type.</typeparam>
/// <param name="id">Interop id.</param>
[SpecializeImport(typeof(IEnumerable<>))]
public abstract class LazySequenceImport<T>(int id) : SpecializedImport(id)
{
    /// <inheritdoc/>
    protected override object Unwrap() =>
        throw new NotSupportedException("A lazy sequence cannot cross; pass a materialized array.");
}

/// <summary>Engine <see cref="IEnumerable{T}"/> as a JS handle.</summary>
/// <typeparam name="T">Element type.</typeparam>
/// <param name="it">The engine sequence.</param>
[SpecializeExport(typeof(IEnumerable<>))]
public sealed class LazySequenceExport<T>(IEnumerable<T> it) : SpecializedExport(it);
