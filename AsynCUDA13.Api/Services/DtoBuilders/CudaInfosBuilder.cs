using AsynCUDA13.Runtime;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.CudaDtos;
using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Api.Services.DtoBuilders
{
    public static class CudaInfosBuilder
    {
        public static CudaContextInfo BuildCudaContextInfo(ICudaService service)
        {
            var contextInfo = new CudaContextInfo
            {
                DeviceInfo = BuildCudaDeviceInfo(service),
                UsageInfo = BuildCudaUsageInfo(service),
                MemoryInfos = BuildCudaMemoryInfos(service),
                KernelInfos = BuildCudaKernelInfos(service)
            };
            return contextInfo;
        }

        public static CudaDeviceInfo? BuildCudaDeviceInfo(ICudaService service)
        {
            var info = new CudaDeviceInfo();
            if (service.SelectedDeviceId < 0)
            {
                return null;
            }

            info.DeviceId = service.SelectedDeviceId;
            var selectedProperties = service.SelectedDeviceProperties;
            if (selectedProperties == null)
            {
                info.DeviceName = "N/A";
                info.Properties = [];
                return info;
            }

            info.DeviceName = selectedProperties.DeviceName;
            info.Properties = selectedProperties.GetType()
                .GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                .ToDictionary(
                    prop => prop.Name,
                    prop => prop.GetValue(selectedProperties)?.ToString() ?? string.Empty);

            return info;
        }

        public static CudaDeviceInfo[] BuildCudaAllDeviceInfos()
        {
            if (!CudaAvailabilityTester.IsCudaAvailable())
            {
                return [];
            }

            var infos = CudaService.GetAvailableDevicesProperties().Select((props, index) => new CudaDeviceInfo
            {
                DeviceId = index,
                DeviceName = props.Value.DeviceName,
                Properties = props.Value.GetType()
                    .GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                    .ToDictionary(
                        prop => prop.Name,
                        prop => prop.GetValue(props.Value)?.ToString() ?? string.Empty)
            }).ToArray() ?? [];

            return infos;
        }

        public static CudaUsageInfo? BuildCudaUsageInfo(ICudaService service)
        {
            if (!service.Online)
            {
                return null;
            }

            var info = new CudaUsageInfo
            {
                ActiveThreads = service.ThreadsActive,
                IdleThreads = service.ThreadsIdle,
                RegisteredMemoryCount = service.RegisteredMemoryobjects,
                TotalAllocatedBytes = service.TotalAllocatedBytes.ToString()
            };

            return info;
        }

        public static CudaMemInfo[]? BuildCudaMemoryInfos(ICudaService service, string? indexPointerOrId = null)
        {
            if (!service.Online)
            {
                return null;
            }

            var infos = service.RegisteredMemory.Select(mem => new CudaMemInfo
            {
                Id = mem.Id.ToString(),
                ElementType = mem.ElementType.ToString(),
                Pointers = mem.Pointers.Select(ptr => ptr.ToString()).ToArray(),
                Lengths = mem.PointerLengths.Select(len => len.ToString()).ToArray(),
                Message = mem.Message
            }).Where(mem => indexPointerOrId == null ? true : mem.Id == indexPointerOrId || (mem.Pointers.Contains(indexPointerOrId) && mem.IndexPointer == indexPointerOrId)).ToArray() ?? [];

            return infos;
        }

        public static CudaMemInfo? BuildCudaMemoryInfo(ICudaService service, string indexPointerOrId)
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

            var info = new CudaMemInfo
            {
                Id = mem.Id.ToString(),
                ElementType = mem.ElementType.ToString(),
                Pointers = mem.Pointers.Select(ptr => ptr.ToString()).ToArray(),
                Lengths = mem.PointerLengths.Select(len => len.ToString()).ToArray(),
                Message = mem.Message
            };
            return info;
        }

        public static CudaKernelInfo[]? BuildCudaKernelInfos(ICudaService service, bool filterCompiled = true)
        {
            if (!service.Online || service._compiler == null || service._launcher == null)
            {
                return null;
            }

            string[] cuPaths = CudaCompiler.SourceFiles.ToArray() ?? [];
            if (filterCompiled)
            {
                cuPaths = cuPaths.Where(cu => CudaCompiler.CompiledFiles.Any(ptx => Path.GetFileNameWithoutExtension(ptx) == Path.GetFileNameWithoutExtension(cu))).ToArray() ?? [];
            }

            var infos = cuPaths.Select(kernel => new CudaKernelInfo
            {
                SourcePath = CudaCompiler.SourceFiles.FirstOrDefault(src => Path.GetFileNameWithoutExtension(src) == Path.GetFileNameWithoutExtension(kernel))?.ToString() ?? string.Empty,
                PtxPath = CudaCompiler.CompiledFiles.FirstOrDefault(comp => Path.GetFileNameWithoutExtension(comp) == Path.GetFileNameWithoutExtension(kernel))?.ToString(),
                KernelCode = CudaCompiler.GetKernelCode(kernel) ?? string.Empty,
                FunctionName = Path.GetFileNameWithoutExtension(kernel),
                ArgumentNames = service._compiler.GetArguments(CudaCompiler.GetKernelCode(kernel) ?? string.Empty).Keys.ToArray(),
                ArgumentTypes = service._compiler.GetArguments(CudaCompiler.GetKernelCode(kernel) ?? string.Empty).Values.Select(type => type.Name).ToArray()

            }).ToArray() ?? [];

            return infos;
        }

        public static CudaKernelInfo? BuildCudaKernelInfo(ICudaService service, string kernelName)
        {
            if (!service.Online || service._compiler == null || service._launcher == null)
            {
                return null;
            }
            string? cuPath = CudaCompiler.SourceFiles.FirstOrDefault(src => Path.GetFileNameWithoutExtension(src) == kernelName);
            if (cuPath == null)
            {
                return null;
            }
            var info = new CudaKernelInfo
            {
                SourcePath = cuPath,
                PtxPath = CudaCompiler.CompiledFiles.FirstOrDefault(comp => Path.GetFileNameWithoutExtension(comp) == kernelName)?.ToString(),
                KernelCode = CudaCompiler.GetKernelCode(cuPath) ?? string.Empty,
                FunctionName = kernelName,
                ArgumentNames = service._compiler.GetArguments(CudaCompiler.GetKernelCode(cuPath) ?? string.Empty).Keys.ToArray(),
                ArgumentTypes = service._compiler.GetArguments(CudaCompiler.GetKernelCode(cuPath) ?? string.Empty).Values.Select(type => type.Name).ToArray()
            };
            return info;
        }

    }
}
