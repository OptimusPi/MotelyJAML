using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely.Filters;

/// <summary>One event stream: where it starts, and whether a pull from it is a hit.</summary>
public interface IEventRoll
{
    MotelyVectorPrngStream Create(ref MotelyVectorSearchContext ctx);
    VectorMask Next(ref MotelyVectorSearchContext ctx, ref MotelyVectorPrngStream stream);
}

/// <summary>
/// The SIMD filter behind every <see cref="EventClause"/>: walks the event's stream from the
/// first pull to the last requested roll, counts the hits on the requested rolls, and keeps the
/// lanes whose count sits inside the clause's min and max. Exact: the scorer counts the same
/// pulls, so a match here needs no re-evaluation. <typeparamref name="TRoll"/> is a struct, so
/// each event gets its own specialised loop with no call through an interface.
/// </summary>
public readonly struct EventFilterDesc<TRoll>(EventClause clause, TRoll roll)
    : IMotelySeedFilterDesc<EventFilterDesc<TRoll>.EventFilter>
    where TRoll : struct, IEventRoll
{
    public EventFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        int[] rolls = [.. clause.Rolls.Order()];
        return new(rolls, clause.Min, clause.Max, roll);
    }

    public readonly struct EventFilter(int[] rolls, int min, int? max, TRoll roll) : IMotelySeedFilter
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            var stream = roll.Create(ref ctx);
            var counts = Vector256<int>.Zero;
            var floor = Vector256.Create(min);
            int next = 0;

            for (int index = 0; index <= rolls[^1]; index++)
            {
                VectorMask hit = roll.Next(ref ctx, ref stream);
                if (index != rolls[next])
                    continue;
                next++;
                SimdPackSupport.AddMatchCounts(hit, ref counts);

                // With no ceiling, stop once every lane has either reached min or can no longer.
                if (max is null)
                {
                    var reached = Vector256.GreaterThanOrEqual(counts, floor);
                    var lost = Vector256.LessThan(Vector256.Add(counts, Vector256.Create(rolls.Length - next)), floor);
                    if (Vector256.BitwiseOr(reached, lost).ExtractMostSignificantBits() == 0xFF)
                        break;
                }
            }

            return SimdPackSupport.MeetsMinMaxMask(counts, min, max);
        }
    }
}

public readonly struct LuckyMoneyRoll(double luck) : IEventRoll
{
    public MotelyVectorPrngStream Create(ref MotelyVectorSearchContext ctx) => ctx.CreateLuckyCardMoneyStream();
    public VectorMask Next(ref MotelyVectorSearchContext ctx, ref MotelyVectorPrngStream stream) => ctx.GetNextLuckyMoney(ref stream, luck);
}

public readonly struct LuckyMultRoll(double luck) : IEventRoll
{
    public MotelyVectorPrngStream Create(ref MotelyVectorSearchContext ctx) => ctx.CreateLuckyCardMultStream();
    public VectorMask Next(ref MotelyVectorSearchContext ctx, ref MotelyVectorPrngStream stream) => ctx.GetNextLuckyMult(ref stream, luck);
}

public readonly struct WheelOfFortuneRoll(double luck) : IEventRoll
{
    public MotelyVectorPrngStream Create(ref MotelyVectorSearchContext ctx) => ctx.CreateWheelOfFortuneStream();
    public VectorMask Next(ref MotelyVectorSearchContext ctx, ref MotelyVectorPrngStream stream) =>
        ~VectorEnum256.Equals(ctx.GetNextWheelOfFortune(ref stream, luck), MotelyItemEdition.None);
}

public readonly struct WheelStaysFlippedRoll(double luck) : IEventRoll
{
    public MotelyVectorPrngStream Create(ref MotelyVectorSearchContext ctx) => ctx.CreateTheWheelPrngStream();
    public VectorMask Next(ref MotelyVectorSearchContext ctx, ref MotelyVectorPrngStream stream) => ctx.GetNextWheelStaysFlipped(ref stream, luck);
}

public readonly struct GlassDestroyRoll(double luck) : IEventRoll
{
    public MotelyVectorPrngStream Create(ref MotelyVectorSearchContext ctx) => ctx.CreateGlassPrngStream();
    public VectorMask Next(ref MotelyVectorSearchContext ctx, ref MotelyVectorPrngStream stream) => ctx.GetNextGlassDestroy(ref stream, luck);
}

public readonly struct CavendishExtinctRoll(double luck) : IEventRoll
{
    public MotelyVectorPrngStream Create(ref MotelyVectorSearchContext ctx) => ctx.CreateCavendishPrngStream(false);
    public VectorMask Next(ref MotelyVectorSearchContext ctx, ref MotelyVectorPrngStream stream) => ctx.GetNextCavendishExtinct(ref stream, luck);
}

public readonly struct GrosMichelExtinctRoll(double luck) : IEventRoll
{
    public MotelyVectorPrngStream Create(ref MotelyVectorSearchContext ctx) => ctx.CreateGrosMichelPrngStream(false);
    public VectorMask Next(ref MotelyVectorSearchContext ctx, ref MotelyVectorPrngStream stream) => ctx.GetNextGrosMichelExtinct(ref stream, luck);
}

public readonly struct SpaceLevelupRoll(double luck) : IEventRoll
{
    public MotelyVectorPrngStream Create(ref MotelyVectorSearchContext ctx) => ctx.CreateSpacePrngStream();
    public VectorMask Next(ref MotelyVectorSearchContext ctx, ref MotelyVectorPrngStream stream) => ctx.GetNextSpaceLevelup(ref stream, luck);
}

public readonly struct BusinessPayoutRoll : IEventRoll
{
    public MotelyVectorPrngStream Create(ref MotelyVectorSearchContext ctx) => ctx.CreateBusinessPrngStream();
    public VectorMask Next(ref MotelyVectorSearchContext ctx, ref MotelyVectorPrngStream stream) => ctx.GetNextBusinessPayout(ref stream);
}

public readonly struct ParkingPayoutRoll : IEventRoll
{
    public MotelyVectorPrngStream Create(ref MotelyVectorSearchContext ctx) => ctx.CreateParkingPrngStream();
    public VectorMask Next(ref MotelyVectorSearchContext ctx, ref MotelyVectorPrngStream stream) => ctx.GetNextParkingPayout(ref stream);
}

public readonly struct BloodstoneTriggerRoll : IEventRoll
{
    public MotelyVectorPrngStream Create(ref MotelyVectorSearchContext ctx) => ctx.CreateBloodstonePrngStream();
    public VectorMask Next(ref MotelyVectorSearchContext ctx, ref MotelyVectorPrngStream stream) => ctx.GetNextBloodstoneTrigger(ref stream);
}

public readonly struct MisprintMultRoll(int mult) : IEventRoll
{
    private readonly Vector256<int> _floor = Vector256.Create(mult);

    public MotelyVectorPrngStream Create(ref MotelyVectorSearchContext ctx) => ctx.CreateMisprintPrngStream();
    public VectorMask Next(ref MotelyVectorSearchContext ctx, ref MotelyVectorPrngStream stream) =>
        new(MotelyVectorUtils.VectorizedComparisonToMask(Vector256.GreaterThanOrEqual(ctx.GetNextMisprintMult(ref stream), _floor)));
}
