using BrawlerSim.Genome;
using BrawlerSim.Sprites;
using NG = NameGen;

namespace BrawlerSim.Serialization;

/// <summary>
/// The presentation pass over a built game (2026-08-22, sprite selection —
/// docs/features/sprite-selection.md decision 3): names, sprites, and their shared
/// register are settled TOGETHER at game open (or packaging) and persisted once.
/// This is the single implementation behind both consumers — the Game Player's
/// game-open hook (godot/src/BuiltGamePresenter.cs) and BrawlerRunner prep-game —
/// which previously each carried their own copy of the naming loop.
///
/// Per character: a name-needing entry runs the full sprite/name negotiation (the
/// inherited gene is candidate #1, so heredity usually wins; per-roster usage counts
/// make duplicate fighters diverge); an entry with a kept name (manual rename or
/// previously generated) gets its sprite resolved around the kept name without
/// touching it. Stages have no sprites — naming only, exactly as before. Deterministic:
/// everything derives from BuiltGameNaming.NamingSeed (content-derived, sprite-blind).
/// </summary>
public static class BuiltGamePresentation
{
    /// <summary>Runs the pass in place; returns how many elements changed. A null
    /// selector (sprite library unavailable) degrades to the pure naming pass.</summary>
    public static int EnsurePresented(BuiltGame game, NG.NameGenerator generator, SpriteSelector? selector)
    {
        var session = new NG.UniqueNameSession(generator);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (BuiltCharacter c in game.Characters.Where(c => !BuiltGameNaming.NeedsGeneratedName(c.DisplayName)))
        {
            session.Reserve(c.DisplayName);
            taken.Add(c.DisplayName);
        }
        foreach (BuiltStage s in game.Stages.Where(s => !BuiltGameNaming.NeedsGeneratedName(s.DisplayName)))
        {
            session.Reserve(s.DisplayName);
            taken.Add(s.DisplayName);
        }

        // Sprites already settled on this roster weigh into the overuse penalty, so a
        // partially presented game (v1 file gaining sprites, a builder addition) still
        // diverges its new picks from the kept ones.
        var usage = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (BuiltCharacter c in game.Characters)
        {
            if (c.SpriteId is { } id)
            {
                usage[id] = usage.TryGetValue(id, out int n) ? n + 1 : 1;
            }
        }

        // Melee move sprites (M4b): ids already persisted on the roster pre-count
        // into the cross-character duplicate penalty, exactly like character usage.
        var moveUsage = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (BuiltCharacter c in game.Characters)
        {
            foreach (string? id in c.MoveSpriteIds ?? Array.Empty<string?>())
            {
                if (id is not null)
                {
                    moveUsage[id] = moveUsage.TryGetValue(id, out int n) ? n + 1 : 1;
                }
            }
        }

        int changed = 0;
        for (int i = 0; i < game.Characters.Count; i++)
        {
            BuiltCharacter entry = game.Characters[i];
            bool needsName = BuiltGameNaming.NeedsGeneratedName(entry.DisplayName);
            bool needsSprite = selector is not null
                && (entry.SpriteId is null || !selector.Library.Contains(entry.SpriteId));
            if (!needsName && !needsSprite)
            {
                changed += EnsureMoveSprites(game, i, selector, moveUsage);
                continue;
            }
            ulong seed = BuiltGameNaming.NamingSeed(entry.Character);

            // Roster distinctness beats heredity here (the overuse rule's intent —
            // "duplicate fighters diverge"): an inherited sprite ALREADY worn by an
            // earlier roster entry loses its candidate-#1 privilege and the entry
            // selects fresh, with the usage penalty steering it elsewhere.
            string? inherited = entry.SpriteId ?? entry.Character.SpriteId;
            if (inherited is not null && usage.ContainsKey(inherited))
            {
                inherited = null;
            }

            if (needsName && selector is not null)
            {
                SpritePresentation presented = selector.Negotiate(
                    entry.Character, seed, generator, usage,
                    inheritedSpriteId: inherited,
                    nameTaken: taken.Contains);
                string name = presented.DisplayName;
                if (taken.Contains(name))
                {
                    // Negotiation's last-resort roll collided — the uniqueness session
                    // gets the final word (derived-seed retries), register kept.
                    name = session.GenerateCharacterName(
                        SpriteSelector.Map(entry.Character),
                        new NG.NameOptions { Seed = seed, Register = presented.Register }).Display;
                }
                else
                {
                    session.Reserve(name);
                }
                taken.Add(name);
                Count(usage, presented.SpriteId);
                game.Characters[i] = entry with
                {
                    DisplayName = name,
                    SpriteId = presented.SpriteId,
                    Register = presented.Register,
                };
                changed++;
                changed += EnsureMoveSprites(game, i, selector, moveUsage);
                continue;
            }
            if (needsName)
            {
                // No sprite library: the pre-feature naming pass, byte-for-byte.
                string name = session.GenerateCharacterName(
                    SpriteSelector.Map(entry.Character), new NG.NameOptions { Seed = seed }).Display;
                taken.Add(name);
                game.Characters[i] = entry with { DisplayName = name };
                changed++;
                continue;
            }

            // Kept name, missing sprite: resolve the sprite around it — the genome's
            // inherited gene when it still makes sense (and no earlier entry wears it),
            // else the seeded top candidate; the register comes from the same shared
            // seed either way.
            IReadOnlyList<SpriteCandidate> candidates =
                selector!.SelectCandidates(entry.Character, seed, out string register, usage);
            string spriteId = inherited is not null
                && selector.Library.Contains(inherited) && !selector.NeedsRepair(entry.Character)
                    ? inherited
                    : candidates[0].Sprite.Id;
            Count(usage, spriteId);
            game.Characters[i] = entry with { SpriteId = spriteId, Register = register };
            changed++;
            changed += EnsureMoveSprites(game, i, selector, moveUsage);
        }

        for (int i = 0; i < game.Stages.Count; i++)
        {
            BuiltStage entry = game.Stages[i];
            if (!BuiltGameNaming.NeedsGeneratedName(entry.DisplayName))
            {
                continue;
            }
            string name = session.GenerateStageName(
                new NG.StageGenome(entry.Stage.Params.ToDictionary()),
                new NG.NameOptions { Seed = BuiltGameNaming.NamingSeed(entry.Stage) }).Display;
            game.Stages[i] = entry with { DisplayName = name };
            changed++;
        }
        return changed;
    }

