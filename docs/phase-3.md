# Phase 3 — Input & coin interaction: Completion Documentation

**Status:** ✅ COMPLETE
**Date:** 2026-10-17
**Reference:** `IMPLEMENTATION_PLAN.md` §6 Phase 3 · `RuleBreaker_GDD_Final (1).md` (Input System, principles 50–53)

---

## 1. What Phase 3 Was Supposed To Deliver (per plan)

> **Goal:** Mouse-first gameplay input with unambiguous gesture recognition.
> - Gesture recognizer (release-evaluated): Click · DoubleClick (≤300 ms window) · Hold (≥400 ms) · Drag/Swipe (≥24 px) · **NoInput**
> - Disambiguation rules: a press that exceeds hold threshold becomes Hold; movement beyond drag threshold becomes Swipe; double-click window closes before a lone Click commits. All thresholds in one tunable config.
> - Deliberate no-input is a first-class action (R-1)
> - Coin feedback event hooks: depress (click), flip (double-click), glow (hold), follows cursor (drag) — presentation consumes events
> - Keyboard: Escape/Tab/F1/F2/R/Enter/Arrows → UI only (principle 51–52)
>
> **Acceptance:** principles 50–53; gesture unit tests incl. ambiguous-timing cases; core gameplay runnable with keyboard fully disconnected.

All of it is delivered, including principle 53: the display specification is pinned in `DisplaySpec` as the coordinate space gestures are measured in, and verified headlessly (§4). Rendering it at those resolutions remains Phase 9's visual QA (§7).

---

## 2. Delivered Artifacts

```
src/RuleBreaker.Core/Input/
├── GestureConfig.cs        one tunable place: 300 ms window · 400 ms hold · 24 px drag
├── Gesture.cs              GestureKind (5) · CoinFeedback (4) · Gesture record + action mapping
├── GestureRecognizer.cs    release-evaluated recogniser + live provisional gesture
├── SessionInputAdapter.cs  feeds gestures into GameSession (record/submit split, R-1)
└── KeyboardShortcuts.cs    the GDD's ten keys → UI commands (no gameplay path)
src/RuleBreaker.Core/Model/
└── DisplaySpec.cs          principle 53: 16:9 · 1920×1080 minimum · design scale (Phase 9 draws it)
tests/RuleBreaker.Core.Tests/Input/
├── GestureRecognizerTests.cs    five gestures, ambiguous timing, thresholds, feedback, malformed streams
├── SessionInputAdapterTests.cs  gestures drive the Phase 2 loop; expiry mid-press; keyboard-free run
└── KeyboardShortcutTests.cs     GDD key table, structural "no keyboard entry point", state untouched
tests/RuleBreaker.Core.Tests/DisplaySpecTests.cs   supported resolutions, portrait/4:3 rejection, scale
```

Suite: **201 tests / 201 passing** (128 from Phases 0–2, 73 new here). Build: 0 warnings, 0 errors.

The core stays engine-free: no Godot types, no ambient clock, no device polling. Phase 9/10 feed pointer events from `_UnhandledInput` into the same API the tests drive.

---

## 3. The Five Gestures and Their Disambiguation

Recognition is **release-evaluated**: a press becomes exactly one gesture when the button comes up. All times are injected play-clock seconds.

| Gesture | Rule as implemented |
|---|---|
| **Click** | A simple tap (no threshold crossed) that is **not reported immediately**: it commits once the 300 ms double-click window closes. A second press inside the window therefore still becomes a DoubleClick. |
| **DoubleClick** | Two simple taps where the second press begins within 300 ms of the first release. The second press must itself be a simple tap — a held or dragged second press is its own gesture, and the first tap is committed as a Click. |
| **Hold** | The press reaches 400 ms before any drag threshold is crossed, and is released. The coin glows at the threshold instant, while the button is still down. |
| **Swipe / Drag** | Travel from the press point reaches 24 px before the hold threshold is crossed, at release. The coin follows the cursor from the crossing instant. |
| **NoInput** | Declared explicitly for a response window with no input (R-1). Never inferred from a missing event. |

Two rules make the ambiguity deterministic:

1. **First threshold crossed wins.** Both crossing instants are recorded, so a release at 700 ms cannot reclassify a drag that began at 100 ms — and a press that reached 400 ms before moving stays a Hold even if the player drags before releasing.
2. **Only a simple tap completes a double click.** If the second press becomes a Hold or Swipe, any pending first tap is flushed immediately as a Click, in order before the new gesture.

`GestureRecognizer.LiveGesture` exposes what the in-flight press *would* be if released now, using the same rule. That is what the adapter accumulates, so an expiry mid-press evaluates what the player was doing rather than the state at press-down.

### Coin feedback hooks (GDD "Coin Visual Feedback")

| Event | When |
|---|---|
| `Depress` | On every press |
| `Flip` | When a DoubleClick is recognised |
| `Glow` | Once the hold threshold is crossed, while still down |
| `FollowCursor` | Once the drag threshold is crossed |

Feedback is a queue; presentation drains it. NoInput emits nothing — GDD "Ignore Coin: no reaction".

