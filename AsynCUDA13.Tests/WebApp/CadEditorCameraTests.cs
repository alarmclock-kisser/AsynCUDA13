namespace AsynCUDA13.Tests.WebApp;

[TestClass]
public sealed class CadEditorCameraTests
{
    [TestMethod]
    [DataRow(-120)]
    [DataRow(-300)]
    public void Orbit_WithoutBelowGroundPermission_KeepsEyeAboveGround(int verticalDrag)
    {
        // Arrange
        var editor = new CadEditorTestHost();

        // Act
        editor.Orbit(verticalDrag);
        double[] camera = editor.Camera;

        // Assert
        camera.ShouldAllBe(value => double.IsFinite(value));
        camera[1].ShouldBeGreaterThanOrEqualTo(0.05d - 1e-9);
        camera[10].ShouldBeLessThan(0d); // Forward direction remains directed down towards the ground.
    }
}
