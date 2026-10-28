using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Motely.Filters;

namespace Motely.Gpu;

/// <summary>
/// Result of a live hardware GPU benchmark on the active OpenCL device.
/// </summary>
public sealed record GpuBenchmarkResult(
    bool GpuAvailable,
    string DeviceName,
    string Vendor,
    string DriverVersion,
    uint ComputeUnits,
    uint MaxClockFrequencyMHz,
    ulong GlobalMemoryBytes,
    long SeedsEvaluated,
    double ElapsedMilliseconds,
    double MillionsOfSeedsPerSecond
);

/// <summary>
/// Compiled GPU filter instruction set for a <see cref="JamlConfig"/>.
/// Uses a data-driven opcode stream so the OpenCL C kernel compiles once per process
/// and executes any filter configuration without recompilation overhead.
/// </summary>
public sealed class GpuFilterProgram
{
    public const int OpVoucher = 1;
    public const int OpTag = 2;
    public const int OpBoss = 3;
    public const int OpSoulEdition = 4;
    public const int OpSoulPack = 5;

    public int[] Instructions { get; }
    public int InstructionGroupCount { get; }
    public bool HasSelectiveOps => InstructionGroupCount > 0;

    private GpuFilterProgram(int[] instructions, int groupCount)
    {
        Instructions = instructions;
        InstructionGroupCount = groupCount;
    }

    public static GpuFilterProgram Empty { get; } = new([0], 0);

    /// <summary>
    /// Compiles the <c>must</c> and exact <c>mustNot</c> clauses of a <see cref="JamlConfig"/> into
    /// GPU opcodes. Every emitted opcode is either 100% bit-exact with CPU (Voucher, Tag, Boss,
    /// SoulEdition) or a mathematically sound necessary condition with zero false negatives (SoulPack).
    /// Surviving candidates are always confirmed and scored by the CPU pipeline.
    /// </summary>
    public static GpuFilterProgram Compile(JamlConfig? config)
    {
        if (config is null)
            return Empty;

        var ops = new List<int> { 0 }; // slot 0 = opcode count, patched at end
        int opCount = 0;

        foreach (var clause in config.Must)
        {
            if (TryEmitClause(ops, clause, negate: false, ref opCount))
                continue;
        }

        foreach (var clause in config.MustNot)
        {
            if (ClauseScoring.IsExactFilterConfirm(clause))
            {
                TryEmitClause(ops, clause, negate: true, ref opCount);
            }
        }

        ops[0] = opCount;
        return new GpuFilterProgram([.. ops], opCount);
    }

