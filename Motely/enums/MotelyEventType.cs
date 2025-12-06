namespace Motely;

/// <summary>
/// Types of random events that can be filtered in searches
/// </summary>
public enum MotelyEventType
{
    /// <summary>Lucky card triggers $1 money drop</summary>
    LuckyMoney,

    /// <summary>Lucky card triggers +mult</summary>
    LuckyMult,

    /// <summary>Misprint joker mult value roll</summary>
    MisprintMult,

    /// <summary>Wheel of Fortune tarot gives edition to random joker</summary>
    WheelOfFortune,

    /// <summary>Cavendish banana goes extinct (destroys itself)</summary>
    CavendishExtinct,

    /// <summary>Gros Michel banana goes extinct (destroys itself and gives Cavendish)</summary>
    GrosMichelExtinct,
}
