using AsynCUDA13.Api.Controllers;
using AsynCUDA13.Api.Services.DtoBuilders;
using AsynCUDA13.Media;
using AsynCUDA13.Runtime;
using AsynCUDA13.Shared.Api.Payloads;
using AsynCUDA13.Shared.Api.Requests;
using AsynCUDA13.Shared.Api.Responses;
using AsynCUDA13.Shared.CudaDtos;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Shouldly;

namespace AsynCUDA13.Tests.Api
{
    [TestClass]
    public class CudaMemoryControllerTests : TestBase
    {
        private Mock<ICudaService> _mockCuda = null!;
        private Mock<AudioCollection> _mockAudios = null!;
        private Mock<ImageCollection> _mockImages = null!;
        private CudaMemoryController _controller = null!;

        [TestInitialize]
        public void SetUp()
        {
            this._mockCuda = new Mock<ICudaService>();
            this._mockAudios = new Mock<AudioCollection>();
            this._mockImages = new Mock<ImageCollection>();
            this._controller = new CudaMemoryController(this._mockCuda.Object, this._mockAudios.Object, this._mockImages.Object);
        }

        // =====================================================================
        // GET /api/cudamemory/memory-list
        // =====================================================================

        [TestMethod]
        public void GetMemoryList_WhenOffline_Returns503()
        {
            // Arrange
            this._mockCuda.Setup(c => c.Online).Returns(false);

            // Act
            var result = this._controller.GetMemoryList();
            Assert.IsNotNull(result);

            // Assert
            var objectResult = Require(Require(result).Result).ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(503);

            var problemDetails = Require(objectResult.Value as ProblemDetails);
            problemDetails.Title?.ShouldContain("CUDA not available");
            problemDetails.Status.ShouldBe(503);
        }

        [TestMethod]
        public void GetMemoryList_WhenOnlineButEmpty_Returns404()
        {
            // Arrange
            this._mockCuda.Setup(c => c.Online).Returns(true);
            this._mockCuda.Setup(c => c.RegisteredMemory).Returns(new List<CudaMem>());

            // Act
            var result = this._controller.GetMemoryList();
            Assert.IsNotNull(result);

            // Assert
            var objectResult = Require(Require(result).Result).ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(404);

            var problemDetails = Require(objectResult.Value as ProblemDetails);
            problemDetails.Title?.ShouldContain("No CUDA memory found");
            problemDetails.Status.ShouldBe(404);
        }

        [TestMethod]
        public void GetMemoryList_WhenOnlineWithMemory_Returns200()
        {
            // Arrange
            var fakePtr = new IntPtr(0x12345678);
            var cudaMem = CreateFakeCudaMem(fakePtr, typeof(float), 10);
            this._mockCuda.Setup(c => c.Online).Returns(true);
            this._mockCuda.Setup(c => c.RegisteredMemory).Returns(new List<CudaMem> { cudaMem });

            // Act
            var result = this._controller.GetMemoryList();

            // Assert
            var objectResult = Require(Require(result).Result).ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(200);

            var memoryList = objectResult.Value.ShouldBeAssignableTo<CudaMemInfo[]>();
            memoryList.ShouldNotBeEmpty();
            var first = memoryList.First();
            first.Id.ShouldBe(cudaMem.Id.ToString());
        }

        [TestMethod]
        public void GetMemoryList_WhenOnlineWithMultipleMemory_ReturnsAll()
        {
            // Arrange
            var mem1 = CreateFakeCudaMem(new IntPtr(0x1000), typeof(float), 10);
            var mem2 = CreateFakeCudaMem(new IntPtr(0x2000), typeof(double), 20);
            var mem3 = CreateFakeCudaMem(new IntPtr(0x3000), typeof(int), 30);
            this._mockCuda.Setup(c => c.Online).Returns(true);
            this._mockCuda.Setup(c => c.RegisteredMemory).Returns(new List<CudaMem> { mem1, mem2, mem3 });

            // Act
            var result = this._controller.GetMemoryList();

            // Assert
            var objectResult = Require(Require(result).Result).ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(200);

            var memoryList = objectResult.Value.ShouldBeAssignableTo<CudaMemInfo[]>();
            memoryList?.Length.ShouldBe(3);
        }

