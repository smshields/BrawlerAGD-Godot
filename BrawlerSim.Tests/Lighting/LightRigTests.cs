using BrawlerSim.Backgrounds;
using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Lighting;
using BrawlerSim.Sprites;
using Xunit;

namespace BrawlerSim.Tests.Lighting;

/// <summary>
/// The stage light rig (backgrounds track Phase 4, 2026-09-02 — brief §Phase 4):
/// the contrast floor holds after rig tinting across every remap target (property
/// test over real layouts), near-black outline pixels are BIT-IDENTICAL under every
/// rig, the fighter tint never exceeds its cap, and weather modulation stays inside
/// the amplitude cap (the rig is otherwise static — a pure data record).
/// </summary>
public class LightRigTests
{
    private static readonly Lazy<BackgroundLibrary> LibraryLazy = new(() =>
        BackgroundLibrary.LoadFile(Repo("backgrounds_v1_index.json")));

    private static readonly Lazy<BackgroundPalette> PaletteLazy = new(() =>
        BackgroundPalette.LoadFiles(
            Repo("master_palette.json"),
            Repo("background_ramps_bidir.json"),
            Repo("remap_transition_table.json")));

    private static readonly Lazy<LightingConfig> ConfigLazy = new(() =>
        LightingConfig.LoadFile(FindRepoFile(Path.Combine("godot", "assets", "lighting.json"))));

    private static LightingConfig Config => ConfigLazy.Value;

    private static string Repo(string file) =>
        FindRepoFile(Path.Combine("godot", "assets", "backgrounds_v1", file));

    private static BackgroundSelector NewSelector() => new(
        LibraryLazy.Value, PaletteLazy.Value,
        BackgroundSelectionConfig.LoadFile(FindRepoFile(
            Path.Combine("godot", "assets", "background_selection.json"))),
        StageThemeLibrary.LoadFile(FindRepoFile(
            Path.Combine("godot", "assets", "tiles_v2_slices.json"))));

