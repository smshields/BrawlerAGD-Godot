using BrawlerSim.Agents;
using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Replay;
using BrawlerSim.Serialization;
using BrawlerSim.Sim;
using BrawlerSim.Tests.Support;
using Xunit;

namespace BrawlerSim.Tests.Agents;

/// <summary>
/// The utility instrument (docs/features/utility-agent.md): channel selection semantics,
/// the four designer requirements, determinism, and the commitment window.
/// </summary>
public class UtilityAgentTests
{
    /// <summary>r=0, interval=1: pure argmax every tick — assertions are exact.</summary>
    private static readonly AgentConfig Greedy = new() { Randomness = 0f, DecisionIntervalTicks = 1 };

    private static SimWorld SettledWorld(GameGenome genome, Vec2 p0, Vec2 p1)
    {
        var world = new SimWorld(genome);
        world.Players[0].Position = p0;
        world.Players[1].Position = p1;
        for (int i = 0; i < 120 && !(world.Players[0].IsGrounded && world.Players[1].IsGrounded); i++)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
        return world;
    }

    [Fact]
    public void FarOpponentIsApproached()
    {
        // Req 2: grounded, far apart, nothing else going on → move toward the opponent.
        SimWorld world = SettledWorld(TestGames.FlatArena(), new Vec2(-6f, -1.4f), new Vec2(6f, -1.4f));
        var agent = new UtilityAgent(new Pcg32(1), Greedy);
        InputFrame input = agent.GetInput(world, 0);
        Assert.Equal(1f, input.Horizontal);
        Assert.Equal<byte>(0, input.Actions); // out of range: no swing
    }

    [Fact]
    public void InRangeOpponentIsAttacked()
    {
        // Req 3: hitbox reaches (FlatArena move: 1 unit toward facing) → press button 0.
        SimWorld world = SettledWorld(TestGames.FlatArena(), new Vec2(-1f, -1.4f), new Vec2(0.2f, -1.4f));
        var agent = new UtilityAgent(new Pcg32(1), Greedy);
        InputFrame input = agent.GetInput(world, 0);
        Assert.Equal(InputFrame.ActionBit(0), input.Actions);
    }

    [Fact]
    public void TheMoveThatCanActuallyHitIsChosen()
    {
        // Req 3 with MULTIPLE attacks: move 0 swings upward (can't reach a level
        // opponent), move 1 swings horizontally (can). The agent must press move 1's
        // button — button 1 under the [0,1,0,1] mapping.
        var moves = new[]
        {
            new MoveGenome(TestGames.Move((MoveParams.MoveAngle, MathF.PI / 2f)), 0), // straight up
            new MoveGenome(TestGames.Move(), 0),                                      // straight ahead
        };
        CharacterGenome Make(string name) =>
            new(name, 3, 0, TestGames.Character(), moves, new[] { 0, 1, 0, 1 });
        var genome = new GameGenome(
            new[] { Make("P1"), Make("P2") },
            new StageGenome(new[] { new PlatformGene(-8, -3, 16, 1) }));

        SimWorld world = SettledWorld(genome, new Vec2(-1f, -1.4f), new Vec2(0.2f, -1.4f));
        var agent = new UtilityAgent(new Pcg32(1), Greedy);
        InputFrame input = agent.GetInput(world, 0);
        Assert.Equal(InputFrame.ActionBit(1), input.Actions);
    }

    [Fact]
    public void TheStrongestMoveThatCanHitWins()
    {
        // Second-move feature: both moves reach the opponent; move 1 deals more
        // damage -> its button (1 under the [0,1,0,1] mapping) must be pressed.
        var moves = new[]
        {
            new MoveGenome(TestGames.Move((MoveParams.DamageFactor, 2f)), 0),
            new MoveGenome(TestGames.Move((MoveParams.DamageFactor, 9f)), 0),
        };
        CharacterGenome Make(string name) =>
            new(name, 3, 0, TestGames.Character(), moves, new[] { 0, 1, 0, 1 });
        var genome = new GameGenome(
            new[] { Make("P1"), Make("P2") },
            new StageGenome(new[] { new PlatformGene(-8, -3, 16, 1) }));

        SimWorld world = SettledWorld(genome, new Vec2(-1f, -1.4f), new Vec2(0.2f, -1.4f));
        var agent = new UtilityAgent(new Pcg32(1), Greedy);
        Assert.Equal(InputFrame.ActionBit(1), agent.GetInput(world, 0).Actions);
    }

