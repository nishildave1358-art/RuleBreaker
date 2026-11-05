# Phase 6 — Special Card system · Completion Documentation

**Status:** ✅ COMPLETE
**Date:** 2026-11-05
**Reference:** `IMPLEMENTATION_PLAN.md` §6 Phase 6 · `RuleBreaker_GDD_Final (1).md` (Special Card System, Card Acquisition Methods, Card Persistence, Pre-Session Deck Selection, Hybrid Activation System, principles 8, 17, 35–43, 46–48; plan rulings R-8, R-10, pins 6)

---

## 1. What Phase 6 Was Supposed To Deliver (per plan)

> **Goal:** All 8 cards, acquisition, persistence, loadout, hybrid activation.
> - Effect handlers: Joker (auto, action-mistakes only), Shield (auto, any failure), Freeze (3 turns no new rules), Chaos (random rule in), Mirror (previous rule again — R-10), Echo (resurrect chosen dead rule — only resurrection path, principle 8), Time Warp (rate up / timer −40% × 5 turns, multiplier still applies — principle 17), Rule Bomb (remove 1 random active rule, can hit Identifier rules — principle 47)
> - Acquisition: combo milestones (10/20/30+), level completion choice-of-2–3, boss guaranteed card, random drops (low rate, tunable), shop (currency earned only — principle 36)
> - Persistence: collection counts between sessions; **consumed on activation** (38); unused loadout cards returned (39); max 3 slots (39)
> - Manual activation strictly inside the Between-Turns Window (principle 43); slots glow/dim
>
> **Acceptance:** principles 35–43, 47–48 as tests; save/load round-trip tests; all 8 effect tests.

All of it is delivered. The presentation half — slot glow/dim, the eight activation FX,
"card consumed" slot dimming — is Phase 9; the core exposes the exact state it draws
from (§3, §7).

---

## 2. Delivered Artifacts

```
data/cards/
└── cards.json          the eight cards as data (ids, activation, effect, rarity,
                        copy, visual cue, Freeze/Time Warp parameters)

src/RuleBreaker.Core/Model/
└── CardTypes.cs        CardActivation · CardEffectKind · CardRarity · CardDefinition
                        · CardReward · CardRewardSource

src/RuleBreaker.Core/Content/
├── CardSchemaValidator.cs   per-card schema; pins Freeze 3 turns and Time Warp
│                            5 turns at ×0.6, and rejects parameters an effect
│                            cannot use
├── CardLibraryLoader.cs     single-object or array files, errors collected
└── CardLibraryLinter.cs     pool lint: 8 cards, one per effect, 2 Auto / 6 Manual,
                            unique id/name, Joker never rarer than Shield

src/RuleBreaker.Core/Systems/
├── CardCollection.cs   CardLoadout (max 3 slots, duplicates allowed) ·
│                       CardCollection (copies, consumption, loadout validation,
│                       unused return, earned currency) · CurrencyRules (pins)
├── CardEffects.cs      CardEffectEngine — the eight handlers, with the effect
│                       rulings recorded on the class
├── CardRuntime.cs      CardSession — Begin/ResolveAutoCardCheck/PlayManual/
│                       EndSession; the principle-43 gate and the R-8 resolution
├── CardAcquisition.cs  the four channels + shop, seeded and deterministic
└── PlayerProfile.cs    the persisted model (collection, currency, last loadout,
                        per-tier best scores, settings) + JSON round trip

src/RuleBreaker.Core/Systems/GameSession.cs        (+ card hooks)
src/RuleBreaker.Core/Model/RuleSetState.cs         (+ RemoveAt for Rule Bomb)
tools/RuleBreaker.Tools.LevelValidator/Program.cs  (+ --cards mode)
.github/workflows/ci.yml                           (+ card-library lint step)

tests/RuleBreaker.Core.Tests/
├── Content/CardLibraryTests.cs             13 tests (content + schema + linter + loader)
└── Systems/
    ├── CardCollectionTests.cs              10 tests (slots, copies, currency)
    ├── CardEffectTests.cs                  22 tests (all 8 cards + activation rules)
    ├── CardAcquisitionTests.cs             14 tests (channels, shop, granting)
    └── PlayerProfileTests.cs                5 tests (round trip, unknown ids)
```

Suite: **383 tests / 383 passing** (319 from Phases 0–5, 64 new here). Build: 0 warnings, 0 errors.
Library CLI: **8 rule files, 100 rules, 0 lint errors** · **1 card file, 8 cards, 0 lint errors.**

