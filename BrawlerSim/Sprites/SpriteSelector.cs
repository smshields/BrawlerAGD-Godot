using BrawlerSim.Genome;
using NameGen;
using NameGen.Data;
using NameGen.Features;
using NameGen.Traits;
using NgPcg = NameGen.Core.Pcg32;

namespace BrawlerSim.Sprites;

/// <summary>A scored sprite candidate, ordered by the seeded softmax sample.</summary>
public sealed record SpriteCandidate(SpriteDef Sprite, double Score);

/// <summary>The negotiated presentation of one fighter: sprite, display name, and the
/// shared register that keeps them coherent (sprite-selection.md decision 3).</summary>
public sealed record SpritePresentation(string SpriteId, string DisplayName, string Register);

/// <summary>
/// Semantic sprite selection (2026-08-22, docs/features/sprite-selection.md).
/// Deterministic: same genome + same seed = same (sprite, name, register) everywhere —
/// no wall clock, no global RNG; the namegen Pcg32 on a sprite-private sequence, so
/// selection never perturbs the naming stream. Reuses the namegen FeatureExtractor +
/// TraitScorer exactly as naming does (decision: do not re-derive), and picks the
/// register through the same weighted register data, so sprite theme and name register
/// can never disagree.
/// </summary>
public sealed class SpriteSelector
{
    /// <summary>Private Pcg32 sequence for sprite draws — independent of the naming
    /// stream (which runs on the Pcg32 default sequence) even under the same seed.</summary>
    private const ulong SpriteSequence = 0x5350524954453276UL; // "SPRITE2v"

    /// <summary>Move-sprite draws get their own sequence too, so per-move sampling
    /// can never collide with character selection under related seeds.</summary>
    private const ulong MoveSequence = 0x4d4f56455350524bUL; // "MOVESPRK"

    private readonly NameGenData _data;
    private readonly FeatureExtractor _extractor;

    public SpriteLibrary Library { get; }
    public SpriteSelectionConfig Config { get; }

    /// <summary>The melee attack library (M4b, 2026-08-23). Null = attack sprite
    /// selection off; genomes keep null move genes.</summary>
    public MoveSpriteLibrary? MoveLibrary { get; }

    public SpriteSelector(SpriteLibrary library, SpriteSelectionConfig? config = null, NameGenData? data = null,
        MoveSpriteLibrary? moveLibrary = null)
    {
        Library = library;
        Config = config ?? SpriteSelectionConfig.Default;
        MoveLibrary = moveLibrary;
        _data = data ?? NameGenData.LoadEmbedded();
        _extractor = new FeatureExtractor(_data.Ranges);
    }

    /// <summary>The genome mapping proven by NameGenIntegrationTests (lifted from the
    /// app layer's BuiltGameNamer when selection moved engine-side).</summary>
    public static NameGen.CharacterGenome Map(Genome.CharacterGenome character) => new(
        character.Params.ToDictionary(),
        character.Moves.Select(m => new NameGen.MoveGenome(m.Type switch
        {
            MoveType.Shield => MoveKind.Shield,
            MoveType.Dash => MoveKind.Dash,
            MoveType.Projectile => MoveKind.Projectile,
            _ => MoveKind.Melee,
        }, m.Params.ToDictionary())).ToList());

    /// <summary>Salient traits exactly as naming derives them.</summary>
    public IReadOnlyList<SalientTrait> Salient(Genome.CharacterGenome character)
    {
        FeatureVector features = _extractor.ExtractCharacter(Map(character));
        return TraitScorer.SelectSalient(features, _data.Traits.Character,
            _data.Traits.SalienceTopK, _data.Traits.SalienceThreshold);
    }

    /// <summary>Raw trait-affinity score (the repair metric, "before normalization"):
    /// sum over salient traits of salience × the sprite's affinity for that trait.</summary>
    public static double AffinityScore(SpriteDef sprite, IReadOnlyList<SalientTrait> salient)
    {
        double score = 0;
        foreach (SalientTrait trait in salient)
        {
            if (sprite.TraitAffinity.TryGetValue(trait.Name, out float affinity))
            {
                score += trait.Score * affinity;
            }
        }
        return score;
    }

