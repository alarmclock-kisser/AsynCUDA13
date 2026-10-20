using AsynCUDA13.Api.Controllers;
using AsynCUDA13.Api.Services;
using AsynCUDA13.Shared.Api.Payloads;
using AsynCUDA13.Shared.Api.Requests;
using AsynCUDA13.Shared.Api.Responses;
using AsynCUDA13.Shared.Interfaces;
using AsynCUDA13.Shared.Serialization;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace AsynCUDA13.Tests.Api;

[TestClass]
public sealed class RuntimeMemoryControllerPushTests : TestBase
{
    private Mock<IRuntimeService> runtime = null!;
    private Mock<IRuntimeRegister> register = null!;
    private RuntimeMemoryController controller = null!;

    [TestInitialize]
    public void Initialize()
    {
        this.runtime = new Mock<IRuntimeService>();
        this.register = new Mock<IRuntimeRegister>(MockBehavior.Strict);
        this.runtime.SetupGet(value => value.Online).Returns(true);
        this.runtime.SetupGet(value => value.Register).Returns(this.register.Object);
        this.controller = new RuntimeMemoryController(this.runtime.Object, Mock.Of<IAssetProvider>(), this.Logger);
    }

    [TestMethod]
    [DataRow("Single")]
    [DataRow("System.Single")]
    [DataRow("System.Single, System.Private.CoreLib")]
    public async Task PushAsync_WithFloatPayload_UploadsAllValuesAsSingleAndReturns200(string elementType)
    {
        // Arrange
        float[] values = Enumerable.Range(-10, 20).Select(value => value * 0.5f).ToArray();
        var payload = (await DataSerializer.SerializeAsync(values)).ShouldBeOfType<SimdPayload1D>();
        payload.ElementType = elementType;
        var memory = this.CreateMemory([values.LongLength]);
        float[]? uploaded = null;
        this.register.Setup(value => value.PushDataAsync(It.IsAny<IEnumerable<float>>()))
            .Callback<IEnumerable<float>>(data => uploaded = data.ToArray()).ReturnsAsync(memory);

        // Act
        var result = await this.controller.PushAsync(new RuntimePushRequest { Payload = payload });

        // Assert
        var response = result!.Result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<RuntimePushResponse>();
        response.Success.ShouldBeTrue();
        response.MemoryInfo.ShouldNotBeNull().ElementType.ShouldBe(typeof(float).ToString());
        response.MemoryInfo.TotalLength.ShouldBe(values.Length.ToString());
        uploaded.ShouldNotBeNull().ShouldBe(values);
        this.register.Verify(value => value.PushDataAsync(It.IsAny<IEnumerable<float>>()), Times.Once);
        this.register.Invocations.ShouldHaveSingleItem().Method.GetGenericArguments().ShouldBe([typeof(float)]);
    }

    [TestMethod]
    [DataRow("Single")]
    [DataRow("System.Single")]
    public async Task PushAsync_WithFloatChunks_UploadsEveryRowWithoutBoxing(string elementType)
    {
        // Arrange
        float[][] values = [[1f, 2.5f], [-3f, 4f, 5f]];
        var payload = (await DataSerializer.SerializeAsync(values)).ShouldBeOfType<SimdPayload2D>();
        payload.ElementType = elementType;
        var memory = this.CreateMemory(values.Select(row => row.LongLength).ToArray());
        float[][]? uploaded = null;
        this.register.Setup(value => value.PushChunksAsync(It.IsAny<IEnumerable<IEnumerable<float>>>()))
            .Callback<IEnumerable<IEnumerable<float>>>(rows => uploaded = rows.Select(row => row.ToArray()).ToArray()).ReturnsAsync(memory);

        // Act
        var result = await this.controller.PushAsync(new RuntimePushRequest { Payload = payload });

        // Assert
        var response = result!.Result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<RuntimePushResponse>();
        response.Success.ShouldBeTrue();
        uploaded.ShouldNotBeNull().Length.ShouldBe(values.Length);
        uploaded.Zip(values).ShouldAllBe(pair => pair.First.SequenceEqual(pair.Second));
        this.register.Verify(value => value.PushChunksAsync(It.IsAny<IEnumerable<IEnumerable<float>>>()), Times.Once);
        this.register.Invocations.ShouldHaveSingleItem().Method.GetGenericArguments().ShouldBe([typeof(float)]);
    }

