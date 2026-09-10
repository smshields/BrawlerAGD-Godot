using Godot;
using System.Linq;
using BrawlerSim.Evolution;
using BrawlerSim.Genome;
using BrawlerSim.Serialization;

namespace BrawlerGodot;

/// <summary>One plottable archive point: raw descriptor 4-vector + fitness + genome.
/// Name doubles as the save-to-favorites base name; PreviewSeed feeds the mini arena.</summary>
public sealed record HyperspaceEntry(
    float[] Descriptor, float Fitness, GameGenome Genome, string Name, string Origin, ulong PreviewSeed);

/// <summary>Immutable per-batch/per-generation archive snapshot published to the
/// Hyperspace tab (map-elites-descriptor-spec §8 data contract): the view copies it
/// and never reads live engine state.</summary>
public sealed record HyperspaceSnapshot(
    DescriptorBins Bins, int PlayerCount, HyperspaceEntry[] Entries, string StatusLine);

/// <summary>
/// The Evolve screen's HYPERSPACE tab (2026-09-10, map-elites-descriptor-spec §8):
/// the run's archive as a 3D cube lattice — three descriptor axes spatial, the fourth
/// on a scrub slider (with ALL aggregation and play), axis-swap buttons, empty-cell
/// fog, click-to-select feeding the config column's match preview, saved library
/// games as landmark markers. Read-only with respect to the run: nothing here touches
/// engine RNG or evaluation order.
/// </summary>
public partial class HyperspaceView : Control
{
    private const float CellStep = 1f;          // lattice spacing (world units)
    private const int Bins = DescriptorBins.BinsPerAxis;
    private const float Half = (Bins - 1) / 2f; // recenter bins 0..7 on the origin

    private HyperspaceSnapshot? _snapshot;

    // Axis assignment: which descriptor axis renders on X/Y/Z; the fourth is hidden
    // behind the scrub slider. Swap button i exchanges spatial slot i with the
    // hidden axis (the prototype-C axis-swap verb).
    private readonly int[] _spatial = { 0, 1, 2 };
    private int _hidden = 3;

    // 3D scene
    private SubViewport _viewport = null!;
    private SubViewportContainer _viewportContainer = null!;
    private Node3D _pivot = null!;
    private Camera3D _camera = null!;
    private MultiMeshInstance3D _cells = null!;
    private MultiMeshInstance3D _fog = null!;
    private MultiMeshInstance3D _landmarkMarks = null!;
    private MeshInstance3D _selectionBox = null!;
    private readonly Label3D[] _axisLabels = new Label3D[3];

    // HUD
    private HSlider _hiddenSlider = null!;
    private Label _hiddenLabel = null!;
    private Button _play = null!;
    private readonly Button[] _swapButtons = new Button[3];
    private CheckButton _fogToggle = null!;
    private Label _statusLine = null!;
    private Label _detail = null!;
    private Button _watch = null!;
    private Label _landmarkNote = null!;
    private double _playAccumulator;

    // Orbit state
    private float _yaw = 0.7f, _pitch = -0.5f, _distance = 17f;
    private Vector2 _pressPosition;
    private bool _dragging;

    // What the current rebuild plotted: visible spatial cell → best entry there.
    private readonly System.Collections.Generic.Dictionary<Vector3I, HyperspaceEntry> _visible = new();
    private Vector3I? _selectedCell;

    // Library landmarks (favorites + demo), loaded once per tab lifetime.
    private System.Collections.Generic.List<(string Name, GameGenome Genome, float[] Descriptor)>? _landmarks;

    /// <summary>Raised on cell/landmark pick — EvolveView routes it into the config
    /// column's match preview + ADD TO GAMES plumbing.</summary>
    public System.Action<HyperspaceEntry>? EntrySelected;

    public override void _Ready()
    {
        BuildUi();
        UpdateAxisUi();
        ApplyStatus();
    }

    /// <summary>Automation (BRAWLER_AUTOEVOLVE hslice=N): drive the hidden-axis
    /// slider so headless captures can verify slicing; 8 = ALL.</summary>
    public void SetSliceForAutomation(int slice) => _hiddenSlider.Value = slice;

    /// <summary>Publish a new archive snapshot (main thread). The view re-renders per
    /// snapshot, never per insertion.</summary>
    public void SetSnapshot(HyperspaceSnapshot snapshot)
    {
        _snapshot = snapshot;
        Rebuild();
        ApplyStatus();
    }

    public void Clear()
    {
        _snapshot = null;
        _selectedCell = null;
        Rebuild();
        ApplyStatus();
        _detail.Text = "";
        _watch.Visible = false;
    }

