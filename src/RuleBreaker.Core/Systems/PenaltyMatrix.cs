namespace RuleBreaker.Core.Systems;

using RuleBreaker.Core.Engine;
using RuleBreaker.Core.Model;

/// <summary>
/// How much one failure costs, after the GDD penalty table has been applied
/// (plan §6 Phase 5; GDD "Penalty Table by Difficulty and Source").
/// </summary>
public enum PenaltyKind
{
    /// <summary>No cost at all — an Easy fake-instruction mistake, or a failure an Auto Card absorbed.</summary>
    None,

    /// <summary>Medium fake-instruction mistake: the combo chain breaks, no life is lost (principle 27).</summary>
    ComboResetOnly,

    /// <summary>Normal penalty: one life lost and the combo chain breaks (principles 16, 26–28).</summary>
    Full,
}

/// <summary>
/// The GDD's penalty table as pure data decisions (principles 16, 24, 26–28).
///
/// | Action source              | Easy | Medium           | Hard | Nightmare |
/// |----------------------------|------|------------------|------|-----------|
/// | Real instruction mistake   | Full | Full             | Full | Full      |
/// | Fake instruction mistake   | None | ComboResetOnly   | Full | Full      |
/// | Timer expiry               | Full | Full             | Full | Full      |
///
/// Two things the table deliberately does NOT do:
///  • Timer expiry is identical on every tier — there is no Easy exception
///    (principle 16). It is not treated as a "fake mistake".
///  • A correct decision has no penalty even when it traces to a fake instruction:
///    the game evaluates the final action, not the intention (principle 24).
/// </summary>
public static class PenaltyRules
{
    /// <summary>The penalty for one evaluated turn, before any Auto Card absorbs it.</summary>
    public static PenaltyKind For(
        TurnOutcomeKind kind,
        SourceTrace sourceTrace,
        Difficulty difficulty)
    {
        // An undefined tier is a programming error, not a table row: fail loudly
        // even for the branches that do not read the difficulty today.
        if (!Enum.IsDefined(difficulty))
            throw new ArgumentOutOfRangeException(
                nameof(difficulty), difficulty, "Unknown difficulty tier.");

        return kind switch
        {
            TurnOutcomeKind.Correct => PenaltyKind.None,
            TurnOutcomeKind.TimerExpiry => PenaltyKind.Full,
            TurnOutcomeKind.IncorrectAction when sourceTrace == SourceTrace.FakeOrigin =>
                FakeInstructionMistake(difficulty),
            TurnOutcomeKind.IncorrectAction => PenaltyKind.Full,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind), kind, "Unknown turn outcome kind."),
        };
    }

    /// <summary>The GDD's Source B row: Easy none · Medium combo reset only · Hard/Nightmare full.</summary>
    public static PenaltyKind FakeInstructionMistake(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => PenaltyKind.None,             // principle 26
        Difficulty.Medium => PenaltyKind.ComboResetOnly, // principle 27
        Difficulty.Hard or Difficulty.Nightmare => PenaltyKind.Full, // principle 28
        _ => throw new ArgumentOutOfRangeException(
            nameof(difficulty), difficulty, "Unknown difficulty tier."),
    };

    /// <summary>
    /// GDD "Combo Reset Conditions by Difficulty": any penalty other than
    /// <see cref="PenaltyKind.None"/> breaks the chain. On Easy a fake mistake
    /// has no penalty, so the combo survives; Medium resets without costing a
    /// life. An Auto Card absorbing the failure leaves a <see cref="PenaltyKind.None"/>
    /// behind, which is why absorption never resets the combo.
    /// </summary>
    public static bool ResetsCombo(PenaltyKind penalty) => penalty != PenaltyKind.None;

    /// <summary>GDD "Lives System": only a full penalty costs a life; the Medium combo reset does not.</summary>
    public static bool CostsLife(PenaltyKind penalty) => penalty == PenaltyKind.Full;
}

/// <summary>
/// The two auto-protection cards the penalty flow must consider (GDD "Auto Cards",
/// principles 41–42). Which cards are in the loadout is Phase 6's concern; this
/// type answers only "is this card allowed to fire on this failure?" and in what
/// order, so the penalty flow cannot double-charge a single failure (R-8).
/// </summary>
public enum AutoCardTrigger
{
    /// <summary>Fires on incorrect actions only — never on timer expiry (principle 41).</summary>
    Joker,

    /// <summary>Fires on any failure, including timer expiry (principle 42).</summary>
    Shield,
}

/// <summary>
/// R-8 priority for the auto-card check inside the penalty flow (GDD loop step 15).
///
/// When both cards are in the loadout, Joker is the more specific trigger and is
/// offered first on an incorrect action; Shield is the only card that covers a
/// timer expiry, so Joker is never offered there. Resolving a single winner
/// before any card is consumed prevents both cards being spent on one failure.
/// </summary>
public static class AutoCardRules
{
    /// <summary>Can a card with this trigger absorb this kind of failure?</summary>
    public static bool CanFire(AutoCardTrigger trigger, TurnOutcomeKind kind) => trigger switch
    {
        AutoCardTrigger.Joker => kind == TurnOutcomeKind.IncorrectAction, // principle 41
        AutoCardTrigger.Shield => kind is TurnOutcomeKind.IncorrectAction or TurnOutcomeKind.TimerExpiry, // principle 42
        _ => throw new ArgumentOutOfRangeException(
            nameof(trigger), trigger, "Unknown auto-card trigger."),
    };

    /// <summary>
    /// The R-8 order in which auto cards may be offered for this outcome:
    /// incorrect action → Joker, then Shield; timer expiry → Shield only;
    /// correct decision → nothing. Phase 6 consumes this list to pick the card
    /// that actually fires (and is consumed).
    /// </summary>
    public static IReadOnlyList<AutoCardTrigger> FireOrder(TurnOutcomeKind kind) => kind switch
    {
        TurnOutcomeKind.IncorrectAction => new[] { AutoCardTrigger.Joker, AutoCardTrigger.Shield },
        TurnOutcomeKind.TimerExpiry => new[] { AutoCardTrigger.Shield },
        TurnOutcomeKind.Correct => Array.Empty<AutoCardTrigger>(),
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind), kind, "Unknown turn outcome kind."),
    };

    /// <summary>The first card the R-8 order would offer, or null when nothing may fire.</summary>
    public static AutoCardTrigger? FirstEligible(TurnOutcomeKind kind)
    {
        var order = FireOrder(kind);
        return order.Count == 0 ? null : order[0];
    }
}
