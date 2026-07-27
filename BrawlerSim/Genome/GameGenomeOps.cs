using BrawlerSim.Determinism;
using BrawlerSim.Params;

namespace BrawlerSim.Genome;

/// <summary>
/// Game-level genetic operations, Unity parity throughout:
/// - Crossover pairs characters positionally (a.P1 × b.P1, a.P2 × b.P2), crosses each
///   character's params, each corresponding move's params, and the stage platform list;
///   sprite genes come from a coin-flipped parent; names/stocks from parent A.
/// - Mutation is all-or-none per game: ONE roll against the mutation rate decides whether
///   every segment mutates (5 param re-rolls each, sprites re-randomized, stage fully
///   regenerated) or none does.
/// </summary>
public static class GameGenomeOps
{
    public static GameGenome Crossover(GameGenome a, GameGenome b, Pcg32 rng, GenerationConfig? config = null)
    {
        config ??= GenerationConfig.Default;
        if (a.Characters.Count != b.Characters.Count)
        {
            throw new ArgumentException(
                $"Cannot cross games with different character counts ({a.Characters.Count} vs {b.Characters.Count}).");
        }

        var children = new List<CharacterGenome>(a.Characters.Count);
        for (int c = 0; c < a.Characters.Count; c++)
        {
            children.Add(CrossoverCharacter(a.Characters[c], b.Characters[c], rng, config));
        }
        StageGenome stage = StageGenome.SinglePointCrossover(a.Stage, b.Stage, rng);
        // Crossover can merge platform lists into a layout one character can't traverse —
        // fit it to both children (2026-07-22, RNG-free; docs/features/spawn-and-polish.md).
        return new GameGenome(children, GameGenome.FitStage(stage, children));
    }

    public static GameGenome Mutate(GameGenome genome, Pcg32 rng, GenerationConfig? config = null)
    {
        config ??= GenerationConfig.Default;
        var mutated = new List<CharacterGenome>(genome.Characters.Count);
        foreach (CharacterGenome character in genome.Characters)
        {
            var moves = config.ButtonComposition is { } composition
                ? MutateComposedMoves(character, composition, config, rng)
                : character.Moves
                    .Select(m => new MoveGenome(
                        GenomeOps.Mutate(m.Params, rng), rng.NextInt(config.MoveSpriteCount), m.Type))
                    .ToList();
            mutated.Add(new CharacterGenome(
                character.Name,
                character.Stocks,
                rng.NextInt(config.PlayerSpriteCount),
                GenomeOps.Mutate(character.Params, rng),
                moves,
                config.IsComposed
                    ? Enumerable.Range(0, character.ButtonMoves.Count).ToArray() // identity invariant
                    : MutateButtonMoves(character, rng)));
        }
        return new GameGenome(mutated, GameGenome.FitStage(MutateStage(genome.Stage, config, rng), mutated));
    }

    /// <summary>
    /// Stage mutation (2026-07-21, Map Size — docs/features/map-size.md). Previously
    /// an unconditional full regeneration; now:
    /// 1. the stage ParamSet mutates via the standard 5-reroll op;
    /// 2. if the mutated `mirrored` gene is ON but the current layout is asymmetric,
    ///    the layout is TRANSFORMED per `mirrorSide` (designer: "mirror left"/"mirror
    ///    right" decides how the transform happens), spawn 2 becoming spawn 1's
    ///    mirror — preserving the layout's character instead of discarding it;
    /// 3. otherwise the layout regenerates from the mutated params (the Unity-parity
    ///    exploration path). A degenerate transform (no platform mass on the chosen
    ///    side) falls back to regeneration.
    /// </summary>
    private static StageGenome MutateStage(StageGenome stage, GenerationConfig config, Pcg32 rng)
    {
        ParamSet mutated = GenomeOps.Mutate(stage.Params, rng);
        if (StageRules.IsMirrored(mutated) && !StageRules.IsSymmetric(stage.Platforms))
        {
            List<PlatformGene>? transformed = StageRules.MirrorTransform(
                stage.Platforms, StageRules.MirrorSideRight(mutated));
            if (transformed is not null)
            {
                // Repair spawn 1 against the transformed layout, then mirror it for
                // spawn 2 — fairness by symmetry, like the generator's mirrored path.
                Determinism.Vec2 s1 = StageRules.RepairSpawn(
                    new Determinism.Vec2(
                        mutated.Get(StageParams.Spawn1X), mutated.Get(StageParams.Spawn1Y)),
                    transformed,
                    mutated.Get(StageParams.VisibleHalfWidth),
                    mutated.Get(StageParams.VisibleHalfHeight));
                ParamSet symmetricSpawns = mutated.With(
                    (StageParams.Spawn1X, s1.X), (StageParams.Spawn1Y, s1.Y),
                    (StageParams.Spawn2X, -s1.X), (StageParams.Spawn2Y, s1.Y));
                return new StageGenome(transformed, symmetricSpawns);
            }
        }
        return config.CreateStageGenerator().Regenerate(mutated, rng);
    }

