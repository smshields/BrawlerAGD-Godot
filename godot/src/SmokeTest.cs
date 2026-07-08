using Godot;
using BrawlerSim.Determinism;
using BrawlerSim.Genome;

namespace BrawlerGodot;

/// <summary>
/// Capability smoke test (pre-Phase 4): draws a generated BrawlerSim stage to prove the
/// sim→view link renders, saves a screenshot to $BRAWLER_SHOT if set, then quits.
/// </summary>
public partial class SmokeTest : Node2D
{
    private StageGenome _stage = null!;

    public override void _Ready()
    {
        _stage = GenerationConfig.Default.CreateStageGenerator().Generate(new Pcg32(42));
        QueueRedraw();
        _ = CaptureAndQuitAsync();
    }

    private async System.Threading.Tasks.Task CaptureAndQuitAsync()
    {
        for (int i = 0; i < 5; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        string path = OS.GetEnvironment("BRAWLER_SHOT");
        if (path.Length > 0)
        {
            Image image = GetViewport().GetTexture().GetImage();
            image.SavePng(path);
            GD.Print($"screenshot saved: {path}");
            GetTree().Quit();
        }
    }

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.09f, 0.09f, 0.12f));

        // Sim units → pixels: +y up in sim, down on screen.
        const float scale = 42f;
        Vector2 origin = size / 2f + new Vector2(0f, 60f);
        foreach (PlatformGene p in _stage.Platforms)
        {
            var rect = new Rect2(
                origin.X + p.X * scale,
                origin.Y - (p.Y + p.YSize) * scale,
                p.XSize * scale,
                p.YSize * scale);
            DrawRect(rect, new Color(0.33f, 0.72f, 0.5f));
            DrawRect(rect, new Color(0.85f, 0.95f, 0.88f), filled: false, width: 2f);
        }

        Font font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(40f, 64f),
            "BrawlerAGD-Godot — sim → view smoke test",
            HorizontalAlignment.Left, -1f, 28, Colors.White);
        DrawString(font, new Vector2(40f, 98f),
            $"BrawlerSim v{BrawlerSim.SimInfo.Version}  ·  Godot {Engine.GetVersionInfo()["string"]}  ·  stage seed 42  ·  {_stage.Platforms.Count} platforms",
            HorizontalAlignment.Left, -1f, 16, new Color(0.7f, 0.75f, 0.82f));
    }
}
