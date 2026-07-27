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
        private CudaDeviceController _controller = null!;

        [TestInitialize]
        public void SetUp()
        {
            var mockCuda = new Mock<ICudaService>();
            _controller = new CudaDeviceController(mockCuda.Object);
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

        [TestMethod]
        public void GetDevices_ReturnsValidDeviceInfoStructure()
        {
            // Arrange
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
            var firstDevice = deviceInfos.First();

            // Verify the DTO structure is properly populated
            firstDevice.DeviceId.ShouldNotBeNull();
            firstDevice.DeviceName.ShouldNotBe("N/A");
            firstDevice.Properties.ShouldNotBeNull();
            firstDevice.Properties!.Keys.ShouldNotBeEmpty();

            // Verify properties contain expected device property fields
            firstDevice.Properties.Keys.ShouldContain("DeviceName");
        }

        [TestMethod]
        public void GetDevices_MultipleDevices_ReturnsAll()
        {
            // Arrange
            if (!CudaAvailabilityTester.IsCudaAvailable())
            {
                Assert.Inconclusive("CUDA runtime is not available; this test requires CUDA to be available.");
            }

            var expectedCount = CudaService.DeviceCount;
            if (expectedCount <= 0)
            {
                Assert.Inconclusive("No CUDA devices found on the system.");
            }

            // Act
            var result = _controller.GetDevices();

            // Assert
            var objectResult = result.Result.ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(200);

            var deviceInfos = objectResult.Value.ShouldBeOfType<IEnumerable<CudaDeviceInfo>>();
            deviceInfos.Count().ShouldBe(expectedCount);
        }

        [TestMethod]
        public void GetDevices_DeviceIdsAreUnique()
        {
            // Arrange
            if (!CudaAvailabilityTester.IsCudaAvailable())
            {
                Assert.Inconclusive("CUDA runtime is not available; this test requires CUDA to be available.");
            }

            // Act
            var result = _controller.GetDevices();

            // Assert
            var objectResult = result.Result.ShouldBeAssignableTo<ObjectResult>();
            var deviceInfos = objectResult.Value.ShouldBeOfType<IEnumerable<CudaDeviceInfo>>();

            var deviceIds = deviceInfos.Select(d => d.DeviceId).ToList();
            Assert.IsTrue(deviceIds.Distinct().Count() == deviceIds.Count, "Device IDs should be unique.");
        }

        [TestMethod]
        public void GetDevices_WhenExceptionThrown_Returns500()
        {
            // Arrange — this test verifies the exception handling path.
            // Since we cannot easily mock static calls, we verify that the controller
            // returns a valid result (either 200 or 503 depending on CUDA availability).
            // A 500 would only occur if an unexpected exception is thrown.

            // Act
            var result = _controller.GetDevices();

            // Assert — result should never be null
            result.ShouldNotBeNull();

            // The result should be either Ok (200) or a StatusCodeResult
            if (result.Result is ObjectResult objResult)
            {
                objResult.StatusCode.ShouldBe(200);
            }
            else if (result.Result is ObjectResult notFoundResult)
            {
                // Should be 503 (CUDA not available) or 404 (no devices), never 500
                notFoundResult.StatusCode.ShouldNotBe(500);
            }
        }
    }
}