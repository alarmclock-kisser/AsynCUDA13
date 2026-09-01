using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Interfaces;
using OpenTK.Compute.OpenCL;

namespace AsynCUDA13.OpenClBackend
{
    internal static class OpenClDevicePropertyFormatter
    {
        public static Dictionary<string, string> GetProperties(int deviceId, IRollingFileMemoryLogger logger)
        {
            var device = OpenClDevice.DiscoverAll().FirstOrDefault(d => d.Index == deviceId);
            if (device == null)
            {
                logger.LogWarning($"OpenClDevicePropertyFormatter: Device Index <{deviceId}> not found.");
                return [];
            }

            return GetProperties(device.Device);
        }

        public static Dictionary<string, string> GetProperties(CLDevice clDevice)
        {
            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            DeviceInfo[] infoTypes = Enum.GetValues<DeviceInfo>();

            foreach (DeviceInfo infoType in infoTypes)
            {
                CLResultCode result = CL.GetDeviceInfo(clDevice, infoType, out byte[] outBytes);
                if (result != CLResultCode.Success || outBytes == null || outBytes.Length == 0)
                {
                    properties[infoType.ToString()] = "N/A";
                    continue;
                }

                properties[infoType.ToString()] = FormatPropertyValue(infoType, outBytes);
            }

            return properties;
        }

        private static string FormatPropertyValue(DeviceInfo infoType, byte[] bytes)
        {
            // 1. Strings (Null-terminierte C-Strings)
            switch (infoType)
            {
                case DeviceInfo.Name:
                case DeviceInfo.Vendor:
                case DeviceInfo.DriverVersion:
                case DeviceInfo.Profile:
                case DeviceInfo.Version:
                case DeviceInfo.OpenClCVersion:
                case DeviceInfo.Extensions:
                case DeviceInfo.BuiltInKernels:
                case DeviceInfo.IntermediateLanguageVersion:
                    return ReadCLString(bytes);
            }

            // 2. cl_bool (In OpenCL C-API immer cl_uint / 4 Bytes)
            switch (infoType)
            {
                case DeviceInfo.CompilerAvailable:
                case DeviceInfo.LinkerAvailable:
                case DeviceInfo.EndianLittle:
                case DeviceInfo.Available:
                case DeviceInfo.ImageSupport:
                // case DeviceInfo.HostUnifiedMemory:
                case DeviceInfo.ErrorCorrectionSupport:
                case DeviceInfo.PreferredInteropUserSync:
                case DeviceInfo.SubGroupIndependentForwardProgress:
                    return BitConverter.ToUInt32(bytes, 0) != 0 ? "True" : "False";
            }

            // 3. Spezialisierte Bitmasken
            switch (infoType)
            {
                case DeviceInfo.SingleFloatingPointConfiguration:
                case DeviceInfo.DoubleFloatingPointConfiguration:
                    ulong fpFlags = bytes.Length == 8 ? BitConverter.ToUInt64(bytes, 0) : BitConverter.ToUInt32(bytes, 0);
                    return FormatFpConfig(fpFlags);

                case DeviceInfo.ExecutionCapabilities:
                    uint execCap = BitConverter.ToUInt32(bytes, 0);
                    var caps = new List<string>();
                    if ((execCap & 1) != 0)
                    {
                        caps.Add("Kernel");
                    }

                    if ((execCap & 2) != 0)
                    {
                        caps.Add("Native");
                    }

                    return string.Join(" | ", caps);

                // case DeviceInfo.QueueProperties:
                case DeviceInfo.QueueOnDeviceProperties:
                    uint queueProp = BitConverter.ToUInt32(bytes, 0);
                    var qProps = new List<string>();
                    if ((queueProp & 1) != 0)
                    {
                        qProps.Add("OutOfOrderExec");
                    }

                    if ((queueProp & 2) != 0)
                    {
                        qProps.Add("Profiling");
                    }

                    return qProps.Count > 0 ? string.Join(" | ", qProps) : "None";

                case DeviceInfo.SvmCapabilities:
                    uint svmCap = BitConverter.ToUInt32(bytes, 0);
                    var svmList = new List<string>();
                    if ((svmCap & (1 << 0)) != 0)
                    {
                        svmList.Add("CoarseGrainedBuffer");
                    }

                    if ((svmCap & (1 << 1)) != 0)
                    {
                        svmList.Add("FineGrainedBuffer");
                    }

                    if ((svmCap & (1 << 2)) != 0)
                    {
                        svmList.Add("FineGrainedSystem");
                    }

                    if ((svmCap & (1 << 3)) != 0)
                    {
                        svmList.Add("Atomics");
                    }

                    return svmList.Count > 0 ? string.Join(" | ", svmList) : "None";
            }

            // 4. Arrays (size_t[])
            if (infoType == DeviceInfo.MaximumWorkItemSizes)
            {
                return FormatSizeTArray(bytes);
            }

            // 5. Speichergrößen (Bytes -> formatierte Angaben)
            switch (infoType)
            {
                case DeviceInfo.GlobalMemorySize:
                case DeviceInfo.MaximumMemoryAllocationSize:
                case DeviceInfo.GlobalMemoryCacheSize:
                case DeviceInfo.LocalMemorySize:
                case DeviceInfo.MaximumConstantBufferSize:
                case DeviceInfo.ImageMaximumBufferSize:
                case DeviceInfo.PrintfBufferSize:
                    if (bytes.Length == 8)
                    {
                        return FormatMemorySize(BitConverter.ToUInt64(bytes, 0));
                    }

                    if (bytes.Length == 4)
                    {
                        return FormatMemorySize(BitConverter.ToUInt32(bytes, 0));
                    }

                    break;
            }

            // 6. Generische Zahlen-Interpretation
            if (bytes.Length == 4)
            {
                return BitConverter.ToUInt32(bytes, 0).ToString();
            }
            if (bytes.Length == 8)
            {
                return BitConverter.ToUInt64(bytes, 0).ToString();
            }
            if (bytes.Length == 1)
            {
                return bytes[0].ToString();
            }

            // Fallback: Hex-Dump
            return $"0x{Convert.ToHexString(bytes)}";
        }

