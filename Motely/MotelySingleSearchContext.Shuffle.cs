namespace Motely;

public partial class MotelySingleSearchContext
{
    public void Shuffle(string seed, Span<MotelyItem> deck, int advance = 0)
    {
        MotelySinglePrngStream stream = CreatePrngStream(seed);
        for (int i = 0; i < advance; i++)
            GetNextPrngState(ref stream);
        LuaRandom random = GetNextLuaRandom(ref stream);

        for (int i = deck.Length - 1; i > 0; i--)
        {
            int j = random.RandInt(0, i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
    }
}
