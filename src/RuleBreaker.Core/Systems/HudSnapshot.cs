namespace RuleBreaker.Core.Systems;

using RuleBreaker.Core.Model;

/// <summary>
/// One immutable read of everything the GDD HUD displays (plan §6 Phase 5;
/// GDD "UI Elements — PC Widescreen Layout"). The presentation layer reads this
/// snapshot and never mutates the session (plan architecture rule 4), which is
/// what keeps Phase 9 a pure renderer of simulation state.
///
/// Field-to-HUD mapping:
///   Score (white) · Lives / SpentLives (gold coin vs dark circle) ·
///   ComboCount + Multiplier (gold combo meter, intensity scales with multiplier) ·
///   TurnNumber (amber) · Difficulty badge ·
///   TimerRemainingSeconds / TimerTotalSeconds / TimerFractionRemaining
///   (amber → orange 20% → red 10%) · ActiveRuleCount (left panel) ·
///   Mode + StreamingSpeed (pacing indicator) · IsGameOver, IsPaused.
/// </summary>
public sealed record HudSnapshot(
    int Score,
    int Lives,
    int MaxLives,
    int SpentLives,
    int ComboCount,
    int Multiplier,
    ComboMilestone LastMilestone,
    PenaltyKind LastPenalty,
    bool LastPenaltyAbsorbed,
    int TurnNumber,
    Difficulty Difficulty,
    PacingMode Mode,
    StreamingSpeed? StreamingSpeed,
    double TimerRemainingSeconds,
    double TimerTotalSeconds,
    double TimerFractionRemaining,
    int ActiveRuleCount,
    bool IsGameOver,
    bool IsPaused)
{
    /// <summary>Capture the current state of a session for the HUD.</summary>
    public static HudSnapshot Capture(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var run = session.Run;
        var settlement = run.LastSettlement;

        return new HudSnapshot(
            Score: run.Score,
            Lives: run.Lives,
            MaxLives: run.MaxLives,
            SpentLives: run.SpentLives,
            ComboCount: run.ConsecutiveCorrect,
            Multiplier: run.Multiplier,
            LastMilestone: settlement?.Milestone ?? ComboMilestone.None,
            LastPenalty: settlement?.Penalty ?? PenaltyKind.None,
            LastPenaltyAbsorbed: settlement?.AbsorbedByAutoCard ?? false,
            TurnNumber: session.TurnNumber,
            Difficulty: session.Config.Difficulty,
            Mode: session.Config.Mode,
            StreamingSpeed: session.StreamingSpeed,
            TimerRemainingSeconds: session.Timer.RemainingSeconds,
            TimerTotalSeconds: session.Timer.TotalSeconds,
            TimerFractionRemaining: session.Timer.FractionRemaining,
            ActiveRuleCount: session.Rules.OrderedRules.Count,
            IsGameOver: run.IsGameOver,
            IsPaused: session.IsPaused);
    }
}
