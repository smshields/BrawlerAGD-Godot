using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Params;
using BrawlerSim.Sim;
using BrawlerSim.Tests.Support;
using Xunit;

namespace BrawlerSim.Tests.Sim;

/// <summary>
/// Projectile reflection (2026-07-20, designer): reflect genes on shield and dash
/// re-fire a bolt at its shooter — ownership transfers, the path restarts mirrored
/// from the reflection point, and the TTL/damage-decay clocks keep running.
/// </summary>
public class ReflectTests
{
    private static ParamSet Shield(float reflect) =>
        ParamSet.FromDictionary(DefaultSchemas.Shield, new Dictionary<string, float>
        {
            ["windUpDuration"] = 0.05f, ["coolDownDuration"] = 0.1f, ["initialSize"] = 2.0f,
            ["holdDegradationRate"] = 0.05f, ["hitDegradationScalar"] = 0.02f,
            ["knockbackReduction"] = 0.8f, ["spacingPush"] = 0.5f, ["regenRate"] = 0.3f,
            ["breakStunDuration"] = 1f, ["reflect"] = reflect,
        });

    private static ParamSet Dash(float reflect) =>
        ParamSet.FromDictionary(DefaultSchemas.Dash, new Dictionary<string, float>
        {
            ["windUpDuration"] = 0.1f, ["acceleration"] = 6f, ["duration"] = 0.4f,
            ["warmUpInvulnerable"] = 0f, ["durationInvulnerable"] = 0f, ["reflect"] = reflect,
        });

    private static GameGenome Arena(ParamSet defenderMove, MoveType defenderType,
        params (string Key, float Value)[] projectileOverrides)
    {
        var shooter = new CharacterGenome("Shooter", 3, 0, TestGames.Character(),
            new[] { new MoveGenome(TestGames.Projectile(projectileOverrides), 0, MoveType.Projectile) },
            new[] { 0, 0, 0, 0, 0 });
        var defender = new CharacterGenome("Defender", 3, 0, TestGames.Character(),
            new[] { new MoveGenome(defenderMove, 0, defenderType) },
            new[] { 0, 0, 0, 0, 0 });
        return new GameGenome(new[] { shooter, defender },
            new StageGenome(new[] { new PlatformGene(-8, -3, 16, 1) }));
    }

    private static SimWorld Grounded(GameGenome genome, float p0X, float p1X)
    {
        var world = new SimWorld(genome);
        world.Players[0].Position = new Vec2(p0X, -1.4f);
        world.Players[1].Position = new Vec2(p1X, -1.4f);
        for (int i = 0; i < 120 && !(world.Players[0].IsGrounded && world.Players[1].IsGrounded); i++)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
        return world;
    }

