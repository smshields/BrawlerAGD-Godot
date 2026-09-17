using Godot;
using BrawlerSim.Evolution;
using BrawlerSim.Hyperspace;
using BrawlerSim.Serialization;

namespace BrawlerGodot.Hyperspace;

/// <summary>
/// The cockpit dashboard (galaxy-view.md §7): four pods of real Controls under the
/// world viewport — position, map + hyperdrive, match preview, target details.
///
/// REMOVED vs the PoC, deliberately: the help overlay and the top-left galaxy title
/// and colour legend. Not moved here — deleted (2026-09-12 direction). Persistent
/// readouts live in these pods; controls documentation belongs to the app's own help
/// system; transient feedback is the toast.
///
/// The spec sized this for a full screen. It lives in a tab beside the Evolve config
/// column, so pods drop in priority order as the tab narrows rather than crushing
/// each other: preview first (the config column already has one), then the map.
/// </summary>
public sealed partial class GalaxyDashboard : PanelContainer
{
    public const float Height = 168f;
    private const float PreviewNeedsWidth = 860f;
    private const float MapNeedsWidth = 620f;

    private HBoxContainer _pods = null!;
    private Control _positionPod = null!;
    private Control _mapPod = null!;
    private Control _previewPod = null!;
    private Control _targetPod = null!;

    private Label _positionText = null!;
    private Label _speedText = null!;
    private Label _targetText = null!;
    private ColorRect _targetSwatch = null!;
    private MatchPreview _preview = null!;
    private Label _previewNote = null!;
    private readonly Button[] _segments = new Button[GalaxyLayout.Bins];
    private IMapPodInstrument _map = null!;

    /// <summary>Hyperdrive: jump directly to a galaxy index.</summary>
    public System.Action<int>? GalaxyRequested;

    /// <summary>Hyperdrive: step one galaxy along the lane (-1 / +1).</summary>
    public System.Action<int>? GalaxyStepRequested;

    private string _previewKey = "";

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(0f, Height);
        AddThemeStyleboxOverride("panel", UiWidgets.PanelStyle(
            new Color(0.06f, 0.07f, 0.10f, 0.96f), cornerRadius: 4, marginX: 8f, marginY: 6f));

        _pods = new HBoxContainer();
        _pods.AddThemeConstantOverride("separation", 10);
        AddChild(_pods);

        _positionPod = BuildPositionPod();
        _pods.AddChild(_positionPod);
        _mapPod = BuildMapPod();
        _pods.AddChild(_mapPod);
        _previewPod = BuildPreviewPod();
        _pods.AddChild(_previewPod);
        _targetPod = BuildTargetPod();
        _pods.AddChild(_targetPod);