---

## 3. The Card Flow As Implemented

```
CardSession.Begin(session, collection, [ids], effects)
  BuildLoadout  → max 3, every card owned, never more copies than owned
  take copies   → the loadout holds them for the session (not spent yet)

loop step 15  CardSession.ResolveAutoCardCheck()
  AutoCardRules.FireOrder(outcome.Kind)   R-8: Joker → Shield on a wrong action,
  │                                            Shield alone on expiry
  ├── no matching card in loadout → Session.ResolveAutoCardCheck(null) → penalty paid
  └── card found → consume one loadout slot
                 → Session.ResolveAutoCardCheck(cardId) → penalty None
                   (TurnSettlement.AbsorbedRedundantPenalty marks principle 46)

loop step 16  CardSession.PlayManual(cardId, targetRuleId?)
  phase must be ActivationWindow        principle 43
  card must be in the loadout and Manual
  effects.Apply(...)                    Freeze/Chaos/Mirror/Echo/TimeWarp/RuleBomb
  applied? → consume one slot           a failed activation costs nothing

EndSession()  unused slots return to the collection    principle 39
```

Effect by effect:

| Card | Implementation |
|---|---|
| Joker | Absorbs an `IncorrectAction` only (principle 41); never offered for `TimerExpiry` |
| Shield | Absorbs any failure, expiry included (principle 42) |
| Freeze | `GameSession.FreezeNewRules(3)`; `ActivateRule` throws while frozen, the turn counter keeps running |
| Chaos | random `Modifier` rule within `MaxIntroDepth`, activated with its own scope |
| Mirror | re-activates `ActivationHistory`'s newest rule, original scope, fresh duration (R-10) |
| Echo | restores a rule the player names, only from `ExpiredRules` (principles 4, 8) |
| Time Warp | `ApplyTimerPressure(5, 0.6)`; the window is multiplied after the difficulty multiplier, then floored (principle 17) |
| Rule Bomb | one uniformly random index out of the live set, identifiers included (principle 47) |

---

## 4. Acceptance Criteria — Verified

