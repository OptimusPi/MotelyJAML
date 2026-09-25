using System.Runtime.Intrinsics;

namespace Motely.Filters.Native;

public struct NaNSeedFilterDesc : IMotelySeedFilterDesc<NaNSeedFilterDesc.NaNSeedFilter>
{
    public string[] PseudoHashKeys { get; set; }

    public NaNSeedFilterDesc()
    {
        var keys = new List<string>();

        keys.AddRange([
            "lucky_money",
            "lucky_mult",
            "misprint",
            "bloodstone",
            "parking",
            "business",
            "space",
            "8ball",
            "glass",
            "boss",
            "wheel",
            "hook",
            "cerulean_bell",
            "crimson_heart",
            "wheel_of_fortune",
            "invisible",
            "perkeo",
            "madness",
            "ankh_choice",
            "to_do",
            "marb_fr",
            "cert_fr",
            "certsl",
            "sigil",
            "ouija",
            "familiar_create",
            "grim_create",
            "incantation_create",
            "random_destroy",
            "spe_card",
            "immolate",
            "stdset",
            "stdseal",
            "stdsealtype",
            "omen_globe",
            "illusion",
            "boss",
            "wheel",
            "hook",
            "cerulean_bell",
            "crimson_heart",
            "aura",
            "edition_generic",
            "flipped_card",
            "edition_deck",
            "erratic",
            "orbital",
        ]);

        for (int ante = 1; ante <= 8; ante++)
        {
            keys.Add($"halu{ante}");
            keys.Add($"stdset{ante}");
            keys.Add($"stdseal{ante}");
            keys.Add($"stdsealtype{ante}");
            keys.Add($"cdt{ante}");
            keys.Add($"rarity{ante}");
            keys.Add($"standard_edition{ante}");
            keys.Add($"etperpoll{ante}");
            keys.Add($"packetper{ante}");
            keys.Add($"ssjr{ante}");
            keys.Add($"packssjr{ante}");
            keys.Add($"idol{ante}");
            keys.Add($"mail{ante}");
            keys.Add($"anc{ante}");
            keys.Add($"cas{ante}");

            keys.Add($"soul_Tarot{ante}");
            keys.Add($"soul_Planet{ante}");
            keys.Add($"soul_Spectral{ante}");

            keys.Add($"ediar1{ante}");
            keys.Add($"edipl1{ante}");
            keys.Add($"edispe{ante}");
            keys.Add($"edista{ante}");
            keys.Add($"edibuf{ante}");
            keys.Add($"edisho{ante}");

            keys.Add($"frontar1{ante}");
            keys.Add($"frontpl1{ante}");
            keys.Add($"frontspe{ante}");
            keys.Add($"frontsta{ante}");
            keys.Add($"frontbuf{ante}");
            keys.Add($"frontsho{ante}");
        }

        PseudoHashKeys = [.. keys];
    }

    public readonly NaNSeedFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        foreach (var key in PseudoHashKeys)
        {
            ctx.CachePseudoHash(key);
        }
        return new NaNSeedFilter(PseudoHashKeys);
    }

    public struct NaNSeedFilter(string[] pseudoHashKeys) : IMotelySeedFilter
    {
        public readonly string[] PseudoHashKeys = pseudoHashKeys;

        public readonly VectorMask Filter(ref MotelyVectorSearchContext searchContext)
        {
            VectorMask resultMask = VectorMask.NoBitsSet;

            for (int i = 0; i < PseudoHashKeys.Length; i++)
            {
                var key = PseudoHashKeys[i];
                MotelyVectorPrngStream stream = searchContext.CreatePrngStream(key, true);
                VectorMask resultMask3p2 = Vector512.Equals(
                    stream.State,
                    Vector512.Create(0.3211483013596)
                );
                resultMask |= resultMask3p2;
            }
            return resultMask;
        }
    }
}
