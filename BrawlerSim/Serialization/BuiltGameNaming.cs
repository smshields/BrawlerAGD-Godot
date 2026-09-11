using System.Text;
using System.Text.RegularExpressions;
using BrawlerSim.Determinism;
using BrawlerSim.Genome;

namespace BrawlerSim.Serialization;

/// <summary>
/// Naming rules for built games (Game Player, 2026-08-14, FEATURES.md §Game Menu
/// item 6; docs/features/game-player.md). Names are GENERATED ON GAME OPEN in the
/// player and persisted once (designer); an element still needs a name when its
/// display name is empty or still the Game Builder's provenance-default shape.
/// This class holds the engine-free, namegen-free half (pattern + seeds) so the
/// rules are unit-testable in the sim suite; the generation glue lives in the app
/// layer, which is where the namegen dependency ships.
/// </summary>
public static class BuiltGameNaming
{
    // The builder's defaults are "<SOURCE-FILE> P<n>" and "<SOURCE-FILE> STAGE",
    // where the label is an uppercased file name (letters/digits/_ . -). Manual
    // renames almost never match this shape, so they survive the naming pass.
    private static readonly Regex DefaultShape =
        new(@"^[A-Z0-9_.\-]+ (P\d+|STAGE)$", RegexOptions.Compiled);

    // Regression 2026-09-11: the evolve screen's default run names became
    // "EVOLUTION N" (2026-09-10 UI rework), so favorite FILE names — and therefore
    // builder labels — now contain SPACES ("EVOLUTION 3-B264-GAME69 P1"). Those
    // labels failed DefaultShape and were treated as manual renames: the naming
    // pass skipped them forever while sprites/registers/backgrounds presented fine.
    // A spacey LABEL counts as a builder default only when the label itself carries
    // a digit (every run-derived file stem embeds gen/batch/game numbers), so actual
    // manual renames like "LAVA STAGE" or "MY COOL P2" still survive the pass.
    private static readonly Regex SpaceyDefaultShape =
        new(@"^[A-Z0-9_. \-]*\d[A-Z0-9_. \-]* (P\d+|STAGE)$", RegexOptions.Compiled);

    /// <summary>True when the player's namegen pass should (re)name this element.</summary>
    public static bool NeedsGeneratedName(string? displayName) =>
        string.IsNullOrWhiteSpace(displayName)
        || DefaultShape.IsMatch(displayName.Trim())
        || SpaceyDefaultShape.IsMatch(displayName.Trim());

    /// <summary>Deterministic naming seed from the element's CONTENT — the same
    /// fighter gets the same name no matter which built game or session names it
    /// first (reproducibility doctrine).</summary>
    public static ulong NamingSeed(CharacterGenome character) =>
        Fnv1a.Hash(Encoding.UTF8.GetBytes(BuiltGame.ContentKey(character)), Fnv1a.OffsetBasis);

    public static ulong NamingSeed(StageGenome stage) =>
        Fnv1a.Hash(Encoding.UTF8.GetBytes(BuiltGame.ContentKey(stage)), Fnv1a.OffsetBasis);
}