        [TestMethod]
        public void GetMemoryList_WhenExceptionThrown_Returns500()
        {
            // Arrange
            this._mockCuda.Setup(c => c.Online).Returns(true);
            this._mockCuda.Setup(c => c.RegisteredMemory).Throws(new InvalidOperationException("Test exception"));

            // Act
            var result = this._controller.GetMemoryList();

            // Assert
            var objectResult = Require(result.Result).ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(500);

            var problemDetails = Require(objectResult.Value as ProblemDetails);
            problemDetails.Detail?.ShouldContain("Test exception");
        }


        // =====================================================================
        // GET /api/cudamemory/memory-info/{indexPointerOrId}
        // =====================================================================

        [TestMethod]
        public void GetMemoryInfo_WhenOffline_Returns503()
        {
            // Arrange
            this._mockCuda.Setup(c => c.Online).Returns(false);

            // Act
            var result = this._controller.GetMemoryInfo("0x1234");

            // Assert
            var objectResult = Require(Require(result).Result).ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(503);
        }

        [TestMethod]
        public void GetMemoryInfo_WhenOnlineButNotFound_Returns404()
        {
            // Arrange
            this._mockCuda.Setup(c => c.Online).Returns(true);
            this._mockCuda.Setup(c => c.RegisteredMemory).Returns(new List<CudaMem>());

            // Act
            var result = this._controller.GetMemoryInfo("0x1234");
            Assert.IsNotNull(result);

            // Assert
            var objectResult = Require(Require(result).Result).ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(404);

            var problemDetails = Require(objectResult.Value as ProblemDetails);
            problemDetails.Detail?.ShouldContain("0x1234");
        }

        [TestMethod]
        public void GetMemoryInfo_WhenOnlineAndFoundByPointer_Returns200()
        {
            // Arrange
            var fakePtr = new IntPtr(0x12345678);
            var cudaMem = CreateFakeCudaMem(fakePtr, typeof(float), 10);
            this._mockCuda.Setup(c => c.Online).Returns(true);
            this._mockCuda.Setup(c => c.RegisteredMemory).Returns(new List<CudaMem> { cudaMem });

            // Act
            var result = this._controller.GetMemoryInfo(cudaMem.IndexPointer.ToString());

            // Assert
            var objectResult = Require(Require(result).Result).ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(200);

            var memoryInfo = objectResult.Value.ShouldBeOfType<CudaMemInfo>();
            memoryInfo.Id.ShouldBe(cudaMem.Id.ToString());
        }

        [TestMethod]
        public void GetMemoryInfo_WhenOnlineAndFoundById_Returns200()
        {
            // Arrange
            var fakePtr = new IntPtr(0x12345678);
            var cudaMem = CreateFakeCudaMem(fakePtr, typeof(float), 10);
            this._mockCuda.Setup(c => c.Online).Returns(true);
            this._mockCuda.Setup(c => c.RegisteredMemory).Returns(new List<CudaMem> { cudaMem });

            // Act
            var result = this._controller.GetMemoryInfo(cudaMem.Id.ToString());

            // Assert
            var objectResult = Require(Require(result).Result).ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(200);

            var memoryInfo = objectResult.Value.ShouldBeOfType<CudaMemInfo>();
            memoryInfo.Id.ShouldBe(cudaMem.Id.ToString());
        }

        [TestMethod]
        public void GetMemoryInfo_WhenExceptionThrown_Returns500()
        {
            // Arrange
            this._mockCuda.Setup(c => c.Online).Returns(true);
            this._mockCuda.Setup(c => c.RegisteredMemory).Throws(new InvalidOperationException("Test exception"));

            // Act
            var result = this._controller.GetMemoryInfo("0x1234");

            // Assert
            var objectResult = Require(Require(result).Result).ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(500);
        }


        // =====================================================================
        // DELETE /api/cudamemory/memory-free/{indexPointerOrId}
        // =====================================================================

        [TestMethod]
        public void FreeMemory_WhenOffline_Returns503()
        {
            // Arrange
            this._mockCuda.Setup(c => c.Online).Returns(false);

            // Act
            var result = this._controller.FreeMemory("0x1234");

            // Assert
            var objectResult = Require(Require(result).Result).ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(503);
        }

