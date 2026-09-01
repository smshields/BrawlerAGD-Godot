using Godot;
using BrawlerSim.Genome;
using BrawlerSim.Sim;

namespace BrawlerGodot;

/// <summary>Shared move-name vocabulary (internal-to-the-view since 2026-08-17: the
/// character select's key-to-move view uses the same labels as the HUD debug strip;
/// the builder's chips add the damage gene). The two vocabularies are deliberately
/// distinct — Abbrev names a character's move slot, Chip summarizes a move genome.</summary>
public static class MoveLabels
{
    /// <summary>Short move name for a character's move slot: attacks are numbered
    /// (ATK1..), other types read by type alone.</summary>
    public static string Abbrev(CharacterGenome character, int moveIndex)
    {
        MoveType type = character.Moves[moveIndex].Type;
        if (type == MoveType.Attack)
        {
            return $"ATK{moveIndex + 1}";
        }
        return type switch
        {
            MoveType.Shield => "SHLD",
            MoveType.Dash => "DASH",
            MoveType.Projectile => "PROJ",
            _ => type.ToString().ToUpperInvariant(),
        };
    }

    /// <summary>One-line move summary (Game Builder chips): type + the damage gene
    /// for attack-family moves (defensive moves read by type alone).</summary>
    public static string Chip(MoveGenome move) => move.Type switch
    {
        MoveType.Attack => $"ATK {move.Params.Get(MoveParams.DamageFactor):F1}",
        MoveType.Projectile => $"PROJ {move.Params.Get(ProjectileParams.DamageFactor):F1}",
        MoveType.Shield => "SHLD",
        MoveType.Dash => "DASH",
        _ => move.Type.ToString().ToUpperInvariant(),
    };
}

/// <summary>The player-state readability vocabulary: body tints and the HUD's
/// human-readable state names, kept together so they can never drift apart.</summary>
public static class StateVocabulary
{
    /// <summary>Unity SpriteRenderer state tints, verbatim — plus cyan for the Shield
    /// state (designer tint decision, 2026-07-12). The HUD's human-readable state
    /// readout matches the body tint (HUD polish, 2026-07-23).</summary>
    public static Color Color(PlayerState state) => state switch
    {
        PlayerState.Shield => Colors.Cyan,
        PlayerState.Dash => Colors.Orange, // 2026-07-13 designer tint decision
        PlayerState.Crouch => Colors.Purple, // 2026-07-13 designer tint decision
        PlayerState.Idle => Colors.White,
        PlayerState.Air => Colors.Green,
        PlayerState.AirJumpsExhausted => Colors.Gray,
        PlayerState.WarmUp => Colors.Yellow,
        PlayerState.Attack => Colors.Red,
        PlayerState.CoolDown => Colors.Blue,
        PlayerState.Stun => Colors.Magenta,
        _ => Colors.White,
    };

    /// <summary>Human-readable state names (FEATURES.md, HUD item 3) — machine enums
    /// stay in the sim; the player reads verbs.</summary>
    public static string Name(SimPlayer player)
    {
        if (player.Eliminated)
        {
            return "ELIMINATED"; // out for good (2026-08-12, STOCK rule)
        }
        if (player.IsRespawning)
        {
            return $"RESPAWNING {player.RespawnBlackoutLeft / BrawlerSim.SimInfo.TicksPerSecond:F1}s";
        }
        if (player.State == PlayerState.Stun && player.StunFromShieldBreak)
        {
            return "SHIELD BROKEN";
        }
        return player.State switch
        {
            PlayerState.Idle => "READY",
            PlayerState.Air => "AIRBORNE",
            PlayerState.AirJumpsExhausted => "EXHAUSTED",
            PlayerState.WarmUp => "WINDING UP",
            PlayerState.Attack => "ATTACKING",
            PlayerState.CoolDown => "RECOVERING",
            PlayerState.Stun => "STUNNED",
            PlayerState.Shield => "SHIELDING",
            PlayerState.Dash => "DASHING",
            PlayerState.Crouch => "CROUCHING",
            _ => player.State.ToString().ToUpperInvariant(),
        };
    }
}

/// <summary>The five-button control scheme's keycap labels (2026-07-20, DEVIATIONS
/// #25), previously triplicated across HudView / CharacterSelectView / EvolveView.
/// Slot order is the genome's button order: I/J/K/U/L on keyboard maps to
/// L1/X/A/Y/R1 on pad. INVARIANT: the L (pad R1) slot stays PINNED LAST — U (pad Y)
/// was inserted as slot 3 when the fifth button landed, and evolve-menu per-button
/// composition relies on that ordering (the pinned dash lives in the last slot).</summary>
public static class ControlLabels
{
    public static readonly string[] Keyboard = { "I", "J", "K", "U", "L" };
    public static readonly string[] Pad = { "L1", "X", "A", "Y", "R1" };
    public const string KeyboardJump = "SPC";
    public const string PadJump = "B";
}