### Keyboard

`KeyboardShortcuts` maps exactly the ten GDD keys (Escape → pause, Tab → rule log, F1 → settings, F2 → HUD toggle, R → replay, Enter → confirm, arrows → navigation). There is no keyboard path into gameplay, and this is structural rather than a convention: `GameSession` has no method taking a `KeyboardShortcut` or `UiCommand`, which `GameSession_HasNoKeyboardEntryPoint` asserts by reflection.

---

## 4. Acceptance Criteria — Verified

| Plan / GDD requirement | Test |
|---|---|
| Principle 50: mouse-only gestures — Click / DoubleClick / Hold / Swipe | `Click_IsNotReportedUntilTheDoubleClickWindowCloses`, `TwoTapsInsideTheWindow_AreOneDoubleClick_NotTwoClicks`, `APressThatReachesTheHoldThreshold_IsAHold_NotAClick`, `MovementPastTheDragThreshold_IsASwipe_OnlyOnceTheButtonComesUp` |
| ≤300 ms double-click window, and a lone Click commits only after it closes | `Click_IsNotReportedUntilTheDoubleClickWindowCloses`, `APressAfterTheWindow_CommitsTheFirstTapThenStartsANewOne` |
| ≥400 ms hold threshold | `APressThatReachesTheHoldThreshold_IsAHold_NotAClick`, `HoldThresholdAtExactly400Milliseconds_CountsAsAHold` |
| ≥24 px drag threshold | `MovementPastTheDragThreshold_IsASwipe_OnlyOnceTheButtonComesUp`, `MovementBelowTheDragThreshold_StaysAClick`, `DragDistanceIsMeasuredFromThePressPoint_NotFromTheLastMove` |
| Ambiguous timing: hold vs drag, drag vs hold, second press not a tap | `DragBeforeHold_StaysASwipeEvenWhenTheButtonIsHeld`, `HoldBeforeDrag_StaysAHoldEvenWhenThePlayerDragsLater`, `ASecondPressThatBecomesAHold_LeavesTheFirstTapAsAClick`, `ASecondPressThatBecomesASwipe_LeavesTheFirstTapAsAClick` |
| All thresholds in one tunable config, validated | `DefaultThresholds_AreThePinnedValues`, `CustomThresholds_ChangeRecognition`, `NonPositiveThresholds_AreRejected` |
| NoInput is a first-class action (R-1) | `NoInput_IsAFirstClassGesture_NotTheAbsenceOfOne`, `NoInput_IsRejectedWhileInputIsStillInFlight`, `DeliberateNoInput_SubmitsTheNoInputAction` |
| Coin feedback hooks: depress / flip / glow / follow | `CoinFeedback_MatchesTheGddTable`, `TheCoinGlowsWhileTheButtonIsStillDown_AndTheGestureWaitsForRelease`, `CoreGameplayRunsOnMouseAlone_ForATurnOfEveryGestureType` |
| Gestures drive the loop through the record/submit split | `Click_IsRecordedWhileInFlight_ThenSubmittedWhenItIsUnambiguouslyAClick`, `DoubleClick_SubmitsDoubleClick`, `Hold_SubmitsHold`, `Swipe_SubmitsSwipe` |
| R-1: expiry mid-press evaluates accumulated input | `ExpiryDuringAnInFlightClick_EvaluatesTheAccumulatedInput`, `ExpiryDuringAHold_EvaluatesTheAccumulatedHold`, `ExpiryDuringADrag_EvaluatesTheAccumulatedSwipe` |
| Principle 51: keyboard is exactly the ten GDD keys, UI-only | `EveryShortcut_MapsToTheGddUiFunction`, `TheKeyboardVocabulary_IsExactlyTheGddTable`, `EveryKeyboardCommand_IsAUiConcern_NotACoinAction` |
| Principle 52: core gameplay never requires the keyboard | `CoreGameplayRunsOnMouseAlone_ForATurnOfEveryGestureType`, `GameSession_HasNoKeyboardEntryPoint` |
| Principle 53: 16:9, 1920×1080 minimum, QHD supported, no portrait | `WidescreenResolutionsAtOrAboveTheMinimum_AreSupported`, `AnythingThatIsNotWidescreenAndAtLeastFullHd_IsRejected`, `TheDesignSpaceIs1080pWithA16By9Aspect`, `ScaleIsOneAtTheMinimum_AndGrowsWithTheViewport` |
| Pressing keys cannot change gameplay state | `PressingEveryShortcut_DoesNotChangeGameplayState` |
| Gestures outside the response window are not gameplay input | `GesturesOutsideTheResponseWindow_AreNotSubmitted` |
| Malformed pointer streams are rejected, not guessed | `ReleaseWithoutAPress_IsRejected`, `TwoPressesWithoutARelease_AreRejected`, `TimeOnlyMovesForward` |

**Verification commands (run in `Project_RuleBreaker/`):**

```bash
dotnet build --nologo      # 0 warnings, 0 errors
dotnet test  --nologo      # 201 passed / 0 failed
dotnet run --project tools/RuleBreaker.Tools.LevelValidator -- data/rules/act_click.json data/rules/trp_click_now.json
```

