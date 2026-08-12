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

    /// <summary>
    /// Regression pin of the stage design space (2026-07-21, Map Size —
    /// docs/features/map-size.md). Size genes span 0.5×–5× the legacy dimensions;
    /// the legacy visibleHalfWidth reference is 11·(16/9)/2 (the blast width without
    /// its second 1.1 Unity-quirk factor) so legacy blast zones reconstruct
    /// bit-exactly. Order matters: single-point crossover operates on indices.
    /// </summary>
    [Fact]
    public void StageSchemaMatchesDesignRecord()
    {
        const float legacyHalfWidth = 11f * (16f / 9f) / 2f;
        var expected = new (string Key, float Min, float Max)[]
        {
            ("visibleHalfWidth", legacyHalfWidth * 0.5f, legacyHalfWidth * 5f),
            ("visibleHalfHeight", 2.5f, 25f),
            ("koMarginFraction", 0.05f, 0.25f),
            ("platformCount", 2f, 16f),
            ("maxPlatformSize", 3f, 14f),
            ("mirrored", 0f, 1f),
            ("mirrorSide", 0f, 1f),
            ("spawn1X", -49f, 49f),
            ("spawn1Y", -25f, 26f),
            ("spawn2X", -49f, 49f),
            ("spawn2Y", -25f, 26f),
            // Spawning Behaviors (2026-07-22): platform lifetime + character invuln.
            ("platformSpawnDuration", 1f, 5f),
            ("spawnInvulnDuration", 1f, 3f),
            // Four Player Support (2026-08-12): spawns 3/4 — every stage carries four
            // spawn points regardless of player count (docs/features/four-player.md).
            ("spawn3X", -49f, 49f),
            ("spawn3Y", -25f, 26f),
            ("spawn4X", -49f, 49f),
            ("spawn4Y", -25f, 26f),
        };
        AssertSchema(DefaultSchemas.Stage, expected);
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
