namespace RuleBreaker.Core.Systems;

using RuleBreaker.Core.Model;

/// <summary>
/// The bridge between a run and the player's cards (plan §6 Phase 6): owns this
/// session's loadout, decides which Auto card fires at loop step 15, and routes
/// Manual card clicks from the Between-Turns Activation Window (step 16) into
/// <see cref="CardEffectEngine"/>.
///
/// Lifecycle follows the GDD exactly:
///   • <see cref="Begin"/> takes the chosen cards out of the collection — they are
///     held by the loadout for the session, not spent yet (principle 39);
///   • an activation consumes its copy from the loadout (principle 38);
///   • <see cref="EndSession"/> returns whatever is still in the loadout
///     (principle 39: "not used during the session are returned").
///
/// Principle 43 lives in <see cref="PlayManual"/>: manual activation is legal
/// only while <see cref="GameSession.Phase"/> is
/// <see cref="TurnPhase.ActivationWindow"/> — never during the response window,
/// never mid-turn.
/// </summary>
public sealed class CardSession
{
    private CardSession(
        GameSession session,
        CardCollection collection,
        CardLoadout loadout,
        CardEffectEngine effects)
    {
        Session = session;
        Collection = collection;
        Loadout = loadout;
        Effects = effects;
    }

    public GameSession Session { get; }

    public CardCollection Collection { get; }

    /// <summary>The three (or fewer) cards available this session.</summary>
    public CardLoadout Loadout { get; private set; }

    public CardEffectEngine Effects { get; }

    /// <summary>True until <see cref="EndSession"/> hands the unused cards back.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Loadout slots glow while this is true (principle 43, Phase 9 draws it).</summary>
    public bool IsActivationWindowOpen => Session.Phase == TurnPhase.ActivationWindow;

    /// <summary>The expired rules Echo may offer for resurrection (principle 8).</summary>
    public IReadOnlyList<ActiveRule> EchoChoices => Session.ExpiredRules;

    /// <summary>
    /// Start a session with the chosen loadout. Each chosen copy leaves the
    /// collection and is held by the loadout; unused copies return at
    /// <see cref="EndSession"/>.
    /// </summary>
    public static CardSession Begin(
        GameSession session,
        CardCollection collection,
        IEnumerable<string> cardIds,
        CardEffectEngine effects)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(cardIds);
        ArgumentNullException.ThrowIfNull(effects);

        var loadout = collection.BuildLoadout(cardIds);

        foreach (var card in loadout.Cards)
        {
            if (!collection.TryConsume(card.Id))
                throw new InvalidOperationException(
                    $"The collection no longer holds '{card.Name}', so the session cannot take it.");
        }

        return new CardSession(session, collection, loadout, effects);
    }

    /// <summary>
    /// The Auto card that would fire for this outcome, without consuming it — the
    /// presentation layer uses this to preview the flash (GDD "Auto Card Activation
    /// Animation"). Null when nothing in the loadout can cover this failure.
    /// </summary>
    public CardDefinition? PeekAutoCard(TurnOutcomeKind kind)
    {
        foreach (var trigger in AutoCardRules.FireOrder(kind))
        {
            var effect = EffectFor(trigger);
            var card = Loadout.Cards.FirstOrDefault(c => c.Effect == effect);
            if (card is not null) return card;
        }

        return null;
    }

    /// <summary>
    /// Step 15: pick the Auto card that fires (R-8 order, at most one), consume it
    /// and settle the turn with its id. Returns the fired card's id, or null when
    /// the failure is paid normally.
    ///
    /// Joker-then-Shield ordering is Phase 5's
    /// <see cref="AutoCardRules.FireOrder"/>; this method only resolves that order
    /// against the loadout, so both cards can never be spent on one failure.
    /// </summary>
    public string? ResolveAutoCardCheck()
    {
        if (!IsActive)
            throw new InvalidOperationException("This card session has already ended.");

        if (Session.Phase != TurnPhase.AutoCardCheck)
            throw new InvalidOperationException(
                $"The auto-card check runs at loop step 15, in phase {TurnPhase.AutoCardCheck}; " +
                $"the session is in phase {Session.Phase}.");

        var outcome = Session.Outcome
            ?? throw new InvalidOperationException(
                "There is no evaluated turn to cover — ResolveEvaluation must run first.");

        var card = PeekAutoCard(outcome.Kind);
        if (card is null)
        {
            Session.ResolveAutoCardCheck(absorbedByCardId: null);
            return null;
        }

        Loadout = Loadout.Without(card.Id);              // principle 38: one copy consumed
        Session.ResolveAutoCardCheck(card.Id);           // principle 46: redundancy is the card's problem
        return card.Id;
    }

    /// <summary>
    /// Step 16: play a Manual card during the Between-Turns Activation Window.
    /// Throws outside the window (principle 43) — that is a programming error in
    /// the caller, not a player action. A card whose effect cannot apply is
    /// returned unapplied and <em>not</em> consumed.
    /// </summary>
    public CardEffectResult PlayManual(string cardId, string? targetRuleId = null)
    {
        if (!IsActive)
            throw new InvalidOperationException("This card session has already ended.");

        if (!IsActivationWindowOpen)
            throw new InvalidOperationException(
                "Manual cards may only be activated during the between-turns activation window " +
                $"(principle 43); the session is in phase {Session.Phase}.");

        var card = Loadout.Cards.FirstOrDefault(c =>
            string.Equals(c.Id, cardId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Card '{cardId}' is not in this session's loadout.");

        if (card.Activation != CardActivation.Manual)
            throw new InvalidOperationException(
                $"'{card.Name}' is an Auto card: it fires on failure, it is never clicked (principle 40).");

        var result = Effects.Apply(card, Session, targetRuleId);

        if (result.Applied)
            Loadout = Loadout.Without(card.Id);          // principle 38

        return result;
    }

    /// <summary>
    /// Return every unused card to the collection and close the session
    /// (principle 39). Safe to call once; a second call is a no-op.
    /// </summary>
    public void EndSession()
    {
        if (!IsActive) return;

        Collection.ReturnUnused(Loadout);
        Loadout = CardLoadout.Empty;
        IsActive = false;
    }

    /// <summary>The GDD pairs each absorbing card with one trigger (principles 41, 42).</summary>
    private static CardEffectKind EffectFor(AutoCardTrigger trigger) => trigger switch
    {
        AutoCardTrigger.Joker => CardEffectKind.Joker,
        AutoCardTrigger.Shield => CardEffectKind.Shield,
        _ => throw new ArgumentOutOfRangeException(
            nameof(trigger), trigger, "Unknown auto-card trigger."),
    };
}