| Plan / GDD requirement | Test |
|---|---|
| All eight cards exist, one per effect | `Library_HasTheEightGddCards_AndPassesTheLinter` |
| 2 Auto / 6 Manual (principles 40–43) | same + `Linter_RejectsAWrongActivationSplit` |
| Freeze = 3 turns, Time Warp = 5 turns at −40% (R-10, pin 6) | `Library_PinsTheGddParameters`, `Schema_RejectsAFreezeThatDoesNotLastThreeTurns`, `Schema_RejectsAWarpThatIsNotFortyPercent` |
| Parameters an effect cannot use are rejected | `Schema_RejectsParametersAnEffectCannotUse` |
| Absorption stays on Auto cards; Auto cards are only absorbers | `Schema_KeepsAbsorptionOnAutoCardsOnly` |
| Principle 41: Joker never fires on expiry | `Joker_DoesNotFireOnTimerExpiry` |
| Principle 42: Shield covers expiry | `Shield_CoversTimerExpiry` |
| R-8: Joker first, exactly one card spent | `R8_JokerFiresBeforeShield_AndOnlyOneCardIsSpent`, `PeekAutoCard_ShowsWhatWouldFire_WithoutConsuming` |
| Auto card absorbs: no life, no combo reset, copy consumed (38) | `Joker_AbsorbsAnIncorrectAction_AndIsConsumed` |
| Principle 46: Joker on an Easy fake mistake is wasted | `Joker_OnAnEasyFakeMistake_IsAWastedActivation` |
| Freeze blocks new rules for 3 turns, counter continues | `Freeze_BlocksNewRulesForThreeTurns_WhileTurnsContinue` |
| Time Warp compresses 5 windows and expires | `TimeWarp_ShortensTheNextFiveWindows_AndThenExpires` |
| Principle 17: difficulty multiplier still applies | `TimeWarp_StillMultipliesByDifficulty` |
| Principle 47: Rule Bomb removes a random rule, identifiers included | `RuleBomb_RemovesOneRandomActiveRule_IncludingAnIdentifier` |
| A card whose effect cannot apply is not consumed | `RuleBomb_DoesNothingWhenTheRuleSetIsEmpty_AndIsNotConsumed`, `Mirror_DoesNothingWithoutAPreviousRule`, `Echo_RequiresThePlayerToChooseARule_AndIsNotConsumedWithoutOne` |
| R-10: Mirror repeats the previous rule with a fresh duration | `Mirror_ReactivatesThePreviousRule_WithAFreshDuration` |
| Principle 8: Echo is the only resurrection path, and only for expired rules | `Echo_RestoresAChosenExpiredRule`, `Echo_IsTheOnlyResurrectionPath_SoARuleBombedRuleIsNotOffered` |
| Chaos draws a depth-appropriate Modifier rule | `Chaos_ActivatesAModifierRule_WithinTheDepthGate` |
| Principle 43: manual activation only in the between-turns window | `ManualCards_CannotBePlayedDuringTheResponseWindow`, `AutoCards_CannotBeClickedInTheActivationWindow`, `CardsOutsideTheLoadout_CannotBePlayed` |
| Principle 39: max 3 slots, duplicates allowed, ownership enforced | `Loadout_HoldsAtMostThreeCards`, `Loadout_AllowsDuplicateCopies_AsSeparateSlots`, `BuildLoadout_RejectsAnUnownedCard`, `BuildLoadout_RejectsMoreCopiesThanOwned`, `BuildLoadout_RejectsAFourthSlot` |
| Principle 38: one copy consumed per activation | `Consumption_RemovesExactlyOneCopy`, `ActivatedCards_AreNotReturned_OnlyTheRemainingOnes` |
| Principle 39: unused cards returned | `UnusedLoadoutCards_AreReturned_NotConsumed`, `EndSession_ReturnsUnusedCards_AndClosesTheSession` |
| Principle 36: currency earned only, spent in the shop | `Currency_IsEarnedThroughGameplayAndSpent`, `Shop_SellsACardForEarnedCurrency`, `Shop_RefusesWhenTheBalanceIsShort_AndSpendsNothing`, `Prices_RiseWithRarity` |
| Principle 35 / 48: the four acquisition channels | `ComboMilestones_AtTenAndTwenty_GrantOneRandomCard`, `ThirtyPlusCombo_OffersARareChoice`, `LevelCompletion_OffersTwoOrThreeDistinctCards`, `BossCompletion_GuaranteesARareCard`, `RandomDrop_RespectsTheConfiguredRate` |
| Random drops are low-rate and tunable | `RandomDrop_IsRoughlyTheConfiguredRateOverManyTurns` (10 000 turns at 20% ± tolerance) |
| Principle 37: collection persists; save/load round trip | `Profile_RoundTrips_CollectionCurrencyLoadoutScoresAndSettings` |
| A save with retired card ids is reported, not silently dropped | `Profile_ReportsIdsTheLibraryNoLongerHas_InsteadOfDroppingThemSilently` |
| Broken content fails loudly | `Loader_ReportsABrokenFileInsteadOfThrowing`, `Profile_RejectsMalformedOrNegativeSaves` |

**Verification commands (run in `Project_RuleBreaker/`):**

```bash
dotnet build --nologo      # 0 warnings, 0 errors
dotnet test  --nologo      # 383 passed / 0 failed
dotnet run --project tools/RuleBreaker.Tools.LevelValidator -- --library data/rules/*.json
                           # 8 files, 100 rules, 0 errors, 0 lint errors
dotnet run --project tools/RuleBreaker.Tools.LevelValidator -- --cards data/cards/*.json
                           # 1 file, 8 cards, 2 auto / 6 manual, 0 lint errors
```

---

## 5. Decisions & Pinned Numbers

