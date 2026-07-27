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

    /// <summary>Match time limit; reaching it ends the match as a draw. Unity: 60 s;
    /// raised to 300 s on 2026-07-21 (designer, Map Size feature) as headroom for
    /// large-map navigation — most matches still end early; run.json records the
    /// per-run value, so old checkpoints resume with their original 60 s.</summary>
    public float MaxMatchSeconds { get; init; } = 300f;
    public int MaxTicks => (int)(MaxMatchSeconds * TicksPerSecond);

    /// <summary>
    /// LEGACY blast-zone half extents. Unity sized this at runtime from the camera:
    /// height = 2·orthoSize(5)·1.1 = 11, width = height·aspect·1.1 — i.e. the blast
    /// zone DEPENDED ON THE WINDOW ASPECT RATIO (note width carries the 1.1 factor
    /// twice — a preserved quirk). Fixed to the 16:9 values so every match plays the
    /// same arena. Since Map Size (2026-07-21) the LIVE blast zone is genome-driven —
    /// SimWorld computes visible × (1 + koMargin) from the stage params, which
    /// reproduces these values bit-exactly for legacy games (regression-tested);
    /// these constants remain only as the documented legacy reference.
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

    /// <summary>Spawning Behaviors (2026-07-22, FEATURES.md §Gameplay Polish;
    /// docs/features/spawn-and-polish.md). Post-death blackout before the character
    /// reappears on its spawn platform — fixed (not evolved), respawns only, and only
    /// engaged when the per-level spawn feature is active. Spawn-platform GEOMETRY:
    /// a thin pad just under the spawning body's feet (half extents in world units).</summary>
    public float RespawnBlackoutSeconds { get; init; } = 3f;
    public int RespawnBlackoutTicks => ToTicks(RespawnBlackoutSeconds);
    public float SpawnPadHalfWidth { get; init; } = 1.0f;
    public float SpawnPadHalfHeight { get; init; } = 0.15f;

    /// <summary>Projectile path constants (2026-07-14, docs/features/projectiles.md).
    /// One scalar gene per spec ("frequency for waves, scalars for quadratics"), so
    /// the sine amplitude and the quadratic unit scale are fixed here.</summary>
    public float ProjectileSineAmplitude { get; init; } = 0.8f;
    public float ProjectileQuadraticScale { get; init; } = 0.05f;

    /// <summary>
    /// Upper bound on a single hit's stun (2026-07-10, docs/features/second-move.md):
    /// stun scales with victim damage, so uncapped it grows into multi-second locks
    /// that high-scoring genomes exploit. PositiveInfinity = uncapped (Unity parity);
    /// the shipped default comes from the stun-cap experiments in that doc:
    /// 0.75 s (first sweep) still allowed re-stun CHAINS up to a 97%-stunned round
    /// (0.1 s invincibility < 0.75 s stun ⇒ infinite re-chaining), so the second sweep
    /// (2026-07-10, with the stunLock fitness penalty + jump reward live) settled on
    /// 0.25 s: a flinch that cannot chain past ~16% of a match, with the strongest
    /// champions and recovered jump-force genes.
    /// </summary>
    public float MaxStunSeconds { get; init; } = 0.25f;

    /// <summary>Shield constants (2026-07-12, FEATURES.md §Shield). Break threshold:
    /// the shield breaks when its radius shrinks to 1/5 of the character's height
    /// (spec: "1/5th of the character size in radius"). Offset speed: how fast the
    /// directional controls slide the shield. Push cap: positional expulsion per tick
    /// (same anti-teleport idea as MaxDepenetrationPerTick).</summary>
    public float ShieldBreakRadiusFraction { get; init; } = 0.2f;
    public float ShieldOffsetSpeed { get; init; } = 3f;
    public float ShieldPushMaxPerTick { get; init; } = 0.05f;

    /// <summary>Dash-into-opponent contact (2026-07-13, designer): solid, but the
    /// velocity a dashing player can impart is capped here — damage-independent,
    /// deliberately far below KO speeds ("a dash can shove, never KO").</summary>
    public float DashContactPushCap { get; init; } = 2f;

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
