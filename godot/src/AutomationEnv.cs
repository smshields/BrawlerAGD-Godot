using Godot;

namespace BrawlerGodot;

/// <summary>
/// THE automation surface: every BRAWLER_* environment variable the app reads for
/// headless-style development verification, in one place (previously raw
/// OS.GetEnvironment calls across nine files).
///
///   BRAWLER_AUTOPLAY = "ai:&lt;seed&gt;" | "replay" — jump straight into the arena.
///   BRAWLER_GAME     = path to game.json (defaults to a generated genome).
///   BRAWLER_TRACE    = path to trace.json (for replay mode).
///   BRAWLER_RULES    = "timed:&lt;seconds&gt;" — run the automated match under the
///                      TIMED rule (KO counter + clock verification).
///   BRAWLER_SHOT     = single screenshot path: with autoplay/autoevolve those flows
///                      capture it themselves; otherwise Boot saves whatever scene is
///                      up after a second and quits.
///   BRAWLER_SHOT_AT  = seconds; capture BRAWLER_SHOT mid-flight instead, whatever
///                      the scene is doing (animated screens).
///   BRAWLER_SHOT_DIR + BRAWLER_SHOT_TICKS = "60,300,..." — ArenaView saves
///                      screenshots at those sim ticks and quits after the last.
///   BRAWLER_TICKS_PER_FRAME = sim fast-forward for captures.
///   BRAWLER_PAUSE_AT = open the pause menu at a sim tick (+ shoot "paused").
///   BRAWLER_QUIT_AFTER = seconds; fail-safe quit so no stray window survives.
///   BRAWLER_SCENE    = "evolve" | "manage" | ... — jump to a scene on boot.
///   BRAWLER_TITLE    = "play" | "credits" | "settings" — title-screen routing
///                      (also forces standalone routing in the editor).
///   BRAWLER_PICKER   = "1" — open the game picker on main-menu load.
///   BRAWLER_AUTOOPEN = "1" — game select opens the first complete game.
///   BRAWLER_AUTOBUILD = "1" — game builder assembles a sample game on load.
///   BRAWLER_AUTOSELECT = "p1=0;cpu2=3;..." — arrange a character-select lobby
///                      (parsed by CharacterSelectView.ApplyAutoSelect).
///   BRAWLER_AUTOEVOLVE = "name=x;pop=24;..." — start an evolve run on load
///                      (parsed by EvolveView.ApplyAutoConfig).
///   BRAWLER_GALAXY_CAM = "x,y,z[,yaw,pitch]" — park the GALAXY tab's ship for a
///                      capture (the view has no other headless way to be looked
///                      at from a chosen spot).
///   BRAWLER_GALAXY_FLY = "seconds[,boost]" — fly the ship straight ahead for that
///                      long before capturing: the §8.1 pop-in acceptance pass.
/// </summary>
public static class AutomationEnv
{
    public static string Autoplay => OS.GetEnvironment("BRAWLER_AUTOPLAY");
    public static string Game => OS.GetEnvironment("BRAWLER_GAME");
    public static string Trace => OS.GetEnvironment("BRAWLER_TRACE");
    public static string Rules => OS.GetEnvironment("BRAWLER_RULES");
    public static string Shot => OS.GetEnvironment("BRAWLER_SHOT");
    /// <summary>BRAWLER_SHOT_AT=&lt;seconds&gt;: capture BRAWLER_SHOT that many seconds
    /// after the scene loads and quit, whatever the scene is doing. The only way to
    /// see a mid-flight animated screen (a live evolve chart, a fly-through) headlessly
    /// — the ordinary shot paths fire at a flow's end.</summary>
    public static string ShotAt => OS.GetEnvironment("BRAWLER_SHOT_AT");
    public static string ShotDir => OS.GetEnvironment("BRAWLER_SHOT_DIR");
    public static string ShotTicks => OS.GetEnvironment("BRAWLER_SHOT_TICKS");
    public static string TicksPerFrame => OS.GetEnvironment("BRAWLER_TICKS_PER_FRAME");
    public static string PauseAt => OS.GetEnvironment("BRAWLER_PAUSE_AT");
    public static string QuitAfter => OS.GetEnvironment("BRAWLER_QUIT_AFTER");
    public static string Scene => OS.GetEnvironment("BRAWLER_SCENE");
    public static string Title => OS.GetEnvironment("BRAWLER_TITLE");
    public static bool Picker => OS.GetEnvironment("BRAWLER_PICKER") == "1";
    /// <summary>BRAWLER_FLYOUT=play|build|evolve|settings: open that main-menu flyout
    /// on load (2026-09-10 menu rework screenshots).</summary>
    public static string Flyout => OS.GetEnvironment("BRAWLER_FLYOUT");
    public static bool AutoOpen => OS.GetEnvironment("BRAWLER_AUTOOPEN") == "1";
    public static bool AutoBuild => OS.GetEnvironment("BRAWLER_AUTOBUILD") == "1";
    public static string AutoSelect => OS.GetEnvironment("BRAWLER_AUTOSELECT");
    public static string AutoEvolve => OS.GetEnvironment("BRAWLER_AUTOEVOLVE");
    public static string GalaxyCam => OS.GetEnvironment("BRAWLER_GALAXY_CAM");
    public static string GalaxyFly => OS.GetEnvironment("BRAWLER_GALAXY_FLY");
    /// <summary>BRAWLER_GALAXY_LOCK="hover": lock whatever the crosshair is on once
    /// the archive lands, so a capture can show the reticle and cell highlight.</summary>
    public static string GalaxyLock => OS.GetEnvironment("BRAWLER_GALAXY_LOCK");
    /// <summary>BRAWLER_GALAXY_TAB_AT=&lt;seconds&gt;: switch the Evolve screen to the
    /// GALAXY tab that long after load — the headless stand-in for a person clicking
    /// into the tab mid-run, which is a different code path (deferred snapshot,
    /// late layout) from opening it at boot with tab=galaxy.</summary>
    public static string GalaxyTabAt => OS.GetEnvironment("BRAWLER_GALAXY_TAB_AT");
    /// <summary>BRAWLER_GALAXY_GRID="cube": open the galaxy view on the cube grid
    /// instead of the radial default (2026-09-17: radial was promoted to default
    /// after the designer flew it).</summary>
    public static string GalaxyGrid => OS.GetEnvironment("BRAWLER_GALAXY_GRID");
    /// <summary>BRAWLER_GALAXY_WARP="1": warp to the auto-locked target and run the
    /// ease to completion, so a capture shows the ARRIVAL.</summary>
    public static string GalaxyWarp => OS.GetEnvironment("BRAWLER_GALAXY_WARP");
}