    public override void _Process(double delta)
    {
        if (!_play.ButtonPressed)
        {
            return;
        }
        _playAccumulator += delta;
        if (_playAccumulator >= 0.6)
        {
            _playAccumulator = 0;
            // Scrub 0..7 in a loop (skips ALL while playing).
            double next = _hiddenSlider.Value >= Bins - 1 ? 0 : _hiddenSlider.Value + 1;
            _hiddenSlider.Value = next; // fires ValueChanged → Rebuild
        }
    }

    // ── Rendering ─────────────────────────────────────────────────────────────────

    private void Rebuild()
    {
        _visible.Clear();
        int slice = (int)_hiddenSlider.Value; // 0..7, 8 = ALL
        bool all = slice >= Bins;

        if (_snapshot is { } snapshot)
        {
            foreach (HyperspaceEntry entry in snapshot.Entries)
            {
                int[] coords = DescriptorBins.CellCoordinates(snapshot.Bins.CellIndex(entry.Descriptor));
                if (!all && coords[_hidden] != slice)
                {
                    continue;
                }
                var key = new Vector3I(coords[_spatial[0]], coords[_spatial[1]], coords[_spatial[2]]);
                if (!_visible.TryGetValue(key, out HyperspaceEntry? incumbent)
                    || entry.Fitness > incumbent.Fitness)
                {
                    _visible[key] = entry;
                }
            }
        }

        // Fitness color ramp over the plotted cells (one sequential ramp, dark→green).
        // The low end is floored at 0 when any positive fitness exists, QD-style —
        // otherwise a single deeply negative elite washes every healthy cell to the
        // bright end (observed on the first MAP-Elites capture, 2026-09-10).
        float min = float.MaxValue, max = float.MinValue;
        foreach (HyperspaceEntry entry in _visible.Values)
        {
            min = Mathf.Min(min, entry.Fitness);
            max = Mathf.Max(max, entry.Fitness);
        }
        if (max > 0f)
        {
            min = Mathf.Max(min, 0f);
        }
        float span = max > min ? max - min : 1f;

        MultiMesh cells = _cells.Multimesh;
        cells.InstanceCount = _visible.Count;
        int i = 0;
        foreach ((Vector3I key, HyperspaceEntry entry) in _visible)
        {
            cells.SetInstanceTransform(i, new Transform3D(Basis.Identity, LatticePosition(key)));
            float t = Mathf.Clamp((entry.Fitness - min) / span, 0f, 1f);
            cells.SetInstanceColor(i, new Color(0.16f, 0.22f, 0.3f).Lerp(new Color(0.45f, 0.9f, 0.55f), t));
            i++;
        }

        RebuildFog();
        RebuildLandmarks(all, slice);
        UpdateSelectionBox();
    }

    private void RebuildFog()
    {
        MultiMesh fog = _fog.Multimesh;
        if (!_fogToggle.ButtonPressed || _snapshot is null)
        {
            fog.InstanceCount = 0;
            return;
        }
        fog.InstanceCount = Bins * Bins * Bins - _visible.Count;
        int i = 0;
        for (int x = 0; x < Bins; x++)
        {
            for (int y = 0; y < Bins; y++)
            {
                for (int z = 0; z < Bins; z++)
                {
                    var key = new Vector3I(x, y, z);
                    if (_visible.ContainsKey(key))
                    {
                        continue;
                    }
                    fog.SetInstanceTransform(i++, new Transform3D(Basis.Identity, LatticePosition(key)));
                }
            }
        }
    }

    private void RebuildLandmarks(bool all, int slice)
    {
        MultiMesh marks = _landmarkMarks.Multimesh;
        if (_snapshot is not { } snapshot)
        {
            marks.InstanceCount = 0;
            _landmarkNote.Text = "";
            return;
        }
        _landmarks ??= LoadLandmarks();
        var plotted = new System.Collections.Generic.List<Vector3I>();
        int mismatched = 0;
        foreach ((string _, GameGenome genome, float[] descriptor) in _landmarks)
        {
            // A library game only plots into an archive of its own configuration
            // (map-elites-descriptor-spec §7/§8): player count is the checkable part.
            if (genome.Characters.Count != snapshot.PlayerCount)
            {
                mismatched++;
                continue;
            }
            int[] coords = DescriptorBins.CellCoordinates(snapshot.Bins.CellIndex(descriptor));
            if (!all && coords[_hidden] != slice)
            {
                continue;
            }
            plotted.Add(new Vector3I(coords[_spatial[0]], coords[_spatial[1]], coords[_spatial[2]]));
        }
        marks.InstanceCount = plotted.Count;
        for (int i = 0; i < plotted.Count; i++)
        {
            marks.SetInstanceTransform(i, new Transform3D(Basis.Identity, LatticePosition(plotted[i])));
        }
        _landmarkNote.Text = _landmarks.Count == 0
            ? ""
            : $"LIBRARY LANDMARKS: {_landmarks.Count - mismatched} PLOTTED"
              + (mismatched > 0 ? $" · {mismatched} OTHER-CONFIG (NOT PLOTTED)" : "");
    }