        Resized += ApplyResponsiveLayout;
        ApplyResponsiveLayout();
    }

    private void ApplyResponsiveLayout()
    {
        _previewPod.Visible = Size.X >= PreviewNeedsWidth;
        _mapPod.Visible = Size.X >= MapNeedsWidth;
    }

    private Control BuildPositionPod()
    {
        var pod = Pod("POSITION", out VBoxContainer body);
        _positionText = UiWidgets.MakeLabel("", 11);
        body.AddChild(_positionText);
        _speedText = UiWidgets.MakeLabel("", 11, UiPalette.Hint);
        body.AddChild(_speedText);
        return pod;
    }

    private Control BuildMapPod()
    {
        var pod = Pod("SECTOR MAP", out VBoxContainer body);
        var instrument = new CubeMapInstrument();
        _map = instrument;
        body.AddChild(instrument.Root);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 2);
        body.AddChild(row);
        row.AddChild(StepButton("<", -1));
        for (int g = 0; g < GalaxyLayout.Bins; g++)
        {
            int target = g;
            var segment = new Button
            {
                Text = g.ToString(),
                CustomMinimumSize = new Vector2(16f, 16f),
                TooltipText = $"WARP TO GALAXY {g}",
            };
            segment.AddThemeFontSizeOverride("font_size", 9);
            segment.Pressed += () => GalaxyRequested?.Invoke(target);
            _segments[g] = segment;
            row.AddChild(segment);
        }
        row.AddChild(StepButton(">", 1));
        return pod;
    }

    private Button StepButton(string text, int direction)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(18f, 16f) };
        button.AddThemeFontSizeOverride("font_size", 9);
        button.Pressed += () => GalaxyStepRequested?.Invoke(direction);
        return button;
    }

    private Control BuildPreviewPod()
    {
        var pod = Pod("MATCH PREVIEW", out VBoxContainer body);
        var frame = new Control { CustomMinimumSize = new Vector2(196f, 110f) };
        body.AddChild(frame);
        var container = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
        frame.AddChild(container);
        container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var viewport = new SubViewport { RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible };
        container.AddChild(viewport);
        _preview = new MatchPreview();
        viewport.AddChild(_preview);

        _previewNote = UiWidgets.MakeLabel("NOTHING IN THE CROSSHAIR", 10, UiPalette.Hint);
        body.AddChild(_previewNote);
        return pod;
    }

    private Control BuildTargetPod()
    {
        var pod = Pod("TARGET", out VBoxContainer body);
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 6);
        body.AddChild(header);
        _targetSwatch = new ColorRect
        {
            CustomMinimumSize = new Vector2(12f, 12f),
            Color = new Color(0f, 0f, 0f, 0f),
            // ShrinkBegin: in an HBox a ColorRect otherwise stretches to the pod's
            // full height and reads as a bar rather than a swatch.
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
        };
        header.AddChild(_targetSwatch);
        _targetText = UiWidgets.MakeLabel("NOTHING IN THE CROSSHAIR", 11);
        _targetText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _targetText.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(_targetText);
        pod.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return pod;
    }

    private static PanelContainer Pod(string title, out VBoxContainer body)
    {
        var pod = new PanelContainer();
        pod.AddThemeStyleboxOverride("panel", UiWidgets.PanelStyle(
            new Color(0.09f, 0.10f, 0.14f, 1f), cornerRadius: 3, marginX: 8f, marginY: 5f));
        body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 3);
        pod.AddChild(body);
        body.AddChild(UiWidgets.MakeLabel(title, 10, UiPalette.Hint));
        return pod;
    }

    /// <summary>Stop the preview's mini-sim — the view calls this when its tab is
    /// no longer in front.</summary>
    public void StopPreview()
    {
        if (_previewKey.Length > 0)
        {
            _preview.Stop();
            _previewKey = "";
        }
    }

    /// <summary>Per-frame readouts. `nearest` is a DERIVED value (§8.1.6) — it drives
    /// these pods and nothing about what the world draws.</summary>
    public void Refresh(GalaxyStarField stars, IGalaxyGeometry geometry, Vector3 ship, float yaw,
        float speed, bool boosting, int nearest, GalaxyTarget? focus, bool locked)
    {
        _positionText.Text = PositionReadout(geometry, ship, nearest);
        _speedText.Text = $"SPEED {speed:F0} U/S{(boosting ? "  ·  BOOST" : "")}";

        for (int g = 0; g < GalaxyLayout.Bins; g++)
        {
            _segments[g].Modulate = g == nearest
                ? new Color(1f, 0.82f, 0.35f)
                : new Color(1f, 1f, 1f, 0.55f);
        }
        if (_mapPod.Visible)
        {
            _map.Refresh(stars, geometry, nearest, ship, yaw, focus?.Star);
        }

        UpdateTargetPod(focus, locked);
        UpdatePreviewPod(focus);
    }

    private static string PositionReadout(IGalaxyGeometry geometry, Vector3 ship, int nearest)
    {
        Vector3 local = ship - GalaxyVec.From(geometry.GalaxyCenter(nearest));
        string[] names = Descriptors.AxisNames;
        (int I, int J, int K)? sector = geometry.SectorOf(GalaxyVec.To(local));
        string Bin(int? value) => value is { } bin ? bin.ToString() : "OUTSIDE";
        return $"{names[0]}  {Bin(sector?.I)}\n{names[1]}  {Bin(sector?.J)}\n"
            + $"{names[2]}  {Bin(sector?.K)}\n{names[3]}  {nearest}";
    }

    private void UpdateTargetPod(GalaxyTarget? focus, bool locked)
    {
        if (focus is null)
        {
            _targetSwatch.Color = new Color(0f, 0f, 0f, 0f);
            _targetText.Text = "NOTHING IN THE CROSSHAIR";
            return;
        }
        _targetSwatch.Color = focus.Star?.Color ?? new Color(0.62f, 0.70f, 0.95f);
        string name = GalaxyTargeting.Describe(focus);
        string tag = locked ? "  ◈ LOCKED · ESC RELEASES" : "";
        if (focus.Kind == GalaxyTargetKind.Galaxy)
        {
            _targetText.Text = $"{name}{tag}\n{Descriptors.AxisNames[3]} BIN {focus.Galaxy}"
                + "\nENTER: WARP THERE";
            return;
        }
        GalaxyStar star = focus.Star!;
        HyperspaceEntry entry = focus.Entry!;
        _targetText.Text = $"{name}{tag}\nFITNESS {entry.Fitness:F1}\n"
            + $"{Descriptors.AxisNames[0]} {star.I} · {Descriptors.AxisNames[1]} {star.J} · "
            + $"{Descriptors.AxisNames[2]} {star.K} · {Descriptors.AxisNames[3]} {star.G}"
            + "\nENTER: WARP THERE";
    }

    /// <summary>Preview source: the locked object, else the hovered star. A galaxy at
    /// this range has no feed; nothing at all shows no feed either — never static.</summary>
    private void UpdatePreviewPod(GalaxyTarget? focus)
    {
        if (!_previewPod.Visible)
        {
            return;
        }
        if (focus?.Entry is not { } entry)
        {
            if (_previewKey.Length > 0)
            {
                _preview.Stop();
                _previewKey = "";
            }
            _previewNote.Text = focus is { Kind: GalaxyTargetKind.Galaxy }
                ? "NO FEED AT THIS RANGE"
                : "NOTHING IN THE CROSSHAIR";
            return;
        }
        string key = $"{entry.Name}|{entry.PreviewSeed}";
        if (key == _previewKey)
        {
            return;
        }
        _previewKey = key;
        _preview.ShowGame(new GameRecord(entry.Name, entry.Origin, entry.Genome), entry.PreviewSeed);
        _previewNote.Text = $"{entry.Name.ToUpperInvariant()} · FITNESS {entry.Fitness:F1}";
    }
}
