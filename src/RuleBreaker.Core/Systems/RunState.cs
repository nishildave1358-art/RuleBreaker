namespace RuleBreaker.Core.Systems;

using RuleBreaker.Core.Engine;
using RuleBreaker.Core.Model;

/// <summary>
/// Everything one evaluated turn did to the run (plan §6 Phase 5). This is the
/// single record Phase 6 (cards/economy) and Phase 9 (HUD/VFX) consume: what the
/// failure cost, what the correct decision paid, where the combo and lives now
/// stand, and whether the run just ended.
/// </summary>
public sealed record TurnSettlement(
    TurnOutcomeKind Kind,
    SourceTrace SourceTrace,
    PenaltyKind Penalty,
    PenaltyKind PenaltyBeforeAbsorption,
    bool AbsorbedByAutoCard,
    bool AbsorbedRedundantPenalty,
    string? AbsorbedByCardId,
    int ScoreAwarded,
    int Score,
    int ComboCount,
    int Multiplier,
    ComboMilestone Milestone,
    int Lives,
    bool IsGameOver)
{
    /// <summary>The failure cost nothing because an Auto Card covered it.</summary>
    public bool WasAbsorbed => AbsorbedByAutoCard;

    /// <summary>The turn ended the run (the third life was just spent).</summary>
    public bool EndedRun => IsGameOver && Penalty == PenaltyKind.Full;
}

/// <summary>
/// The state of one run: score, combo chain, and lives (plan §6 Phase 5;
/// GDD "Score", "Combo System", "Lives System"). Difficulty is fixed for the run
/// because it decides the penalty table row, and lives default to the GDD's 3
/// (Hardcore's 1-life run is Phase 7 and simply constructs this with 1).
///
/// Scoring (plan ruling R-12, GDD reward section):
///   • a correct decision is always <c>100 × combo multiplier</c> — no intention
///     check, so an accidental correct action pays exactly like a deliberate one
///     (principle 24);
///   • the multiplier is read AFTER the chain grows, so the 5th correct decision
///     is the first one paid at 2x (it completes the "5 correct → 2x" set);
///   • failures never subtract score — they cost a life, the combo, or nothing,
///     per the difficulty matrix.
///
/// Game-over policy: <see cref="IsGameOver"/> is reported as soon as the last life
/// is spent, but this type does not refuse further settlements. Stopping a run is
/// the run shell's job (Phase 7 modes / Phase 11 UI); the turn loop in
/// <see cref="GameSession"/> deliberately stays content- and run-agnostic, which
/// is also what lets the Phase 2 loop tests play long failing sequences.
/// </summary>
public sealed class RunState
{
    /// <summary>GDD "Lives System": starting lives = 3.</summary>
    public const int DefaultStartingLives = 3;

    /// <summary>Plan ruling R-12: base score per correct decision.</summary>
    public const int BaseScorePerCorrect = 100;

    public RunState(Difficulty difficulty, int startingLives = DefaultStartingLives)
    {
        if (startingLives <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(startingLives), startingLives,
                "A run must start with at least one life (GDD Lives System).");

        Difficulty = difficulty;
        MaxLives = startingLives;
        Lives = startingLives;
    }

    public Difficulty Difficulty { get; }

    public int MaxLives { get; }

    public int Lives { get; private set; }

    public int SpentLives => MaxLives - Lives;

    public int Score { get; private set; }

    public ComboMeter Combo { get; } = new();

    public int ConsecutiveCorrect => Combo.ConsecutiveCorrect;

    public int Multiplier => Combo.Multiplier;

    /// <summary>GDD "No Lives Remaining: Game Over".</summary>
    public bool IsGameOver => Lives <= 0;

    /// <summary>The most recent settlement (survives into the next turn for HUD/VFX).</summary>
    public TurnSettlement? LastSettlement { get; private set; }

    /// <summary>The last turn number settled — one settlement per displayed instruction.</summary>
    public int? LastSettledTurn { get; private set; }

    /// <summary>Settle a turn with no Auto Card absorbing it.</summary>
    public TurnSettlement Settle(TurnOutcome outcome) => Settle(outcome, absorbedByCardId: null);

    /// <summary>
    /// Apply one evaluated turn to the run. Called once per turn by the penalty
    /// flow after the auto-card check (GDD loop step 15), so
    /// <paramref name="absorbedByCardId"/> is the only thing Phase 6 has to supply:
    /// a non-null id means an Auto Card covered the failure and the run pays
    /// nothing — no life, no combo reset (GDD "Auto Card absorbs failure: No life
    /// lost"; combo table "Auto Card absorbs mistake: No reset").
    ///
    /// Principle 46 falls out of the same branch: on Easy a fake-instruction
    /// mistake already has no penalty, so an absorbing card is reported as
    /// <see cref="TurnSettlement.AbsorbedRedundantPenalty"/> — a wasted activation,
    /// exactly as the GDD intends.
    /// </summary>
    public TurnSettlement Settle(TurnOutcome outcome, string? absorbedByCardId)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        if (LastSettledTurn == outcome.TurnNumber)
            throw new InvalidOperationException(
                $"Turn {outcome.TurnNumber} has already been settled. A turn is settled exactly once, " +
                "after its auto-card check (GDD loop step 15).");

        var penaltyBeforeAbsorption = PenaltyRules.For(outcome.Kind, outcome.SourceTrace, Difficulty);
        var absorbed = outcome.IsFailure && absorbedByCardId is not null;
        var penalty = absorbed ? PenaltyKind.None : penaltyBeforeAbsorption;

        var milestone = ComboMilestone.None;
        var awarded = 0;

        if (outcome.IsCorrect)
        {
            // Principle 24: the final action decides, never the intention — an
            // accidental match earns the same reward as a deliberate one.
            milestone = Combo.RegisterCorrect();
            awarded = BaseScorePerCorrect * Combo.Multiplier;
            Score += awarded;
        }
        else if (!absorbed)
        {
            if (PenaltyRules.ResetsCombo(penalty)) Combo.Reset();
            if (PenaltyRules.CostsLife(penalty)) Lives = Math.Max(0, Lives - 1);
        }

        var settlement = new TurnSettlement(
            outcome.Kind,
            outcome.SourceTrace,
            penalty,
            penaltyBeforeAbsorption,
            absorbed,
            AbsorbedRedundantPenalty: absorbed && penaltyBeforeAbsorption == PenaltyKind.None,
            AbsorbedByCardId: absorbed ? absorbedByCardId : null,
            ScoreAwarded: awarded,
            Score: Score,
            ComboCount: Combo.ConsecutiveCorrect,
            Multiplier: Combo.Multiplier,
            Milestone: milestone,
            Lives: Lives,
            IsGameOver: IsGameOver);

        LastSettlement = settlement;
        LastSettledTurn = outcome.TurnNumber;
        return settlement;
    }
}
