using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;

namespace Motely.Gpu;

/// <summary>
/// Information about an OpenCL hardware device capable of accelerating Motely seed searches.
/// </summary>
public sealed class GpuDeviceInfo
{
    private static readonly object DiscoveryLock = new();
    private static IReadOnlyList<GpuDeviceInfo>? _cachedDevices;
    private static GpuDeviceInfo? _cachedBestDevice;
    private static bool _discovered;

    [JsonIgnore]
    public nint PlatformId { get; init; }

    [JsonIgnore]
    public nint DeviceId { get; init; }
    public string Name { get; init; } = "";
    public string Vendor { get; init; } = "";
    public string DriverVersion { get; init; } = "";
    public string DeviceVersion { get; init; } = "";
    public string PlatformName { get; init; } = "";
    public uint VendorId { get; init; }
    public uint ComputeUnits { get; init; }
    public uint MaxClockFrequencyMHz { get; init; }
    public ulong GlobalMemoryBytes { get; init; }

    [JsonIgnore]
    public nuint MaxWorkGroupSize { get; init; }
    public ulong MaxWorkGroupSizeLimit => (ulong)MaxWorkGroupSize;
    public bool SupportsFp64 { get; init; }
    public bool IsDiscreteGpu { get; init; }

    public static bool IsGpuAvailable => GetBestDevice() is { SupportsFp64: true };

    /// <summary>
    /// Discovers all OpenCL GPU devices on the current machine. Returns an empty list on browser WASM
    /// or when no OpenCL runtime is installed.
    /// </summary>
    public static IReadOnlyList<GpuDeviceInfo> DiscoverDevices(bool forceRefresh = false)
    {
        if (OperatingSystem.IsBrowser())
            return [];

        lock (DiscoveryLock)
        {
            if (_discovered && !forceRefresh && _cachedDevices is not null)
                return _cachedDevices;

            var list = new List<GpuDeviceInfo>();
            try
            {
                DiscoverDevicesCore(list);
            }
            catch
            {
                // No OpenCL runtime or driver failure; fall back cleanly.
            }

            _cachedDevices = list;
            _cachedBestDevice = list
                .Where(d => d.SupportsFp64)
                .OrderByDescending(d => d.IsDiscreteGpu)
                .ThenByDescending(d => (ulong)d.ComputeUnits * Math.Max(1u, d.MaxClockFrequencyMHz))
                .ThenByDescending(d => d.GlobalMemoryBytes)
                .FirstOrDefault();
            _discovered = true;
            return _cachedDevices;
        }
    }

    /// <summary>
    /// Returns the highest-throughput FP64-capable GPU device on the host (e.g., NVIDIA GeForce RTX 4070 SUPER),
    /// or null if no FP64 GPU is available.
    /// </summary>
    public static GpuDeviceInfo? GetBestDevice()
    {
        if (OperatingSystem.IsBrowser())
            return null;

        if (!_discovered)
            DiscoverDevices();

        return _cachedBestDevice;
    }

