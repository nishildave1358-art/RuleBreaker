# Phase 1 — Simulation Core: rule model + Modifier Engine: Completion Documentation

**Status:** ✅ COMPLETE
**Date:** 2026-10-16
**Reference:** `IMPLEMENTATION_PLAN.md` §6 Phase 1 · `RuleBreaker_GDD_Final (1).md` (principle tables, inversion tables, worked examples)

---

## 1. What Phase 1 Was Supposed To Deliver (per plan)

> **Goal:** The Modifier Engine resolves any combination of active rules to exactly one directive, per GDD.
> - `RuleDefinition`, `ActiveRule` (activation turn, expiry, state), `TurnContext`, `Directive`
> - Resolution pipeline, in strict GDD priority order:
>   1. **Ignore check** — absolute; scope = exactly the next instruction; cascading ignore voids the Ignore itself, not the one after; voided instructions stay visible (data flag for presentation)
>   2. **Identifier classification** — predicate match on visual metadata (R-5); inverted identifier flips trust; trusted fakes become mechanically real (principles 22–23); fakes removed from pipeline before inversion touches them (GDD Boss example)
>   3. **Inversion count** — Lie + Reverse combined; odd = inverted / even = original; **non-binary inversion**: everything resolves to "Do Not [action]" (principle 2)
>   4. **Conflict resolution** — contradictory active rules → most recently activated wins (principle 7)
>   5. Output exactly one directive; retroactive Lie: forward-only if target still active, zero effect otherwise (principle 3)
> - Rule lifecycle: turn-based countdown decrements once per displayed instruction (never seconds); expired rules permanently removed
> - Source tracking: post-action trace → Real vs FakeOrigin (principle 25)
>
> **Acceptance (tests, all from GDD tables):** inversion table (1/0, 0/1, 1/1, 2/1, 2/2); identifier inversion table (all 6 rows); cascading ignore; ignore voids every type incl. another Ignore; retroactive lie both branches; conflict = newest wins; word-trap exact match; full Turn-5 stacking example; Boss-Round-14 example; **fuzz invariant: never 0, never 2+ directives**.

All of it is delivered. §5/§6 record the modelling decisions and the defects fixed along the way.

---

## 2. Delivered Artifacts

```
src/RuleBreaker.Core/
├── Model/
│   ├── RuleTypes.cs            RuleCategory · CoinAction · ModifierSubType · InversionKind
│   │                           (+ RetroactiveLie) · IdentifierMode · VisualMetadata ·
│   │                           RuleDefinition · Instruction
│   ├── Directive.cs            the "exactly one correct action" output (Action + Condition, NoInput)
│   ├── ActiveRule.cs           RuleScope · ActiveRule (activation turn, expiry, target) ·
│   │                           RuleSetTickResult
│   └── RuleSetState.cs         ordered live set: Activate · TickExpiry · IsInstructionActive(±turn)
├── Engine/
│   ├── ModifierEngine.cs       the 5-step priority pipeline (the heart of the game)
│   ├── ResolutionContext.cs    live rule set + global turn counter for one resolution
│   ├── ResolutionResult.cs     one result per instruction: classification + the single directive
│   └── SourceTracker.cs        WordMatcher (exact whole-word match) + Source A/B trace
tests/RuleBreaker.Core.Tests/
├── Engine/ModifierEngineGddTableTests.cs   the GDD acceptance tables & worked examples (44 facts + 1 theory/5 cases)
└── Engine/SourceReaderProbe.cs             contract probes (reflection pinning of public API shapes)
```

The whole suite is **81 tests / 81 passing**, build is **0 warnings / 0 errors**, and the
content-validation CLI stays green (`docs/phase-0.md` §8).

---

## 3. Pipeline As Implemented

```
Resolve(instruction, context)
├── 0. Immunity frame (documented no-op): single-use MatchThenIgnore rules are cleared by the
│      turn system before their triggering Ignore card is displayed, so nothing to veto here.
├── 1. IGNORE — absolute priority. Any in-force Ignore voids this instruction, of any type
│      (including another Ignore → IgnoredByCascadeRule flag). Result carries a concrete
│      directive, the flag for the IGNORED visual, and the crediting rule id.
├── 2. IDENTIFIER CLASSIFICATION — predicate match on VisualMetadata; newest RELEVANT identifier
│      wins and its verdict is final (matching fakes leave the pipeline before inversion).
├── 3. INVERSION COUNT — Lie + Reverse combined into one parity; odd = inverted, even = original.
│      Retroactive Lie is counted only while its backward target is still standing.
├── 4. DIRECTIVE EVALUATION — Action / Restriction / Trigger / Memory / Modifier / Trap semantics,
│      non-binary inversion → "Do Not [action]" = NoInput.
└── 5. CONFLICT RESOLUTION — newest active directive-carrying rule wins (principle 7).
   Output: exactly one ResolutionResult carrying exactly one non-null FinalDirective.
```

