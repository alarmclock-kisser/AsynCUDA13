using AsynCUDA13.Api.Controllers;
using AsynCUDA13.Shared.Api.Requests;
using AsynCUDA13.Shared.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Shouldly;

namespace AsynCUDA13.Tests.Api;

[TestClass]
public sealed class RuntimeContextControllerTests : TestBase
{
    private Mock<IRuntimeService> runtime = null!;
    private RuntimeContextController controller = null!;

    [TestInitialize]
    public void SetUp()
    {
        this.runtime = new Mock<IRuntimeService>();
        this.runtime.SetupGet(value => value.RuntimeType).Returns("TestRuntime");
        this.runtime.SetupGet(value => value.TotalAvailableDeviceProperties).Returns([]);
        this.controller = new RuntimeContextController(this.runtime.Object, this.Logger);
    }

    [TestMethod]
    [DataRow("backend")]
    [DataRow("status")]
    public void ReadEndpoints_WithoutDevices_Return503(string endpoint)
    {
        // Arrange & Act
        var result = endpoint == "backend"
            ? this.controller.GetBackend().Result
            : this.controller.GetContextStatus().Result;

        // Assert
        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(503);
        response.Value.ShouldBeOfType<ProblemDetails>().Status.ShouldBe(503);
    }

    [TestMethod]
    public void GetBackend_WithAvailableDevice_ReturnsRuntimeName()
    {
        // Arrange
        this.runtime.SetupGet(value => value.TotalAvailableDeviceProperties)
            .Returns(new Dictionary<int, Dictionary<string, string>> { [0] = [] });

        // Act
        var result = this.controller.GetBackend();

        // Assert
        result.Result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe("N/A");
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(3)]
    public void InitializeContext_WhenRuntimeRejectsDevice_Returns500(int deviceId)
    {
        // Arrange
        this.runtime.SetupGet(value => value.TotalAvailableDeviceProperties)
            .Returns(new Dictionary<int, Dictionary<string, string>> { [0] = [] });
        this.runtime.Setup(value => value.Initialize(deviceId)).Returns(false);

        // Act
        var result = this.controller.InitializeContext(new RuntimeInitializeRequest { DeviceId = deviceId });

        // Assert
        result.Result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(500);
        this.runtime.Verify(value => value.Initialize(deviceId), Times.Once);
    }

    [TestMethod]
    public void InitializeContext_WhenRuntimeThrows_Returns500()
    {
        // Arrange
        this.runtime.SetupGet(value => value.TotalAvailableDeviceProperties)
            .Returns(new Dictionary<int, Dictionary<string, string>> { [0] = [] });
        this.runtime.Setup(value => value.Initialize(It.IsAny<int>())).Throws<InvalidOperationException>();

        // Act
        var result = this.controller.InitializeContext(0);

        // Assert
        result.Result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(500);
    }
}