    /// <summary>
    /// Composed-mode move mutation (2026-07-14, evolve-composition-and-ranges.md): each
    /// RANDOM-spec slot first rolls against TypeRerollRate — success regenerates the
    /// slot wholesale (uniform type draw + fresh params/sprite; landing the same type is
    /// a legitimate full re-roll), failure mutates params as usual. Fixed-spec slots
    /// never roll: no real choice, no RNG (the stream-gating principle).
    /// </summary>
    private static List<MoveGenome> MutateComposedMoves(
        CharacterGenome character, IReadOnlyList<SlotSpec> composition, GenerationConfig config, Pcg32 rng)
    {
        var moves = new List<MoveGenome>(character.Moves.Count);
        for (int i = 0; i < character.Moves.Count; i++)
        {
            MoveGenome m = character.Moves[i];
            if (i < composition.Count && composition[i] == SlotSpec.Random
                && rng.NextFloat() < config.TypeRerollRate)
            {
                moves.Add(MoveGenome.GenerateOfType((MoveType)rng.NextInt(4), config, rng));
                continue;
            }
            moves.Add(new MoveGenome(GenomeOps.Mutate(m.Params, rng), rng.NextInt(config.MoveSpriteCount), m.Type));
        }
        return moves;
    }

    /// <summary>
    /// Button→move genes re-randomize on mutation like the sprite genes, but consume RNG
    /// only when there is a real choice (>1 move) so that single-move games reproduce
    /// pre-feature RNG streams bit-exactly (see docs/features/multi-move-controls.md).
    /// </summary>
    private static int[] MutateButtonMoves(CharacterGenome character, Pcg32 rng)
    {
        var buttonMoves = character.ButtonMoves.ToArray();
        (int mappableButtons, int mappableMoves, int dashSlot) = MappableRange(character);
        if (mappableMoves > 1)
        {
            for (int b = 0; b < mappableButtons; b++)
            {
                buttonMoves[b] = rng.NextInt(mappableMoves);
            }
        }
        CharacterGenome.EnsureButtonCoverage(buttonMoves, mappableMoves, mappableButtons);
        if (dashSlot >= 0)
        {
            buttonMoves[buttonMoves.Length - 1] = dashSlot;
        }
        return buttonMoves;
    }

    /// <summary>The dash pin (2026-07-13): a dash slot (always last when present)
    /// owns the last button; other moves map over the remaining buttons.</summary>
    private static (int MappableButtons, int MappableMoves, int DashSlot) MappableRange(CharacterGenome character)
    {
        int last = character.Moves.Count - 1;
        bool pinned = last >= 0 && character.Moves[last].Type == MoveType.Dash;
        return pinned
            ? (Sim.InputFrame.ActionCount - 1, last, last)
            : (Sim.InputFrame.ActionCount, character.Moves.Count, -1);
    }

    /// <summary>Crossover followed by the single all-or-none mutation roll.</summary>
    public static GameGenome Breed(GameGenome a, GameGenome b, float mutationRate, Pcg32 rng, GenerationConfig? config = null)
    {
        GameGenome child = Crossover(a, b, rng, config);
        if (rng.NextFloat() < mutationRate)
        {
            child = Mutate(child, rng, config);
        }
        return child;
    }

    private static CharacterGenome CrossoverCharacter(CharacterGenome a, CharacterGenome b, Pcg32 rng, GenerationConfig config)
    {
        if (a.Moves.Count != b.Moves.Count)
        {
            throw new ArgumentException(
                $"Cannot cross characters with different move counts ({a.Moves.Count} vs {b.Moves.Count}).");
        }
        ParamSet childParams = GenomeOps.SinglePointCrossover(a.Params, b.Params, rng);
        int spriteIndex = rng.NextInt(2) == 0 ? a.SpriteIndex : b.SpriteIndex;
        var moves = new List<MoveGenome>(a.Moves.Count);
        for (int m = 0; m < a.Moves.Count; m++)
        {
            if (a.Moves[m].Type != b.Moves[m].Type)
            {
                // Mismatched slot types (future dynamic composition): the whole move —
                // type, params, sprite — comes from ONE parent; params of different
                // schemas cannot cross. RNG draw happens only on mismatch, so today's
                // fixed compositions consume no extra stream.
                moves.Add(rng.NextInt(2) == 0 ? a.Moves[m] : b.Moves[m]);
                continue;
            }
            ParamSet moveParams = GenomeOps.SinglePointCrossover(a.Moves[m].Params, b.Moves[m].Params, rng);
            int moveSprite = rng.NextInt(2) == 0 ? a.Moves[m].SpriteIndex : b.Moves[m].SpriteIndex;
            moves.Add(new MoveGenome(moveParams, moveSprite, a.Moves[m].Type));
        }
        if (config.IsComposed)
        {
            // Composed mode: buttons are identity by structural invariant — nothing to
            // cross, no draws consumed.
            return new CharacterGenome(a.Name, a.Stocks, spriteIndex, childParams, moves,
                Enumerable.Range(0, a.ButtonMoves.Count).ToArray());
        }
        // Per-button coin flip between parents, RNG-gated like MutateButtonMoves: with a
        // single move both parents' genes are identical zeros, so no draw is consumed.
        var buttonMoves = a.ButtonMoves.ToArray();
        (int mappableButtons, int mappableMoves, int dashSlot) = MappableRange(a);
        if (mappableMoves > 1)
        {
            for (int btn = 0; btn < mappableButtons; btn++)
            {
                buttonMoves[btn] = rng.NextInt(2) == 0 ? a.ButtonMoves[btn] : b.ButtonMoves[btn];
            }
        }
        CharacterGenome.EnsureButtonCoverage(buttonMoves, mappableMoves, mappableButtons);
        if (dashSlot >= 0)
        {
            buttonMoves[buttonMoves.Length - 1] = dashSlot;
        }
        return new CharacterGenome(a.Name, a.Stocks, spriteIndex, childParams, moves, buttonMoves);
    }
}
