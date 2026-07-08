using Godot;
using BrawlerSim.Serialization;

namespace BrawlerGodot;

/// <summary>
/// Autoload. Registers input actions in code (readable + versionable, no serialized
/// project.godot blobs) and handles headless-style automation for development:
///   BRAWLER_AUTOPLAY = "ai:<seed>" | "replay"   → jump straight into the arena
///   BRAWLER_GAME     = path to game.json        (defaults to a generated genome)
///   BRAWLER_TRACE    = path to trace.json       (for replay mode)
///   BRAWLER_SHOT_DIR + BRAWLER_SHOT_TICKS="60,300,..." → ArenaView saves screenshots
///     at those sim ticks and quits after the last one.
/// Bindings mirror Unity: P1 = A/D + W jump + S attack (pad 0: stick/dpad, A/B jump,
/// X/Y attack); P2 = J/L + I jump + K attack (pad 1, same buttons).
/// </summary>
public partial class Boot : Node
{
    public override void _Ready()
    {
        RegisterActions();

        // Fail-safe for automation: never leave a stray window running.
        string quitAfter = OS.GetEnvironment("BRAWLER_QUIT_AFTER");
        if (quitAfter.Length > 0)
        {
            GetTree().CreateTimer(double.Parse(quitAfter)).Timeout += () => GetTree().Quit(2);
        }

        try
        {
            HandleAutoplay();
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"autoplay failed: {e.Message}");
            GetTree().Quit(1);
        }

        // Scene navigation for automation: BRAWLER_SCENE="evolve"|"manage" jumps there.
        string scene = OS.GetEnvironment("BRAWLER_SCENE");
        if (scene.Length > 0)
        {
            CallDeferred(nameof(GoToScene), $"res://scenes/{scene}.tscn");
        }

        // Screen capture support: BRAWLER_SHOT (without autoplay/autoevolve, which handle
        // their own captures) saves whatever scene is up after a second, then quits.
        string shot = OS.GetEnvironment("BRAWLER_SHOT");
        if (shot.Length > 0
            && OS.GetEnvironment("BRAWLER_AUTOPLAY").Length == 0
            && OS.GetEnvironment("BRAWLER_AUTOEVOLVE").Length == 0)
        {
            GetTree().CreateTimer(1.0).Timeout += () =>
            {
                GetViewport().GetTexture().GetImage().SavePng(shot);
                GD.Print($"shot saved: {shot}");
                GetTree().Quit();
            };
        }
    }

    /// <summary>Fullscreen toggle: F11 anywhere, or macOS-standard Cmd+Ctrl+F.</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }
        bool f11 = key.PhysicalKeycode == Key.F11;
        bool macFullscreen = key.PhysicalKeycode == Key.F && key.MetaPressed && key.CtrlPressed;
        if (!f11 && !macFullscreen)
        {
            return;
        }
        Window window = GetWindow();
        window.Mode = window.Mode == Window.ModeEnum.Fullscreen
            ? Window.ModeEnum.Maximized
            : Window.ModeEnum.Fullscreen;
    }

    private static void RegisterActions()
    {
        // (action, key, padDevice, padButton, axisValue)
        AddKey("p1_left", Key.A); AddPadAxis("p1_left", 0, -1f); AddPadButton("p1_left", 0, JoyButton.DpadLeft);
        AddKey("p1_right", Key.D); AddPadAxis("p1_right", 0, 1f); AddPadButton("p1_right", 0, JoyButton.DpadRight);
        AddKey("p1_jump", Key.W); AddPadButton("p1_jump", 0, JoyButton.A); AddPadButton("p1_jump", 0, JoyButton.B);
        AddKey("p1_attack", Key.S); AddPadButton("p1_attack", 0, JoyButton.X); AddPadButton("p1_attack", 0, JoyButton.Y);

        AddKey("p2_left", Key.J); AddPadAxis("p2_left", 1, -1f); AddPadButton("p2_left", 1, JoyButton.DpadLeft);
        AddKey("p2_right", Key.L); AddPadAxis("p2_right", 1, 1f); AddPadButton("p2_right", 1, JoyButton.DpadRight);
        AddKey("p2_jump", Key.I); AddPadButton("p2_jump", 1, JoyButton.A); AddPadButton("p2_jump", 1, JoyButton.B);
        AddKey("p2_attack", Key.K); AddPadButton("p2_attack", 1, JoyButton.X); AddPadButton("p2_attack", 1, JoyButton.Y);

        AddKey("ui_pause", Key.Escape);
    }

    private void HandleAutoplay()
    {
        string autoplay = OS.GetEnvironment("BRAWLER_AUTOPLAY");
        if (autoplay.Length == 0)
        {
            return;
        }

        string gamePath = OS.GetEnvironment("BRAWLER_GAME");
        MatchSession.Game = gamePath.Length > 0
            ? GameGenomeJson.Load(gamePath)
            : new GameRecord("generated", "autoplay",
                BrawlerSim.Genome.GameGenome.Generate(
                    BrawlerSim.Genome.GenerationConfig.Default, new BrawlerSim.Determinism.Pcg32(1)));

        if (autoplay.StartsWith("ai", System.StringComparison.Ordinal))
        {
            MatchSession.Mode = MatchMode.AiVsAi;
            MatchSession.AiSeed = autoplay.Contains(':') ? ulong.Parse(autoplay.Split(':')[1]) : 7;
        }
        else if (autoplay == "replay")
        {
            MatchSession.Mode = MatchMode.Replay;
            MatchSession.Trace = BrawlerSim.Replay.InputTraceJson.Load(OS.GetEnvironment("BRAWLER_TRACE"));
        }
        CallDeferred(nameof(GoToArena));
    }

    private void GoToArena()
    {
        GetTree().ChangeSceneToFile("res://scenes/arena.tscn");
    }

    private void GoToScene(string path)
    {
        GetTree().ChangeSceneToFile(path);
    }

    private static void EnsureAction(string action)
    {
        if (!InputMap.HasAction(action))
        {
            InputMap.AddAction(action, deadzone: 0.3f);
        }
    }

    private static void AddKey(string action, Key key)
    {
        EnsureAction(action);
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }

    private static void AddPadButton(string action, int device, JoyButton button)
    {
        EnsureAction(action);
        InputMap.ActionAddEvent(action, new InputEventJoypadButton { Device = device, ButtonIndex = button });
    }

    private static void AddPadAxis(string action, int device, float direction)
    {
        EnsureAction(action);
        InputMap.ActionAddEvent(action, new InputEventJoypadMotion
        {
            Device = device,
            Axis = JoyAxis.LeftX,
            AxisValue = direction,
        });
    }
}
