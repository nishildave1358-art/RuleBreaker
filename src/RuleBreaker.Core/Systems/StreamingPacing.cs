namespace RuleBreaker.Core.Systems;

using RuleBreaker.Core.Model;

/// <summary>
/// Streaming speed escalation (plan ruling 4: tier up every 20 turns, at least 10
/// turns per tier — tunable in Phase 14). Speeds climb Slow → Medium → Fast →
/// Maximum and stay at Maximum; the response timer compresses with each tier, so
/// the pressure rises without the turn structure changing (principles 9, 15).
/// </summary>
public sealed class StreamingPacing
{
    public StreamingPacing(int turnsPerTier = SessionConfig.DefaultTurnsPerTier)
    {
        if (turnsPerTier < SessionConfig.MinimumTurnsPerTier)
            throw new ArgumentOutOfRangeException(
                nameof(turnsPerTier), turnsPerTier,
                $"Streaming escalates at most one tier every {SessionConfig.MinimumTurnsPerTier} turns (plan ruling 4).");

        TurnsPerTier = turnsPerTier;
    }

    public int TurnsPerTier { get; }

    /// <summary>Zero-based escalation tier for a turn (0 Slow, 1 Medium, 2 Fast, 3+ Maximum).</summary>
    public int TierForTurn(int turnNumber)
    {
        if (turnNumber < 1)
            throw new ArgumentOutOfRangeException(
                nameof(turnNumber), turnNumber, "The global turn counter starts at 1 on the first instruction (principle 10).");

        return Math.Min(3, (turnNumber - 1) / TurnsPerTier);
    }

    /// <summary>The streaming speed in force on the given turn.</summary>
    public StreamingSpeed SpeedForTurn(int turnNumber) => TierForTurn(turnNumber) switch
    {
        0 => StreamingSpeed.Slow,
        1 => StreamingSpeed.Medium,
        2 => StreamingSpeed.Fast,
        _ => StreamingSpeed.Maximum,
    };
}
