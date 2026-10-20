using Microsoft.AspNetCore.Components;

namespace AsynCUDA13.Tests.WebApp;

[TestClass]
public sealed class CadViewModelCameraTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CheckboxChanged_InvokesJavaScriptAndUpdatesProperty(bool allowed)
    {
        // Arrange
        await using var context = new CadViewModelTestContext();

        // Act
        await context.ViewModel.OnAllowCameraBelowGroundChangedAsync(new ChangeEventArgs { Value = allowed });

        // Assert
        context.ViewModel.AllowCameraBelowGround.ShouldBe(allowed);
        var invocation = context.JavaScript.Invocations.ShouldHaveSingleItem();
        invocation.Arguments[0].ShouldBe("cadEditor.setAllowBelowGround");
        var actualArguments = invocation.Arguments[1].ShouldBeOfType<object[]>();
        actualArguments.ShouldHaveSingleItem().ShouldBe(allowed);
    }

    [TestMethod]
    [DataRow(false, 0)]
    [DataRow(true, 1)]
    public async Task JavaScriptCallback_SynchronizesPropertyWithoutEcho(bool allowed, int expectedNotifications)
    {
        // Arrange
        await using var context = new CadViewModelTestContext();
        int notifications = 0;
        context.ViewModel.StateChanged += () => notifications++;

        // Act
        context.ViewModel.OnCameraBelowGroundChanged(allowed);
        context.ViewModel.OnCameraBelowGroundChanged(allowed);

        // Assert
        context.ViewModel.AllowCameraBelowGround.ShouldBe(allowed);
        notifications.ShouldBe(expectedNotifications);
        context.JavaScript.Invocations.ShouldBeEmpty();
    }

    [TestMethod]
    public async Task JavaScriptCallback_AfterDisposal_DoesNotUpdateState()
    {
        // Arrange
        await using var context = new CadViewModelTestContext();
        await context.ViewModel.DisposeAsync();

        // Act
        context.ViewModel.OnCameraBelowGroundChanged(true);

        // Assert
        context.ViewModel.AllowCameraBelowGround.ShouldBeFalse();
        context.JavaScript.Invocations.ShouldBeEmpty();
    }
}
