using System.Text.Json;
using Jint;

namespace AsynCUDA13.Tests.WebApp;

internal sealed class CadEditorTestHost
{
    private readonly Engine engine = new(options => options.TimeoutInterval(TimeSpan.FromSeconds(5)));

    internal CadEditorTestHost(string? savedState = null)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "WebApp");
        this.engine.Execute(File.ReadAllText(Path.Combine(directory, "CadEditorTestHost.js")));
        this.engine.SetValue("savedState", savedState);
        this.engine.Execute("test.storage = savedState;");
        this.engine.Execute(File.ReadAllText(Path.Combine(directory, "cad-editor.js")));
        this.Execute("test.initialize();");
    }

    internal double[] Camera => JsonSerializer.Deserialize<double[]>(this.engine.Evaluate("JSON.stringify(test.packet.camera)").AsString())!;
    internal string? FrameError => this.engine.Evaluate("test.frameError").ToObject() as string;
    internal bool FrameCompleted => this.engine.Evaluate("test.frameCompleted").AsBoolean();
    internal int DrawnFrames => (int)this.engine.Evaluate("test.drawnFrames").AsNumber();
    internal bool? CameraOption => this.engine.Evaluate("test.cameraOption").ToObject() as bool?;
    internal string SavedState => this.engine.Evaluate("test.storage").AsString();
    internal string ExportedState => this.engine.Evaluate("test.exported").AsString();
    internal JsonElement SavedCamera => JsonDocument.Parse(this.SavedState).RootElement.GetProperty("camera").Clone();

    internal void Execute(string script)
    {
        this.engine.Execute(script);
        this.engine.Advanced.ProcessTasks();
    }

    internal void Orbit(int deltaY)
    {
        this.engine.SetValue("deltaY", deltaY);
        this.Execute("test.orbit(deltaY);");
    }

    internal void SetAllowed(bool allowed)
    {
        this.engine.SetValue("allowed", allowed);
        this.Execute("cadEditor.setAllowBelowGround(allowed); test.flushRender();");
    }

    internal void Pan(int deltaY)
    {
        this.engine.SetValue("deltaY", deltaY);
        this.Execute("test.pan(deltaY);");
    }

    internal void Zoom(int deltaY)
    {
        this.engine.SetValue("deltaY", deltaY);
        this.Execute("test.zoom(deltaY);");
    }

    internal void Import(string content)
    {
        this.engine.SetValue("content", content);
        this.Execute("test.importScene(content);");
    }

    internal void DeliverFrame(bool stale)
    {
        this.engine.SetValue("stale", stale);
        this.Execute("test.deliverFrame(stale);");
    }
}