    /// <summary>Favorites + demo game.jsons with descriptors computed on load — pure
    /// genome functions, so this is free and touches nothing in the run.</summary>
    private static System.Collections.Generic.List<(string, GameGenome, float[])> LoadLandmarks()
    {
        var landmarks = new System.Collections.Generic.List<(string, GameGenome, float[])>();
        foreach (string dir in new[] { AppPaths.FavoritesRoot(), AppPaths.DemoRoot() })
        {
            if (!System.IO.Directory.Exists(dir))
            {
                continue;
            }
            foreach (string file in System.IO.Directory.GetFiles(dir, "*.json").OrderBy(f => f))
            {
                try
                {
                    GameRecord record = GameGenomeJson.Load(file);
                    landmarks.Add((record.Name, record.Genome, Descriptors.Compute(record.Genome)));
                }
                catch (System.Exception e)
                {
                    GD.Print($"hyperspace: skipped unreadable library game {file}: {e.Message}");
                }
            }
        }
        return landmarks;
    }

    private static Vector3 LatticePosition(Vector3I key) =>
        new((key.X - Half) * CellStep, (key.Y - Half) * CellStep, (key.Z - Half) * CellStep);

    private void UpdateSelectionBox()
    {
        if (_selectedCell is { } cell && _visible.ContainsKey(cell))
        {
            _selectionBox.Visible = true;
            _selectionBox.Position = LatticePosition(cell);
        }
        else
        {
            _selectionBox.Visible = false;
        }
    }

    private void ApplyStatus()
    {
        _statusLine.Text = _snapshot?.StatusLine ?? "NO ARCHIVE YET — START A RUN";
    }

    // ── Interaction ───────────────────────────────────────────────────────────────

