using Godot;
using BrawlerSim.Evolution;
using BrawlerSim.Genome;

namespace BrawlerGodot;

/// <summary>
/// App-side cache for pilot descriptor-bin edges (2026-09-10/11): a run dir (the
/// Evolve screen's shadow archive) or a screen-stable location (QUALITY EXPLORATION)
/// keeps one descriptor-bins.json so the same content bins identically across
/// sessions (frozen-edges rule). The cache only counts when it matches the CURRENT
/// configuration and pilot — a stale or corrupt file re-pilots, never silently bins
/// with foreign edges (pilots are per-configuration).
/// </summary>
public static class DescriptorBinsCache
{
    public static DescriptorBins LoadOrCreate(
        GenerationConfig generation, ulong pilotSeed, string path, int samples)
    {
        string configKey = DescriptorBins.ConfigKeyFor(generation);
        if (System.IO.File.Exists(path))
        {
            try
            {
                DescriptorBins cached = DescriptorBins.Load(path);
                if (cached.ConfigKey == configKey && cached.PilotSeed == pilotSeed
                    && cached.PilotSamples == samples)
                {
                    return cached;
                }
                GD.Print($"descriptor bins cache is for another configuration — re-piloting ({path})");
            }
            catch (System.Exception e)
            {
                GD.Print($"descriptor bins cache unreadable — re-piloting ({e.Message})");
            }
        }
        DescriptorBins bins = DescriptorBins.FromPilot(generation, pilotSeed, samples);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        bins.Save(path);
        return bins;
    }
}