        private static string ReadCLString(byte[] bytes)
        {
            int nullIndex = Array.IndexOf(bytes, (byte) 0);
            int length = nullIndex >= 0 ? nullIndex : bytes.Length;
            return length == 0 ? "<null>" : Encoding.UTF8.GetString(bytes, 0, length).Trim();
        }

        private static string FormatFpConfig(ulong flags)
        {
            var list = new List<string>();
            if ((flags & (1 << 0)) != 0)
            {
                list.Add("Denorm");
            }

            if ((flags & (1 << 1)) != 0)
            {
                list.Add("InfNan");
            }

            if ((flags & (1 << 2)) != 0)
            {
                list.Add("RoundNearest");
            }

            if ((flags & (1 << 3)) != 0)
            {
                list.Add("RoundZero");
            }

            if ((flags & (1 << 4)) != 0)
            {
                list.Add("RoundInf");
            }

            if ((flags & (1 << 5)) != 0)
            {
                list.Add("FMA");
            }

            if ((flags & (1 << 6)) != 0)
            {
                list.Add("SoftFloat");
            }

            return list.Count > 0 ? string.Join(" | ", list) : "None";
        }

        private static string FormatMemorySize(ulong val)
        {
            if (val >= 1024 * 1024 * 1024)
            {
                return $"{val} ({val / (1024.0 * 1024.0 * 1024.0):F2} GB)";
            }
            if (val >= 1024 * 1024)
            {
                return $"{val} ({val / (1024.0 * 1024.0):F2} MB)";
            }
            return val >= 1024 ? $"{val} ({val / 1024.0:F2} KB)" : $"{val} Bytes";
        }

        private static string FormatSizeTArray(byte[] bytes)
        {
            int size = IntPtr.Size;
            int count = bytes.Length / size;
            var values = new List<string>(count);

            for (int i = 0; i < count; i++)
            {
                ulong val = size == 8
                    ? BitConverter.ToUInt64(bytes, i * size)
                    : BitConverter.ToUInt32(bytes, i * size);
                values.Add(val.ToString());
            }

            return $"[{string.Join(", ", values)}]";
        }
    }
}