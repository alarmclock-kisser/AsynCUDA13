using AsynCUDA13.OpenClBackend;
using AsynCUDA13.Runtime;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Interfaces;
using AsynCUDA13.Shared.RuntimeDtos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AsynCUDA13.Api.Services.DtoBuilders
{
    public static class RuntimeInfosBuilder
    {
        public static RuntimeContextInfo BuildRuntimeContextInfo(IRuntimeService service)
        {
            var contextInfo = new RuntimeContextInfo
            {
                RuntimeType = service is ICudaService ? "CUDA" : service is IOpenClService ? "OpenCL" : "N/A",
                DeviceInfo = BuildRuntimeDeviceInfo(service, null),
                UsageInfo = BuildRuntimeUsageInfo(service),
                MemoryInfos = BuildRuntimeMemoryInfos(service),
                KernelInfos = BuildRuntimeKernelInfos(service)
            };
            return contextInfo;
        }

        public static RuntimeDeviceInfo? BuildRuntimeDeviceInfo(IRuntimeService service, int? index = 0)
        {
            var info = new RuntimeDeviceInfo();
            index ??= service.SelectedDeviceId;
            if ((!index.HasValue || index.Value < 0))
            {
                return null;
            }

            info.DeviceId = index.Value;
            var selectedProperties = service.TotalAvailableDeviceProperties.ElementAtOrDefault(index.Value).Value;
            if (selectedProperties == null)
            {
                info.DeviceName = "N/A";
                info.Properties = [];
                return info;
            }

            info.RuntimeType = service.RuntimeType;
            info.DeviceName = service.SelectedDeviceName ?? service.TotalAvailableDeviceProperties.ElementAtOrDefault(index.Value).Value.FirstOrDefault(kv => kv.Key.Contains("Name", StringComparison.OrdinalIgnoreCase)).Value ?? "N/A";
            info.Properties = selectedProperties;

            return info;
        }

        public static RuntimeDeviceInfo[] BuildRuntimeAllDeviceInfos(IRuntimeService service)
        {
            if (!service.GetType().IsAssignableTo(typeof(ICudaService)) && !service.GetType().IsAssignableTo(typeof(IOpenClService)))
            {
                throw new ArgumentException("IRuntimeService service must be a type that implements IRuntimeService and is either ICudaService or IOpenClService.");
            }

            return (service.TotalAvailableDeviceProperties.Select((props, index) => new RuntimeDeviceInfo
            {
                RuntimeType = service.RuntimeType,
                DeviceId = index,
                DeviceName = props.Value.FirstOrDefault(kv => kv.Key.Contains("Name", StringComparison.OrdinalIgnoreCase)).Value,
                Properties = props.Value

            }).ToArray() ?? []);
        }

        public static RuntimeUsageInfo? BuildRuntimeUsageInfo(IRuntimeService service)
        {
            if (!service.Online)
            {
                return null;
            }

            var info = new RuntimeUsageInfo
            {
                ActiveThreads = service is ICudaService ? (service as ICudaService)?.ThreadsActive ?? 0 : 0,
                IdleThreads = service is ICudaService ? (service as ICudaService)?.ThreadsIdle ?? 0 : 0,
                TotalAllocations = service.TotalAllocations,
                TotalAllocatedBytes = service.TotalAllocatedBytes.ToString()
            };

            return info;
        }

        public static RuntimeMemInfo[]? BuildRuntimeMemoryInfos(IRuntimeService service, string? indexPointerOrId = null)
        {
            if (!service.Online)
            {
                return null;
            }

            var infos = service.RegisteredMemory.Select(mem => new RuntimeMemInfo
            {
                Id = mem.Id.ToString(),
                ElementType = mem.ElementType.ToString().Split('.').Last(),
                Pointers = mem.PointerIds.Select(ptr => ptr.ToString()).ToArray(),
                Lengths = mem.PointerLengths.Select(len => len.ToString()).ToArray(),
                Message = mem.Message
            }).Where(mem => indexPointerOrId == null || mem.Id == indexPointerOrId || (mem.Pointers.Contains(indexPointerOrId) && mem.IndexPointer == indexPointerOrId)).ToArray() ?? [];

            return infos;
        }

        public static RuntimeMemInfo? BuildRuntimeMemoryInfo(IRuntimeService service, string indexPointerOrId)
        {
            if (!service.Online)
            {
                return null;
            }

            var mem = service[indexPointerOrId];
            if (mem == null)
            {
                return null;
            }

            var info = new RuntimeMemInfo
            {
                Id = mem.Id.ToString(),
                ElementType = mem.ElementType.ToString().Split('.').Last(),
                Pointers = mem.PointerIds.Select(ptr => ptr.ToString()).ToArray(),
                Lengths = mem.PointerLengths.Select(len => len.ToString()).ToArray(),
                Message = mem.Message
            };
            return info;
        }

        public static RuntimeKernelInfo[]? BuildRuntimeKernelInfos(IRuntimeService service, bool filterCompiled = true)
        {
            if (!service.Online || service.Compiler == null || service.Launcher == null)
            {
                return null;
            }

            string[] cuPaths = service.Compiler.GetSourceFiles().ToArray() ?? [];
            if (filterCompiled)
            {
                cuPaths = cuPaths.Where(cu => service.Compiler.GetCompiledFiles().Any(ptx => Path.GetFileNameWithoutExtension(ptx) == Path.GetFileNameWithoutExtension(cu))).ToArray() ?? [];
            }

            var infos = cuPaths.Select(kernel => {
                var args = service.Compiler.GetArguments(kernel);
                return new RuntimeKernelInfo
                {
                    SourcePath = service.Compiler.GetSourceFiles().FirstOrDefault(src => Path.GetFileNameWithoutExtension(src) == Path.GetFileNameWithoutExtension(kernel))?.ToString() ?? string.Empty,
                    PtxPath = service.Compiler.GetCompiledFiles().FirstOrDefault(comp => Path.GetFileNameWithoutExtension(comp) == Path.GetFileNameWithoutExtension(kernel))?.ToString(),
                    KernelCode = service.Compiler.GetKernelCode(kernel) ?? string.Empty,
                    FunctionName = service.Compiler.GetFunctionName(kernel) ?? string.Empty,
                    ArgumentNames = args.Keys.ToArray(),
                    ArgumentTypes = args.Values.Select(type => type.Name).ToArray()

                };
            }).ToArray() ?? [];

            return infos;
        }

        public static RuntimeKernelInfo? BuildRuntimeKernelInfo(IRuntimeService service, string kernelName)
        {
            if (!service.Online || service.Compiler == null || service.Launcher == null)
            {
                return null;
            }
            string? srcPath = service.Compiler.GetSourceFiles().FirstOrDefault(src => Path.GetFileNameWithoutExtension(src) == kernelName);
            if (srcPath == null)
            {
                return null;
            }
            var args = service.Compiler.GetArguments(srcPath);
            var info = new RuntimeKernelInfo
            {
                SourcePath = srcPath,
                PtxPath = service.Compiler.GetCompiledFiles().FirstOrDefault(comp => Path.GetFileNameWithoutExtension(comp) == kernelName)?.ToString(),
                KernelCode = service.Compiler.GetKernelCode(srcPath) ?? string.Empty,
                FunctionName = service.Compiler.GetFunctionName(srcPath) ?? string.Empty,
                ArgumentNames = args.Keys.ToArray(),
                ArgumentTypes = args.Values.Select(type => type.Name).ToArray()
            };
            return info;
        }

    }
}
