using Motely.Filters.Native;

namespace Motely.Analysis;

public sealed class MotelySeedRouterDesc : IMotelySeedRouterDesc, IDisposable
{
    private MotelySearchParameters _searchParams;
    private MotelySearchContextParams _contextParams;
    private int _lane;
    private readonly IMotelySearch? _ownedSearch;

    public MotelySeedRouterDesc(string seed, MotelyDeck deck, MotelyStake stake)
    {
        PassthroughFilterDesc filterDesc = new();
        var settings = new MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>(filterDesc)
            .WithDeck(deck)
            .WithStake(stake)
            .WithSeedList([seed])
            .WithThreadCount(1)
            .WithSeedRouter(this);
        _ownedSearch = settings.CreateSearch();
        _ownedSearch.Start();
        _ownedSearch.AwaitCompletion();
    }

    IMotelySeedRouter IMotelySeedRouterDesc.CreateSeedRouter(ref MotelyFilterCreationContext ctx)
    {
        return new ContextCapturingRouter(this);
    }

    private readonly struct ContextCapturingRouter(MotelySeedRouterDesc desc) : IMotelySeedRouter
    {
        public void InjectSingleSeedContext(in MotelySingleSearchContext ctx)
        {
            desc._searchParams = ctx.SearchParameters;
            desc._contextParams = ctx.SearchContextParams;
            desc._lane = ctx.VectorLane;
        }
    }

    public MotelySingleSearchContext Instance()
    {
        return new MotelySingleSearchContext(in _searchParams, in _contextParams, _lane);
    }

    public void Dispose() => _ownedSearch?.Dispose();
}
