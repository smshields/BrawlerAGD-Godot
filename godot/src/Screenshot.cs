using Godot;
using System.Threading.Tasks;

namespace BrawlerGodot;

/// <summary>Screenshot capture for the automation flows (ArenaView tick shots,
/// EvolveView dashboard shots, Boot's plain BRAWLER_SHOT): await the next
/// FramePostDraw so the frame is complete, save, optionally quit.</summary>
public static class Screenshot
{
    public static async Task CaptureAsync(Node node, string path, bool quitWhenDone)
    {
        await node.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        node.GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"shot saved: {path}");
        if (quitWhenDone)
        {
            node.GetTree().Quit();
        }
    }
}
