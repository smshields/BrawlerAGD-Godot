using System.Collections.Generic;
using Godot;
using BrawlerSim.Sim;

namespace BrawlerGodot;

/// <summary>BRAWLER_AUTOSELECT automation (screenshot verification) — the direct
/// lobby-arranging tokens plus click= targets that go through the REAL input
/// pipeline. Split from CharacterSelectView.cs (pure text move).</summary>
public partial class CharacterSelectView
{
    // Unlike the direct tokens below, click= targets go through Input.ParseInputEvent —
    // the full input pipeline including GUI consumption — so they verify what a real
    // mouse does (2026-08-17: the direct tokens masked a root MouseFilter bug).
    private readonly List<string> _autoClicks = new();

    private void ApplyAutoSelect(string spec)
    {
        foreach (string pair in spec.Split(';'))
        {
            string[] kv = pair.Split('=');
            if (kv.Length != 2)
            {
                continue;
            }
            switch (kv[0])
            {
                case "p1": // join the mouse/keyboard human and pick a character
                    JoinPane(-1);
                    _panes[_mousePane].CharacterIndex = int.Parse(kv[1]);
                    break;
                case var s when s.StartsWith("cpu", System.StringComparison.Ordinal)
                    && !s.EndsWith("level", System.StringComparison.Ordinal):
                {
                    int pane = int.Parse(s[3..]) - 1;
                    _panes[pane].Mode = PaneMode.Cpu;
                    _panes[pane].CharacterIndex = int.Parse(kv[1]);
                    break;
                }
                case var s when s.EndsWith("level", System.StringComparison.Ordinal):
                    _panes[int.Parse(s[3..^5]) - 1].CpuLevel = int.Parse(kv[1]);
                    break;
                case "stage":
                    _stageIndex = int.Parse(kv[1]);
                    break;
                case "mode":
                    _mode = kv[1] == "timed" ? MatchEndRule.Timed : MatchEndRule.Stock;
                    break;
                case "rename1":
                    _panes[0].Renaming = kv[1] == "1";
                    break;
                case "start":
                    CallDeferred(nameof(StartMatch));
                    break;
                case "click": // "pane0" / "grid3" / "stage2" — REAL mouse clicks
                    _autoClicks.Add(kv[1]);
                    break;
            }
        }
        RefreshAll();
        if (_autoClicks.Count > 0)
        {
            ScheduleAutoClicks();
        }
    }

    private void ScheduleAutoClicks()
    {
        double at = 0.2; // after first-frame layout; all clicks land before the 1 s shot
        foreach (string target in _autoClicks)
        {
            string t = target;
            GetTree().CreateTimer(at).Timeout += () => InjectClick(t);
            at += 0.2;
        }
    }

    private void InjectClick(string target)
    {
        Control? area = target switch
        {
            _ when target.StartsWith("grid", System.StringComparison.Ordinal)
                => _gridCells[int.Parse(target[4..])],
            _ when target.StartsWith("stage", System.StringComparison.Ordinal)
                => _stageCards[int.Parse(target[5..])],
            _ when target.StartsWith("pane", System.StringComparison.Ordinal)
                => _panes[int.Parse(target[4..])].Root,
            _ => null,
        };
        if (area is null)
        {
            return;
        }
        // PushInput(local) delivers in canvas coords through the viewport's full
        // pipeline (GUI consumption first, unhandled after) — ParseInputEvent would
        // treat Position as SCREEN coords and land the click in the wrong control.
        Vector2 pos = area.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseButton
        {
            Position = pos, GlobalPosition = pos, ButtonIndex = MouseButton.Left, Pressed = true,
        }, inLocalCoords: true);
        GetViewport().PushInput(new InputEventMouseButton
        {
            Position = pos, GlobalPosition = pos, ButtonIndex = MouseButton.Left, Pressed = false,
        }, inLocalCoords: true);
        GD.Print($"autoclick: {target} @ {pos}");
    }
}
