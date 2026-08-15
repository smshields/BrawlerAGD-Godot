using System.Linq;
using BrawlerSim.Genome;
using BrawlerSim.Serialization;
using NG = NameGen;

namespace BrawlerGodot;

/// <summary>
/// The namegen pass over a built game (Game Player item 6, 2026-08-14 —
/// docs/features/game-player.md). Runs when a game is OPENED for play: every
/// element whose display name is empty or still the builder's provenance-default
/// shape gets a namegen name — seeded from the element's CONTENT so the same
/// fighter names identically everywhere — and the doc is PERSISTED once. Manual
/// renames and previously generated names are left alone (BuiltGameNaming owns
/// the rules; this class is the namegen glue and the app's only naming entry).
/// </summary>
public static class BuiltGameNamer
{
    private static NG.NameGenerator? _generator;

    private static NG.NameGenerator Generator => _generator ??= NG.NameGenerator.CreateDefault();

    /// <summary>Names what needs naming; saves and returns true when anything changed.</summary>
    public static bool EnsureNamed(BuiltGame game, string path)
    {
        var session = new NG.UniqueNameSession(Generator);
        foreach (BuiltCharacter c in game.Characters.Where(c => !BuiltGameNaming.NeedsGeneratedName(c.DisplayName)))
        {
            session.Reserve(c.DisplayName);
        }
        foreach (BuiltStage s in game.Stages.Where(s => !BuiltGameNaming.NeedsGeneratedName(s.DisplayName)))
        {
            session.Reserve(s.DisplayName);
        }

        bool changed = false;
        for (int i = 0; i < game.Characters.Count; i++)
        {
            BuiltCharacter entry = game.Characters[i];
            if (!BuiltGameNaming.NeedsGeneratedName(entry.DisplayName))
            {
                continue;
            }
            string name = session.GenerateCharacterName(
                Map(entry.Character),
                new NG.NameOptions { Seed = BuiltGameNaming.NamingSeed(entry.Character) }).Display;
            game.Characters[i] = entry with { DisplayName = name };
            changed = true;
        }
        for (int i = 0; i < game.Stages.Count; i++)
        {
            BuiltStage entry = game.Stages[i];
            if (!BuiltGameNaming.NeedsGeneratedName(entry.DisplayName))
            {
                continue;
            }
            string name = session.GenerateStageName(
                Map(entry.Stage),
                new NG.NameOptions { Seed = BuiltGameNaming.NamingSeed(entry.Stage) }).Display;
            game.Stages[i] = entry with { DisplayName = name };
            changed = true;
        }
        if (changed)
        {
            BuiltGameJson.Save(game, path);
        }
        return changed;
    }

    /// <summary>The genome mapping proven by NameGenIntegrationTests.</summary>
    private static NG.CharacterGenome Map(CharacterGenome character) => new(
        character.Params.ToDictionary(),
        character.Moves.Select(m => new NG.MoveGenome(m.Type switch
        {
            MoveType.Shield => NG.MoveKind.Shield,
            MoveType.Dash => NG.MoveKind.Dash,
            MoveType.Projectile => NG.MoveKind.Projectile,
            _ => NG.MoveKind.Melee,
        }, m.Params.ToDictionary())).ToList());

    private static NG.StageGenome Map(StageGenome stage) => new(stage.Params.ToDictionary());
}
