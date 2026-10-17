namespace RuleBreaker.Core.Input;

/// <summary>
/// Turns raw pointer events into exactly one of the GDD's five gestures. Pure,
/// deterministic, and time-injected: the caller supplies the play clock, so the
/// recogniser behaves identically in tests, headless validation and the engine.
///
/// Disambiguation rules (plan §6 Phase 3), all release-evaluated:
///
/// 1. **First threshold crossed wins.** A press that travels ≥ 24 px before the
///    400 ms mark is a Swipe even if the button stays down afterwards; a press
///    that reaches 400 ms before moving is a Hold even if the player drags later.
///    Both threshold-crossing times are recorded, so the outcome never depends on
///    when the release happens to arrive.
/// 2. **A lone Click is deferred.** The first tap is not reported until the
///    300 ms double-click window closes, so a second press inside that window can
///    still turn the pair into a DoubleClick. A press that begins after the window
///    has closed commits the pending Click first (at its window deadline, which is
///    the moment the decision became certain).
/// 3. **Only a simple tap can complete a double click.** A second press that
///    becomes a Hold or a Swipe is its own gesture; the first tap is committed as
///    a Click immediately, because it can no longer pair with anything.
/// 4. **NoInput is explicit.** Deliberate non-action is a first-class gesture
///    (R-1), declared when the response window ends with no input — never inferred
///    from a missing event.
/// </summary>
public sealed class GestureRecognizer
{
    private const double Epsilon = 1e-9;

    private readonly GestureConfig _config;
    private readonly List<Gesture> _gestures = [];
    private readonly List<CoinFeedback> _feedback = [];

    private double _clock;
    private bool _pressed;
    private double _pressStartTime;
    private double _pressStartX;
    private double _pressStartY;
    private double _cursorX;
    private double _cursorY;
    private double _travel;
    private bool _holdCrossed;
    private double _holdCrossedAt;
    private bool _dragCrossed;
    private double _dragCrossedAt;

    private bool _pendingClick;
    private double _pendingClickPressedAt;
    private double _pendingClickReleasedAt;

    public GestureRecognizer(GestureConfig? config = null) => _config = config ?? GestureConfig.Default;

    public GestureConfig Config => _config;

    /// <summary>True while the left button is down.</summary>
    public bool IsPressed => _pressed;

    /// <summary>True while the current press has passed the hold threshold (the coin glows).</summary>
    public bool IsHolding => _pressed && _holdCrossed;

    /// <summary>True while the current press has passed the drag threshold (the coin follows the cursor).</summary>
    public bool IsDragging => _pressed && _dragCrossed;

    /// <summary>Furthest distance from the press point reached during the current (or last) press.</summary>
    public double TravelPixels => _travel;

    /// <summary>
    /// What the current press would be if it were released now, or null when no
    /// press is in flight. Uses the same first-threshold-wins rule as release, so it
    /// is what the loop records provisionally while a gesture is still resolving
    /// (R-1: expiry evaluates the accumulated input).
    /// </summary>
    public GestureKind? LiveGesture
    {
        get
        {
            if (!_pressed) return null;
            if (DragLeads) return GestureKind.Swipe;
            if (_holdCrossed) return GestureKind.Hold;
            return GestureKind.Click;
        }
    }

    private bool DragLeads => _dragCrossed && (!_holdCrossed || _dragCrossedAt <= _holdCrossedAt);

    public double CursorX => _cursorX;
    public double CursorY => _cursorY;

    /// <summary>
    /// Left button pressed at (<paramref name="x"/>, <paramref name="y"/>).
    /// Emits the coin-depress feedback immediately; the gesture itself waits for release.
    /// </summary>
    public void PointerDown(double x, double y, double timeSeconds)
    {
        AdvanceClock(timeSeconds);
        if (_pressed)
            throw new InvalidOperationException(
                "PointerDown received while the button is already down; every press needs exactly one release.");

        // A press that starts after the double-click window has closed means the
        // previous tap can only ever be a Click (rule 2).
        CommitPendingClickIfDue(_clock);

        _pressed = true;
        _pressStartTime = _clock;
        _pressStartX = x;
        _pressStartY = y;
        _cursorX = x;
        _cursorY = y;
        _travel = 0;
        _holdCrossed = false;
        _holdCrossedAt = 0;
        _dragCrossed = false;
        _dragCrossedAt = 0;

        _feedback.Add(CoinFeedback.Depress);
    }

    /// <summary>Pointer moved to (<paramref name="x"/>, <paramref name="y"/>); only meaningful while pressed.</summary>
    public void PointerMove(double x, double y, double timeSeconds)
    {
        AdvanceClock(timeSeconds);
        _cursorX = x;
        _cursorY = y;

        if (!_pressed) return;

        // The hold threshold may have elapsed without any move events; its crossing
        // time is the threshold instant, not "now" (rule 1).
        ConfirmHoldIfElapsed(_clock);
        UpdateTravel(x, y, _clock);
    }

