using System.Runtime.CompilerServices;

namespace Motely.Filters;

public struct MotelyScoredSeedResult : IMotelySeedScores
{
    public const int MAX_TALLY_COUNT = 256;

    public int Score { get; set; }
    public string Seed { get; set; }
    private int _tallyCount;
    private int[] _tallyValues = new int[MAX_TALLY_COUNT];

    public MotelyScoredSeedResult() => Seed = string.Empty;

    public readonly byte[] Tally
    {
        get
        {
            var tally = new byte[_tallyCount];
            for (int i = 0; i < _tallyCount; i++)
                tally[i] = (byte)_tallyValues[i];
            return tally;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset(string seed, int score = 0)
    {
        Seed = seed;
        Score = score;
        _tallyCount = 0;
        _tallyValues ??= new int[MAX_TALLY_COUNT];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddTally(int value)
    {
        _tallyValues[_tallyCount] = value;
        _tallyCount++;
    }

    public readonly int GetTally(int index)
    {
        if (index < 0 || index >= _tallyCount)
            return 0;
        return _tallyValues[index];
    }

    public readonly int TallyCount
    {
        get { return _tallyCount; }
    }

    public readonly ReadOnlySpan<int> TallyValuesSpan
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _tallyValues.AsSpan(0, _tallyCount);
    }

    public int[] Tallies
    {
        readonly get => _tallyValues.AsSpan(0, _tallyCount).ToArray();
        set
        {
            _tallyValues ??= new int[MAX_TALLY_COUNT];
            _tallyCount = Math.Min(value?.Length ?? 0, MAX_TALLY_COUNT);
            for (int i = 0; i < _tallyCount; i++)
                _tallyValues[i] = value![i];
        }
    }
}
