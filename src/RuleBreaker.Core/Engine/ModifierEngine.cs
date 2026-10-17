namespace RuleBreaker.Core.Engine;

using RuleBreaker.Core.Model;

/// <summary>
/// The Modifier Engine — the heart of the game (GDD Modifier Engine section).
///
/// On each displayed instruction it collects every active modifier and resolves
/// exactly one correct directive per GDD priority order:
///
///   0. IMMUNITY FRAME (documented no-op):
///      Single-use MatchThenIgnore rules are cleared by the turn system before
///      the Ignore card that triggers them is displayed, so they are never in the
///      active set when the Ignore card is resolved. No veto needed here; kept as
///      a documented guard for completeness.
///
///   1. IGNORE CHECK — ABSOLUTE PRIORITY (GDD: "processed before the Modifier
///      Engine runs inversion resolution", "no exceptions", "each Ignore targets
///      exactly the next instruction"). An active Ignore voids the current
///      instruction, of ANY type, including another Ignore (cascading: the Ignore
///      itself is voided, not the one after it). Ignored cards stay visible
///      (strikethrough/IGNORED stamp — presentation concern, data flag here).
///
///   2. IDENTIFIER CLASSIFICATION (R-5: all instruction types; R-6: metadata
///      alone never reveals truth). Active Modifier-Identifier rules classify the
///      instruction real/fake by a predicate on VisualMetadata. Matching fakes are
///      removed from the pipeline BEFORE inversion touches them (Boss-Turn-14
///      example: Lie hits an already-removed fake → no mechanical effect). When
///      multiple active identifiers describe the same card, the newest RELEVANT
///      one wins and its verdict is final — "relevant" means its predicate matches
///      the card, and an older identifier never overrides a newer one even when
///      the newer verdict is "real" (mirror of the priority table's newest-wins
///      discipline).
///      Identifier rules themselves can be inverted: an identifier hit by an odd
///      number of inversions flips its trust semantics per the GDD identifier
///      inversion table (Deny + odd → "Trust [cat]" → those cards become real;
///      TrustOnly + odd → "Distrust [cat]" → the trusted category becomes fake).
///
///   3. INVERSION COUNT (GDD: Lie + Reverse combined into ONE parity count;
///      odd = inverted, even = original). Non-binary inversion (principle 2):
///      every inversion resolves to "Do Not [action]" = NoInput. A Restriction
///      ("Do Not Click") inverts back into its base action (the GDD restriction
///      row of the inversion table). Fakes already removed by step 2 stay fake —
///      inversion parity is reported but does not resurrect fakery.
///      Retroactive Lie ("Previous Instruction Was A Lie", principle 3) is counted
///      only while the rule it looks back at is still active in the rule set: the
///      inversion is forward-only then, and contributes nothing at all once the
///      target has already resolved or expired.
///
///   4. CONFLICT RESOLUTION (GDD principle 7: "most recently activated rule wins";
///      Turn-5 example: Rule 1 "Do Not Click" and Rule 3's inverted "Do Not Click"
///      agree → no conflict). Active directive-carrying rules (Action/Restriction,
///      possibly Trigger/Memory later) targeting the same coin decision are compared;
///      contradictory directives (different CoinAction) conflict; the newest active
///      rule wins. The state is ordered oldest→newest, so scanning backwards finds
///      the newest.
///
///   5. EXACTLY-ONE DIRECTIVE OUTPUT + SOURCE-TRACE FOOTPRINT.
///      After the pipeline there is exactly one Directive — the absolute correct
///      answer for this turn. The result also carries whether the instruction was
///      Fake / Ignored / TrustedThenReal so the presentation layer can render
///      visual treatment and so SourceTracker (post-action, principle 25) can
///      classify an incorrect action as Source A vs Source B.
///
/// Source tracking (principle 25) is post-action evaluation and lives in
/// SourceTracker, not in resolution itself. This engine stamps the bits needed
/// to perform it: was the instruction mechanically fake currently displayed?
/// (Source B detection).
/// </summary>
public sealed class ModifierEngine
{
    /// <summary>Phase 0 compatible: resolve one instruction against an empty rule set.</summary>
    public ResolutionResult Resolve(Instruction instruction)
        => Resolve(instruction, ResolutionContext.Empty);

    /// <summary>Resolve one displayed instruction against the live rule set at a given turn.</summary>
    public ResolutionResult Resolve(Instruction instruction, ResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        if (state is null)
            return EmptyRuleSetResolve(instruction, context);

        return FullPipelineResolve(instruction, state, context);
    }

