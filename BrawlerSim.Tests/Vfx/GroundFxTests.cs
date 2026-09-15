using BrawlerSim.Vfx;
using Xunit;

namespace BrawlerSim.Tests.Vfx;

/// <summary>
/// Ground FX magnitude math (particle prototype, 2026-09-14): hand-computed
/// landing/footstep expectations, monotonicity in mass and impact speed, the
/// readability-budget load clamps, and the shipped-file == code-defaults contract
/// every tuning file honors.
/// </summary>
public class GroundFxTests
{
    private static readonly GroundFxConfig Config = new();

    // ── the shipped file mirrors the code defaults ──────────────────────────────

    [Fact]
    public void ShippedFileParsesAndMatchesCodeDefaults()
    {
        GroundFxConfig shipped = GroundFxConfig.LoadFile(
            FindRepoFile(Path.Combine("godot", "assets", "ground_fx.json")));
        var defaults = new GroundFxConfig();
        Assert.Equal(defaults.LandingMinImpactSpeed, shipped.LandingMinImpactSpeed);
        Assert.Equal(defaults.LandingEnergyMin, shipped.LandingEnergyMin);
        Assert.Equal(defaults.LandingEnergyMax, shipped.LandingEnergyMax);
        Assert.Equal(defaults.LandingCountMin, shipped.LandingCountMin);
        Assert.Equal(defaults.LandingCountMax, shipped.LandingCountMax);
        Assert.Equal(defaults.LandingScaleMin, shipped.LandingScaleMin);
        Assert.Equal(defaults.LandingScaleMax, shipped.LandingScaleMax);
        Assert.Equal(defaults.LandingSpreadSpeedMin, shipped.LandingSpreadSpeedMin);
        Assert.Equal(defaults.LandingSpreadSpeedMax, shipped.LandingSpreadSpeedMax);
        Assert.Equal(defaults.LandingOpacityMin, shipped.LandingOpacityMin);
        Assert.Equal(defaults.LandingOpacityMax, shipped.LandingOpacityMax);
        Assert.Equal(defaults.LandingLifeMin, shipped.LandingLifeMin);
        Assert.Equal(defaults.LandingLifeMax, shipped.LandingLifeMax);
        Assert.Equal(defaults.FootstepSpeedMin, shipped.FootstepSpeedMin);
        Assert.Equal(defaults.FootstepCountMin, shipped.FootstepCountMin);
        Assert.Equal(defaults.FootstepCountMax, shipped.FootstepCountMax);
        Assert.Equal(defaults.FootstepScaleMin, shipped.FootstepScaleMin);
        Assert.Equal(defaults.FootstepScaleMax, shipped.FootstepScaleMax);
        Assert.Equal(defaults.FootstepSpreadSpeedMin, shipped.FootstepSpreadSpeedMin);
        Assert.Equal(defaults.FootstepSpreadSpeedMax, shipped.FootstepSpreadSpeedMax);
        Assert.Equal(defaults.FootstepOpacityMin, shipped.FootstepOpacityMin);
        Assert.Equal(defaults.FootstepOpacityMax, shipped.FootstepOpacityMax);
        Assert.Equal(defaults.FootstepLifeMin, shipped.FootstepLifeMin);
        Assert.Equal(defaults.FootstepLifeMax, shipped.FootstepLifeMax);
        Assert.Equal(defaults.StrideFactor, shipped.StrideFactor);
        Assert.Equal(defaults.ScaleBirthFraction, shipped.ScaleBirthFraction);
        Assert.Equal(defaults.FadeStartFraction, shipped.FadeStartFraction);
        Assert.Equal(defaults.LandingFanTiltDeg, shipped.LandingFanTiltDeg);
        Assert.Equal(defaults.LandingFanSpreadDeg, shipped.LandingFanSpreadDeg);
        Assert.Equal(defaults.FootstepBackTiltDeg, shipped.FootstepBackTiltDeg);
        Assert.Equal(defaults.FootstepSpreadDeg, shipped.FootstepSpreadDeg);
        Assert.Equal(defaults.RefBodyHalfX, shipped.RefBodyHalfX);
        Assert.Equal(defaults.RefBodyHalfY, shipped.RefBodyHalfY);
        Assert.Equal(defaults.MassFactorLo, shipped.MassFactorLo);
        Assert.Equal(defaults.MassFactorHi, shipped.MassFactorHi);
        Assert.Equal(defaults.OpacityCap, shipped.OpacityCap);
        Assert.Equal(defaults.LifetimeCap, shipped.LifetimeCap);
        Assert.Equal(defaults.MaxBurstCount, shipped.MaxBurstCount);
        Assert.Equal(defaults.MaxEventsPerFrame, shipped.MaxEventsPerFrame);
        Assert.Equal(defaults.WeatherBlendMax, shipped.WeatherBlendMax);
    }

