namespace RuleBreaker.Core.Systems;

using RuleBreaker.Core.Model;

/// <summary>
/// The GDD response-timer tables (principles 30, 31) and the streaming
/// compression table. Two formulas, both straight from the GDD:
///
///   turn-based  Final Timer = Level Base Timer × Difficulty Multiplier
///   streaming   Final Timer = (Compressed Base by Streaming Speed) × Difficulty Multiplier
///
/// Values are rounded to milliseconds so the GDD's own reference tables come out
/// exactly (4.2 s stays 4.2 s, not 4.199999999999999), and clamped to a floor
/// because "the minimum response timer is never zero" (principle 14).
/// </summary>
public static class TimerTable
{
    /// <summary>The GDD's floor: the player always has some window to respond.</summary>
    public const double MinimumSeconds = 0.1;

    /// <summary>Orange warning threshold — percentage of the window remaining (GDD 20%).</summary>
    public const double WarningFraction = 0.20;

    /// <summary>Red critical threshold — percentage of the window remaining (GDD 10%, fires the critical cue).</summary>
    public const double CriticalFraction = 0.10;

    private const int Precision = 3; // millisecond resolution

    /// <summary>Round to milliseconds and never return a zero-length window.</summary>
    public static double Round(double seconds) =>
        Math.Round(Math.Max(seconds, MinimumSeconds), Precision);

    /// <summary>GDD "Combined Timer Reference Table": Level Base × Difficulty Multiplier.</summary>
    public static double TurnBasedSeconds(double levelBaseSeconds, Difficulty difficulty) =>
        levelBaseSeconds <= 0
            ? MinimumSeconds
            : Round(levelBaseSeconds * difficulty.Multiplier());

    /// <summary>GDD streaming table: the compressed base a streaming speed substitutes for the level base.</summary>
    public static double CompressedBaseSeconds(this StreamingSpeed speed) => speed switch
    {
        StreamingSpeed.Slow => 5.0,
        StreamingSpeed.Medium => 3.0,
        StreamingSpeed.Fast => 2.0,
        StreamingSpeed.Maximum => 1.0,
        _ => throw new ArgumentOutOfRangeException(nameof(speed), speed, "Unknown streaming speed."),
    };

    /// <summary>GDD streaming table: compressed base × difficulty multiplier, for every speed × tier cell.</summary>
    public static double StreamingSeconds(StreamingSpeed speed, Difficulty difficulty) =>
        Round(speed.CompressedBaseSeconds() * difficulty.Multiplier());

    /// <summary>The response window for a turn under the session's pacing.</summary>
    public static double ForTurn(
        double levelBaseSeconds,
        Difficulty difficulty,
        PacingMode mode,
        StreamingSpeed? streamingSpeed = null) =>
        mode == PacingMode.Streaming && streamingSpeed is { } speed
            ? StreamingSeconds(speed, difficulty)
            : TurnBasedSeconds(levelBaseSeconds, difficulty);

    /// <summary>
    /// How much streaming compresses a window: the compressed base relative to the
    /// level's designed base. Turn-based play does not compress (factor 1). The
    /// difficulty multiplier is common to both, so it cancels out — the ratio is
    /// the same for every tier, which is what lets the activation window compress
    /// "proportionally with the response window" (principle 45).
    /// </summary>
    public static double CompressionFactor(
        PacingMode mode,
        StreamingSpeed? streamingSpeed,
        double levelBaseSeconds) =>
        mode == PacingMode.Streaming && streamingSpeed is { } speed
            ? speed.CompressedBaseSeconds() / levelBaseSeconds
            : 1.0;
}

/// <summary>
/// One response window: its total length and how much is left. Immutable — time
/// advances by producing a new timer, so the presentation layer can hold
/// snapshots and the simulation stays deterministic.
/// </summary>
public sealed record ResponseTimer
{
    /// <summary>No window has been opened yet.</summary>
    public static readonly ResponseTimer NotStarted = new(0, 0);

    /// <summary>Tolerance for fraction comparisons after millisecond rounding.</summary>
    private const double FractionTolerance = 1e-9;

    public ResponseTimer(double totalSeconds, double remainingSeconds)
    {
        TotalSeconds = Math.Max(0, totalSeconds);
        RemainingSeconds = Math.Clamp(remainingSeconds, 0, TotalSeconds);
    }

    public double TotalSeconds { get; }
    public double RemainingSeconds { get; }

    /// <summary>True once the window has run out (a window that never opened is not expired).</summary>
    public bool HasExpired => TotalSeconds > 0 && RemainingSeconds <= 0;

    /// <summary>Fraction of the window still remaining (0 when nothing was opened).</summary>
    public double FractionRemaining => TotalSeconds <= 0 ? 0 : RemainingSeconds / TotalSeconds;

    /// <summary>Amber → orange at 20% remaining.</summary>
    public bool IsWarning =>
        TotalSeconds > 0 && FractionRemaining <= TimerTable.WarningFraction + FractionTolerance;

    /// <summary>Orange → red at 10% remaining; this is also the critical-sound cue.</summary>
    public bool IsCritical =>
        TotalSeconds > 0 && FractionRemaining <= TimerTable.CriticalFraction + FractionTolerance;

    /// <summary>Deplete the window by <paramref name="seconds"/> of play (paused time never reaches here).</summary>
    public ResponseTimer Advance(double seconds)
    {
        if (seconds < 0)
            throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "Time only moves forward.");

        return new ResponseTimer(TotalSeconds, Math.Max(0, RemainingSeconds - seconds));
    }

    /// <summary>Open a window of the GDD-computed length.</summary>
    public static ResponseTimer Start(double totalSeconds) => new(totalSeconds, totalSeconds);
}
