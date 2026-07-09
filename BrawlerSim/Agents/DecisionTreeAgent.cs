using BrawlerSim.Determinism;
using BrawlerSim.Sim;

namespace BrawlerSim.Agents;

/// <summary>
/// Port of the Unity `AI` controller — the automated playtester whose behavior the
/// fitness function measures. Its quirks are the instrument the AIIDE '22 results were
/// produced with, so they are ported FAITHFULLY, not fixed. Documented quirks:
/// - Pursue's vertical checks compare the target's ABSOLUTE y to 0 (not relative to the
///   player), so "jump when the target is above" really means "above the world origin".
/// - Jump/attack intents are LEVELS, not edges: while `jump` stays true the ground jump
///   chains instantly into the air jump on the next tick.
/// - Recovery's platform search only sees platforms overlapping a 20×15 box around the
///   player (the Unity OverlapDetector child); with none in range it homes toward the
///   world origin (Unity's "closest point" defaulted to (0,0)).
/// - RecoveryTicks increments EVERY tick spent recovering (Unity incremented
///   totalRecoveryStateTransition per frame, despite the name).
/// </summary>
public sealed class DecisionTreeAgent : IInputSource
{
    private const int RecoveryTimeLimit = 100;
    private const int TargetTimeLimit = 150;

    private readonly Pcg32 _rng;
    private int _recoveryTime;
    private int _targetTime;
    private Vec2 _targetMod;

    private bool _pressLeft;
    private bool _pressRight;
    private bool _pressJump;
    private bool _pressAttack;

    public DecisionTreeAgent(Pcg32 rng)
    {
        _rng = rng;
    }

    public InputFrame GetInput(SimWorld world, int playerIndex)
    {
        SimPlayer self = world.Players[playerIndex];
        SimPlayer opponent = world.Players[1 - playerIndex];

        _targetTime++;
        if (_targetTime > TargetTimeLimit)
        {
            _targetTime = 0;
            _targetMod = new Vec2(_rng.NextFloat(-1f, 1f), _rng.NextFloat(-0.2f, 0.2f));
        }

        bool overPit = OverPit(world, self, 0f);
        _recoveryTime = overPit ? _recoveryTime + 1 : 0;

        if (_recoveryTime > RecoveryTimeLimit)
        {
            self.RecoveryTicks++;
            UpdateRecover(world, self);
        }
        else
        {
            UpdatePursue(world, self, opponent);
        }

        float horizontal = _pressLeft ? -1f : (_pressRight ? 1f : 0f);

        // 2026-07-08 multi-move controls: the agent's decisions are unchanged, only the
        // encoding — "attack" now means pressing the lowest-index button mapped to the
        // move it wants. The ported tree only ever wants move 0 (the single Unity move);
        // move selection for multi-move genomes is future agent work (DEVIATIONS.md).
        byte actions = 0;
        if (_pressAttack)
        {
            int button = self.ButtonForMove(0);
            if (button >= 0)
            {
                actions = InputFrame.ActionBit(button);
            }
        }
        return new InputFrame(horizontal, 0f, _pressJump, actions);
    }

    private void UpdatePursue(SimWorld world, SimPlayer self, SimPlayer opponent)
    {
        Vec2 movePosition = self.Hitbox.Center;
        Vec2 relMove = (movePosition - self.Position) * 1.2f;
        Vec2 target = opponent.Position - relMove + _targetMod;

        if (self.Hitbox.Overlaps(opponent.Body))
        {
            _pressAttack = true;
            return;
        }
        _pressAttack = false;

        // Unity quirk preserved: target.Y compared to 0 (world origin), not to self.
        _pressJump = (target.Y > 0f && self.IsGrounded) || ApproachingEdge(world, self);

        if (target.Y < 0f && self.IsGrounded)
        {
            _pressRight = true;
            _pressLeft = false;
        }
        else if (target.X > self.Position.X)
        {
            _pressRight = true;
            _pressLeft = false;
        }
        else
        {
            _pressLeft = true;
            _pressRight = false;
        }
    }

    private void UpdateRecover(SimWorld world, SimPlayer self)
    {
        Vec2 platformDirection = ClosestSensedPlatformPoint(world, self) - self.Position;
        _pressJump = OverPit(world, self, 0f) && platformDirection.Y <= 0.1f;
        _pressRight = platformDirection.X > 0f;
        _pressLeft = !_pressRight;
        _pressAttack = false;
    }

    /// <summary>No platform anywhere below the sample point (Unity: infinite raycast down).</summary>
    private static bool OverPit(SimWorld world, SimPlayer self, float xOffset)
    {
        float x = self.Position.X + xOffset;
        foreach (Aabb platform in world.Platforms)
        {
            if (x >= platform.Left && x <= platform.Right && platform.Top <= self.Position.Y)
            {
                return false;
            }
        }
        return true;
    }

    private static bool ApproachingEdge(SimWorld world, SimPlayer self)
    {
        // Unity scaled the probe by localScale.x → sign is facing, magnitude widthScalar.
        float probe = 0.2f * self.Facing * self.WidthScalar;
        return OverPit(world, self, probe) && !OverPit(world, self, 0f);
    }

    /// <summary>
    /// Closest point on any platform overlapping the sense box; (0,0) when none —
    /// reproducing Unity's default-initialized "nearest platform point".
    /// </summary>
    private static Vec2 ClosestSensedPlatformPoint(SimWorld world, SimPlayer self)
    {
        var sense = new Aabb(
            self.Position,
            new Vec2(world.Config.PlatformSenseHalfWidth, world.Config.PlatformSenseHalfHeight));

        Vec2 nearest = Vec2.Zero;
        float best = float.PositiveInfinity;
        foreach (Aabb platform in world.Platforms)
        {
            if (!sense.Overlaps(platform))
            {
                continue;
            }
            Vec2 point = platform.ClosestPoint(self.Position);
            float distance = (point - self.Position).Length();
            if (distance < best)
            {
                best = distance;
                nearest = point;
            }
        }
        return nearest;
    }
}