Source tracing is post-action (principle 25) and lives in `SourceTracker`; the engine stamps
what that trace needs (was the instruction mechanically fake and still displayed).

---

## 4. Acceptance Criteria — Verified

| Plan acceptance item | Where it is verified | Status |
|---|---|---|
| Inversion table 1/0 (1 Lie on Action) | `Inversion_1_LieOnAction_NextInstruction_GivesNoInput` | ✅ |
| Inversion table 0/1 (0 modifier, Action) | `ModifierEngineTests.Resolve_StandaloneActionInstruction_ReturnsItsDirective`, `SmokeTests.JsonRule_Validates_AndResolves_ToClickDirective` | ✅ |
| Inversion table 1/1 (1 Lie on Restriction → base action) | `Inversion_1_LieOnRestriction_NextInstruction_RestoresBaseAction` | ✅ |
| Inversion table 2/1 (2 Lies on Action → original) | `Inversion_2_LiesOnAction_NextInstruction_RestoresAction` | ✅ |
| Inversion table 2/2 (2 on Restriction → NoInput) | `Inversion_2_OnRestriction_RestoresNoInput` | ✅ |
| Identifier inversion table row 1 (Ignore Flashing, even → remain fake) | `Identifier_DenyFlashing_NonInverted_FlashingIsFake`, `Identifier_EvenInversionCount_KeepsBasePosture` | ✅ |
| Identifier inversion table row 2 (Ignore Flashing + 1 Lie → Trust Flashing → real) | `Identifier_DenyFlashing_Inverted_TrustFlashing_Real` | ✅ |
| Identifier inversion table row 3 (Ignore Flashing + 2 → even → remain fake) | `Identifier_EvenInversionCount_KeepsBasePosture` | ✅ |
| Identifier inversion table row 4 (Only Trust Blue, even → blue = real) | `Identifier_TrustOnlyBlue_NonInverted_BlueCardReal`, `Identifier_EvenInversionCount_KeepsBasePosture` | ✅ |
| Identifier inversion table row 5 (Only Trust Blue + 1 Lie → Distrust Blue = fake) | `Identifier_TrustOnlyBlue_Inverted_DistrustBlue_Fake` | ✅ |
| Identifier inversion table row 6 (Only Trust Blue + 2 → even → blue = real) | `Identifier_EvenInversionCount_KeepsBasePosture` | ✅ |
| Identifier newest-relevant wins | `RuleSetState_Identifier_EvaluatesNewestRelevantAsExpected` | ✅ |
| Identifier predicates classify every instruction type (R-5) | `Identifier_DenyFlashing_NonInverted_FlashingIsFake`, `Identifier_DenyRedText_ClassifiesCardCarryingExtraVisualState`, `TrapFakeByOrigin_IdentifierTrustInvertsToReal_TrustedThenReal` | ✅ |
| Trusted fakes become mechanically real (principles 22–23) | `TrapFakeByOrigin_IdentifierTrustInvertsToReal_TrustedThenReal` | ✅ |
| Cascading ignore voids the Ignore itself, not the next instruction | `Ignore_VoidsAnIgnoreInstruction_CascadeFlagged`, `Ignore_VoidsAnotherIgnore_Cascading` | ✅ |
| Ignore voids every type (Action, Trap, Ignore) | `Ignore_VoidsAction_Instruction_AndHasConcreteDirective`, `Ignore_VoidsTrap_Instruction`, `Ignore_VoidsAnIgnoreInstruction_CascadeFlagged` | ✅ |
| Retroactive Lie — target still active → inverted going forward | `RetroactiveLie_TargetStillActive_InvertsGoingForward` | ✅ |
| Retroactive Lie — target already resolved → zero mechanical effect | `RetroactiveLie_TargetAlreadyResolved_ZeroMechanicalEffect`, `RetroactiveLie_UnknownTarget_ZeroMechanicalEffect` | ✅ |
| Conflict = newest activated wins | `Conflict_NewerActionOverridesOlderAction`, `Conflict_NewerRestrictionOverridesOlderAction`, `Conflict_NoContradiction_NoOverride` | ✅ |
| Word-trap exact match (RATE ≠ RATED / PIRATE / RATER) | `WordTrap_ExactMatch_RateIsNotSubstring` (5 cases), `SourceReaderProbe.WordMatcher_Matches_BoundarySemantic` | ✅ |
| Full Turn-5 stacking example (GDD "Example — Full Resolution (Turn 5)") | `Turn5Stacking_Example_OneLieInvertsTrigger_RestrictionAgrees_NoInput` | ✅ |
| Boss-Round-14 example (GDD "Boss Round Example (V1)") | `BossTurn14_IdentifierRemovesFake_BeforeInversion_LieHasNoEffect` | ✅ |
| Fuzz invariant: never 0, never 2+ directives | `Fuzz_TenThousandRandomRuleSets_AlwaysExactlyOneDirective` (10,000 seeded rule sets, with coverage guards so it cannot go vacuous) | ✅ |
| Rule lifecycle: countdown in turns, dead rules stay dead | `TurnsRule_ExpiredAtTurnBeyondDuration_IsNotActive`, `ExpiredRule_DeadStaysDead_AfterReactivationWindow`, `NextInstructionRule_AppliesToTargetTurnOnly_ThenExpires`, `Ignore_NextInstructionScope_ExpiredAtTurn3_NoLongerVoids`, `RuleSetState_IsInstructionActive_*` | ✅ |
| Source tracking (principle 25) | `SourceB_FakeVisibleFakeWrongAction_IsFakeOrigin`, `SourceA_RealWrongAction_IsReal`, `Source_CorrectAction_IsReal` | ✅ |

