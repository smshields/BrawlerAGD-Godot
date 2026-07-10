namespace BrawlerSim.Sim;

/// <summary>
/// Fixed physical and rules constants for a match. Values are ported from the Unity
/// project (Physics2DSettings, prefab colliders, Arena.cs) and documented per field.
/// </summary>
public sealed record MatchConfig
{
    public int TicksPerSecond { get; init; } = SimInfo.TicksPerSecond;
    public float Dt => 1f / TicksPerSecond;

    /// <summary>Downward gravity magnitude (Unity Physics2D gravity was (0, -9.81)).</summary>
    public float Gravity { get; init; } = 9.81f;

    /// <summary>Match time limit; reaching it ends the match as a draw (Unity: 60 s).</summary>
    public float MaxMatchSeconds { get; init; } = 60f;
    public int MaxTicks => (int)(MaxMatchSeconds * TicksPerSecond);

    /// <summary>
    /// Blast-zone half extents, centered on the origin. Unity sized this at runtime from
    /// the camera: height = 2·orthoSize(5)·1.1 = 11, width = height·aspect·1.1 — i.e. the
    /// blast zone DEPENDED ON THE WINDOW ASPECT RATIO. Fixed here to the 16:9 values so
    /// every match, headless or rendered, plays in the same arena.
    /// </summary>
    public float BlastZoneHalfWidth { get; init; } = 11f * (16f / 9f) * 1.1f / 2f;   // ≈ 10.756
    public float BlastZoneHalfHeight { get; init; } = 11f / 2f;

    /// <summary>Player collider base size before per-character scaling (Unity capsule 0.74289274 × 1, boxed).</summary>
    public float PlayerBaseWidth { get; init; } = 0.74289274f;
    public float PlayerBaseHeight { get; init; } = 1f;

    /// <summary>
    /// Move hitbox base size before scaling (Unity BoxCollider2D 1 × 1). The effective
    /// hitbox is scaled by BOTH the move's and the owning player's scalars — the move was
    /// a child transform in Unity, so parent scale multiplied in. Preserved: bigger
    /// characters genuinely swing bigger attacks.
    /// </summary>
    public float MoveBaseSize { get; init; } = 1f;

    /// <summary>Post-hit invincibility (Unity: 0.1 s coroutine).</summary>
    public int InvincibilityTicks { get; init; } = 6;

    /// <summary>
    /// Upper bound on a single hit's stun (2026-07-10, docs/features/second-move.md):
    /// stun scales with victim damage, so uncapped it grows into multi-second locks
    /// that high-scoring genomes exploit. PositiveInfinity = uncapped (Unity parity);
    /// the shipped default comes from the stun-cap experiment in that doc:
    /// 0.75 s (2026-07-10) — bounds single-hit locks while stun chains keep the
    /// mechanic alive; uncapped runs let a 46%-stunned near-single-move exploiter top
    /// the fitness table, 0.75 s produced the healthiest champions across seeds.
    /// </summary>
    public float MaxStunSeconds { get; init; } = 0.75f;

    /// <summary>
    /// Half extents of the AI's platform-sensing box (Unity: 20×15 BoxCollider2D on the
    /// OverlapDetector child, used by GetClosestPlatformDirection).
    /// </summary>
    public float PlatformSenseHalfWidth { get; init; } = 10f;
    public float PlatformSenseHalfHeight { get; init; } = 7.5f;

    /// <summary>Max distance a body may travel per collision substep (anti-tunneling).</summary>
    public float MaxStepDistance { get; init; } = 0.25f;

    /// <summary>
    /// Max player-vs-player overlap resolved per tick. Deep overlaps (landing on the
    /// opponent's head) separate smoothly over several ticks like Box2D's positional
    /// correction, instead of one teleport-sized position jump.
    /// </summary>
    public float MaxDepenetrationPerTick { get; init; } = 0.05f;

    public static readonly MatchConfig Default = new();

    public int ToTicks(float seconds) => (int)MathF.Round(seconds * TicksPerSecond);
}
