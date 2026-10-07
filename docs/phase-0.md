# Phase 0 — Foundation: Completion Documentation

**Status:** ✅ COMPLETE
**Development history:** rebuilt as granular commits before publishing (see `git log`); foundation content spans the initial commits through "Document foundation phase completion"
**Date:** 2026-10-08
**Reference:** `IMPLEMENTATION_PLAN.md` §6 Phase 0

---

## 1. What Phase 0 Was Supposed To Deliver (per plan)

> **Goal:** Repo, solution, and test harness exist; nothing game-specific yet.
> - Godot 4.x .NET project + `core/` class library + `tests/` project wired into one solution
> - Git init, .gitignore, CI (build + test on push)
> - JSON loading utilities + schema validation scaffolding
> - `theme/` color constants file seeded with the GDD's 9 hex ranges (used from day one)
>
> **Deliverables:** empty-but-running project; `dotnet test` green in CI.
> **Acceptance:** solution builds; a smoke test resolves a trivial rule end-to-end through core.

All of it is delivered, with one deviation documented in §7 (repo location).

---

## 2. Repository Location & Layout

The project repo was created at:

```
C:\Users\NISHIL DAVE\Desktop\RuleBreaker\Project_RuleBreaker\   ← git repo root
```

(The desktop `RuleBreaker\` folder also holds the GDD, the implementation plan,
and the downloaded .NET installer script — none of which belong in the repo.)

```
Project_RuleBreaker/
├── RuleBreaker.sln                        # one solution, four projects
├── .gitignore                             # .NET + Godot + OS noise + secrets
├── .github/workflows/ci.yml               # CI: build + test + content validation
├── src/RuleBreaker.Core/                  # ★ the simulation core (pure C#, zero Godot refs)
│   ├── Model/RuleTypes.cs                 #   RuleCategory, CoinAction, RuleDefinition, Instruction
│   ├── Model/Directive.cs                 #   the "exactly one correct action" output type
│   ├── Engine/ModifierEngine.cs           #   Phase 0 resolver (v0) + ResolutionResult
│   ├── Content/JsonLoader.cs              #   strict JSON load, errors collected never swallowed
│   ├── Content/RuleSchemaValidator.cs     #   structural validation for rules
│   └── Theme/ThemeColors.cs               #   GDD 9-color functional system as data
├── tests/RuleBreaker.Core.Tests/          # xUnit test project (29 tests)
├── tools/RuleBreaker.Tools.LevelValidator/ # CLI: validates rule JSON via the core pipeline
├── game/project.godot                     # Godot 4 project placeholder (1080p, 16:9)
└── data/rules/                            # content seed
    ├── act_click.json                     #   "Click Coin" (Action)
    └── trp_click_now.json                 #   "CLICK NOW" (Trap, fake-by-origin)
```

---

## 3. Environment Setup (done during this phase)

The machine had **no .NET SDK** installed. It was installed **user-locally**
(no system-wide changes, no admin required):

| Step | Result |
|---|---|
| Downloaded `dotnet-sdk-win-x64.zip` (channel 8.0) via `aka.ms` link | 285 MB zip |
| Extracted to `C:\Users\<user>\.dotnet` | user-local install |
| `dotnet --version` | **8.0.425** |
| PATH persisted in `~/.bashrc` + `~/.bash_profile` | future sessions get `dotnet` automatically |

Godot 4.x itself is **not** required for Phase 0 (no engine code yet); the
editor only becomes necessary at Phase 9 (presentation layer).

---

## 4. What Was Built (by component)

### 4.1 Solution & projects
- `RuleBreaker.sln` containing:
  - `src/RuleBreaker.Core` — class library, `net8.0`, **references nothing but the BCL**. This enforces plan engineering rule 1 (core never references engine/Godot namespaces) at the solution level.
  - `tests/RuleBreaker.Core.Tests` — xUnit project referencing Core.
  - `tools/RuleBreaker.Tools.LevelValidator` — console app referencing Core.
  - `game/project.godot` — Godot project **placeholder only** (app name from GDD title, 1920×1080, `canvas_items` stretch, `keep` aspect per GDD display spec). It is intentionally not yet wired into the .NET solution; the Phase 9 presentation layer will add the csproj shim.

### 4.2 Core data model (`src/RuleBreaker.Core/Model/`)
- **`RuleCategory`** enum — exactly the six GDD categories: `Action, Modifier, Restriction, Trigger, Memory, Trap`.
- **`CoinAction`** enum — the mouse-first actions: `None, Click, DoubleClick, Hold, Swipe`. `None` is a first-class action (deliberate no-input), per implementation ruling R-1.
- **`RuleDefinition`** record — the data-facing definition of a base rule: `Id, Category, Text, BaseDirective?, IsFakeByOrigin, IntroDepth`. Plus `ToInstruction()` producing the runtime view.
- **`Instruction`** record — one displayed instruction; encodes GDD locked principle 9 (one instruction = one turn).
- **`Directive`** record — the single resolved action with an optional condition string; `Directive.NoInput` models every inversion result ("Do Not [action]") per locked principle 2.

### 4.3 ModifierEngine v0 (`src/RuleBreaker.Core/Engine/ModifierEngine.cs`)
- Resolves one standalone instruction → exactly one `ResolutionResult`.
- **Fake-origin instructions are classified fake and produce no directive** — the seed of the pipeline stage 2 (Identifier classification) and the GDD's "removed from pipeline" behavior.
- Directive-less non-Action categories throw with a message pointing at Phase 1 — the full pipeline (Ignore → Identifier → Inversion count → Conflict → one directive) is Phase 1 work and this file's XML doc lists the exact priority order from the GDD.
- `ResolutionResult` record carries `RuleId, IsFake, FinalDirective, InversionCount` — the shape presentation and tests will build against. The public contract is stable from Phase 0 onward.

### 4.4 Content pipeline (`src/RuleBreaker.Core/Content/`)
- **`JsonLoader`** — camelCase JSON ↔ PascalCase C#, case-insensitive, comments allowed, trailing commas allowed, **enum names as strings** (`JsonStringEnumConverter`), `WriteIndented` output. Errors are collected into human-readable lists; `Load<T>` throws `RuleContentException`, `TryLoad<T>` returns them. A broken content file must fail loudly — never silently.
- **`RuleSchemaValidator`** — structural checks enforced now:
  - ID convention: `UPPERCASE_SNAKE_CASE` with the category prefix as first segment (`ACT_`, `MOD_`, `RST_`, `TRG_`, `MEM_`, `TRP_`), minimum two segments. Example: `ACT_CLICK`, `MOD_LIE_NEXT`, `TRP_CLICK_NOW`.
  - Category must be a valid `RuleCategory`; text must be non-empty.
  - `IsFakeByOrigin` only legal for the Trap category (GDD: traps are the 5-rule fake pool).
  - Action/Restriction rules must carry a `BaseDirective`; `BaseDirective = None` is rejected (restrictions are modelled as inversions inside the engine, not as no-op directives).
  - Modifier/Trigger/Memory rules may be directive-less **in Phase 0 only** — Phase 1 wires their directive semantics.
  - `IntroDepth` must be 1–5 (the progression tiers Learning → Mastery; Identifiers are depth 5 / Level 76+).

### 4.5 Functional color system (`src/RuleBreaker.Core/Theme/ThemeColors.cs`)
- All **nine** GDD functional colors with their exact hex ranges copied verbatim from the GDD table (NearBlack `#0A0A0A–#111111` … Purple `#6600CC–#9944FF`), each with its gameplay meaning.
- This is the plan's "colors exist in exactly one place" rule made concrete: the Godot theme layer (Phase 9) reads from here; no other file may hardcode a gameplay color. Because it lives in the pure core, it is unit-test-able and already tested.

### 4.6 LevelValidator CLI (`tools/RuleBreaker.Tools.LevelValidator/`)
The seed of the Phase 8 authoring toolkit. Takes one or more rule JSON paths,
runs each through the exact pipeline the game will use
(JSON deserialize → schema validation → engine resolvability), prints
`[OK]/[FAIL]` per file with error details, and **exits 1 if any file is invalid**
(CI-usable). Verified both ways:

```
=== CLI: valid rule ===
[OK]   data/rules/act_click.json
[OK]   data/rules/trp_click_now.json
Checked 2 file(s), 0 error(s).
CLI-VALID-EXIT:0

=== CLI: broken rule (should FAIL, exit 1) ===
[FAIL] .../broken_rule.json
       - Invalid JSON: ... Path: $.category
CLI-BROKEN-EXIT:1
```

### 4.7 Sample content (`data/rules/`)
Two canonical rules proving the schema end-to-end:
- `act_click.json` — "Click Coin", Action, directive `Click`, depth 1.
- `trp_click_now.json` — "CLICK NOW", Trap, `isFakeByOrigin: true`, depth 5 (traps are Level 76+ content).

### 4.8 CI (`.github/workflows/ci.yml`)
Windows runner, .NET 8, three steps on every push to `main` and every PR:
1. `dotnet build`
2. `dotnet test`
3. LevelValidator over the sample content (so broken content fails CI from day one)

---

## 5. Test Suite — 28 tests, all green

`dotnet test` final result: **`Passed! - Failed: 0, Passed: 28, Skipped: 0, Total: 28`** (exit 0).

| File | Tests | What they guard |
|---|---|---|
| `SmokeTests.cs` | 1 | **The Phase 0 acceptance criterion:** JSON rule → `JsonLoader.Load` → `RuleSchemaValidator` (0 errors) → `ModifierEngine.Resolve` → `Click` directive, 0 inversions, not fake. |
| `ThemeColorsTests.cs` | 3 | Exactly 9 colors in GDD order; hex ranges verbatim (NearBlack/Gold/BrightRed/Blue/Amber/Purple spot-checked); case-insensitive lookup, unknown name throws. |
| `JsonLoaderTests.cs` | 4 | camelCase mapping; malformed JSON → descriptive `Invalid JSON` error; empty input → clear error; `Load("null")` → `RuleContentException`. |
| `ModifierEngineTests.cs` | 4 | Action passthrough; fake-origin → `IsFake` + no directive; directive-less category → informative `Phase 1` error; null instruction → `ArgumentNullException`. |
| `RuleSchemaValidatorTests.cs` | 16 | Valid rule clean; 6 invalid-ID cases rejected; empty text; missing Action directive; fake-origin outside Trap rejected / inside Trap allowed; `BaseDirective=None` rejected; 3 out-of-range `IntroDepth` cases; Modifier rule allowed directive-less. |

### Bugs found and fixed during the test run (why the tests earn their keep)
1. **`BaseDirective` had no default value** — record required it positionally. Fixed by defaulting to `null` (the schema genuinely allows directive-less rules for Modifier/Trigger/Memory).
2. **Enum JSON deserialization failed** (`"category": "Trigger"` could not map) — root cause: `System.Text.Json` needs `JsonStringEnumConverter`. Added to `JsonLoader.Options` once, benefits all content forever.
3. **ID convention inconsistency** — the validator originally enforced a dot-separated PascalCase convention (`ACT.CLICK`) that contradicted the plan, the tests, and the GDD examples (`ACT_CLICK`). Root cause: two conventions written independently. Fixed by making the validator enforce the real convention: `UPPERCASE_SNAKE_CASE` with category prefix — one convention everywhere, backed by 6 rejection tests.

Each fix addressed the cause; no assertions were weakened to pass.

---

## 6. Git

- Repo initialized on branch `main`.
- Template leftovers (`Class1.cs`, `UnitTest1.cs`) removed before committing.
- Single clean commit, working tree clean after it:

```
5c6d6a5 Phase 0: solution scaffold, simulation core seed, test harness, CI
```

---

## 7. Deviations From The Plan (and why)

| Deviation | Reason | Impact |
|---|---|---|
| Repo root is `Desktop/RuleBreaker/Project_RuleBreaker/` (new subfolder) rather than the GDD folder itself | The GDD folder contains the GDD + plan + downloaded installer script, which should not be tracked in the game repo | None — plan §3.2 structure maps 1:1 inside the repo (`src/RuleBreaker.Core`, `tests/`, `tools/`, `data/`, `game/`) |
| Godot project is a placeholder, not a compiled C# Godot project | Phase 0 needs no engine code; the Godot editor becomes relevant at Phase 9 | None — `game/project.godot` already fixes app name, 1080p, 16:9 stretch per GDD |
| `.NET` SDK installed user-locally instead of machine-wide | Avoids system-wide changes without explicit user request | `.bashrc`/`.bash_profile` PATH entries make `dotnet` available in future shells |

---

## 8. Acceptance Criteria — Verified

| Plan criterion | Status |
|---|---|
| Godot 4.x .NET project + core class library + tests wired into one solution | ✅ (Godot placeholder at `game/`; core + tests in one solution) |
| Git init, .gitignore, CI | ✅ (commit `5c6d6a5`; CI workflow committed) |
| JSON loading utilities + schema validation scaffolding | ✅ (`JsonLoader` + `RuleSchemaValidator`, both tested) |
| `theme/` color constants with the 9 GDD hex ranges used from day one | ✅ (`Core/Theme/ThemeColors.cs`, tested verbatim against the GDD) |
| `dotnet test` green | ✅ 29/29 passed, exit 0 |
| Solution builds | ✅ `dotnet build` 0 errors (exit 0) |
| Smoke test resolves a trivial rule end-to-end through core | ✅ JSON → validate → engine → `Click` directive |
| Nothing game-specific yet | ✅ No turn loop, no timers, no cards, no modes — only the data contract and a passthrough resolver |

**Checks that could not run here:** the GitHub Actions CI workflow itself
(requires the repo to be pushed to GitHub — pushing was not authorized/local-only so far);
the Godot project was not opened in the Godot editor (not needed this phase).

---

## 9. What Phase 1 Picks Up

The ModifierEngine pipeline, in strict GDD priority order (all inside the stable
contract created this phase):

1. **Ignore check** — absolute priority, scope = exactly next instruction, cascading-ignore semantics, voided-but-visible state (principles 5–6).
2. **Identifier classification** — metadata predicates, inverted identifiers ("Trust [category]" makes fakes mechanically real, principles 19–23), blue-trap exclusion (R-6).
3. **Inversion count** — Lie + Reverse together, odd/even, non-binary inversion (principles 1–2) with the full GDD test tables.
4. **Conflict resolution** — most recently activated rule wins (principle 7).
5. Retroactive Lie semantics (principle 3) + rule lifecycle/countdown model (principles 4, 11) + source tracking (principle 25).

Phase 1 is the L-sized heart of the game; the 29 tests from Phase 0 already pin the contract it must uphold.
