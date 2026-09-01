using AsynCUDA13.Api.Services.DtoBuilders;
using AsynCUDA13.Runtime;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.Interfaces;
using AsynCUDA13.Shared.RuntimeDtos;
using Microsoft.AspNetCore.Mvc;

namespace AsynCUDA13.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RuntimeDeviceController : ApiControllerBase
    {
        public RuntimeDeviceController(IRuntimeService backend, IRollingFileMemoryLogger logger)
            : base(backend, logger)
        {

        }


        [HttpGet("devices")]
        public ActionResult<IEnumerable<RuntimeDeviceInfo>?> GetDevices()
        {
            try
            {
                if (!this.IsBackendAvailable)
                {
                    return this.StatusCode(503, new ProblemDetails
                    {
                        Title = $"{this.RuntimeType} not available",
                        Detail = $"{this.RuntimeType} is not available on this system.",
                        Status = 503
                    });
                }

                var devices = this.backend.TotalAvailableDeviceProperties;
                if (devices.Count <= 0)
                {
                    return this.StatusCode(404, new ProblemDetails
                    {
                        Title = $"No {this.RuntimeType} devices found",
                        Detail = $"No {this.RuntimeType} devices were found on this system.",
                        Status = 404
                    });
                }

                var infos = RuntimeInfosBuilder.BuildRuntimeAllDeviceInfos(this.backend);

                return this.Ok(infos);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = $"Error retrieving {this.RuntimeType} devices",
                    Detail = ex.Message,
                    Status = 500
                };

                return this.StatusCode(500, pd);
            }
        }

        [HttpGet("device/{deviceId}")]
        public ActionResult<RuntimeDeviceInfo> GetDevice(int? deviceId = null)
        {
            if (!this.IsBackendAvailable)
            {
                var pd = new ProblemDetails
                {
                    Title = $"{this.RuntimeType} not available",
                    Detail = $"The {this.RuntimeType} runtime is not available. Please ensure that the {this.RuntimeType} runtime is properly installed.",
                    Status = 503
                };
                return this.StatusCode(503, pd);
            }
            try
            {
                var devices = this.backend.TotalAvailableDeviceProperties;

                var info = RuntimeInfosBuilder.BuildRuntimeDeviceInfo(this.backend, deviceId);
                if (info == null)
                {
                    var pd = new ProblemDetails
                    {
                        Title = $"Device not found",
                        Detail = $"The specified device with ID {deviceId} was not found.",
                        Status = 404
                    };
                    return this.StatusCode(404, pd);
                }

                return this.Ok(info);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = $"Error retrieving {this.RuntimeType} device",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }
    }
}
