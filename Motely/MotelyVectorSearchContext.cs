using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely;

public struct MotelyVectorPrngStream(Vector512<double> state)
{
    public static MotelyVectorPrngStream Invalid => new(Vector512.CreateScalar(-1.0));

    public Vector512<double> State = state;
    public readonly bool IsInvalid => State[0] < 0;

    public readonly MotelySinglePrngStream CreateSingleStream(int lane)
    {
        Debug.Assert(!IsInvalid, "Invalid PRNG stream - cursor setup failed");
        return new MotelySinglePrngStream(State[lane]);
    }
}

public struct MotelyVectorResampleStream(MotelyVectorPrngStream initialPrngStream, bool isCached)
{
    public static MotelyVectorResampleStream Invalid => new(MotelyVectorPrngStream.Invalid, false);

    public const int StackResampleCount = 8;

    [InlineArray(StackResampleCount)]
    public struct MotelyResampleStreams
    {
        public MotelyVectorPrngStream PrngStream;
    }

    public MotelyVectorPrngStream InitialPrngStream = initialPrngStream;
    public MotelyResampleStreams ResamplePrngStreams;
    public int ResamplePrngStreamInitCount;

    public List<StrongBox<MotelyVectorPrngStream>>? HighResamplePrngStreams;
    public bool IsCached = isCached;
    public readonly bool IsInvalid => InitialPrngStream.IsInvalid;

    public readonly MotelySingleResampleStream CreateSingleStream(int lane)
    {
        Debug.Assert(!IsInvalid, "Invalid resample stream - cursor setup failed");

        MotelySingleResampleStream stream = new()
        {
            InitialPrngStream = InitialPrngStream.CreateSingleStream(lane),
            ResamplePrngStreamInitCount = ResamplePrngStreamInitCount,
            IsCached = IsCached,
        };

        for (int i = 0; i < ResamplePrngStreamInitCount; i++)
        {
            stream.ResamplePrngStreams[i] = ResamplePrngStreams[i].CreateSingleStream(lane);
        }

        if (HighResamplePrngStreams != null)
        {
            stream.HighResamplePrngStreams = new List<object>(HighResamplePrngStreams.Count);

            for (int i = 0; i < HighResamplePrngStreams.Count; i++)
            {
                stream.HighResamplePrngStreams.Add(
                    HighResamplePrngStreams[i].Value.CreateSingleStream(lane)
                );
            }
        }

        return stream;
    }
}

public delegate int MotelyIndividualSeedSearcher(MotelySingleSearchContext searchContext);

