using Godot;
using BrawlerSim.Serialization;

namespace BrawlerGodot;

/// <summary>
/// Shared game-library UI rituals: the FAVORITES / DEMO GAMES directory sections
/// used by the main menu's game picker and the builder's source browser, the
/// completion badge string, and the identically-configured *.json file browsers.
/// </summary>
public static class GameLibraryUi
{
    /// <summary>Lists a directory's *.json games as left-aligned upper-invariant
    /// buttons under a heading. A missing or empty directory adds nothing.
    /// Returns the file count (0 when the section was skipped).</summary>
    public static int AddSection(VBoxContainer list, string heading, string dir, System.Action<string> onPick)
    {
        if (!System.IO.Directory.Exists(dir))
        {
            return 0;
        }
        string[] files = System.IO.Directory.GetFiles(dir, "*.json");
        System.Array.Sort(files);
        if (files.Length == 0)
        {
            return 0;
        }
        list.AddChild(UiWidgets.Heading(heading));
        foreach (string file in files)
        {
            string path = file;
            var button = new Button
            {
                Text = System.IO.Path.GetFileNameWithoutExtension(file).ToUpperInvariant(),
                Alignment = HorizontalAlignment.Left,
            };
            button.Pressed += () => onPick(path);
            list.AddChild(button);
        }
        return files.Length;
    }

    /// <summary>The "x/8 · y/4" roster-progress string (builder library badges and
    /// the game select's IN PROGRESS rows).</summary>
    public static string CompletionBadge(BuiltGame game)
        => $"{game.Characters.Count}/{BuiltGame.RequiredCharacters} · "
           + $"{game.Stages.Count}/{BuiltGame.RequiredStages}";

    /// <summary>The evolved-game *.json file browser — opens where evolution runs
    /// and imported games live. The caller adds it to the tree.</summary>
    public static FileDialog JsonBrowser(System.Action<string> onSelected, string? title = null, string filter = "*.json")
    {
        var dialog = new FileDialog
        {
            Access = FileDialog.AccessEnum.Filesystem,
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Filters = new[] { filter },
            CurrentDir = AppPaths.RunsRoot(),
        };
        if (title is not null)
        {
            dialog.Title = title;
        }
        dialog.FileSelected += path => onSelected(path);
        return dialog;
    }
}
