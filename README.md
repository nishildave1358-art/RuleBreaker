# RuleBreaker: The Coin Paradox

A psychological puzzle game built around one question: *what is the truth right now?*

A single gold coin on a dark table. A stream of floating instruction cards that
lie, reverse, ignore, and contradict each other. Every rule can expire, every
action can become wrong, and a response timer is always running. The challenge
is not reaction speed — it is resolving what the active rules actually mean
before the timer expires.

## Status

**Phase 3 (Input & coin interaction) complete** — mouse-first gameplay input now
resolves unambiguously into Click, DoubleClick, Hold, Swipe or deliberate NoInput,
wired into the turn loop, with coin feedback hooks, a keyboard that is
structurally UI-only, and the GDD's 16:9 / 1080p-minimum display spec pinned as
the gesture coordinate space. Phase 2 (turn system, timers, game loop), Phase 1
(Simulation Core: the Modifier Engine) and Phase 0 (foundation) are also complete.
201 tests pass. See `docs/phase-0.md`, `docs/phase-1.md`, `docs/phase-2.md` and
`docs/phase-3.md`.

## Tech Stack

- **Engine:** Godot 4.x (presentation layer, Phase 9+)
- **Language:** C# / .NET 8
- **Architecture:** pure deterministic simulation core (`RuleBreaker.Core`)
  with zero engine dependencies — all game logic is unit-testable headless
- **Content:** data-driven (JSON) for rules, cards, levels, and pacing tables

## Repository Layout

```
src/RuleBreaker.Core/          Simulation core: models, Modifier Engine, turn systems, content, theme
tests/RuleBreaker.Core.Tests/  xUnit suite for the core (GDD tables + 10k-run fuzz invariant)
tools/RuleBreaker.Tools.LevelValidator/  CLI that validates rule content
game/                          Godot 4 project (presentation layer placeholder)
data/rules/                    Rule definitions (JSON)
docs/                          Per-phase development documentation
```

## Getting Started

Requires the .NET 8 SDK.

```bash
# build
dotnet build

# run the test suite
dotnet test

# validate rule content files
dotnet run --project tools/RuleBreaker.Tools.LevelValidator -- data/rules/*.json
```

## Design

RuleBreaker launches with 100 unique base instructions across six rule
categories (Action, Modifier, Restriction, Trigger, Memory, Trap). The Modifier
Engine combines them — lie inversions, reversals, ignore effects, identifier
classifications, conflict resolution — into thousands of unique situations,
while always guaranteeing exactly one correct action per turn.

Five game modes (Classic, Endless, Memory, Chaos, Hardcore), four difficulty
tiers, a special-card loadout system, and online leaderboards separated by
difficulty are planned for version 1.
