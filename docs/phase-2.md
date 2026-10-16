# Phase 2 — Turn system, timers, game loop: Completion Documentation

**Status:** ✅ COMPLETE
**Date:** 2026-10-16
**Reference:** `IMPLEMENTATION_PLAN.md` §6 Phase 2 · `RuleBreaker_GDD_Final (1).md` (Core Game Loop, Turn System, Response Timer System, principles 9–18, 30–31, 43–45)

---

## 1. What Phase 2 Was Supposed To Deliver (per plan)

> **Goal:** The 19-step GDD Core Game Loop runs as a deterministic state machine.
> - `GameSession` state machine: InstructionShown → ResponseWindow → Evaluation → AutoCardCheck → ActivationWindow → Next
> - Global turn counter (starts at 1 on first instruction); per-rule turn countdowns
> - Response timer: `Level Base × Difficulty Multiplier` (1.5/1.0/0.7/0.5); streaming compression table (5/3/2/1s compressed bases); thresholds 20% orange / 10% red + critical-sound hook; **never zero** (principles 13, 14, 30, 31)
> - **R-1 implemented:** expiry evaluates accumulated input; no-input + no-act directive = correct; otherwise expiry failure
> - Between-Turns Activation Window: always present, compresses with streaming, never skipped (principles 43, 44, 45); new instruction never appears until both windows close (principle 18)
> - Streaming pacing: speed tiers, escalation curve (default: tier up every 20 turns, ≥10 turns per tier — tunable in Phase 14)
>
> **Acceptance:** loop tests covering principles 9–18, 30–31, 43–45; timer reference table (6/4/2s × 4 tiers) reproduced exactly in tests.

All of it is delivered; §5 records the two numbers the GDD leaves open and how they were pinned.

---

## 2. Delivered Artifacts

```
src/RuleBreaker.Core/
├── Model/
│   ├── Difficulty.cs        Difficulty + the GDD multiplier table (principles 30, 31)
│   └── TurnContext.cs       PacingMode · StreamingSpeed · TurnPhase · TurnContext (incl. the
│                            principle-12 future-turn reference gate) · SessionConfig
└── Systems/
    ├── TimerSystem.cs       TimerTable (both GDD reference tables, rounding, never-zero floor)
    │                        + ResponseTimer (remaining time, 20%/10% thresholds)
    ├── StreamingPacing.cs   escalation curve: Slow → Medium → Fast → Maximum
    └── GameSession.cs       the 19-step loop state machine + TurnOutcome
tests/RuleBreaker.Core.Tests/Systems/
├── TimerSystemTests.cs        the GDD timer tables reproduced exactly, thresholds, floors
└── GameSessionLoopTests.cs    the loop, R-1, R-2, windows, escalation, source tracing
```

Also touched in Phase 1 files (both were needed by the loop):
- `Model/ActiveRule.cs` — `RemainingTurns(currentTurn)` for the HUD's amber countdown.
- `Engine/ResolutionResult.cs` — `IsMechanicallyVisibleFake` fix (§6).

Suite: **128 tests / 128 passing** (81 from Phases 0–1, 47 new here). Build: 0 warnings, 0 errors.

---

## 3. The Loop As Implemented

Every loop step has a named touch point, so Phase 3+ plug in without re-opening the state machine:

| GDD loop step | Where it happens |
|---|---|
| 1. New instruction appears | `ShowInstruction(instruction)` |
| 2. Global Turn Counter increments | inside `ShowInstruction` (`TurnNumber`, 0 before the first instruction) |
| 3. Turn-based rules count down | inside `ShowInstruction` → `Rules.TickExpiry(turn)`; result in `LastTick` |
| 4. Instruction enters the active rule set | `ActivateRule(rule)` (also legal during the activation window for manual cards — principle 43) |
| 5–9. Modifiers modify / engine evaluates / trigger event | resolved inside `ResolveEvaluation` (Modifier Engine call) |
| 10. Response Timer begins | `OpenResponseWindow()` → `Final Timer = Level Base × Difficulty` (or the streaming compressed base) |
| 11. Player acts, or deliberately does not | `SubmitInput(action)` · `RecordInput(action)` (uncommitted gesture) · `ConfirmNoInput()` · `Advance(seconds)` until expiry |
| 12–14. Engine compares, traces source, scores | `ResolveEvaluation()` → `TurnOutcome` (+ `SourceTrace`) |
| 15. Auto Cards check fires | `ResolveAutoCardCheck(absorbedByCardId?)` — Phase 5/6 supply the cards, Phase 2 records the absorption |
| 16–17. Activation Window opens / closes | `ResolveAutoCardCheck` opens it · `CloseActivationWindow()` or window expiry closes it |
| 18. Next turn begins | `TurnComplete` is the only phase from which `ShowInstruction` is allowed — principle 18 enforced by the state machine, not by convention |
| 19. Difficulty increases over time | Phase 7 (mode/difficulty framework); the session already carries the tier in `TurnContext` |