    // ── landings ────────────────────────────────────────────────────────────────

    [Fact]
    public void SoftTouchdownEmitsNothing()
    {
        // Just under the 3.0 impact gate: stepping off a small ledge stays silent.
        LandingBurst burst = Config.ComputeLanding(
            mass: 1f, impactSpeed: 2.9f, bodyHalfX: 0.4f, bodyHalfY: 0.6f);
        Assert.Equal(0, burst.Count);
    }

    [Fact]
    public void LandingHandComputed()
    {
        // mass 1, impact 10, reference body (width/size factors both 1):
        // energy = 1 x 10^2 = 100; e01 = (100 - 9) / (250 - 9) = 91/241.
        float e01 = 91f / 241f;
        LandingBurst burst = Config.ComputeLanding(
            mass: 1f, impactSpeed: 10f, bodyHalfX: 0.4f, bodyHalfY: 0.6f);
        Assert.Equal(24, burst.Count); // round(14 + 26 x e01) = round(23.817)
        Assert.Equal(1.3f + 1.9f * e01, burst.Scale, 0.0001f);
        Assert.Equal(70f + 150f * e01, burst.SpreadSpeed, 0.0001f);
        Assert.Equal(0.24f + 0.14f * e01, burst.Opacity, 0.0001f);
        Assert.Equal(0.35f + 0.45f * e01, burst.Lifetime, 0.0001f);
    }

    [Fact]
    public void HeavierAndFasterIsBigger()
    {
        // Monotone in mass and impact speed over a grid: more energy, more dust.
        float[] masses = { 0.5f, 1f, 1.5f, 2f, 2.5f };
        float[] impacts = { 4f, 6f, 9f, 13f, 20f };
        foreach (float mass in masses)
        {
            LandingBurst prev = default;
            foreach (float impact in impacts)
            {
                LandingBurst burst = Config.ComputeLanding(mass, impact, 0.4f, 0.6f);
                Assert.True(burst.Count >= prev.Count);
                Assert.True(burst.Scale >= prev.Scale);
                Assert.True(burst.Opacity >= prev.Opacity);
                prev = burst;
            }
        }
        foreach (float impact in impacts)
        {
            LandingBurst prev = default;
            foreach (float mass in masses)
            {
                LandingBurst burst = Config.ComputeLanding(mass, impact, 0.4f, 0.6f);
                Assert.True(burst.Count >= prev.Count);
                prev = burst;
            }
        }
    }

    [Fact]
    public void CapsHoldAtExtremes()
    {
        // Heaviest genome at a blast-zone impact speed with a huge body: every
        // output stays inside the readability budget.
        LandingBurst burst = Config.ComputeLanding(
            mass: 2.5f, impactSpeed: 60f, bodyHalfX: 3f, bodyHalfY: 3f);
        Assert.True(burst.Count <= Config.MaxBurstCount);
        Assert.True(burst.Opacity <= Config.OpacityCap);
        Assert.True(burst.Lifetime <= Config.LifetimeCap);
    }