    // ── EMPTY RULE SET PATH (Phase 0 shape) ─────────────────────────────────────
    private static ResolutionResult EmptyRuleSetResolve(
        Instruction instruction,
        ResolutionContext context)
    {
        if (instruction.IsFakeByOrigin)
        {
            return ResolutionResult.Fake(
                ruleId: instruction.RuleId,
                correctDirective: Directive.NoInput,
                visual: instruction.Visual ?? VisualMetadata.None,
                turn: context.CurrentTurn,
                classifiedByRuleId: string.Empty,
                activeSnapshot: ResolutionResult.EmptyRules);
        }

        var evalDir = EvaluateDirective(
            instruction.BaseDirective, inverted: false, cardBecameFake: false, instruction, context);

        if (instruction.Category == RuleCategory.Modifier)
            evalDir = Directive.NoInput;

        var result = ResolutionResult.Empty(
            ruleId: instruction.RuleId,
            finalDirective: evalDir,
            visual: instruction.Visual ?? VisualMetadata.None,
            turn: context.CurrentTurn);

        return WithMemoryWrite(result, instruction, context);
    }

    /// <summary>
    /// A Memory store rule records the value shown on its turn. Resolution stays
    /// pure: the write is reported on the result and the caller applies it to its
    /// <see cref="MemoryBank"/>. Fake or ignored cards never write. Recall rules
    /// and non-store rules produce no write.
    /// </summary>
    private static ResolutionResult WithMemoryWrite(
        ResolutionResult result,
        Instruction instruction,
        ResolutionContext context)
    {
        if (result.IsFake || result.IsIgnored) return result;
        if (instruction.Category != RuleCategory.Memory) return result;
        if (instruction.MemoryStoreKey is not { } key) return result;
        if (string.IsNullOrWhiteSpace(context.ScreenValue)) return result;

        return result with { MemoryWrite = new MemoryWrite(key, context.ScreenValue.Trim()) };
    }

    // ── TRIGGER CONDITION ───────────────────────────────────────────────────────
    /// <summary>
    /// GDD Word Trap: a trigger fires only when its exact word appears in this
    /// turn's ambient screen text. The trigger card's own wording is excluded by
    /// construction — <see cref="ResolutionContext.ScreenText"/> carries the other
    /// on-screen text — so "Click When You See RATE" cannot satisfy itself.
    /// </summary>
    private static bool TriggerMet(Instruction instruction, ResolutionContext context)
    {
        var word = instruction.TriggerWord ?? instruction.BaseDirective?.Condition;
        return word is not null && WordMatcher.Matches(word, context.ScreenText);
    }

    // ── 1. IGNORE (absolute; scope = exactly this instruction) ─────────────────
    private static ResolutionResult ResolveViaIgnore(
        Instruction instruction,
        RuleSetState state,
        int turn,
        string ignoreRuleId)
    {
        // Derive a concrete correct directive from the card itself so the stamp and
        // source systems have something concrete. An ignored card is mechanically
        // non-actionable for THIS card; the correct answer is the underlying verb
        // (what would have been correct absent the ignore) — the card is simply gone.
        // Presentation consumes IsIgnored to draw the IGNORED visual.
        var baseAction = instruction.BaseDirective?.Action ?? CoinAction.None;

        return ResolutionResult.Ignored(
            ruleId: instruction.RuleId,
            correctDirective: new Directive(baseAction),
            visual: instruction.Visual ?? VisualMetadata.None,
            turn: turn,
            ignoreByRuleId: ignoreRuleId,
            cascade: instruction.ModifierSubType == ModifierSubType.Ignore,
            activeSnapshot: state.OrderedRules);
    }

