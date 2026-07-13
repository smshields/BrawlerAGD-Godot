using BrawlerSim.Genome;
using BrawlerSim.Params;
using Xunit;

namespace BrawlerSim.Tests.Params;

public class ParamSchemaTests
{
    [Fact]
    public void DuplicateKeysThrow()
    {
        Assert.Throws<ArgumentException>(() => new ParamSchema("bad", new[]
        {
            new ParamSpec("a", 0f, 1f),
            new ParamSpec("a", 0f, 2f),
        }));
    }

    [Fact]
    public void IndexOfUnknownKeyThrows()
    {
        Assert.Throws<KeyNotFoundException>(() => DefaultSchemas.Character.IndexOf("nope"));
    }

    /// <summary>
    /// Regression pin of the character design space (Unity SerializedPlayer.ranges /
    /// paper Table 1). Order matters: single-point crossover operates on indices.
    /// Changing any line below is a deliberate design-space change.
    /// </summary>
    [Fact]
    public void CharacterSchemaMatchesUnityRanges()
    {
        var expected = new (string Key, float Min, float Max)[]
        {
            ("maxGroundSpeed", 2f, 10f),
            ("maxAirSpeed", 2f, 10f),
            ("groundAccelerationFactor", 0f, 1f),
            ("airAccelerationFactor", 0f, 1f),
            ("groundJumpForce", 1f, 15f),
            ("airJumpForce", 1f, 15f),
            ("mass", 0.5f, 2.5f),
            ("drag", 1f, 6f),
            ("widthScalar", 0.7f, 1.5f),
            ("heightScalar", 0.5f, 1.5f),
            ("gravityScalar", 0.3f, 1.3f),
            ("hitstunDamageScalar", 0.1f, 0.3f),
            // Appended 2026-07-13 (fastfall-crouch-di.md) — append-only preserved.
            ("fastFallAcceleration", 0f, 15f),
            ("crouchAccelerationChange", -8f, 8f),
            ("crouchSpeed", 0.05f, 0.2f),
            ("crouchMoveSpeed", 0.3f, 1.5f),
            ("crouchHeightRatio", 0.4f, 0.9f),
            ("directionalInfluence", 0.02f, 0.10f),
            ("diKnockbackReduction", 0.05f, 0.20f),
        };
        AssertSchema(DefaultSchemas.Character, expected);
    }

    /// <summary>
    /// Regression pin of the move design space. Note coolDownDuration is 0.1–0.6: the
    /// paper's Table 1 says 0.1–0.4, but the shipped Unity code (which generated the
    /// study games — Game C's cool-down is 0.48) used 0.6. Code is authoritative.
    /// </summary>
    [Fact]
    public void MoveSchemaMatchesUnityRanges()
    {
        var expected = new (string Key, float Min, float Max)[]
        {
            ("moveDist", 0.8f, 1.5f),
            ("moveAngle", 0f, 2f * MathF.PI),
            ("widthScalar", 0.5f, 1.5f),
            ("heightScalar", 0.5f, 1.5f),
            ("warmUpDuration", 0.1f, 0.6f),
            ("executionDuration", 0.1f, 0.4f),
            ("coolDownDuration", 0.1f, 0.6f),
            ("damageFactor", 0f, 10f),
            ("knockbackScalar", 1f, 16f),
            ("knockbackModX", 0f, 1f),
            ("knockbackModY", -1f, 1f),
            ("hitstunDuration", 0f, 1f),
        };
        AssertSchema(DefaultSchemas.Move, expected);
    }

    private static void AssertSchema(ParamSchema schema, (string Key, float Min, float Max)[] expected)
    {
        Assert.Equal(expected.Length, schema.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Key, schema[i].Key);
            Assert.Equal(expected[i].Min, schema[i].Min);
            Assert.Equal(expected[i].Max, schema[i].Max);
        }
    }
}