    /// <summary>Repair rule (decision 2): a sprite needs re-resolving when it is
    /// missing/unknown, or when the genome HAS salient traits and the sprite's
    /// affinity score against them falls below the floor. A neutral genome (no
    /// salient traits) contradicts nothing — heredity stands.</summary>
    public bool NeedsRepair(Genome.CharacterGenome character)
    {
        SpriteDef? sprite = Library.ById(character.SpriteId);
        if (sprite is null)
        {
            return true;
        }
        IReadOnlyList<SalientTrait> salient = Salient(character);
        return salient.Count > 0 && AffinityScore(sprite, salient) < Config.RepairFloor;
    }

    /// <summary>Steps 1–5 of the selection algorithm: salient traits → seeded register
    /// pick → register pool (full-library fallback below the pool floor) → scoring
    /// (affinity + aspect bonus − overuse) → distribution shaping (goof budget, biped
    /// cap) → seeded softmax sample of an ordered candidate list.</summary>
    public IReadOnlyList<SpriteCandidate> SelectCandidates(
        Genome.CharacterGenome character, ulong seed, out string register,
        IReadOnlyDictionary<string, int>? priorUse = null)
    {
        var rng = new NgPcg(seed, SpriteSequence);
        string picked = PickRegister(rng);
        register = picked;

        var pool = Library.Sprites.Where(s => s.Registers.Contains(picked)).ToList();
        if (pool.Count < Config.RegisterPoolFloor)
        {
            pool = Library.Sprites.ToList();
        }

        IReadOnlyList<SalientTrait> salient = Salient(character);
        double aspect = CharacterAspect(character);
        var scores = new double[pool.Count];
        for (int i = 0; i < pool.Count; i++)
        {
            scores[i] = CandidateScore(pool[i], salient, aspect, priorUse);
        }

        double[] probs = Softmax(scores, Config.SoftmaxTemperature);
        ShapeDistribution(pool, probs);

        int k = Math.Min(Config.CandidateCount, pool.Count);
        var ordered = new List<SpriteCandidate>(k);
        for (int draw = 0; draw < k; draw++)
        {
            int index = SampleIndex(probs, rng);
            ordered.Add(new SpriteCandidate(pool[index], scores[index]));
            probs[index] = 0;
        }
        return ordered;
    }

    /// <summary>Full candidate score: trait affinity + aspect bonus − overuse.</summary>
    public double CandidateScore(SpriteDef sprite, IReadOnlyList<SalientTrait> salient,
        double characterAspect, IReadOnlyDictionary<string, int>? priorUse)
    {
        double score = AffinityScore(sprite, salient);
        // Aspect bonus: reward sprites whose drawn w/h ratio matches the genome's
        // widthScalar/heightScalar ratio; similarity falls off on the log-ratio.
        double similarity = 1.0 - Math.Min(1.0, Math.Abs(Math.Log(sprite.Aspect / characterAspect)));
        score += Config.AspectBonusWeight * similarity;
        if (priorUse is not null && priorUse.TryGetValue(sprite.Id, out int uses))
        {
            score -= Config.OverusePenalty * uses;
        }
        return score;
    }

    /// <summary>Resolve the SpriteId GENE: the first candidate of the seeded sample.
    /// Used at generation and by the validation-time repair rule.</summary>
    public string ResolveSpriteId(Genome.CharacterGenome character, ulong seed,
        IReadOnlyDictionary<string, int>? priorUse = null) =>
        SelectCandidates(character, seed, out _, priorUse)[0].Sprite.Id;

    /// <summary>Gene upkeep for the breeding pipeline: assigns a sprite to a genome
    /// that needs one (fresh generation, unknown id, or the repair floor fired) using
    /// the CONTENT-derived seed, and returns the genome unchanged otherwise. RNG-free
    /// with respect to the evolution stream.</summary>
    public Genome.CharacterGenome EnsureGene(Genome.CharacterGenome character,
        IReadOnlyDictionary<string, int>? priorUse = null)
    {
        if (!NeedsRepair(character))
        {
            return character;
        }
        ulong seed = SpriteSeed(character);
        return character.WithSpriteId(ResolveSpriteId(character, seed, priorUse));
    }

    /// <summary>The shared seed (decision 3): identical to the naming seed, derived
    /// from the character's content EXCLUDING the sprite gene — so selection at
    /// generation time and naming at game-open time see the same seed and pick the
    /// same register.</summary>
    public static ulong SpriteSeed(Genome.CharacterGenome character) =>
        Serialization.BuiltGameNaming.NamingSeed(character);

