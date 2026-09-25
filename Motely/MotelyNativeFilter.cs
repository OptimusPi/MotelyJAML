#nullable enable
using Motely.Filters;

namespace Motely;

public enum MotelyNativeFilter
{
    PerkeoObservatory,
    Observatory,
    Trickeoglyph,
    NaturalNegatives,
    NegativePerkeo,
    NegativeCopy,
    ShuffleFinder,
    ErraticFinder,
    FilledSoul,
    LuckyCard,
    NanSeed,
    NegativeTag,
    TwoBlackHole,
}

public static class MotelyNativeFilterNames
{
    public static readonly string[] DisplayNames =
    [
        nameof(MotelyNativeFilter.PerkeoObservatory),
        nameof(MotelyNativeFilter.Observatory),
        nameof(MotelyNativeFilter.Trickeoglyph),
        nameof(MotelyNativeFilter.NaturalNegatives),
        nameof(MotelyNativeFilter.NegativePerkeo),
        nameof(MotelyNativeFilter.NegativeCopy),
        nameof(MotelyNativeFilter.ShuffleFinder),
        nameof(MotelyNativeFilter.ErraticFinder),
        nameof(MotelyNativeFilter.FilledSoul),
        nameof(MotelyNativeFilter.LuckyCard),
        nameof(MotelyNativeFilter.NanSeed),
        nameof(MotelyNativeFilter.NegativeTag),
        nameof(MotelyNativeFilter.TwoBlackHole),
    ];

    public static bool TryParse(string name, out MotelyNativeFilter filter)
    {
        switch (name.Trim().ToLowerInvariant())
        {
            case "perkeoobservatory":
                filter = MotelyNativeFilter.PerkeoObservatory;
                return true;
            case "observatory":
                filter = MotelyNativeFilter.Observatory;
                return true;
            case "trickeoglyph":
                filter = MotelyNativeFilter.Trickeoglyph;
                return true;
            case "naturalnegatives":
                filter = MotelyNativeFilter.NaturalNegatives;
                return true;
            case "negativeperkeo":
                filter = MotelyNativeFilter.NegativePerkeo;
                return true;
            case "negativecopy":
                filter = MotelyNativeFilter.NegativeCopy;
                return true;
            case "shufflefinder":
                filter = MotelyNativeFilter.ShuffleFinder;
                return true;
            case "erraticfinder":
                filter = MotelyNativeFilter.ErraticFinder;
                return true;
            case "filledsoul":
                filter = MotelyNativeFilter.FilledSoul;
                return true;
            case "luckycard":
                filter = MotelyNativeFilter.LuckyCard;
                return true;
            case "nanseed":
                filter = MotelyNativeFilter.NanSeed;
                return true;
            case "negativetag":
                filter = MotelyNativeFilter.NegativeTag;
                return true;
            case "twoblackhole":
                filter = MotelyNativeFilter.TwoBlackHole;
                return true;
            default:
                filter = default;
                return false;
        }
    }
}

public static class MotelyNativeFilterFactory
{
    public static IMotelySearchSettings CreateSettings(MotelyNativeFilter filter) =>
        filter switch
        {
            MotelyNativeFilter.PerkeoObservatory =>
                new MotelySearchSettings<PerkeoObservatoryFilterDesc.PerkeoObservatoryFilter>(
                    new PerkeoObservatoryFilterDesc()
                ),
            MotelyNativeFilter.Observatory =>
                new MotelySearchSettings<ObservatoryDesc.ObservatoryFilter>(new ObservatoryDesc()),
            MotelyNativeFilter.Trickeoglyph =>
                new MotelySearchSettings<TrickeoglyphFilterDesc.TrickeoglyphFilter>(
                    new TrickeoglyphFilterDesc()
                ),
            MotelyNativeFilter.NaturalNegatives =>
                new MotelySearchSettings<NaturalNegativesFilterDesc.NaturalNegativesFilter>(
                    new NaturalNegativesFilterDesc()
                ),
            MotelyNativeFilter.NegativePerkeo =>
                new MotelySearchSettings<NegativePerkeoFilterDescOld.FilterStruct>(
                    new NegativePerkeoFilterDescOld()
                ),
            MotelyNativeFilter.NegativeCopy =>
                new MotelySearchSettings<NegativeCopyFilterDesc.NegativeCopyFilter>(
                    new NegativeCopyFilterDesc()
                ),
            MotelyNativeFilter.ShuffleFinder =>
                new MotelySearchSettings<ShuffleFinderFilterDesc.ShuffleFinderFilter>(
                    new ShuffleFinderFilterDesc()
                ),
            MotelyNativeFilter.ErraticFinder =>
                new MotelySearchSettings<ErraticFinderDesc.FilterStruct>(new ErraticFinderDesc()),
            MotelyNativeFilter.FilledSoul =>
                new MotelySearchSettings<FilledSoulFilterDesc.FilterStruct>(
                    new FilledSoulFilterDesc()
                ),
            MotelyNativeFilter.LuckyCard =>
                new MotelySearchSettings<LuckCardFilterDesc.LuckyCardFilter>(
                    new LuckCardFilterDesc()
                ),
            MotelyNativeFilter.NanSeed => new MotelySearchSettings<NaNSeedFilterDesc.NaNSeedFilter>(
                new NaNSeedFilterDesc()
            ),
            MotelyNativeFilter.NegativeTag =>
                new MotelySearchSettings<NegativeTagFilterDesc.NegativeTagFilter>(
                    new NegativeTagFilterDesc()
                ),
            MotelyNativeFilter.TwoBlackHole =>
                new MotelySearchSettings<TwoBlackHoleFilterDesc.TwoBlackHoleFilter>(
                    new TwoBlackHoleFilterDesc()
                ),
            _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, null),
        };
}
