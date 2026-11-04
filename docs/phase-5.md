# Phase 5 — Evaluation, scoring, combo, lives, penalties · Completion Documentation

**Status:** ✅ COMPLETE
**Date:** 2026-11-04
**Reference:** `IMPLEMENTATION_PLAN.md` §6 Phase 5 · `RuleBreaker_GDD_Final (1).md` (Fake Instruction Penalty System, Combo System, Lives System, principles 16, 24–28, 41–42, 46, 48)

---

## 1. What Phase 5 Was Supposed To Deliver (per plan)

> **Goal:** Every decision produces the GDD-specified outcome.
> - Final-action evaluation (principle 24): accidental correct = rewarded, no intention check
> - Penalty matrix: Source A (real mistake) → normal all tiers; Source B (fake) → Easy none / Medium combo-reset-only / Hard+Nightmare full; timer expiry → full all tiers (principles 16, 26–28)
> - Auto Card check in the penalty flow (loop step 15) with R-8 priority
> - Combo: 5→2x, 10→3x, 20→5x; reset table per difficulty; milestones at 10/20/30+ (principle 48)
> - Lives: 3; loss rules per matrix; Game Over; gold-coin/dark-circle state
> - Score: base 100 × multiplier (R-12); HUD state snapshot
>
> **Acceptance:** principles 16, 24–28, 41–42, 46, 48 as tests; full penalty matrix table test.

All of it is delivered. The two items that cannot land in the headless core — the gold-coin/dark-circle rendering and the red/amber/grey penalty flashes — are Phase 9 presentation; the core exposes the exact state they draw from (§7).

---

## 2. Delivered Artifacts

```
src/RuleBreaker.Core/Systems/
├── PenaltyMatrix.cs   PenaltyKind · PenaltyRules (the GDD table, combo-reset and
│                      life-loss consequences) · AutoCardTrigger · AutoCardRules
│                      (principles 41–42 + the R-8 fire order)
├── ComboSystem.cs     ComboMilestone (10/20/30+) · ComboRules (5→2x, 10→3x, 20→5x,
│                      milestone thresholds) · ComboMeter
├── RunState.cs        TurnSettlement · RunState (score, combo, lives, game over,
│                      one-settlement-per-turn contract, auto-card absorption)
├── HudSnapshot.cs     the immutable HUD read (score/lives/combo/turn/timer/rules)
└── GameSession.cs     + Run · LastSettlement · Hud · IsGameOver; settles at loop
                       step 15, once the auto-card check has decided absorption

tests/RuleBreaker.Core.Tests/Systems/
├── PenaltyMatrixTests.cs    the full 16-cell matrix (4 tiers × correct/real/fake/expiry),
│                            combo-reset table, life-loss table, Joker/Shield triggers, R-8
├── ComboScoreTests.cs       multiplier thresholds, milestone thresholds, R-12 payout
└── RunStateTests.cs         lives/game over, absorption, principle 46, principle 24,
│                            session penalty flow, HUD snapshots
```

Suite: **319 tests / 319 passing** (235 from Phases 0–4, 84 new here). Build: 0 warnings, 0 errors.
Library CLI: **8 files, 100 rules, 0 errors, 0 lint errors.**

---

## 3. The Penalty Flow As Implemented

```
ResolveEvaluation()            (Phase 2, unchanged)
  outcome = Correct | IncorrectAction | TimerExpiry
  source  = Real | FakeOrigin                    (principle 25, Phase 1/2)

ResolveAutoCardCheck(absorbedByCardId?)          (GDD loop step 15)
  │
  ├── AutoCardRules.FireOrder(outcome.Kind)      R-8
  │     incorrect action → [Joker, Shield]        principles 41–42
  │     timer expiry     → [Shield]
  │     correct          → []
  │
  ├── Run.Settle(outcome, absorbedByCardId)      ← Phase 5
  │     penalty = PenaltyRules.For(kind, source, difficulty)
  │              0. absorbed? → None (nothing paid)
  │              1. Easy + fake mistake → None
  │              2. Medium + fake mistake → ComboResetOnly
  │              3. everything else failed → Full
  │     correct → combo++, score += 100 × multiplier
  │     penalty.ResetsCombo() → combo = 0
  │     penalty.CostsLife()  → lives--
  │
  └── ActivationWindow opens (step 16, Phase 2)
```

The settlement is a `TurnSettlement` record, so every consumer downstream reads the
same facts: what was paid, what it would have cost without the card, the new score,
combo, multiplier, milestone, lives, and whether the run ended.

---

## 4. Acceptance Criteria — Verified

