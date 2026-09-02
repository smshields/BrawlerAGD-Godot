using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrawlerSim.Backgrounds;

/// <summary>A crop rect in source-image pixels (x, y, width, height).</summary>
public readonly record struct BgRect(int X, int Y, int W, int H);

/// <summary>Per-entry guardrail metrics measured by the pipeline (stored, never
/// re-derived at runtime — "no image analysis at runtime" is the M-BG contract).</summary>
public sealed record BackgroundMetrics(
    float SatMean, float ContrastBand, IReadOnlyList<int> DomLightColor, float ValMean);

/// <summary>
/// One background entry of the bg-v1 index. Fields mirror the contract
/// (docs/background-implementation-brief.md); `Descriptor` is the decoded 8x8 Lab
/// perceptual thumbnail (192 bytes) the uniqueness rules measure distance over.
/// </summary>
public sealed record BackgroundEntry(
    string Id,
    string File,
    int Width,
    int Height,
    string LayerRole,           // "full" | "far" | "mid" | "element"
    bool Tileable,
    string? Style,
    int? HorizonY,
    IReadOnlyList<string> Register,
    IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, float> TraitAffinity,
    string PaletteGroup,
    IReadOnlyList<string> Scene,
    BackgroundMetrics Metrics,
    IReadOnlyList<string> PairExclude,
    IReadOnlyList<string> Remaps,
    byte[] Descriptor,
    BgRect? LayerFar,           // matted far region of a full entry (optional)
    BgRect? LayerMid,           // matted mid region of a full entry (optional)
    string Source,
    string License,
    string? Author,
    string? Attribution)
{
    /// <summary>Mean absolute byte difference of the perceptual descriptors,
    /// normalized to [0, 1]. Zero when either side has no descriptor.</summary>
    public static double DescriptorDistance(byte[] a, byte[] b)
    {
        if (a.Length == 0 || a.Length != b.Length)
        {
            return 0;
        }
        long sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            sum += Math.Abs(a[i] - b[i]);
        }
        return sum / (double)(a.Length * 255);
    }
}

/// <summary>
/// The bg-v1 background index (backgrounds track M-BG5, 2026-09-02 —
/// docs/background-implementation-brief.md). Engine-side reader ONLY: the
/// Cowork-side pipeline produces the index; fixes are data edits, never code.
/// Parsing enforces the attribution invariants DEFENSIVELY (the pipeline already
/// guarantees them): every entry needs a license from the allowed set, and any
/// attribution-bearing license (everything but CC0/PD) needs a non-null attribution
/// string — violating entries are REFUSED (dropped and counted), because shipping
/// an un-creditable image is a license violation, not a cosmetic bug.
/// No texture concerns here — pixels are the view layer's business.
/// </summary>
public sealed class BackgroundLibrary
{
    /// <summary>Licenses the corpus may carry (background-corpus-plan.md decision 1;
    /// OGA-BY 3.0 joined in the shipped v0.2 index — attribution-bearing like CC-BY).</summary>
    private static readonly HashSet<string> AllowedLicenses = new(StringComparer.Ordinal)
    {
        "CC0", "PD", "CC-BY-3.0", "CC-BY-4.0", "OGA-BY-3.0",
    };

    private readonly Dictionary<string, BackgroundEntry> _byId;

    public string Contract { get; }
    public string Version { get; }
    public IReadOnlyList<string> TraitVocabulary { get; }
    public IReadOnlyList<string> RemapTargets { get; }
    public IReadOnlyList<BackgroundEntry> Entries { get; }

    /// <summary>Entries dropped by the license/attribution assertions, with reasons —
    /// exposed so tests (and a loud loader warning) can see refusals.</summary>
    public IReadOnlyList<string> Refused { get; }

    private BackgroundLibrary(string contract, string version,
        IReadOnlyList<string> traitVocabulary, IReadOnlyList<string> remapTargets,
        List<BackgroundEntry> entries, List<string> refused)
    {
        Contract = contract;
        Version = version;
        TraitVocabulary = traitVocabulary;
        RemapTargets = remapTargets;
        Entries = entries;
        Refused = refused;
        _byId = entries.ToDictionary(e => e.Id, StringComparer.Ordinal);
    }

    public BackgroundEntry? ById(string? id) =>
        id is not null && _byId.TryGetValue(id, out BackgroundEntry? e) ? e : null;

    public bool Contains(string? id) => id is not null && _byId.ContainsKey(id);

    /// <summary>True when an attribution-bearing license requires a credit string.</summary>
    public static bool RequiresAttribution(string license) =>
        license is not ("CC0" or "PD");