    [Fact]
    public void ReflectShieldSendsTheBoltBackAtItsShooter()
    {
        var genome = Arena(Shield(1f), MoveType.Shield);
        SimWorld world = Grounded(genome, -3f, 1f);
        SimPlayer shooter = world.Players[0];
        SimPlayer defender = world.Players[1];
        // Shooter fires once; defender holds the shield the whole time.
        var hold = new InputFrame(0f, 0f, false, InputFrame.ActionBit(0));
        world.Tick(stackalloc[] { new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)), hold });
        SimProjectile? bolt = null;
        for (int t = 0; t < 200 && shooter.TotalHitsReceived == 0; t++)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, hold });
            if (world.Projectiles.Count > 0)
            {
                bolt = world.Projectiles[0];
            }
        }
        // The bolt came back: reflected (not blocked), re-owned, and it hit the shooter.
        Assert.Equal(1, defender.ProjectilesReflected);
        Assert.Equal(0, defender.BlockedHits);
        Assert.Equal(0, defender.TotalHitsReceived);
        Assert.Equal(1, shooter.TotalHitsReceived);
        Assert.NotNull(bolt);
        Assert.Equal(defender.Index, bolt!.Owner);   // ownership transferred
        Assert.Equal(-1, bolt.Facing);               // mirrored (fired rightward)
        Assert.Equal(1, defender.ProjectileHits);    // the reflector gets the credit
    }

    [Fact]
    public void ReflectionKeepsTheLifetimeAndDecayClocksRunning()
    {
        // Slow bolt with decay: reflect mid-flight, then verify DamageScale keeps
        // falling on the ORIGINAL clock (no reset on reflection).
        var genome = Arena(Shield(1f), MoveType.Shield,
            (ProjectileParams.Velocity, 4f),
            (ProjectileParams.DamageDecay, 1f), (ProjectileParams.DecayRate, 0.3f),
            (ProjectileParams.TimeToDecay, 3f));
        SimWorld world = Grounded(genome, -3f, 1f);
        var hold = new InputFrame(0f, 0f, false, InputFrame.ActionBit(0));
        world.Tick(stackalloc[] { new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)), hold });
        SimProjectile? bolt = null;
        int reflectAge = -1;
        float scaleAtReflect = 1f;
        bool spawned = false;
        for (int t = 0; t < 400; t++)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, hold });
            if (world.Projectiles.Count == 0)
            {
                if (spawned) { break; } // lived and died
                continue;               // still in the shooter's warm-up
            }
            spawned = true;
            bolt = world.Projectiles[0];
            if (bolt.ReflectTick >= 0 && reflectAge < 0)
            {
                reflectAge = bolt.AgeTicks;
                scaleAtReflect = bolt.DamageScale;
                Assert.Equal(0, bolt.PathAgeTicks); // path clock reset
            }
        }
        Assert.True(reflectAge > 0, "the bolt was never reflected");
        Assert.True(scaleAtReflect < 1f, "decay had not engaged by the reflect");
        // After the reflect the lifetime clock kept counting from reflectAge — the
        // bolt eventually decayed away entirely (scale → 0 or TTL) instead of
        // getting a fresh life.
        Assert.Empty(world.Projectiles);
    }

    [Fact]
    public void NonReflectShieldStillBlocksAndConsumesTheBolt()
    {
        var genome = Arena(Shield(0f), MoveType.Shield);
        SimWorld world = Grounded(genome, -3f, 1f);
        SimPlayer defender = world.Players[1];
        var hold = new InputFrame(0f, 0f, false, InputFrame.ActionBit(0));
        world.Tick(stackalloc[] { new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)), hold });
        for (int t = 0; t < 120 && defender.BlockedHits == 0; t++)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, hold });
        }
        Assert.Equal(1, defender.BlockedHits);
        Assert.Equal(0, defender.ProjectilesReflected);
        Assert.Empty(world.Projectiles); // absorbed, not returned
        Assert.Equal(0, world.Players[0].TotalHitsReceived);
    }

    [Fact]
    public void ReflectShieldStillDegradesAndPokesStillHit()
    {
        // Degradation on reflect (documented judgment call — the work isn't free).
        var genome = Arena(Shield(1f), MoveType.Shield);
        SimWorld world = Grounded(genome, -3f, 1f);
        SimPlayer defender = world.Players[1];
        float healthBefore = -1f;
        var hold = new InputFrame(0f, 0f, false, InputFrame.ActionBit(0));
        world.Tick(stackalloc[] { new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)), hold });
        for (int t = 0; t < 30; t++)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, hold });
            if (defender.State == PlayerState.Shield && healthBefore < 0f)
            {
                healthBefore = defender.ShieldHealths[0];
            }
        }
        for (int t = 0; t < 120 && defender.ProjectilesReflected == 0; t++)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, hold });
        }
        Assert.Equal(1, defender.ProjectilesReflected);
        // Hold degradation also runs, so just require MORE loss than hold alone
        // would produce in the elapsed window — the hit tax is 7.5 × 0.02 = 0.15,
        // far above a few ticks of 0.05/s hold decay.
        Assert.True(defender.ShieldHealths[0] < healthBefore - 0.1f,
            "reflect did not degrade the shield");
    }

    [Fact]
    public void ReflectDashReturnsTheBoltEvenWithoutIFrames()
    {
        // The dash has reflect but NO invulnerability genes: contact during the Dash
        // state reflects (checked before i-frames), so the bolt never lands.
        var genome = Arena(Dash(1f), MoveType.Dash, (ProjectileParams.Velocity, 6f));
        SimWorld world = Grounded(genome, -3f, 1.5f);
        SimPlayer shooter = world.Players[0];
        SimPlayer defender = world.Players[1];
        world.Tick(stackalloc[] { new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)), InputFrame.Neutral });
        // Spawn ≈ tick 12 at x ≈ −2.89; overlap window at the defender ≈ ticks 50–62.
        // Dash (6 warm-up + 24 travel) pressed at t=46 keeps the Dash state through it.
        for (int t = 0; t < 46; t++)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
        world.Tick(stackalloc[] { InputFrame.Neutral, new InputFrame(-1f, 0f, false, InputFrame.ActionBit(0)) });
        for (int t = 0; t < 90 && shooter.TotalHitsReceived == 0; t++)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
        Assert.Equal(1, defender.ProjectilesReflected);
        Assert.Equal(0, defender.TotalHitsReceived);
        Assert.Equal(0, defender.DashInvulnDodges); // reflected, not i-frame-negated
        Assert.Equal(1, shooter.TotalHitsReceived); // and it came home
    }

    [Fact]
    public void ReflectedBoltCannotPingPongOffItsReflector()
    {
        // After a reflect the reflector is the OWNER: the owner-clearance latch means
        // the bolt cannot re-reflect off them while still overlapping — it leaves.
        var genome = Arena(Shield(1f), MoveType.Shield, (ProjectileParams.Velocity, 3f));
        SimWorld world = Grounded(genome, -3f, 1f);
        SimPlayer defender = world.Players[1];
        var hold = new InputFrame(0f, 0f, false, InputFrame.ActionBit(0));
        world.Tick(stackalloc[] { new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)), hold });
        bool spawned = false;
        for (int t = 0; t < 400; t++)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, hold });
            if (world.Projectiles.Count > 0)
            {
                spawned = true;
            }
            else if (spawned)
            {
                break; // lived and died
            }
        }
        Assert.True(spawned);
        Assert.Equal(1, defender.ProjectilesReflected); // exactly once, no ping-pong
    }

    [Fact]
    public void LegacyShieldAndDashFilesLoadWithReflectOff()
    {
        // A v6 file written BEFORE the reflect append (params lack the key).
        var pre = Arena(Shield(0f), MoveType.Shield);
        string json = System.Text.RegularExpressions.Regex.Replace(
            BrawlerSim.Serialization.GameGenomeJson.Serialize(
                new BrawlerSim.Serialization.GameRecord("t", null, pre)),
            ",\\s*\"reflect\": [-0-9.E+]+", ""); // simulate the pre-append file
        Assert.DoesNotContain("reflect", json);
        var loaded = BrawlerSim.Serialization.GameGenomeJson.Deserialize(json);
        MoveGenome shield = loaded.Genome.Characters[1].Moves[0];
        Assert.Equal(0f, shield.Params.Get(ShieldParams.Reflect));
        Assert.Empty(loaded.Genome.Validate());
    }

    [Fact]
    public void ReflectMatchesAreDeterministic()
    {
        var genome = Arena(Shield(1f), MoveType.Shield,
            (ProjectileParams.Velocity, 5f), (ProjectileParams.DamageDecay, 1f),
            (ProjectileParams.DecayRate, 0.2f));
        ulong Run()
        {
            SimWorld world = Grounded(genome, -3f, 1f);
            var hold = new InputFrame(0f, 0f, false, InputFrame.ActionBit(0));
            var fire = new InputFrame(0f, 0f, false, InputFrame.ActionBit(0));
            for (int t = 0; t < 400; t++)
            {
                world.Tick(stackalloc[] { fire, hold }); // shooter mashes, defender holds
            }
            return world.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
