namespace RuleBreaker.Core.Systems;

using RuleBreaker.Core.Engine;
using RuleBreaker.Core.Model;

/// <summary>How an evaluated turn ended (GDD loop steps 12–14, R-1).</summary>
public enum TurnOutcomeKind
{
    /// <summary>Final action matched the resolved directive — including "did nothing" when nothing was required.</summary>
    Correct,

    /// <summary>Player acted and the action did not match the resolved directive.</summary>
    IncorrectAction,

    /// <summary>Response window ran out with a required (unperformed or wrong) action — full penalty on every tier (principle 16).</summary>
    TimerExpiry,
}

/// <summary>
/// The result of one turn: what was required, what the player's final action was,
/// which source it traces to, and whether the auto-card opportunity has to run.
/// The penalty flow consumes this into a <see cref="TurnSettlement"/> (Phase 5);
/// cards decide absorption in Phase 6.
/// </summary>
public sealed record TurnOutcome(
    TurnOutcomeKind Kind,
    int TurnNumber,
    string InstructionRuleId,
    CoinAction RequiredAction,
    CoinAction? FinalAction,
    SourceTrace SourceTrace,
    bool AutoCardCheckRequired,
    string? AbsorbedByCardId = null)
{
    public bool IsCorrect => Kind == TurnOutcomeKind.Correct;
    public bool IsFailure => !IsCorrect;
}

/// <summary>
/// The 19-step core game loop as a deterministic state machine (plan §6 Phase 2).
///
/// Phase 2 owns the loop's *timing and sequencing*, not its content: which rules
/// a level activates is authored data (Phase 4/7), what a card does is Phase 5/6,
/// and gesture recognition is Phase 3. So the session fires the loop's touch
/// points in GDD order and lets those systems plug in:
///
///   steps 1–4   ShowInstruction — instruction appears, turn counter increments,
///               turn-based rules count down, the rule set may be extended
///               (ActivateRule) before anything is evaluated
///   steps 5–9   ActivateRule / engine modifier evaluation inside OpenResponseWindow
///   step 10     OpenResponseWindow — Final Timer = Level Base × Difficulty (or the
///               streaming compressed base), never zero
///   step 11     SubmitInput / ConfirmNoInput, or Advance(...) until the timer expires
///   steps 12–14 ResolveEvaluation — the Modifier Engine resolves the instruction and
///               compares it to the player's final action; source is traced (principle 25)
///   step 15     ResolveAutoCardCheck — the failure's card opportunity; the run is
///               settled here (penalty matrix, combo, lives, score — Phase 5)
///   steps 16–17 Activation window — always present, never skipped (principles 43–45)
///   step 18     TurnComplete — the gate that keeps principle 18 honest: no new
///               instruction until BOTH windows have fully closed
///
/// Time is injected, never read: callers pass elapsed seconds to
/// <see cref="Advance"/> (the plan's clock port), so the whole loop is
/// reproducible in tests and headless validation.
/// </summary>
public sealed class GameSession
{
    private readonly ModifierEngine _engine = new();
    private readonly StreamingPacing _pacing;
    private readonly List<ActiveRule> _activationHistory = [];
    private readonly List<ActiveRule> _expiredRules = [];
    private readonly List<ActiveRule> _removedRules = [];
    private bool _responseWindowExpired;
    private int _freezeStartTurn;
    private int _frozenThroughTurn;
    private int _timerPressureStartTurn;
    private int _timerPressureThroughTurn;
    private double _timerPressureFactor = 1.0;

    public GameSession(SessionConfig? config = null)
    {
        Config = config ?? new SessionConfig();
        Run = new RunState(Config.Difficulty);
        _pacing = new StreamingPacing(Config.TurnsPerTier);
    }

    public SessionConfig Config { get; }

    /// <summary>
    /// Score, combo and lives for this run (Phase 5). One settlement is applied
    /// per turn at loop step 15, once the auto-card check has decided whether a
    /// card absorbed the failure.
    /// </summary>
    public RunState Run { get; }

    /// <summary>What the most recent turn did to the run (null before the first settlement).</summary>
    public TurnSettlement? LastSettlement => Run.LastSettlement;

    /// <summary>Immutable HUD state for the presentation layer (plan architecture rule 4).</summary>
    public HudSnapshot Hud => HudSnapshot.Capture(this);

    /// <summary>GDD "No Lives Remaining: Game Over" — reported by the run, not enforced by the loop.</summary>
    public bool IsGameOver => Run.IsGameOver;

