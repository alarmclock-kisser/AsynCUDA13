using AsynCUDA13.Api.Controllers;
using AsynCUDA13.Api.Services;
using AsynCUDA13.Media;
using AsynCUDA13.Shared.Api.Requests;
using AsynCUDA13.Shared.Interfaces;
using AsynCUDA13.Shared.RuntimeDtos;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Shouldly;

namespace AsynCUDA13.Tests.Api;

[TestClass]
public sealed class RuntimeControllerOfflineTests : TestBase
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task CompileKernel_WhenRuntimeOffline_Returns503(bool asyncCall)
    {
        // Arrange
        var runtime = CreateOfflineRuntime();
        var controller = new RuntimeKernelController(runtime.Object, Mock.Of<IAssetProvider>(), this.Logger);
        var request = new RuntimeCompileRequest { KernelSource = "kernel", AsyncCall = asyncCall };

        // Act
        var result = await controller.CompileKernelAsync(request);

        // Assert
        result!.Result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(503);
    }

    [TestMethod]
    public async Task RunFourier_WhenRuntimeOffline_Returns503()
    {
        // Arrange
        var runtime = CreateOfflineRuntime();
        await using var audios = new AudioCollection(this.Logger);
        var controller = new RuntimeFourierController(runtime.Object, audios, this.Logger);
        var request = new RuntimeFourierRequest { MemoryInfo = new RuntimeMemInfo() };

        // Act
        var result = await controller.RunCudaFourierAsync(request);

        // Assert
        result.Result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(503);
    }

    [TestMethod]
    public void GetKernels_WhenCompilerIsAvailable_ReturnsCollection()
    {
        // Arrange
        var runtime = CreateOfflineRuntime();
        var compiler = new Mock<IRuntimeCompiler>();
        runtime.SetupGet(value => value.Compiler).Returns(compiler.Object);
        var controller = new RuntimeKernelController(runtime.Object, Mock.Of<IAssetProvider>(), this.Logger);

        // Act
        var result = controller.GetKernels();

        // Assert
        var response = result.Result.ShouldBeOfType<OkObjectResult>();
        response.StatusCode.ShouldBe(200);
        response.Value.ShouldBeAssignableTo<IEnumerable<object>>();
    }

    private static Mock<IRuntimeService> CreateOfflineRuntime()
    {
        var runtime = new Mock<IRuntimeService>();
        runtime.SetupGet(value => value.RuntimeType).Returns("TestRuntime");
        runtime.SetupGet(value => value.Online).Returns(false);
        runtime.SetupGet(value => value.RegisteredMemory).Returns([]);
        runtime.SetupGet(value => value.TotalAvailableDeviceProperties).Returns([]);
        return runtime;
    }
}