    [Fact]
    public void OverAPitTheAgentHeadsForThePlatformAndCountsRecovery()
    {
        // Req 1a: airborne over the void left of the platform → move right (toward it),
        // and the RecoveryTicks research stat advances.
        var world = new SimWorld(TestGames.FlatArena());
        world.Players[0].Position = new Vec2(-9.5f, 1f);  // platform spans x ∈ [-8, 8]; blast zone ±10.76
        world.Players[1].Position = new Vec2(6f, -1.4f);
        world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });

        var agent = new UtilityAgent(new Pcg32(1), Greedy);
        InputFrame input = agent.GetInput(world, 0);
        Assert.Equal(1f, input.Horizontal);
        Assert.True(world.Players[0].RecoveryTicks > 0);
    }

    [Fact]
    public void DoomedCharactersTurnOnTheOpponent()
    {
        // Req 1b: over the void, platform hopeless (it sits ABOVE a falling character
        // whose jumps are spent — no way to gain height) → chase the opponent instead.
        var world = new SimWorld(TestGames.FlatArena());
        SimPlayer self = world.Players[0];
        self.Position = new Vec2(-10f, -5f);    // inside the blast zone, below stage level
        self.JumpsExhausted = true;
        world.Players[1].Position = new Vec2(-10.5f, -5f); // opponent to the LEFT
        world.Players[1].JumpsExhausted = true;
        world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });

        var agent = new UtilityAgent(new Pcg32(1), Greedy);
        InputFrame input = agent.GetInput(world, 0);
        Assert.Equal(-1f, input.Horizontal); // toward the opponent, not the platform
    }

    [Fact]
    public void HighDamageBacksAway()
    {
        // Req 4: damage 100, opponent out of hit range → retreat beats approach.
        SimWorld world = SettledWorld(TestGames.FlatArena(), new Vec2(-2f, -1.4f), new Vec2(4f, -1.4f));
        world.Players[0].Damage = 100f;
        var agent = new UtilityAgent(new Pcg32(1), Greedy);
        InputFrame input = agent.GetInput(world, 0);
        Assert.Equal(-1f, input.Horizontal);
    }

    [Fact]
    public void HighDamageStillAttacksInRange()
    {
        // Req 4's rider: evasive posture must not disable the attack channel.
        SimWorld world = SettledWorld(TestGames.FlatArena(), new Vec2(-1f, -1.4f), new Vec2(0.2f, -1.4f));
        world.Players[0].Damage = 100f;
        var agent = new UtilityAgent(new Pcg32(1), Greedy);
        InputFrame input = agent.GetInput(world, 0);
        Assert.Equal(InputFrame.ActionBit(0), input.Actions);
    }

    [Fact]
    public void TelegraphedSwingsAreDodgedWithAJump()
    {
        // Opponent winds up (WarmUp) in range; we can't hit back yet -> hop away
        // (2026-07-10 designer request: agents should use jumps to escape attacks).
        SimWorld world = SettledWorld(TestGames.FlatArena(), new Vec2(-1.2f, -1.4f), new Vec2(0.8f, -1.4f));
        // Face the opponent toward us (one tick of leftward input), then wind up.
        world.Tick(stackalloc[] { InputFrame.Neutral, new InputFrame(-1f, 0f, false, 0) });
        world.Tick(stackalloc[] { InputFrame.Neutral, new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)) });
        Assert.Equal(PlayerState.WarmUp, world.Players[1].State);
        Assert.Equal(-1, world.Players[1].Facing);

        var agent = new UtilityAgent(new Pcg32(1), Greedy);
        InputFrame input = agent.GetInput(world, 0);
        Assert.True(input.Jump, "agent did not hop out of the telegraphed swing");
    }

    [Fact]
    public void ExhaustedJumpersDisengageInsteadOfChasing()
    {
        // AirJumpsExhausted cannot attack (Unity parity), so chasing is pure exposure
        // (designer, 2026-07-10): while exhausted mid-air near the opponent, the agent
        // must drift AWAY and press no attack buttons.
        SimWorld world = SettledWorld(TestGames.FlatArena(), new Vec2(-2f, -1.4f), new Vec2(1f, -1.4f));
        SimPlayer self = world.Players[0];
        // Burn both jumps: ground jump, then air jump.
        world.Tick(stackalloc[] { new InputFrame(0f, 0f, true, 0), InputFrame.Neutral });
        world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        world.Tick(stackalloc[] { new InputFrame(0f, 0f, true, 0), InputFrame.Neutral });
        Assert.Equal(PlayerState.AirJumpsExhausted, self.State);

        var agent = new UtilityAgent(new Pcg32(1), Greedy);
        InputFrame input = agent.GetInput(world, 0);
        Assert.Equal(-1f, input.Horizontal); // away from the opponent at x = 1
        Assert.Equal<byte>(0, input.Actions); // no pointless attack presses

        // Once landed (capabilities restored), the chase resumes.
        for (int i = 0; i < 300 && self.State != PlayerState.Idle; i++)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
        Assert.Equal(PlayerState.Idle, self.State);
        Assert.Equal(1f, agent.GetInput(world, 0).Horizontal);
    }

    [Fact]
    public void ExhaustedJumpersStillRecoverOverPits()
    {
        // Disengaging must never override survival: exhausted OVER A PIT still steers
        // toward the reachable platform.
        var world = new SimWorld(TestGames.FlatArena());
        world.Players[0].Position = new Vec2(-9.5f, 1f); // left of the platform, falling
        world.Players[0].JumpsExhausted = true;
        world.Players[1].Position = new Vec2(-6f, -1.4f); // opponent near the left edge
        world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });

        var agent = new UtilityAgent(new Pcg32(1), Greedy);
        Assert.Equal(1f, agent.GetInput(world, 0).Horizontal); // toward the platform
    }

    [Fact]
    public void CommitmentWindowHoldsMovementAndEdgesActions()
    {
        // Interval 10, quiet world: horizontal persists between decisions; jump/attack
        // may only appear on decision ticks (single-tick presses).
        var config = new AgentConfig { Randomness = 0f, DecisionIntervalTicks = 10 };
        SimWorld world = SettledWorld(TestGames.FlatArena(), new Vec2(-6f, -1.4f), new Vec2(6f, -1.4f));
        var agent = new UtilityAgent(new Pcg32(1), config);

        InputFrame first = agent.GetInput(world, 0);
        for (int t = 1; t < 10; t++)
        {
            InputFrame held = agent.GetInput(world, 0);
            Assert.Equal(first.Horizontal, held.Horizontal);
            Assert.False(held.Jump);
            Assert.Equal<byte>(0, held.Actions);
        }
    }

    [Fact]
    public void SameSeedSameConfigIsBitDeterministic()
    {
        var genome = LegacyImporter.ImportGameFolder(
            Path.Combine(AppContext.BaseDirectory, "TestData", "UnityGames", "GameC")).Genome;
        var config = new AgentConfig { Randomness = 0.35f, DecisionIntervalTicks = 5 };

        MatchResult Run() => MatchRunner.Run(genome, new IInputSource[]
        {
            config.CreateSource(new Pcg32(42, 0)),
            config.CreateSource(new Pcg32(42, 1)),
        });

        MatchResult a = Run();
        MatchResult b = Run();
        Assert.Equal(a.FinalHash, b.FinalHash);
        Assert.Equal(a.Ticks, b.Ticks);
    }

    [Fact]
    public void UtilityMatchesReplayBitExactly()
    {
        var genome = LegacyImporter.ImportGameFolder(
            Path.Combine(AppContext.BaseDirectory, "TestData", "UnityGames", "GameA")).Genome;
        MatchResult live = MatchRunner.Run(genome, new IInputSource[]
        {
            AgentConfig.Default.CreateSource(new Pcg32(9, 0)),
            AgentConfig.Default.CreateSource(new Pcg32(9, 1)),
        }, recordTrace: true);

        InputTrace roundTripped = InputTraceJson.Deserialize(InputTraceJson.Serialize(live.Trace!));
        MatchResult replayed = MatchRunner.Replay(genome, roundTripped);
        Assert.Equal(live.FinalHash, replayed.FinalHash);
    }

    [Fact]
    public void StudyGameMatchesAlwaysTerminate()
    {
        foreach (string game in new[] { "GameA", "GameB", "GameC", "GameD", "GameE", "GameF" })
        {
            var genome = LegacyImporter.ImportGameFolder(
                Path.Combine(AppContext.BaseDirectory, "TestData", "UnityGames", game)).Genome;
            MatchResult result = MatchRunner.Run(genome, new IInputSource[]
            {
                AgentConfig.Default.CreateSource(new Pcg32(7, 0)),
                AgentConfig.Default.CreateSource(new Pcg32(7, 1)),
            });
            Assert.True(result.Ticks <= MatchConfig.Default.MaxTicks);
            Assert.InRange(result.LoserIndex, -1, 1);
        }
    }

    /// <summary>
    /// Cross-platform canary #3: a full match under the DEFAULT (utility) instrument,
    /// pinned on macOS ARM64 / .NET 8. Complements the decision-tree golden — both
    /// instruments must be bit-deterministic everywhere.
    /// </summary>
    [Fact]
    public void UtilityGoldenMatchHashMatches()
    {
        var genome = LegacyImporter.ImportGameFolder(
            Path.Combine(AppContext.BaseDirectory, "TestData", "UnityGames", "GameC")).Genome;
        MatchResult result = MatchRunner.Run(genome, new IInputSource[]
        {
            AgentConfig.Default.CreateSource(new Pcg32(20260709, 0)),
            AgentConfig.Default.CreateSource(new Pcg32(20260709, 1)),
        });
        // Re-pinned 2026-07-13: dash feature — hash format grew (dash fields) and the
        // defense-channel refactor changed agent RNG draw order. Prior pins:
        // 17369012423366605927 (shield), 2695584452249808183 (exhausted disengage),
        // 13063472053697347474 (traversal), 4239894947699402948 / 8169156236120396373
        // (stun caps), 3417322836374644188 (flank), 15992591370472251803 (initial).
        Assert.Equal(5206514131316504700UL, result.FinalHash);
    }
}
