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

        public static CudaDeviceInfo BuildCudaDeviceInfo(ICudaService service)
        {
            var info = new CudaDeviceInfo();
            if (service.SelectedDeviceId < 0)
            {
                return info;
            }

            info.DeviceId = service.SelectedDeviceId;
            info.DeviceName = service.SelectedDeviceProperties?.DeviceName ?? "N/A";
            info.Properties = service.SelectedDeviceProperties?.GetType()
                .GetProperties()
                .ToDictionary(
                    prop => prop.Name,
                    prop => prop.GetValue(service.SelectedDeviceProperties)?.ToString() ?? string.Empty)
                ?? [];

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
                    .GetProperties()
                    .ToDictionary(
                        prop => prop.Name,
                        prop => prop.GetValue(props.Value)?.ToString() ?? string.Empty)
            }).ToArray() ?? [];

            return infos;
        }

        public static CudaUsageInfo BuildCudaUsageInfo(ICudaService service)
        {
            if (!service.Online)
            {
                return new CudaUsageInfo();
            }

            var info = new CudaUsageInfo
            {
                ActiveThreads = service.ThreadsActive,
                IdleThreads = service.ThreadsIdle,
                RegisteredMemoryCount = service.RegisteredMemoryObjects,
                TotalAllocatedBytes = service.TotalAllocated.ToString()
            };

            return info;
        }

        public static CudaMemInfo[] BuildCudaMemoryInfos(ICudaService service, string? indexPointerOrId = null)
        {
            if (!service.Online)
            {
                return [];
            }

            var infos = service.RegisteredMemory.Select(mem => new CudaMemInfo
            {
                Id = mem.Id.ToString(),
                ElementType = mem.ElementType.ToString(),
                Pointers = mem.Pointers.Select(ptr => ptr.ToString()).ToArray(),
                Lengths = mem.Lengths.Select(len => len.ToString()).ToArray(),
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

            var mem = service.RegisteredMemory.FirstOrDefault(m => m.Id.ToString() == indexPointerOrId || m.Pointers.Any(ptr => ptr.ToString() == indexPointerOrId) || m.IndexPointer.ToString() == indexPointerOrId);
            if (mem == null)
            {
                return null;
            }

            var info = new CudaMemInfo
            {
                Id = mem.Id.ToString(),
                ElementType = mem.ElementType.ToString(),
                Pointers = mem.Pointers.Select(ptr => ptr.ToString()).ToArray(),
                Lengths = mem.Lengths.Select(len => len.ToString()).ToArray(),
                Message = mem.Message
            };
            return info;
        }

        public static CudaKernelInfo[] BuildCudaKernelInfos(ICudaService service, bool filterCompiled = true)
        {
            if (!service.Online || service.Compiler == null || service.Launcher == null)
            {
                return [];
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
                ArgumentNames = service.Compiler.GetArguments(CudaCompiler.GetKernelCode(kernel) ?? string.Empty).Keys.ToArray(),
                ArgumentTypes = service.Compiler.GetArguments(CudaCompiler.GetKernelCode(kernel) ?? string.Empty).Values.Select(type => type.Name).ToArray()

            }).ToArray() ?? [];

            return infos;
        }

    }
}
