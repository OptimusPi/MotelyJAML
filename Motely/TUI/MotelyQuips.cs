namespace Motely.TUI;

/// <summary>
/// Quirky quips and JAML JOTD (Joke Of The Day) for the main menu.
/// Themes: JAML, Motely, SIMD, CPU, Speed, Search, Rare Seeds, Legendary, Balatro, Joker, Jimbo, pifreak
/// </summary>
public static class MotelyQuips
{
    private static readonly Random _random = new();

    /// <summary>
    /// JAML JOTD - Jokey acronym expansions for "JAML"
    /// Displayed as subtitle under the JAML logo
    /// </summary>
    public static readonly string[] JamlJotd =
    [
        // The real one
        "Joker Ante Markup Language",
        // Jimbo themed
        "Jimbo's Ante Markup Language",
        "Jimbo And Motely? Lovely!",
        "Jimbo Approves My Luck",
        "Jimbo's Amazing Multiplier Locator",
        // Poker/card puns
        "Jolly Aces Make Legendaries",
        "Jackpot Ante Modifier Language",
        "Just Another Multiplier Legend",
        "Jokers Always Mean Luck",
        "Just Ace Me Lucky",
        // Programming jokes
        "Java? Assembly?? Machine Language?!",
        "Just Another Markup Language",
        "JSON's Awesome Markup Lovechild",
        "YAML Already Made Lemonade",
        // Balatro specific
        "Jolly Ante Manipulation Lab",
        "Just Another Motely Launcher",
        "Jokers Acquired, Multipliers Loaded",
        "Just Ante My Life",
        // Silly/absurd
        "Jumbled Assorted Microprocessor Luggage",
        "Jolly And Merry Love",
        "Jimbos Absolutely Magnificent Laughter",
        "Just Add More Legendaries",
        "Jokes And Memes, Literally",
        // Search themed
        "Just Another Million Loops",
        "Join And Match Legendaries",
        "Juggling All Matching Loots",
        // Speed themed
        "Juiced And Massively Loaded",
        "Jetting At Maximum Lightspeed",
        // Dad jokes
        "Jokes Are My Legacy",
        "Jesting About My Luck",
        "Joyful And Maybe Lucky",
    ];

    /// <summary>
    /// Gets a random JAML JOTD (Joke Of The Day) acronym expansion.
    /// </summary>
    public static string GetRandomJamlJotd() => JamlJotd[_random.Next(JamlJotd.Length)];
}
