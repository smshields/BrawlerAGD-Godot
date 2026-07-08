using BrawlerSim.Determinism;
using BrawlerSim.Sim;
using BrawlerSim.Tests.Support;
using Xunit;

namespace BrawlerSim.Tests.Sim;

public class HitResolutionTests
{
    /// <summary>Attacker at −4 facing right, victim planted inside the hitbox arc at −3.</summary>
    private static SimWorld ArrangeDuel(out SimPlayer attacker, out SimPlayer victim)
    {
        var world = new SimWorld(TestGames.FlatArena());
        attacker = world.Players[0];
        victim = world.Players[1];
        attacker.Position = new Vec2(-4f, -1.4f);
        victim.Position = new Vec2(-3f, -1.4f);
        Settle(world, attacker, victim);
        return world;
    }

    private static void Settle(SimWorld world, SimPlayer a, SimPlayer b)
    {
        for (int i = 0; i < 120 && !(a.IsGrounded && b.IsGrounded); i++)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
    }

    private static void RunAttack(SimWorld world, int ticks = 40)
    {
        Span<InputFrame> frame = stackalloc[] { new InputFrame(0f, false, Attack: true), InputFrame.Neutral };
        world.Tick(frame);
        frame[0] = InputFrame.Neutral;
        for (int i = 0; i < ticks; i++)
        {
            world.Tick(frame);
        }
    }

    [Fact]
    public void HitAppliesExactDamageAndStats()
    {
        SimWorld world = ArrangeDuel(out SimPlayer attacker, out SimPlayer victim);
        RunAttack(world);

        // damageGiven = 5 + (0.2 + 0.1 + 0.2)·5 = 7.5, exactly one hit (6-tick window,
        // knockback launches the victim out of the hitbox).
        Assert.Equal(1, victim.TotalHitsReceived);
        Assert.Equal(7.5f, victim.Damage, 0.0001f);
        Assert.Equal(7.5f, victim.TotalDamageTaken, 0.0001f);
        Assert.Equal(0, attacker.TotalHitsReceived);
    }

    [Fact]
    public void HitLaunchesVictimAndAppliesScaledHitstun()
    {
        SimWorld world = ArrangeDuel(out _, out SimPlayer victim);

        bool sawStun = false;
        int stunTicksObserved = 0;
        Span<InputFrame> frame = stackalloc[] { new InputFrame(0f, false, Attack: true), InputFrame.Neutral };
        world.Tick(frame);
        frame[0] = InputFrame.Neutral;
        for (int i = 0; i < 120; i++)
        {
            world.Tick(frame);
            if (victim.State == PlayerState.Stun)
            {
                sawStun = true;
                stunTicksObserved++;
            }
        }
        Assert.True(sawStun);
        // hitstun = 0.5 s · damage(7.5) · scalar(0.2) = 0.75 s → 45 ticks.
        Assert.Equal(45, stunTicksObserved);
        Assert.True(victim.TotalHitsReceived >= 1);
    }

    [Fact]
    public void InvincibilityPreventsMultipleHitsPerOverlap()
    {
        // Long execution window (0.4 s = 24 ticks) + zero knockback: victim stays inside
        // the hitbox the whole swing. Without invincibility this would be many hits;
        // with the 6-tick window it is exactly ceil(24 / 6)... hits every 6 ticks → 4.
        var world = new SimWorld(TestGames.FlatArena(
            moveOverrides: new[]
            {
                (BrawlerSim.Genome.MoveParams.ExecutionDuration, 0.4f),
                (BrawlerSim.Genome.MoveParams.KnockbackScalar, 1f),
                (BrawlerSim.Genome.MoveParams.DamageFactor, 0f),
                (BrawlerSim.Genome.MoveParams.HitstunDuration, 0f),
            }));
        SimPlayer attacker = world.Players[0];
        SimPlayer victim = world.Players[1];
        attacker.Position = new Vec2(-4f, -1.4f);
        victim.Position = new Vec2(-3f, -1.4f);
        Settle(world, attacker, victim);

        RunAttack(world, ticks: 60);
        Assert.Equal(4, victim.TotalHitsReceived);
    }

    [Fact]
    public void KnockbackFormulaMatchesUnityExactly()
    {
        var victimPos = new Vec2(-3f, -1.4f);
        var hitboxCenter = new Vec2(-3.0f, -1.4f);
        var kbDir = new Vec2(0f, 1f);

        Vec2 kb = SimWorld.ComputeKnockback(victimPos, hitboxCenter, kbDir,
            attackerFacing: 1, knockbackScalar: 8f, damageAfterHit: 7.5f);

        // (victim − center + dir) · scalar · (damage·0.1) = (0,1)·8·0.75 = (0, 6).
        Assert.Equal(0f, kb.X);
        Assert.Equal(6f, kb.Y);

        // Facing left mirrors the X component of the knockback direction.
        Vec2 mirrored = SimWorld.ComputeKnockback(victimPos, hitboxCenter, new Vec2(0.5f, 1f),
            attackerFacing: -1, knockbackScalar: 8f, damageAfterHit: 7.5f);
        Assert.Equal(-0.5f * 8f * 0.75f, mirrored.X, 0.0001f);
    }

    [Fact]
    public void StunInterruptsAnInFlightMove()
    {
        SimWorld world = ArrangeDuel(out SimPlayer attacker, out SimPlayer victim);

        // Victim starts its own long warm-up; attacker's faster move lands first.
        Span<InputFrame> frame = stackalloc[]
        {
            new InputFrame(0f, false, Attack: true),
            new InputFrame(0f, false, Attack: true),
        };
        world.Tick(frame);
        Assert.Equal(PlayerState.WarmUp, victim.State);

        frame[0] = InputFrame.Neutral;
        frame[1] = InputFrame.Neutral;
        for (int i = 0; i < 40 && victim.State != PlayerState.Stun; i++)
        {
            world.Tick(frame);
        }
        Assert.Equal(PlayerState.Stun, victim.State); // move canceled by the hit
    }

    [Fact]
    public void FallingOutOfTheBlastZoneCostsAStockAndRespawns()
    {
        SimWorld world = ArrangeDuel(out SimPlayer attacker, out SimPlayer victim);
        victim.Position = new Vec2(0f, -20f); // well outside the blast zone
        world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });

        Assert.Equal(2, victim.Stocks);
        Assert.Equal(victim.SpawnPosition, victim.Position);
        Assert.Equal(0f, victim.Damage);
        Assert.False(world.IsOver);
    }

    [Fact]
    public void FourthDeathEndsTheMatch()
    {
        // Unity parity: stocks==0 at death ends the match — "3 stocks" = 4 lives.
        SimWorld world = ArrangeDuel(out _, out SimPlayer victim);
        for (int death = 0; death < 4; death++)
        {
            Assert.False(world.IsOver);
            victim.Position = new Vec2(0f, -20f);
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
        Assert.True(world.IsOver);
        Assert.Equal(victim.Index, world.LoserIndex);
        Assert.Equal(0, victim.Stocks);
    }
}
