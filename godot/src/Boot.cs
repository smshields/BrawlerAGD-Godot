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
/// Bindings (2026-07-09 multi-move control scheme, docs/features/multi-move-controls.md):
/// P1 owns the FULL keyboard — A/D move, W/S vertical (inert for now), SPACE jump,
/// I/J/K/L the four assignable action buttons — plus an optional SECOND gamepad
/// (device 1). P2 is gamepad-only (device 0): 2-player requires a controller.
/// Pad layout (designer-specified): Y/B (top/right face) jump; X (left face) = J,
/// A (bottom face) = K, L1 = I, R1 = L; stick/dpad move.
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
        // P1 keyboard: WASD movement (W/S = the captured-but-inert vertical axis),
        // SPACE jump, I/J/K/U/L = assignable action buttons 0..4 (2026-07-20: jump
        // shrank to one pad button, freeing a fifth slot; U is the new key at
        // index 3 so the L key stays the LAST button — the dash pin's home).
        AddKey("p1_left", Key.A);
        AddKey("p1_right", Key.D);
        AddKey("p1_up", Key.W);
        AddKey("p1_down", Key.S);
        AddKey("p1_jump", Key.Space);
        AddKey("p1_action0", Key.I);
        AddKey("p1_action1", Key.J);
        AddKey("p1_action2", Key.K);
        AddKey("p1_action3", Key.U);
        AddKey("p1_action4", Key.L);

        // Pads: P2 gets the FIRST pad (2P requires a controller; the keyboard is P1's);
        // P1 can use a second pad. Same layout on both.
        RegisterPadLayout(playerNumber: 1, device: 1);
        RegisterPadLayout(playerNumber: 2, device: 0);

        AddKey("ui_pause", Key.Escape);
    }

    /// <summary>
    /// Designer-specified pad layout (2026-07-20 revision: ONE jump button): B (right
    /// face) = jump; X (left face) and A (bottom face) = action buttons 1 (J) and
    /// 2 (K); Y (top face) = the NEW action button 3 (U); the shoulders carry
    /// L1 = action 0 (I) and R1 = action 4 (L — the last button, the dash pin's
    /// home). Stick + dpad move.
    /// </summary>
    private static void RegisterPadLayout(int playerNumber, int device)
    {
        string p = $"p{playerNumber}_";
        AddPadAxis(p + "left", device, JoyAxis.LeftX, -1f); AddPadButton(p + "left", device, JoyButton.DpadLeft);
        AddPadAxis(p + "right", device, JoyAxis.LeftX, 1f); AddPadButton(p + "right", device, JoyButton.DpadRight);
        AddPadAxis(p + "up", device, JoyAxis.LeftY, -1f); AddPadButton(p + "up", device, JoyButton.DpadUp);
        AddPadAxis(p + "down", device, JoyAxis.LeftY, 1f); AddPadButton(p + "down", device, JoyButton.DpadDown);
        AddPadButton(p + "jump", device, JoyButton.B);
        AddPadButton(p + "action0", device, JoyButton.LeftShoulder);
        AddPadButton(p + "action1", device, JoyButton.X);
        AddPadButton(p + "action2", device, JoyButton.A);
        AddPadButton(p + "action3", device, JoyButton.Y);
        AddPadButton(p + "action4", device, JoyButton.RightShoulder);
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

        // BRAWLER_RULES="timed:<seconds>" (2026-08-12): run the automated match under
        // the TIMED rule — visual verification of the KO counter + clock.
        string rules = OS.GetEnvironment("BRAWLER_RULES");
        if (rules.StartsWith("timed", System.StringComparison.Ordinal))
        {
            MatchSession.EndRule = BrawlerSim.Sim.MatchEndRule.Timed;
            MatchSession.TimedMatchSeconds = rules.Contains(':') ? float.Parse(rules.Split(':')[1]) : 120f;
        }
        else
        {
            MatchSession.EndRule = BrawlerSim.Sim.MatchEndRule.Stock;
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

    private static void AddPadAxis(string action, int device, JoyAxis axis, float direction)
    {
        EnsureAction(action);
        InputMap.ActionAddEvent(action, new InputEventJoypadMotion
        {
            Device = device,
            Axis = axis,
            AxisValue = direction,
        });
    }
}