| Plan / GDD requirement | Test |
|---|---|
| Full penalty matrix table, all 16 cells | `PenaltyTable_MatchesTheGddRowForEverySourceAndTier` (16 rows) |
| Principle 16: timer expiry = full penalty on every tier, no Easy exception | `TimerExpiry_IsAFullPenaltyOnEveryTier_WithNoEasyException`, `TimerExpiry_CostsALifeAndTheCombo_OnEveryTier` |
| Source A real mistake = normal penalty on every tier | `RealInstructionMistake_IsAFullPenaltyOnEveryTier`, `RealInstructionMistake_CostsALifeAndTheCombo_OnEveryTier` |
| Principle 26: Easy fake mistake = no penalty | `FakeInstructionMistake_FollowsTheSourceBRow`, `Session_EasyFakeMistake_CostsNothing_AndKeepsTheCombo` |
| Principle 27: Medium fake mistake = combo reset only, no life | same theory + `Session_MediumFakeMistake_ResetsTheComboOnly` |
| Principle 28: Hard/Nightmare fake mistake = full penalty | same theory + `FakeInstructionMistake_CostsExactlyWhatItsTierSays` |
| Principle 24: final action decides — accidental correct is rewarded normally | `Session_AccidentalCorrectAction_IsRewardedNormally` (fake trap on screen, coincidental Click → correct, full payout) |
| Combo reset table (real / fake / expiry / absorbed) | `ComboReset_MatchesTheGddResetTable`, `FakeInstructionMistake_CostsExactlyWhatItsTierSays`, `AbsorbedFailure_LeavesLivesAndComboUntouched_AndPaysNothing` |
| Lives: 3, one per full penalty, never negative | `ThreeFullPenalties_EndTheRun`, `LifeLoss_OnlyHappensOnFullPenalties` |
| Game Over at zero lives | `ThreeFullPenalties_EndTheRun`, `Session_GameOver_IsReportedThroughTheRunAndTheHud` |
| 1-life run (Hardcore hook, Phase 7) | `AOneLifeRun_EndsOnItsFirstFullPenalty` |
| Combo multipliers 5→2x, 10→3x, 20→5x | `Multiplier_MatchesTheGddComboTable` (9 cases), `RegisterCorrect_GrowsTheChain_AndTheMultiplierFollows` |
| Score = 100 × multiplier (R-12) | `CorrectDecisions_ScoreOneHundredTimesTheMultiplier`, `Session_FiveCorrectDecisions_PayTheTwoTimesCombo` |
| Combo reset returns to 1x | `Reset_ReturnsTheChainAndMultiplierToTheStart` |
| Principle 48: milestones at 10 / 20 / 30+ | `Milestones_FireAtTheGddThresholds`, `Milestones_DoNotRepeatBetweenThresholds`, `Milestone_IsReportedOnTheSettlementThatCrossesIt` |
| Principle 41: Joker never fires on timer expiry | `Joker_FiresOnIncorrectActionsOnly_NeverOnTimerExpiry` |
| Principle 42: Shield fires on any failure | `Shield_FiresOnAnyFailure_IncludingTimerExpiry` |
| R-8: Joker before Shield on an incorrect action; Shield alone on expiry | `AutoCardOrder_PutsJokerBeforeShield_OnAnIncorrectAction`, `AutoCardOrder_OffersOnlyShield_OnTimerExpiry`, `NoAutoCardIsOffered_ForACorrectDecision` |
| Auto Card absorbs failure: no life, no reset | `AbsorbedFailure_LeavesLivesAndComboUntouched_AndPaysNothing`, `Session_SettlesAFailureAbsorbedByAnAutoCard` |
| Principle 46: Joker on an Easy fake mistake is a wasted activation | `AbsorptionOnAnEasyFakeMistake_IsAWastedActivation` |
| Settlement happens after the auto-card check, exactly once per turn | `Session_SettlesOnlyAfterTheAutoCardCheck`, `ATurn_IsSettledExactlyOnce` |
| HUD state snapshot (score, lives, combo, turn, timer, rules, game over) | `Session_HudSnapshot_CarriesThePhase5State`, `Session_HudSnapshot_IsAnImmutableRead`, `Session_HudSnapshot_CountsActiveRulesForTheLeftPanel` |
| Unknown enum inputs are rejected, not guessed | `UnknownPenaltyInputs_AreRejectedRatherThanGuessed`, `StartingLives_MustBePositive`, `NegativeChains_AreRejected` |

**Verification commands (run in `Project_RuleBreaker/`):**

```bash
dotnet build --nologo      # 0 warnings, 0 errors
dotnet test  --nologo      # 319 passed / 0 failed
dotnet run --project tools/RuleBreaker.Tools.LevelValidator -- --library data/rules/*.json
                           # 8 files, 100 rules, 0 errors, 0 lint errors
```

---

## 5. Decisions & Pinned Numbers

