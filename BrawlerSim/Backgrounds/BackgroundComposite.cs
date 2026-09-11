namespace BrawlerSim.Backgrounds;

/// <summary>
/// The parsed form of a recombined background gene (backgrounds track Phase 2,
/// 2026-09-02 — brief decision 1): far, mid, and — since v0.4 (2026-09-11, designer:
/// the near layer joins the gene) — the near bokeh entry, plus the UNIFIED palette
/// remap, serialized as "far:&lt;id&gt;|mid:&lt;id&gt;|near:&lt;id|none&gt;|remap:
/// &lt;group|none&gt;". A composite inherits and repairs AS A UNIT — repair
/// re-resolves the whole gene, never one layer, so children cannot drift into
/// illegal stacks. A plain entry id (no "far:" prefix) is the single-image gene —
/// the EXTREME fallback since v0.4, kept loadable for archived content.
/// HasNear distinguishes a pre-v0.4 three-part gene (near key absent — repairs into
/// the three-layer stack) from a modern gene whose near legitimately resolved to
/// "none" (an empty near pool, accepted as-is so repair terminates).
/// </summary>
public sealed record BackgroundComposite(
    string FarId, string MidId, string? NearId, string? Remap, bool HasNear = true)
{
    public const string None = "none";

    public string ToGene() =>
        $"far:{FarId}|mid:{MidId}|near:{NearId ?? None}|remap:{Remap ?? None}";

    /// <summary>True when the gene string is a composite (vs a single entry id).</summary>
    public static bool IsComposite(string? gene) =>
        gene is not null && gene.StartsWith("far:", StringComparison.Ordinal);

    public static BackgroundComposite? TryParse(string? gene)
    {
        if (gene is null || !IsComposite(gene))
        {
            return null;
        }
        string? far = null, mid = null, near = null, remap = null;
        bool hasNear = false;
        foreach (string part in gene.Split('|'))
        {
            int colon = part.IndexOf(':');
            if (colon <= 0)
            {
                return null;
            }
            string key = part[..colon];
            string value = part[(colon + 1)..];
            switch (key)
            {
                case "far": far = value; break;
                case "mid": mid = value; break;
                case "near": near = value; hasNear = true; break;
                case "remap": remap = value; break;
                default: return null;
            }
        }
        if (string.IsNullOrEmpty(far) || string.IsNullOrEmpty(mid) || remap is null)
        {
            return null;
        }
        return new BackgroundComposite(far, mid,
            near is null or None or "" ? null : near,
            remap == None ? null : remap,
            hasNear);
    }
}
