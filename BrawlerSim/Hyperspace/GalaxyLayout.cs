using BrawlerSim.Determinism;
using BrawlerSim.Evolution;

namespace BrawlerSim.Hyperspace;

/// <summary>A point in the galaxy view's world space (engine-free — the Godot layer
/// converts to Vector3 at the boundary).</summary>
public readonly record struct GalaxyPoint(float X, float Y, float Z)
{
    public static GalaxyPoint operator +(GalaxyPoint a, GalaxyPoint b) =>
        new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static GalaxyPoint operator *(GalaxyPoint a, float s) => new(a.X * s, a.Y * s, a.Z * s);

    public float Length => MathF.Sqrt(X * X + Y * Y + Z * Z);

    public GalaxyPoint Normalized()
    {
        float len = Length;
        return len <= 1e-6f ? new GalaxyPoint(0f, 0f, 0f) : new GalaxyPoint(X / len, Y / len, Z / len);
    }

    public static GalaxyPoint Cross(GalaxyPoint a, GalaxyPoint b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);
}

/// <summary>An RGB colour in [0,1] (engine-free).</summary>
public readonly record struct GalaxyColor(float R, float G, float B);

/// <summary>One planet's orbit: a true ellipse with the star at a FOCUS, in an
/// arbitrarily oriented plane. `Plane*` are the orthonormal basis vectors spanning
/// the orbital plane, already rotated by the argument of periapsis.</summary>
public readonly record struct OrbitElements(
    float SemiMajor, float Eccentricity, GalaxyPoint PlaneU, GalaxyPoint PlaneV,
    float Phase, float AngularRate, float BodyRadius)
{
    public float Apoapsis => SemiMajor * (1f + Eccentricity);
    public float Periapsis => SemiMajor * (1f - Eccentricity);
}

/// <summary>
/// The galaxy view's layout math (2026-09-16, docs/features/galaxy-view.md §3):
/// where a cell's star sits, how big and what colour it is, and where its planets
/// are at time t. Every value is a pure function of (cell, fitness, hash, clock) via
/// <see cref="Fnv1a"/> — two clients rendering the same snapshot see the identical
/// universe, and none of it touches engine RNG.
///
/// SCALE (designer, 2026-09-16): the world is DOUBLE the PoC's, and the ship's
/// speeds are doubled with it, so every traversal TIME is unchanged (a galaxy
/// crossing is still ~13.5 s at cruise) while systems get room for many planets.
/// See <see cref="GalaxyNavigation"/> for the speeds that pair with these constants.
///
/// THE CONTAINMENT INVARIANT (designer, 2026-09-17, superseding the 09-16
/// separation rule): a system — star, orbits, planet bodies — stays entirely inside
/// its OWN cell. Containment implies non-intersection with every neighbour for free,
/// and it frees the star to sit ANYWHERE in its bucket rather than hugging the
/// centre: placement is hashed UNIFORM within the cell, inset by
/// <see cref="PlacementInset"/> so the worst-case envelope cannot reach a wall.
///
///     |offset from cell centre| + MaxSystemRadius + WallMargin  <=  S / 2
///
/// The envelope stays capped at <see cref="MaxSystemRadius"/> independent of the
/// planet count, so K remains a free knob; orbit separation comes from randomized
/// PLANE ORIENTATION and eccentricity, not radial spacing. The inset uses the
/// MAXIMUM envelope, never the actual one — a star's position must be a pure
/// function of its cell alone, or a snapshot that adds a member would MOVE the star
/// (§8.1: rebuilds never move anything). Any change to S, MaxSystemRadius, or the
/// radius formulas must re-check containment — GalaxyLayoutTests pins it.
/// </summary>
public static class GalaxyLayout
{
    /// <summary>World units per descriptor bin (PoC 44 doubled — see SCALE above).</summary>
    public const float S = 88f;

    public const int Bins = DescriptorBins.BinsPerAxis;

    /// <summary>Galaxy edge: 8 bins.</summary>
    public const float Extent = Bins * S;          // 704

    public const float Half = Extent / 2f;         // 352

    /// <summary>Galaxy centre spacing along +X — the Timing lane (5 x Extent).</summary>
    public const float Gap = 5f * Extent;          // 3520