    // ── footsteps ───────────────────────────────────────────────────────────────

    [Fact]
    public void FootstepBelowThresholdIsSilent()
    {
        FootstepPuff puff = Config.ComputeFootstep(
            mass: 1.5f, absVelX: 1.4f, maxGroundSpeed: 5.5f, bodyHalfY: 0.6f);
        Assert.Equal(0, puff.Count);
    }

    [Fact]
    public void FootstepHandComputed()
    {
        // speed01 = (3.5 - 1.5) / (5.5 - 1.5) = 0.5; mass 1.5 => massFactor 1.
        FootstepPuff puff = Config.ComputeFootstep(
            mass: 1.5f, absVelX: 3.5f, maxGroundSpeed: 5.5f, bodyHalfY: 0.6f);
        Assert.Equal(5, puff.Count); // round(lerp(3, 7, 0.5)) = 5
        Assert.Equal(1.2f, puff.Scale, 0.0001f);        // lerp(0.8, 1.6, 0.5)
        Assert.Equal(62.5f, puff.SpreadSpeed, 0.0001f); // lerp(35, 90, 0.5)
        Assert.Equal(0.23f, puff.Opacity, 0.0001f);     // lerp(0.16, 0.30, 0.5)
        Assert.Equal(0.39f, puff.Lifetime, 0.0001f);    // lerp(0.28, 0.5, 0.5)
    }

    [Fact]
    public void FootstepMassFactorClampsAtBothEnds()
    {
        // At full speed the unclamped count is 7; the lightest genome (0.5) hits
        // the 0.7 floor => round(4.9) = 5; the heaviest (2.5) hits the 1.3
        // ceiling => round(9.1) = 9.
        FootstepPuff light = Config.ComputeFootstep(0.5f, 5.5f, 5.5f, 0.6f);
        FootstepPuff heavy = Config.ComputeFootstep(2.5f, 5.5f, 5.5f, 0.6f);
        Assert.Equal(5, light.Count);
        Assert.Equal(9, heavy.Count);
    }

    [Fact]
    public void StrideLengthScalesWithBody()
    {
        Assert.Equal(0.68f, Config.StrideLength(0.4f), 0.0001f); // 0.4 x 2 x 0.85
        Assert.Equal(2f * Config.StrideLength(0.4f), Config.StrideLength(0.8f), 0.0001f);
    }

    // ── the budget clamps at load ───────────────────────────────────────────────

    [Fact]
    public void OverBudgetJsonClampsAtLoad()
    {
        GroundFxConfig config = GroundFxConfig.Parse(
            """{ "opacityCap": 0.9, "lifetimeCap": 5.0, "maxBurstCount": 500, "maxEventsPerFrame": 99, "weatherBlendMax": 3.0 }""");
        Assert.Equal(GroundFxConfig.OpacityCeiling, config.OpacityCap);
        Assert.Equal(GroundFxConfig.LifetimeCeiling, config.LifetimeCap);
        Assert.Equal(GroundFxConfig.BurstCountCeiling, config.MaxBurstCount);
        Assert.Equal(GroundFxConfig.EventsPerFrameCeiling, config.MaxEventsPerFrame);
        Assert.Equal(1f, config.WeatherBlendMax);
    }

    [Fact]
    public void GrowAndFadeFractionsStayInRange()
    {
        // The shape knobs must survive a bad hot-edit: a particle born at zero
        // size or fading from the last instant would read as a pop, not a puff.
        GroundFxConfig config = GroundFxConfig.Parse(
            """{ "scaleBirthFraction": 0.0, "fadeStartFraction": 1.5 }""");
        Assert.Equal(0.02f, config.ScaleBirthFraction, 0.0001f);
        Assert.Equal(0.95f, config.FadeStartFraction, 0.0001f);
        Assert.True(config.ScaleBirthFraction < 1f); // born smaller than final
    }

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
}
