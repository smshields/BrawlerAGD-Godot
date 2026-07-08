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
}
