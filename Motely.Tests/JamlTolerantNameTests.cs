using Motely.Filters;
using Motely.Schema;

namespace Motely.Tests;

/// <summary>
/// A name in a mapping reads as loosely as in shorthand: any case, spaces or punctuation, and a
/// leading "The" either way — when exactly one name fits. A name that belongs to another kind of
/// thing fails with the key it goes under.
/// </summary>
public sealed class JamlTolerantNameTests
{
    private static string LoadError(string yaml)
    {
        Assert.False(JamlConfigLoader.TryLoad(yaml, out _, out var error), "the filter loaded");
        return error!;
    }

    [Theory]
    [InlineData("voucher: Reroll Surplus", "voucher: RerollSurplus")]
    [InlineData("boss: The Hook", "boss: TheHook")]
    [InlineData("boss: the_hook", "boss: TheHook")]
    [InlineData("boss: Hook", "boss: TheHook")]
    [InlineData("tarotCard: Fool", "tarotCard: TheFool")]
    [InlineData("joker: The Luchador", "joker: Luchador")]
    [InlineData("joker: [blue print, Invisible Joker, oops all 6s]", "joker: [Blueprint, InvisibleJoker, OopsAll6s]")]
    [InlineData("tag: Charm Tag", "tag: CharmTag")]
    [InlineData("spectralCard: Black Hole", "spectralCard: BlackHole")]
    [InlineData("standardCard:\n      rank: K\n      suit: Hearts", "standardCard:\n      rank: King\n      suit: Hearts")]
    [InlineData("standardCard:\n      rank: q", "standardCard:\n      rank: Queen")]
    [InlineData("erraticRank: A", "erraticRank: Ace")]
    [InlineData("erraticRank: J", "erraticRank: Jack")]
    public void LooseName_LoadsTheSameClauseAsTheExactName(string loose, string exact)
    {
        Assert.Equal(
            TwinFilters.AllFields(ProofSearch.LoadOrThrow($"must:\n  - {exact}\n")),
            TwinFilters.AllFields(ProofSearch.LoadOrThrow($"must:\n  - {loose}\n")));
    }

    [Fact]
    public void LooseNames_FindExactlyWhatExactNamesFind()
    {
        TwinFilters.AssertSame(
            """
            should:
              - voucher: RerollSurplus
                antes: [1, 2]
              - boss: TheHook
                antes: [1, 2, 3]
              - tarotCard: TheFool
                antes: [1, 2]
              - joker: [Luchador, InvisibleJoker]
                antes: [1, 2]
              - standardCard:
                  rank: King
                antes: [1, 2]
            """,
            """
            should:
              - voucher: Reroll Surplus
                antes: [1, 2]
              - boss: the hook
                antes: [1, 2, 3]
              - tarotCard: Fool
                antes: [1, 2]
              - joker: [The Luchador, invisible joker]
                antes: [1, 2]
              - standardCard:
                  rank: K
                antes: [1, 2]
            """);
    }

    [Theory]
    [InlineData("standardCard: Certificate", "'Certificate' is an uncommon joker, not a standard card: write `uncommonJoker: Certificate` or `joker: Certificate`.")]
    [InlineData("commonJoker: Showman", "'Showman' is an uncommon joker, not a common joker: write `uncommonJoker: Showman` or `joker: Showman`.")]
    [InlineData("boss: The Luchador", "'The Luchador' is an uncommon joker, not a boss: write `uncommonJoker: Luchador` or `joker: Luchador`.")]
    [InlineData("joker: The Fool", "'The Fool' is a tarot card, not a joker: write `tarotCard: TheFool`.")]
    [InlineData("rareJoker: Perkeo", "'Perkeo' is a legendary joker, not a rare joker: write `legendaryJoker: Perkeo`.")]
    [InlineData("joker: Blueprnt", "'Blueprnt' is not a joker name; did you mean Blueprint?")]
    [InlineData("voucher: Telescop", "'Telescop' is not a voucher name; did you mean Telescope?")]
    public void NameOfAnotherKind_NamesTheKeyItGoesUnder(string clause, string message)
    {
        Assert.Equal($"JAML line 2: {message}", LoadError($"must:\n  - {clause}\n"));
    }

    [Fact]
    public void ANumberIsStillNoRank()
    {
        // Letters are the card's own; a number stays refused (Enum.Parse read 2 as Four).
        Assert.StartsWith("JAML line 2: '2' is not a name", LoadError("must:\n  - erraticRank: 2\n"));
    }

    [Fact]
    public void EveryEnumName_IsOneNameWhenSpelledLoosely()
    {
        // The loose match accepts only a single fit; this pins that no JAML enum has two names
        // that normalize alike, so no exact name ever became unreachable or ambiguous.
        foreach (var (type, _) in FilterVocabulary.ValueEnumKinds)
        {
            var clashes = Enum.GetNames(type)
                .GroupBy(JamlKeys.Normalize)
                .Where(g => g.Count() > 1)
                .Select(g => string.Join("/", g));
            Assert.True(!clashes.Any(), $"{type.Name}: {string.Join(", ", clashes)}");
            foreach (var name in Enum.GetNames(type))
                Assert.Equal(name, Enum.GetName(type, JamlEnumName.Parse(type, name.ToLowerInvariant())));
        }
    }
}