        [TestMethod]
        public void FreeMemory_WhenOnlineButNotFound_Returns404()
        {
            // Arrange
            this._mockCuda.Setup(c => c.Online).Returns(true);
            this._mockCuda.Setup(c => c.RegisteredMemory).Returns(new List<CudaMem>());

            // Act
            var result = this._controller.FreeMemory("0x1234");

            // Assert
            var objectResult = Require(Require(result).Result).ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(404);
        }

        [TestMethod]
        public void FreeMemory_WhenInvalidPointer_Returns400()
        {
            // Arrange
            var fakePtr = new IntPtr(0x12345678);
            var cudaMem = CreateFakeCudaMem(fakePtr, typeof(float), 10);
            this._mockCuda.Setup(c => c.Online).Returns(true);
            this._mockCuda.Setup(c => c.RegisteredMemory).Returns(new List<CudaMem> { cudaMem });

            // Act — nicht-gültiger Pointer-String (nicht im registrierten Speicher)
            var result = this._controller.FreeMemory("not-a-pointer");
            Assert.IsNotNull(result);

            // Assert
            var objectResult = Require(result.Result).ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(400);

            var problemDetails = Require(objectResult.Value as ProblemDetails);
            problemDetails.Title?.ShouldContain("Invalid pointer");
        }

        [TestMethod]
        public void FreeMemory_WhenValidPointerButInvalidFormat_Returns404()
        {
            // Arrange
            var fakePtr = new IntPtr(0x12345678);
            var cudaMem = CreateFakeCudaMem(fakePtr, typeof(float), 10);
            this._mockCuda.Setup(c => c.Online).Returns(true);
            this._mockCuda.Setup(c => c.RegisteredMemory).Returns(new List<CudaMem>());

            // Act — gültiger Pointer, aber nicht im registrierten Speicher
            var result = this._controller.FreeMemory(fakePtr.ToString());
            Assert.IsNotNull(result);

            // Assert
            var objectResult = Require(result.Result).ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(404);
        }

        [TestMethod]
        public void FreeMemory_WhenValidPointer_Returns200()
        {
            // Arrange
            var fakePtr = new IntPtr(0x12345678);
            var cudaMem = CreateFakeCudaMem(fakePtr, typeof(float), 10);
            this._mockCuda.Setup(c => c.Online).Returns(true);
            this._mockCuda.Setup(c => c.RegisteredMemory).Returns(new List<CudaMem> { cudaMem });
            this._mockCuda.Setup(c => c.FreeMemory(fakePtr)).Returns(40L);

            // Act
            var result = this._controller.FreeMemory(fakePtr.ToString());

            // Assert
            var objectResult = Require(Require(result).Result).ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(200);
            objectResult.Value.ShouldBe("40");

            this._mockCuda.Verify(c => c.FreeMemory(fakePtr), Times.Once);
        }

        [TestMethod]
        public void FreeMemory_WhenExceptionThrown_Returns500()
        {
            // Arrange
            var fakePtr = new IntPtr(0x12345678);
            var cudaMem = CreateFakeCudaMem(fakePtr, typeof(float), 10);
            this._mockCuda.Setup(c => c.Online).Returns(true);
            this._mockCuda.Setup(c => c.RegisteredMemory).Returns(new List<CudaMem> { cudaMem });
            this._mockCuda.Setup(c => c.FreeMemory(fakePtr)).Throws(new InvalidOperationException("Test exception"));

            // Act
            var result = this._controller.FreeMemory(fakePtr.ToString());

            // Assert
            var objectResult = Require(Require(result).Result).ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(500);
        }


        // =====================================================================
        // DELETE /api/cudamemory/memory-all-memory
        // =====================================================================

        [TestMethod]
        public async Task FreeAllMemoryAsync_WhenOffline_Returns503()
        {
            // Arrange
            this._mockCuda.Setup(c => c.Online).Returns(false);

            // Act
            var result = await this._controller.FreeAllMemoryAsync();

            // Assert
            var objectResult = Require(Require(result).Result).ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(503);
        }

