namespace BrawlerSim.Backgrounds;

/// <summary>The credits screen's data model (backgrounds track Phase 1 — the legal
/// gate for attribution-bearing licenses). Built from the SAME index the renderer
/// uses, so credits can never drift from shipped content: every distinct required
/// attribution string (CC-BY / OGA-BY), grouped under its author, plus a courtesy
/// list of CC0/PD authors (project practice since the DCSS credit).</summary>
public sealed record BackgroundCreditsModel(
    IReadOnlyList<BackgroundCreditsModel.AuthorCredits> Required,
    IReadOnlyList<string> CourtesyAuthors)
{
    public sealed record AuthorCredits(string Author, IReadOnlyList<string> Attributions);
}

public static class BackgroundCredits
{
    /// <summary>Every distinct attribution string in the library, grouped by author
    /// and ordered for stable rendering; courtesy authors are the distinct authors of
    /// CC0/PD entries. Entries the library refused never reach the model — they also
    /// never reach the renderer, which is the invariant that matters.</summary>
    public static BackgroundCreditsModel Build(BackgroundLibrary library)
    {
        var required = new SortedDictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        var courtesy = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (BackgroundEntry entry in library.Entries)
        {
            if (BackgroundLibrary.RequiresAttribution(entry.License))
            {
                string author = entry.Author ?? "Unknown";
                if (!required.TryGetValue(author, out SortedSet<string>? strings))
                {
                    strings = new SortedSet<string>(StringComparer.Ordinal);
                    required[author] = strings;
                }
                strings.Add(entry.Attribution!);
            }
            else if (!string.IsNullOrWhiteSpace(entry.Author))
            {
                courtesy.Add(entry.Author);
            }
        }
        return new BackgroundCreditsModel(
            required.Select(kv => new BackgroundCreditsModel.AuthorCredits(
                kv.Key, kv.Value.ToList())).ToList(),
            courtesy.ToList());
    }
}
