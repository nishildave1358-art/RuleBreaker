namespace RuleBreaker.Core.Model;

/// <summary>
/// The GDD's hybrid activation model (principles 40–43): Auto cards fire
/// themselves when their trigger is met (loop step 15), Manual cards are
/// activated by the player during the Between-Turns Activation Window (step 16).
/// </summary>
public enum CardActivation
{
    /// <summary>Protective/defensive — fires on a failure without player input (principles 41, 42).</summary>
    Auto,

    /// <summary>Tactical/offensive — left-click on a loadout slot during the activation window (principle 43).</summary>
    Manual,
}

/// <summary>
/// The one mechanical effect a Special Card applies. Each value has exactly one
/// handler in <c>CardEffectEngine</c>; the GDD's eight cards map one-to-one onto
/// these, so adding a card is data, not code (plan engineering rule 2).
/// </summary>
public enum CardEffectKind
{
    /// <summary>Joker (principle 41): absorbs the next incorrect action, never a timer expiry.</summary>
    Joker,

    /// <summary>Shield (principle 42): absorbs any failure, including timer expiry.</summary>
    Shield,

    /// <summary>Freeze: no new rules for N turns; the turn counter keeps running.</summary>
    Freeze,

    /// <summary>Chaos: a random rule from the pool becomes active (depth-gated).</summary>
    Chaos,

    /// <summary>Mirror (R-10): the most recent prior rule activates again with a fresh duration.</summary>
    Mirror,

    /// <summary>Echo (principle 8): restore one chosen dead rule — the only resurrection path.</summary>
    Echo,

    /// <summary>Time Warp (principle 17): shorter response timers for N turns; difficulty still multiplies.</summary>
    TimeWarp,

    /// <summary>Rule Bomb (principle 47): remove one random active rule, identifiers included.</summary>
    RuleBomb,
}

/// <summary>
/// Card rarity. The GDD only distinguishes "rare" rewards (30+ combo milestone,
/// boss rounds) from ordinary drops, so two tiers are enough — Phase 14 owns the
/// actual drop weights.
/// </summary>
public enum CardRarity
{
    Common,
    Rare,
}

/// <summary>
/// Data-facing definition of one Special Card (JSON: camelCase). Content is data:
/// the code knows the eight <see cref="CardEffectKind"/> handlers, never the eight
/// card names.
///
/// Effect parameters are explicit and validated rather than a loose bag, because
/// the GDD fixes both of the numbers that exist today:
///   • Freeze — <see cref="DurationTurns"/> = 3 (plan ruling R-10);
///   • Time Warp — <see cref="DurationTurns"/> = 5 and <see cref="TimerFactor"/>
///     = 0.6 (the GDD's "−40%", plan §7 pin 6).
/// Every other card takes neither, and the schema rejects params on effects that
/// cannot use them — a content author cannot smuggle in a silently ignored field.
/// </summary>
public sealed record CardDefinition(
    string Id,
    string Name,
    CardActivation Activation,
    CardEffectKind Effect,
    CardRarity Rarity = CardRarity.Common,
    string? Text = null,
    int? DurationTurns = null,
    double TimerFactor = 1.0,
    string? VisualCue = null)
{
    /// <summary>Auto cards fire on the failure path; manual cards need the activation window.</summary>
    public bool IsAuto => Activation == CardActivation.Auto;

    /// <summary>True for the two cards that absorb a penalty rather than acting on the rule set.</summary>
    public bool IsAbsorber => Effect is CardEffectKind.Joker or CardEffectKind.Shield;
}

/// <summary>
/// One card offered as an acquisition reward (GDD "Card Reward UI"). A reward of
/// two or three distinct cards is a choice; a count of one is a guaranteed card
/// (random drop, boss reward, random milestone roll).
/// </summary>
public sealed record CardReward(
    IReadOnlyList<CardDefinition> Offered,
    CardRewardSource Source,
    bool IsChoice)
{
    /// <summary>The single card this reward grants, or null when the player must choose.</summary>
    public CardDefinition? Guaranteed => IsChoice ? null : Offered.FirstOrDefault();
}

/// <summary>
/// The GDD's four acquisition channels (principle 35), plus the shop, which is a
/// purchase rather than a drop.
/// </summary>
public enum CardRewardSource
{
    /// <summary>Method 1 — 10/20/30+ consecutive correct decisions (principle 48).</summary>
    ComboMilestone,

    /// <summary>Method 2a — level completion, choice of 2–3 offered cards.</summary>
    LevelCompletion,

    /// <summary>Method 2b — boss round completion, guaranteed and typically rarer.</summary>
    BossCompletion,

    /// <summary>Method 3 — low-rate random drop during play.</summary>
    RandomDrop,
}
