namespace BrawlerSim.Backgrounds;

/// <summary>
/// The parsed form of a recombined background gene (backgrounds track Phase 2,
/// 2026-09-02 — brief decision 1): far and mid layer entries plus the UNIFIED palette
/// remap, serialized as "far:&lt;id&gt;|mid:&lt;id&gt;|remap:&lt;group|none&gt;". A
/// composite inherits and repairs AS A UNIT — repair re-resolves the whole gene,
/// never one layer, so children cannot drift into illegal pairs. A plain entry id
/// (no "far:" prefix) is the Phase-1 single-image gene.
/// </summary>
public sealed record BackgroundComposite(string FarId, string MidId, string? Remap)
{
    public const string NoneRemap = "none";

    public string ToGene() => $"far:{FarId}|mid:{MidId}|remap:{Remap ?? NoneRemap}";

    /// <summary>True when the gene string is a composite (vs a single entry id).</summary>
    public static bool IsComposite(string? gene) =>
        gene is not null && gene.StartsWith("far:", StringComparison.Ordinal);

    public static BackgroundComposite? TryParse(string? gene)
    {
        if (gene is null || !IsComposite(gene))
        {
            return null;
        }
        string? far = null, mid = null, remap = null;
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
                case "remap": remap = value; break;
                default: return null;
            }
        }
        if (string.IsNullOrEmpty(far) || string.IsNullOrEmpty(mid) || remap is null)
        {
            return null;
        }
        return new BackgroundComposite(far, mid, remap == NoneRemap ? null : remap);
    }
}
