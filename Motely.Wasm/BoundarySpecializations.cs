using Bootsharp;
using Motely;

// Two types on the settings surface that the 0.9 serializer cannot build a Binary<> for.
// Both are boundary facts, so both are answered here rather than in the engine.

/// <summary>
/// A filter desc crosses as an opaque handle. Its members drive ref-struct contexts, so there is
/// nothing JavaScript can read or implement — but the settings surface names the type, and the
/// generator needs a declared shape rather than a value serializer.
/// </summary>
[SpecializeImport(typeof(IMotelySeedFilterDesc))]
public abstract class MotelySeedFilterDescImport(int id) : SpecializedImport(id) { }

[SpecializeExport(typeof(IMotelySeedFilterDesc))]
public sealed class MotelySeedFilterDescExport(IMotelySeedFilterDesc desc)
    : SpecializedExport(desc) { }

/// <summary>
/// A deferred sequence has no value to serialize, so it crosses as its materialized items. The
/// lazy form stays native-side; anything that reaches this boundary is already enumerable once.
/// <para>
/// Registered on the OPEN generic, and that is load-bearing. Bootsharp stores a specialization
/// under the attribute argument exactly as written (Preferences.cs:84) but looks it up through
/// OpenGeneric (Preferences.cs:52) — so <c>typeof(IEnumerable&lt;string&gt;)</c> is filed under the
/// closed type and searched for under <c>IEnumerable&lt;&gt;</c>, and never matches. The miss makes
/// IsSpecialized false, which makes IsUserType false (CoreLib is not a user assembly), which makes
/// IsInstanced false, so the type falls through to BuildObject and the generator emits
/// <c>new IEnumerable&lt;string&gt;()</c>. Every example in specialization.md is open for this reason.
/// </para>
/// <para>
/// This exists because <see cref="IMotelySearchSettings.WithSeedGenerator"/> takes a lazy sequence
/// on purpose — it streams a keyspace that can never be materialized. WithSeedList(string[]) is the
/// materialized door; the generator must stay deferred.
/// </para>
/// </summary>
[SpecializeImport(typeof(IEnumerable<>))]
public abstract class StringSequenceImport(int id) : SpecializedImport(id)
{
    public abstract string[] Items { get; }

    protected override object Unwrap() => Items;
}

[SpecializeExport(typeof(IEnumerable<>))]
public sealed class StringSequenceExport : SpecializedExport
{
    public StringSequenceExport(IEnumerable<string> sequence)
        : base(sequence) => Items = [.. sequence];

    public string[] Items { get; }
}
