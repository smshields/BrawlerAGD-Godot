using Godot;

namespace BrawlerGodot;

/// <summary>
/// Small construction helpers for the UI rituals repeated across every screen:
/// labels with a font-size override, flat panel styles, scrollable lists, and
/// child clearing. Pure factories — call sites set any extra properties after,
/// so the produced node tree is identical to the hand-rolled versions.
/// </summary>
public static class UiWidgets
{
    /// <summary>A Label with a font-size override and optional Modulate tint.</summary>
    public static Label MakeLabel(string text, int fontSize, Color? color = null)
    {
        var label = new Label { Text = text };
        if (color is { } c)
        {
            label.Modulate = c;
        }
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }

    /// <summary>Section-heading label in <see cref="UiPalette.Heading"/> grey.</summary>
    public static Label Heading(string text, int fontSize = 14)
        => MakeLabel(text, fontSize, UiPalette.Heading);

    /// <summary>Dim hint label in <see cref="UiPalette.Hint"/> grey.</summary>
    public static Label Hint(string text, int fontSize = 13)
        => MakeLabel(text, fontSize, UiPalette.Hint);

    /// <summary>A flat panel style: uniform border width and corner radius, and
    /// optional uniform-per-axis content margins (negative = leave unset, the
    /// StyleBox default).</summary>
    public static StyleBoxFlat PanelStyle(
        Color bg, Color? border = null, int borderWidth = 0, int cornerRadius = 0,
        float marginX = -1f, float marginY = -1f)
    {
        var style = new StyleBoxFlat { BgColor = bg };
        if (border is { } b)
        {
            style.BorderColor = b;
        }
        style.SetBorderWidthAll(borderWidth);
        style.SetCornerRadiusAll(cornerRadius);
        if (marginX >= 0f)
        {
            style.ContentMarginLeft = marginX;
            style.ContentMarginRight = marginX;
        }
        if (marginY >= 0f)
        {
            style.ContentMarginTop = marginY;
            style.ContentMarginBottom = marginY;
        }
        return style;
    }

    /// <summary>The vertical list scaffold every screen uses: a ScrollContainer
    /// (horizontal scrolling disabled) wrapping an expand-fill VBox with the given
    /// separation. Extra scroll properties (size flags, minimum size, stretch
    /// ratio, visibility) are set by the caller after.</summary>
    public static ScrollContainer ScrollList(out VBoxContainer inner, int separation)
    {
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        inner = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        inner.AddThemeConstantOverride("separation", separation);
        scroll.AddChild(inner);
        return scroll;
    }

    /// <summary>Queue-free every child (list rebuild ritual).</summary>
    public static void ClearChildren(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            child.QueueFree();
        }
    }
}