    public static BackgroundLibrary Parse(string indexJson)
    {
        IndexDoc doc = JsonSerializer.Deserialize<IndexDoc>(indexJson, Options)
            ?? throw new JsonException("background index parsed to null.");
        if (doc.Contract != "bg-v1")
        {
            throw new NotSupportedException(
                $"background index contract '{doc.Contract}' is not supported (expected bg-v1).");
        }
        var entries = new List<BackgroundEntry>(doc.Entries?.Count ?? 0);
        var refused = new List<string>();
        foreach (EntryDoc e in doc.Entries ?? new List<EntryDoc>())
        {
            if (e.Id is null || e.File is null || e.Size is not { Count: 2 })
            {
                refused.Add($"{e.Id ?? "<no id>"}: malformed entry");
                continue;
            }
            if (e.License is null || !AllowedLicenses.Contains(e.License))
            {
                refused.Add($"{e.Id}: license '{e.License}' is not in the allowed set");
                continue;
            }
            if (RequiresAttribution(e.License) && string.IsNullOrWhiteSpace(e.Attribution))
            {
                refused.Add($"{e.Id}: {e.License} entry has no attribution string");
                continue;
            }
            byte[] descriptor = Array.Empty<byte>();
            if (e.Descriptor is not null)
            {
                try
                {
                    descriptor = Convert.FromBase64String(e.Descriptor);
                }
                catch (FormatException)
                {
                    // A broken descriptor only weakens the uniqueness rule — keep the entry.
                }
            }
            entries.Add(new BackgroundEntry(
                e.Id,
                e.File,
                e.Size[0],
                e.Size[1],
                e.LayerRole ?? "full",
                e.Tileable ?? false,
                e.Style,
                e.HorizonY,
                (IReadOnlyList<string>?)e.Register ?? Array.Empty<string>(),
                (IReadOnlyList<string>?)e.Tags ?? Array.Empty<string>(),
                e.TraitAffinity ?? new Dictionary<string, float>(),
                e.PaletteGroup ?? "grey",
                (IReadOnlyList<string>?)e.Scene ?? Array.Empty<string>(),
                new BackgroundMetrics(
                    e.Metrics?.SatMean ?? 0f,
                    e.Metrics?.ContrastBand ?? 0f,
                    (IReadOnlyList<int>?)e.Metrics?.DomLightColor ?? new[] { 128, 128, 128 },
                    e.Metrics?.ValMean ?? 0.5f),
                (IReadOnlyList<string>?)e.PairExclude ?? Array.Empty<string>(),
                (IReadOnlyList<string>?)e.Remaps ?? Array.Empty<string>(),
                descriptor,
                ToRect(e.Layers?.Far),
                ToRect(e.Layers?.Mid),
                e.Source ?? "unknown",
                e.License,
                e.Author,
                e.Attribution));
        }
        return new BackgroundLibrary(
            doc.Contract,
            doc.Version ?? "unversioned",
            (IReadOnlyList<string>?)doc.TraitVocabulary ?? Array.Empty<string>(),
            (IReadOnlyList<string>?)doc.RemapTargets ?? Array.Empty<string>(),
            entries,
            refused);
    }

    public static BackgroundLibrary LoadFile(string path) => Parse(File.ReadAllText(path));

    private static BgRect? ToRect(List<int>? r) =>
        r is { Count: 4 } ? new BgRect(r[0], r[1], r[2], r[3]) : null;

    private static readonly JsonSerializerOptions Options = Serialization.JsonOptions.Library;

    // DTOs — the on-disk index shape; unknown fields are ignored by design so the
    // Cowork pipeline can extend the contract without breaking older engines.
    private sealed class IndexDoc
    {
        public string? Contract { get; set; }
        public string? Version { get; set; }
        public List<string>? TraitVocabulary { get; set; }
        public List<string>? RemapTargets { get; set; }
        public List<EntryDoc>? Entries { get; set; }
    }

    private sealed class LayersDoc
    {
        public List<int>? Far { get; set; }
        public List<int>? Mid { get; set; }
    }

    private sealed class MetricsDoc
    {
        public float? SatMean { get; set; }
        public float? ContrastBand { get; set; }
        public List<int>? DomLightColor { get; set; }
        public float? ValMean { get; set; }
    }

    private sealed class EntryDoc
    {
        public string? Id { get; set; }
        public string? File { get; set; }
        public List<int>? Size { get; set; }
        public string? LayerRole { get; set; }
        public bool? Tileable { get; set; }
        public string? Style { get; set; }
        public int? HorizonY { get; set; }
        public List<string>? Register { get; set; }
        public List<string>? Tags { get; set; }
        public Dictionary<string, float>? TraitAffinity { get; set; }
        public string? PaletteGroup { get; set; }
        public List<string>? Scene { get; set; }
        public MetricsDoc? Metrics { get; set; }
        public List<string>? PairExclude { get; set; }
        public List<string>? Remaps { get; set; }
        public string? Descriptor { get; set; }
        public LayersDoc? Layers { get; set; }
        public string? Source { get; set; }
        public string? License { get; set; }
        public string? Author { get; set; }
        public string? Attribution { get; set; }
    }
}