    /// <summary>Inside this radius a galaxy is "here", not a distant target.</summary>
    public const float NearGalaxy = Half * 2f;     // 704

    /// <summary>Camera distance within which orbits and planets render and are
    /// clickable. Scales with S by construction.</summary>
    public const float PlanetRange = 7f * S;       // 616

    /// <summary>Clear space kept between a worst-case system and its cell wall, so
    /// two neighbours' orbits cannot even touch across the boundary.</summary>
    public const float WallMargin = 1f;

    /// <summary>Numerator of the star distance fade — scales with S.</summary>
    public const float FadeNumerator = 11f * S;    // 968

    /// <summary>Hard cap on how far ANY part of a system reaches from its star,
    /// planet bodies included. The invariant's only geometric input.</summary>
    public const float MaxSystemRadius = 0.17f * S; // 14.96

    /// <summary>How far a star's centre must stay from its cell walls: the largest
    /// envelope any system can have, plus the wall margin. The placement range per
    /// axis is +/- (S/2 - PlacementInset) around the cell centre.</summary>
    public static float PlacementInset => MaxSystemRadius + WallMargin;

    /// <summary>Largest eccentricity a planet's orbit may take.</summary>
    public const float MaxEccentricity = 0.35f;

    /// <summary>Slack kept between the outermost body and the envelope cap, so the
    /// invariant holds STRICTLY rather than to within float rounding.</summary>
    public const float EnvelopeMargin = 0.01f;

    /// <summary>Star radius from normalized fitness (world units). Deliberately NOT
    /// scaled with S — sparsity is the point.</summary>
    public static float StarRadius(float fitness) => 0.5f + 1.9f * Clamp01(fitness);

    /// <summary>Planet radius from the MEMBER's own normalized fitness (designer,
    /// 2026-09-16 — the spec's screen-space-only size carried no data). The 0.42
    /// factor keeps a planet strictly smaller than its star at every fitness: the
    /// elite is its cell's maximum by construction, so f_member &lt;= f_star.</summary>
    public static float PlanetRadius(float memberFitness) => 0.42f * StarRadius(memberFitness);

    /// <summary>Largest a planet body can ever be — an input to the orbit budget.</summary>
    public static float MaxPlanetRadius => PlanetRadius(1f);

    public static GalaxyPoint GalaxyCenter(int g) => new((g - 3.5f) * Gap, 0f, 0f);

    /// <summary>Hash of a cell, the seed for every per-star derived value.</summary>
    public static ulong CellHash(int i, int j, int k, int g)
    {
        ulong hash = Fnv1a.Add(Fnv1a.OffsetBasis, i);
        hash = Fnv1a.Add(hash, j);
        hash = Fnv1a.Add(hash, k);
        return Fnv1a.Add(hash, g);
    }

    /// <summary>Byte n of a hash as a unit float (the PoC's u(n)).</summary>
    public static float HashUnit(ulong hash, int b) => ((hash >> (b * 8)) & 0xFF) / 255f;

    /// <summary>
    /// Star world position: hashed UNIFORM placement anywhere in the cell that the
    /// worst-case system envelope still fits (designer 2026-09-17 — "randomly placed
    /// inside the bucket"; the earlier triangular jitter hugged the centre). Two hash
    /// bytes per axis, so placement resolves finer than a byte grid.
    /// </summary>
    public static GalaxyPoint StarPosition(int i, int j, int k, int g)
    {
        ulong hash = CellHash(i, j, k, g);
        GalaxyPoint center = GalaxyCenter(g);
        return new GalaxyPoint(
            center.X + (i - 3.5f) * S + PlacementOffset(hash, 0, 1),
            center.Y + (j - 3.5f) * S + PlacementOffset(hash, 2, 3),
            center.Z + (k - 3.5f) * S + PlacementOffset(hash, 4, 5));
    }

    private static float PlacementOffset(ulong hash, int byteA, int byteB)
    {
        float u = (HashUnit(hash, byteA) * 255f + HashUnit(hash, byteB)) / 256f;
        return (u - 0.5f) * 2f * (S / 2f - PlacementInset);
    }

