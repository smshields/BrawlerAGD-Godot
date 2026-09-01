using BrawlerSim.Determinism;
using BrawlerSim.Sim;

namespace BrawlerSim.Agents;

/// <summary>Everything a behavior may appraise, computed once per tick. AttackTarget is
/// the position to fight from — opponent minus the best move's mirrored hitbox offset.</summary>
public readonly record struct UtilityContext(
    SimWorld World,
    SimPlayer Self,
    SimPlayer Opponent,
    bool OverPit,
    bool Doomed,
    Vec2 RecoverTarget,
    bool RecoverTargetValid,
    float Distance,
    bool[] CanHit,
    bool AnyCanHit,
    int FacingToOpponent,
    Vec2 AttackTarget,
    bool UnderThreat,
    int FlankDirection,
    bool FlankSafe,
    bool HasTraversal,
    Vec2 TraversalLaunch,
    int TraversalDirection,
    bool TraversalNeedsJump,
    bool Vulnerable,
    float ShieldHealthFraction,
    bool OpponentBreakStunned,
    bool TelegraphThreat,
    bool DashUsable,
    int DashSlot,
    bool OpponentStunned,
    Vec2 RecoverAim,
    bool CrouchClearsThreat,
    bool ProjectileThreat,
    bool RangedThreat,
    // Thin platforms (2026-09-01, DEVIATIONS #34) — all false on thin-free stages.
    bool OnThinPlatform,
    bool CanDropSafely,
    bool TraversalDrop);

/// <summary>
/// The per-decision score sheet. Horizontal = {left, neutral, right}; Jump = {no, yes};
/// Attack[0] = none, then one candidate per distinct usable move (lowest mapped button,
/// feature-1 convention). AttackMoves/AttackButtons map candidates back to moves/buttons.
/// </summary>
public sealed class UtilityScores
{
    // Channel slot indices (Select() returns one of these per channel). The
    // slot − 1 = axis value convention maps Left/Down to −1 and Right/Up to +1.
    public const int Left = 0, HNeutral = 1, Right = 2;
    public const int Down = 0, VNeutral = 1, Up = 2;
    public const int NoJump = 0, DoJump = 1;

    /// <summary>The horizontal slot pushing along <paramref name="direction"/>'s sign
    /// (strictly positive → Right, else Left — callers with an inclusive-zero
    /// tie-break spell it out themselves).</summary>
    public static int Toward(float direction) => direction > 0f ? Right : Left;

    public readonly float[] Horizontal = new float[3];
    public readonly float[] Vertical = new float[3]; // down, neutral, up (2026-07-13)
    public readonly float[] Jump = new float[2];
    public readonly float[] Attack;
    public readonly int[] AttackMoves;
    public readonly int[] AttackButtons;

    public UtilityScores(SimPlayer self)
    {
        var moves = new List<int>(self.Moves.Count);
        for (int m = 0; m < self.Moves.Count; m++)
        {
            if (self.ButtonForMove(m) >= 0)
            {
                moves.Add(m);
            }
        }
        Attack = new float[1 + moves.Count];
        AttackMoves = new int[1 + moves.Count];
        AttackButtons = new int[1 + moves.Count];
        for (int c = 0; c < moves.Count; c++)
        {
            AttackMoves[c + 1] = moves[c];
            AttackButtons[c + 1] = self.ButtonForMove(moves[c]);
        }
    }
}
