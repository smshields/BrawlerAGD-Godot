using BrawlerSim.Genome;

namespace BrawlerSim.Serialization;

/// <summary>
/// The persisted unit: one genome plus identifying metadata. Serializes to a single
/// game.json (see GameGenomeJson). Kept deliberately flat so a future move to a database
/// is a storage-adapter swap — one record, one document.
///
/// VOCABULARY (2026-09-01 — "game" carries two meanings; this table is the map):
///   GameRecord / game.json      ONE evolved genome: 2-4 fighters + 1 stage. The
///                               atom evolution produces and matches consume.
///   BuiltGame / built-game.json A curated COMPILATION: exactly 8 fighters + 4
///                               stages, self-contained (BuiltGame.cs/BuiltGameJson).
///                               What the Game Builder edits and PLAY GAME opens.
///   BuiltGamePresentation       The engine-side names+sprites+themes pass run once
///                               at game-open/prep-game (negotiated presentation).
///   BuiltGamePresenter (godot/) The app-side wrapper that supplies the generator
///                               and saves the result.
///   BuiltCharacter.Presented    The genome WITH its negotiated presentation
///                               injected — what the sim actually renders.
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