    public TurnPhase Phase { get; private set; } = TurnPhase.NotStarted;

    /// <summary>Global turn counter: 0 before the first instruction, then absolute (principle 10).</summary>
    public int TurnNumber { get; private set; }

    /// <summary>The live rule set (oldest → newest), as the Modifier Engine sees it.</summary>
    public RuleSetState Rules { get; private set; } = new();

    /// <summary>
    /// Short-term memory for Memory Rules (GDD Memory System). A store turn writes
    /// the displayed value; later recall turns read it through the engine.
    /// </summary>
    public MemoryBank Memory { get; } = new();

    /// <summary>Ambient on-screen text this turn — Trigger Rules exact-match against it.</summary>
    public string? ScreenText { get; private set; }

    /// <summary>The value displayed this turn that a Memory store rule records.</summary>
    public string? ScreenValue { get; private set; }

    /// <summary>What the last countdown tick removed (loop step 3, principles 4, 11).</summary>
    public RuleSetTickResult? LastTick { get; private set; }

    // ── Phase 6 card hooks ──────────────────────────────────────────────────────
    // The card system is the only client of these: Mirror needs the most recent
    // activation, Echo needs dead rules, Rule Bomb removes a live one, Freeze
    // gates activation, Time Warp compresses the response window.

    /// <summary>Every rule activated this run, oldest → newest (loop step 4 and card plays).</summary>
    public IReadOnlyList<ActiveRule> ActivationHistory => _activationHistory;

    /// <summary>
    /// Rules that ran out of turns (principles 4, 11). This is Echo's shop window:
    /// principle 8 makes the Echo card the only mechanic that can bring one back.
    /// Rules removed by a Rule Bomb are <em>not</em> here — they did not expire.
    /// </summary>
    public IReadOnlyList<ActiveRule> ExpiredRules => _expiredRules;

    /// <summary>Rules destroyed by a Rule Bomb (principle 47), kept for diagnostics.</summary>
    public IReadOnlyList<ActiveRule> RemovedRules => _removedRules;

    /// <summary>The rule activated immediately before now — what Mirror duplicates (R-10).</summary>
    public ActiveRule? PreviousActivatedRule =>
        _activationHistory.Count == 0 ? null : _activationHistory[^1];

    /// <summary>
    /// True while Freeze is in force: no new rules may enter the set. Freeze starts
    /// with the next instruction, so a second card may still be played in the same
    /// between-turns window that Freeze was used in.
    /// </summary>
    public bool NewRulesFrozen =>
        _frozenThroughTurn > 0 && TurnNumber >= _freezeStartTurn && TurnNumber <= _frozenThroughTurn;

    /// <summary>
    /// Whole frozen turns: the two full turns still to come plus a partially spent
    /// one. At the moment Freeze is played this reads the card's full duration.
    /// </summary>
    public int FrozenTurnsRemaining =>
        _frozenThroughTurn <= 0
            ? 0
            : Math.Max(0, _frozenThroughTurn - Math.Max(TurnNumber, _freezeStartTurn) + 1);

    /// <summary>True while Time Warp compresses the response window (principle 17).</summary>
    public bool TimerPressureActive =>
        _timerPressureThroughTurn > 0
        && TurnNumber >= _timerPressureStartTurn
        && TurnNumber <= _timerPressureThroughTurn;

    /// <summary>Whole turns of Time Warp still to come (0 when it is not in force).</summary>
    public int TimerPressureTurnsRemaining =>
        _timerPressureThroughTurn <= 0
            ? 0
            : Math.Max(0, _timerPressureThroughTurn - Math.Max(TurnNumber, _timerPressureStartTurn) + 1);

    /// <summary>The timer multiplier in force this turn (1.0 when Time Warp is not active).</summary>
    public double TimerPressureFactor => TimerPressureActive ? _timerPressureFactor : 1.0;

    /// <summary>Pacing context for the current turn (null before the first instruction).</summary>
    public TurnContext? Context { get; private set; }

    public Instruction? Instruction { get; private set; }

    public ResolutionResult? Resolution { get; private set; }

    public ResponseTimer Timer { get; private set; } = ResponseTimer.NotStarted;

    /// <summary>The turn's accumulated input — the final action R-1 resolves on expiry.</summary>
    public CoinAction? AccumulatedInput { get; private set; }

    public TurnOutcome? Outcome { get; private set; }