    /// <summary>
    /// Left button released at (<paramref name="x"/>, <paramref name="y"/>).
    /// This is where a press becomes exactly one gesture.
    /// </summary>
    public void PointerUp(double x, double y, double timeSeconds)
    {
        AdvanceClock(timeSeconds);
        if (!_pressed)
            throw new InvalidOperationException(
                "PointerUp received without a matching PointerDown.");

        _cursorX = x;
        _cursorY = y;

        ConfirmHoldIfElapsed(_clock);
        UpdateTravel(x, y, _clock);

        var pressedAt = _pressStartTime;
        var travel = _travel;

        if (DragLeads)
        {
            // Any tap still waiting for its double-click window can no longer pair
            // with this press — commit it as the Click it is (rule 3).
            FlushPendingClick();
            Emit(new Gesture(GestureKind.Swipe, pressedAt, _clock, travel, Clicks: 1));
        }
        else if (_holdCrossed)
        {
            FlushPendingClick();
            Emit(new Gesture(GestureKind.Hold, pressedAt, _clock, travel, Clicks: 1));
        }
        else if (_pendingClick)
        {
            // The second press began inside the window (otherwise PointerDown would
            // have committed the first tap), and this release is a simple tap.
            _pendingClick = false;
            Emit(new Gesture(GestureKind.DoubleClick, _pendingClickPressedAt, _clock, travel, Clicks: 2));
            _feedback.Add(CoinFeedback.Flip);
        }
        else
        {
            // Defer the Click until the window closes (rule 2).
            _pendingClick = true;
            _pendingClickPressedAt = pressedAt;
            _pendingClickReleasedAt = _clock;
        }

        _pressed = false;
    }

    /// <summary>
    /// Advance the recogniser's clock without pointer input. Emits the hold glow
    /// when the hold threshold is reached with the button still down, and commits a
    /// deferred Click once its double-click window has closed. Call this every frame.
    /// </summary>
    public void AdvanceTo(double timeSeconds)
    {
        AdvanceClock(timeSeconds);

        if (_pressed) ConfirmHoldIfElapsed(_clock);
        CommitPendingClickIfDue(_clock);
    }

    /// <summary>
    /// Declare deliberate non-action (R-1) for the current response window.
    /// Only legal when nothing is in flight: a press or an undecided Click would
    /// contradict "no input".
    /// </summary>
    public void DeclareNoInput(double timeSeconds)
    {
        AdvanceClock(timeSeconds);

        if (_pressed)
            throw new InvalidOperationException(
                "NoInput cannot be declared while the button is down; release first so the gesture can be evaluated.");
        if (_pendingClick)
            throw new InvalidOperationException(
                "NoInput cannot be declared while a Click is awaiting its double-click window; advance past the window or resolve it first.");

        Emit(new Gesture(GestureKind.NoInput, _clock, _clock, TravelPixels: 0, Clicks: 0));
    }

    /// <summary>
    /// Commit any deferred Click immediately, as a Click. Used when the response
    /// window is about to close: an ambiguous tap must resolve before the turn does.
    /// </summary>
    public void FlushPendingClick()
    {
        if (!_pendingClick) return;

        _pendingClick = false;
        Emit(new Gesture(GestureKind.Click, _pendingClickPressedAt, _pendingClickReleasedAt, TravelPixels: 0, Clicks: 1));
    }

    /// <summary>Completed gestures since the last read, oldest first. Reading clears the queue.</summary>
    public Gesture[] ReadGestures()
    {
        var result = _gestures.ToArray();
        _gestures.Clear();
        return result;
    }

    /// <summary>Coin feedback events since the last read, oldest first. Reading clears the queue.</summary>
    public CoinFeedback[] ReadFeedback()
    {
        var result = _feedback.ToArray();
        _feedback.Clear();
        return result;
    }

    /// <summary>Return to the pre-level state (drops queues, pending press and clock).</summary>
    public void Reset()
    {
        _gestures.Clear();
        _feedback.Clear();
        _clock = 0;
        _pressed = false;
        _travel = 0;
        _holdCrossed = false;
        _dragCrossed = false;
        _pendingClick = false;
    }

    private void AdvanceClock(double timeSeconds)
    {
        if (timeSeconds < _clock - Epsilon)
            throw new ArgumentOutOfRangeException(
                nameof(timeSeconds), timeSeconds,
                $"Time only moves forward (clock is at {_clock:0.###} s).");
        if (timeSeconds > _clock) _clock = timeSeconds;
    }

    private void ConfirmHoldIfElapsed(double timeSeconds)
    {
        if (_holdCrossed) return;
        if (timeSeconds - _pressStartTime < _config.HoldThresholdSeconds - Epsilon) return;

        _holdCrossed = true;
        _holdCrossedAt = _pressStartTime + _config.HoldThresholdSeconds;

        // A press already claimed by a drag does not glow: first threshold wins.
        if (!_dragCrossed || _holdCrossedAt <= _dragCrossedAt)
            _feedback.Add(CoinFeedback.Glow);
    }

    private void UpdateTravel(double x, double y, double timeSeconds)
    {
        var dx = x - _pressStartX;
        var dy = y - _pressStartY;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance > _travel) _travel = distance;

        if (_dragCrossed || _travel < _config.DragThresholdPixels - Epsilon) return;

        _dragCrossed = true;
        _dragCrossedAt = timeSeconds;
        _feedback.Add(CoinFeedback.FollowCursor);
    }

    private void CommitPendingClickIfDue(double timeSeconds)
    {
        if (!_pendingClick) return;
        if (timeSeconds < _pendingClickReleasedAt + _config.DoubleClickWindowSeconds - Epsilon) return;

        _pendingClick = false;
        Emit(new Gesture(GestureKind.Click, _pendingClickPressedAt, _pendingClickReleasedAt, TravelPixels: 0, Clicks: 1));
    }

    private void Emit(Gesture gesture) => _gestures.Add(gesture);
}
