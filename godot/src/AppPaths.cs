using Godot;

namespace BrawlerGodot;

/// <summary>Where the app reads and writes run/game data.</summary>
public static class AppPaths
{
    /// <summary>
    /// The runs directory: `<repo>/runs` when running from the project (so the app and
    /// the CLI share one library), `user://runs` in exported builds.
    /// </summary>
    public static string RunsRoot()
    {
        string projectDir = ProjectSettings.GlobalizePath("res://");
        if (projectDir.Length > 0 && System.IO.Directory.Exists(projectDir))
        {
            string repoRuns = System.IO.Path.Combine(
                System.IO.Directory.GetParent(projectDir.TrimEnd('/'))!.FullName, "runs");
            System.IO.Directory.CreateDirectory(repoRuns);
            return repoRuns;
        }
        string userRuns = ProjectSettings.GlobalizePath("user://runs");
        System.IO.Directory.CreateDirectory(userRuns);
        return userRuns;
    }

    public static string ReplaysRoot() => ProjectSettings.GlobalizePath("user://replays");

    /// <summary>The favorites basket (Evolution Explorer, 2026-07-27): games saved
    /// via ADD TO GAMES, listed first by the game picker. Lives under the runs root
    /// so the CLI and exported builds see the same library.</summary>
    public static string FavoritesRoot()
    {
        string dir = System.IO.Path.Combine(RunsRoot(), "favorites");
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>The curated demo games (runs/demo, maintained per designer): listed
    /// by the game picker when present.</summary>
    public static string DemoRoot() => System.IO.Path.Combine(RunsRoot(), "demo");
}
