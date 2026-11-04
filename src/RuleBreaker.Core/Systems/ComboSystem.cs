namespace RuleBreaker.Core.Systems;

/// <summary>
/// The GDD's combo reward milestones (principles 48, 35): 10, 20 and 30+
/// consecutive correct decisions each earn a card. A milestone fires once per
/// chain — the counter passing 30 does not keep paying out every turn.
/// </summary>
public enum ComboMilestone
{
    None,
    Ten,
    Twenty,
    ThirtyPlus,
}

/// <summary>
/// The GDD "Combo System" table and its milestone thresholds (principles 48, 12).
///
/// | Correct decisions | Score multiplier |
/// |-------------------|------------------|
/// | 5                 | 2x               |
/// | 10                | 3x               |
/// | 20                | 5x               |
///
/// The multiplier is a function of the current chain length only, so a reset
/// always returns to 1x without any extra bookkeeping. Whether the chain resets
/// at all is a difficulty decision and lives in
/// <see cref="PenaltyRules.ResetsCombo"/>.
/// </summary>
public static class ComboRules
{
    /// <summary>2x from the 5th consecutive correct decision.</summary>
    public const int TwoXThreshold = 5;

    /// <summary>3x from the 10th consecutive correct decision.</summary>
    public const int ThreeXThreshold = 10;

    /// <summary>5x from the 20th consecutive correct decision.</summary>
    public const int FiveXThreshold = 20;

    /// <summary>First card reward threshold (principle 48).</summary>
    public const int FirstMilestone = 10;

    /// <summary>Second card reward threshold (principle 48).</summary>
    public const int SecondMilestone = 20;

    /// <summary>Third (rare) card reward threshold — the GDD's "30+" row (principle 48).</summary>
    public const int ThirdMilestone = 30;

    /// <summary>1x below 5, then 2x / 3x / 5x at the GDD thresholds.</summary>
    public static int MultiplierFor(int consecutiveCorrect)
    {
        if (consecutiveCorrect < 0)
            throw new ArgumentOutOfRangeException(
                nameof(consecutiveCorrect), consecutiveCorrect, "A combo chain cannot be negative.");

        if (consecutiveCorrect >= FiveXThreshold) return 5;
        if (consecutiveCorrect >= ThreeXThreshold) return 3;
        if (consecutiveCorrect >= TwoXThreshold) return 2;
        return 1;
    }

    /// <summary>
    /// The milestone reached at exactly this chain length (10 / 20 / 30), or
    /// <see cref="ComboMilestone.None"/>. Only the exact threshold fires: the
    /// reward is once per chain, not every turn from 30 onward.
    /// </summary>
    public static ComboMilestone MilestoneFor(int consecutiveCorrect) => consecutiveCorrect switch
    {
        FirstMilestone => ComboMilestone.Ten,
        SecondMilestone => ComboMilestone.Twenty,
        ThirdMilestone => ComboMilestone.ThirtyPlus,
        _ => ComboMilestone.None,
    };
}

/// <summary>
/// The live combo chain: consecutive correct decisions, the multiplier they
/// currently earn, and the milestone the last correct decision crossed.
/// Immutable state is not required here because a combo has no presentation
/// snapshot of its own — the HUD reads it through <see cref="HudSnapshot"/>.
/// </summary>
public sealed class ComboMeter
{
    /// <summary>Consecutive correct decisions in the current chain (0 after a reset).</summary>
    public int ConsecutiveCorrect { get; private set; }

    /// <summary>Score multiplier the chain currently earns (1x, 2x, 3x or 5x).</summary>
    public int Multiplier => ComboRules.MultiplierFor(ConsecutiveCorrect);

    /// <summary>
    /// Extend the chain by one correct decision and report the milestone it
    /// crossed (usually <see cref="ComboMilestone.None"/>).
    /// </summary>
    public ComboMilestone RegisterCorrect()
    {
        ConsecutiveCorrect++;
        return ComboRules.MilestoneFor(ConsecutiveCorrect);
    }

    /// <summary>Break the chain: back to 0 correct decisions and 1x.</summary>
    public void Reset() => ConsecutiveCorrect = 0;
}