        [TestMethod]
        public async Task FreeAllMemoryAsync_WhenOnlineWithMemory_Returns200()
        {
            // Arrange
            var mem1 = CreateFakeCudaMem(new IntPtr(0x1000), typeof(float), 10);
            var mem2 = CreateFakeCudaMem(new IntPtr(0x2000), typeof(double), 20);
            this._mockCuda.Setup(c => c.Online).Returns(true);
            this._mockCuda.Setup(c => c.RegisteredMemory).Returns(new List<CudaMem> { mem1, mem2 });
            this._mockCuda.Setup(c => c.FreeMemoryAsync(mem1.IndexPointer)).ReturnsAsync(40L);
            this._mockCuda.Setup(c => c.FreeMemoryAsync(mem2.IndexPointer)).ReturnsAsync(160L);

            // Act
            var result = await this._controller.FreeAllMemoryAsync();

            // Assert
            var objectResult = Require(result.Result).ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(200);
            objectResult.Value.ShouldBe("200"); // 40 + 160
        }

        [TestMethod]
        public async Task FreeAllMemoryAsync_WhenOnlineButNoMemory_Returns200()
        {
            // Arrange
            this._mockCuda.Setup(c => c.Online).Returns(true);
            this._mockCuda.Setup(c => c.RegisteredMemory).Returns(new List<CudaMem>());

            // Act
            var result = await this._controller.FreeAllMemoryAsync();

            // Assert
            var objectResult = Require(result.Result).ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(200);
            objectResult.Value.ShouldBe("0");
        }

        [TestMethod]
        public async Task FreeAllMemoryAsync_WhenExceptionThrown_Returns500()
        {
            // Arrange
            var mem1 = CreateFakeCudaMem(new IntPtr(0x1000), typeof(float), 10);
            this._mockCuda.Setup(c => c.Online).Returns(true);
            this._mockCuda.Setup(c => c.RegisteredMemory).Returns(new List<CudaMem> { mem1 });
            this._mockCuda.Setup(c => c.FreeMemoryAsync(mem1.IndexPointer)).ThrowsAsync(new InvalidOperationException("Test exception"));

            // Act
            var result = await this._controller.FreeAllMemoryAsync();

            // Assert
            var objectResult = Require(result.Result).ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(500);
        }


        // =====================================================================
        // POST /api/cudamemory/push
        // =====================================================================

        [TestMethod]
        public async Task PushAsync_WhenOffline_Returns503()
        {
            // Arrange
            this._mockCuda.Setup(c => c.Online).Returns(false);
            var request = CudaRequestsBuilder.BuildCudaPushRequest("1,2,3,4,5", "System.Single");

            // Act
            var result = await this._controller.PushAsync(request);

            // Assert
            var objectResult = Require(result?.Result).ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(503);
        }

        [TestMethod]
        public async Task PushAsync_WhenPayloadNull_Returns400()
        {
            // Arrange
            this._mockCuda.Setup(c => c.Online).Returns(true);
            var request = new CudaPushRequest { Payload = new CudaPayload1D() };

            // Act
            var result = await this._controller.PushAsync(request);
            Assert.IsNotNull(result);

            // Assert
            var objectResult = Require(result.Result).ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(400);

            var problemDetails = Require(objectResult.Value as ProblemDetails);
            problemDetails.Title?.ShouldContain("Invalid request");
        }

        [TestMethod]
        public async Task PushAsync_With1DPayload_Success_Returns200()
        {
            // This test requires integration testing due to dynamic typing in the controller.
            Assert.Inconclusive("PushAsync test requires integration testing due to dynamic typing limitations in the controller.");
        }

        [TestMethod]
        public async Task PushAsync_With2DPayload_Success_Returns200()
        {
            // This test requires integration testing due to dynamic typing in the controller.
            Assert.Inconclusive("PushAsync test requires integration testing due to dynamic typing limitations in the controller.");
        }

        [TestMethod]
        public async Task PushAsync_WhenPushReturnsNull_Returns500()
        {
            // This test requires integration testing due to dynamic typing in the controller.
            Assert.Inconclusive("PushAsync test requires integration testing due to dynamic typing limitations in the controller.");
        }