    private static bool TryEmitClause(List<int> ops, IMotelyClause clause, bool negate, ref int opCount)
    {
        switch (clause)
        {
            case VoucherClause vc when vc.Antes.Length > 0 && vc.Rolls.Length > 0:
            {
                int anteMask = 0;
                int maxAnte = 0;
                foreach (int a in vc.Antes)
                {
                    if (a is < 1 or > 8)
                        return false;
                    anteMask |= 1 << a;
                    if (a > maxAnte)
                        maxAnte = a;
                }

                int rollMask = 0;
                int maxRoll = 0;
                foreach (int r in vc.Rolls)
                {
                    if (r is < 0 or > 7)
                        return false;
                    rollMask |= 1 << r;
                    if (r > maxRoll)
                        maxRoll = r;
                }

                // Only roll 0 is stateless across voucher stream draws; for roll >= 1 we also support up to roll 7.
                uint voucherMask = vc.Vouchers.Length == 0 ? 0xFFFFFFFFu : 0u;
                foreach (var v in vc.Vouchers)
                    voucherMask |= 1u << (int)v;

                ops.Add(OpVoucher);
                ops.Add(anteMask);
                ops.Add(maxAnte);
                ops.Add( unchecked((int)voucherMask) );
                ops.Add(rollMask);
                ops.Add(maxRoll);
                ops.Add(vc.Min);
                ops.Add(vc.Max ?? -1);
                ops.Add(negate ? 1 : 0);
                opCount++;
                return true;
            }

            case TagClause tc when tc.Antes.Length > 0 && tc.Rolls.Length > 0:
            {
                int anteMask = 0;
                foreach (int a in tc.Antes)
                {
                    if (a is < 1 or > 8)
                        return false;
                    anteMask |= 1 << a;
                }

                int rollMask = 0;
                int maxRoll = 0;
                foreach (int r in tc.Rolls)
                {
                    if (r is < 0 or > 7)
                        return false;
                    rollMask |= 1 << r;
                    if (r > maxRoll)
                        maxRoll = r;
                }

                uint tagMask = tc.Tags.Length == 0 ? 0x00FFFFFFu : 0u;
                foreach (var t in tc.Tags)
                    tagMask |= 1u << (int)t;

                ops.Add(OpTag);
                ops.Add(anteMask);
                ops.Add(unchecked((int)tagMask));
                ops.Add(rollMask);
                ops.Add(maxRoll);
                ops.Add(tc.Min);
                ops.Add(tc.Max ?? -1);
                ops.Add(negate ? 1 : 0);
                opCount++;
                return true;
            }

            case BossClause bc when bc.Antes.Length > 0:
            {
                int anteMask = 0;
                int maxAnte = 0;
                foreach (int a in bc.Antes)
                {
                    if (a is < 1 or > 8)
                        return false;
                    anteMask |= 1 << a;
                    if (a > maxAnte)
                        maxAnte = a;
                }

                uint bossMask = bc.Bosses.Length == 0 ? 0x0FFFFFFFu : 0u;
                foreach (var b in bc.Bosses)
                    bossMask |= 1u << b.GetBossIndex();

                ops.Add(OpBoss);
                ops.Add(anteMask);
                ops.Add(maxAnte);
                ops.Add(unchecked((int)bossMask));
                ops.Add(bc.Min);
                ops.Add(bc.Max ?? -1);
                ops.Add(negate ? 1 : 0);
                opCount++;
                return true;
            }

            case LegendaryJokerClause ljc when !negate && ljc.Antes.Length > 0:
            {
                int anteMask = 0;
                foreach (int a in ljc.Antes)
                {
                    if (a is < 0 or > 8)
                        return false;
                    anteMask |= 1 << a;
                }

                bool emitted = false;
                if (ljc.Edition.HasValue)
                {
                    int targetEdition = ljc.Edition.Value switch
                    {
                        MotelyItemEdition.Foil => 1,
                        MotelyItemEdition.Holographic => 2,
                        MotelyItemEdition.Polychrome => 3,
                        MotelyItemEdition.Negative => 4,
                        _ => 0,
                    };
                    int maxSouls = LegendarySoulEditionPrefilter.GetEditionSoulRollCount(ljc);

                    ops.Add(OpSoulEdition);
                    ops.Add(anteMask);
                    ops.Add(targetEdition);
                    ops.Add(maxSouls);
                    opCount++;
                    emitted = true;
                }

                // Every LegendaryJokerClause requires The Soul to appear in at least one Arcana or Spectral pack
                // in the targeted antes. Checking soul_Tarot{ante} and soul_Spectral{ante} (> 0.997) is a 100%
                // sound superset check with zero false negatives.
                var src = ljc.Sources ?? LegendaryJokerFilterDesc.DefaultSources;
                int maxSlot = Math.Max(5, src.MaxReferencedBoosterSlot());
                int maxTarotDraws = (maxSlot + 1 + src.TagPacks.Length) * 5;
                int maxSpectralDraws = (maxSlot + 1) * 8;

                ops.Add(OpSoulPack);
                ops.Add(anteMask);
                ops.Add(1); // checkTarot
                ops.Add(maxTarotDraws);
                ops.Add(1); // checkSpectral
                ops.Add(maxSpectralDraws);
                ops.Add(0); // checkPlanet
                ops.Add(0);
                opCount++;
                return emitted || true;
            }

            case SpectralCardClause scc when !negate && SpecialSpectralCardFilterDesc.Handles(scc) && scc.Antes.Length > 0:
            {
                var sources = SpectralCardFilterDesc.ResolveSources(scc);
                if (sources.ShopItems.Length > 0 || sources.SixthSense.Length > 0 || sources.Seance.Length > 0)
                    return false;

                int anteMask = 0;
                foreach (int a in scc.Antes)
                {
                    if (a is < 0 or > 8)
                        return false;
                    anteMask |= 1 << a;
                }

                bool wantsSoul = scc.Spectrals.Length == 0 || scc.Spectrals.Contains(MotelySpectralCard.TheSoul);
                bool wantsBlackHole = scc.Spectrals.Length == 0 || scc.Spectrals.Contains(MotelySpectralCard.BlackHole);

                ops.Add(OpSoulPack);
                ops.Add(anteMask);
                ops.Add(wantsSoul ? 1 : 0);
                ops.Add(35);
                ops.Add(1); // Spectral packs can hold both Soul and BlackHole
                ops.Add(36);
                ops.Add(wantsBlackHole ? 1 : 0);
                ops.Add(35);
                opCount++;
                return true;
            }

            default:
                return false;
        }
    }
}

/// <summary>
/// Hardware-accelerated OpenCL FP64 seed searcher for Balatro / Motely.
/// Implements bit-exact IEEE-754 double-precision <c>PseudoHash</c>, <c>IteratePRNG</c> (with explicit FMA),
/// and 4-state 64-bit Tausworthe LFSR <c>LuaRandom</c> on the GPU.
/// </summary>
public sealed unsafe class GpuSeedSearcher : IDisposable
{
    private static readonly object SharedContextLock = new();
    private static SharedGpuContext? _sharedContext;

    private readonly SharedGpuContext _ctx;
    private readonly GpuFilterProgram _program;
    private bool _disposed;

    public GpuDeviceInfo Device => _ctx.Device;
    public GpuFilterProgram FilterProgram => _program;

    private GpuSeedSearcher(SharedGpuContext ctx, GpuFilterProgram program)
    {
        _ctx = ctx;
        _program = program;
    }

