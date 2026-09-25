using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely;

public struct VectorLuaRandom
{
    private static readonly Vector512<double> _vecPI = Vector512.Create(3.14159265358979323846);
    private static readonly Vector512<double> _vecE = Vector512.Create(2.7182818284590452354);
    private static readonly Vector512<double> _vecOne = Vector512.Create(1d);
    private static readonly Vector512<ulong> _mask1 = Vector512.Create(ulong.MaxValue << 1);
    private static readonly Vector512<ulong> _mask6 = Vector512.Create(ulong.MaxValue << 6);
    private static readonly Vector512<ulong> _mask9 = Vector512.Create(ulong.MaxValue << 9);
    private static readonly Vector512<ulong> _mask17 = Vector512.Create(ulong.MaxValue << 17);
    private static readonly Vector512<ulong> _randDblMemMask = Vector512.Create(4503599627370495ul);
    private static readonly Vector512<ulong> _randDblMemOr = Vector512.Create(
        4607182418800017408ul
    );

    [InlineArray(4)]
    private struct VectorLuaRandomState
    {
        Vector512<ulong> value;
    }

    private VectorLuaRandomState _state;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VectorLuaRandom(Vector512<double> seed)
    {
        Vector512<double> d = seed;
        int r = 0x11090601;

        for (int i = 0; i < 4; i++)
        {
            ulong m = 1ul << (r & 255);
            r >>= 8;

            d *= _vecPI;
            d += _vecE;

            Vector512<ulong> state = d.AsUInt64();

            Vector512<ulong> mask = Vector512.LessThan(state, Vector512.Create(m));

            state += Vector512.Create(m) & mask;

            _state[i] = state;
        }

        Vector512<ulong> z = _state[0];
        for (int i = 0; i < 5; i++)
        {
            z = (((z << 31) ^ z) >> 45) ^ ((z & _mask1) << 18);
            z = (((z << 31) ^ z) >> 45) ^ ((z & _mask1) << 18);
        }
        _state[0] = z;

        z = _state[1];
        for (int i = 0; i < 5; i++)
        {
            z = (((z << 19) ^ z) >> 30) ^ ((z & _mask6) << 28);
            z = (((z << 19) ^ z) >> 30) ^ ((z & _mask6) << 28);
        }
        _state[1] = z;

        z = _state[2];
        for (int i = 0; i < 5; i++)
        {
            z = (((z << 24) ^ z) >> 48) ^ ((z & _mask9) << 7);
            z = (((z << 24) ^ z) >> 48) ^ ((z & _mask9) << 7);
        }
        _state[2] = z;

        z = _state[3];
        for (int i = 0; i < 5; i++)
        {
            z = (((z << 21) ^ z) >> 39) ^ ((z & _mask17) << 8);
            z = (((z << 21) ^ z) >> 39) ^ ((z & _mask17) << 8);
        }
        _state[3] = z;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector512<ulong> RandInt()
    {
        Vector512<ulong> r = Vector512<ulong>.Zero;

        ref Vector512<ulong> z = ref _state[0];
        z = (((z << 31) ^ z) >> 45) ^ ((z & _mask1) << 18);
        r ^= z;

        z = ref _state[1];
        z = (((z << 19) ^ z) >> 30) ^ ((z & _mask6) << 28);
        r ^= z;

        z = ref _state[2];
        z = (((z << 24) ^ z) >> 48) ^ ((z & _mask9) << 7);
        r ^= z;

        z = ref _state[3];
        z = (((z << 21) ^ z) >> 39) ^ ((z & _mask17) << 8);
        r ^= z;

        return r;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector512<ulong> RandDblMem()
    {
        return (RandInt() & _randDblMemMask) | _randDblMemOr;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector512<double> Random()
    {
        Vector512<ulong> u = RandDblMem();
        return u.AsDouble() - _vecOne;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector256<int> RandInt(int min, int max)
    {
        return MotelyVectorUtils.ConvertToVector256Int32(Random() * (max - min))
            + Vector256.Create<int>(min);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<ulong> RandInt(Vector512<double> seed)
    {
        Vector512<double> d = seed;
        int r = 0x11090601;

        Vector512<ulong> randint = Vector512<ulong>.Zero;

        ulong m;
        Vector512<ulong> state;

        m = 1ul << (r & 255);
        r >>= 8;

        d *= _vecPI;
        d += _vecE;

        state = d.AsUInt64();

        state += Vector512.Create(m) & Vector512.LessThan(state, Vector512.Create(m));

        for (int i = 0; i < 5; i++)
        {
            state = (((state << 31) ^ state) >> 45) ^ ((state & _mask1) << 18);
            state = (((state << 31) ^ state) >> 45) ^ ((state & _mask1) << 18);
        }
        state = (((state << 31) ^ state) >> 45) ^ ((state & _mask1) << 18);

        randint ^= state;

        m = 1ul << (r & 255);
        r >>= 8;

        d *= _vecPI;
        d += _vecE;

        state = d.AsUInt64();

        state += Vector512.Create(m) & Vector512.LessThan(state, Vector512.Create(m));

        for (int i = 0; i < 5; i++)
        {
            state = (((state << 19) ^ state) >> 30) ^ ((state & _mask6) << 28);
            state = (((state << 19) ^ state) >> 30) ^ ((state & _mask6) << 28);
        }
        state = (((state << 19) ^ state) >> 30) ^ ((state & _mask6) << 28);

        randint ^= state;

        m = 1ul << (r & 255);
        r >>= 8;

        d *= _vecPI;
        d += _vecE;

        state = d.AsUInt64();

        state += Vector512.Create(m) & Vector512.LessThan(state, Vector512.Create(m));

        for (int i = 0; i < 5; i++)
        {
            state = (((state << 24) ^ state) >> 48) ^ ((state & _mask9) << 7);
            state = (((state << 24) ^ state) >> 48) ^ ((state & _mask9) << 7);
        }
        state = (((state << 24) ^ state) >> 48) ^ ((state & _mask9) << 7);

        randint ^= state;

        m = 1ul << (r & 255);

        d *= _vecPI;
        d += _vecE;

        state = d.AsUInt64();

        state += Vector512.Create(m) & Vector512.LessThan(state, Vector512.Create(m));

        for (int i = 0; i < 5; i++)
        {
            state = (((state << 21) ^ state) >> 39) ^ ((state & _mask17) << 8);
            state = (((state << 21) ^ state) >> 39) ^ ((state & _mask17) << 8);
        }
        state = (((state << 21) ^ state) >> 39) ^ ((state & _mask17) << 8);

        randint ^= state;

        return randint;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<ulong> RandDblMem(Vector512<double> seed)
    {
        return (RandInt(seed) & _randDblMemMask) | _randDblMemOr;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<double> Random(Vector512<double> seed)
    {
        Vector512<ulong> u = RandDblMem(seed);
        return u.AsDouble() - _vecOne;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<int> RandInt(Vector512<double> seed, int min, int max)
    {
        return MotelyVectorUtils.ConvertToVector256Int32(Random(seed) * (max - min))
            + Vector256.Create<int>(min);
    }
}