    /// <summary>Melee move sprites for one roster entry (M4b): resolved around the
    /// NEGOTIATED character sprite and persisted once — entries that already carry a
    /// full, valid list keep it. Returns 1 when the entry changed, else 0; newly
    /// resolved ids join the cross-character usage pool.</summary>
    private static int EnsureMoveSprites(BuiltGame game, int i, SpriteSelector? selector,
        Dictionary<string, int> moveUsage)
    {
        if (selector?.MoveLibrary is null)
        {
            return 0;
        }
        BuiltCharacter entry = game.Characters[i];
        SpriteDef? presentedSprite = selector.Library.ById(entry.SpriteId ?? entry.Character.SpriteId);
        if (presentedSprite is null)
        {
            return 0; // no resolvable look to wield around (no character library data)
        }
        if (entry.MoveSpriteIds is { } existing && existing.Count == entry.Character.Moves.Count
            && Enumerable.Range(0, existing.Count).All(m =>
                entry.Character.Moves[m].Type != MoveType.Attack
                    ? existing[m] is null
                    : selector.MoveLibrary.Contains(existing[m])))
        {
            return 0; // persisted once — settled lists stand
        }
        ulong seed = BuiltGameNaming.NamingSeed(entry.Character);
        string register = entry.Register ?? selector.PickRegister(seed);
        IReadOnlyList<string?> ids = selector.ResolveMoveSpriteIds(
            entry.Character, presentedSprite, register, moveUsage);
        foreach (string? id in ids)
        {
            if (id is not null)
            {
                Count(moveUsage, id);
            }
        }
        game.Characters[i] = entry with { MoveSpriteIds = ids };
        return 1;
    }

    private static void Count(Dictionary<string, int> usage, string id) =>
        usage[id] = usage.TryGetValue(id, out int n) ? n + 1 : 1;
}