**Verification commands (run in `Project_RuleBreaker/`):**

```bash
dotnet build --nologo      # 0 warnings, 0 errors
dotnet test  --nologo      # 81 passed / 0 failed
dotnet run --project tools/RuleBreaker.Tools.LevelValidator -- data/rules/act_click.json data/rules/trp_click_now.json
```

---

## 5. Modelling Decisions (recorded, so Phase 2+ does not re-litigate them)

| Decision | Rationale |
|---|---|
| **`TurnContext` is `ResolutionContext`(RuleSetState, CurrentTurn) plus the Phase-2 loop** | The plan lists `TurnContext` as a Phase-1 model, but its content *is* the turn state machine (instruction shown → response window → evaluation → activation) which the plan assigns to Phase 2. Inventing a second overlapping context type in Phase 1 would have created two competing "current turn" owners. Phase 1 consumes an already-displayed instruction + the live rule set, exactly as the plan's step list describes. |
| **Identifier inversion is represented as an odd inversion recorded on the identifier rule itself** | The GDD's identifier table is about an identifier rule that was *targeted* by a Lie/Reverse ("Ignore Flashing + 1 Lie → Trust Flashing"). The engine has no notion of one active rule targeting another, so the odd parity is stamped on the identifier's own `InversionKind` when its card is activated; an even count is the base posture (no stamp). Mechanically only parity matters, so this is lossless. |
| **A new `InversionKind.RetroactiveLie` + `ActiveRule.RetroactiveTargetRuleId`** | \"Previous Instruction Was A Lie\" needs to ask *whether its target still stands* (`RuleSetState.IsInstructionActive(id, turn)`). Recording the target rule id is the smallest change that makes both GDD branches testable: in force → +1 parity; resolved/expired/never-existed → 0 parity (zero mechanical effect). |
| **`WasInvertedMechanically` = "the inversion machinery was engaged" (count > 0); `NetParityIsInverted` = count odd** | Consumers (presentation, fuzzing) need both facts. The directive only flips on odd parity — that is `NetParityIsInverted`. Reporting the flag for a fake result too is what makes the Boss-Turn-14 example self-describing (the Lie is in force; it just cannot resurrect a fake). |
| **Word matching is whole-token and case-insensitive** | Cards render mixed case ("Rate it now"), so case folding keeps the GDD word *valid*; tokenisation keeps RATED / RATER / PIRATE *invalid*. Case folding never crosses a word boundary. |
| **Identifier predicates constrain only the flags they name (true flags are required)** | A blue-bordered card that also flashes is still blue: \"Only Trust Blue\" must trust it, and \"Ignore Red Text\" must fake a red-text card that carries extra visual state. Exact-equality matching would have let cards escape classification whenever they carried an extra visual flag. |
| **Trigger directives apply only while their exact word is on screen** | Plan §6 Phase 1: the word-trap table is \"done here for trigger condition model\". A Trigger with no wired condition (`BaseDirective == null`) resolves to NoInput — never an exception. |

