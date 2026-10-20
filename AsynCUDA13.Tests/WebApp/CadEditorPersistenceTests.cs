namespace AsynCUDA13.Tests.WebApp;

[TestClass]
public sealed class CadEditorPersistenceTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Reload_PreservesPermissionAndNotifiesViewModel(bool allowed)
    {
        // Arrange
        var first = new CadEditorTestHost();
        first.SetAllowed(allowed);
        first.Orbit(-300);

        // Act
        var reloaded = new CadEditorTestHost(first.SavedState);

        // Assert
        reloaded.CameraOption.ShouldBe(allowed);
        reloaded.Camera.ShouldBe(first.Camera);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ExportAndImport_PreservePermissionAndNotifyViewModel(bool allowed)
    {
        // Arrange
        var original = new CadEditorTestHost();
        original.SetAllowed(allowed);
        original.Orbit(-300);
        original.Execute("cadEditor.exportScene();");
        var imported = new CadEditorTestHost();
        imported.SetAllowed(!allowed);

        // Act
        imported.Import(original.ExportedState);

        // Assert
        imported.CameraOption.ShouldBe(allowed);
        imported.SavedCamera.GetProperty("allowBelowGround").GetBoolean().ShouldBe(allowed);
        imported.Camera.ShouldBe(original.Camera);
    }

    [TestMethod]
    [DataRow("{}")] 
    [DataRow("{\"target\":[0,0,0],\"elevation\":-1.2,\"distance\":12}")]
    [DataRow("{\"target\":[0,-100,0],\"elevation\":\"invalid\",\"distance\":-3}")]
    [DataRow("{\"target\":[0,0,0],\"azimuth\":null,\"elevation\":null,\"distance\":null,\"allowBelowGround\":\"true\"}")]
    public void LoadLegacyOrInvalidCamera_DefaultsToSafeFiniteCamera(string cameraJson)
    {
        // Arrange
        string saved = "{\"scene\":[],\"camera\":" + cameraJson + "}";

        // Act
        var editor = new CadEditorTestHost(saved);

        // Assert
        editor.CameraOption.ShouldBe(false);
        editor.Camera.ShouldAllBe(value => double.IsFinite(value));
        editor.Camera[1].ShouldBeGreaterThanOrEqualTo(0.05d - 1e-9);
    }
}