    private static string FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"could not locate {relative} above the test directory");
    }

    [Fact]
    public void ContrastFloorHoldsAcrossEveryEntryAndRemapTarget()
    {
        // Property test straight over the corpus: every (full entry, remap target)
        // pair — plus every far/mid pair's blend the selector could compose — must
        // derive a rig whose lit-cap-vs-backdrop contrast clears the floor (or the
        // rig-off baseline when the dim layer already can't do better).
        BackgroundSelector selector = NewSelector();
        foreach (BackgroundEntry entry in LibraryLazy.Value.Entries
            .Where(e => e.LayerRole == "full"))
        {
            foreach (string? remap in new string?[] { null }.Concat(selector.LegalRemaps(entry)))
            {
                var layout = new BackgroundLayout(entry, null, null, remap,
                    new BackgroundVariant(new BgRect(0, 0, entry.Width, entry.Height),
                        false, 1f, 1f, 1f), 0.1f, 0f, 0f, null);
                LightRigPlan plan = LightRig.Derive(layout, PaletteLazy.Value, Config);
                AssertContrastHolds(entry, remap, plan);
            }
        }
    }

    private static void AssertContrastHolds(BackgroundEntry entry, string? remap, LightRigPlan plan)
    {
        (byte r, byte g, byte b) = PaletteLazy.Value.RemapDomLight(entry, remap);
        float bgLum = (0.299f * r + 0.587f * g + 0.114f * b) / 255f * Config.AssumedBackdropDim;
        float capLightLum = 0.299f * plan.CapR + 0.587f * plan.CapG + 0.114f * plan.CapB;
        float cap = Config.CapBaseLum + (capLightLum - Config.CapBaseLum) * plan.CapStrength;
        float ambientMul = 1f + (0.299f * plan.AmbientR + 0.587f * plan.AmbientG
            + 0.114f * plan.AmbientB - 1f) * plan.AmbientStrength;
        float floorTarget = Math.Min(Config.ContrastFloor, Math.Abs(Config.CapBaseLum - bgLum));
        Assert.True(Math.Abs(cap * ambientMul - bgLum) >= floorTarget - 1e-3f,
            $"{entry.Id} remap {remap ?? "none"}: contrast under floor");
    }

    [Fact]
    public void OutlinePixelsAreBitIdenticalUnderEveryRig()
    {
        BackgroundSelector selector = NewSelector();
        var outlineColors = new (byte, byte, byte)[]
        {
            (0, 0, 0), (12, 12, 14), (23, 23, 23), (40, 40, 40), (40, 12, 3),
        };
        int rigs = 0;
        foreach (BackgroundEntry entry in LibraryLazy.Value.Entries
            .Where(e => e.LayerRole == "full").Take(60))
        {
            foreach (string? remap in new string?[] { null }.Concat(selector.LegalRemaps(entry)))
            {
                var layout = new BackgroundLayout(entry, null, null, remap,
                    new BackgroundVariant(new BgRect(0, 0, entry.Width, entry.Height),
                        false, 1f, 1f, 1f), 0.1f, 0f, 0f, null);
                LightRigPlan plan = LightRig.Derive(layout, PaletteLazy.Value, Config);
                rigs++;
                foreach ((byte, byte, byte) c in outlineColors)
                {
                    Assert.Equal(c, LightRig.TintTile(c, plan, Config));
                    Assert.Equal(c, LightRig.TintTile(c, plan, Config, weatherMod: 1.08f));
                }
                // Non-outline pixels DO tint under a non-neutral rig.
                if (plan.AmbientStrength > 0.01f
                    && (plan.AmbientR < 0.95f || plan.AmbientB < 0.95f))
                {
                    Assert.NotEqual(((byte)200, (byte)200, (byte)200),
                        LightRig.TintTile((200, 200, 200), plan, Config));
                }
            }
        }
        Assert.True(rigs > 100, "too few rigs exercised");
    }

    [Fact]
    public void FighterTintNeverExceedsTheCap()
    {
        BackgroundSelector selector = NewSelector();
        var config = GenerationConfig.Default with
        {
            StageThemeSelector = new StageThemeSelector(StageThemeLibrary.LoadFile(
                FindRepoFile(Path.Combine("godot", "assets", "tiles_v2_slices.json")))),
            BackgroundSelector = selector,
        };
        for (ulong seed = 1; seed <= 200; seed++)
        {
            StageGenome stage = GameGenome.Generate(config, new Pcg32(seed)).Stage;
            BackgroundLayout? layout = selector.Layout(
                stage, BackgroundSelector.BackgroundSeed(stage));
            LightRigPlan plan = LightRig.Derive(layout, PaletteLazy.Value, Config);
            Assert.True(plan.FighterTint <= Config.FighterTintCap + 1e-6f);
            (float r, float g, float b) = LightRig.FighterTint(plan);
            // A 15% cap means the multiplier never strays past 15% from white.
            Assert.InRange(r, 1f - Config.FighterTintCap, 1f + Config.FighterTintCap);
            Assert.InRange(g, 1f - Config.FighterTintCap, 1f + Config.FighterTintCap);
            Assert.InRange(b, 1f - Config.FighterTintCap, 1f + Config.FighterTintCap);
        }
    }

    [Fact]
    public void WeatherModulationStaysInsideTheAmplitudeCapAndRigIsOtherwiseStatic()
    {
        var plan = LightRigPlan.Neutral with { WeatherAmplitude = Config.WeatherAmplitudeCap };
        // No weather → exactly 1 (static during play).
        Assert.Equal(1f, LightRig.WeatherModulation(plan, null, 1f));
        foreach (string type in new[] { "rain", "ash", "embers", "snow" })
        {
            for (float x = 0f; x <= 2f; x += 0.1f)
            {
                float mod = LightRig.WeatherModulation(plan, type, x);
                Assert.InRange(mod,
                    1f - Config.WeatherAmplitudeCap - 1e-6f,
                    1f + Config.WeatherAmplitudeCap + 1e-6f);
            }
        }
        // Embers warm; precipitation darkens.
        Assert.True(LightRig.WeatherModulation(plan, "embers", 1f) > 1f);
        Assert.True(LightRig.WeatherModulation(plan, "rain", 1f) < 1f);
        // The neutral (legacy) plan is the identity rig.
        Assert.Equal((1f, 1f, 1f), LightRig.FighterTint(LightRigPlan.Neutral));
        Assert.Equal(((byte)200, (byte)90, (byte)90),
            LightRig.TintTile((200, 90, 90), LightRigPlan.Neutral, Config));
    }
}
