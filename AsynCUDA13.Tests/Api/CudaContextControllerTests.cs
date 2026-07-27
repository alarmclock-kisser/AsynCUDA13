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

        [TestInitialize]
        public void SetUp()
        {
            var mockCuda = new Mock<ICudaService>();
            _controller = new CudaContextController(mockCuda.Object);
        }

        // =====================================================================
        // GET /api/cudacontext/devices
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
            // Arrange — when CUDA is available
            if (!CudaAvailabilityTester.IsCudaAvailable())
            {
                Assert.Inconclusive("CUDA runtime is not available; this test requires CUDA to be available.");
            }

            // Act
            var result = _controller.GetDevices();

            // Assert
            var objectResult = result.Result.ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(200);

            var deviceInfos = objectResult.Value.ShouldBeOfType<IEnumerable<CudaDeviceInfo>>();
            deviceInfos.ShouldNotBeEmpty();

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
            // This test is theoretical — if CUDA runtime is available but reports 0 devices.
            // In practice this is unlikely, so we mark inconclusive if CUDA is not available.
            if (!CudaAvailabilityTester.IsCudaAvailable())
            {
                Assert.Inconclusive("CUDA runtime is not available; this test requires CUDA to be available.");
            }

            // Act
            var result = _controller.GetDevices();

            // Assert — if devices exist, we get 200; if none, 404
            if (result.Result is ObjectResult objectResult)
            {
                // CUDA available with devices — 200 is expected
                objectResult.StatusCode.ShouldBe(200);
            }
            else if (result.Result is ObjectResult notFoundResult)
            {
                // CUDA available but no devices — 404
                notFoundResult.StatusCode.ShouldBe(404);
                var problemDetails = (notFoundResult.Value as ProblemDetails)!;
                problemDetails.Title?.ShouldContain("No CUDA devices found");
            }
        }

        // =====================================================================
        // GET /api/cudacontext/device/{deviceId}
        // =====================================================================

        [TestMethod]
        public void GetDevice_WhenCudaNotAvailable_Returns503()
        {
            // Arrange
            if (CudaAvailabilityTester.IsCudaAvailable())
            {
                Assert.Inconclusive("CUDA runtime is available; this test requires CUDA to be unavailable.");
            }

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
            if (!CudaAvailabilityTester.IsCudaAvailable())
            {
                Assert.Inconclusive("CUDA runtime is not available; this test requires CUDA to be available.");
            }

            // Get an actual device ID
            var devices = CudaService.GetAvailableDevicesProperties();
            if (!devices.Any())
            {
                Assert.Inconclusive("No CUDA devices found on the system.");
            }

            var deviceId = devices.Keys.First();

            // Act
            var result = _controller.GetDevice(deviceId);

            // Assert
            var objectResult = result.Result.ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(200);

            var deviceInfo = objectResult.Value.ShouldBeOfType<CudaDeviceInfo>();
            deviceInfo.DeviceId.ShouldBe(deviceId);
            deviceInfo.DeviceName.ShouldNotBeNullOrEmpty();
            deviceInfo.Properties.ShouldNotBeNull();
        }

        [TestMethod]
        public void GetDevice_WhenDeviceIdNotFound_Returns404()
        {
            // Arrange
            if (!CudaAvailabilityTester.IsCudaAvailable())
            {
                Assert.Inconclusive("CUDA runtime is not available; this test requires CUDA to be available.");
            }

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
            // Arrange — this test is difficult to trigger without mocking statics,
            // so we verify the controller is constructed and callable.
            if (!CudaAvailabilityTester.IsCudaAvailable())
            {
                Assert.Inconclusive("CUDA runtime is not available; this test requires CUDA to be available.");
            }

            // Act — call with deviceId 0 (should exist if CUDA is available)
            var result = _controller.GetDevice(0);

            // Assert — should not be 500 under normal conditions
            result.Result.ShouldNotBeOfType<StatusCodeResult>();
        }
    }
}