    /// <summary>True while the auto-card opportunity is open (loop step 15).</summary>
    public bool AutoCardCheckPending { get; private set; }

    public double ActivationWindowTotalSeconds { get; private set; }
    public double ActivationWindowRemainingSeconds { get; private set; }

    /// <summary>GDD R-2: Escape freezes timers, streaming and countdowns.</summary>
    public bool IsPaused { get; private set; }

    public StreamingSpeed? StreamingSpeed => Context?.StreamingSpeed;

    // ── loop steps 1–4: the instruction appears ─────────────────────────────────
    /// <summary>
    /// Display the next instruction. Increments the global turn counter (step 2),
    /// runs the once-per-instruction countdown tick (step 3) and makes the
    /// instruction current (step 1). Rules that belong to it can then be activated
    /// (step 4) before the response window opens.
    /// </summary>
    public void ShowInstruction(Instruction instruction, string? screenText = null, string? screenValue = null)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        RequirePhase(
            "a new instruction may only appear once the previous turn's response window AND activation window have both fully closed (principles 18, 44)",
            TurnPhase.NotStarted, TurnPhase.TurnComplete);

        TurnNumber++;                                        // step 2
        var (surviving, tick) = Rules.TickExpiry(TurnNumber); // step 3
        Rules = surviving;
        LastTick = tick;
        _expiredRules.AddRange(tick.JustExpired); // Echo's graveyard (principle 8)

        Context = new TurnContext(
            TurnNumber,
            Config.Difficulty,
            Config.Mode,
            Config.LevelBaseSeconds,
            Config.Mode == PacingMode.Streaming ? _pacing.SpeedForTurn(TurnNumber) : null);

        Instruction = instruction;                           // step 1
        ScreenText = screenText;
        ScreenValue = screenValue;
        Resolution = null;
        Outcome = null;
        AutoCardCheckPending = false;
        AccumulatedInput = null;
        _responseWindowExpired = false;
        Timer = ResponseTimer.NotStarted;
        ActivationWindowTotalSeconds = 0;
        ActivationWindowRemainingSeconds = 0;