| Decision | Why |
|---|---|
| **The run is settled at loop step 15, not at evaluation** | GDD puts the auto-card check inside the penalty flow, and whether a card absorbs the failure changes the penalty. Settling at `ResolveAutoCardCheck(...)` makes "evaluate → absorb → pay" one ordered path, and the `absorbedByCardId` argument is the only input Phase 6 has to supply. |
| **One settlement per turn, enforced** | A double-settled turn would silently double-charge a life or double-pay a score. `RunState` rejects a second settlement for the same turn, so a future caller cannot get it wrong quietly. |
| **The multiplier is read after the chain grows** | GDD "5 correct decisions → 2x" reads as the 5th decision completing the set, so it is the first paid at 2x. This is a Phase 14 tuning candidate like the R-12 defaults; pinning it here keeps the payout deterministic and testable. |
| **`PenaltyKind` carries magnitude only; consequences are derived** | `ResetsCombo` and `CostsLife` are functions of the penalty, so the penalty table, the combo-reset table and the lives table cannot drift apart. Easy-fake "None" automatically means no reset and no life loss. |
| **Absorption leaves an explicit `PenaltyBeforeAbsorption`** | Presentation and telemetry still need to know what the mistake would have cost (Shield "absorbs the full penalty"), and principle 46 needs to distinguish a meaningful absorb from a wasted one: `AbsorbedRedundantPenalty` is true exactly when the table said `None`. |
| **Game over is reported, not enforced by the loop** | `Run.IsGameOver` and the HUD report zero lives the moment the third life is spent, but `ShowInstruction` keeps working. Stopping the run belongs to the run shell (Phase 7 modes / Phase 11 UI); the turn loop is deliberately run-agnostic, which is also what lets the Phase 2 loop tests play long failing sequences. |
| **`RunState` takes `startingLives` (default 3)** | Hardcore's 1-life rule (Phase 7) is then a construction argument, not a special case in the penalty code. |
| **R-8 lives as a pure fire order, not a card system** | Phase 5 must not pretend to own card selection. `AutoCardRules.FireOrder` pins "Joker first on an incorrect action, Shield alone on expiry"; Phase 6 resolves the order against the loadout and consumes exactly one card. |
| **HUD snapshot computed on demand** | The presentation layer reads one immutable record and never touches session state (plan architecture rule 4). If Phase 15 profiling shows per-frame allocation matters, the snapshot can be cached without changing its shape. |
| **`EndedRun` means "game over after a full penalty"** | After the run is over, the shell stops asking for turns; the flag documents the turn that spent the last life. Lives are clamped at zero so even an out-of-contract caller cannot produce a negative HUD. |

---

## 6. Defects Found & Fixed

| # | Defect | Fix |
|---|---|---|
| 1 | **`PenaltyRules.For` did not validate the difficulty on branches that do not read it.** A correct decision with an undefined `Difficulty` returned `PenaltyKind.None` instead of failing, so an invalid tier could travel through the penalty flow unnoticed until a failure row happened to be hit. Found by `UnknownPenaltyInputs_AreRejectedRatherThanGuessed`. | The tier is validated at the top of `For`, so every outcome kind rejects an undefined difficulty. The test was kept and the implementation corrected. |
| 2 | A test-helper parameter was named `lives` while the test called it by `startingLives` (compile error, caught before any test ran). | Helper renamed to `startingLives` to match `RunState`'s public constructor argument. |

No behaviour in Phases 0–4 changed: the pre-existing 235 tests still pass unmodified, including the Phase 2 test that deliberately plays more failing turns than the 3 lives a run has.

---

## 7. Checks That Could Not Run Here

- **Penalty visuals** — full red flash, grey dismissal (Easy fake), amber combo flash (Medium fake), gold coin vs dark empty circle life icons. The core exposes `TurnSettlement.Penalty`, `AbsorbedByAutoCard`, `HudSnapshot.Lives/MaxLives/SpentLives` and the timer fractions; Phase 9 draws them.
- **Auto Card consumption and the loadout** — which cards are owned, which is offered, and the single-use consumption are Phase 6. Phase 5 supplies the R-8 order and records the winner's id.
- **Score/leaderboard persistence and per-tier boards** — Phase 11/12; `HudSnapshot.Score` is the number they will submit.
- **Mode rules that change lives or run end** (Hardcore 1 life, timer expiry ending the run) — Phase 7; the 1-life construction hook is already tested.
- **Balance pass** — R-12's 100/2x/3x/5x and the milestone thresholds are pinned as specified but are Phase 14 tuning candidates, like the activation window and gesture thresholds.
- **GitHub Actions CI** — the workflow is committed, but this environment cannot confirm a run; the same commands were executed locally (§4).

---

## 8. What Phase 6 Picks Up

1. Card definitions and the eight effect handlers, using `AutoCardRules.FireOrder` for the R-8 selection and `TurnSettlement.AbsorbedRedundantPenalty` for principle 46.
2. Card consumption/persistence: the absorbing card id already reaches the outcome and the settlement; the loadout/collection model is Phase 6.
3. Combo-milestone acquisition: `TurnSettlement.Milestone` reports 10/20/30+ exactly once per chain.
4. Score economy inputs for the shop: `RunState.Score` and the milestone events are the earn signals.
5. A save/load round trip for collection, loadout and per-tier best scores (Phase 11 owns the file format; Phase 6 owns the model).
