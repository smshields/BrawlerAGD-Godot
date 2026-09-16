using Godot;
using BrawlerSim.Hyperspace;

namespace BrawlerGodot.Hyperspace;

public enum GalaxyTargetKind { Star, Planet, Galaxy }

/// <summary>What the crosshair or the cursor is on. Planets are tracked live along
/// their orbits, so a target is resolved to a position per frame, never cached.</summary>
public sealed record GalaxyTarget(
    GalaxyTargetKind Kind, GalaxyStar? Star, GalaxyPlanet? Planet, int Galaxy)
{
    public static GalaxyTarget OfStar(GalaxyStar star) => new(GalaxyTargetKind.Star, star, null, star.G);

    public static GalaxyTarget OfPlanet(GalaxyPlanet planet) =>
        new(GalaxyTargetKind.Planet, planet.Star, planet, planet.Star.G);

    public static GalaxyTarget OfGalaxy(int g) => new(GalaxyTargetKind.Galaxy, null, null, g);

    /// <summary>The archive entry behind this target — a star's elite or a planet's
    /// member. Galaxies have none.</summary>
    public HyperspaceEntry? Entry => Kind switch
    {
        GalaxyTargetKind.Star => Star!.Entry,
        GalaxyTargetKind.Planet => Planet!.Entry,
        _ => null,
    };

    public bool SameAs(GalaxyTarget? other) =>
        other is not null && other.Kind == Kind && other.Galaxy == Galaxy
        && ReferenceEquals(other.Star, Star) && ReferenceEquals(other.Planet, Planet);
}

/// <summary>
/// Hover and click resolution (galaxy-view.md §5). All picking is screen-space math
/// against projected positions — no physics bodies, because planets move and stars
/// are numerous, and screen-space picking is what the PoC validated.
///
/// HOVER is measured from the CROSSHAIR (you aim with the ship) and never includes
/// planets; CLICK is measured from the cursor and prefers a planet over its star.
/// World-space reaches are the PoC's, doubled with the world; pixel radii are not —
/// screen space did not change.
/// </summary>
public sealed class GalaxyTargeting
{
    private const float HoverStarPixels = 55f;
    private const float HoverGalaxyPixels = 46f;
    private const float PickPlanetPixels = 13f;
    private const float PickStarPixels = 26f;
    private const float PickGalaxyPixels = 46f;

    private readonly Camera3D _camera;
    private readonly GalaxyStarField _stars;
    private readonly GalaxyPlanets _planets;

    public GalaxyTargeting(Camera3D camera, GalaxyStarField stars, GalaxyPlanets planets)
    {
        _camera = camera;
        _stars = stars;
        _planets = planets;
    }

    /// <summary>Screen position of a world point, or null when it is behind the
    /// camera (unprojecting a point behind the eye yields a mirrored ghost).</summary>
    public Vector2? Project(Vector3 world) =>
        _camera.IsPositionBehind(world) ? null : _camera.UnprojectPosition(world);

    public Vector3 PositionOf(GalaxyTarget target, float clock) => target.Kind switch
    {
        GalaxyTargetKind.Star => target.Star!.Position,
        GalaxyTargetKind.Planet => target.Planet!.PositionAt(clock),
        _ => GalaxyVec.From(GalaxyLayout.GalaxyCenter(target.Galaxy)),
    };

    /// <summary>Apparent radius in pixels — sizes the lock reticle and the hover ring.</summary>
    public float ScreenRadius(GalaxyTarget target, float clock)
    {
        Vector3 world = PositionOf(target, clock);
        if (Project(world) is not { } center)
        {
            return 0f;
        }
        float worldRadius = target.Kind switch
        {
            GalaxyTargetKind.Star => target.Star!.Radius * 2.2f,
            GalaxyTargetKind.Planet => Mathf.Max(target.Planet!.Orbit.BodyRadius * 3f, 1.5f),
            _ => GalaxyLayout.Half,
        };
        Vector3 offset = _camera.GlobalBasis.X * worldRadius;
        return Project(world + offset) is { } edge
            ? Mathf.Max(8f, center.DistanceTo(edge))
            : 12f;
    }

