namespace Motely.Filters;

// The event clauses: things that happen on a joker's or card's own PRNG stream rather than in an
// ante. `rolls` are absolute indices into that stream (roll i is the i-th pull), the same indices
// the Jamlyzer's event queues report. Each clause names its stream and its trigger through a
// roll struct; EventFilterDesc walks the stream for all of them.

/// <summary>A clause over one event stream: <c>rolls</c> picks the pulls that count.</summary>
public abstract class EventClause : IRollScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; } = 1;
    public int[] Rolls { get; set; } = [];

    public abstract IMotelySeedFilterDesc CreateFilterDesc();
}

/// <summary>An event whose odds Oops! All 6s doubles: <c>with: { luck: X2 }</c>.</summary>
public abstract class LuckEventClause : EventClause, IWithScopedClause
{
    public MotelyWith With { get; set; } = new();

    protected double Luck => (double)With.Luck;
}

/// <summary>A Lucky card paying $20.</summary>
[YamlDotNet.Serialization.YamlSerializable]
public sealed partial class LuckyMoneyClause : LuckEventClause
{
    public override IMotelySeedFilterDesc CreateFilterDesc() => new EventFilterDesc<LuckyMoneyRoll>(this, new(Luck));
}

/// <summary>A Lucky card giving +20 Mult.</summary>
[YamlDotNet.Serialization.YamlSerializable]
public sealed partial class LuckyMultClause : LuckEventClause
{
    public override IMotelySeedFilterDesc CreateFilterDesc() => new EventFilterDesc<LuckyMultRoll>(this, new(Luck));
}

/// <summary>The Wheel of Fortune applying an edition.</summary>
[YamlDotNet.Serialization.YamlSerializable]
public sealed partial class WheelOfFortuneClause : LuckEventClause
{
    public override IMotelySeedFilterDesc CreateFilterDesc() => new EventFilterDesc<WheelOfFortuneRoll>(this, new(Luck));
}

/// <summary>The Wheel boss blind leaving a card face down.</summary>
[YamlDotNet.Serialization.YamlSerializable]
public sealed partial class WheelStaysFlippedClause : LuckEventClause
{
    public override IMotelySeedFilterDesc CreateFilterDesc() => new EventFilterDesc<WheelStaysFlippedRoll>(this, new(Luck));
}

/// <summary>A Glass card shattering.</summary>
[YamlDotNet.Serialization.YamlSerializable]
public sealed partial class GlassDestroyClause : LuckEventClause
{
    public override IMotelySeedFilterDesc CreateFilterDesc() => new EventFilterDesc<GlassDestroyRoll>(this, new(Luck));
}

/// <summary>Cavendish going extinct.</summary>
[YamlDotNet.Serialization.YamlSerializable]
public sealed partial class CavendishExtinctClause : LuckEventClause
{
    public override IMotelySeedFilterDesc CreateFilterDesc() => new EventFilterDesc<CavendishExtinctRoll>(this, new(Luck));
}

/// <summary>Gros Michel going extinct.</summary>
[YamlDotNet.Serialization.YamlSerializable]
public sealed partial class GrosMichelExtinctClause : LuckEventClause
{
    public override IMotelySeedFilterDesc CreateFilterDesc() => new EventFilterDesc<GrosMichelExtinctRoll>(this, new(Luck));
}

/// <summary>Space Joker levelling up the hand.</summary>
[YamlDotNet.Serialization.YamlSerializable]
public sealed partial class SpaceLevelupClause : LuckEventClause
{
    public override IMotelySeedFilterDesc CreateFilterDesc() => new EventFilterDesc<SpaceLevelupRoll>(this, new(Luck));
}

/// <summary>Business Card paying out: a flat coin flip, so no luck dial.</summary>
[YamlDotNet.Serialization.YamlSerializable]
public sealed partial class BusinessPayoutClause : EventClause
{
    public override IMotelySeedFilterDesc CreateFilterDesc() => new EventFilterDesc<BusinessPayoutRoll>(this, default);
}

/// <summary>Reserved Parking paying out: a flat coin flip.</summary>
[YamlDotNet.Serialization.YamlSerializable]
public sealed partial class ParkingPayoutClause : EventClause
{
    public override IMotelySeedFilterDesc CreateFilterDesc() => new EventFilterDesc<ParkingPayoutRoll>(this, default);
}

/// <summary>Bloodstone triggering: a flat coin flip.</summary>
[YamlDotNet.Serialization.YamlSerializable]
public sealed partial class BloodstoneTriggerClause : EventClause
{
    public override IMotelySeedFilterDesc CreateFilterDesc() => new EventFilterDesc<BloodstoneTriggerRoll>(this, default);
}

/// <summary>Misprint rolling at least <see cref="Mult"/>.</summary>
[YamlDotNet.Serialization.YamlSerializable]
public sealed partial class MisprintMultClause : EventClause
{
    /// <summary>The least Mult a roll must show to count (0..23).</summary>
    public int Mult { get; set; }

    public override IMotelySeedFilterDesc CreateFilterDesc() => new EventFilterDesc<MisprintMultRoll>(this, new(Mult));
}
