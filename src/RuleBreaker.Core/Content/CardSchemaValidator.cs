namespace RuleBreaker.Core.Content;

using RuleBreaker.Core.Model;

/// <summary>
/// Structural validation for one <see cref="CardDefinition"/>. Mirrors
/// <see cref="RuleSchemaValidator"/>: per-card checks live here, pool-wide checks
/// in <see cref="CardLibraryLinter"/>.
///
/// The parameter checks are the important part — the GDD fixes Freeze's 3 turns and
/// Time Warp's 5 turns at −40%, and no other card takes either parameter. A card
/// carrying a field its effect cannot use is rejected instead of silently ignored,
/// so a balance edit cannot hide in an unused property.
/// </summary>
public static class CardSchemaValidator
{
    /// <summary>Freeze's fixed duration (plan ruling R-10).</summary>
    public const int FreezeTurns = 3;

    /// <summary>Time Warp's fixed duration in turns (plan §7 pin 6).</summary>
    public const int TimeWarpTurns = 5;

    /// <summary>Time Warp's timer multiplier: the GDD's "−40%" (plan §7 pin 6).</summary>
    public const double TimeWarpTimerFactor = 0.6;

    public static IReadOnlyList<string> Validate(CardDefinition card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(card.Id))
            errors.Add("Card Id must not be empty.");
        else if (!IsValidCardId(card.Id))
            errors.Add($"Card Id '{card.Id}' must be UPPERCASE snake case starting with the CRD_ prefix (e.g. 'CRD_JOKER').");

        if (string.IsNullOrWhiteSpace(card.Name))
            errors.Add($"Card '{card.Id}': Name must not be empty.");

        if (!Enum.IsDefined(card.Activation))
            errors.Add($"Card '{card.Id}': Activation '{card.Activation}' is not a valid CardActivation.");

        if (!Enum.IsDefined(card.Effect))
            errors.Add($"Card '{card.Id}': Effect '{card.Effect}' is not a valid CardEffectKind.");

        if (!Enum.IsDefined(card.Rarity))
            errors.Add($"Card '{card.Id}': Rarity '{card.Rarity}' is not a valid CardRarity.");

        if (string.IsNullOrWhiteSpace(card.Text))
            errors.Add($"Card '{card.Id}': Text must not be empty (the collection screen shows it).");

        if (string.IsNullOrWhiteSpace(card.VisualCue))
            errors.Add($"Card '{card.Id}': VisualCue must not be empty (Phase 9 plays it on activation).");

        // Principles 40–43: absorption is defensive, so only the Auto cards may
        // absorb; and the Auto cards are exactly the two absorbers.
        if (card.IsAbsorber && card.Activation != CardActivation.Auto)
            errors.Add($"Card '{card.Id}': an absorbing card ({card.Effect}) must be an Auto card (principles 41, 42).");

        if (card.Activation == CardActivation.Auto && !card.IsAbsorber)
            errors.Add($"Card '{card.Id}': Auto activation is only defined for Joker/Shield (principles 41, 42).");

        ValidateParameters(card, errors);
        return errors;
    }

    private static void ValidateParameters(CardDefinition card, List<string> errors)
    {
        switch (card.Effect)
        {
            case CardEffectKind.Freeze:
                RequireDuration(card, FreezeTurns, errors);
                RequireNoTimerFactor(card, errors);
                break;

            case CardEffectKind.TimeWarp:
                RequireDuration(card, TimeWarpTurns, errors);
                if (Math.Abs(card.TimerFactor - TimeWarpTimerFactor) > 1e-9)
                    errors.Add($"Card '{card.Id}': Time Warp's TimerFactor must be {TimeWarpTimerFactor} (the GDD's −40%), was {card.TimerFactor}.");
                break;

            default:
                if (card.DurationTurns is not null)
                    errors.Add($"Card '{card.Id}': DurationTurns is not used by {card.Effect}.");
                RequireNoTimerFactor(card, errors);
                break;
        }
    }

    private static void RequireDuration(CardDefinition card, int expected, List<string> errors)
    {
        if (card.DurationTurns is not { } turns)
            errors.Add($"Card '{card.Id}': {card.Effect} requires DurationTurns = {expected}.");
        else if (turns != expected)
            errors.Add($"Card '{card.Id}': {card.Effect} must last exactly {expected} turns, was {turns}.");
    }

    private static void RequireNoTimerFactor(CardDefinition card, List<string> errors)
    {
        if (Math.Abs(card.TimerFactor - 1.0) > 1e-9)
            errors.Add($"Card '{card.Id}': TimerFactor is not used by {card.Effect}.");
    }

    /// <summary>Valid card ID: uppercase snake case, at least two segments, CRD_ prefix.</summary>
    private static bool IsValidCardId(string id)
    {
        var segments = id.Split('_');
        if (segments.Length < 2 || segments[0] != "CRD") return false;

        foreach (var segment in segments)
        {
            if (segment.Length == 0) return false;
            if (!char.IsAsciiLetterOrDigit(segment[0])) return false;
            if (segment.Any(c => !char.IsAsciiLetterOrDigit(c))) return false;
        }

        return true;
    }
}