---

## 6. Defects Found & Fixed In This Pass

| # | Defect | Fix |
|---|---|---|
| 1 | **`RuleScope.NextInstruction` expired one turn too early** (`currentTurn > ActivatedAtTurn`) — "Next Instruction Is A Lie" activated on turn 1 was already dead before turn 2 could resolve it (21 test cases failing). | Bound is now `currentTurn > ActivatedAtTurn + 1`, with the GDD one-shot window documented on `HasExpired`. |
| 2 | **Older identifiers overrode newer ones.** The classification loop only returned on a "fake" verdict, so a newer \"Only Trust Blue\" could still be overridden by an older \"Ignore Flashing\". | The newest identifier whose predicate matches the card now returns its verdict unconditionally; only non-matching identifiers are skipped. |
| 3 | **Identifier inversion keyed on active parity instead of the rule's own parity**, and the identifier tests modelled the Lie as a separate rule (the GDD models it as the identifier rule being targeted). | Identifier trust flip is driven by the identifier rule's own odd-parity stamp; two tests re-modelled to the GDD shape and a dedicated even-parity test added for table rows 3 and 6. |
| 4 | **Fake results dropped inversion diagnostics** (`InversionCount` hardcoded to 0), hiding the Boss-14 "Lie counted but cannot resurrect a fake" behaviour. | `ResolutionResult.Fake(...)` now carries `inversionCount` / `wasInvertedMechanically`. |
| 5 | **`MatchesVisual` used exact record equality**, so a card carrying an extra visual flag escaped identifier classification. | Predicate match now requires the predicate's `true` flags and ignores the rest. |
| 6 | **Retroactive Lie was unimplemented** (audit items v1–v5). | `InversionKind.RetroactiveLie` + `ActiveRule.ForRetroactiveLie(... target rule id)` + turn-aware `RuleSetState.IsInstructionActive(id, turn)`; both GDD branches tested. |
| 7 | **Fuzz invariant was a static 25-case loop**, not the required 10,000-run property test. | `Fuzz_TenThousandRandomRuleSets_AlwaysExactlyOneDirective` — 10,000 seeded rule sets × random instruction, asserting one non-null directive, exclusive classifications and self-consistent inversion diagnostics, with coverage guards. |
| 8 | A pre-existing nullable warning in the `ResolutionResult.Real` factory (`CS8604`). | `trustedThenRealByRuleId` is now nullable; build is warning-free. |

Test hygiene done at the same time: the duplicated
`Identifier_WinNewestRelevant_OverridesOlder_DirectiveBoolFix` test was removed (it duplicated
`RuleSetState_Identifier_EvaluatesNewestRelevantAsExpected`), and the two assertions that
contradicted the GDD model were rewritten with the reason inline
(`RuleSetState_IsInstructionActive_..._Expired` never ticked; the one-shot lie test asserted
expiry on the lie's *target* turn).

---

## 7. Checks That Could Not Run Here

- **GitHub Actions CI itself** — the workflow is committed but the repo has not been pushed from
  this machine, so the equivalent commands were run locally instead (§4).
- **Godot editor** — not needed until Phase 9 (no engine code exists yet).
- **Content-library scale** — only the two seed rule files exist, so the 100-rule linter and the
  category-count checks are Phase 4 work.

---

## 8. What Phase 2 Picks Up

1. `GameSession` state machine: InstructionShown → ResponseWindow → Evaluation → AutoCardCheck → ActivationWindow → Next.
2. Global turn counter (starts at 1) driving `RuleSetState.TickExpiry(turn)` once per displayed instruction, and the activation window that feeds `RuleSetState.Activate(...)`.
3. Response timer model (`Level Base × Difficulty Multiplier`, 1.5/1.0/0.7/0.5; streaming compression; thresholds; never zero) and the R-1 expiry evaluation rules.
4. The `TurnContext`/`Turn` state that the plan lists for Phase 1 is supplied by this loop on top of `ResolutionContext`.
