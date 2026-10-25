using Motely.Filters;

namespace Motely.Tests;

/// <summary>
/// A wrong key fails to load with one line, on the key's own JAML line, naming the key meant.
/// A clause mapping with no clause key used to throw a NullReferenceException: the discriminator
/// had scanned the whole buffered mapping, and the error was built from the buffer's position
/// past its end.
/// </summary>
public sealed class JamlKeyErrorTests
{
    private static string LoadError(string yaml)
    {
        Assert.False(JamlConfigLoader.TryLoad(yaml, out _, out var error), "the filter loaded");
        return error!;
    }

    [Fact]
    public void UnknownClauseKey_NamesTheClosest_OnItsLine()
    {
        Assert.Equal(
            "JAML line 4: 'luckMoney' is not a clause key; did you mean `luckyMoney`?",
            LoadError("name: x\nmust:\n  - joker: Blueprint\n  - luckMoney: [0, 1]\n    min: 2\n"));
    }

    [Fact]
    public void UnknownClauseKey_AfterAnotherKey_PointsAtThatKey()
    {
        Assert.Equal(
            "JAML line 4: 'luckMoney' is not a clause key; did you mean `luckyMoney`?",
            LoadError("must:\n  - score: 5\n    min: 1\n    luckMoney: [0]\n"));
    }

    [Fact]
    public void NoClauseKeyAtAll_IsAnError_NotACrash()
    {
        var error = LoadError("must:\n  - zzzzzz: 3\n");
        Assert.StartsWith("JAML line 2: no clause key among zzzzzz: a clause starts with a key such as `joker`", error);
    }

    [Fact]
    public void EmptyClauseMapping_IsAnError_NotACrash()
    {
        Assert.Equal("JAML line 2: an empty clause: write one such as `joker: Blueprint`.", LoadError("must:\n  - {}\n"));
    }

    [Fact]
    public void ClauseWrittenAsAList_IsAnError_NotACrash()
    {
        Assert.StartsWith("JAML line 2: a clause is a mapping such as `joker: Blueprint`", LoadError("must:\n  - [joker, Blueprint]\n"));
    }

    [Fact]
    public void TypeValueClause_NamesTheKeyForm()
    {
        var error = LoadError("{\"must\": [{\"type\": \"Joker\", \"value\": \"OopsAll6s\"}]}");
        Assert.Equal(
            "JAML line 1: a clause names its kind by its key, not by `type:` and `value:`: write `joker: Blueprint` for { type: Joker, value: Blueprint }.",
            error);
    }

    [Fact]
    public void TwoClauseKeysInOneMapping_SaysToSplitThem()
    {
        var error = LoadError("""
            should:
              - and:
                - smallBlindTag: NegativeTag
                  or:
                  - joker: Photograph
                  antes: [3]
            """);
        Assert.Equal("JAML line 4: one clause has two clause keys, `smallBlindTag` and `or`: write each as its own `- ` item, as in `- or:`.", error);
    }

    [Fact]
    public void ASourcesKey_BesideTheClauseKey_SaysItGoesUnderSources()
    {
        // boosterPacks is a clause key too, so this reads as two clauses in one mapping.
        Assert.Equal(
            "JAML line 3: 'boosterPacks' on a `rareJoker` clause goes under `sources:`, as in `sources: { boosterPacks: [0] }`.",
            LoadError("must:\n  - rareJoker: Any\n    boosterPacks: [0]\n"));
    }

    [Fact]
    public void ASecondClauseKeyTheTypeTakes_IsNotTwoClauses()
    {
        // soulOrder takes boosterPacks as its own key.
        var config = ProofSearch.LoadOrThrow("must:\n  - soulOrder: [{ legendary: Perkeo }]\n    boosterPacks: [0, 1]\n");
        Assert.IsType<LegendarySoulOrderClause>(Assert.Single(config.Must));
    }

    [Theory]
    [InlineData("must:\n  - joker: Blueprint\n    antse: [1]\n", "JAML line 3: 'antse' is not a key of a `joker` clause; did you mean `antes`?")]
    [InlineData("must:\n  - joker: Blueprint\n    sources:\n      shopItem: [1]\n", "JAML line 4: 'shopItem' is not a key of `sources:` on a `joker` clause; did you mean `shopItems`?")]
    [InlineData("must:\n  - joker: Blueprint\n    shopItems: [1]\n", "JAML line 3: 'shopItems' is not a key of a `joker` clause: it goes under `sources:`, as in `sources: { shopItems: [0] }`.")]
    [InlineData("nmae: x\n", "JAML line 1: 'nmae' is not a key of a filter; did you mean `name`?")]
    public void UnknownKey_NamesTheKeyMeant(string yaml, string message)
    {
        Assert.Equal(message, LoadError(yaml));
    }
}