    // ── 2. IDENTIFIER CLASSIFICATION ───────────────────────────────────────────
    private static bool? TryClassifyAsFakeViaIdentifiers(
        Instruction instruction,
        RuleSetState state,
        int turn,
        out string? classifyingRuleId)
    {
        // 2. IDENTIFIER CLASSIFICATION (newest relevant wins if several apply).
        var identifiers = state.OrderedRules
            .Where(r => r.Instruction.ModifierSubType == ModifierSubType.Identifier
                        && !r.HasExpired(turn))
            .ToList();

        // Walk newest-first so the newest identifier whose predicate matches the
        // instruction is the one that decides (mirrors the priority table's
        // newest-wins discipline for identifiers; GDD identifier inversion table
        // rows are evaluated per-identifier in that order).
        //
        // "Relevant" = the rule's predicate describes this card. Once a relevant
        // identifier is found its verdict is FINAL — an older identifier never
        // overrides a newer one, not even when the newer verdict is "real".
        // Only non-matching (irrelevant) identifiers are skipped.
        for (var i = identifiers.Count - 1; i >= 0; i--)
        {
            var idRule = identifiers[i];
            var mode = idRule.Instruction.IdentifierMode
                ?? throw new InvalidOperationException(
                    $"Identifier rule {idRule.Instruction.RuleId} must declare IdentifierMode.");
            var pred = idRule.Instruction.Visual
                ?? throw new InvalidOperationException(
                    $"Identifier rule {idRule.Instruction.RuleId} must declare a Visual predicate.");

            if (!MatchesVisual(pred, instruction.Visual))
                continue; // this identifier does not describe the card; try the next older one.

            bool isTrustMode = mode == IdentifierMode.TrustOnly;

            // GDD identifier inversion table: an identifier rule hit by an ODD
            // number of inversions flips its trust semantics ("Ignore Flashing" +
            // 1 Lie → "Trust Flashing"). The odd parity is recorded on the rule
            // itself when the identifier card was activated (an even count leaves
            // the rule in its base posture, i.e. no inversion kind recorded).
            bool identifierInverted =
                idRule.Instruction.InversionKind is InversionKind.Lie or InversionKind.Reverse;

            //                              post-card-match verdict
            //   Deny,      even → fake   |  Deny,      odd  → real  ("Trust [cat]")
            //   TrustOnly, even → real   |  TrustOnly, odd  → fake  ("Distrust [cat]")
            bool ruleSaysFake = identifierInverted ? isTrustMode : !isTrustMode;

            classifyingRuleId = idRule.Instruction.RuleId;
            return ruleSaysFake;
        }

        classifyingRuleId = null;
        return null; // no active identifier describes this card.
    }

    /// <summary>
    /// Predicate match for identifier rules (GDD: "Only Trust Blue Instruction
    /// Cards → blue-bordered cards are real"; "Ignore Flashing Instructions →
    /// flashing cards are fake"). Every flag the predicate requires (true) must be
    /// present on the card; flags the predicate does not mention (false) are not
    /// constrained. A blue-and-flashing card is therefore still "blue" for a
    /// BorderBlue identifier — a card carrying extra visual state must not escape
    /// classification.
    /// </summary>
    private static bool MatchesVisual(VisualMetadata pred, VisualMetadata? card)
    {
        var m = card ?? VisualMetadata.None;
        return (!pred.Flashing || m.Flashing)
            && (!pred.RedText || m.RedText)
            && (!pred.Animated || m.Animated)
            && (!pred.BorderBlue || m.BorderBlue);
    }

    // ── 3. INVERSION COUNT ──────────────────────────────────────────────────────
    private static (int inversionCount, bool inverted) ComputeInversion(RuleSetState state, int turn)
    {
        var inversionCount = 0;

        foreach (var rule in state.OrderedRules)
        {
            if (rule.Instruction.ModifierSubType != ModifierSubType.Inversion) continue;
            if (rule.HasExpired(turn)) continue;

            // Retroactive Lie (principle 3): "Previous Instruction Was A Lie"
            // looks backward. It is forward-only while the rule it targets is
            // still active in the live rule set, and has ZERO mechanical effect
            // once that target has already resolved or expired. A retroactive lie
            // with no recorded target can never bite.
            if (rule.IsRetroactiveLie
                && (rule.RetroactiveTargetRuleId is null
                    || !state.IsInstructionActive(rule.RetroactiveTargetRuleId, turn)))
                continue;

            inversionCount++;
        }

        bool inverted = inversionCount % 2 == 1;

        // Fakery decided by identifiers is sticky — inversion does not resurrect a
        // fake (Boss example: Lie hits already-removed fake → no effect).
        // We still report the count for transparency/fuzz.

        return (inversionCount, inverted);
    }

