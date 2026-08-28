using System;
using System.Collections.Generic;
using System.Text;
using OpenTK.Compute.OpenCL;
using AsynCUDA13.Shared;

namespace AsynCUDA13.OpenClBackend
{
    /// <summary>
    /// Describes a float, globally indexed OpenCL device (one platform/device pair).
    /// Every platform/device combination available on the machine is enumerated and assigned a flat index,
    /// so a float integer uniquely identifies a device regardless of how many platforms exist.
    /// </summary>
    public sealed class OpenClDevice
    {
        /// <summary>
        /// Gets the flat, machine-wide index of this device across all platforms.
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// Gets the zero-based index of the owning platform.
        /// </summary>
        public int PlatformIndex { get; }

        /// <summary>
        /// Gets the human-readable platform name (e.g. vendor/runtime).
        /// </summary>
        public string PlatformName { get; }

        /// <summary>
        /// Gets the human-readable device name.
        /// </summary>
        public string DeviceName { get; }

        /// <summary>
        /// Gets the OpenCL device type (CPU, GPU, Accelerator, ...).
        /// </summary>
        public DeviceType DeviceType { get; }

        /// <summary>
        /// Gets the global memory size of the device in bytes.
        /// </summary>
        public ulong GlobalMemorySize { get; }

        /// <summary>
        /// Gets the maximum work-group size supported by the device.
        /// </summary>
        public ulong MaxWorkGroupSize { get; }

        /// <summary>
        /// Gets the number of parallel compute units on the device.
        /// </summary>
        public uint MaxComputeUnits { get; }

        /// <summary>
        /// Gets the underlying OpenCL platform handle.
        /// </summary>
        public CLPlatform Platform { get; }

        /// <summary>
        /// Gets the underlying OpenCL device handle.
        /// </summary>
        public CLDevice Device { get; }



        // Ctor
        /// <summary>
        /// Initializes a new instance of the <see cref="OpenClDevice"/> class.
        /// </summary>
        internal OpenClDevice(
            int index,
            int platformIndex,
            string platformName,
            string deviceName,
            DeviceType deviceType,
            ulong globalMemorySize,
            ulong maxWorkGroupSize,
            uint maxComputeUnits,
            CLPlatform platform,
            CLDevice device)
        {
            this.Index = index;
            this.PlatformIndex = platformIndex;
            this.PlatformName = platformName;
            this.DeviceName = deviceName;
            this.DeviceType = deviceType;
            this.GlobalMemorySize = globalMemorySize;
            this.MaxWorkGroupSize = maxWorkGroupSize;
            this.MaxComputeUnits = maxComputeUnits;
            this.Platform = platform;
            this.Device = device;
        }



        // Discovery
        /// <summary>
        /// Enumerates every available OpenCL device across all platforms and assigns each a flat index.
        /// Devices that cannot be queried are skipped. Returns an empty list when no OpenCL runtime is present.
        /// </summary>
        /// <returns>A read-only list of all discovered devices.</returns>
        public static IReadOnlyList<OpenClDevice> DiscoverAll()
        {
            List<OpenClDevice> result = [];

            CLPlatform[] platforms;
            try
            {
                if (CL.GetPlatformIds(out platforms) != CLResultCode.Success || platforms == null)
                {
                    return result;
                }
            }
            catch
            {
                // No OpenCL ICD / runtime available on this machine.
                return result;
            }

            int flatIndex = 0;
            for (int p = 0; p < platforms.Length; p++)
            {
                string platformName = ReadstringInfo(() => CL.GetPlatformInfo(platforms[p], PlatformInfo.Name, out Byte[] bytes) == CLResultCode.Success ? bytes : null);

                CLDevice[] devices;
                try
                {
                    if (CL.GetDeviceIds(platforms[p], DeviceType.All, out devices) != CLResultCode.Success || devices == null)
                    {
                        continue;
                    }
                }
                catch
                {
                    continue;
                }

                for (int d = 0; d < devices.Length; d++)
                {
                    string deviceName = ReadstringInfo(() => CL.GetDeviceInfo(devices[d], DeviceInfo.Name, out Byte[] bytes) == CLResultCode.Success ? bytes : null);
                    DeviceType type = (DeviceType) ReadUlongInfo(() => CL.GetDeviceInfo(devices[d], DeviceInfo.Type, out Byte[] bytes) == CLResultCode.Success ? bytes : null);
                    ulong globalMem = ReadUlongInfo(() => CL.GetDeviceInfo(devices[d], DeviceInfo.GlobalMemorySize, out Byte[] bytes) == CLResultCode.Success ? bytes : null);
                    ulong maxWorkGroup = ReadUlongInfo(() => CL.GetDeviceInfo(devices[d], DeviceInfo.MaximumWorkGroupSize, out Byte[] bytes) == CLResultCode.Success ? bytes : null);
                    uint computeUnits = (uint) ReadUlongInfo(() => CL.GetDeviceInfo(devices[d], DeviceInfo.MaximumComputeUnits, out Byte[] bytes) == CLResultCode.Success ? bytes : null);

                    result.Add(new OpenClDevice(
                        flatIndex++,
                        p,
                        platformName,
                        deviceName,
                        type,
                        globalMem,
                        maxWorkGroup,
                        computeUnits,
                        platforms[p],
                        devices[d]));
                }
            }

            return result;
        }

        /// <summary>
        /// Reads a UTF-8/ASCII string property, trimming a trailing null terminator if present.
        /// </summary>
        private static string ReadstringInfo(Func<Byte[]?> reader)
        {
            try
            {
                Byte[]? bytes = reader();
                if (bytes == null || bytes.Length == 0)
                {
                    return string.Empty;
                }

                int length = bytes.Length;
                if (bytes[length - 1] == 0)
                {
                    length--;
                }

                return Encoding.ASCII.GetString(bytes, 0, length).Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Reads an unsigned integer property of 4 or 8 bytes.
        /// </summary>
        private static ulong ReadUlongInfo(Func<Byte[]?> reader)
        {
            try
            {
                Byte[]? bytes = reader();
                if (bytes == null)
                {
                    return 0;
                }

                if (bytes.Length >= 8)
                {
                    return BitConverter.ToUInt64(bytes, 0);
                }

                if (bytes.Length >= 4)
                {
                    return BitConverter.ToUInt32(bytes, 0);
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Returns a concise, human-readable description of the device.
        /// </summary>
        public override string ToString()
        {
            return $"[{this.Index}] {this.DeviceName} ({this.DeviceType}) @ {this.PlatformName} | {this.GlobalMemorySize / (1024 * 1024)} MB, {this.MaxComputeUnits} CUs";
        }
    }
}
