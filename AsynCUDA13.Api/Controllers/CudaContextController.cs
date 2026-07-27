using AsynCUDA13.Runtime;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.CudaDtos;
using Microsoft.AspNetCore.Mvc;

namespace AsynCUDA13.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CudaContextController : ControllerBase
    {
        private readonly ICudaService cuda;


        public CudaContextController(ICudaService cuda)
        {
            this.cuda = cuda;
        }


        [HttpGet("devices")]
        public ActionResult<IEnumerable<CudaDeviceInfo>> GetDevices()
        {
            if (CudaAvailabilityTester.IsCudaAvailable() == false)
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
                // Dict<int, CudaDeviceProperties> CudaService.GetAvailableDeviceProperties(), where CudaDeviceProperties contains PropertyFields (whomst with reflection their .Name.ToString()) with values (which will be ToString()), this in info-DTOs Dict<string, string> Properties. DeviceId in DTO is the key of the Dict<int, CudaDeviceProperties>. DeviceName in DTO is CudaDeviceProperties.DeviceName.ToString().
                var deviceInfos = CudaService.GetAvailableDevicesProperties()
                    .Select(kvp => new CudaDeviceInfo
                    {
                        DeviceId = kvp.Key,
                        DeviceName = kvp.Value.DeviceName.ToString(),
                        Properties = kvp.Value.GetType()
                            .GetProperties()
                            .ToDictionary(
                                prop => prop.Name,
                                prop => prop.GetValue(kvp.Value)?.ToString() ?? string.Empty)
                    });

                if (!deviceInfos.Any())
                {
                    var pd = new ProblemDetails
                    {
                        Title = "No CUDA devices found",
                        Detail = "No CUDA devices were found on this system.",
                        Status = 404
                    };
                    return this.StatusCode(404, pd);
                }

                return this.Ok(deviceInfos);
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
            if (CudaAvailabilityTester.IsCudaAvailable() == false)
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
                var deviceProperties = CudaService.GetAvailableDevicesProperties();
                if (!deviceProperties.ContainsKey(deviceId))
                {
                    var pd = new ProblemDetails
                    {
                        Title = "CUDA device not found",
                        Detail = $"No CUDA device with ID {deviceId} was found on this system.",
                        Status = 404
                    };
                    return this.StatusCode(404, pd);
                }

                var deviceInfo = new CudaDeviceInfo
                {
                    DeviceId = deviceId,
                    DeviceName = deviceProperties[deviceId].DeviceName.ToString(),
                    Properties = deviceProperties[deviceId].GetType()
                        .GetProperties()
                        .ToDictionary(
                            prop => prop.Name,
                            prop => prop.GetValue(deviceProperties[deviceId])?.ToString() ?? string.Empty)
                };
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
