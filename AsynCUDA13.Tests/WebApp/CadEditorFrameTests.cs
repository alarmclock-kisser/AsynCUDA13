namespace AsynCUDA13.Tests.WebApp;

[TestClass]
public sealed class CadEditorFrameTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DisplayBackendFrame_WithDotNetStreamContract_CompletesWithoutClientDispose(bool stale)
    {
        // Arrange
        var editor = new CadEditorTestHost();

        // Act
        editor.DeliverFrame(stale);
        string? error = editor.FrameError;

        // Assert
        error.ShouldBeNull();
        editor.FrameCompleted.ShouldBeTrue();
        editor.DrawnFrames.ShouldBe(stale ? 0 : 1);
    }
}