    /// <summary>What the crosshair is on: the nearest star, else a distant galaxy.
    /// Hover drives the readout and a thin ring; it never rotates the ship.</summary>
    public GalaxyTarget? Hover(Vector2 crosshair, Vector3 eye)
    {
        GalaxyStar? best = null;
        float bestDistance = HoverStarPixels;
        for (int g = 0; g < GalaxyLayout.Bins; g++)
        {
            foreach (GalaxyStar star in _stars.StarsIn(g))
            {
                if (eye.DistanceTo(star.Position) > GalaxyNavigation.TargetingRange
                    || Project(star.Position) is not { } screen)
                {
                    continue;
                }
                float d = screen.DistanceTo(crosshair);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = star;
                }
            }
        }
        if (best is not null)
        {
            return GalaxyTarget.OfStar(best);
        }
        return DistantGalaxyAt(crosshair, eye, HoverGalaxyPixels);
    }

    /// <summary>
    /// Click resolution, in priority order: planet, then star, then distant galaxy.
    /// Null means empty space, which releases any lock.
    /// </summary>
    public GalaxyTarget? Pick(Vector2 cursor, Vector3 eye, float clock)
    {
        GalaxyPlanet? planet = null;
        float bestPlanet = PickPlanetPixels;
        foreach ((GalaxyPlanet candidate, Vector3 position, float alpha) in _planets.Visible)
        {
            // Only what is actually drawn can be clicked — the fade band owns both.
            if (alpha <= 0.02f || Project(position) is not { } screen)
            {
                continue;
            }
            float d = screen.DistanceTo(cursor);
            if (d < bestPlanet)
            {
                bestPlanet = d;
                planet = candidate;
            }
        }
        if (planet is not null)
        {
            return GalaxyTarget.OfPlanet(planet);
        }

        GalaxyStar? star = null;
        float bestStar = PickStarPixels;
        for (int g = 0; g < GalaxyLayout.Bins; g++)
        {
            foreach (GalaxyStar candidate in _stars.StarsIn(g))
            {
                if (eye.DistanceTo(candidate.Position) > GalaxyNavigation.TargetingRange
                    || Project(candidate.Position) is not { } screen)
                {
                    continue;
                }
                float d = screen.DistanceTo(cursor);
                if (d < bestStar)
                {
                    bestStar = d;
                    star = candidate;
                }
            }
        }
        return star is not null
            ? GalaxyTarget.OfStar(star)
            : DistantGalaxyAt(cursor, eye, PickGalaxyPixels);
    }

    /// <summary>A galaxy only counts as a target from outside it — inside, its stars
    /// are what you are pointing at.</summary>
    private GalaxyTarget? DistantGalaxyAt(Vector2 point, Vector3 eye, float pixels)
    {
        for (int g = 0; g < GalaxyLayout.Bins; g++)
        {
            Vector3 center = GalaxyVec.From(GalaxyLayout.GalaxyCenter(g));
            if (eye.DistanceTo(center) < GalaxyLayout.NearGalaxy
                || Project(center) is not { } screen)
            {
                continue;
            }
            if (screen.DistanceTo(point) < pixels)
            {
                return GalaxyTarget.OfGalaxy(g);
            }
        }
        return null;
    }

    /// <summary>Human-readable target name for the reticle and the readout.</summary>
    public static string Describe(GalaxyTarget target) => target.Kind switch
    {
        GalaxyTargetKind.Star => $"STAR {target.Star!.I}-{target.Star.J}-{target.Star.K}",
        GalaxyTargetKind.Planet =>
            $"PLANET {target.Star!.I}-{target.Star.J}-{target.Star.K}/{target.Planet!.Index + 1}",
        _ => $"GALAXY {target.Galaxy}",
    };
}