---

## 5. Decisions & Pinned Numbers

| Decision | Why |
|---|---|
| **300 ms / 400 ms / 24 px are the plan's numbers, in `GestureConfig`** | The GDD names the gestures, not their thresholds. They are design tunables like the Phase 2 activation window, so they live in one validated object — Godot reads the same defaults, and a future accessibility option can widen the window without touching the recogniser. |
| **The double-click window runs from the first release to the second press** | That is the standard desktop convention (press-to-press interval) and it makes the deferred commit well-defined: the window closes only after the first release, so a press that starts before it is a real candidate, and a slow second tap cannot lose its pairing mid-air. |
| **The first threshold crossed wins, not the larger excursion** | Otherwise the same physical motion would be classified by timing accident (when the release happens). Recording both crossing instants makes the outcome independent of release latency and gives the rule one sentence: whichever happened first. |
| **A Click is deferred, everything else commits on release** | Deferral is the price of a double click; Hold and Swipe are unambiguous by the time the button comes up. `AdvanceTo` commits the deferred Click at its deadline so the queue does not depend on the next event. |
| **`LiveGesture` + provisional `RecordInput`** | Phase 2 built `RecordInput` for exactly this. Recording the live interpretation on every pointer/frame event means an expiry mid-press has the real accumulated input to evaluate (R-1), including a hold that crossed its threshold while the player kept still. |
| **NoInput cannot be declared while input is in flight** | "No input" and "a press is resolving" contradict each other; silently discarding the press would make a deliberate-no-input decision hide a real gesture. The recogniser throws instead, and the adapter only declares it when the window ends with nothing in hand. |
| **Feedback events are separate from gestures** | GDD feedback starts before a gesture exists (the depress on press-down, the glow at 400 ms). Emitting from the recogniser's own clock keeps presentation in sync without polling `IsHolding`. |
| **Card-slot clicks are not submitted to the loop** | Clicks outside the response window are counted (`DroppedGestureCount`) and left for the Phase 6 card UI, which owns the activation window's hit-testing. |
| **`DisplaySpec` lives in the core, not the presentation layer** | Principle 53 is in this phase's acceptance, and the display size is the coordinate space gestures are measured in (24 px, distance thresholds). Pinning the ratio and the minimum here lets Phase 9 consume one source of truth and lets Phase 3 test the constraint without a renderer. |

---

## 6. Defects Found & Fixed

| # | Defect | Fix |
|---|---|---|
| 1 | The adapter recorded the provisional input only on press and movement, so a press that crossed the 400 ms hold threshold while the button stayed still was still accumulated as `Click` — an expiry mid-hold would evaluate the wrong accumulated action, contradicting R-1's "accumulated input". Found while reviewing the R-1 path against the hold case, before it reached a test. | `GestureRecognizer.LiveGesture` (same first-threshold-wins rule as release) is recorded on every pointer/frame event. Pinned by `ExpiryDuringAHold_EvaluatesTheAccumulatedHold` and `ExpiryDuringADrag_EvaluatesTheAccumulatedSwipe`. |
| 2 | One new test asserted a 1 s duration for `NoInput`, which has no press to measure. The implementation was right (duration 0, timestamp = declaration time); the assertion was wrong and was corrected in the test rather than by giving NoInput a fictitious press. | `NoInput_IsAFirstClassGesture_NotTheAbsenceOfOne` asserts the declaration timestamp and a zero duration. |

No claim in this phase rests on a test that was weakened to pass; the full suite went 128 → 201 with one failing assertion on the first run (item 2) and one pre-test implementation fix (item 1).

---

## 7. Checks That Could Not Run Here

- **Godot input polling (Phase 9/10)** — the recogniser takes raw coordinates and an injected clock; wiring `_UnhandledInput` and the real frame delta is presentation work that does not exist yet. The same applies to the coin's visual follow/glow, which consume the feedback queue.
- **Principle 53 at runtime** — the constraint, supported set and design scale are verified headlessly. Rendering a readable 16:9 layout at 1920×1080 through 4K, and the layout stress test with 10+ active rules, is Phase 9 visual QA; no viewport exists to measure yet.
- **Real OS double-click timing** — the 300 ms window is measured on the game's injected clock, and tests drive exact instants. Whether a real mouse's press/release events arrive with that granularity is a Phase 9/10 integration question.
- **Manual card activation inside the between-turns window** — the gesture side is ready (a click there is recognised and dropped), but hit-testing card slots and consuming a card is Phase 6.

---

## 8. What Phase 4 Picks Up

1. Author the 100-rule library as JSON (30/25/15/15/10/5) with visual metadata and `introDepth` gating.
2. Trigger evaluation via exact word match on screen text — the engine's trigger seam was laid in Phase 1.
3. Memory values and later-recall task wording.
4. Trap pool with `isFakeByOrigin`, validating against the Phase 1 engine (`IsMechanicallyVisibleFake`, source tracing).
5. A rule linter (category counts, duplicate text, resolvability, Identifier/Ignore copy disambiguation) wired into the existing validator CLI.