        Phase = TurnPhase.InstructionShown;
    }

    /// <summary>
    /// Steps 4–5: the instruction enters the active rule set, and (during the
    /// activation window) a manual card may activate a rule too (principles 43).
    /// </summary>
    public void ActivateRule(ActiveRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        RequirePhase(
            "rules are activated either when their instruction appears or during the between-turns activation window (principle 43)",
            TurnPhase.InstructionShown, TurnPhase.ActivationWindow);

        // Freeze's whole point: the set stops growing while turns keep coming.
        if (NewRulesFrozen)
            throw new InvalidOperationException(
                $"Freeze is in force for {FrozenTurnsRemaining} more turn(s): no new rules may be activated.");

        Rules = Rules.Activate(rule);
        _activationHistory.Add(rule);
    }

    // ── Phase 6: card effects that mutate the turn state ────────────────────────
    /// <summary>
    /// Freeze: no new rules for <paramref name="turns"/> turns, counted from the
    /// turn the card was played (so a play in turn 7's window covers 8, 9 and 10).
    /// The turn counter itself keeps running — only activation stops.
    /// </summary>
    public void FreezeNewRules(int turns)
    {
        if (turns <= 0)
            throw new ArgumentOutOfRangeException(nameof(turns), turns, "Freeze must last at least one turn.");

        RequirePhase(
            "Freeze is a Manual Card: it may only be played during the between-turns activation window (principle 43)",
            TurnPhase.ActivationWindow);

        // Freeze starts with the next instruction (the one the window leads into).
        _freezeStartTurn = _frozenThroughTurn > 0 && TurnNumber >= _freezeStartTurn
            ? _freezeStartTurn
            : TurnNumber + 1;
        _frozenThroughTurn = Math.Max(_frozenThroughTurn, TurnNumber + turns);
    }

    /// <summary>
    /// Time Warp (principle 17): compress the next <paramref name="turns"/> response
    /// windows by <paramref name="factor"/>. The difficulty multiplier is applied
    /// first and is not redefined — Time Warp only shortens the window the turn
    /// already had, so the turn itself never changes (principle 9).
    /// </summary>
    public void ApplyTimerPressure(int turns, double factor)
    {
        if (turns <= 0)
            throw new ArgumentOutOfRangeException(nameof(turns), turns, "Time Warp must last at least one turn.");
        if (factor is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(
                nameof(factor), factor, "Time Warp only ever shortens the window, so its factor must be in (0, 1].");

        RequirePhase(
            "Time Warp is a Manual Card: it may only be played during the between-turns activation window (principle 43)",
            TurnPhase.ActivationWindow);

        // Overlapping warps do not stack multiplicatively; the stronger factor wins.
        _timerPressureFactor = TimerPressureActive ? Math.Min(_timerPressureFactor, factor) : factor;
        _timerPressureStartTurn = _timerPressureThroughTurn > 0 && TurnNumber >= _timerPressureStartTurn
            ? _timerPressureStartTurn
            : TurnNumber + 1;
        _timerPressureThroughTurn = Math.Max(_timerPressureThroughTurn, TurnNumber + turns);
    }

    /// <summary>
    /// Remove one active rule by position (Rule Bomb, principle 47). Identifiers are
    /// removable like any other rule — that is the card's stated power.
    /// </summary>
    public ActiveRule RemoveRuleAt(int index)
    {
        if (index < 0 || index >= Rules.OrderedRules.Count)
            throw new ArgumentOutOfRangeException(
                nameof(index), index, $"There is no active rule at index {index}.");

        var removed = Rules.OrderedRules[index];
        Rules = Rules.RemoveAt(index);
        _removedRules.Add(removed);
        return removed;
    }

    // ── step 10: the response window opens ──────────────────────────────────────
    /// <summary>
    /// Open the response window: Final Timer = Level Base × Difficulty Multiplier
    /// (turn-based) or Compressed Base × Difficulty Multiplier (streaming),
    /// never zero (principles 13, 14, 30, 31).
    /// </summary>
    public void OpenResponseWindow()
    {
        RequirePhase("the response window opens once the instruction is on screen", TurnPhase.InstructionShown);

        var total = TimerTable.ForTurn(
            Config.LevelBaseSeconds, Config.Difficulty, Config.Mode, StreamingSpeed);

        // Time Warp compresses the window the turn already had (principle 17): the
        // difficulty multiplier went in above, and the floor still applies.
        if (TimerPressureActive)
            total = TimerTable.Round(total * _timerPressureFactor);

        Timer = ResponseTimer.Start(total);
        AccumulatedInput = null;
        _responseWindowExpired = false;
        Phase = TurnPhase.ResponseWindow;
    }

    // ── step 11: the player acts ────────────────────────────────────────────────
    /// <summary>Record input without closing the turn (a gesture still being recognised).</summary>
    public void RecordInput(CoinAction action)
    {
        RequirePhase("input is only accepted while the response window is open", TurnPhase.ResponseWindow);
        AccumulatedInput = action;
    }

    /// <summary>A completed gesture closes the response window and moves to evaluation.</summary>
    public void SubmitInput(CoinAction action)
    {
        RecordInput(action);
        EndResponseWindow(expired: false);
    }

    /// <summary>The deliberate no-input decision (a first-class action, GDD R-1).</summary>
    public void ConfirmNoInput() => SubmitInput(CoinAction.None);

    // ── time (injected, never read) ─────────────────────────────────────────────
    /// <summary>
    /// Advance the loop by <paramref name="deltaSeconds"/> of play. Depletes the
    /// response timer and, when it runs out, closes the window for expiry
    /// resolution (R-1); depletes the activation window and closes it when it
    /// runs out. While paused (R-2) nothing moves.
    /// </summary>
    public void Advance(double deltaSeconds)
    {
        if (deltaSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds), deltaSeconds, "Time only moves forward.");
        if (IsPaused || deltaSeconds == 0) return;

        switch (Phase)
        {
            case TurnPhase.ResponseWindow:
                Timer = Timer.Advance(deltaSeconds);
                if (Timer.HasExpired) EndResponseWindow(expired: true);
                break;

            case TurnPhase.ActivationWindow:
                ActivationWindowRemainingSeconds = Math.Max(0, ActivationWindowRemainingSeconds - deltaSeconds);
                if (ActivationWindowRemainingSeconds <= 0) CloseActivationWindow();
                break;
        }
    }

    private void EndResponseWindow(bool expired)
    {
        _responseWindowExpired = expired;
        Phase = TurnPhase.Evaluation;
    }

    // ── steps 12–14: resolve, compare, trace source ─────────────────────────────
    /// <summary>
    /// Resolve the instruction through the Modifier Engine and compare it with the
    /// player's final action.
    ///
    /// R-1 (no-input resolution): expiry evaluates the turn's accumulated input as
    /// the final action. A no-act directive with no input is a CORRECT decision —
    /// the failure is expiry with a required, unperformed action.
    /// </summary>
    public TurnOutcome ResolveEvaluation()
    {
        RequirePhase("the engine evaluates after the response window closes", TurnPhase.Evaluation);

        Resolution = _engine.Resolve(
            Instruction!,
            new ResolutionContext(Rules, TurnNumber, ScreenText, Memory, ScreenValue));

        // A real Memory store turn commits its value once, before the comparison;
        // fake/ignored stores return no write (engine contract).
        if (Resolution.MemoryWrite is { } write) Memory.Apply(write);

        var requiredAction = Resolution.FinalDirective.Action;
        var finalAction = AccumulatedInput ?? CoinAction.None;
        var matched = finalAction == requiredAction;

        var kind = matched
            ? TurnOutcomeKind.Correct
            : _responseWindowExpired
                ? TurnOutcomeKind.TimerExpiry
                : TurnOutcomeKind.IncorrectAction;

        // Principle 25: source is only meaningful for an incorrect action, and it
        // depends on whether the instruction the player reacted to was mechanically
        // fake and still on screen.
        var sourceTrace = SourceTracker.Classify(
            matched, Instruction!, Resolution.IsMechanicallyVisibleFake);

        Outcome = new TurnOutcome(
            kind,
            TurnNumber,
            Instruction!.RuleId,
            requiredAction,
            AccumulatedInput,
            sourceTrace,
            AutoCardCheckRequired: kind != TurnOutcomeKind.Correct);

        AutoCardCheckPending = AutoCardCheckRequired(kind);
        Phase = TurnPhase.AutoCardCheck;
        return Outcome;
    }

    private static bool AutoCardCheckRequired(TurnOutcomeKind kind) => kind != TurnOutcomeKind.Correct;

    // ── step 15: auto cards ─────────────────────────────────────────────────────
    /// <summary>
    /// Close the auto-card opportunity (step 15) and open the between-turns
    /// activation window (step 16). Phase 5 supplies the actual card logic and
    /// passes the absorbing card's id; Phase 2 records it and always opens the
    /// window — it is never skipped (principles 43, 45).
    /// </summary>
    public void ResolveAutoCardCheck(string? absorbedByCardId = null)
    {
        RequirePhase(
            "the auto-card check runs once per evaluated turn (GDD loop step 15)",
            TurnPhase.AutoCardCheck);

        AutoCardCheckPending = false;
        if (absorbedByCardId is not null && Outcome is not null)
            Outcome = Outcome with { AbsorbedByCardId = absorbedByCardId };

        // Phase 5 penalty flow: evaluate → auto-card check → settle. The card id
        // (or its absence) is the only input Phase 6 has to supply; a non-null id
        // means the failure cost nothing.
        if (Outcome is not null)
            Run.Settle(Outcome, absorbedByCardId);

        // The window compresses proportionally with the response window in
        // streaming, and is never zero-length (principles 45, 14).
        var factor = TimerTable.CompressionFactor(Config.Mode, StreamingSpeed, Config.LevelBaseSeconds);
        ActivationWindowTotalSeconds = TimerTable.Round(Config.ActivationWindowSeconds * factor);
        ActivationWindowRemainingSeconds = ActivationWindowTotalSeconds;

        Phase = TurnPhase.ActivationWindow;
    }

    // ── step 17: the activation window closes ───────────────────────────────────
    /// <summary>Close the between-turns window (by player action or by expiring).</summary>
    public void CloseActivationWindow()
    {
        RequirePhase("the activation window can only close while it is open", TurnPhase.ActivationWindow);

        ActivationWindowRemainingSeconds = 0;
        Phase = TurnPhase.TurnComplete; // step 18: the next instruction may now appear
    }

    // ── GDD R-2: pause freezes everything ───────────────────────────────────────
    /// <summary>Freeze timers, streaming and countdowns (turn counters only move with instructions).</summary>
    public void Pause() => IsPaused = true;

    public void Resume() => IsPaused = false;

    private void RequirePhase(string because, params TurnPhase[] allowed)
    {
        if (Array.IndexOf(allowed, Phase) >= 0) return;

        throw new InvalidOperationException(
            $"Cannot perform this step in phase {Phase}: {because}. Allowed from: {string.Join(", ", allowed)}.");
    }
}
