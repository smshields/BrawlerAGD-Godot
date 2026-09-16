namespace BrawlerSim.Fitness.Terms;

/// <summary>
/// AIRBORNE PLAY: a SATURATING reward for jumping — jumping matters, jump-spam earns
/// nothing extra.
///
/// The saturation point is the term's whole character and was hard-coded in every
/// shipped version (v3 read the <c>const</c> directly even though the weight beside it
/// was a parameter). <c>scaleWithPlayers</c> keeps the per-player expectation constant
/// as the roster grows: 40 per pair, so 20 per player. At N = 2 that is exactly the
/// flat 40 the two-player versions used.
/// </summary>
public sealed class JumpsTerm : IFitnessTerm
{
    public const string TermId = "jumps";

    public static IReadOnlyList<FitnessTermParam> Specs { get; } = new[]
    {
        new FitnessTermParam("weight", "WEIGHT", StandardFitnessV3.DefaultJumpWeight, 0f, 50f,
            "Fitness at or above the saturation point."),
        new FitnessTermParam("saturation", "JUMPS TO SATURATE", StandardFitnessV3.DefaultJumpSaturation, 1f, 500f,
            "Total jumps (per pair of players) at which the reward maxes out."),
        FitnessTermParam.Flag("scaleWithPlayers", "SCALE WITH PLAYER COUNT", true,
            "Flag: multiply the saturation by playerCount/2 so per-player expectations hold at 3-4 players."),
    };

    private readonly float _weight;
    private readonly float _saturation;
    private readonly bool _scaleWithPlayers;

    public JumpsTerm(IReadOnlyDictionary<string, float>? values = null)
    {
        _weight = TermValues.Read(values, Specs, "weight");
        _saturation = TermValues.Read(values, Specs, "saturation");
        _scaleWithPlayers = TermValues.Flag(values, Specs, "scaleWithPlayers");
    }

    public JumpsTerm(float weight)
        : this(TermValues.Of(
            ("weight", weight),
            ("saturation", StandardFitnessV3.DefaultJumpSaturation),
            ("scaleWithPlayers", 1f)))
    {
    }

    public string Id => TermId;

    public IReadOnlyDictionary<string, float> Values => TermValues.Of(
        ("weight", _weight), ("saturation", _saturation),
        ("scaleWithPlayers", _scaleWithPlayers ? 1f : 0f));

    public float Evaluate(FitnessInput input)
    {
        float saturation = _scaleWithPlayers
            ? _saturation * input.PlayerCount / 2f
            : _saturation;
        return _weight * MathF.Min(input.Sum(p => p.Jumps), saturation) / saturation;
    }
}