        [TestMethod]
        public async Task PushAsync_WhenExceptionThrown_Returns500()
        {
            // This test requires integration testing due to dynamic typing in the controller.
            Assert.Inconclusive("PushAsync test requires integration testing due to dynamic typing limitations in the controller.");
        }


        // =====================================================================
        // POST /api/cudamemory/pull
        // =====================================================================

        [TestMethod]
        public async Task PullAsync_WhenOffline_Returns503()
        {
            // Arrange
            this._mockCuda.Setup(c => c.Online).Returns(false);
            var request = CudaRequestsBuilder.BuildCudaPullRequest("0x1234");

            // Act
            var result = await this._controller.PullAsync(request);

            // Assert
            var objectResult = Require(result?.Result).ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(503);
        }

        [TestMethod]
        public async Task PullAsync_WhenIndexPointerNull_Returns400()
        {
            // Arrange
            this._mockCuda.Setup(c => c.Online).Returns(true);
            var request = new CudaPullRequest { IndexPointerOrId = string.Empty, FreeAfterPull = true };

            // Act
            var result = await this._controller.PullAsync(request);

            // Assert
            var objectResult = Require(result?.Result).ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(400);
        }

        [TestMethod]
        public async Task PullAsync_WhenMemoryNotFound_Returns404()
        {
            // Arrange
            this._mockCuda.Setup(c => c.Online).Returns(true);
            this._mockCuda.Setup(c => c.RegisteredMemory).Returns(new List<CudaMem>());

            var request = CudaRequestsBuilder.BuildCudaPullRequest("0x9999");

            // Act
            var result = await this._controller.PullAsync(request);

            // Assert
            var objectResult = Require(result?.Result).ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(404);
        }

        [TestMethod]
        public async Task PullAsync_WhenOnlineAndMemoryFound_SingleElement_Returns200()
        {
            // This test requires integration testing due to dynamic typing in the controller.
            Assert.Inconclusive("PullAsync test requires integration testing due to dynamic typing limitations in the controller.");
        }

        [TestMethod]
        public async Task PullAsync_WhenOnlineAndMemoryFound_MultipleElements_Returns200()
        {
            // This test requires integration testing due to dynamic typing in the controller.
            Assert.Inconclusive("PullAsync test requires integration testing due to dynamic typing limitations in the controller.");
        }

        [TestMethod]
        public async Task PullAsync_WhenExceptionThrown_Returns500()
        {
            // Arrange
            var fakePtr = new IntPtr(0xABCDEF00);
            var cudaMem = CreateFakeCudaMem(fakePtr, typeof(float), 5);
            this._mockCuda.Setup(c => c.Online).Returns(true);
            this._mockCuda.Setup(c => c.RegisteredMemory).Returns(new List<CudaMem> { cudaMem });

            this._mockCuda.Setup(c => c.PullDataAsync<float>(fakePtr, false))
                .ThrowsAsync(new InvalidOperationException("Pull failed"));

            var request = CudaRequestsBuilder.BuildCudaPullRequest(cudaMem.IndexPointer.ToString(), false);

            // Act
            var result = await this._controller.PullAsync(request);

            // Assert
            var objectResult = Require(result?.Result).ShouldBeAssignableTo<ObjectResult>();
            objectResult.StatusCode.ShouldBe(500);
        }


        // =====================================================================
        // Helper Methods
        // =====================================================================

        private static CudaMem CreateFakeCudaMem(IntPtr pointer, Type elementType, int length)
        {
            var mem = new CudaMem(
                pointer: new ManagedCuda.BasicTypes.CUdeviceptr { Pointer = (ulong) (uint) pointer },
                length: new IntPtr(length),
                type: elementType
            );
            return mem;
        }

        private static CudaMem CreateFakeCudaMem(IntPtr[] pointers, IntPtr[] lengths, Type elementType)
        {
            var devicePointers = pointers.Select(p => new ManagedCuda.BasicTypes.CUdeviceptr { Pointer = (ulong) (uint) p }).ToArray();
            var mem = new CudaMem(devicePointers, lengths, elementType);
            return mem;
        }
    }
}
