using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Motely.Gpu;

/// <summary>
/// Zero-dependency, NativeAOT-clean P/Invoke bindings for the OpenCL 1.2 / 2.0 C runtime API.
/// Guarded against browser WASM execution via <see cref="OperatingSystem.IsBrowser"/>.
/// </summary>
internal static unsafe class OpenClNative
{
    private const string LibraryName = "OpenCL";

    public const int CL_SUCCESS = 0;

    // cl_device_type
    public const ulong CL_DEVICE_TYPE_DEFAULT = 1UL << 0;
    public const ulong CL_DEVICE_TYPE_CPU = 1UL << 1;
    public const ulong CL_DEVICE_TYPE_GPU = 1UL << 2;
    public const ulong CL_DEVICE_TYPE_ACCELERATOR = 1UL << 3;
    public const ulong CL_DEVICE_TYPE_ALL = 0xFFFFFFFFUL;

    // cl_platform_info
    public const uint CL_PLATFORM_VERSION = 0x0901;
    public const uint CL_PLATFORM_NAME = 0x0902;
    public const uint CL_PLATFORM_VENDOR = 0x0903;

    // cl_device_info
    public const uint CL_DEVICE_TYPE = 0x1000;
    public const uint CL_DEVICE_VENDOR_ID = 0x1001;
    public const uint CL_DEVICE_MAX_COMPUTE_UNITS = 0x1002;
    public const uint CL_DEVICE_MAX_WORK_GROUP_SIZE = 0x1004;
    public const uint CL_DEVICE_MAX_CLOCK_FREQUENCY = 0x100C;
    public const uint CL_DEVICE_GLOBAL_MEM_SIZE = 0x101F;
    public const uint CL_DEVICE_NAME = 0x102B;
    public const uint CL_DEVICE_VENDOR = 0x102C;
    public const uint CL_DRIVER_VERSION = 0x102D;
    public const uint CL_DEVICE_VERSION = 0x102F;
    public const uint CL_DEVICE_EXTENSIONS = 0x1030;
    public const uint CL_DEVICE_DOUBLE_FP_CONFIG = 0x1032;

    // cl_mem_flags
    public const ulong CL_MEM_READ_WRITE = 1UL << 0;
    public const ulong CL_MEM_WRITE_ONLY = 1UL << 1;
    public const ulong CL_MEM_READ_ONLY = 1UL << 2;
    public const ulong CL_MEM_COPY_HOST_PTR = 1UL << 5;

    // cl_program_build_info
    public const uint CL_PROGRAM_BUILD_LOG = 0x1183;

    public const uint CL_TRUE = 1;
    public const uint CL_FALSE = 0;

    static OpenClNative()
    {
        if (!OperatingSystem.IsBrowser())
        {
            NativeLibrary.SetDllImportResolver(typeof(OpenClNative).Assembly, ResolveOpenClLibrary);
        }
    }

