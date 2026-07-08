using BrawlerSim.Genome;

namespace BrawlerSim.Serialization;

/// <summary>
/// The persisted unit: one genome plus identifying metadata. Serializes to a single
/// game.json (see GameGenomeJson). Kept deliberately flat so a future move to a database
/// is a storage-adapter swap — one record, one document.
/// </summary>
public sealed class GameRecord
{
    public string Name { get; }

    /// <summary>Freeform provenance, e.g. "unity-import:GameC" or "evolved:run42/gen118/game7".</summary>
    public string? Origin { get; }

    public GameGenome Genome { get; }

    public GameRecord(string name, string? origin, GameGenome genome)
    {
        Name = name;
        Origin = origin;
        Genome = genome;
    }
}