    private static unsafe void DiscoverDevicesCore(List<GpuDeviceInfo> list)
    {
        if (OpenClNative.clGetPlatformIDs(0, null, out uint numPlatforms) != OpenClNative.CL_SUCCESS || numPlatforms == 0)
            return;

        if (numPlatforms > 32)
            numPlatforms = 32;

        nint* platforms = stackalloc nint[(int)numPlatforms];
        if (OpenClNative.clGetPlatformIDs(numPlatforms, platforms, out numPlatforms) != OpenClNative.CL_SUCCESS)
            return;

        nint* devices = stackalloc nint[32];
        for (uint p = 0; p < numPlatforms; p++)
        {
            nint platform = platforms[p];
            string platformName = GetPlatformString(platform, OpenClNative.CL_PLATFORM_NAME);

            int err = OpenClNative.clGetDeviceIDs(platform, OpenClNative.CL_DEVICE_TYPE_GPU, 0, null, out uint numDevices);
            if (err != OpenClNative.CL_SUCCESS || numDevices == 0)
                continue;

            if (numDevices > 32)
                numDevices = 32;

            if (OpenClNative.clGetDeviceIDs(platform, OpenClNative.CL_DEVICE_TYPE_GPU, numDevices, devices, out numDevices) != OpenClNative.CL_SUCCESS)
                continue;

            for (uint d = 0; d < numDevices; d++)
            {
                nint device = devices[d];
                string name = GetDeviceString(device, OpenClNative.CL_DEVICE_NAME).Trim();
                string vendor = GetDeviceString(device, OpenClNative.CL_DEVICE_VENDOR).Trim();
                string driverVersion = GetDeviceString(device, OpenClNative.CL_DRIVER_VERSION).Trim();
                string deviceVersion = GetDeviceString(device, OpenClNative.CL_DEVICE_VERSION).Trim();
                string extensions = GetDeviceString(device, OpenClNative.CL_DEVICE_EXTENSIONS);

                uint vendorId = GetDeviceUInt(device, OpenClNative.CL_DEVICE_VENDOR_ID);
                uint computeUnits = GetDeviceUInt(device, OpenClNative.CL_DEVICE_MAX_COMPUTE_UNITS);
                uint clockMHz = GetDeviceUInt(device, OpenClNative.CL_DEVICE_MAX_CLOCK_FREQUENCY);
                ulong globalMem = GetDeviceULong(device, OpenClNative.CL_DEVICE_GLOBAL_MEM_SIZE);
                nuint maxWgSize = GetDeviceNUInt(device, OpenClNative.CL_DEVICE_MAX_WORK_GROUP_SIZE);
                ulong fp64Config = GetDeviceULong(device, OpenClNative.CL_DEVICE_DOUBLE_FP_CONFIG);

                bool supportsFp64 =
                    fp64Config != 0
                    || extensions.Contains("cl_khr_fp64", StringComparison.OrdinalIgnoreCase);

                bool isDiscrete =
                    vendorId == 0x10DE // NVIDIA
                    || vendorId == 0x1002 // AMD
                    || vendor.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
                    || vendor.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("RTX", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("GTX", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase);

                list.Add(
                    new GpuDeviceInfo
                    {
                        PlatformId = platform,
                        DeviceId = device,
                        Name = name,
                        Vendor = vendor,
                        DriverVersion = driverVersion,
                        DeviceVersion = deviceVersion,
                        PlatformName = platformName.Trim(),
                        VendorId = vendorId,
                        ComputeUnits = computeUnits,
                        MaxClockFrequencyMHz = clockMHz,
                        GlobalMemoryBytes = globalMem,
                        MaxWorkGroupSize = maxWgSize > 0 ? maxWgSize : 256,
                        SupportsFp64 = supportsFp64,
                        IsDiscreteGpu = isDiscrete,
                    }
                );
            }
        }
    }

    private static unsafe string GetPlatformString(nint platform, uint param)
    {
        if (OpenClNative.clGetPlatformInfo(platform, param, 0, null, out nuint size) != OpenClNative.CL_SUCCESS || size == 0)
            return "";

        if (size > 8192)
            size = 8192;

        byte* buf = stackalloc byte[(int)size];
        if (OpenClNative.clGetPlatformInfo(platform, param, size, buf, out _) != OpenClNative.CL_SUCCESS)
            return "";

        int len = (int)size;
        while (len > 0 && buf[len - 1] == 0)
            len--;
        return Encoding.UTF8.GetString(buf, len);
    }

    private static unsafe string GetDeviceString(nint device, uint param)
    {
        if (OpenClNative.clGetDeviceInfo(device, param, 0, null, out nuint size) != OpenClNative.CL_SUCCESS || size == 0)
            return "";

        if (size > 16384)
            size = 16384;

        byte* buf = stackalloc byte[(int)size];
        if (OpenClNative.clGetDeviceInfo(device, param, size, buf, out _) != OpenClNative.CL_SUCCESS)
            return "";

        int len = (int)size;
        while (len > 0 && buf[len - 1] == 0)
            len--;
        return Encoding.UTF8.GetString(buf, len);
    }

    private static unsafe uint GetDeviceUInt(nint device, uint param)
    {
        uint val = 0;
        OpenClNative.clGetDeviceInfo(device, param, sizeof(uint), &val, out _);
        return val;
    }

    private static unsafe ulong GetDeviceULong(nint device, uint param)
    {
        ulong val = 0;
        OpenClNative.clGetDeviceInfo(device, param, sizeof(ulong), &val, out _);
        return val;
    }

    private static unsafe nuint GetDeviceNUInt(nint device, uint param)
    {
        nuint val = 0;
        OpenClNative.clGetDeviceInfo(device, param, (nuint)sizeof(nuint), &val, out _);
        return val;
    }
}