    // ── 4. DIRECTIVE EVALUATION ─────────────────────────────────────────────────
    //
    // DESIGN (locked): Restriction rules carry BaseDirective = the FORBIDDEN action
    // (e.g. "Do Not Click" → BaseDirective = Click). The engine uses the rule's
    // Category to decide output:
    //   - Restriction, not inverted → NoInput (the restriction is in force;
    //     correct = do not do the forbidden action).
    //   - Restriction, inverted       → BaseDirective.Action (the forbidden action
    //     is restored; GDD: "Do Not Click" inverted → "Click Coin").
    //   - Action, not inverted        → BaseDirective.Action.
    //   - Action, inverted            → NoInput.
    //   - Modifier card (Lie/Reverse/Ignore/Identifier), real → NoInput
    //     (the card auto-activates; the player does nothing on it). This is a
    //     real instruction whose correct player action is "do nothing" because
    //     the modifier takes effect automatically; this is the engine's output
    //     for the modifier card itself (Phase 2 uses the classification+directive
    //     to decide whether to activate the modifier).
    //   - Trigger: if a wired trigger word is present and exact-matched in the
    //     instruction text AND met, use its directive (Phase 4 wires trigger words;
    //     for now, no wired word → NoInput, the safe default). Condition is on the
    //     DIRECTIVE (GDD "Click When You See RATE" → directive Click + condition
    //     "RATE"). The engine currently only handles wired trigger words; un-wired
    //     triggers resolve to NoInput (do nothing yet).
    //   - Memory: not yet wired (Phase 5+); resolve to NoInput (do nothing yet).
    //   - Trap, fake by origin → NoInput (fake; correct = do nothing on it) unless
    //     an identifier rule made it real (TrustedThenReal, handled upstream).
    private static Directive EvaluateDirective(
        Directive? baseDirective,
        bool inverted,
        bool cardBecameFake,
        Instruction instruction,
        ResolutionContext context,
        bool trustedThenReal = false)
    {
        if (cardBecameFake)
            return Directive.NoInput;

        var cat = instruction.Category;

        // Modifier cards auto-activate when real; player directive = NoInput.
        if (cat == RuleCategory.Modifier)
            return Directive.NoInput;

        // Restriction: BaseDirective = the forbidden action.
        if (cat == RuleCategory.Restriction)
        {
            var forbiddenAction = baseDirective?.Action ?? CoinAction.Click;
            return inverted ? new Directive(forbiddenAction) : Directive.NoInput;
        }

        // Action: BaseDirective = the action to perform.
        if (cat == RuleCategory.Action)
        {
            var action = baseDirective?.Action ?? CoinAction.Click;
            return inverted ? Directive.NoInput : new Directive(action);
        }

        // Trigger: directive active only when its exact word is on screen.
        if (cat == RuleCategory.Trigger)
        {
            var condition = instruction.TriggerWord ?? baseDirective?.Condition;
            if (condition is not null && TriggerMet(instruction, context))
            {
                var action = baseDirective?.Action ?? CoinAction.Click;
                return inverted ? Directive.NoInput : new Directive(action, condition);
            }
            return Directive.NoInput; // condition not met / not wired.
        }

        // Memory: a store rule is an observation turn (do nothing while the value
        // is recorded); a recall rule acts only when the stored value satisfies it.
        if (cat == RuleCategory.Memory)
        {
            if (instruction.MemoryStoreKey is not null)
                return Directive.NoInput;

            if (instruction.MemoryTask is { } task
                && MemoryEvaluator.IsSatisfied(task, context.Memory))
            {
                var action = baseDirective?.Action ?? CoinAction.Click;
                return inverted ? Directive.NoInput : new Directive(action, task.ToString());
            }

            return Directive.NoInput;
        }

        // Trap: fake by origin → do nothing. A trap an Identifier Rule made real
        // (TrustedThenReal, principle 22–23) is mechanically real this turn and
        // resolves its bait directive like any other real card.
        if (cat == RuleCategory.Trap)
        {
            if (instruction.IsFakeByOrigin && !trustedThenReal)
                return Directive.NoInput;

            // A trusted trap with no authored bait action has nothing to follow.
            if (baseDirective is null)
                return Directive.NoInput;

            return inverted ? Directive.NoInput : new Directive(baseDirective.Action);
        }

        // Unknown category: safe default.
        return Directive.NoInput;
    }

    // ── 5. CONFLICT RESOLUTION ──────────────────────────────────────────────────
    //
    // Active directive-carrying rules (Action/Restriction, later Trigger/Memory)
    // that apply to this turn are compared against the current evaluated directive.
    // Contradictory directives (different CoinAction) conflict; the newest active
    // rule wins (principle 7). State is oldest→newest, so scanning backwards finds
    // the newest first.
    //
    // Restriction rules' carried directive is the forbidden action; their EFFECT is
    // NoInput. Action rules' carried directive is the action to do.
    private static Directive ResolveConflicts(
        Directive currentDirective,
        RuleSetState state,
        int turn,
        ResolutionContext context)
    {
        var directiveRules = state.OrderedRules
            .Where(r => !r.HasExpired(turn) && CarriesDirectiveThisTurn(r.Instruction, context))
            .Reverse() // newest first
            .ToList();

        foreach (var rule in directiveRules)
        {
            var carried = rule.Instruction.BaseDirective!; // non-null due to filter
            var ruleEffect = rule.Instruction.Category == RuleCategory.Restriction
                ? Directive.NoInput
                : new Directive(carried.Action);

            if (DirectivesConflict(currentDirective, ruleEffect))
                return ruleEffect; // newest wins (first match scanning newest-first).
        }

        return currentDirective;
    }

