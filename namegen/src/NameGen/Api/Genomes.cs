using System.Collections.Generic;

namespace NameGen
{
    /// <summary>Kinds of move segments a character genome can carry.</summary>
    public enum MoveKind
    {
        Melee,
        Shield,
        Dash,
        Projectile,
    }

    /// <summary>
    /// One move's params, keyed by the same stable strings as the game's schema
    /// (e.g. "damageFactor"). The library normalizes against its own copy of the
    /// schema ranges; unknown keys are ignored, missing keys read as neutral.
    /// </summary>
    public sealed class MoveGenome
    {
        public MoveKind Kind { get; }
        public IReadOnlyDictionary<string, float> Params { get; }

        public MoveGenome(MoveKind kind, IReadOnlyDictionary<string, float> parameters)
        {
            Kind = kind;
            Params = parameters;
        }
    }

    /// <summary>Character body params plus the moveset. Naming reads this, never writes it.</summary>
    public sealed class CharacterGenome
    {
        public IReadOnlyDictionary<string, float> Params { get; }
        public IReadOnlyList<MoveGenome> Moves { get; }

        public CharacterGenome(IReadOnlyDictionary<string, float> parameters, IReadOnlyList<MoveGenome>? moves = null)
        {
            Params = parameters;
            Moves = moves ?? new List<MoveGenome>();
        }
    }

    public sealed class StageGenome
    {
        public IReadOnlyDictionary<string, float> Params { get; }

        public StageGenome(IReadOnlyDictionary<string, float> parameters)
        {
            Params = parameters;
        }
    }
}