internal readonly unsafe struct MotelySearchContextParams(
    PartialSeedHashCache* seedHashCache,
    int seedLength,
    int firstCharactersLength,
    char* seedFirstCharacters,
    Vector512<double>* seedLastCharacters,
    bool isAdditionalFilter = false
)
{
    public readonly PartialSeedHashCache* SeedHashCache = seedHashCache;
    public readonly int SeedLength = seedLength;
    public readonly int SeedFirstCharactersLength = firstCharactersLength;
    public readonly int SeedLastCharactersLength => SeedLength - SeedFirstCharactersLength;

    public readonly char* SeedFirstCharacters = seedFirstCharacters;

    public readonly Vector512<double>* SeedLastCharacters = seedLastCharacters;
    public readonly bool IsAdditionalFilter = isAdditionalFilter;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsLaneValid(int lane)
    {
        if (SeedFirstCharactersLength == SeedLength)
            return lane == 0;

        return ((double*)&SeedLastCharacters[0])[lane] != '\0';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly string GetSeed(int lane)
    {
        char* seed = stackalloc char[MotelyGlobals.MaxSeedLength];
        int length = GetSeed(lane, seed);
        return new string(seed, 0, length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int GetSeed(int lane, char* output)
    {
        Debug.Assert(IsLaneValid(lane));

        int i = 0;

        for (; i < SeedLastCharactersLength; i++)
        {
            output[i] = (char)
                ((double*)SeedLastCharacters)[i * MotelyGlobals.MaxVectorWidth + lane];
        }

        for (; i < SeedLength; i++)
        {
            output[i] = SeedFirstCharacters[i - SeedLastCharactersLength];
        }

        return SeedLength;
    }
}

internal static class MotelyVectorConstants
{
    public static readonly Vector512<double> PrngMultiplier = Vector512.Create(1.72431234);
    public static readonly Vector512<double> PrngAddend = Vector512.Create(2.134453429141);
    public static readonly Vector512<double> PrngRoundingFactor = Vector512.Create(1e13);

    public static readonly Vector512<double> PrngMagicNumber = Vector512.Create(4503599627370496.0);

    public static readonly Vector512<double> HashConstant = Vector512.Create(1.1239285023);
    public static readonly Vector512<double> Pi = Vector512.Create(Math.PI);

    public static readonly Vector512<double> Two = Vector512.Create(2.0);
}

public readonly unsafe ref partial struct MotelyVectorSearchContext
{
    internal const int MotelyVectorResampleLimit = 64;

    private readonly ref readonly MotelySearchParameters _searchParameters;
    private readonly ref readonly MotelySearchContextParams _contextParams;

    public MotelyStake Stake => _searchParameters.Stake;
    public MotelyDeck Deck => _searchParameters.Deck;

    private PartialSeedHashCache* SeedHashCache => _contextParams.SeedHashCache;
    private int SeedLength => _contextParams.SeedLength;
    private char* SeedFirstCharacters => _contextParams.SeedFirstCharacters;
    private int SeedFirstCharactersLength => _contextParams.SeedFirstCharactersLength;
    private int SeedLastCharactersLength => _contextParams.SeedLastCharactersLength;
    private Vector512<double>* SeedLastCharacters => _contextParams.SeedLastCharacters;
    private bool IsAdditionalFilter => _contextParams.IsAdditionalFilter;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal MotelyVectorSearchContext(
        ref readonly MotelySearchParameters searchParameters,
        ref readonly MotelySearchContextParams contextParams
    )
    {
        _contextParams = ref contextParams;
        _searchParameters = ref searchParameters;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsLaneValid(int lane) => _contextParams.IsLaneValid(lane);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string GetSeed(int lane) => _contextParams.GetSeed(lane);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetSeed(int lane, char* output) => _contextParams.GetSeed(lane, output);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VectorMask SearchIndividualSeeds(MotelyIndividualSeedSearcher searcher, int scoreCutoff = 1)
    {
        uint results = 0;

        for (int lane = 0; lane < MotelyGlobals.MaxVectorWidth; lane++)
        {
            if (IsLaneValid(lane))
            {
                MotelySingleSearchContext singleSearchContext = new(
                    in _searchParameters,
                    in _contextParams,
                    lane
                );

                if (searcher(singleSearchContext) >= scoreCutoff)
                {
                    results |= 1u << lane;
                }
            }
        }

        return new(results);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VectorMask SearchIndividualSeeds(VectorMask mask, MotelyIndividualSeedSearcher searcher, int scoreCutoff = 1)
    {
        if (mask.IsAllFalse())
            return mask;

        uint results = 0;

        uint maskShift = mask.Value;

        for (int lane = 0; lane < MotelyGlobals.MaxVectorWidth; lane++)
        {
            if ((maskShift & 1) != 0 && IsLaneValid(lane))
            {
                MotelySingleSearchContext singleSearchContext = new(
                    in _searchParameters,
                    in _contextParams,
                    lane
                );

                if (searcher(singleSearchContext) >= scoreCutoff)
                {
                    results |= 1u << lane;
                }
            }

            maskShift >>= 1;
        }

        return new(results);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector512<double> PseudoHash(string key, bool isCached = false)
    {
        Vector512<double> partialHash;

        if (!IsAdditionalFilter && (isCached || SeedHashCache->HasPartialHash(key.Length)))
        {
            partialHash = SeedHashCache->GetPartialHashVector(key.Length);
        }
        else
        {
            partialHash = InternalPseudoHashSeed(key.Length);

            if (key.Length < MotelyGlobals.MaxCachedPseudoHashKeyLength)
                SeedHashCache->CachePartialHash(key.Length, partialHash);
        }

        return InternalPseudoHash(key, partialHash);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector512<double> InternalPseudoHashSeed(int keyLength)
    {
        int seedLastCharacterLength = SeedLastCharactersLength;
        double num = 1;

        for (int i = SeedFirstCharactersLength - 1; i >= 0; i--)
        {
            num =
                (
                    1.1239285023 / num * SeedFirstCharacters[i] * Math.PI
                    + Math.PI * (i + keyLength + seedLastCharacterLength + 1)
                ) % 1;
        }

        Vector512<double> numVector = Vector512.Create(num);

        for (int i = seedLastCharacterLength - 1; i >= 0; i--)
        {
            numVector = Vector512.Divide(MotelyVectorConstants.HashConstant, numVector);
            numVector = Vector512.Multiply(numVector, SeedLastCharacters[i]);
            numVector = Vector512.Multiply(numVector, MotelyVectorConstants.Pi);
            numVector = Vector512.Add(numVector, Vector512.Create((i + keyLength + 1) * Math.PI));

            Vector512<double> intPart = Vector512.Floor(numVector);
            numVector = Vector512.Subtract(numVector, intPart);
        }

        return numVector;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector512<double> InternalPseudoHash(string key, Vector512<double> partialHash)
    {
        for (int i = key.Length - 1; i >= 0; i--)
        {
            partialHash = Vector512.Divide(MotelyVectorConstants.HashConstant, partialHash);
            partialHash = Vector512.Multiply(partialHash, key[i]);
            partialHash = Vector512.Multiply(partialHash, MotelyVectorConstants.Pi);
            partialHash = Vector512.Add(partialHash, Vector512.Create((i + 1) * Math.PI));

            Vector512<double> intPart = Vector512.Floor(partialHash);
            partialHash = Vector512.Subtract(partialHash, intPart);
        }

        return partialHash;
    }

    private static readonly double InvPrec = Math.Pow(10.0, 13);
    private static readonly double TwoInvPrec = Math.Pow(2.0, 13);
    private static readonly double FiveInvPrec = Math.Pow(5.0, 13);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector512<double> IteratePRNG(Vector512<double> state)
    {
        state = Vector512.Multiply(state, MotelyVectorConstants.PrngMultiplier);
        state = Vector512.Add(state, MotelyVectorConstants.PrngAddend);

        Vector512<double> intPart = Vector512.Floor(state);
        state = Vector512.Subtract(state, intPart);

        state = Vector512.FusedMultiplyAdd(
            state,
            MotelyVectorConstants.PrngRoundingFactor,
            MotelyVectorConstants.PrngMagicNumber
        );
        state = Vector512.Subtract(state, MotelyVectorConstants.PrngMagicNumber);
        state = Vector512.Divide(state, MotelyVectorConstants.PrngRoundingFactor);

        return state;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MotelyVectorPrngStream CreatePrngStream(string key, bool isCached = false)
    {
        return new(PseudoHash(key, isCached));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector512<double> GetNextPrngState(ref MotelyVectorPrngStream stream)
    {
        return stream.State = IteratePRNG(stream.State);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector512<double> GetNextPrngState(
        ref MotelyVectorPrngStream stream,
        in Vector512<double> mask
    )
    {
        return stream.State = Vector512.ConditionalSelect(
            mask,
            IteratePRNG(stream.State),
            stream.State
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector512<double> IteratePseudoSeed(ref MotelyVectorPrngStream stream)
    {
        return (GetNextPrngState(ref stream) + SeedHashCache->GetSeedHashVector())
            / MotelyVectorConstants.Two;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector512<double> IteratePseudoSeed(
        ref MotelyVectorPrngStream stream,
        in Vector512<double> mask
    )
    {
        return (GetNextPrngState(ref stream, mask) + SeedHashCache->GetSeedHashVector())
            / MotelyVectorConstants.Two;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector512<double> GetNextPseudoSeed(
        ref MotelyVectorPrngStream stream,
        in Vector512<double> mask
    )
    {
        return (GetNextPrngState(ref stream, mask) + SeedHashCache->GetSeedHashVector())
            / MotelyVectorConstants.Two;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector512<double> GetNextRandom(ref MotelyVectorPrngStream stream)
    {
        return VectorLuaRandom.Random(IteratePseudoSeed(ref stream));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector512<double> GetNextRandom(
        ref MotelyVectorPrngStream stream,
        in Vector512<double> mask
    )
    {
        return VectorLuaRandom.Random(GetNextPseudoSeed(ref stream, mask));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector256<int> GetNextRandomInt(ref MotelyVectorPrngStream stream, int min, int max)
    {
        return VectorLuaRandom.RandInt(IteratePseudoSeed(ref stream), min, max);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector256<int> GetNextRandomInt(
        ref MotelyVectorPrngStream stream,
        int min,
        int max,
        in Vector512<double> mask
    )
    {
        return VectorLuaRandom.RandInt(IteratePseudoSeed(ref stream, mask), min, max);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VectorEnum256<T> GetNextRandomElement<T>(ref MotelyVectorPrngStream stream, T[] choices)
        where T : unmanaged, Enum
    {
        return VectorEnum256.Create(GetNextRandomInt(ref stream, 0, choices.Length), choices);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VectorEnum256<T> GetNextRandomElement<T>(
        ref MotelyVectorPrngStream stream,
        T[] choices,
        in Vector512<double> mask
    )
        where T : unmanaged, Enum
    {
        return VectorEnum256.Create(GetNextRandomInt(ref stream, 0, choices.Length, mask), choices);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private MotelyVectorResampleStream CreateResampleStream(string key, bool isCached)
    {
        return new(CreatePrngStream(key, isCached), isCached);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private MotelyVectorPrngStream CreateResamplePrngStream(string key, int resample, bool isCached)
    {
        if (isCached && resample >= 8)
            isCached = false;
        return CreatePrngStream(key + MotelyPrngKeys.Resample + (resample + 2), isCached);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref MotelyVectorPrngStream GetResamplePrngStream(
        ref MotelyVectorResampleStream resampleStream,
        string key,
        int resample
    )
    {
        if (resample < MotelyVectorResampleStream.StackResampleCount)
        {
            ref MotelyVectorPrngStream prngStream = ref resampleStream.ResamplePrngStreams[
                resample
            ];

            if (resample == resampleStream.ResamplePrngStreamInitCount)
            {
                ++resampleStream.ResamplePrngStreamInitCount;

                prngStream = CreateResamplePrngStream(key, resample, resampleStream.IsCached);
            }

            return ref prngStream;
        }

        {
            if (resample == MotelyVectorResampleStream.StackResampleCount)
            {
                resampleStream.HighResamplePrngStreams = [];
            }

            Debug.Assert(resampleStream.HighResamplePrngStreams != null);

            if (resample < resampleStream.HighResamplePrngStreams.Count)
            {
                return ref resampleStream.HighResamplePrngStreams[resample].Value;
            }

            var prngStreamBox = new StrongBox<MotelyVectorPrngStream>();
            resampleStream.HighResamplePrngStreams.Add(prngStreamBox);

            prngStreamBox.Value = CreateResamplePrngStream(key, resample, resampleStream.IsCached);

            return ref prngStreamBox.Value;
        }
    }
}
