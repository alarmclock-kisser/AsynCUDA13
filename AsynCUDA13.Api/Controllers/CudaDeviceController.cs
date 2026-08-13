using AsynCUDA13.Api.Services.DtoBuilders;
using AsynCUDA13.Runtime;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.CudaDtos;
using Microsoft.AspNetCore.Mvc;

namespace AsynCUDA13.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CudaDeviceController : ApiControllerBase
    {
        private readonly ICudaService cuda;


        public CudaDeviceController(ICudaService cuda)
        {
            this.cuda = cuda;
        }


        [HttpGet("devices")]
        public ActionResult<IEnumerable<CudaDeviceInfo>?> GetDevices()
        {
            try
            {
                if (!this.cuda.IsCudaAvailable())
                {
                    return this.StatusCode(503, new ProblemDetails
                    {
                        Title = "CUDA not available",
                        Detail = "CUDA is not available on this system.",
                        Status = 503
                    });
                }

                var devices = this.cuda.GetAllDeviceInfos();
                if (devices.Length == 0)
                {
                    return this.StatusCode(404, new ProblemDetails
                    {
                        Title = "No CUDA devices found",
                        Detail = "No CUDA devices were found on this system.",
                        Status = 404
                    });
                }

                return this.Ok(devices);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error retrieving CUDA devices",
                    Detail = ex.Message,
                    Status = 500
                };

                return this.StatusCode(500, pd);
            }
        }

        [HttpGet("device/{deviceId}")]
        public ActionResult<CudaDeviceInfo> GetDevice(int deviceId)
        {
            if (!this.cuda.IsCudaAvailable())
            {
                var pd = new ProblemDetails
                {
                    Title = "CUDA not available",
                    Detail = "The CUDA runtime is not available. Please ensure that the CUDA runtime is properly installed.",
                    Status = 503
                };
                return this.StatusCode(503, pd);
            }
            try
            {
                var devices = this.cuda.GetAllDeviceInfos();
                var deviceInfo = devices.FirstOrDefault(d => d.DeviceId == deviceId);
                if (deviceInfo == null)
                {
                    var pd = new ProblemDetails
                    {
                        Title = "CUDA device not found",
                        Detail = $"No CUDA device with ID {deviceId} was found on this system.",
                        Status = 404
                    };
                    return this.StatusCode(404, pd);
                }

                return this.Ok(deviceInfo);
            }
            catch (Exception ex)
            {
                var pd = new ProblemDetails
                {
                    Title = "Error retrieving CUDA device",
                    Detail = ex.Message,
                    Status = 500
                };
                return this.StatusCode(500, pd);
            }
        }
    }
}
