namespace RuleBreaker.Core.Model;

using RuleBreaker.Core.Content;

/// <summary>
/// How instructions arrive (GDD "Gameplay Pacing"). Turn-based: Classic, Memory,
/// Hardcore. Streaming: Endless, Chaos, Boss Rounds. Streaming changes only how
/// quickly instructions appear and how long the response window is — never what a
/// turn is (principles 9, 15).
/// </summary>
public enum PacingMode
{
    TurnBased,
    Streaming,
}

/// <summary>
/// The four streaming speeds from the GDD streaming timer table. Each speed has
/// its own compressed base response timer (Slow 5 s · Medium 3 s · Fast 2 s ·
/// Maximum 1 s).
/// </summary>
public enum StreamingSpeed
{
    Slow,
    Medium,
    Fast,
    Maximum,
}

/// <summary>
/// The phases of the 19-step core game loop that have an external actor
/// (plan §6 Phase 2). The remaining steps are pure computation inside a single
/// call: rule activation/modification and modifier evaluation (steps 4–9), the
/// engine's comparison and source trace (steps 12–14).
///
/// Order: InstructionShown → ResponseWindow → Evaluation → AutoCardCheck →
/// ActivationWindow → TurnComplete → (next instruction).
/// </summary>
public enum TurnPhase
{
    /// <summary>Level has started but no instruction has appeared yet (turn counter 0).</summary>
    NotStarted,

    /// <summary>Instruction is on screen; turn counter and countdowns have already been applied.</summary>
    InstructionShown,

    /// <summary>Response window is open and the response timer is depleting (GDD step 10).</summary>
    ResponseWindow,

    /// <summary>Response window closed; the engine is about to resolve and compare (steps 12–13).</summary>
    Evaluation,

    /// <summary>Failure recorded; the auto-card opportunity runs here (step 15, cards land in Phase 5).</summary>
    AutoCardCheck,

    /// <summary>Between-turns activation window is open; manual cards may be used (step 16, principles 43–45).</summary>
    ActivationWindow,

    /// <summary>Both windows are closed; the next instruction may now appear (principles 18, 44).</summary>
    TurnComplete,
}

/// <summary>
/// Everything the turn-dependent systems need for one turn: the global turn
/// counter (GDD principle 10 — absolute, starts at 1 on the first instruction,
/// 0 before it), the session difficulty, and the pacing that produced this
/// turn's response timer.
/// </summary>
public sealed record TurnContext(
    int TurnNumber,
    Difficulty Difficulty,
    PacingMode Mode,
    double LevelBaseSeconds,
    StreamingSpeed? StreamingSpeed = null)
{
    public bool IsStreaming => Mode == PacingMode.Streaming;

    /// <summary>
    /// Principle 12: a turn-specific rule may only reference a FUTURE turn. If the
    /// global turn counter is at Turn 8, no rule referencing Turn 5 or earlier is
    /// ever generated — every turn-specific rule must be achievable, so
    /// dead-on-arrival rules are never created. Hand-crafted level scripts
    /// (Phase 13) and the procedural generator (Phase 7) both gate on this.
    /// </summary>
    public bool IsFutureTurnReference(int referencedTurn) => referencedTurn > TurnNumber;

    /// <summary>
    /// Enforce principle 12 for one authored reference, with the reason attached
    /// so a content author sees why the rule was rejected.
    /// </summary>
    public void ValidateFutureTurnReference(int referencedTurn, string ruleId)
    {
        if (IsFutureTurnReference(referencedTurn)) return;

        throw new RuleContentException(
            $"Rule '{ruleId}' references turn {referencedTurn} while the global turn counter is at " +
            $"turn {TurnNumber}: a turn-specific rule may only reference a future turn (principle 12), " +
            "so this rule would be dead on arrival.");
    }
}

/// <summary>
/// Session-level configuration (the level's designed base timer, the difficulty
/// chosen at session start, the mode's pacing, and the tunables Phase 14 pins).
/// </summary>
public sealed record SessionConfig
{
    /// <summary>
    /// Base length of the between-turns activation window in turn-based play.
    /// The GDD fixes only its behavior (always present, never skipped,
    /// proportional compression) — the number is a Phase 14 balance pin, like the
    /// R-12 economy defaults.
    /// </summary>
    public const double DefaultActivationWindowSeconds = 2.0;

    /// <summary>Streaming escalation default: tier up every 20 turns (plan ruling 4).</summary>
    public const int DefaultTurnsPerTier = 20;

    /// <summary>Streaming escalation floor: at least 10 turns per tier (plan ruling 4).</summary>
    public const int MinimumTurnsPerTier = 10;

    public SessionConfig(
        Difficulty difficulty = Difficulty.Medium,
        double levelBaseSeconds = 6.0,
        PacingMode mode = PacingMode.TurnBased,
        double activationWindowSeconds = DefaultActivationWindowSeconds,
        int turnsPerTier = DefaultTurnsPerTier)
    {
        if (levelBaseSeconds <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(levelBaseSeconds), levelBaseSeconds,
                "A level must design a positive base response timer (GDD: the level sets the base timer).");
        if (activationWindowSeconds <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(activationWindowSeconds), activationWindowSeconds,
                "The between-turns window is always present and never zero-length (principles 43–45).");
        if (turnsPerTier < MinimumTurnsPerTier)
            throw new ArgumentOutOfRangeException(
                nameof(turnsPerTier), turnsPerTier,
                $"Streaming escalates at most one tier every {MinimumTurnsPerTier} turns (plan ruling 4).");

        Difficulty = difficulty;
        LevelBaseSeconds = levelBaseSeconds;
        Mode = mode;
        ActivationWindowSeconds = activationWindowSeconds;
        TurnsPerTier = turnsPerTier;
    }

    public Difficulty Difficulty { get; }
    public double LevelBaseSeconds { get; }
    public PacingMode Mode { get; }
    public double ActivationWindowSeconds { get; }
    public int TurnsPerTier { get; }
}