    [TestMethod]
    [DataRow("UnknownType")]
    [DataRow("Object")]
    [DataRow("String")]
    [DataRow("System.Nullable`1[System.Single]")]
    public async Task PushAsync_WithUnsupportedElementType_Returns400WithoutRegisterCall(string elementType)
    {
        // Arrange
        var payload = new SimdPayload1D { ElementType = elementType, Data = "AACAPw==" };

        // Act
        var result = await this.controller.PushAsync(new RuntimePushRequest { Payload = payload });

        // Assert
        var response = result!.Result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(400);
        var problem = response.Value.ShouldBeOfType<ProblemDetails>();
        problem.Detail.ShouldNotBeNull().ShouldContain(elementType);
        problem.Detail.ShouldContain("could not be parsed");
        this.register.Invocations.ShouldBeEmpty();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PushAsync_WhenRegisterReturnsNull_ReturnsBackendFailureInsteadOfBinderError(bool chunked)
    {
        // Arrange
        ISimdPayload payload = chunked
            ? (await DataSerializer.SerializeAsync(new float[][] { [1f, 2f] })).ShouldBeOfType<SimdPayload2D>()
            : (await DataSerializer.SerializeAsync(new float[] { 1f, 2f })).ShouldBeOfType<SimdPayload1D>();
        if (chunked)
            this.register.Setup(value => value.PushChunksAsync(It.IsAny<IEnumerable<IEnumerable<float>>>())).ReturnsAsync((IRuntimeMem?)null);
        else
            this.register.Setup(value => value.PushDataAsync(It.IsAny<IEnumerable<float>>())).ReturnsAsync((IRuntimeMem?)null);

        // Act
        var result = await this.controller.PushAsync(new RuntimePushRequest { Payload = payload });

        // Assert
        var response = result!.Result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(500);
        response.Value.ShouldBeOfType<ProblemDetails>().Detail.ShouldBe("The N/A push operation failed.");
        this.register.Invocations.ShouldHaveSingleItem().Method.GetGenericArguments().ShouldBe([typeof(float)]);
    }

    [TestMethod]
    [DataRow(false, 503)]
    [DataRow(true, 400)]
    public async Task PushAsync_WithMissingPayload_RejectsBeforeRegisterAccess(bool online, int expectedStatus)
    {
        // Arrange
        this.runtime.SetupGet(value => value.Online).Returns(online);

        // Act
        var result = await this.controller.PushAsync(new RuntimePushRequest());

        // Assert
        result!.Result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(expectedStatus);
        this.register.Invocations.ShouldBeEmpty();
    }

    private IRuntimeMem CreateMemory(long[] lengths)
    {
        var memory = new Mock<IRuntimeMem>();
        var pointer = new IntPtr(123);
        memory.SetupGet(value => value.Id).Returns(Guid.NewGuid());
        memory.SetupGet(value => value.CreatedAt).Returns(DateTime.UtcNow);
        memory.SetupGet(value => value.ElementType).Returns(typeof(float));
        memory.SetupGet(value => value.IndexPointer).Returns(pointer);
        memory.SetupGet(value => value.PointerIds).Returns(lengths.Select((_, index) => new IntPtr(123 + index)).ToArray());
        memory.SetupGet(value => value.PointerLengths).Returns(lengths);
        memory.SetupGet(value => value.AssetReferenceIds).Returns([]);
        memory.SetupGet(value => value.Message).Returns(string.Empty);
        this.runtime.Setup(value => value[It.IsAny<string>()]).Returns(memory.Object);
        return memory.Object;
    }
}
