using Godot;

namespace BrawlerGodot;

/// <summary>
/// Persisted app settings (2026-07-21, Map Size feature — the minimap options are its
/// first tenants). Backed by user://settings.cfg via ConfigFile; loaded lazily, saved
/// on every change. View-layer only — nothing here may influence the sim.
/// </summary>
public static class AppSettings
{
    private const string FilePath = "user://settings.cfg";
    private const string Section = "minimap";

    public enum Corner
    {
        UpperLeft = 0,
        UpperRight = 1,
        LowerLeft = 2,
        LowerRight = 3,
    }

    private static bool _loaded;
    private static bool _minimapEnabled = true;            // designer default: ON
    private static Corner _minimapCorner = Corner.UpperRight;
    private static float _minimapSize = 0.22f;             // fraction of viewport width
    private static float _minimapOpacity = 0.5f;           // designer default: 50%
    private static bool _debugPanelEnabled = true;         // HUD polish: default ON

    /// <summary>The per-player debug strip above the HUD (state + controls),
    /// toggleable from the pause menu (HUD polish, 2026-07-23).</summary>
    public static bool DebugPanelEnabled
    {
        get { Load(); return _debugPanelEnabled; }
        set { Load(); _debugPanelEnabled = value; Save(); }
    }

    public static bool MinimapEnabled
    {
        get { Load(); return _minimapEnabled; }
        set { Load(); _minimapEnabled = value; Save(); }
    }

    public static Corner MinimapCorner
    {
        get { Load(); return _minimapCorner; }
        set { Load(); _minimapCorner = value; Save(); }
    }

    /// <summary>Minimap width as a fraction of the viewport width, clamped 0.1–0.4.</summary>
    public static float MinimapSize
    {
        get { Load(); return _minimapSize; }
        set { Load(); _minimapSize = Mathf.Clamp(value, 0.1f, 0.4f); Save(); }
    }

    /// <summary>Overlay opacity, clamped 0.1–1.</summary>
    public static float MinimapOpacity
    {
        get { Load(); return _minimapOpacity; }
        set { Load(); _minimapOpacity = Mathf.Clamp(value, 0.1f, 1f); Save(); }
    }

    private static void Load()
    {
        if (_loaded)
        {
            return;
        }
        _loaded = true;
        var file = new ConfigFile();
        if (file.Load(FilePath) != Error.Ok)
        {
            return; // first run — defaults stand
        }
        _minimapEnabled = (bool)file.GetValue(Section, "enabled", true);
        _minimapCorner = (Corner)(int)file.GetValue(Section, "corner", (int)Corner.UpperRight);
        _minimapSize = Mathf.Clamp((float)file.GetValue(Section, "size", 0.22f), 0.1f, 0.4f);
        _minimapOpacity = Mathf.Clamp((float)file.GetValue(Section, "opacity", 0.5f), 0.1f, 1f);
        _debugPanelEnabled = (bool)file.GetValue("hud", "debugPanel", true);
    }

    private static void Save()
    {
        var file = new ConfigFile();
        file.SetValue(Section, "enabled", _minimapEnabled);
        file.SetValue(Section, "corner", (int)_minimapCorner);
        file.SetValue(Section, "size", _minimapSize);
        file.SetValue(Section, "opacity", _minimapOpacity);
        file.SetValue("hud", "debugPanel", _debugPanelEnabled);
        file.Save(FilePath);
    }
}
