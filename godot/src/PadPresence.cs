using Godot;

namespace BrawlerGodot;

/// <summary>
/// Controller-presence watcher for the screens that gate 2-player entry points
/// (2-player needs a controller — the keyboard is entirely P1's now). Wraps the
/// JoyConnectionChanged subscribe/apply/unsubscribe ritual shared by MainMenu and
/// ManageView; each screen supplies its own feedback (button text vs tooltip).
/// </summary>
public sealed class PadPresence
{
    private readonly System.Action<bool> _apply;

    private PadPresence(System.Action<bool> apply) => _apply = apply;

    public static bool HasPad => Input.GetConnectedJoypads().Count > 0;

    /// <summary>Subscribes to pad connect/disconnect and applies the feedback once
    /// immediately. Call <see cref="Detach"/> from _ExitTree.</summary>
    public static PadPresence Watch(System.Action<bool> apply)
    {
        var watcher = new PadPresence(apply);
        Input.Singleton.JoyConnectionChanged += watcher.OnJoyConnectionChanged;
        apply(HasPad);
        return watcher;
    }

    public void Detach() => Input.Singleton.JoyConnectionChanged -= OnJoyConnectionChanged;

    private void OnJoyConnectionChanged(long device, bool connected) => _apply(HasPad);
}