    /// <summary>
    /// The name negotiation loop (step 6), bounded by MaxNegotiationIterations, pure
    /// given (genome, seed). The inherited sprite (when given and known) is candidate
    /// #1 — heredity usually wins — followed by the seeded sample; a poor name fit
    /// alternates between advancing to the next sprite candidate and re-rolling the
    /// name on a deterministically bumped seed. Fallback after the bound: the best
    /// joint (spriteScore + compatibility) seen. An optional nameTaken predicate
    /// (roster uniqueness) forces a name re-roll and excludes that iteration from
    /// the fallback.
    /// </summary>
    public SpritePresentation Negotiate(Genome.CharacterGenome character, ulong seed,
        NameGenerator generator, IReadOnlyDictionary<string, int>? priorUse = null,
        string? inheritedSpriteId = null, Func<string, bool>? nameTaken = null)
    {
        IReadOnlyList<SpriteCandidate> sampled =
            SelectCandidates(character, seed, out string register, priorUse);
        var candidates = new List<SpriteCandidate>(sampled.Count + 1);
        SpriteDef? inherited = Library.ById(inheritedSpriteId);
        if (inherited is not null)
        {
            candidates.Add(new SpriteCandidate(inherited,
                CandidateScore(inherited, Salient(character), CharacterAspect(character), priorUse)));
        }
        candidates.AddRange(sampled.Where(c => c.Sprite.Id != inheritedSpriteId));

        NameGen.CharacterGenome mapped = Map(character);
        int spriteAdvances = 0;
        int nameRerolls = 0;
        bool advanceSpriteNext = true; // the alternation state
        SpriteCandidate bestSprite = candidates[0];
        string? bestName = null;
        double bestJoint = double.NegativeInfinity;

        for (int iter = 0; iter < Config.MaxNegotiationIterations; iter++)
        {
            SpriteCandidate candidate = candidates[spriteAdvances % candidates.Count];
            NameResult name = generator.GenerateCharacterName(mapped, new NameOptions
            {
                Seed = NameSeed(seed, nameRerolls),
                Register = register,
            });
            if (nameTaken?.Invoke(name.Display) == true)
            {
                nameRerolls++; // the sprite wasn't the problem — burn a name seed only
                continue;
            }
            double compatibility = Compatibility(name, candidate.Sprite);
            double joint = candidate.Score + compatibility;
            if (joint > bestJoint)
            {
                bestJoint = joint;
                bestSprite = candidate;
                bestName = name.Display;
            }
            if (compatibility >= Config.CompatibilityThreshold)
            {
                return new SpritePresentation(candidate.Sprite.Id, name.Display, register);
            }
            if (advanceSpriteNext)
            {
                spriteAdvances++;
            }
            else
            {
                nameRerolls++;
            }
            advanceSpriteNext = !advanceSpriteNext;
        }
        if (bestName is null)
        {
            // Every iteration hit a taken name — one final deterministic roll; the
            // caller's uniqueness session gets the last word.
            NameResult last = generator.GenerateCharacterName(mapped, new NameOptions
            {
                Seed = NameSeed(seed, nameRerolls),
                Register = register,
            });
            bestName = last.Display;
        }
        return new SpritePresentation(bestSprite.Sprite.Id, bestName, register);
    }

    /// <summary>Name/sprite compatibility (step 6): overlap between the name's salient
    /// traits (weighted by salience) plus its morpheme part tags (flat PartTagWeight)
    /// and the sprite's traitAffinity, weighted by affinity.</summary>
    public double Compatibility(NameResult name, SpriteDef sprite)
    {
        var weights = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (TraitScore trait in name.SalientTraits)
        {
            weights[trait.Name] = Math.Max(
                weights.TryGetValue(trait.Name, out double w) ? w : 0, trait.Score);
        }
        foreach (NamePart part in name.Parts)
        {
            foreach (string tag in part.Tags)
            {
                if (!weights.ContainsKey(tag) || weights[tag] < Config.PartTagWeight)
                {
                    weights[tag] = Config.PartTagWeight;
                }
            }
        }
        double compatibility = 0;
        foreach ((string trait, double weight) in weights)
        {
            if (sprite.TraitAffinity.TryGetValue(trait, out float affinity))
            {
                compatibility += weight * affinity;
            }
        }
        return compatibility;
    }

