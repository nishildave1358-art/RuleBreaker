namespace RuleBreaker.Core.Input;

using RuleBreaker.Core.Model;

/// <summary>
/// The five recognised gestures (GDD "Mouse — Gameplay Controls"). NoInput is a
/// gesture like any other: deliberate non-action is a first-class action (R-1),
/// not the absence of one.
/// </summary>
public enum GestureKind
{
    Click,
    DoubleClick,
    Hold,
    Swipe,
    NoInput,
}

public static class GestureKindExtensions
{
    /// <summary>The gameplay action a gesture of this kind submits to the loop.</summary>
    public static CoinAction ToCoinAction(this GestureKind kind) => kind switch
    {
        GestureKind.Click => CoinAction.Click,
        GestureKind.DoubleClick => CoinAction.DoubleClick,
        GestureKind.Hold => CoinAction.Hold,
        GestureKind.Swipe => CoinAction.Swipe,
        GestureKind.NoInput => CoinAction.None,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown gesture."),
    };
}

/// <summary>
/// Coin feedback hooks (GDD "Coin Visual Feedback" column). Presentation consumes
/// these; core never renders anything. One gesture can produce several events
/// (a press depresses the coin immediately, a hold additionally glows once the
/// hold threshold is crossed).
/// </summary>
public enum CoinFeedback
{
    /// <summary>Coin briefly depresses — emitted on every press.</summary>
    Depress,

    /// <summary>Coin flips briefly — emitted when a DoubleClick is recognised.</summary>
    Flip,

    /// <summary>Coin glows while held — emitted once the hold threshold is crossed, before release.</summary>
    Glow,

    /// <summary>Coin follows the cursor — emitted once the drag threshold is crossed.</summary>
    FollowCursor,
}

/// <summary>
/// One completed gesture. Times are injected play-clock seconds, so recognition is
/// deterministic and headless-testable.
/// </summary>
public sealed record Gesture(
    GestureKind Kind,
    double PressedAtSeconds,
    double ReleasedAtSeconds,
    double TravelPixels = 0,
    int Clicks = 0)
{
    public double DurationSeconds => ReleasedAtSeconds - PressedAtSeconds;

    /// <summary>The gameplay action this gesture submits to the loop.</summary>
    public CoinAction ToCoinAction() => Kind.ToCoinAction();
}
