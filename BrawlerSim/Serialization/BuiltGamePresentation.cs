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
/// touching it. Stages settle their tile THEME with their name the same way since
/// M4d (2026-09-01, stage-tile-selection.md) — shared register by construction.
/// Deterministic: everything derives from BuiltGameNaming.NamingSeed
/// (content-derived, sprite- and theme-blind).
/// </summary>
public static class BuiltGamePresentation
{
    /// <summary>Runs the pass in place; returns how many elements changed. A null
    /// selector (library unavailable) degrades that half to the pure naming pass.</summary>
    public static int EnsurePresented(BuiltGame game, NG.NameGenerator generator, SpriteSelector? selector,
        StageThemeSelector? themes = null, Backgrounds.BackgroundSelector? backgrounds = null)
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
                Count(usage, id);
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
                    Count(moveUsage, id);
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

        // Stage themes settled on this game pre-count into the overuse penalty, so a
        // partially presented game (a v4 file gaining themes) still diverges its new
        // picks from the kept ones — the four-stage lineup reads as four places.
        var themeUsage = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (BuiltStage s in game.Stages)
        {
            if (s.ThemeId is { } id)
            {
                Count(themeUsage, id);
            }
        }

        // Backgrounds settled on this game pre-count into the overuse penalty AND the
        // lineup descriptor-distance rule (2026-09-02, backgrounds track), so a v5
        // file gaining backgrounds diverges its new picks from the kept ones.
        var backgroundUsage = new Dictionary<string, int>(StringComparer.Ordinal);
        var usedDescriptors = new List<byte[]>();
        void CountBackground(string gene)
        {
            // Usage and descriptors are tracked per ENTRY: a composite gene counts
            // its far and mid parts (that is what the overuse penalty scores).
            Backgrounds.BackgroundSpec? spec = backgrounds!.ParseGene(gene);
            if (spec is null)
            {
                return;
            }
            foreach (Backgrounds.BackgroundEntry e in spec.IsComposite
                ? new[] { spec.Far!, spec.Mid! } : new[] { spec.Single! })
            {
                Count(backgroundUsage, e.Id);
                if (e.Descriptor.Length > 0)
                {
                    usedDescriptors.Add(e.Descriptor);
                }
            }
        }
        if (backgrounds is not null)
        {
            foreach (BuiltStage s in game.Stages)
            {
                if (s.BackgroundId is { } gene)
                {
                    CountBackground(gene);
                }
            }
        }

        for (int i = 0; i < game.Stages.Count; i++)
        {
            BuiltStage entry = game.Stages[i];
            bool needsName = BuiltGameNaming.NeedsGeneratedName(entry.DisplayName);
            bool needsTheme = themes is not null
                && (entry.ThemeId is null || !themes.Library.Contains(entry.ThemeId));
            bool needsBackground = backgrounds is not null
                && backgrounds.ParseGene(entry.BackgroundId) is null; // unset or unknown (composite-aware)
            if (!needsName && !needsTheme && !needsBackground)
            {
                continue;
            }
            ulong seed = BuiltGameNaming.NamingSeed(entry.Stage);
            bool entryChanged = false;

            if (needsName || needsTheme)
            {
                // Lineup distinctness beats heredity (the roster rule, applied to
                // stages): an inherited theme already worn by an earlier stage loses its
                // privilege and this entry selects fresh, steered by the usage penalty.
                string? inherited = entry.ThemeId ?? entry.Stage.ThemeId;
                if (inherited is not null && themeUsage.ContainsKey(inherited))
                {
                    inherited = null;
                }

                if (themes is not null)
                {
                    ThemePresentation presented = themes.Present(
                        entry.Stage, seed, generator, themeUsage, inherited);
                    string stageName = entry.DisplayName;
                    if (needsName)
                    {
                        stageName = presented.DisplayName;
                        if (taken.Contains(stageName))
                        {
                            stageName = session.GenerateStageName(
                                StageThemeSelector.Map(entry.Stage),
                                new NG.NameOptions { Seed = seed, Register = presented.Register }).Display;
                        }
                        else
                        {
                            session.Reserve(stageName);
                        }
                        taken.Add(stageName);
                    }
                    Count(themeUsage, presented.ThemeId);
                    entry = entry with
                    {
                        DisplayName = stageName,
                        ThemeId = presented.ThemeId,
                        Register = presented.Register,
                    };
                    entryChanged = true;
                }
                else if (needsName)
                {
                    // No theme library: the pre-feature stage naming pass, byte-for-byte.
                    string name = session.GenerateStageName(
                        StageThemeSelector.Map(entry.Stage),
                        new NG.NameOptions { Seed = seed }).Display;
                    entry = entry with { DisplayName = name };
                    entryChanged = true;
                }
            }

            if (needsBackground)
            {
                // Same lineup-distinctness rule for the background gene; the settled
                // TILE THEME feeds palette harmony (backgrounds follow tiles).
                string? inheritedBg = entry.BackgroundId ?? entry.Stage.BackgroundId;
                if (inheritedBg is not null && backgrounds!.ParseGene(inheritedBg) is { } inh
                    && (inh.IsComposite
                        ? backgroundUsage.ContainsKey(inh.Far!.Id) || backgroundUsage.ContainsKey(inh.Mid!.Id)
                        : backgroundUsage.ContainsKey(inh.Single!.Id)))
                {
                    inheritedBg = null;
                }
                Backgrounds.BackgroundPresentation bg = backgrounds!.Present(
                    entry.Stage, seed, entry.ThemeId ?? entry.Stage.ThemeId,
                    backgroundUsage, usedDescriptors, inheritedBg);
                CountBackground(bg.BackgroundId);
                entry = entry with
                {
                    BackgroundId = bg.BackgroundId,
                    BackgroundRemap = bg.Remap,
                    Register = entry.Register ?? bg.Register,
                };
                entryChanged = true;
            }

            if (entryChanged)
            {
                game.Stages[i] = entry;
                changed++;
            }
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