    /// <summary>
    /// Star colour: bins pick the hue, fitness the saturation and luminosity.
    /// R = move variety, G = character asymmetry, B = platform ratio; the fourth
    /// descriptor (timing) is the galaxy lane, so it has no colour channel.
    /// </summary>
    public static GalaxyColor StarColor(int i, int j, int k, float fitness)
    {
        float f = Clamp01(fitness);
        (float h, float s, _) = RgbToHsl(i / 7f, j / 7f, k / 7f);
        return HslToRgb(h, s * (0.25f + 0.75f * f), 0.28f + 0.44f * f);
    }

    /// <summary>
    /// Planet body colour: a habitability ramp from red (hostile — low fitness) to
    /// blue-green (habitable — high fitness), by the member's own NORMALIZED fitness
    /// (designer, 2026-09-17). This replaces the earlier star-derived tint: the orbit
    /// RING carries system membership (it is drawn in the star's colour), which frees
    /// the body to carry data.
    /// </summary>
    public static GalaxyColor PlanetColor(float normalizedFitness)
    {
        float f = Clamp01(normalizedFitness);
        // Hue 0 (red) sweeping to 0.44 (blue-green); habitable worlds sit a little
        // brighter, so the ramp reads in size-limited dots too.
        return HslToRgb(0.44f * f, 0.78f, 0.40f + 0.18f * f);
    }

    /// <summary>Per-instance star alpha by camera distance. Floored ABOVE zero — there
    /// is deliberately no far cull for stars (§8.1).</summary>
    public static float StarFade(float distance, float fitness) =>
        Math.Clamp(FadeNumerator / MathF.Max(distance, 1e-3f), 0.10f, 1f)
        * (0.35f + 0.62f * Clamp01(fitness));

    /// <summary>
    /// The outermost semi-major axis a system may use, given how big its planets get.
    /// Solved from the envelope cap: apoapsis + body radius &lt;= MaxSystemRadius.
    /// </summary>
    public static float MaxSemiMajor(float largestPlanetRadius) =>
        (MaxSystemRadius - largestPlanetRadius - EnvelopeMargin) / (1f + MaxEccentricity);

    /// <summary>
    /// Orbit elements for member `n` of `count` around a star of radius `starRadius`.
    /// Plane normal, argument of periapsis, eccentricity and a +/-15% radial jitter
    /// all come from the member's own hash, so two planets at similar radii still
    /// read as separate rings — orientation does the work radial spacing used to.
    /// </summary>
    public static OrbitElements Orbit(ulong starHash, int n, int count, float starRadius,
        float memberFitness)
    {
        ulong h = starHash ^ (ulong)unchecked((uint)((n + 1) * 0x9E3779B9));
        h = Fnv1a.Add(h, n);

        // Orbital plane: hashed normal, then an orthonormal basis inside the plane.
        float azimuth = ((h & 1023UL) / 1023f) * MathF.Tau;
        float elevation = ((((h >> 10) & 1023UL) / 1023f) - 0.5f) * MathF.PI;
        float ce = MathF.Cos(elevation);
        var normal = new GalaxyPoint(ce * MathF.Cos(azimuth), MathF.Sin(elevation), ce * MathF.Sin(azimuth));
        var up = new GalaxyPoint(0f, 1f, 0f);
        GalaxyPoint u = GalaxyPoint.Cross(normal, up);
        if (u.Length < 1e-4f)
        {
            u = GalaxyPoint.Cross(normal, new GalaxyPoint(1f, 0f, 0f)); // degenerate: pole-aligned
        }
        u = u.Normalized();
        GalaxyPoint v = GalaxyPoint.Cross(normal, u).Normalized();

        // Argument of periapsis: rotate the basis inside the plane so ellipses do not
        // all point the same way.
        float argument = (((h >> 20) & 1023UL) / 1023f) * MathF.Tau;
        float ca = MathF.Cos(argument), sa = MathF.Sin(argument);
        GalaxyPoint pu = u * ca + v * sa;
        GalaxyPoint pv = v * ca + u * -sa;

        float eccentricity = (((h >> 30) & 255UL) / 255f) * MaxEccentricity;
        float bodyRadius = PlanetRadius(memberFitness);

        // Radial ladder across [2 x star radius, the envelope budget], evenly spread
        // with a hashed +/-15% wobble, then clamped so apoapsis + body fits.
        float inner = 2f * starRadius;
        float outer = MathF.Max(inner, MaxSemiMajor(MaxPlanetRadius));
        float t = count <= 1 ? 0.5f : (n + 0.5f) / count;
        float wobble = 1f + ((((h >> 38) & 255UL) / 255f) - 0.5f) * 0.30f;
        float semiMajor = (inner + (outer - inner) * t) * wobble;
        float ceiling = (MaxSystemRadius - bodyRadius - EnvelopeMargin) / (1f + eccentricity);
        semiMajor = Math.Clamp(semiMajor, MathF.Min(inner, ceiling), ceiling);

        float phase = (((h >> 46) & 1023UL) / 1023f) * MathF.Tau;
        float rate = 0.25f + 0.18f * n;
        return new OrbitElements(semiMajor, eccentricity, pu, pv, phase, rate, bodyRadius);
    }

