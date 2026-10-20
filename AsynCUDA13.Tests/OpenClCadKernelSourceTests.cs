using System.Text.RegularExpressions;

namespace AsynCUDA13.Tests;

[TestClass]
public sealed class OpenClCadKernelSourceTests
{
    [TestMethod]
    [DataRow("round")]
    [DataRow("rint")]
    [DataRow("floor")]
    [DataRow("ceil")]
    [DataRow("trunc")]
    public void CadSource_DoesNotReintroduceFloatingPointRoundingIntrinsics(string intrinsic)
    {
        // Arrange
        string path = Path.Combine(AppContext.BaseDirectory, "Kernels", "cad_render_scene.cl");
        string source = File.ReadAllText(path);

        // Act
        var matches = Regex.Matches(source, $@"(?<!\w){intrinsic}\s*\(");

        // Assert
        matches.ShouldBeEmpty();
        source.ShouldContain("inline float cad_round_grid(float value)");
        source.ShouldContain("__kernel void cad_render_scene(");
    }
}