    public static GpuSeedSearcher? TryCreate(JamlConfig? config)
    {
        if (OperatingSystem.IsBrowser())
            return null;

        try
        {
            var shared = GetOrCreateSharedContext();
            if (shared is null)
                return null;

            var program = GpuFilterProgram.Compile(config);
            return new GpuSeedSearcher(shared, program);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Runs a live hardware benchmark on the primary OpenCL GPU and returns measured throughput.
    /// </summary>
    public static GpuBenchmarkResult Benchmark(int batchChars = 3, int batchCount = 35)
    {
        var dev = GpuDeviceInfo.GetBestDevice();
        if (dev is null || !dev.SupportsFp64)
        {
            return new GpuBenchmarkResult(
                false,
                dev?.Name ?? "None",
                dev?.Vendor ?? "",
                dev?.DriverVersion ?? "",
                dev?.ComputeUnits ?? 0,
                dev?.MaxClockFrequencyMHz ?? 0,
                dev?.GlobalMemoryBytes ?? 0,
                0,
                0,
                0
            );
        }

        // Benchmark with a representative multi-clause filter (Hieroglyph + Negative Soul Edition)
        var benchConfig = new JamlConfig
        {
            Must =
            [
                new VoucherClause { Vouchers = [MotelyVoucher.Hieroglyph], Antes = [1] },
                new LegendaryJokerClause
                {
                    Edition = MotelyItemEdition.Negative,
                    Antes = [1, 2],
                },
            ],
        };

        using var searcher = TryCreate(benchConfig);
        if (searcher is null)
        {
            return new GpuBenchmarkResult(
                false,
                dev.Name,
                dev.Vendor,
                dev.DriverVersion,
                dev.ComputeUnits,
                dev.MaxClockFrequencyMHz,
                dev.GlobalMemoryBytes,
                0,
                0,
                0
            );
        }

        int seedsPerBatch = (int)Math.Pow(MotelyGlobals.SeedDigits.Length, batchChars);
        long totalSeeds = (long)seedsPerBatch * batchCount;

        // Warmup dispatch
        int[] scratch = ArrayPool<int>.Shared.Rent(4096);
        try
        {
            searcher.FilterBatchRange(0, 1, batchChars, scratch);

            var sw = Stopwatch.StartNew();
            searcher.FilterBatchRange(0, batchCount, batchChars, scratch);
            sw.Stop();

            double elapsedMs = Math.Max(0.001, sw.Elapsed.TotalMilliseconds);
            double mSeedsPerSec = (totalSeeds / 1_000_000.0) / (elapsedMs / 1000.0);

            return new GpuBenchmarkResult(
                true,
                dev.Name,
                dev.Vendor,
                dev.DriverVersion,
                dev.ComputeUnits,
                dev.MaxClockFrequencyMHz,
                dev.GlobalMemoryBytes,
                totalSeeds,
                Math.Round(elapsedMs, 3),
                Math.Round(mSeedsPerSec, 2)
            );
        }
        finally
        {
            ArrayPool<int>.Shared.Return(scratch);
        }
    }

    /// <summary>
    /// Evaluates <paramref name="batchCount"/> consecutive sequential batches starting at
    /// <paramref name="startBatchIdx"/> on the GPU and writes passing global work-item indices
    /// (<c>0 .. batchCount * seedsPerBatch - 1</c>) into <paramref name="candidateIndices"/>.
    /// Returns the number of candidate indices written, or <c>-1</c> if the candidate buffer overflowed
    /// (in which case the caller should fall back to CPU evaluation for that range).
    /// </summary>
    public int FilterBatchRange(
        long startBatchIdx,
        int batchCount,
        int batchChars,
        Span<int> candidateIndices
    )
    {
        if (_disposed)
            return -1;

        int seedsPerBatch = 1;
        for (int i = 0; i < batchChars; i++)
            seedsPerBatch *= MotelyGlobals.SeedDigits.Length;

        long totalSeedsLong = (long)seedsPerBatch * batchCount;
        if (totalSeedsLong <= 0 || totalSeedsLong > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(batchCount));

        int totalSeeds = (int)totalSeedsLong;
        int maxCandidates = Math.Min(candidateIndices.Length, SharedGpuContext.MaxCandidateCapacity);
        int opsLen = _program.Instructions.Length;
        if (opsLen > SharedGpuContext.MaxOpsCapacity)
            return -1;

        lock (_ctx.QueueLock)
        {
            fixed (int* opsPtr = _program.Instructions)
            {
                int writeOpsErr = OpenClNative.clEnqueueWriteBuffer(
                    _ctx.Queue,
                    _ctx.OpsBuffer,
                    OpenClNative.CL_TRUE,
                    0,
                    (nuint)(opsLen * sizeof(int)),
                    opsPtr,
                    0,
                    null,
                    null
                );
                if (writeOpsErr != OpenClNative.CL_SUCCESS)
                    return -1;
            }

            int zero = 0;
            int err = OpenClNative.clEnqueueWriteBuffer(
                _ctx.Queue,
                _ctx.CountBuffer,
                OpenClNative.CL_TRUE,
                0,
                sizeof(int),
                &zero,
                0,
                null,
                null
            );
            if (err != OpenClNative.CL_SUCCESS)
                return -1;

            long startBatchArg = startBatchIdx;
            int batchCharsArg = batchChars;
            int seedsPerBatchArg = seedsPerBatch;
            int totalSeedsArg = totalSeeds;
            int maxCandidatesArg = maxCandidates;
            nint opsBuf = _ctx.OpsBuffer;
            nint candBuf = _ctx.CandidatesBuffer;
            nint cntBuf = _ctx.CountBuffer;

            CheckArg(OpenClNative.clSetKernelArg(_ctx.Kernel, 0, (nuint)sizeof(long), &startBatchArg), 0);
            CheckArg(OpenClNative.clSetKernelArg(_ctx.Kernel, 1, (nuint)sizeof(int), &batchCharsArg), 1);
            CheckArg(OpenClNative.clSetKernelArg(_ctx.Kernel, 2, (nuint)sizeof(int), &seedsPerBatchArg), 2);
            CheckArg(OpenClNative.clSetKernelArg(_ctx.Kernel, 3, (nuint)sizeof(int), &totalSeedsArg), 3);
            CheckArg(OpenClNative.clSetKernelArg(_ctx.Kernel, 4, (nuint)sizeof(nint), &opsBuf), 4);
            CheckArg(OpenClNative.clSetKernelArg(_ctx.Kernel, 5, (nuint)sizeof(nint), &candBuf), 5);
            CheckArg(OpenClNative.clSetKernelArg(_ctx.Kernel, 6, (nuint)sizeof(nint), &cntBuf), 6);
            CheckArg(OpenClNative.clSetKernelArg(_ctx.Kernel, 7, (nuint)sizeof(int), &maxCandidatesArg), 7);

            nuint localWorkSize = Math.Min((nuint)256, _ctx.Device.MaxWorkGroupSize);
            nuint globalWorkSize = ((nuint)totalSeeds + localWorkSize - 1) / localWorkSize * localWorkSize;

            err = OpenClNative.clEnqueueNDRangeKernel(
                _ctx.Queue,
                _ctx.Kernel,
                1,
                null,
                &globalWorkSize,
                &localWorkSize,
                0,
                null,
                null
            );
            if (err != OpenClNative.CL_SUCCESS)
                return -1;

            int candidateCount = 0;
            err = OpenClNative.clEnqueueReadBuffer(
                _ctx.Queue,
                _ctx.CountBuffer,
                OpenClNative.CL_TRUE,
                0,
                sizeof(int),
                &candidateCount,
                0,
                null,
                null
            );
            if (err != OpenClNative.CL_SUCCESS)
                return -1;

            if (candidateCount > maxCandidates)
                return -1; // Overflow; caller falls back to CPU evaluation for this batch

            if (candidateCount > 0)
            {
                fixed (int* outPtr = candidateIndices)
                {
                    err = OpenClNative.clEnqueueReadBuffer(
                        _ctx.Queue,
                        _ctx.CandidatesBuffer,
                        OpenClNative.CL_TRUE,
                        0,
                        (nuint)(candidateCount * sizeof(int)),
                        outPtr,
                        0,
                        null,
                        null
                    );
                    if (err != OpenClNative.CL_SUCCESS)
                        return -1;
                }

                // Sort candidate indices so seeds are confirmed in deterministic sequential order
                candidateIndices[..candidateCount].Sort();
            }

            return candidateCount;
        }
    }

    /// <summary>
    /// Reconstructs an 8-character seed from a sequential batch index and intra-batch index,
    /// matching <see cref="MotelySearch{TBaseFilter}"/>'s exact digit layout.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReconstructSeed(long batchIdx, int intraBatchIdx, int batchChars, Span<char> destination)
    {
        ReadOnlySpan<char> digits = MotelyGlobals.SeedDigits;
        int tmpIntra = intraBatchIdx;
        for (int p = 0; p < batchChars; p++)
        {
            destination[p] = digits[tmpIntra % 35];
            tmpIntra /= 35;
        }

        long tmpBatch = batchIdx;
        for (int p = batchChars; p < MotelyGlobals.MaxSeedLength; p++)
        {
            destination[p] = digits[(int)(tmpBatch % 35L)];
            tmpBatch /= 35L;
        }
    }

    private static void CheckArg(int err, uint index)
    {
        if (err != OpenClNative.CL_SUCCESS)
            throw new InvalidOperationException($"clSetKernelArg({index}) failed: {err}");
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private static SharedGpuContext? GetOrCreateSharedContext()
    {
        lock (SharedContextLock)
        {
            if (_sharedContext is not null)
                return _sharedContext;

            var dev = GpuDeviceInfo.GetBestDevice();
            if (dev is null || !dev.SupportsFp64)
                return null;

            nint deviceId = dev.DeviceId;
            nint context = OpenClNative.clCreateContext(null, 1, &deviceId, nint.Zero, nint.Zero, out int err);
            if (err != OpenClNative.CL_SUCCESS || context == nint.Zero)
                return null;

            nint queue;
            try
            {
                queue = OpenClNative.clCreateCommandQueueWithProperties(context, deviceId, null, out err);
                if (err != OpenClNative.CL_SUCCESS || queue == nint.Zero)
                    queue = OpenClNative.clCreateCommandQueue(context, deviceId, 0, out err);
            }
            catch
            {
                queue = OpenClNative.clCreateCommandQueue(context, deviceId, 0, out err);
            }

            if (err != OpenClNative.CL_SUCCESS || queue == nint.Zero)
            {
                OpenClNative.clReleaseContext(context);
                return null;
            }

            byte[] sourceBytes = Encoding.UTF8.GetBytes(OpenClKernelSource);
            nint program;
            fixed (byte* srcPtr = sourceBytes)
            {
                byte* ptr = srcPtr;
                nuint len = (nuint)sourceBytes.Length;
                program = OpenClNative.clCreateProgramWithSource(context, 1, &ptr, &len, out err);
            }

            if (err != OpenClNative.CL_SUCCESS || program == nint.Zero)
            {
                OpenClNative.clReleaseCommandQueue(queue);
                OpenClNative.clReleaseContext(context);
                return null;
            }

            err = OpenClNative.clBuildProgram(program, 1, &deviceId, null, nint.Zero, nint.Zero);
            if (err != OpenClNative.CL_SUCCESS)
            {
                string buildLog = GetBuildLog(program, deviceId);
                OpenClNative.clReleaseProgram(program);
                OpenClNative.clReleaseCommandQueue(queue);
                OpenClNative.clReleaseContext(context);
                throw new InvalidOperationException($"OpenCL kernel build failed ({err}): {buildLog}");
            }

            byte[] kernelName = "motely_filter_seeds\0"u8.ToArray();
            nint kernel;
            fixed (byte* namePtr = kernelName)
            {
                kernel = OpenClNative.clCreateKernel(program, namePtr, out err);
            }

            if (err != OpenClNative.CL_SUCCESS || kernel == nint.Zero)
            {
                OpenClNative.clReleaseProgram(program);
                OpenClNative.clReleaseCommandQueue(queue);
                OpenClNative.clReleaseContext(context);
                return null;
            }

            nint opsBuffer = OpenClNative.clCreateBuffer(
                context,
                OpenClNative.CL_MEM_READ_ONLY,
                (nuint)(SharedGpuContext.MaxOpsCapacity * sizeof(int)),
                null,
                out int opsErr
            );
            nint candidatesBuffer = OpenClNative.clCreateBuffer(
                context,
                OpenClNative.CL_MEM_WRITE_ONLY,
                (nuint)(SharedGpuContext.MaxCandidateCapacity * sizeof(int)),
                null,
                out int candErr
            );
            nint countBuffer = OpenClNative.clCreateBuffer(
                context,
                OpenClNative.CL_MEM_READ_WRITE,
                sizeof(int),
                null,
                out int cntErr
            );

            if (
                opsErr != OpenClNative.CL_SUCCESS
                || candErr != OpenClNative.CL_SUCCESS
                || cntErr != OpenClNative.CL_SUCCESS
                || opsBuffer == nint.Zero
                || candidatesBuffer == nint.Zero
                || countBuffer == nint.Zero
            )
            {
                if (opsBuffer != nint.Zero) OpenClNative.clReleaseMemObject(opsBuffer);
                if (candidatesBuffer != nint.Zero) OpenClNative.clReleaseMemObject(candidatesBuffer);
                if (countBuffer != nint.Zero) OpenClNative.clReleaseMemObject(countBuffer);
                OpenClNative.clReleaseKernel(kernel);
                OpenClNative.clReleaseProgram(program);
                OpenClNative.clReleaseCommandQueue(queue);
                OpenClNative.clReleaseContext(context);
                return null;
            }

            _sharedContext = new SharedGpuContext(
                dev,
                context,
                queue,
                program,
                kernel,
                opsBuffer,
                candidatesBuffer,
                countBuffer
            );
            return _sharedContext;
        }
    }

    private static string GetBuildLog(nint program, nint deviceId)
    {
        if (OpenClNative.clGetProgramBuildInfo(program, deviceId, OpenClNative.CL_PROGRAM_BUILD_LOG, 0, null, out nuint size) != OpenClNative.CL_SUCCESS || size == 0)
            return "";

        if (size > 65536)
            size = 65536;

        byte[] buf = new byte[(int)size];
        fixed (byte* ptr = buf)
        {
            OpenClNative.clGetProgramBuildInfo(program, deviceId, OpenClNative.CL_PROGRAM_BUILD_LOG, size, ptr, out _);
        }
        return Encoding.UTF8.GetString(buf).TrimEnd('\0');
    }

    private sealed class SharedGpuContext(
        GpuDeviceInfo device,
        nint context,
        nint queue,
        nint program,
        nint kernel,
        nint opsBuffer,
        nint candidatesBuffer,
        nint countBuffer
    )
    {
        public const int MaxOpsCapacity = 4096;
        public const int MaxCandidateCapacity = 262144;

        public GpuDeviceInfo Device { get; } = device;
        public nint Context { get; } = context;
        public nint Queue { get; } = queue;
        public nint Program { get; } = program;
        public nint Kernel { get; } = kernel;
        public nint OpsBuffer { get; } = opsBuffer;
        public nint CandidatesBuffer { get; } = candidatesBuffer;
        public nint CountBuffer { get; } = countBuffer;
        public object QueueLock { get; } = new();
    }

    private const string OpenClKernelSource = """
        #pragma OPENCL EXTENSION cl_khr_fp64 : enable
        #pragma OPENCL FP_CONTRACT OFF

        __constant uchar SEED_DIGITS[35] = {
            '1','2','3','4','5','6','7','8','9',
            'A','B','C','D','E','F','G','H','I','J','K','L','M','N','O','P','Q','R','S','T','U','V','W','X','Y','Z'
        };

        __constant int BOSS_MIN_ANTE[28] = {
            0, 0, 0, 0, 0,
            2, 0, 3, 2, 2, 0, 0, 0, 2, 0, 2, 2, 2, 6, 0, 4, 0, 5, 3, 2, 2, 2, 0
        };

        inline double compute_seed_hash(const uchar seed[8], int key_len) {
            const double PI = 3.141592653589793116;
            double num = 1.0;
            for (int i = 7; i >= 0; i--) {
                double x = 1.1239285023 / num * (double)seed[i] * PI + (double)(i + key_len + 1) * PI;
                num = x - floor(x);
            }
            return num;
        }

        inline double pseudo_hash_key(const uchar seed[8], const uchar* key, int key_len) {
            const double PI = 3.141592653589793116;
            double num = compute_seed_hash(seed, key_len);
            for (int i = key_len - 1; i >= 0; i--) {
                double x = 1.1239285023 / num * (double)key[i] * PI + (double)(i + 1) * PI;
                num = x - floor(x);
            }
            return num;
        }

        inline double iterate_prng(double state) {
            state = state * 1.72431234 + 2.134453429141;
            state -= floor(state);
            state = fma(state, 1e13, 4503599627370496.0) - 4503599627370496.0;
            return state / 1e13;
        }

        inline double get_next_pseudoseed(double* state, double seed_hash_0) {
            *state = iterate_prng(*state);
            return (*state + seed_hash_0) * 0.5;
        }

        inline double lua_rng_randint(double d) {
            ulong s0, s1, s2, s3;
            uint r = 0x11090601u;

            {
                uint m = 1u << (r & 255u);
                r >>= 8;
                d = d * 3.14159265358979323846 + 2.7182818284590452354;
                ulong u = as_ulong(d);
                if (u < (ulong)m) u += (ulong)m;
                s0 = u;
            }
            {
                uint m = 1u << (r & 255u);
                r >>= 8;
                d = d * 3.14159265358979323846 + 2.7182818284590452354;
                ulong u = as_ulong(d);
                if (u < (ulong)m) u += (ulong)m;
                s1 = u;
            }
            {
                uint m = 1u << (r & 255u);
                r >>= 8;
                d = d * 3.14159265358979323846 + 2.7182818284590452354;
                ulong u = as_ulong(d);
                if (u < (ulong)m) u += (ulong)m;
                s2 = u;
            }
            {
                uint m = 1u << (r & 255u);
                r >>= 8;
                d = d * 3.14159265358979323846 + 2.7182818284590452354;
                ulong u = as_ulong(d);
                if (u < (ulong)m) u += (ulong)m;
                s3 = u;
            }

            for (int i = 0; i < 10; i++) {
                s0 = (((s0 << 31) ^ s0) >> 45) ^ ((s0 & (~0UL << 1)) << 18);
                s1 = (((s1 << 19) ^ s1) >> 30) ^ ((s1 & (~0UL << 6)) << 28);
                s2 = (((s2 << 24) ^ s2) >> 48) ^ ((s2 & (~0UL << 9)) << 7);
                s3 = (((s3 << 21) ^ s3) >> 39) ^ ((s3 & (~0UL << 17)) << 8);
            }

            ulong res = 0UL;
            s0 = (((s0 << 31) ^ s0) >> 45) ^ ((s0 & (~0UL << 1)) << 18); res ^= s0;
            s1 = (((s1 << 19) ^ s1) >> 30) ^ ((s1 & (~0UL << 6)) << 28); res ^= s1;
            s2 = (((s2 << 24) ^ s2) >> 48) ^ ((s2 & (~0UL << 9)) << 7);  res ^= s2;
            s3 = (((s3 << 21) ^ s3) >> 39) ^ ((s3 & (~0UL << 17)) << 8); res ^= s3;

            ulong bits = (res & 0x000FFFFFFFFFFFFFUL) | 0x3FF0000000000000UL;
            return as_double(bits);
        }

        inline double lua_rng_random(double seed) {
            return lua_rng_randint(seed) - 1.0;
        }

        inline int get_next_random_int(double* state, double seed_hash_0, int min_val, int max_exclusive) {
            double r = lua_rng_random(get_next_pseudoseed(state, seed_hash_0));
            return (int)(r * (double)(max_exclusive - min_val)) + min_val;
        }

        inline double pseudo_hash_voucher(const uchar seed[8], int ante) {
            uchar key[8] = { 'V','o','u','c','h','e','r', (uchar)('0' + ante) };
            return pseudo_hash_key(seed, key, 8);
        }

        inline double pseudo_hash_voucher_resample(const uchar seed[8], int ante, int resample_num) {
            uchar key[19];
            key[0] = 'V'; key[1] = 'o'; key[2] = 'u'; key[3] = 'c';
            key[4] = 'h'; key[5] = 'e'; key[6] = 'r'; key[7] = (uchar)('0' + ante);
            key[8] = '_'; key[9] = 'r'; key[10] = 'e'; key[11] = 's';
            key[12] = 'a'; key[13] = 'm'; key[14] = 'p'; key[15] = 'l'; key[16] = 'e';
            if (resample_num < 10) {
                key[17] = (uchar)('0' + resample_num);
                return pseudo_hash_key(seed, key, 18);
            } else {
                key[17] = (uchar)('0' + (resample_num / 10));
                key[18] = (uchar)('0' + (resample_num % 10));
                return pseudo_hash_key(seed, key, 19);
            }
        }

        inline double pseudo_hash_tag(const uchar seed[8], int ante) {
            uchar key[4] = { 'T','a','g', (uchar)('0' + ante) };
            return pseudo_hash_key(seed, key, 4);
        }

        inline double pseudo_hash_tag_resample(const uchar seed[8], int ante, int resample_num) {
            uchar key[15];
            key[0] = 'T'; key[1] = 'a'; key[2] = 'g'; key[3] = (uchar)('0' + ante);
            key[4] = '_'; key[5] = 'r'; key[6] = 'e'; key[7] = 's';
            key[8] = 'a'; key[9] = 'm'; key[10] = 'p'; key[11] = 'l'; key[12] = 'e';
            if (resample_num < 10) {
                key[13] = (uchar)('0' + resample_num);
                return pseudo_hash_key(seed, key, 14);
            } else {
                key[13] = (uchar)('0' + (resample_num / 10));
                key[14] = (uchar)('0' + (resample_num % 10));
                return pseudo_hash_key(seed, key, 15);
            }
        }

        inline double pseudo_hash_boss(const uchar seed[8]) {
            uchar key[4] = { 'b','o','s','s' };
            return pseudo_hash_key(seed, key, 4);
        }

        inline double pseudo_hash_edisou(const uchar seed[8], int ante) {
            uchar key[7] = { 'e','d','i','s','o','u', (uchar)('0' + ante) };
            return pseudo_hash_key(seed, key, 7);
        }

        inline double pseudo_hash_soul_tarot(const uchar seed[8], int ante) {
            uchar key[11] = { 's','o','u','l','_','T','a','r','o','t', (uchar)('0' + ante) };
            return pseudo_hash_key(seed, key, 11);
        }

        inline double pseudo_hash_soul_spectral(const uchar seed[8], int ante) {
            uchar key[14] = { 's','o','u','l','_','S','p','e','c','t','r','a','l', (uchar)('0' + ante) };
            return pseudo_hash_key(seed, key, 14);
        }

        inline double pseudo_hash_soul_planet(const uchar seed[8], int ante) {
            uchar key[12] = { 's','o','u','l','_','P','l','a','n','e','t', (uchar)('0' + ante) };
            return pseudo_hash_key(seed, key, 12);
        }

        inline int draw_next_voucher(const uchar seed[8], double seed_hash_0, int ante, uint active_vouchers, double* main_state, double resample_states[16]) {
            int v = get_next_random_int(main_state, seed_hash_0, 0, 32);
            int resample = 0;
            while (resample < 64) {
                bool is_active = (active_vouchers & (1u << v)) != 0u;
                bool locked = ((v & 1) != 0) && ((active_vouchers & (1u << (v - 1))) == 0u);
                if (!is_active && !locked) break;
                if (active_vouchers == 0xFFFFFFFFu) return 16;

                double rst;
                if (resample < 16) {
                    if (resample_states[resample] < 0.0) {
                        resample_states[resample] = pseudo_hash_voucher_resample(seed, ante, resample + 2);
                    }
                    rst = resample_states[resample];
                    v = get_next_random_int(&rst, seed_hash_0, 0, 32);
                    resample_states[resample] = rst;
                } else {
                    rst = pseudo_hash_voucher_resample(seed, ante, resample + 2);
                    v = get_next_random_int(&rst, seed_hash_0, 0, 32);
                }
                resample++;
            }
            return v;
        }

        __kernel void motely_filter_seeds(
            const long start_batch,
            const int batch_chars,
            const int seeds_per_batch,
            const int total_seeds,
            __constant int* ops,
            __global int* candidates,
            __global volatile int* candidate_count,
            const int max_candidates
        ) {
            int gid = (int)get_global_id(0);
            if (gid >= total_seeds) return;

            int batch_offset = gid / seeds_per_batch;
            int intra_idx = gid % seeds_per_batch;
            long batch_idx = start_batch + (long)batch_offset;

            uchar seed[8];
            int tmp_intra = intra_idx;
            for (int p = 0; p < batch_chars; p++) {
                seed[p] = SEED_DIGITS[tmp_intra % 35];
                tmp_intra /= 35;
            }
            long tmp_batch = batch_idx;
            for (int p = batch_chars; p < 8; p++) {
                seed[p] = SEED_DIGITS[(int)(tmp_batch % 35L)];
                tmp_batch /= 35L;
            }

            double seed_hash_0 = compute_seed_hash(seed, 0);
            int op_count = ops[0];
            int pc = 1;

            for (int op_idx = 0; op_idx < op_count; op_idx++) {
                int opcode = ops[pc++];

                if (opcode == 1) { // OP_VOUCHER
                    int ante_mask = ops[pc++];
                    int max_ante = ops[pc++];
                    uint voucher_mask = (uint)ops[pc++];
                    int roll_mask = ops[pc++];
                    int max_roll = ops[pc++];
                    int min_count = ops[pc++];
                    int max_count = ops[pc++];
                    int negate = ops[pc++];

                    uint active_vouchers = 0u;
                    int match_count = 0;

                    for (int ante = 1; ante <= max_ante; ante++) {
                        double st = pseudo_hash_voucher(seed, ante);
                        int v = get_next_random_int(&st, seed_hash_0, 0, 32);
                        int resample = 0;
                        while (resample < 64) {
                            bool is_active = (active_vouchers & (1u << v)) != 0u;
                            bool locked = ((v & 1) != 0) && ((active_vouchers & (1u << (v - 1))) == 0u);
                            if (!is_active && !locked) break;
                            if (active_vouchers == 0xFFFFFFFFu) { v = 16; break; }
                            double rst = pseudo_hash_voucher_resample(seed, ante, resample + 2);
                            v = get_next_random_int(&rst, seed_hash_0, 0, 32);
                            resample++;
                        }

                        active_vouchers |= (1u << v);

                        if (((ante_mask >> ante) & 1) != 0) {
                            if ((roll_mask & 1) != 0) {
                                if (((voucher_mask >> v) & 1u) != 0u) match_count++;
                            }
                            if (max_roll >= 1) {
                                double stream_main = pseudo_hash_voucher(seed, ante);
                                double r_states[16];
                                for (int k = 0; k < 16; k++) r_states[k] = -1.0;
                                for (int r = 1; r <= max_roll; r++) {
                                    int sv = draw_next_voucher(seed, seed_hash_0, ante, active_vouchers, &stream_main, r_states);
                                    if (((roll_mask >> r) & 1) != 0 && ((voucher_mask >> sv) & 1u) != 0u) {
                                        match_count++;
                                    }
                                }
                            }
                        }
                    }

                    bool ok = (match_count >= min_count) && (max_count < 0 || match_count <= max_count);
                    if (negate != 0) ok = !ok;
                    if (!ok) return;
                }
                else if (opcode == 2) { // OP_TAG
                    int ante_mask = ops[pc++];
                    uint tag_mask = (uint)ops[pc++];
                    int roll_mask = ops[pc++];
                    int max_roll = ops[pc++];
                    int min_count = ops[pc++];
                    int max_count = ops[pc++];
                    int negate = ops[pc++];

                    const uint ANTE1_RESTRICTED_TAGS =
                        (1u << 2) | (1u << 4) | (1u << 5) | (1u << 6) | (1u << 7) |
                        (1u << 8) | (1u << 9) | (1u << 10) | (1u << 22);

                    int match_count = 0;
                    for (int ante = 1; ante <= 8; ante++) {
                        if (((ante_mask >> ante) & 1) == 0) continue;

                        double st = pseudo_hash_tag(seed, ante);
                        double r_states[16];
                        for (int k = 0; k < 16; k++) r_states[k] = -1.0;

                        for (int r = 0; r <= max_roll; r++) {
                            int t = get_next_random_int(&st, seed_hash_0, 0, 24);
                            if (ante < 2) {
                                int resample = 0;
                                while ((ANTE1_RESTRICTED_TAGS & (1u << t)) != 0u && resample < 16) {
                                    if (r_states[resample] < 0.0) {
                                        r_states[resample] = pseudo_hash_tag_resample(seed, ante, resample + 2);
                                    }
                                    double rst = r_states[resample];
                                    t = get_next_random_int(&rst, seed_hash_0, 0, 24);
                                    r_states[resample] = rst;
                                    resample++;
                                }
                            }
                            if (((roll_mask >> r) & 1) != 0 && ((tag_mask >> t) & 1u) != 0u) {
                                match_count++;
                            }
                        }
                    }

                    bool ok = (match_count >= min_count) && (max_count < 0 || match_count <= max_count);
                    if (negate != 0) ok = !ok;
                    if (!ok) return;
                }
                else if (opcode == 3) { // OP_BOSS
                    int ante_mask = ops[pc++];
                    int max_ante = ops[pc++];
                    uint boss_mask = (uint)ops[pc++];
                    int min_count = ops[pc++];
                    int max_count = ops[pc++];
                    int negate = ops[pc++];

                    double st = pseudo_hash_boss(seed);
                    uint seen_mask = 0u;
                    int match_count = 0;
                    int pool[23];

                    for (int ante = 1; ante <= max_ante; ante++) {
                        int pool_len = 0;
                        if ((ante & 7) == 0) {
                            for (int b = 0; b < 5; b++) {
                                if (((seen_mask >> b) & 1u) == 0u) pool[pool_len++] = b;
                            }
                            if (pool_len == 0) {
                                seen_mask &= ~0x1Fu;
                                for (int b = 0; b < 5; b++) pool[pool_len++] = b;
                            }
                        } else {
                            for (int b = 5; b < 28; b++) {
                                if (((seen_mask >> b) & 1u) == 0u && ante >= BOSS_MIN_ANTE[b]) {
                                    pool[pool_len++] = b;
                                }
                            }
                            if (pool_len == 0) {
                                seen_mask &= 0x1Fu;
                                for (int b = 5; b < 28; b++) {
                                    if (ante >= BOSS_MIN_ANTE[b]) pool[pool_len++] = b;
                                }
                            }
                        }

                        int selected = pool[get_next_random_int(&st, seed_hash_0, 0, pool_len)];
                        seen_mask |= (1u << selected);

                        if (((ante_mask >> ante) & 1) != 0 && ((boss_mask >> selected) & 1u) != 0u) {
                            match_count++;
                        }
                    }

                    bool ok = (match_count >= min_count) && (max_count < 0 || match_count <= max_count);
                    if (negate != 0) ok = !ok;
                    if (!ok) return;
                }
                else if (opcode == 4) { // OP_SOUL_EDITION
                    int ante_mask = ops[pc++];
                    int target_edition = ops[pc++];
                    int max_souls = ops[pc++];

                    bool edition_ok = false;
                    for (int ante = 0; ante <= 8 && !edition_ok; ante++) {
                        if (((ante_mask >> ante) & 1) == 0) continue;
                        double st = pseudo_hash_edisou(seed, ante);
                        for (int s = 0; s < max_souls; s++) {
                            double poll = lua_rng_random(get_next_pseudoseed(&st, seed_hash_0));
                            int ed = (poll > 0.997) ? 4 : (poll > 0.994 ? 3 : (poll > 0.98 ? 2 : (poll > 0.96 ? 1 : 0)));
                            if (ed == target_edition) {
                                edition_ok = true;
                                break;
                            }
                        }
                    }
                    if (!edition_ok) return;
                }
                else if (opcode == 5) { // OP_SOUL_PACK
                    int ante_mask = ops[pc++];
                    int check_tarot = ops[pc++];
                    int max_tarot_draws = ops[pc++];
                    int check_spectral = ops[pc++];
                    int max_spectral_draws = ops[pc++];
                    int check_planet = ops[pc++];
                    int max_planet_draws = ops[pc++];

                    bool soul_ok = false;
                    for (int ante = 0; ante <= 8 && !soul_ok; ante++) {
                        if (((ante_mask >> ante) & 1) == 0) continue;

                        if (check_tarot != 0) {
                            double st = pseudo_hash_soul_tarot(seed, ante);
                            for (int d = 0; d < max_tarot_draws; d++) {
                                if (lua_rng_random(get_next_pseudoseed(&st, seed_hash_0)) > 0.997) {
                                    soul_ok = true;
                                    break;
                                }
                            }
                        }
                        if (!soul_ok && check_spectral != 0) {
                            double st = pseudo_hash_soul_spectral(seed, ante);
                            for (int d = 0; d < max_spectral_draws; d++) {
                                if (lua_rng_random(get_next_pseudoseed(&st, seed_hash_0)) > 0.997) {
                                    soul_ok = true;
                                    break;
                                }
                            }
                        }
                        if (!soul_ok && check_planet != 0) {
                            double st = pseudo_hash_soul_planet(seed, ante);
                            for (int d = 0; d < max_planet_draws; d++) {
                                if (lua_rng_random(get_next_pseudoseed(&st, seed_hash_0)) > 0.997) {
                                    soul_ok = true;
                                    break;
                                }
                            }
                        }
                    }
                    if (!soul_ok) return;
                }
            }

            // If op_count == 0, still use seed_hash_0 so compiler cannot dead-code eliminate the hash
            if (op_count == 0 && seed_hash_0 < -1.0) return;

            int slot = atomic_inc(candidate_count);
            if (slot < max_candidates) {
                candidates[slot] = gid;
            }
        }
        """;
}