    /// <summary>
    /// Planet position at time t. Mean anomaly advances uniformly and is solved to
    /// the eccentric anomaly by a FIXED four-iteration Newton step — deterministic
    /// and branch-free — so the planet genuinely slows at apoapsis instead of
    /// sweeping a circle at constant speed.
    /// </summary>
    public static GalaxyPoint PlanetPosition(GalaxyPoint star, in OrbitElements orbit, float t)
    {
        float meanAnomaly = orbit.Phase + t * orbit.AngularRate;
        float e = orbit.Eccentricity;
        float eccentric = meanAnomaly;
        for (int iteration = 0; iteration < 4; iteration++)
        {
            float f = eccentric - e * MathF.Sin(eccentric) - meanAnomaly;
            eccentric -= f / MathF.Max(1e-4f, 1f - e * MathF.Cos(eccentric));
        }
        // Ellipse in its own plane, star at the focus.
        float a = orbit.SemiMajor;
        float b = a * MathF.Sqrt(MathF.Max(0f, 1f - e * e));
        float x = a * (MathF.Cos(eccentric) - e);
        float y = b * MathF.Sin(eccentric);
        return star + orbit.PlaneU * x + orbit.PlaneV * y;
    }

    /// <summary>The system's true envelope: the farthest any of its bodies reaches
    /// from the star. What the separation invariant is asserted against.</summary>
    public static float SystemRadius(ulong starHash, int count, float starRadius,
        IReadOnlyList<float> memberFitness)
    {
        float radius = starRadius;
        for (int n = 0; n < count; n++)
        {
            OrbitElements orbit = Orbit(starHash, n, count, starRadius, memberFitness[n]);
            radius = MathF.Max(radius, orbit.Apoapsis + orbit.BodyRadius);
        }
        return radius;
    }

    private static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);

    internal static (float H, float S, float L) RgbToHsl(float r, float g, float b)
    {
        float max = MathF.Max(r, MathF.Max(g, b)), min = MathF.Min(r, MathF.Min(g, b));
        float l = (max + min) / 2f;
        float d = max - min;
        if (d <= 1e-6f)
        {
            return (0f, 0f, l);
        }
        float s = l > 0.5f ? d / (2f - max - min) : d / (max + min);
        float h;
        if (max == r)
        {
            h = (g - b) / d + (g < b ? 6f : 0f);
        }
        else if (max == g)
        {
            h = (b - r) / d + 2f;
        }
        else
        {
            h = (r - g) / d + 4f;
        }
        return (h / 6f, s, l);
    }

    internal static GalaxyColor HslToRgb(float h, float s, float l)
    {
        if (s <= 1e-6f)
        {
            return new GalaxyColor(l, l, l);
        }
        float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
        float p = 2f * l - q;
        return new GalaxyColor(HueToChannel(p, q, h + 1f / 3f), HueToChannel(p, q, h),
            HueToChannel(p, q, h - 1f / 3f));
    }

    private static float HueToChannel(float p, float q, float t)
    {
        if (t < 0f) t += 1f;
        if (t > 1f) t -= 1f;
        if (t < 1f / 6f) return p + (q - p) * 6f * t;
        if (t < 1f / 2f) return q;
        if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
        return p;
    }
}
