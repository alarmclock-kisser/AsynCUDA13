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
    public class CudaDeviceControllerTests : TestBase
    {
        private Mock<ICudaService> _mockCuda = null!;
        private CudaDeviceController _controller = null!;

        [TestInitialize]
        public void SetUp()
        {
            _mockCuda = new Mock<ICudaService>();
            _controller = new CudaDeviceController(_mockCuda.Object);
        }

        // =====================================================================
        // GET /api/cudadevice/devices
        // =====================================================================

        [TestMethod]
        public void GetDevices_WhenCudaNotAvailable_Returns503()
        {
            // Arrange — when CUDA is not available on the system
            if (CudaAvailabilityTester.IsCudaAvailable())
            {
                Assert.Inconclusive("CUDA runtime is available; this test requires CUDA to be unavailable.");
            }

            // Act
            var result = _controller.GetDevices();

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

        [TestMethod]
        public void GetDevices_ReturnsValidDeviceInfoStructure()
        {
            // Arrange — mock CUDA as available
            _mockCuda.Setup(c => c.IsCudaAvailable()).Returns(true);

            var mockDeviceInfos = new CudaDeviceInfo[]
            {
                new CudaDeviceInfo
                {
                    DeviceId = 0,
                    DeviceName = "Test Device",
                    Properties = new Dictionary<string, string>
                    {
                        { "DeviceName", "Test Device" },
                        { "TotalGlobalMemory", "4294967296" }
                    }
                }
            };

            _mockCuda.Setup(c => c.GetAllDeviceInfos()).Returns(mockDeviceInfos);

            // Act
            var result = _controller.GetDevices();

            // Assert
            var objectResult = Require(result.Result).ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(200);

            var deviceInfos = objectResult.Value.ShouldBeOfType<CudaDeviceInfo[]>();

            // Verify the DTO structure is properly populated
            var firstDevice = deviceInfos.First();
            firstDevice.DeviceId.ShouldNotBeNull();
            firstDevice.DeviceName.ShouldNotBe("N/A");
            firstDevice.Properties.ShouldNotBeNull();
            firstDevice.Properties.Keys.ShouldNotBeEmpty();

            // Verify properties contain expected device property fields
            firstDevice?.Properties?.Keys.ShouldContain("DeviceName");
        }

        [TestMethod]
        public void GetDevices_MultipleDevices_ReturnsAll()
        {
            // Arrange — mock CUDA as available
            _mockCuda.Setup(c => c.IsCudaAvailable()).Returns(true);

            var mockDeviceInfos = new CudaDeviceInfo[]
            {
                new CudaDeviceInfo { DeviceId = 0, DeviceName = "Device 0", Properties = new Dictionary<string, string>() },
                new CudaDeviceInfo { DeviceId = 1, DeviceName = "Device 1", Properties = new Dictionary<string, string>() }
            };

            _mockCuda.Setup(c => c.GetAllDeviceInfos()).Returns(mockDeviceInfos);

            // Act
            var result = _controller.GetDevices();

            // Assert
            var objectResult = result.Result.ShouldBeAssignableTo<ObjectResult>();
            objectResult?.StatusCode.ShouldBe(200);

            var deviceInfos = objectResult?.Value.ShouldBeOfType<CudaDeviceInfo[]>();
            deviceInfos?.Length.ShouldBe(2);
        }

        [TestMethod]
        public void GetDevices_DeviceIdsAreUnique()
        {
            // Arrange — mock CUDA as available
            _mockCuda.Setup(c => c.IsCudaAvailable()).Returns(true);

            var mockDeviceInfos = new CudaDeviceInfo[]
            {
                new CudaDeviceInfo { DeviceId = 0, DeviceName = "Device 0", Properties = new Dictionary<string, string>() },
                new CudaDeviceInfo { DeviceId = 1, DeviceName = "Device 1", Properties = new Dictionary<string, string>() },
                new CudaDeviceInfo { DeviceId = 2, DeviceName = "Device 2", Properties = new Dictionary<string, string>() }
            };

            _mockCuda.Setup(c => c.GetAllDeviceInfos()).Returns(mockDeviceInfos);

            // Act
            var result = _controller.GetDevices();

            // Assert
            var objectResult = result.Result.ShouldBeAssignableTo<ObjectResult>();
            var deviceInfos = objectResult?.Value.ShouldBeOfType<CudaDeviceInfo[]>();

            var deviceIds = deviceInfos?.Select(d => d.DeviceId).ToList();
            Assert.IsTrue(deviceIds?.Distinct().Count() == deviceIds?.Count, "Device IDs should be unique.");
        }

        [TestMethod]
        public void GetDevices_WhenExceptionThrown_Returns500()
        {
            // Arrange — mock CUDA as available and throws exception
            _mockCuda.Setup(c => c.IsCudaAvailable()).Returns(true);
            _mockCuda.Setup(c => c.GetAllDeviceInfos()).Throws(new InvalidOperationException("Test exception"));

            // Act
            var result = _controller.GetDevices();

            // Assert
            var objectResult = result.Result.ShouldBeAssignableTo<ObjectResult>();
            objectResult?.StatusCode.ShouldBe(500);

            var problemDetails = (objectResult?.Value as ProblemDetails)!;
            problemDetails.Detail?.ShouldContain("Test exception");
        }
    }
}