Time is **injected**, never read: callers pass elapsed seconds to `Advance(...)`, which is the plan's clock port. That keeps the loop reproducible in tests, bisectable in CI and usable by the future procedural validator.

```
NotStarted ──ShowInstruction──▶ InstructionShown ──OpenResponseWindow──▶ ResponseWindow
                                                                              │  SubmitInput / ConfirmNoInput / Advance→expiry
                                                                              ▼
   TurnComplete ◀──CloseActivationWindow── ActivationWindow ◀──ResolveAutoCardCheck── AutoCardCheck ◀──ResolveEvaluation── Evaluation
        │
        └── ShowInstruction (next turn)
```

---

## 4. Acceptance Criteria — Verified

| Plan / GDD requirement | Test |
|---|---|
| GDD combined timer table, 6/4/2 s × 4 tiers reproduced exactly | `TimerSystemTests.TurnBasedTimerTable_MatchesTheGddReferenceTable` |
| GDD streaming timer table, 5/3/2/1 s × 4 tiers reproduced exactly | `TimerSystemTests.StreamingTimerTable_MatchesTheGddReferenceTable` |
| Principle 31: Easy 1.5 / Medium 1.0 / Hard 0.7 / Nightmare 0.5 | `DifficultyOrdering_IsEasyMediumHardNightmare_ShortestLast`, `MediumDifficulty_EqualsTheLevelBaseExactly` |
| Principle 30: Final Timer = Level Base × Difficulty | `GameSessionLoopTests.ResponseWindow_UsesLevelBaseTimesDifficulty` |
| Principle 14: timer is never zero | `ResponseTimer_IsNeverZero_EvenForAPathologicalBase` |
| 20% orange / 10% red thresholds | `TimerThresholds_FollowTheTwentyAndTenPercentRule` |
| Principle 9: one instruction = one turn (in streaming too) | `TurnCounter_StartsAtZero_ThenCountsOnePerDisplayedInstruction`, `StreamingMode_KeepsOneInstructionOneTurn` |
| Principle 10: absolute counter, starts at 1 | same two + `TurnContext_CarriesTheTurnItsDifficultyAndItsPacing` |
| Principle 11 + GDD countdown table (3/2/1 then expired) | `RuleCountdown_TicksOncePerInstruction_ThenExpiresPermanently` |
| Principle 12: turn-specific rules reference future turns only (GDD: at Turn 8 nothing may reference Turn 5) | `TurnSpecificReferences_MustPointAtAFutureTurn` |
| Principles 13/16: every turn has a window; expiry penalised identically on all tiers | `Expiry_WithARequiredUnperformedAction_IsATimerExpiryFailure`, `TimerExpiry_IsIdenticalOnEveryDifficultyTier` |
| Principle 14/15: streaming compresses; escalation tier up every 20 turns | `StreamingTimers_CompressAsTheEscalationTierRises` |
| Principle 18 / 44: no new instruction until both windows close | `NewInstruction_CannotAppearWhileEitherWindowIsOpen` |
| Principle 43: manual cards only in the activation window | `ManualRuleActivation_OnlyInsideItsWindowOrWithTheInstruction` |
| Principle 45: the window is always present, never skipped, compresses proportionally | `ActivationWindow_AlwaysOpens_ForEveryOutcome`, `StreamingActivationWindow_CompressesProportionally_AndIsNeverZero`, `ActivationWindow_ClosesWhenItsTimeRunsOut` |
| R-1: no-act directive + no input at expiry = **correct** | `Expiry_WithNoActDirectiveAndNoInput_IsACorrectDecision` |
| R-1: required action unperformed at expiry = timer-expiry failure + auto-card check | `Expiry_WithARequiredUnperformedAction_IsATimerExpiryFailure` |
| R-1: accumulated input is what expiry evaluates | `Expiry_WithAPartialInputThatNoLongerMatches_IsATimerExpiryFailure` |
| Steps 12–14: correct/wrong comparison, Auto Card gate | `WrongAction_IsAnIncorrectActionFailure`, `CorrectAction_DoesNotRequestAnAutoCardCheck` |
| Step 15 hand-off (R-8 ordering lands in Phase 5) | `AutoCardCheck_RecordsAnAbsorbingCard` |
| R-2: Escape freezes timers, windows and countdowns | `Pause_FreezesTheResponseTimerAndTheActivationWindow` |
| Principle 25: source trace through the turn outcome | `WrongAction_OnAVisibleFake_TracesToFakeOrigin`, `WrongAction_OnARealInstruction_TracesToReal` |
| Windows never overlap; steps stay in order | `Windows_CannotOverlapOrBeDrivenOutOfOrder` |
| Tunables that would break GDD guarantees are rejected | `SessionConfig_RejectsValuesThatBreakGddGuarantees` |

**Verification commands (run in `Project_RuleBreaker/`):**

```bash
dotnet build --nologo      # 0 warnings, 0 errors
dotnet test  --nologo      # 128 passed / 0 failed
dotnet run --project tools/RuleBreaker.Tools.LevelValidator -- data/rules/act_click.json data/rules/trp_click_now.json
```

