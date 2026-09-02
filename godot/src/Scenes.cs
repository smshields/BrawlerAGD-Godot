namespace BrawlerGodot;

/// <summary>The res:// paths of the app's scenes, previously string literals at
/// every ChangeSceneToFile call. The .tscn files themselves reference scripts by
/// res:// path — renaming a scene means updating exactly one constant here.</summary>
public static class Scenes
{
    public const string Arena = "res://scenes/arena.tscn";
    public const string CharacterSelect = "res://scenes/character_select.tscn";
    public const string Credits = "res://scenes/credits.tscn";
    public const string Evolve = "res://scenes/evolve.tscn";
    public const string GameBuilder = "res://scenes/game_builder.tscn";
    public const string GameSelect = "res://scenes/game_select.tscn";
    public const string MainMenu = "res://scenes/main_menu.tscn";
    public const string Manage = "res://scenes/manage.tscn";
    public const string Title = "res://scenes/title.tscn";

    /// <summary>BRAWLER_SCENE automation: scene path from a bare scene name.</summary>
    public static string ForName(string name) => $"res://scenes/{name}.tscn";
}
