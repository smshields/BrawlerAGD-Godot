using System;
using System.Collections.Generic;
using System.Linq;
using NameGen.Data;

namespace NameGen.Features
{
    /// <summary>
    /// Turns raw genomes into named features. This file owns all schema-semantics
    /// quirks (bools-as-floats, ints-as-floats, off-at-zero params), so the data
    /// layer never has to know about them. Features are the stable contract:
    /// schemas can append params without touching traits.json until a feature uses them.
    /// </summary>
    public sealed class FeatureExtractor
    {
        private readonly SchemaRangesDef _ranges;

        public FeatureExtractor(SchemaRangesDef ranges) => _ranges = ranges;

        // ---- helpers ----

        private static double Norm(IReadOnlyDictionary<string, float> p, IReadOnlyDictionary<string, RangeDef> ranges, string key)
        {
            if (!p.TryGetValue(key, out var v) || !ranges.TryGetValue(key, out var r)) return 0.5;
            if (r.Max <= r.Min) return 0.5;
            return Math.Max(0.0, Math.Min(1.0, (v - r.Min) / (r.Max - r.Min)));
        }

        private static bool Flag(IReadOnlyDictionary<string, float> p, string key)
            => p.TryGetValue(key, out var v) && v >= 0.5f;

        private static int IntOf(IReadOnlyDictionary<string, float> p, string key, int fallback = -1)
            => p.TryGetValue(key, out var v) ? (int)Math.Floor(v) : fallback;

        /// <summary>Off-at-zero params: below the generation min means "mechanic absent".</summary>
        private static bool Present(IReadOnlyDictionary<string, float> p, IReadOnlyDictionary<string, RangeDef> ranges, string key)
        {
            if (!p.TryGetValue(key, out var v)) return false;
            if (!ranges.TryGetValue(key, out var r)) return v > 0;
            return r.Kind == "offAtZero" ? v >= r.Min * 0.999 : true;
        }

        // ---- character ----

        public FeatureVector ExtractCharacter(CharacterGenome g)
        {
            var f = new FeatureVector();
            var p = g.Params;
            var cr = _ranges.Character;

            double groundSpeed = Norm(p, cr, "maxGroundSpeed");
            double airSpeed = Norm(p, cr, "maxAirSpeed");
            double groundAccel = Norm(p, cr, "groundAccelerationFactor");
            double airAccel = Norm(p, cr, "airAccelerationFactor");
            double groundJump = Norm(p, cr, "groundJumpForce");
            double airJump = Norm(p, cr, "airJumpForce");
            double mass = Norm(p, cr, "mass");
            double width = Norm(p, cr, "widthScalar");
            double height = Norm(p, cr, "heightScalar");
            double gravity = Norm(p, cr, "gravityScalar");

            f.Set("speed", (groundSpeed + airSpeed) / 2);
            f.Set("accel", (groundAccel + airAccel) / 2);
            f.Set("jump", (groundJump + airJump) / 2);
            f.Set("mass", mass);
            f.Set("girth", width);
            f.Set("stature", height);
            f.Set("bulk", (mass + width + height) / 3);
            f.Set("gravity", gravity);
            // Floatiness: air mobility against gravity, recentered to 0.5-neutral.
            f.Set("floatiness", Clamp01(0.5 + (airJump - gravity) / 2));
            f.Set("aerialAffinity", Clamp01(0.5 + ((airSpeed + airJump) / 2 - (groundSpeed + groundJump) / 2) / 2));
            f.Set("fragility", Norm(p, cr, "hitstunDamageScalar"));
            f.Set("fastfall", Norm(p, cr, "fastFallAcceleration"));
            f.Set("lowStance", Clamp01(0.5 + ((1 - Norm(p, cr, "crouchHeightRatio")) - 0.5 + Norm(p, cr, "crouchMoveSpeed") - 0.5) / 2));
            f.SetFlag("hasDI", Present(p, cr, "directionalInfluence"));

            ExtractMoveset(g.Moves, f);
            return f;
        }

