using BrawlerSim.Genome;
using BrawlerSim.Params;

namespace BrawlerSim.Tests.Support;

/// <summary>Hand-built genomes with known values so sim tests can assert exact behavior.</summary>
public static class TestGames
{
    public static ParamSet Character(params (string Key, float Value)[] overrides)
    {
        var values = new Dictionary<string, float>
        {
            [CharacterParams.MaxGroundSpeed] = 4f,
            [CharacterParams.MaxAirSpeed] = 4f,
            [CharacterParams.GroundAccelerationFactor] = 0.5f,
            [CharacterParams.AirAccelerationFactor] = 0.5f,
            [CharacterParams.GroundJumpForce] = 8f,
            [CharacterParams.AirJumpForce] = 8f,
            [CharacterParams.Mass] = 1f,
            [CharacterParams.Drag] = 1f,
            [CharacterParams.WidthScalar] = 1f,
            [CharacterParams.HeightScalar] = 1f,
            [CharacterParams.GravityScalar] = 1f,
            [CharacterParams.HitstunDamageScalar] = 0.2f,
            // 2026-07-13 appends at their NEUTRAL values (mechanics off) so every
            // pre-existing hand-computed expectation still holds exactly.
            [CharacterParams.FastFallAcceleration] = 0f,
            [CharacterParams.CrouchAccelerationChange] = 0f,
            [CharacterParams.CrouchSpeed] = 0.1f,
            [CharacterParams.CrouchMoveSpeed] = 1f,
            [CharacterParams.CrouchHeightRatio] = 0.9f,
            [CharacterParams.DirectionalInfluence] = 0f,
            [CharacterParams.DiKnockbackReduction] = 0f,
        };
        foreach ((string key, float value) in overrides)
        {
            values[key] = value;
        }
        return ParamSet.FromDictionary(DefaultSchemas.Character, values);
    }

    public static ParamSet Move(params (string Key, float Value)[] overrides)
    {
        var values = new Dictionary<string, float>
        {
            [MoveParams.MoveDist] = 1f,
            [MoveParams.MoveAngle] = 0f,           // hitbox 1 unit to the right (facing +1)
            [MoveParams.WidthScalar] = 1f,
            [MoveParams.HeightScalar] = 1f,
            [MoveParams.WarmUpDuration] = 0.2f,    // 12 ticks
            [MoveParams.ExecutionDuration] = 0.1f, // 6 ticks — exactly one hit per swing
            [MoveParams.CoolDownDuration] = 0.2f,  // 12 ticks
            [MoveParams.DamageFactor] = 5f,        // damageGiven = 5 + 0.5·5 = 7.5
            [MoveParams.KnockbackScalar] = 8f,
            [MoveParams.KnockbackModX] = 0f,
            [MoveParams.KnockbackModY] = 1f,       // straight up, no 45° flip
            [MoveParams.HitstunDuration] = 0.5f,
        };
        foreach ((string key, float value) in overrides)
        {
            values[key] = value;
        }
        return ParamSet.FromDictionary(DefaultSchemas.Move, values);
    }

    /// <summary>
    /// Two identical characters over one wide floor platform (top at y = −2, spanning
    /// x ∈ [−8, 8]). Spawns land at x = 0 (both) so tests generally reposition players.
    /// </summary>
    public static GameGenome FlatArena(
        (string Key, float Value)[]? characterOverrides = null,
        (string Key, float Value)[]? moveOverrides = null)
    {
        ParamSet character = Character(characterOverrides ?? Array.Empty<(string, float)>());
        ParamSet move = Move(moveOverrides ?? Array.Empty<(string, float)>());
        CharacterGenome Make(string name) =>
            new(name, 3, 0, character, new[] { new MoveGenome(move, 0) });
        var stage = new StageGenome(new[] { new PlatformGene(-8, -3, 16, 1) });
        return new GameGenome(new[] { Make("Player 1"), Make("Player 2") }, stage);
    }
}

/// <summary>Feeds a fixed per-tick script; neutral input once the script runs out.</summary>
public sealed class ScriptedSource : BrawlerSim.Sim.IInputSource
{
    private readonly Func<int, BrawlerSim.Sim.InputFrame> _script;

    public ScriptedSource(Func<int, BrawlerSim.Sim.InputFrame> script)
    {
        _script = script;
    }

    public static readonly ScriptedSource Neutral = new(_ => BrawlerSim.Sim.InputFrame.Neutral);

    public BrawlerSim.Sim.InputFrame GetInput(BrawlerSim.Sim.SimWorld world, int playerIndex) =>
        _script(world.TickCount);
}