    private void OnViewportGuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _distance = Mathf.Clamp(_distance - 1.2f, 6f, 40f);
                UpdateCamera();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _distance = Mathf.Clamp(_distance + 1.2f, 6f, 40f);
                UpdateCamera();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } click:
                if (click.Pressed)
                {
                    _pressPosition = click.Position;
                    _dragging = true;
                }
                else
                {
                    _dragging = false;
                    if (click.Position.DistanceTo(_pressPosition) < 6f)
                    {
                        PickAt(click.Position);
                    }
                }
                break;
            case InputEventMouseMotion motion when _dragging:
                _yaw -= motion.Relative.X * 0.01f;
                _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * 0.01f, -1.5f, 1.5f);
                UpdateCamera();
                break;
        }
    }

    private void UpdateCamera()
    {
        _pivot.Rotation = new Vector3(_pitch, _yaw, 0f);
        _camera.Position = new Vector3(0f, 0f, _distance);
    }

    /// <summary>Exact pick, FitnessChart-style: nearest plotted cell center within a
    /// screen-space threshold (no physics bodies needed for 4,096 boxes).</summary>
    private void PickAt(Vector2 screenPosition)
    {
        Vector3I? bestCell = null;
        float bestDistance = 22f; // px threshold
        foreach (Vector3I key in _visible.Keys)
        {
            Vector3 world = LatticePosition(key);
            if (_camera.IsPositionBehind(world))
            {
                continue;
            }
            float d = _camera.UnprojectPosition(world).DistanceTo(screenPosition);
            if (d < bestDistance)
            {
                bestDistance = d;
                bestCell = key;
            }
        }
        if (bestCell is not { } cell)
        {
            return;
        }
        _selectedCell = cell;
        UpdateSelectionBox();
        HyperspaceEntry entry = _visible[cell];
        ShowDetail(entry);
        EntrySelected?.Invoke(entry);
    }

    private void ShowDetail(HyperspaceEntry entry)
    {
        if (_snapshot is not { } snapshot)
        {
            return;
        }
        int[] coords = DescriptorBins.CellCoordinates(snapshot.Bins.CellIndex(entry.Descriptor));
        var lines = new System.Text.StringBuilder();
        lines.AppendLine(entry.Name.ToUpperInvariant());
        lines.AppendLine($"FITNESS {entry.Fitness:F1} · {entry.Origin}");
        for (int axis = 0; axis < Descriptors.Count; axis++)
        {
            lines.AppendLine(
                $"{Descriptors.AxisNames[axis]}: BIN {coords[axis]} ({entry.Descriptor[axis]:F3})");
        }
        _detail.Text = lines.ToString().TrimEnd();
        _watch.Visible = true;
    }

    /// <summary>WATCH: the selected entry AI-vs-AI in the real arena (the library
    /// picker's launch pattern). Leaving the scene pauses a running evolution at its
    /// last checkpoint — same as BACK.</summary>
    private void WatchSelected()
    {
        if (_selectedCell is not { } cell || !_visible.TryGetValue(cell, out HyperspaceEntry? entry))
        {
            return;
        }
        MatchSession.Game = new GameRecord(entry.Name, entry.Origin, entry.Genome);
        MatchSession.Mode = MatchMode.AiVsAi;
        MatchSession.AiSeed = entry.PreviewSeed;
        MatchSession.EndRule = BrawlerSim.Sim.MatchEndRule.Stock;
        GetTree().ChangeSceneToFile(Scenes.Arena);
    }

    private void SwapAxis(int spatialSlot)
    {
        (_spatial[spatialSlot], _hidden) = (_hidden, _spatial[spatialSlot]);
        UpdateAxisUi();
        Rebuild();
    }

    private void ResetAxes()
    {
        _spatial[0] = 0;
        _spatial[1] = 1;
        _spatial[2] = 2;
        _hidden = 3;
        _hiddenSlider.Value = Bins; // ALL
        UpdateAxisUi();
        Rebuild();
    }

    private void UpdateAxisUi()
    {
        string[] slotNames = { "X", "Y", "Z" };
        for (int slot = 0; slot < 3; slot++)
        {
            _swapButtons[slot].Text = $"{slotNames[slot]}: {Descriptors.AxisNames[_spatial[slot]]}";
            _axisLabels[slot].Text = Descriptors.AxisNames[_spatial[slot]];
        }
        int slice = (int)_hiddenSlider.Value;
        _hiddenLabel.Text = $"{Descriptors.AxisNames[_hidden]}: " + (slice >= Bins ? "ALL" : $"BIN {slice}");
    }

    // ── Scene construction ────────────────────────────────────────────────────────

    private void BuildUi()
    {
        var column = new VBoxContainer();
        column.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        column.AddThemeConstantOverride("separation", 6);
        AddChild(column);

        // Controls row: hidden-axis scrub + play, axis swaps, fog, reset.
        var controls = new HBoxContainer();
        controls.AddThemeConstantOverride("separation", 8);
        column.AddChild(controls);

        _hiddenLabel = UiWidgets.MakeLabel("", 12);
        _hiddenLabel.CustomMinimumSize = new Vector2(170f, 0f);
        controls.AddChild(_hiddenLabel);
        _hiddenSlider = new HSlider
        {
            MinValue = 0, MaxValue = Bins, Step = 1, Value = Bins, // 8 = ALL
            CustomMinimumSize = new Vector2(120f, 20f),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        _hiddenSlider.ValueChanged += _ =>
        {
            UpdateAxisUi();
            Rebuild();
        };
        controls.AddChild(_hiddenSlider);
        _play = new Button { Icon = UiIcons.Play(), ToggleMode = true, TooltipText = "SCRUB THE HIDDEN AXIS" };
        controls.AddChild(_play);

        for (int slot = 0; slot < 3; slot++)
        {
            int captured = slot;
            var swap = new Button { TooltipText = "SWAP THIS AXIS WITH THE HIDDEN ONE" };
            swap.AddThemeFontSizeOverride("font_size", 11);
            swap.Pressed += () => SwapAxis(captured);
            _swapButtons[slot] = swap;
            controls.AddChild(swap);
        }
        _fogToggle = new CheckButton { Text = "EMPTY", ButtonPressed = true, TooltipText = "SHOW EMPTY CELLS" };
        _fogToggle.AddThemeFontSizeOverride("font_size", 11);
        _fogToggle.Toggled += _ => Rebuild();
        controls.AddChild(_fogToggle);
        var reset = new Button { Text = "RESET", TooltipText = "RESET AXIS ASSIGNMENT" };
        reset.AddThemeFontSizeOverride("font_size", 11);
        reset.Pressed += ResetAxes;
        controls.AddChild(reset);

        // The cube viewport.
        _viewportContainer = new SubViewportContainer
        {
            Stretch = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Stop,
        };
        _viewportContainer.GuiInput += OnViewportGuiInput;
        column.AddChild(_viewportContainer);
        _viewport = new SubViewport
        {
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        _viewportContainer.AddChild(_viewport);
        Build3DScene();

        _statusLine = UiWidgets.MakeLabel("", 12);
        column.AddChild(_statusLine);
        _landmarkNote = UiWidgets.MakeLabel("", 11, UiPalette.Hint);
        column.AddChild(_landmarkNote);

        // Detail panel: selected cell readout + WATCH (preview/save live in the
        // config column via EntrySelected).
        var detailRow = new HBoxContainer();
        detailRow.AddThemeConstantOverride("separation", 10);
        column.AddChild(detailRow);
        _detail = UiWidgets.MakeLabel("", 12);
        _detail.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        detailRow.AddChild(_detail);
        _watch = new Button
        {
            Text = "WATCH",
            Visible = false,
            TooltipText = "AI VS AI IN THE ARENA",
            SizeFlagsVertical = SizeFlags.ShrinkBegin, // don't stretch with the readout
        };
        _watch.Pressed += WatchSelected;
        detailRow.AddChild(_watch);
    }

    private void Build3DScene()
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = UiPalette.Background,
        };
        _viewport.AddChild(new WorldEnvironment { Environment = environment });

        _pivot = new Node3D();
        _viewport.AddChild(_pivot);
        _camera = new Camera3D { Current = true };
        _pivot.AddChild(_camera);
        UpdateCamera();

        // Filled cells: unshaded vertex-colored boxes.
        _cells = new MultiMeshInstance3D
        {
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = new BoxMesh
                {
                    Size = Vector3.One * (CellStep * 0.68f),
                    Material = new StandardMaterial3D
                    {
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        VertexColorUseAsAlbedo = true,
                    },
                },
            },
        };
        _viewport.AddChild(_cells);

        // Empty-cell fog: tiny translucent markers hinting the lattice shape.
        _fog = new MultiMeshInstance3D
        {
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = new BoxMesh
                {
                    Size = Vector3.One * (CellStep * 0.1f),
                    Material = new StandardMaterial3D
                    {
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        AlbedoColor = new Color(0.5f, 0.55f, 0.65f, 0.10f),
                    },
                },
            },
        };
        _viewport.AddChild(_fog);

        // Library landmarks: white translucent shells over whatever elite holds the cell.
        _landmarkMarks = new MultiMeshInstance3D
        {
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = new SphereMesh
                {
                    Radius = CellStep * 0.5f,
                    Height = CellStep,
                    Material = new StandardMaterial3D
                    {
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        AlbedoColor = new Color(1f, 1f, 1f, 0.28f),
                    },
                },
            },
        };
        _viewport.AddChild(_landmarkMarks);

        // Selection: a gold translucent cube one full cell in size.
        _selectionBox = new MeshInstance3D
        {
            Visible = false,
            Mesh = new BoxMesh
            {
                Size = Vector3.One * CellStep,
                Material = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    AlbedoColor = new Color(1f, 0.85f, 0.3f, 0.35f),
                },
            },
        };
        _viewport.AddChild(_selectionBox);

        BuildWireframeAndLabels();
    }

    private void BuildWireframeAndLabels()
    {
        float extent = Half * CellStep + CellStep * 0.5f;
        var mesh = new ImmediateMesh();
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.4f, 0.44f, 0.55f),
        };
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);
        for (int corner = 0; corner < 8; corner++)
        {
            var a = new Vector3(
                (corner & 1) == 0 ? -extent : extent,
                (corner & 2) == 0 ? -extent : extent,
                (corner & 4) == 0 ? -extent : extent);
            foreach (int bit in new[] { 1, 2, 4 })
            {
                if ((corner & bit) != 0)
                {
                    continue; // draw each edge once, from its low corner
                }
                var b = new Vector3(
                    bit == 1 ? extent : a.X, bit == 2 ? extent : a.Y, bit == 4 ? extent : a.Z);
                mesh.SurfaceAddVertex(a);
                mesh.SurfaceAddVertex(b);
            }
        }
        mesh.SurfaceEnd();
        _viewport.AddChild(new MeshInstance3D { Mesh = mesh });

        // Billboarded axis names at the positive end of each spatial axis.
        Vector3[] positions =
        {
            new(extent + 0.9f, -extent, -extent),
            new(-extent, extent + 0.9f, -extent),
            new(-extent, -extent, extent + 0.9f),
        };
        for (int slot = 0; slot < 3; slot++)
        {
            _axisLabels[slot] = new Label3D
            {
                Text = "",
                Position = positions[slot],
                PixelSize = 0.012f,
                FontSize = 40,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Modulate = UiPalette.Heading,
            };
            _viewport.AddChild(_axisLabels[slot]);
        }
    }
}