        private void ExtractMoveset(IReadOnlyList<MoveGenome> moves, FeatureVector f)
        {
            var melee = moves.Where(m => m.Kind == MoveKind.Melee).ToList();
            var mr = _ranges.Move;

            if (melee.Count > 0)
            {
                f.Set("peakDamage", melee.Max(m => Norm(m.Params, mr, "damageFactor")));
                f.Set("peakKnockback", melee.Max(m => Norm(m.Params, mr, "knockbackScalar")));
                f.Set("stun", melee.Max(m => Norm(m.Params, mr, "hitstunDuration")));
                f.Set("reach", melee.Max(m => Norm(m.Params, mr, "moveDist")));
                // Commitment: recovery + windup relative to active time. High = honest, heavy swings.
                f.Set("commitment", melee.Average(m =>
                    (Norm(m.Params, mr, "warmUpDuration") + Norm(m.Params, mr, "coolDownDuration")) / 2));
                // Launch direction from knockbackModY sign (generation range differs per axis; use raw sign).
                int launchers = melee.Count(m => m.Params.TryGetValue("knockbackModY", out var y) && y > 0.3f);
                int spikers = melee.Count(m => m.Params.TryGetValue("knockbackModY", out var y) && y < -0.3f);
                f.Set("launcher", (double)launchers / melee.Count);
                f.Set("spiker", (double)spikers / melee.Count);
            }

            var shield = moves.FirstOrDefault(m => m.Kind == MoveKind.Shield);
            f.SetFlag("hasShield", shield != null);
            if (shield != null)
            {
                var sr = _ranges.Shield;
                f.Set("shieldiness", (Norm(shield.Params, sr, "initialSize")
                    + Norm(shield.Params, sr, "regenRate")
                    + (1 - Norm(shield.Params, sr, "holdDegradationRate"))) / 3);
                f.SetFlag("hasShieldReflect", Flag(shield.Params, "reflect"));
            }

            var dash = moves.FirstOrDefault(m => m.Kind == MoveKind.Dash);
            f.SetFlag("hasDash", dash != null);
            if (dash != null)
            {
                var dr = _ranges.Dash;
                f.Set("dashSpeed", Norm(dash.Params, dr, "acceleration"));
                f.SetFlag("hasDashInvuln", Flag(dash.Params, "warmUpInvulnerable") || Flag(dash.Params, "durationInvulnerable"));
                f.SetFlag("hasDashReflect", Flag(dash.Params, "reflect"));
            }

            var projectiles = moves.Where(m => m.Kind == MoveKind.Projectile).ToList();
            f.SetFlag("hasProjectile", projectiles.Count > 0);
            if (projectiles.Count > 0)
            {
                var pr = _ranges.Projectile;
                f.Set("projSpeed", projectiles.Max(m => Norm(m.Params, pr, "velocity")));
                f.Set("projDamage", projectiles.Max(m => Norm(m.Params, pr, "damageFactor")));
                // Path shape is an int-as-float: 0 linear, 1 sine, 2 quadratic.
                f.SetFlag("projLinear", projectiles.Any(m => IntOf(m.Params, "pathShape") == 0));
                f.SetFlag("projSine", projectiles.Any(m => IntOf(m.Params, "pathShape") == 1));
                f.SetFlag("projQuad", projectiles.Any(m => IntOf(m.Params, "pathShape") == 2));
                f.SetFlag("projSpins", projectiles.Any(m => Flag(m.Params, "doesRotate")));
                f.SetFlag("projSelfHarm", projectiles.Any(m => Flag(m.Params, "hitsSelf")));
            }

            f.SetFlag("hasReflect", (f.Get("hasShieldReflect") >= 1.0) || (f.Get("hasDashReflect") >= 1.0));
        }

        // ---- stage ----

        public FeatureVector ExtractStage(StageGenome g)
        {
            var f = new FeatureVector();
            var p = g.Params;
            var sr = _ranges.Stage;

            double w = Norm(p, sr, "visibleHalfWidth");
            double h = Norm(p, sr, "visibleHalfHeight");
            f.Set("vastness", (w + h) / 2);
            f.Set("verticality", Clamp01(0.5 + (h - w) / 2));
            f.Set("lethality", 1 - Norm(p, sr, "koMarginFraction"));
            f.Set("density", Norm(p, sr, "platformCount"));
            f.Set("platformScale", Norm(p, sr, "maxPlatformSize"));
            f.SetFlag("twin", Flag(p, "mirrored"));
            f.SetFlag("shifting", Present(p, sr, "platformSpawnDuration"));
            f.SetFlag("sanctuary", Present(p, sr, "spawnInvulnDuration"));
            return f;
        }

        private static double Clamp01(double v) => Math.Max(0.0, Math.Min(1.0, v));
    }
}
