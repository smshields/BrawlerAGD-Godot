using Godot;

namespace BrawlerGodot;

/// <summary>The in-pane rename keyboard. Split from CharacterSelectView.cs
/// (pure text move).</summary>
public partial class CharacterSelectView
{
    /// <summary>The in-pane rename keyboard (sketch: digit row + letter rows).</summary>
    private void BuildRenameKeyboard(Pane p)
    {
        var grid = new GridContainer { Columns = 10, SizeFlagsVertical = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 3);
        grid.AddThemeConstantOverride("v_separation", 3);
        p.Body.AddChild(grid);
        foreach (char ch in "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ")
        {
            char c = ch;
            void Type(int _)
            {
                if (p.NameOverride.Length < 14)
                {
                    p.NameOverride += c;
                }
                RefreshAll();
            }
            Button key = HotspotButton(c.ToString(), Type);
            key.CustomMinimumSize = new Vector2(24f, 24f);
            UiTheme.CompactKey(key);
            grid.AddChild(key);
        }
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        p.Body.AddChild(row);
        void TypeSpace(int _)
        {
            if (p.NameOverride.Length < 14)
            {
                p.NameOverride += " ";
            }
            RefreshAll();
        }
        Button space = HotspotButton("SPACE", TypeSpace);
        space.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(space);
        void Delete(int _)
        {
            if (p.NameOverride.Length > 0)
            {
                p.NameOverride = p.NameOverride[..^1];
            }
            RefreshAll();
        }
        Button del = HotspotButton("DEL", Delete);
        del.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(del);
        void Done(int _)
        {
            p.Renaming = false;
            RefreshAll();
        }
        Button ok = HotspotButton("OK", Done);
        ok.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(ok);
    }
}