    // ── Melee attack sprite selection (M4b, 2026-08-23 —
    // docs/features/attack-sprite-selection.md). Runs AFTER character resolution,
    // consuming its outputs (the resolved sprite entry, the shared register). ──────

    /// <summary>The register the shared seed picks — identical to what
    /// SelectCandidates(seed) reports, recomputable without re-sampling.</summary>
    public string PickRegister(ulong seed) => PickRegister(new NgPcg(seed, SpriteSequence));

    /// <summary>Per-move seed: Hash(NamingSeed(character), moveIndex) — same genome,
    /// same character, same move index = same attack sprite, everywhere.</summary>
    public static ulong MoveSpriteSeed(Genome.CharacterGenome character, int moveIndex)
    {
        ulong seed = SpriteSeed(character);
        Span<byte> bytes = stackalloc byte[9];
        for (int i = 0; i < 8; i++)
        {
            bytes[i] = (byte)(seed >> (8 * i));
        }
        bytes[8] = (byte)moveIndex;
        return Determinism.Fnv1a.Hash(bytes, Determinism.Fnv1a.OffsetBasis);
    }

    /// <summary>The move's semantic trait vector from its OWN params, normalized by
    /// its schema ranges, mirroring the FeatureExtractor convention (neutral mid-range
    /// contributes nothing; only extremes fire). Also reports the hitbox sweep shape:
    /// horizontal (wide) favors blade/polearm/whip, vertical (tall) favors slam
    /// classes (blunt/impact).</summary>
    public static (Dictionary<string, double> Traits, double Horizontal, double Vertical)
        MoveTraits(Params.ParamSet moveParams)
    {
        double Norm(string key)
        {
            var schema = moveParams.Schema;
            int i = schema.IndexOf(key);
            if (i < 0)
            {
                return 0.5;
            }
            Params.ParamSpec spec = schema[i];
            return spec.Max <= spec.Min
                ? 0.5
                : Math.Clamp((moveParams.Get(key) - spec.Min) / (double)(spec.Max - spec.Min), 0, 1);
        }
        double Fire(double normalized) => Math.Max(0, 2 * normalized - 1);

        var traits = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["brutal"] = Fire(Norm(Genome.MoveParams.DamageFactor)),
            ["stunner"] = Fire(Norm(Genome.MoveParams.HitstunDuration)),
        };
        double knockback = Fire(Norm(Genome.MoveParams.KnockbackScalar));
        float modY = moveParams.Get(Genome.MoveParams.KnockbackModY);
        if (modY > 0.3f)
        {
            traits["launcher"] = knockback;
        }
        else if (modY < -0.3f)
        {
            traits["spiker"] = knockback;
        }
        else
        {
            traits["launcher"] = knockback / 2;
            traits["spiker"] = knockback / 2;
        }
        double extent = (Norm(Genome.MoveParams.WidthScalar) + Norm(Genome.MoveParams.HeightScalar)) / 2;
        traits["reaching"] = Fire((Norm(Genome.MoveParams.MoveDist) + extent) / 2);
        double commitment =
            (Norm(Genome.MoveParams.WarmUpDuration) + Norm(Genome.MoveParams.CoolDownDuration)) / 2;
        traits["patient"] = Fire(commitment);
        double haste = Math.Max(0, 1 - 2 * commitment);
        traits["frantic"] = haste;
        traits["swift"] = haste;