    /// <summary>
    /// Does this active rule have an opinion about this turn? Action and
    /// Restriction rules always do; a Trigger rule only while its word is on
    /// screen; a Memory rule only when it is a recall task whose stored value
    /// satisfies it (store rules are observation turns, never directives).
    /// </summary>
    private static bool CarriesDirectiveThisTurn(Instruction rule, ResolutionContext context) => rule.Category switch
    {
        RuleCategory.Action or RuleCategory.Restriction => rule.BaseDirective is not null,
        RuleCategory.Trigger => rule.BaseDirective is not null && TriggerMet(rule, context),
        RuleCategory.Memory => rule.MemoryTask is { } task
                               && rule.BaseDirective is not null
                               && MemoryEvaluator.IsSatisfied(task, context.Memory),
        _ => false,
    };

    private static bool DirectivesConflict(Directive a, Directive b)
        => a.Action != b.Action;

    // ── FULL PIPELINE ───────────────────────────────────────────────────────────
    private static ResolutionResult FullPipelineResolve(
        Instruction instruction,
        RuleSetState state,
        ResolutionContext context)
    {
        var turn = context.CurrentTurn;
        var activeRules = state.OrderedRules
            .Where(r => !r.HasExpired(turn))
            .ToList();

        // 1. IGNORE (absolute; scope = exactly this instruction including another ignore).
        // Category-scoped ignores ("Ignore Action Rules For 2 Turns") void only
        // cards of that category; an unscoped ignore voids any type (GDD).
        var ignoreRules = activeRules
            .Where(r => r.Instruction.ModifierSubType == ModifierSubType.Ignore
                        && (r.Instruction.IgnoreCategory is not { } scope || scope == instruction.Category))
            .ToList();
        if (ignoreRules.Count > 0)
        {
            // Multiple simultaneous ignores all void this instruction; credit the
            // newest one for source/visual purposes.
            var ignoringRule = ignoreRules[^1];
            return ResolveViaIgnore(instruction, state, turn, ignoringRule.Instruction.RuleId);
        }

        // 3. INVERSION COUNT (computed before classification because the parity is
        //    a property of the live rule set for this turn).
        var (inversionCount, inverted) = ComputeInversion(state, turn);
        bool anyInversionInForce = inversionCount > 0;

        // 2. IDENTIFIER.
        bool? cardIsFake = TryClassifyAsFakeViaIdentifiers(
            instruction, state, turn, out var fakeByRuleId);

        // 4. DIRECTIVE EVALUATION. A trap is fake by origin unless an identifier
        //    classified it real this turn (TrustedThenReal).
        var trustedThenReal = instruction.IsFakeByOrigin && cardIsFake == false;
        var evaluatedDirective = EvaluateDirective(
            instruction.BaseDirective, inverted, cardIsFake == true, instruction, context, trustedThenReal);

        // 5. CONFLICT.
        var finalDirective = ResolveConflicts(evaluatedDirective, state, turn, context);

        // Build classification.
        bool isFakeCurrentlyDisplayed = cardIsFake ?? instruction.IsFakeByOrigin;

        // TrustedThenReal: a trap (fake by origin) that an identifier rule made
        // mechanically real for this turn (principles 22, 23).
        var isTrustedThenReal = trustedThenReal;

        if (isFakeCurrentlyDisplayed)
        {
            return ResolutionResult.Fake(
                ruleId: instruction.RuleId,
                correctDirective: finalDirective,
                visual: instruction.Visual ?? VisualMetadata.None,
                turn: turn,
                classifiedByRuleId: fakeByRuleId ?? string.Empty,
                activeSnapshot: activeRules,
                inversionCount: inversionCount,
                wasInvertedMechanically: anyInversionInForce);
        }

        var real = ResolutionResult.Real(
            ruleId: instruction.RuleId,
            finalDirective: finalDirective,
            visual: instruction.Visual ?? VisualMetadata.None,
            turn: turn,
            activeSnapshot: activeRules,
            wasInvertedMechanically: anyInversionInForce,
            inversionCount: inversionCount,
            matchingRules: ResolutionResult.EmptyRules,
            trustedThenReal: isTrustedThenReal,
            trustedThenRealByRuleId: isTrustedThenReal ? (fakeByRuleId ?? string.Empty) : null);

        return WithMemoryWrite(real, instruction, context);
    }
}

