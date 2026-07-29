using AsynCUDA13.Api.Controllers;
using AsynCUDA13.Runtime;
using AsynCUDA13.Shared;
using AsynCUDA13.Shared.CudaDtos;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Shouldly;

namespace AsynCUDA13.Tests.Api
{
    [TestClass]
    public class CudaContextControllerTests : TestBase
    {
        private CudaContextController _controller = null!;
        private Mock<ICudaService> _mockCuda = null!;

        [TestInitialize]
        public void SetUp()
        {
            _mockCuda = new Mock<ICudaService>();
            _controller = new CudaContextController(_mockCuda.Object);
        }

        // =====================================================================
        // GET /api/cudacontext/devices
        // =====================================================================

        [TestMethod]
        public void GetDevices_WhenCudaNotAvailable_Returns503()
        {
            // Arrange — when CUDA is not available on the system
            _mockCuda.Setup(c => c.IsCudaAvailable()).Returns(false);

            // Act
            var result = _controller.GetDevices();
            Assert.IsNotNull(result);

            // Assert
            var objectResult = result.Result.ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(503);

            var problemDetails = (objectResult.Value as ProblemDetails)!;
            problemDetails.Title?.ShouldContain("CUDA not available");
            problemDetails.Status.ShouldBe(503);
        }

        [TestMethod]
        public void GetDevices_WhenCudaAvailable_Returns200WithDevices()
        {
            // Arrange — mock CUDA as available
            _mockCuda.Setup(c => c.IsCudaAvailable()).Returns(true);

            var mockDeviceInfos = new CudaDeviceInfo[]
            {
                new CudaDeviceInfo
                {
                    DeviceId = 0,
                    DeviceName = "Mock CUDA Device 0",
                    Properties = new Dictionary<string, string>
                    {
                        { "DeviceName", "Mock CUDA Device 0" },
                        { "TotalGlobalMemory", "8589934592" }
                    }
                }
            };

            _mockCuda.Setup(c => c.GetAllDeviceInfos()).Returns(mockDeviceInfos);

            // Act
            var result = _controller.GetDevices();

            // Assert
            var objectResult = result.Result.ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(200);

            var deviceInfos = objectResult.Value.ShouldBeOfType<CudaDeviceInfo[]>();
            deviceInfos.ShouldNotBeEmpty();
            deviceInfos.Length.ShouldBe(1);

            foreach (var info in deviceInfos)
            {
                info.DeviceId.ShouldNotBeNull();
                info.DeviceName.ShouldNotBeNullOrEmpty();
                info.Properties.ShouldNotBeNull();
            }
        }

        [TestMethod]
        public void GetDevices_WhenCudaAvailableButNoDevices_Returns404()
        {
            // Arrange — mock CUDA as available but return empty array
            _mockCuda.Setup(c => c.IsCudaAvailable()).Returns(true);
            _mockCuda.Setup(c => c.GetAllDeviceInfos()).Returns([]);

            // Act
            var result = _controller.GetDevices();

            // Assert
            var objectResult = result.Result.ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(404);

            var problemDetails = (objectResult.Value as ProblemDetails)!;
            problemDetails.Title?.ShouldContain("No CUDA devices found");
        }

        // =====================================================================
        // GET /api/cudacontext/device/{deviceId}
        // =====================================================================

        [TestMethod]
        public void GetDevice_WhenCudaNotAvailable_Returns503()
        {
            // Arrange
            _mockCuda.Setup(c => c.IsCudaAvailable()).Returns(false);

            // Act
            var result = _controller.GetDevice(0);

            // Assert
            var objectResult = result.Result.ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(503);

            var problemDetails = (objectResult.Value as ProblemDetails)!;
            problemDetails.Title?.ShouldContain("CUDA not available");
            problemDetails.Status.ShouldBe(503);
        }

        [TestMethod]
        public void GetDevice_WhenCudaAvailable_Returns200()
        {
            // Arrange
            _mockCuda.Setup(c => c.IsCudaAvailable()).Returns(true);

            var mockDeviceInfos = new CudaDeviceInfo[]
            {
                new CudaDeviceInfo
                {
                    DeviceId = 0,
                    DeviceName = "Mock CUDA Device 0",
                    Properties = new Dictionary<string, string>()
                }
            };

            _mockCuda.Setup(c => c.GetAllDeviceInfos()).Returns(mockDeviceInfos);

            // Act
            var result = _controller.GetDevice(0);

            // Assert
            var objectResult = result.Result.ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(200);

            var deviceInfo = objectResult.Value.ShouldBeOfType<CudaDeviceInfo>();
            deviceInfo.DeviceId.ShouldBe(0);
            deviceInfo.DeviceName.ShouldNotBeNullOrEmpty();
            deviceInfo.Properties.ShouldNotBeNull();
        }

        [TestMethod]
        public void GetDevice_WhenDeviceIdNotFound_Returns404()
        {
            // Arrange
            _mockCuda.Setup(c => c.IsCudaAvailable()).Returns(true);

            var mockDeviceInfos = new CudaDeviceInfo[]
            {
                new CudaDeviceInfo
                {
                    DeviceId = 0,
                    DeviceName = "Device 0",
                    Properties = new Dictionary<string, string>()
                }
            };

            _mockCuda.Setup(c => c.GetAllDeviceInfos()).Returns(mockDeviceInfos);

            // Use a device ID that doesn't exist
            var nonExistentDeviceId = int.MaxValue;

            // Act
            var result = _controller.GetDevice(nonExistentDeviceId);

            // Assert
            var objectResult = result.Result.ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(404);

            var problemDetails = (objectResult.Value as ProblemDetails)!;
            problemDetails.Title?.ShouldContain("CUDA device not found");
            problemDetails.Status.ShouldBe(404);
        }

        [TestMethod]
        public void GetDevice_WhenExceptionThrown_Returns500()
        {
            // Arrange — mock CUDA as available and throws exception
            _mockCuda.Setup(c => c.IsCudaAvailable()).Returns(true);
            _mockCuda.Setup(c => c.GetAllDeviceInfos()).Throws(new InvalidOperationException("Test exception"));

            // Act
            var result = _controller.GetDevice(0);

            // Assert
            var objectResult = result.Result.ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(500);

            var problemDetails = (objectResult.Value as ProblemDetails)!;
            problemDetails.Detail.ShouldContain("Test exception");
        }
    }
}
