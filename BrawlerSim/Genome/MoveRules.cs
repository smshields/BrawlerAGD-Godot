using BrawlerSim.Determinism;
using BrawlerSim.Params;

namespace BrawlerSim.Genome;

/// <summary>
/// Semantics derived from a move's raw params, carried over from the Unity
/// SerializedMove. Genomes store raw values only; everything here is a pure function,
/// applied when the sim (or a test) needs the effective move.
/// </summary>
public static class MoveRules
{
    public const float BaseDamage = 5f;

    /// <summary>Hitbox center relative to the character, from polar (dist, angle) params.</summary>
    public static Vec2 MoveLocation(ParamSet move) =>
        new(
            move.Get(MoveParams.MoveDist) * DetMath.Cos(move.Get(MoveParams.MoveAngle)),
            move.Get(MoveParams.MoveDist) * DetMath.Sin(move.Get(MoveParams.MoveAngle)));

    /// <summary>Damage per hit: 5 + (warmUp + execution + coolDown) * damageFactor. Slower moves hit harder.</summary>
    public static float DamageGiven(ParamSet move) =>
        BaseDamage
        + (move.Get(MoveParams.WarmUpDuration)
           + move.Get(MoveParams.ExecutionDuration)
           + move.Get(MoveParams.CoolDownDuration))
          * move.Get(MoveParams.DamageFactor);

    /// <summary>
    /// Effective knockback direction: the raw gene vector, verbatim.
    ///
    /// HISTORY (CHANGE_LOG #36): through 2026-09-09 this was the Unity-parity rule
    /// "if the raw knockback vector points within 45° of the hitbox direction, flip
    /// its X" — the paper's documented "knockback pointing diagonally backwards"
    /// quirk. Evolution learned to TARGET the flip: aligning moveAngle with the
    /// knockback genes produced inward knockback that dragged the victim into the
    /// attacker for stun chains (6 of 12 dominant moves across the 2026-09-09
    /// timescale champions triggered it). Removed 2026-09-10, designer-directed
    /// ("the weak version": inward knockback stays EXPRESSIBLE via the genes' valid
    /// domain, KnockbackModX >= -1.5, but is no longer produced by innocuous aligned
    /// genes). ConstrainKnockback below is untouched — generation streams stay
    /// byte-exact (fingerprint unmoved); match goldens re-pinned with this change.
    /// </summary>
    public static Vec2 EffectiveKnockback(ParamSet move) =>
        new(move.Get(MoveParams.KnockbackModX), move.Get(MoveParams.KnockbackModY));

    /// <summary>
    /// Generation-time constraint (Unity parity): while the raw knockback vector points
    /// more than 135° away from the hitbox direction, lerp it toward the hitbox direction
    /// in 5% steps. Applied once when a move genome is first generated — crossover and
    /// mutation do NOT re-apply it, exactly as in the original.
    /// </summary>
    public static ParamSet ConstrainKnockback(ParamSet move)
    {
        Vec2 moveLoc = MoveLocation(move);
        var knockback = new Vec2(move.Get(MoveParams.KnockbackModX), move.Get(MoveParams.KnockbackModY));

        // Converges monotonically; the cap only guards against float pathology.
        for (int i = 0; i < 10_000 && Vec2.AngleDeg(moveLoc, knockback) >= 135f; i++)
        {
            knockback = Vec2.Lerp(knockback, moveLoc, 0.05f);
        }

        return move.With(
            (MoveParams.KnockbackModX, knockback.X),
            (MoveParams.KnockbackModY, knockback.Y));
    }
}
