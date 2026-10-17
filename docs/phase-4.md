# Phase 4 — Rule library content: 100 rules · Completion Documentation

**Status:** ✅ COMPLETE (copy authored; awaiting the user's copy review per the plan)
**Date:** 2026-10-17
**Reference:** `IMPLEMENTATION_PLAN.md` §6 Phase 4 · `RuleBreaker_GDD_Final (1).md` (Rule Library, Core Rule Types, Memory System, Word Traps, principles 67–70)

---

## 1. What Phase 4 Was Supposed To Deliver (per plan)

> **Goal:** All 100 base rules authored as data, validated, and copy-approved.
> - Author 30 Action / 25 Modifier (incl. Identifier sub-type) / 15 Restriction / 15 Trigger / 10 Memory / 5 Trap definitions (JSON)
> - Trigger evaluation: **exact word match** on screen text (principle: RATE ≠ RATED)
> - Memory system: store value (number/color/symbol/word) → later tasks ("Click If Number Was Odd", "Click If Color Was Blue", "Click If Symbol Was Not Star")
> - Trap pool: CLICK NOW / FREE BONUS / TAP FAST + 2 more; `isFakeByOrigin` flag
> - Visual metadata assignment across real + trap cards per R-6
> - `introDepth` gating per progression tier (Identifiers = depth 5 / Level 76+ — principle 32)
> - Rule linter: category counts exactly 30/25/15/15/10/5; every rule resolvable; no duplicate text; Identifier copy disambiguation vs Ignore sub-type (R-7)
>
> **Deliverables:** `data/rules/*.json` (100 rules) + linter green. Agent authors; user reviews copy.
> **Acceptance:** principles 67–70; each rule exercised by at least one engine test.

All of it is delivered. The one item Phase 4 cannot close on its own is the user's copy review — the content is authored, linted and engine-tested, but approval is a human step (§7).

---

## 2. Delivered Artifacts

```
src/RuleBreaker.Core/
├── Model/
│   ├── RuleTypes.cs        RuleDefinition/Instruction gain TriggerWord, MemoryStoreKey,
│   │                       MemoryTask, IgnoreCategory and DefaultScope; MemoryTask enum
│   └── MemoryBank.cs       short-term memory (store/recall/apply/clone) + MemoryWrite
├── Engine/
│   ├── MemoryEvaluator.cs  the six recall tasks + the keys they read (fails closed)
│   ├── ResolutionContext.cs screen text · memory bank · displayed value
│   ├── ResolutionResult.cs  + MemoryWrite (pure resolution, explicit side effect)
│   └── ModifierEngine.cs    trigger/ memory/ category-scoped ignore wiring
├── Systems/GameSession.cs  per-turn screen text/value + memory write application
└── Content/
    ├── RuleLibraryLoader.cs  one-rule and array files; schema validation per rule
    ├── RuleLibraryLinter.cs  pool-wide lint + engine resolvability for all 100
    └── RuleSchemaValidator.cs trigger/memory/ignore param consistency

data/rules/
├── actions.json (29) + act_click.json (1 seed)      = 30 Action
├── modifiers.json (25)                               = 25 Modifier (9 Identifier)
├── restrictions.json (15)                            = 15 Restriction
├── triggers.json (15)                                = 15 Trigger
├── memory.json (10)                                  = 10 Memory
└── traps.json (4) + trp_click_now.json (1 seed)      = 5 Trap

tests/RuleBreaker.Core.Tests/
├── Content/RuleLibraryTests.cs   distribution, linter green, every rule through the engine,
│                                 loader formats, one negative test per linter check
├── Engine/Phase4ContentRulesTests.cs  trigger exact-match (incl. self-text), scoped ignore,
│                                 memory store→recall, session integration
└── TestPaths.cs                  locates data/rules from the test binaries
```

Suite: **235 tests / 235 passing** (201 from Phases 0–3, 34 new here). Build: 0 warnings, 0 errors.
Library CLI: **8 files, 100 rules, 0 errors, 0 lint errors.**

---

## 3. What the Content Systems Now Do

### Trigger Rules — exact word on screen

A Trigger rule fires only when its `triggerWord` appears as a whole token in
`ResolutionContext.ScreenText`, the turn's *ambient* on-screen text. The card's own
wording is deliberately excluded: otherwise "Click When You See RATE" would always
satisfy itself. Matching is case-insensitive but boundary-exact — RATE matches
"Rate it now", never RATED / RATER / PIRATE (GDD Word Trap table).

An active Trigger rule also joins conflict resolution only while its word is on
screen, so a trigger cannot silently outrank a newer instruction on a turn where
its condition is absent.

### Memory — store, then recall

* Four **store** rules record the value displayed on their turn
  (`number`, `color`, `symbol`, `word`) through `MemoryWrite` on the resolution
  result. `GameSession` applies the write during evaluation; resolution itself stays
  pure.
* Six **recall** rules ask a task about the stored value: odd / even number, blue
  color, star / not-star symbol, word RATE. A task with nothing stored is **false**,
  so a level that omits the store rule fails closed instead of rewarding a guess.
* Fake or ignored store cards never write (the engine returns no `MemoryWrite`).
* `MemoryBank` keys are case-insensitive; values are kept verbatim; `Clone()`
  gives an independent snapshot for replays.

### Category-scoped Ignore

`Ignore Action Rules For 2 Turns` carries `ignoreCategory: Action` and voids only
Action cards; an unscoped `Ignore Next Instruction` still voids every type,
including another Ignore (GDD cascading rule unchanged).

### Library shape

`RuleLibraryLoader` accepts both the original single-rule files and the new
category arrays, so the two Phase 0 seeds remain part of the 100 instead of being
rewritten. `RuleLibraryLinter` enforces:

| Check | Rule |
|---|---|
| Distribution | exactly 30/25/15/15/10/5 and 100 total (principles 67, 69, 70) |
| Identity | unique ids, unique copy, category-correct `ACT_/MOD_/RST_/TRG_/MEM_/TRP_` prefix |
| Depth | Actions 1–2, Restrictions 1–3, Triggers/Modifiers 2–4, Identifiers 5, Memory 4, Traps 5 |
| Modifier data | sub-type declared; Inversion has its kind; Identifier has mode + visual predicate |
| Copy (R-7) | Identifier copy names the instruction surface (Instruction/Card/Text); Ignore and Identifier copy never collide |
| Traps | fake by origin; metadata is orthogonal to truth (R-6), never a tell |
| Memory | every recall task has a store rule for its key |
| Metadata (R-5/R-6) | each visual flag appears on at least one real card |
| Resolvability | every rule resolves through the real engine to a non-null directive |

---

## 4. Acceptance Criteria — Verified

| Plan / GDD requirement | Test |
|---|---|
| Principle 67: 100 unique base instructions | `Library_HasTheGddDistribution_AndNoLoadOrSchemaErrors` |
| Principle 69: Action 30 / Modifier 25 / Restriction 15 / Trigger 15 / Memory 10 / Trap 5 | same test + `Linter_RejectsAWrongCategoryCount` |
| Principle 70: Identifiers counted inside Modifier (9 of the 25) | same test |
| Every rule resolvable through the engine | `EveryRule_ResolvesThroughTheEngine_WithItsCategorySemantics`, linter `CheckResolvable` |
| Trigger exact match (RATE ≠ RATED / RATER / PIRATE) | `Trigger_FiresOnlyForTheExactWordOnScreen` (6 cases) |
| Trigger never self-satisfies from its own card | `Trigger_IgnoresItsOwnCardWording`, `TriggerCondition_DoesNotSelfSatisfyFromItsOwnCardText` |
| Active Trigger joins conflicts only while its word is on screen | `ActiveTriggerRule_JoinsConflictResolution_OnlyWhileItsWordIsOnScreen` |
| Memory store writes the displayed value | `MemoryStore_RecordsTheDisplayedValue_AndDoesNothingElse` |
| Memory recall: odd / even / blue / star / not-star / RATE | `MemoryRecall_ActsOnlyWhenTheStoredValueSatisfiesTheTask`, `MemorySymbolNotStar_IsARealNegation`, `EveryRule_Resolves…` |
| Missing memory fails closed | `MemoryRecall_FailsClosedWhenNothingWasStored` |
| Fake/ignored store never writes | `AnIgnoredMemoryStore_DoesNotWrite` |
| Store → recall end to end through the turn loop | `Session_StoresAndRecallsThroughTheTurnLoop`, `Apply_CommitsAWholeStoreThenRecallCycle` |
| Category-scoped Ignore voids only its category | `CategoryScopedIgnore_VoidsItsCategory_AndLeavesOtherCategoriesAlone`, `…ExpiresWithItsTurns` |
| Library lint: duplicate copy | `Linter_RejectsDuplicateCopy` |
| Library lint: Identifier depth 5 (principle 32) | `Linter_RejectsAnIdentifierOutsideDepth5` |
| Library lint: R-7 copy disambiguation | `Linter_RejectsIdentifierCopySharedWithAnIgnoreRule` |
| Library lint: memory store coverage | `Linter_RejectsARecallTaskWithNoStoreRule` |
| R-6: metadata is orthogonal to truth, and Trust makes a trap real | `TrapMetadata_IsOrthogonalToTruth_AndIdentifierTrustCanMakeItReal` |
| Library lint: a trap must be fake by origin | `Linter_AllowsTrapMetadata_ButRejectsAFakeTrap` |
| Library lint: unique trigger words | `Linter_RejectsADuplicateTriggerWord` |
| Loader handles single objects and arrays | `Loader_AcceptsSingleObjectsAndArrays` |

**Verification commands (run in `Project_RuleBreaker/`):**

```bash
dotnet build --nologo      # 0 warnings, 0 errors
dotnet test  --nologo      # 235 passed / 0 failed
dotnet run --project tools/RuleBreaker.Tools.LevelValidator -- --library data/rules/*.json
                           # 8 files, 100 rules, 0 errors, 0 lint errors
```

---

## 5. Decisions & Pinned Numbers

| Decision | Why |
|---|---|
| **Trigger conditions read ambient screen text, never the card's own text** | A card reading "Click When You See RATE" contains RATE; matching itself would make every trigger permanently fire and destroy the word trap. `ScreenText` is therefore "other visible text this turn". |
| **Memory resolution is pure; the write is data** | The engine returns `MemoryWrite` instead of mutating a bank, keeping resolution reproducible and fuzzable. `GameSession` applies it once during evaluation; fake/ignored cards produce no write. |
| **A missing memory value makes every task false** | Failing closed keeps an authored level from accidentally rewarding a guess when its store rule was skipped or ignored. |
| **Trigger/Memory join conflict resolution only while satisfied** | An active trigger with its word absent must not outrank a newer instruction; the same applies to a recall task whose stored value does not satisfy it. |
| **`ignoreCategory` is optional** | GDD has both unscoped ignores ("void the next instruction") and category-scoped ones ("Ignore Action Rules For 2 Turns"); one field models both without special-casing copy. |
| **`defaultScope` + `selfDurationTurns` are authored data** | The rule text already says "For 3 Turns" / "Next 2 Instructions"; recording the scope in the library lets Phase 8 activate rules correctly instead of parsing copy. |
| **The two Phase 0 seed files stay in the library** | They are the existing `ACT_CLICK` / `TRP_CLICK_NOW` entries; the loader accepts both shapes, so the seeds count toward the 100 and no historical file is rewritten. |
| **Category files hold arrays** | 100 individual files are hard to review and easy to skew; six category arrays make the distribution and copy visible in one diff while the loader still accepts single-rule files. |
| **Trap metadata is allowed (R-6), but never a tell** | R-6 makes visual metadata orthogonal to truth: real and trap cards may carry the same flags, so metadata alone never reveals a fake. Only trap cards with *unique* styling would leak, and the library shares every flag with real cards. `TRP_TAP_FAST` carries `flashing` to exercise the Trust/Deny identifier interaction on a trap; traps without metadata stay visually identical to standard white cards. |

---

## 6. Defects Found & Fixed

| # | Defect | Fix |
|---|---|---|
| 1 | **Trigger conditions matched the card's own wording** (Phase 1 seam): `ModifierEngine` tested `Directive.Condition` against `instruction.Text`. Every trigger card contains its own trigger word, so "Click When You See RATE" would have fired on every appearance, making the GDD word-trap table meaningless in real play. | Added `TriggerWord` + `ResolutionContext.ScreenText` and matched against ambient text only. The Phase 1 trigger tests were updated to the real contract (they had been written against the seam) and a self-satisfaction regression test added. |
| 2 | **Memory rules were dead content**: every Memory category resolved to `NoInput` unconditionally, so 10 of the 100 authored rules could never do anything. | Wired store/recall, added `MemoryBank`, `MemoryEvaluator`, `MemoryWrite`, and session application. |
| 3 | **Linter depth rule rejected its own Identifier rules** (found by the first full-library run): the Modifier range 2–4 applied to sub-type Identifier, which principle 32 fixes at depth 5. | Identifier modifiers get their own (5, 5) range; caught by `Library_PassesThePoolLinter`, the failing test was kept and the linter corrected rather than the data. |
| 4 | **A trap made real by Trust (TrustedThenReal) still resolved to `NoInput`.** Directive evaluation re-checked `IsFakeByOrigin` after the identifier step had already classified the card real, so principles 22–23 were mechanically dead for every trap — the bait action could never apply even when the player was told to trust it. Surfaced by giving a trap metadata and testing the GDD identifier-inversion row. | `EvaluateDirective` receives the trusted flag and resolves the trap's authored bait directive; a trusted trap with no authored action still yields `NoInput` (it has nothing to follow). Pinned by `TrapMetadata_IsOrthogonalToTruth_AndIdentifierTrustCanMakeItReal` and the existing `TrapFakeByOrigin_IdentifierTrustInvertsToReal_TrustedThenReal`. |

---

## 7. Checks That Could Not Run Here

- **User copy review** — the plan explicitly says "Agent authors; user reviews copy." All 100 texts are authored and linted for uniqueness/disambiguation, but approval is a human decision.
- **Level placement rules (R-6 blue-trap exclusion)** — "never place a trap with `borderBlue` while Trust Blue is active" is a *level* constraint (Phase 8 validator), not a library constraint; the library forbids permanent tells on traps but cannot know which rules a level activates together.
- **Procedural generation constraints** — weighting, 76+ Identifier gating and solvability simulation are Phase 7; the library provides the pool and the depth metadata those checks consume.
- **Godot presentation** — category card styling (white / purple / red accent / amber keyword / memory amber) and the recalled-value display are Phase 9.
- **Trigger words from real rendering** — the engine consumes `ScreenText` as data; which pixels produce that text is a presentation integration question.

---

## 8. What Phase 5 Picks Up

1. Penalty matrix by source and difficulty, using `TurnOutcome` + `SourceTrace` (already reported per turn).
2. Auto-card check priority (R-8) inside the existing `AutoCardCheck` phase.
3. Combo, lives, score and the HUD snapshot, fed by the correct/failure outcome.
4. Full penalty-matrix tests, including Source B on Easy/Medium/Hard/Nightmare and timer-expiry parity.