| Decision | Why |
|---|---|
| **The loadout takes copies out of the collection for the session** | Principle 39 says unused cards are *returned*, which only makes sense if they left the collection first. `CardSession.Begin` takes them, `EndSession` returns what is left; a used card is never returned, so consumption needs no separate refund path. |
| **A failed activation costs nothing** | Echo with no expired rule, Mirror with no previous rule, Rule Bomb with an empty set and an untargeted Echo are all player/UI errors or no-ops. Consuming a card for no effect would punish exactly the tactical thinking the GDD asks for, so `CardEffectResult.Applied` gates consumption. |
| **Echo resurrects only expired rules** | The GDD offers "expired rules" from the graveyard and principle 4 makes dead rules stay dead, with Echo the sole exception. A rule destroyed by a Rule Bomb did not *expire*, so it is deliberately not offered — otherwise the two cards would share a resurrection channel. |
| **Chaos draws from the Modifier pool** | The active rule set only holds rules that act on other instructions (inversion, ignore, identifier; principles 3, 6, 21). Activating an Action/Restriction/Trigger/Memory rule would add a member that never resolves. Retroactive lies are excluded because they need a target id a card cannot supply. |
| **Mirror/Echo restart the duration at the play turn** | R-10 says "fresh duration"; anchoring at `session.TurnNumber` means a 3-turn rule restored on turn 9 covers 9–11, not the turns it originally had. |
| **Freeze starts with the next instruction** | Freeze is played in the between-turns window, so "for 3 turns" means the *next* three instructions. Blocking activation in the same window would prevent a second card being played after it, which the GDD does not ask for. `FrozenTurnsRemaining` therefore reads 3 the moment the card is played and 1 on the last frozen turn. |
| **Overlapping Time Warps do not stack** | Two warps must not multiply to 0.36 of the window; the stronger factor wins and the window extends to the later expiry. It keeps the compression bounded and predictable (principle 14's floor still applies). |
| **Time Warp in streaming compresses the window instead of changing speed** | A speed tier change would also change the streaming schedule; compressing the window gives the GDD's "shorter timer" outcome without redefining a turn (principles 9, 15, 17). |
| **The profile stores ids and counts, not definitions** | Card definitions are content. A save that carried copies of the definitions would freeze old stats into player files and break Phase 14 balance changes. |
| **Unknown ids are reported on load** | A renamed or retired card id must be visible to the player and to us; silently dropping the copies would look like data loss with no trace. |
| **Card lint is part of CI** | The library rule lint already guarded the 100 rules; the card pool now has the same protection, so a missing effect or a drifted Freeze duration fails the build rather than shipping. |

---

## 6. Defects Found & Fixed

| # | Defect | Fix |
|---|---|---|
| 1 | **Mirror and Echo built the restored rule but never activated it.** `Restore(...)` returned a fresh `ActiveRule` and the caller reported success, so tests saw `Applied == true` with an unchanged rule set — a silent no-op that a looser assertion would have passed. | `Restore` now calls `session.ActivateRule(restored)`; `Mirror_ReactivatesThePreviousRule_WithAFreshDuration` and `Echo_RestoresAChosenExpiredRule` assert the rule is actually in the set. |
| 2 | **`FrozenTurnsRemaining` counted the turn the card was played in**, reporting 4 for a 3-turn Freeze. | Freeze now stores an explicit start turn (`playedAt + 1`) alongside its end turn; the remaining count reads the card's real duration at play time and counts down to 1 on the last frozen turn. `Freeze_BlocksNewRulesForThreeTurns_WhileTurnsContinue` covers turns 2–4 frozen, 5 free. |
| 3 | Test-side only: `PlayToActivationWindow` always displayed a *new* instruction, so tests that activated a rule first hit the phase guard (principles 18, 44). | Added `FinishTurnToActivationWindow` for turns whose instruction is already on screen. |
| 4 | Test-side only: the Easy-fake-Joker test sent no input, which against a fake's "no input" directive is a *correct* decision — no failure and therefore no Joker. | The test now clicks (the Source B mistake) and asserts `SourceTrace.FakeOrigin` before checking the wasted activation. |

No behaviour in Phases 0–5 changed: all 319 pre-existing tests still pass unmodified.

---

## 7. Checks That Could Not Run Here

- **Card presentation** — slot glow/dim, the eight activation FX, "MISTAKE CANCELLED" / "PROTECTED", the consumed-slot dimming, the collection and deck screens, the reward-choice UI. The core exposes `CardDefinition.VisualCue`, `CardSession.IsActivationWindowOpen`, `PeekAutoCard`, `CardEffectResult` and `CardLoadout.Cards`; Phase 9 draws them.
- **The in-game shop screen and drop notifications** — `CardAcquisition` and `CurrencyRules` supply the data and the rule (earned currency only), Phase 11 owns the screens.
- **Where saves live** — `PlayerProfile` is the model; Phase 11 decides the path and file lifecycle.
- **Depth-gated Chaos in a real level** — `CardEffectEngine.MaxIntroDepth` is the hook; Phase 7's modes and Phase 8's level scripts supply the value per level.
- **Balance** — drop rate 5%, prices 50/120, and the currency awards are named constants pinned as Phase 14 tuning candidates, like R-12's score defaults.
- **GitHub Actions CI** — the workflow includes the new card lint step, but this environment cannot read the run.