        double ratio = moveParams.Get(Genome.MoveParams.WidthScalar)
            / Math.Max(0.01f, moveParams.Get(Genome.MoveParams.HeightScalar));
        return (traits, Math.Clamp(ratio - 1, 0, 1), Math.Clamp(1 / ratio - 1, 0, 1));
    }

    private static readonly string[] HorizontalClasses = { "blade", "polearm", "whip" };
    private static readonly string[] VerticalClasses = { "blunt", "impact" };

    /// <summary>The semantic half of a move sprite's score (also the repair metric):
    /// trait dot product plus the sweep-shape class bonus.</summary>
    public double MoveTraitScore(MoveSpriteDef sprite,
        Dictionary<string, double> traits, double horizontal, double vertical)
    {
        double score = 0;
        foreach ((string trait, double weight) in traits)
        {
            if (sprite.TraitAffinity.TryGetValue(trait, out float affinity))
            {
                score += weight * affinity;
            }
        }
        if (horizontal > 0 && Array.IndexOf(HorizontalClasses, sprite.AttackClass) >= 0)
        {
            score += Config.Moves.SweepBonus * horizontal;
        }
        if (vertical > 0 && Array.IndexOf(VerticalClasses, sprite.AttackClass) >= 0)
        {
            score += Config.Moves.SweepBonus * vertical;
        }
        return score;
    }

    /// <summary>Sprite-stage score (within one class): trait dot + element register
    /// affinity + palette − cross-character duplicate penalty. Same-character
    /// duplication is a HARD exclusion upstream, never a penalty here.</summary>
    public double ScoreMoveSprite(MoveSpriteDef sprite, Dictionary<string, double> traits,
        double horizontal, double vertical, SpriteDef characterSprite, string register,
        IReadOnlyDictionary<string, int>? gameUsage)
    {
        MoveSelectionConfig tuning = Config.Moves;
        double score = MoveTraitScore(sprite, traits, horizontal, vertical);
        if (tuning.RegisterAffinities.TryGetValue(register, out RegisterAffinityDef? affinity)
            && sprite.Element is { } element && affinity.Elements.Contains(element))
        {
            score += tuning.RegisterBonus;
        }
        if (sprite.PaletteGroup == characterSprite.PaletteGroup
            || tuning.NeutralPalettes.Contains(sprite.PaletteGroup))
        {
            score += tuning.PaletteBonus;
        }
        if (gameUsage is not null && gameUsage.TryGetValue(sprite.Id, out int uses))
        {
            score -= tuning.CrossDuplicatePenalty * uses;
        }
        return score;
    }

    /// <summary>Class-stage score: the character-to-attack link. Wields position
    /// (first entry full, later entries decaying, absent penalized), the register's
    /// class affinity, the hitbox sweep shape, and the move's traits at muted volume
    /// (mean over the class's candidates).</summary>
    public double ScoreMoveClass(string attackClass, IReadOnlyList<MoveSpriteDef> members,
        Dictionary<string, double> traits, double horizontal, double vertical,
        SpriteDef characterSprite, string register)
    {
        MoveSelectionConfig tuning = Config.Moves;
        int position = -1;
        for (int i = 0; i < characterSprite.Wields.Count; i++)
        {
            if (characterSprite.Wields[i] == attackClass)
            {
                position = i;
                break;
            }
        }
        double score = position switch
        {
            0 => tuning.WieldsFirstBonus,
            > 0 => tuning.WieldsOtherBonus * Math.Pow(tuning.WieldsPositionDecay, position - 1),
            _ => -tuning.WieldsMissingPenalty,
        };
        if (tuning.RegisterAffinities.TryGetValue(register, out RegisterAffinityDef? affinity)
            && affinity.Classes.Contains(attackClass))
        {
            score += tuning.RegisterBonus;
        }
        if (horizontal > 0 && Array.IndexOf(HorizontalClasses, attackClass) >= 0)
        {
            score += tuning.SweepBonus * horizontal;
        }
        if (vertical > 0 && Array.IndexOf(VerticalClasses, attackClass) >= 0)
        {
            score += tuning.SweepBonus * vertical;
        }
        double meanTraits = 0;
        foreach (MoveSpriteDef member in members)
        {
            meanTraits += MoveTraitScore(member, traits, horizontal: 0, vertical: 0);
        }
        return score + tuning.ClassTraitWeight * (meanTraits / members.Count);
    }

    /// <summary>Select one attack sprite, TWO-STAGE (see MoveSelectionConfig): the
    /// compatiblePlans hard filter and same-character exclusion shape the pool; the
    /// wields-driven class stage picks the attack CLASS (object goof budget pinned
    /// EXACTLY on FRESH picks — generation, game-open — and EXCLUDED on repair
    /// re-picks, or evolution ratchets objects toward certainty via constant
    /// collision repairs that objects never reverse); the trait-driven sprite stage
    /// picks within the class.</summary>
    public string SelectMoveSpriteId(Genome.MoveGenome move, SpriteDef characterSprite,
        string register, ulong seed, IReadOnlyCollection<string>? excludeIds = null,
        IReadOnlyDictionary<string, int>? gameUsage = null, bool fresh = true)
    {
        if (MoveLibrary is null)
        {
            throw new InvalidOperationException("no move sprite library loaded.");
        }
        var pool = MoveLibrary.Sprites
            .Where(s => s.CompatiblePlans.Contains(characterSprite.BodyPlan)
                && excludeIds?.Contains(s.Id) != true)
            .ToList();
        if (pool.Count == 0)
        {
            // Exhausted plan pool (only possible with a pathological exclusion list)
            // — distinctness yields before we fail to render anything.
            pool = MoveLibrary.Sprites
                .Where(s => s.CompatiblePlans.Contains(characterSprite.BodyPlan)).ToList();
        }
        if (pool.Count == 0)
        {
            pool = MoveLibrary.Sprites.ToList(); // unknown body plan: full library
        }

        (Dictionary<string, double> traits, double horizontal, double vertical) = MoveTraits(move.Params);

        // Class stage: deterministic order (first appearance in the library-ordered pool).
        var classes = new List<(string Name, List<MoveSpriteDef> Members)>();
        var byClass = new Dictionary<string, List<MoveSpriteDef>>(StringComparer.Ordinal);
        foreach (MoveSpriteDef sprite in pool)
        {
            if (!byClass.TryGetValue(sprite.AttackClass, out List<MoveSpriteDef>? members))
            {
                members = new List<MoveSpriteDef>();
                byClass[sprite.AttackClass] = members;
                classes.Add((sprite.AttackClass, members));
            }
            members.Add(sprite);
        }
        var classScores = new double[classes.Count];
        for (int i = 0; i < classes.Count; i++)
        {
            classScores[i] = ScoreMoveClass(classes[i].Name, classes[i].Members,
                traits, horizontal, vertical, characterSprite, register);
        }
        double[] classProbs = Softmax(classScores, Config.Moves.SoftmaxTemperature);
        for (int i = 0; i < classes.Count; i++)
        {
            if (classes[i].Name == "object")
            {
                ScaleAt(classProbs, i, fresh ? Config.Moves.ObjectBudget : 0f);
                break;
            }
        }

        var rng = new NgPcg(seed, MoveSequence);
        List<MoveSpriteDef> chosen = classes[SampleIndex(classProbs, rng)].Members;

        // Sprite stage: the move's own params speak at full volume within the class.
        var spriteScores = new double[chosen.Count];
        for (int i = 0; i < chosen.Count; i++)
        {
            spriteScores[i] = ScoreMoveSprite(chosen[i], traits, horizontal, vertical,
                characterSprite, register, gameUsage);
        }
        double[] spriteProbs = Softmax(spriteScores, Config.Moves.SoftmaxTemperature);
        return chosen[SampleIndex(spriteProbs, rng)].Id;
    }

    /// <summary>Pin index i of a probability vector to exactly `share`, scaling the
    /// rest to the remainder (no-op for a single-entry vector).</summary>
    private static void ScaleAt(double[] probs, int index, float share)
    {
        double others = 0;
        for (int i = 0; i < probs.Length; i++)
        {
            if (i != index)
            {
                others += probs[i];
            }
        }
        if (others <= 0)
        {
            return;
        }
        double factor = (1.0 - share) / others;
        for (int i = 0; i < probs.Length; i++)
        {
            probs[i] = i == index ? share : probs[i] * factor;
        }
    }

    /// <summary>Move-gene upkeep for one character (breeding pipeline): every Attack
    /// move keeps its inherited sprite unless it is missing/unknown, its class left
    /// the character sprite's wields AND scores below the floor, or an earlier move
    /// of this character already wears it (distinctness is a hard rule). RNG-free
    /// with respect to the evolution stream; no-op without libraries or when the
    /// character has no resolved sprite entry.</summary>
    public Genome.CharacterGenome EnsureMoveGenes(Genome.CharacterGenome character,
        IReadOnlyDictionary<string, int>? gameUsage = null)
    {
        SpriteDef? entry = Library.ById(character.SpriteId);
        if (MoveLibrary is null || entry is null)
        {
            return character;
        }
        string? register = null; // lazy: the seed hashes the full character content
        var used = new HashSet<string>(StringComparer.Ordinal);
        List<Genome.MoveGenome>? rebuilt = null;
        for (int m = 0; m < character.Moves.Count; m++)
        {
            Genome.MoveGenome move = character.Moves[m];
            if (move.Type != Genome.MoveType.Attack)
            {
                continue;
            }
            if (!NeedsMoveRepair(move, entry, used))
            {
                used.Add(move.SpriteId!);
                continue;
            }
            register ??= PickRegister(SpriteSeed(character));
            string id = SelectMoveSpriteId(move, entry, register,
                MoveSpriteSeed(character, m), used, gameUsage, fresh: move.SpriteId is null);
            used.Add(id);
            rebuilt ??= character.Moves.ToList();
            rebuilt[m] = move.WithSpriteId(id);
        }
        return rebuilt is null
            ? character
            : new Genome.CharacterGenome(character.Name, character.Stocks, character.SpriteIndex,
                character.Params, rebuilt, character.ButtonMoves, character.SpriteId);
    }

    /// <summary>Presentation-side resolution (built-game pass): per-move attack
    /// sprite ids around an EXPLICIT character sprite entry — the NEGOTIATED one,
    /// whose wields/bodyPlan may differ from the genome gene's. Inherited move genes
    /// that still hold against that entry are kept (heredity); the rest re-resolve on
    /// their per-move seeds. Returns one id per move, null on non-attack slots.</summary>
    public IReadOnlyList<string?> ResolveMoveSpriteIds(Genome.CharacterGenome character,
        SpriteDef presentedSprite, string register, IReadOnlyDictionary<string, int>? gameUsage = null)
    {
        var result = new List<string?>(character.Moves.Count);
        var used = new HashSet<string>(StringComparer.Ordinal);
        for (int m = 0; m < character.Moves.Count; m++)
        {
            Genome.MoveGenome move = character.Moves[m];
            if (move.Type != Genome.MoveType.Attack)
            {
                result.Add(null);
                continue;
            }
            if (!NeedsMoveRepair(move, presentedSprite, used))
            {
                used.Add(move.SpriteId!);
                result.Add(move.SpriteId);
                continue;
            }
            string id = SelectMoveSpriteId(move, presentedSprite, register,
                MoveSpriteSeed(character, m), used, gameUsage, fresh: move.SpriteId is null);
            used.Add(id);
            result.Add(id);
        }
        return result;
    }

    /// <summary>The M4b repair rule: missing/unknown id; duplicate within the
    /// character; or class no longer in the character sprite's wields AND the
    /// semantic score below the floor.</summary>
    public bool NeedsMoveRepair(Genome.MoveGenome move, SpriteDef characterSprite,
        IReadOnlyCollection<string>? usedByCharacter = null)
    {
        MoveSpriteDef? sprite = MoveLibrary?.ById(move.SpriteId);
        if (sprite is null)
        {
            return true;
        }
        if (usedByCharacter?.Contains(sprite.Id) == true)
        {
            return true;
        }
        if (!sprite.CompatiblePlans.Contains(characterSprite.BodyPlan))
        {
            return true; // the hard filter holds across inheritance too
        }
        if (characterSprite.Wields.Contains(sprite.AttackClass))
        {
            return false;
        }
        (Dictionary<string, double> traits, double h, double v) = MoveTraits(move.Params);
        return MoveTraitScore(sprite, traits, h, v) < Config.Moves.RepairFloor;
    }

    private string PickRegister(NgPcg rng)
    {
        double total = 0;
        foreach (RegisterDef def in _data.Registers)
        {
            if (def.Weight > 0)
            {
                total += def.Weight;
            }
        }
        double roll = rng.NextDouble() * total;
        double acc = 0;
        foreach (RegisterDef def in _data.Registers)
        {
            if (def.Weight <= 0)
            {
                continue;
            }
            acc += def.Weight;
            if (roll < acc)
            {
                return def.Name;
            }
        }
        return _data.Registers[_data.Registers.Count - 1].Name;
    }

    private static double CharacterAspect(Genome.CharacterGenome character)
    {
        float width = character.Params.Get(CharacterParams.WidthScalar);
        float height = character.Params.Get(CharacterParams.HeightScalar);
        return height > 0 ? Math.Max(0.01, width / height) : 1.0;
    }

    private static ulong NameSeed(ulong seed, int reroll) =>
        seed ^ (0x9E3779B97F4A7C15UL * (ulong)reroll); // reroll 0 = the shared seed itself

    private static double[] Softmax(double[] scores, float temperature)
    {
        double max = double.NegativeInfinity;
        foreach (double s in scores)
        {
            max = Math.Max(max, s);
        }
        double t = Math.Max(1e-3, temperature);
        var probs = new double[scores.Length];
        double sum = 0;
        for (int i = 0; i < scores.Length; i++)
        {
            probs[i] = Math.Exp((scores[i] - max) / t);
            sum += probs[i];
        }
        for (int i = 0; i < probs.Length; i++)
        {
            probs[i] /= sum;
        }
        return probs;
    }

    /// <summary>Distribution shaping (step 4), on probabilities: pin the goof budget
    /// (vibe == "goofy" holds EXACTLY GoofBudget of the mass when both classes are
    /// present — a floor for earnest pools, a cap for goofy-heavy ones like fey),
    /// then cap bodyPlan == "biped" at BipedCap. The cap runs second, so it holds
    /// exactly; the goof mass may drift slightly where the two interact (the
    /// distribution smoke test's tolerances own that trade).</summary>
    private void ShapeDistribution(List<SpriteDef> pool, double[] probs)
    {
        ScaleClass(pool, probs, s => s.Vibe == "goofy", Config.GoofBudget, exact: true);
        ScaleClass(pool, probs, s => s.BodyPlan == "biped", Config.BipedCap, exact: false);
        CapPerSprite(probs, Config.MaxSpriteShare);
    }

    /// <summary>The no-monopoly rule: water-fill so no single entry exceeds the cap —
    /// capped entries freeze, the rest scale up proportionally, repeated until stable
    /// (each pass caps at least one more entry, so it terminates). Skipped when the
    /// pool is too small for the cap to be satisfiable.</summary>
    private static void CapPerSprite(double[] probs, float cap)
    {
        if (cap <= 0 || cap * probs.Length <= 1.0)
        {
            return;
        }
        var capped = new bool[probs.Length];
        for (int pass = 0; pass < probs.Length; pass++)
        {
            int cappedCount = 0;
            double freeMass = 0;
            for (int i = 0; i < probs.Length; i++)
            {
                if (capped[i])
                {
                    cappedCount++;
                }
                else
                {
                    freeMass += probs[i];
                }
            }
            double target = 1.0 - cappedCount * (double)cap;
            if (freeMass <= 0 || target <= 0)
            {
                return;
            }
            double scale = target / freeMass;
            bool newlyCapped = false;
            for (int i = 0; i < probs.Length; i++)
            {
                if (capped[i])
                {
                    continue;
                }
                double scaled = probs[i] * scale;
                if (scaled > cap)
                {
                    probs[i] = cap;
                    capped[i] = true;
                    newlyCapped = true;
                }
                else
                {
                    probs[i] = scaled;
                }
            }
            if (!newlyCapped)
            {
                return;
            }
        }
    }

    private static void ScaleClass(List<SpriteDef> pool, double[] probs,
        Func<SpriteDef, bool> inClass, float share, bool exact)
    {
        double classMass = 0;
        double otherMass = 0;
        for (int i = 0; i < pool.Count; i++)
        {
            if (inClass(pool[i]))
            {
                classMass += probs[i];
            }
            else
            {
                otherMass += probs[i];
            }
        }
        if (classMass <= 0 || otherMass <= 0)
        {
            return; // one-class pool: nothing to rebalance
        }
        if (!exact && classMass <= share)
        {
            return; // cap not exceeded
        }
        double classFactor = share / classMass;
        double otherFactor = (1.0 - share) / otherMass;
        for (int i = 0; i < pool.Count; i++)
        {
            probs[i] *= inClass(pool[i]) ? classFactor : otherFactor;
        }
    }

    private static int SampleIndex(double[] probs, NgPcg rng)
    {
        double total = 0;
        foreach (double p in probs)
        {
            total += p;
        }
        double roll = rng.NextDouble() * total;
        double acc = 0;
        int last = 0;
        for (int i = 0; i < probs.Length; i++)
        {
            if (probs[i] <= 0)
            {
                continue;
            }
            acc += probs[i];
            last = i;
            if (roll < acc)
            {
                return i;
            }
        }
        return last; // floating-point tail: the last positive entry
    }
}
