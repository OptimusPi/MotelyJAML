using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Motely;

internal unsafe struct PartialSeedHashCache : IDisposable
{
    public readonly Vector512<double>** Cache;

    public readonly Vector512<double>** InitialCache;

    public readonly Vector512<double>* DynamicCacheMemory;
    public int DynamicCacheEntryCount;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public PartialSeedHashCache(IInternalMotelySearch search, Vector512<double>* partialSeedHashes)
    {
        Cache = (Vector512<double>**)
            Marshal.AllocHGlobal(
                sizeof(Vector512<double>*) * MotelyGlobals.MaxCachedPseudoHashKeyLength
            );
        InitialCache = (Vector512<double>**)
            Marshal.AllocHGlobal(
                sizeof(Vector512<double>*) * MotelyGlobals.MaxCachedPseudoHashKeyLength
            );

        DynamicCacheMemory = (Vector512<double>*)
            Marshal.AllocHGlobal(
                sizeof(Vector512<double>)
                    * (MotelyGlobals.MaxCachedPseudoHashKeyLength - search.PseudoHashKeyLengthCount)
            );
        DynamicCacheEntryCount = 0;

        Unsafe.InitBlockUnaligned(
            InitialCache,
            0,
            (uint)sizeof(Vector512<double>*) * MotelyGlobals.MaxCachedPseudoHashKeyLength
        );
        for (int i = 0; i < search.PseudoHashKeyLengthCount; i++)
        {
            int pseudohashKeyLength = search.PseudoHashKeyLengths[i];
            InitialCache[pseudohashKeyLength] = &partialSeedHashes[i];
        }

        ResetCache();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ResetCache()
    {
        Unsafe.CopyBlock(
            Cache,
            InitialCache,
            (uint)sizeof(Vector512<double>*) * MotelyGlobals.MaxCachedPseudoHashKeyLength
        );
        DynamicCacheEntryCount = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset()
    {
        if (DynamicCacheEntryCount != 0)
        {
            ResetCache();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool HasPartialHash(int keyLength)
    {
        return keyLength < MotelyGlobals.MaxCachedPseudoHashKeyLength && Cache[keyLength] != null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Vector512<double> GetSeedHashVector()
    {
        Debug.Assert(Cache[0] != null);
        return *Cache[0];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly double GetSeedHash(int lane)
    {
        return GetSeedHashVector()[lane];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Vector512<double> GetPartialHashVector(int keyLength)
    {
        Debug.Assert(HasPartialHash(keyLength));
        return *Cache[keyLength];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly double GetPartialHash(int keyLength, int lane)
    {
        return GetPartialHashVector(keyLength)[lane];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CachePartialHash(int keyLength, Vector512<double> partialHash)
    {
        Debug.Assert(keyLength < MotelyGlobals.MaxCachedPseudoHashKeyLength);

        if (HasPartialHash(keyLength))
            return;

        int dynamicEntryIndex = DynamicCacheEntryCount++;

        Vector512<double>* dynamicCacheMemory = &DynamicCacheMemory[dynamicEntryIndex];

        *dynamicCacheMemory = partialHash;
        Cache[keyLength] = dynamicCacheMemory;
    }

    public readonly void Dispose()
    {
        Marshal.FreeHGlobal((nint)Cache);
        Marshal.FreeHGlobal((nint)InitialCache);
        Marshal.FreeHGlobal((nint)DynamicCacheMemory);
    }
}
