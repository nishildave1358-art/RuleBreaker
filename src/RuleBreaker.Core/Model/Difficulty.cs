namespace RuleBreaker.Core.Model;

/// <summary>
/// The four difficulty settings (GDD principles 29, 31). Difficulty is chosen at
/// session start and is orthogonal to level progression: the level designs the
/// base response timer, the difficulty multiplies it. They compound, they do not
/// compete.
/// </summary>
public enum Difficulty
{
    Easy,
    Medium,
    Hard,
    Nightmare,
}

/// <summary>
/// The GDD "Difficulty Multiplier Per Tier" table (principles 30, 31).
/// Medium is the balancing reference point: it always equals the level-designed
/// base timer exactly.
/// </summary>
public static class DifficultyRules
{
    /// <summary>The four tiers in GDD table order (Easy → Nightmare).</summary>
    public static IReadOnlyList<Difficulty> All { get; } = new[]
    {
        Difficulty.Easy,
        Difficulty.Medium,
        Difficulty.Hard,
        Difficulty.Nightmare,
    };

    /// <summary>
    /// Easy 1.5x (50% longer) · Medium 1.0x (equals base exactly) ·
    /// Hard 0.7x (30% shorter) · Nightmare 0.5x (halved).
    /// </summary>
    public static double Multiplier(this Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => 1.5,
        Difficulty.Medium => 1.0,
        Difficulty.Hard => 0.7,
        Difficulty.Nightmare => 0.5,
        _ => throw new ArgumentOutOfRangeException(
            nameof(difficulty), difficulty, "Unknown difficulty tier."),
    };
}
