using Godot;
using BrawlerSim.Hyperspace;

namespace BrawlerGodot.Hyperspace;

/// <summary>The boundary between the engine-free layout math and Godot's types.
/// GalaxyLayout deals in GalaxyPoint/GalaxyColor so it can be unit-tested without
/// Godot (galaxy-view.md §10); everything crosses here and nowhere else.</summary>
public static class GalaxyVec
{
    public static Vector3 From(GalaxyPoint p) => new(p.X, p.Y, p.Z);

    public static GalaxyPoint To(Vector3 v) => new(v.X, v.Y, v.Z);

    public static Color From(GalaxyColor c) => new(c.R, c.G, c.B);
}
