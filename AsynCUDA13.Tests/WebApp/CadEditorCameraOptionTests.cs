namespace AsynCUDA13.Tests.WebApp;

[TestClass]
public sealed class CadEditorCameraOptionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Orbit_WithSelectedPermission_PersistsAndHonorsOption(bool allowed)
    {
        // Arrange
        var editor = new CadEditorTestHost();
        editor.SetAllowed(allowed);

        // Act
        editor.Orbit(-300);
        var saved = editor.SavedCamera;

        // Assert
        saved.GetProperty("allowBelowGround").GetBoolean().ShouldBe(allowed);
        editor.Camera.ShouldAllBe(value => double.IsFinite(value));
        (editor.Camera[1] < 0).ShouldBe(allowed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TogglePermission_WhileBelowGround_CorrectsOnlyWhenDisabled(bool allowed)
    {
        // Arrange
        var editor = new CadEditorTestHost();
        editor.SetAllowed(true);
        editor.Orbit(-300);
        editor.Camera[1].ShouldBeLessThan(0d);

        // Act
        editor.SetAllowed(allowed);

        // Assert
        (editor.Camera[1] < 0).ShouldBe(allowed);
        editor.SavedCamera.GetProperty("allowBelowGround").GetBoolean().ShouldBe(allowed);
    }

    [TestMethod]
    [DataRow(-500, -5000)]
    [DataRow(-1500, 5000)]
    [DataRow(500, -5000)]
    public void PanAndZoom_WhenPermissionDisabled_KeepActualEyeAboveWorldFloor(int pan, int zoom)
    {
        // Arrange
        var editor = new CadEditorTestHost();

        // Act
        editor.Pan(pan);
        double pannedEyeY = editor.Camera[1];
        editor.Zoom(zoom);
        double[] camera = editor.Camera;

        // Assert
        pannedEyeY.ShouldBeGreaterThanOrEqualTo(0.05d - 1e-9);
        camera.ShouldAllBe(value => double.IsFinite(value));
        camera[1].ShouldBeGreaterThanOrEqualTo(0.05d - 1e-9);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ResetView_PreservesSelectedPermission(bool allowed)
    {
        // Arrange
        var editor = new CadEditorTestHost();
        editor.SetAllowed(allowed);
        editor.Orbit(-300);

        // Act
        editor.Execute("cadEditor.resetView(); test.flushRender();");

        // Assert
        editor.SavedCamera.GetProperty("allowBelowGround").GetBoolean().ShouldBe(allowed);
        editor.SavedCamera.GetProperty("elevation").GetDouble().ShouldBe(0.62d);
        editor.Camera[1].ShouldBeGreaterThan(0d);
    }
}
