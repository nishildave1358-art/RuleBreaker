namespace RuleBreaker.Core.Input;

using RuleBreaker.Core.Model;
using RuleBreaker.Core.Systems;

/// <summary>
/// Connects recognised gestures to the core loop (plan §6 Phase 3, item 2).
///
/// The split between Phase 2's two input entry points is deliberate:
///  • a press records a provisional <see cref="CoinAction.Click"/> with
///    <see cref="GameSession.RecordInput"/> — if the response window expires while
///    the gesture is still being recognised, R-1 evaluates that accumulated input;
///  • a completed gesture calls <see cref="GameSession.SubmitInput"/> and closes
///    the response window.
///
/// Gestures recognised outside the response window (e.g. a click on a card slot
/// during the between-turns window) are not submitted; they are counted in
/// <see cref="DroppedGestureCount"/> so the caller can route them to the card UI.
/// </summary>
public sealed class SessionInputAdapter
{
    private readonly GameSession _session;
    private readonly List<Gesture> _applied = [];

    public SessionInputAdapter(GameSession session, GestureRecognizer? recognizer = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        Recognizer = recognizer ?? new GestureRecognizer();
    }

    public GestureRecognizer Recognizer { get; }

    /// <summary>Every gesture that reached the session, in recognition order.</summary>
    public IReadOnlyList<Gesture> AppliedGestures => _applied;

    /// <summary>Gestures recognised while the session was not accepting input.</summary>
    public int DroppedGestureCount { get; private set; }

    public void PointerDown(double x, double y, double timeSeconds)
    {
        Recognizer.PointerDown(x, y, timeSeconds);
        RecordProvisionalGesture();
        Pump();
    }

    public void PointerMove(double x, double y, double timeSeconds)
    {
        Recognizer.PointerMove(x, y, timeSeconds);
        RecordProvisionalGesture();
        Pump();
    }

    public void PointerUp(double x, double y, double timeSeconds)
    {
        Recognizer.PointerUp(x, y, timeSeconds);
        Pump();
    }

    /// <summary>Frame tick for the recogniser (hold glow, deferred Click commit).</summary>
    public void AdvanceTo(double timeSeconds)
    {
        Recognizer.AdvanceTo(timeSeconds);
        RecordProvisionalGesture();
        Pump();
    }

    /// <summary>Deliberate non-action for this turn (R-1) — a first-class submission.</summary>
    public void DeclareNoInput(double timeSeconds)
    {
        Recognizer.DeclareNoInput(timeSeconds);
        Pump();
    }

    /// <summary>Resolve an ambiguous tap as a Click before the turn closes.</summary>
    public void FlushPendingClick()
    {
        Recognizer.FlushPendingClick();
        Pump();
    }

    public CoinFeedback[] ReadFeedback() => Recognizer.ReadFeedback();

    private bool AcceptsInput => _session.Phase == TurnPhase.ResponseWindow;

    /// <summary>
    /// Keep the session's accumulated input in step with the gesture still resolving,
    /// so an expiry mid-press evaluates what the player was actually doing (R-1):
    /// press → Click, threshold-crossed drag → Swipe, threshold-crossed hold → Hold.
    /// </summary>
    private void RecordProvisionalGesture()
    {
        if (!AcceptsInput) return;
        if (Recognizer.LiveGesture is not { } kind) return;

        _session.RecordInput(kind.ToCoinAction());
    }

    private void Pump()
    {
        foreach (var gesture in Recognizer.ReadGestures())
        {
            if (!AcceptsInput)
            {
                DroppedGestureCount++;
                continue;
            }

            _applied.Add(gesture);
            if (gesture.Kind == GestureKind.NoInput) _session.ConfirmNoInput();
            else _session.SubmitInput(gesture.ToCoinAction());
        }
    }
}