---

## 5. Decisions & Pinned Numbers

| Decision | Why |
|---|---|
| **Time is an explicit `Advance(seconds)` argument** | The plan asks for a `TurnClock`/`ClockPort` abstraction so Godot injects real time. An explicit delta is that port with nothing ambient: no `DateTime.Now`, no timers, fully deterministic and headless-testable. Phase 9/10 wrap it with the real clock. |
| **Activation-window base = 2.0 s (turn-based)** | The GDD fixes the window's *behavior*, not its length. It is a Phase 14 balance pin exactly like the R-12 economy defaults, so it is a named config constant (`SessionConfig.DefaultActivationWindowSeconds`) rather than a literal in the loop. Memory Mode's +50% (Phase 7) multiplies this. |
| **Window compression = compressed base ÷ level base** | GDD: "compresses proportionally with the response window." The ratio is the same for every difficulty tier (the multiplier cancels), so one factor drives both windows. In turn-based play the factor is 1. |
| **Escalation curve: tier = ⌊(turn−1) ÷ turnsPerTier⌋, capped at Maximum** | Plan ruling 4 (tier up every 20 turns, min 10 turns per tier). `turnsPerTier` is configurable and the "≥10 turns per tier" floor is enforced by validation, so Phase 14 can retune without touching the curve. |
| **`Evaluation` and `AutoCardCheck` are real phases, not internal steps** | The plan lists them in the state machine, and both have genuine external actors (scoring in Phase 4/5, cards in Phase 5/6). Making them observable keeps the ordering testable instead of implied. |
| **Timers round to milliseconds and clamp to a 0.1 s floor** | Rounding is what makes the GDD's own table come out exactly (`6 s × 0.7` is `4.199999999999999` in binary floating point). The floor implements "the minimum response timer is never zero" for any input, including a pathological level base. |
| **`SessionConfig` validates at construction** | A level with a zero base timer, a zero-length window, or an escalation faster than 10 turns/tier would silently break a locked principle; those are now rejected at the boundary instead of producing an unplayable session. |
| **Principle 12 lives on `TurnContext`, not in the loop** | "Turn-specific rules reference future turns only" is a constraint on authored *content*, so the shared gate sits on the turn context that both content systems already hold: level scripts (Phase 13) and the procedural generator (Phase 7) call `ValidateFutureTurnReference` before a rule is admitted. Phase 2 supplies the rule and its tests; it does not author content. |

---

## 6. Defects Found & Fixed

| # | Defect | Fix |
|---|---|---|
| 1 | **`ResolutionResult.IsMechanicallyVisibleFake` could never be true** (Phase 1): it was defined as `IsFakeCurrentlyDisplayed && IsReal`, but `IsReal` is false for every fake result, so Source B detection had a dead signal. Phase 2 consumes it for source tracing, which is how it surfaced. | Defined as "a fake is on screen and still mechanically fake": `IsFakeCurrentlyDisplayed && !IsTrustedThenReal && !IsIgnored`. `WrongAction_OnAVisibleFake_TracesToFakeOrigin` now exercises it end-to-end. |
| 2 | `ResponseTimer` had no floor and no rounding, so the GDD's table values were not reachable exactly. | Millisecond rounding + the 0.1 s floor in `TimerTable` (principle 14). |

Both were caught by newly written tests rather than by weakening them: five Phase-2 tests failed on first run and the fixes went into the implementation (a mis-set tier in a test helper was also corrected, since a helper that overshot by one turn was the test's own bug).

---

## 7. Checks That Could Not Run Here

- **GitHub Actions CI** — the workflow is committed; the equivalent commands were run locally (§4). The Actions API is unreachable from this environment, so the run status cannot be confirmed from here.
- **Godot integration (Phase 9/10)** — no engine code exists yet, so the real clock, input polling and HUD are not wired. The loop is headless by design.
- **Difficulty increase over time (loop step 19)** — belongs to Phase 7 (mode/difficulty framework); the session already exposes the tier.
- **Principle 17 (Time Warp card)** — the card itself is Phase 6 with its R-10 parameters (5 turns at −40% in turn-based). The guarantees it depends on are already pinned here: a shorter window never redefines a turn (principle 9), and the difficulty multiplier still applies on top of any compression (principle 30).
- **Card behavior in steps 15–17** — Phase 5/6. Phase 2 provides the phases, the absorption hook and the R-8 input (whether the failure was an incorrect action or an expiry).

---

## 8. What Phase 3 Picks Up

1. Gesture recogniser: Click · DoubleClick (≤300 ms) · Hold (≥400 ms) · Drag/Swipe (≥24 px) · NoInput, with the disambiguation rules.
2. Feed completed gestures into `SubmitInput(...)` and partial ones into `RecordInput(...)` — the R-1 accumulated-input path is already waiting for exactly that split.
3. Coin feedback event hooks (depress / flip / glow / follow) for the presentation layer.
4. Keyboard handled as UI-only (principles 51, 52).
