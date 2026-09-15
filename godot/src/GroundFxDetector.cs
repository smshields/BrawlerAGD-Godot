using Godot;
using BrawlerSim.Sim;

namespace BrawlerGodot;

/// <summary>
/// Ground FX event detection (particle prototype, 2026-09-14), shared by every host
/// that ticks a SimWorld (ArenaView, MatchPreview, MovesetPreview). Landings must be
/// caught INSIDE the tick loop: SimPhysics zeroes Velocity.Y in the tick that
/// resolves a landing, so the impact speed only exists pre-tick — the same reason
/// ArenaView.DetectDeaths snapshots before Tick. Footsteps ride a per-player stride
/// accumulator (a puff each bodyWidth x strideFactor of ground travel). Events are
/// buffered across fast-forwarded ticks (capped per rendered frame) and drained into
/// the GroundFxView after the host's view sync.
/// </summary>
public sealed class GroundFxDetector
{
    private readonly SimWorld _world;
    private readonly bool[] _preGrounded;
    private readonly float[] _preVelY;
    private readonly float[] _stride;
    private readonly System.Collections.Generic.List<Event> _events = new();

    private readonly record struct Event(
        bool Landing, Vector2 FeetWorld, float Mass, float ImpactSpeed,
        float AbsVelX, float MaxGroundSpeed, float BodyHalfX, float BodyHalfY);

    public GroundFxDetector(SimWorld world)
    {
        _world = world;
        int players = world.Players.Count;
        _preGrounded = new bool[players];
        _preVelY = new float[players];
        _stride = new float[players];
    }

    /// <summary>Call immediately before SimWorld.Tick.</summary>
    public void BeforeTick()
    {
        for (int i = 0; i < _world.Players.Count; i++)
        {
            SimPlayer p = _world.Players[i];
            _preGrounded[i] = p.IsGrounded;
            _preVelY[i] = p.Velocity.Y;
        }
    }

    /// <summary>Call immediately after SimWorld.Tick (inside a fast-forward loop).</summary>
    public void AfterTick()
    {
        BrawlerSim.Vfx.GroundFxConfig config = GroundFxBank.Config;
        for (int i = 0; i < _world.Players.Count; i++)
        {
            SimPlayer p = _world.Players[i];
            if (p.IsAbsent)
            {
                _stride[i] = 0f;
                continue;
            }
            // Spawn pads are their own visual moment — no dust there (pad support
            // also isn't in Platforms, double-guarded by SupportPlatformIndex).
            bool grounded = p.IsGrounded && !p.SpawnPadActive;
            if (!p.IsGrounded)
            {
                _stride[i] = 0f;
                continue;
            }
            if (grounded && !_preGrounded[i])
            {
                _stride[i] = 0f;
                if (_events.Count < config.MaxEventsPerFrame
                    && SimPhysics.SupportPlatformIndex(p, _world.Platforms) >= 0)
                {
                    _events.Add(new Event(
                        Landing: true, Feet(p), p.Mass,
                        ImpactSpeed: Mathf.Max(0f, -_preVelY[i]),
                        AbsVelX: 0f, p.MaxGroundSpeed, p.BodyHalf.X, p.BodyHalf.Y));
                }
            }
            else if (grounded && Mathf.Abs(p.Velocity.X) >= config.FootstepSpeedMin)
            {
                _stride[i] += Mathf.Abs(p.Velocity.X) / 60f;
                float strideLength = config.StrideLength(p.BodyHalf.X);
                if (_stride[i] >= strideLength)
                {
                    _stride[i] -= strideLength;
                    if (_events.Count < config.MaxEventsPerFrame)
                    {
                        _events.Add(new Event(
                            Landing: false, Feet(p), p.Mass, ImpactSpeed: 0f,
                            Mathf.Abs(p.Velocity.X), p.MaxGroundSpeed,
                            p.BodyHalf.X, p.BodyHalf.Y));
                    }
                }
            }
        }
    }

    /// <summary>Drain buffered events into the view (once per rendered frame, after
    /// the host's view syncs). weatherGate = live EpisodeGate x Intensity, 0 when
    /// the host has no weather.</summary>
    public void Drain(GroundFxView view, float weatherGate)
    {
        foreach (Event e in _events)
        {
            if (e.Landing)
            {
                view.TriggerLanding(e.FeetWorld, e.Mass, e.ImpactSpeed,
                    e.BodyHalfX, e.BodyHalfY, weatherGate);
            }
            else
            {
                view.TriggerFootstep(e.FeetWorld, e.Mass, e.AbsVelX,
                    e.MaxGroundSpeed, e.BodyHalfY, weatherGate);
            }
        }
        _events.Clear();
    }

    /// <summary>The contact point: the body's crouch-invariant bottom edge.</summary>
    private static Vector2 Feet(SimPlayer p) =>
        new(p.Position.X, p.Position.Y - p.BodyHalf.Y);
}
