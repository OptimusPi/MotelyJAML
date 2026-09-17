namespace Motely.Filters.Jaml;

/// <summary>
/// What a filter descriptor declares about the clause it runs. The desc is the source of truth —
/// it owns the clause type, it knows which enum the values come from, and it is the thing the
/// engine executes. Everything JAML needs to read, validate, write and complete a clause is
/// stated here, next to the code that acts on it.
///
/// Every member is static abstract, so a desc that fails to declare its grammar does not compile.
/// And the desc <em>builds</em> its clause rather than describing a Type for someone else to
/// reflect on: no Activator, no GetProperty, no [DynamicallyAccessedMembers], nothing for the
/// trimmer to be talked into keeping. Concrete construction the compiler can see all the way
/// down, which is what makes it survive NativeAOT-LLVM rather than merely be annotated for it.
/// </summary>
/// <typeparam name="TClause">The clause this desc runs.</typeparam>
// Clauses construct via generated <c>JamlSchema.CreateClause</c> (empty shell). The desc
// mutates that shell through Set/SetDiscriminatorValue — no Activator, trimmer-safe.
public interface IJamlClauseDesc<TClause> where TClause : class, IJamlClause
{
    /// <summary>Every wire spelling that selects this clause — "joker" and "jokers" both.</summary>
    static abstract string[] Discriminators { get; }

    /// <summary>Every key the clause block accepts. Stated once, here.</summary>
    static abstract string[] ClauseKeys { get; }

    /// <summary>Assigns one wire key onto the clause. A concrete switch, written next to the
    /// properties it assigns, so adding a property and forgetting the key is a visible hole
    /// rather than a silent one.</summary>
    static abstract bool Set(TClause clause, string key, IJamlValueReader value);

    /// <summary>Reads the value carried by the discriminator itself ("joker: Perkeo").
    /// Returns false when the value doesn't fit, having already reported why — a desc that
    /// can't read its own value must say so, not leave the clause half-built and quiet.</summary>
    static virtual bool SetDiscriminatorValue(TClause clause, IJamlValueReader value) => true;
}

/// <summary>
/// The reader a desc uses to pull one value off the document. Deliberately narrow: a desc states
/// what shape it wants and gets it or gets a diagnostic — it never sees the node tree, so the
/// parser stays free to change without every desc changing with it.
/// </summary>
public interface IJamlValueReader
{
    /// <summary>The value as written.</summary>
    string Text { get; }

    /// <summary>Where the value sits, so a bad one underlines itself.</summary>
    JamlSpan Span { get; }

    /// <summary>True when the value is JAML's wildcard. The reader owns this word, so a desc
    /// never spells it — one keyword, one place, however many descs accept it.</summary>
    bool IsAny { get; }

    /// <summary>Reads the value as an int, or reports a diagnostic and returns false.</summary>
    bool TryInt(out int value);

    /// <summary>Reads the value as a bool, or reports a diagnostic and returns false.</summary>
    bool TryBool(out bool value);

    /// <summary>Reads an int array, accepting "1", "[1,2]" and "1-8" alike.</summary>
    bool TryIntArray(out int[] value);

    /// <summary>Reads a card rank, accepting pips (2–10), letters (J/Q/K/A) and names alike.</summary>
    bool TryRank(out MotelyStandardcardRank value);

    /// <summary>Reads one enum member by name, or reports a diagnostic naming the near misses.</summary>
    bool TryEnum<TEnum>(out TEnum value) where TEnum : struct, Enum;

    /// <summary>Reads an enum array, accepting a single bare name as a one-element array.</summary>
    bool TryEnumArray<TEnum>(out TEnum[] value) where TEnum : struct, Enum;
}