    private static nint ResolveOpenClLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal))
            return nint.Zero;

        if (OperatingSystem.IsBrowser())
            return nint.Zero;

        if (OperatingSystem.IsWindows())
        {
            if (NativeLibrary.TryLoad("OpenCL.dll", assembly, searchPath, out nint handle))
                return handle;

            string sysPath = Path.Combine(Environment.SystemDirectory, "OpenCL.dll");
            if (NativeLibrary.TryLoad(sysPath, out handle))
                return handle;
        }
        else if (OperatingSystem.IsLinux())
        {
            if (NativeLibrary.TryLoad("libOpenCL.so.1", assembly, searchPath, out nint handle))
                return handle;
            if (NativeLibrary.TryLoad("libOpenCL.so", assembly, searchPath, out handle))
                return handle;
        }
        else if (OperatingSystem.IsMacOS())
        {
            if (NativeLibrary.TryLoad("/System/Library/Frameworks/OpenCL.framework/OpenCL", out nint handle))
                return handle;
        }

        return nint.Zero;
    }

    [DllImport(LibraryName, ExactSpelling = true)]
    public static extern int clGetPlatformIDs(uint num_entries, nint* platforms, out uint num_platforms);

    [DllImport(LibraryName, ExactSpelling = true)]
    public static extern int clGetPlatformInfo(
        nint platform,
        uint param_name,
        nuint param_value_size,
        void* param_value,
        out nuint param_value_size_ret
    );

    [DllImport(LibraryName, ExactSpelling = true)]
    public static extern int clGetDeviceIDs(
        nint platform,
        ulong device_type,
        uint num_entries,
        nint* devices,
        out uint num_devices
    );

    [DllImport(LibraryName, ExactSpelling = true)]
    public static extern int clGetDeviceInfo(
        nint device,
        uint param_name,
        nuint param_value_size,
        void* param_value,
        out nuint param_value_size_ret
    );

    [DllImport(LibraryName, ExactSpelling = true)]
    public static extern nint clCreateContext(
        nint* properties,
        uint num_devices,
        nint* devices,
        nint pfn_notify,
        nint user_data,
        out int errcode_ret
    );

    [DllImport(LibraryName, ExactSpelling = true)]
    public static extern nint clCreateCommandQueueWithProperties(
        nint context,
        nint device,
        nint* properties,
        out int errcode_ret
    );

    [DllImport(LibraryName, ExactSpelling = true)]
    public static extern nint clCreateCommandQueue(
        nint context,
        nint device,
        ulong properties,
        out int errcode_ret
    );

    [DllImport(LibraryName, ExactSpelling = true)]
    public static extern nint clCreateProgramWithSource(
        nint context,
        uint count,
        byte** strings,
        nuint* lengths,
        out int errcode_ret
    );

    [DllImport(LibraryName, ExactSpelling = true)]
    public static extern int clBuildProgram(
        nint program,
        uint num_devices,
        nint* device_list,
        byte* options,
        nint pfn_notify,
        nint user_data
    );

    [DllImport(LibraryName, ExactSpelling = true)]
    public static extern int clGetProgramBuildInfo(
        nint program,
        nint device,
        uint param_name,
        nuint param_value_size,
        void* param_value,
        out nuint param_value_size_ret
    );

    [DllImport(LibraryName, ExactSpelling = true)]
    public static extern nint clCreateKernel(
        nint program,
        byte* kernel_name,
        out int errcode_ret
    );

    [DllImport(LibraryName, ExactSpelling = true)]
    public static extern nint clCreateBuffer(
        nint context,
        ulong flags,
        nuint size,
        void* host_ptr,
        out int errcode_ret
    );

    [DllImport(LibraryName, ExactSpelling = true)]
    public static extern int clEnqueueWriteBuffer(
        nint command_queue,
        nint buffer,
        uint blocking_write,
        nuint offset,
        nuint size,
        void* ptr,
        uint num_events_in_wait_list,
        nint* event_wait_list,
        nint* evt
    );

    [DllImport(LibraryName, ExactSpelling = true)]
    public static extern int clEnqueueReadBuffer(
        nint command_queue,
        nint buffer,
        uint blocking_read,
        nuint offset,
        nuint size,
        void* ptr,
        uint num_events_in_wait_list,
        nint* event_wait_list,
        nint* evt
    );

    [DllImport(LibraryName, ExactSpelling = true)]
    public static extern int clSetKernelArg(
        nint kernel,
        uint arg_index,
        nuint arg_size,
        void* arg_value
    );

    [DllImport(LibraryName, ExactSpelling = true)]
    public static extern int clEnqueueNDRangeKernel(
        nint command_queue,
        nint kernel,
        uint work_dim,
        nuint* global_work_offset,
        nuint* global_work_size,
        nuint* local_work_size,
        uint num_events_in_wait_list,
        nint* event_wait_list,
        nint* evt
    );

    [DllImport(LibraryName, ExactSpelling = true)]
    public static extern int clFinish(nint command_queue);

    [DllImport(LibraryName, ExactSpelling = true)]
    public static extern int clReleaseMemObject(nint memobj);

    [DllImport(LibraryName, ExactSpelling = true)]
    public static extern int clReleaseKernel(nint kernel);

    [DllImport(LibraryName, ExactSpelling = true)]
    public static extern int clReleaseProgram(nint program);

    [DllImport(LibraryName, ExactSpelling = true)]
    public static extern int clReleaseCommandQueue(nint command_queue);

    [DllImport(LibraryName, ExactSpelling = true)]
    public static extern int clReleaseContext(nint context);
}
