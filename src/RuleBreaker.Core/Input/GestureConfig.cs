namespace RuleBreaker.Core.Input;

/// <summary>
/// Every gesture threshold in one tunable place (plan §6 Phase 3: "all thresholds
/// in one tunable config"). The defaults are the numbers the plan fixes — none of
/// them come from the GDD, so they are balance pins like the Phase 2 activation
/// window, not design constants.
/// </summary>
public sealed record GestureConfig
{
    /// <summary>Double click = a second press that begins within this window of the first release.</summary>
    public const double DefaultDoubleClickWindowSeconds = 0.300;

    /// <summary>A press held at least this long is a Hold, never a Click.</summary>
    public const double DefaultHoldThresholdSeconds = 0.400;

    /// <summary>Travel at least this far from the press point turns the gesture into a Swipe/Drag.</summary>
    public const double DefaultDragThresholdPixels = 24.0;

    public GestureConfig(
        double doubleClickWindowSeconds = DefaultDoubleClickWindowSeconds,
        double holdThresholdSeconds = DefaultHoldThresholdSeconds,
        double dragThresholdPixels = DefaultDragThresholdPixels)
    {
        if (doubleClickWindowSeconds <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(doubleClickWindowSeconds), doubleClickWindowSeconds,
                "The double-click window must be positive: a lone Click commits only when this window closes.");
        if (holdThresholdSeconds <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(holdThresholdSeconds), holdThresholdSeconds,
                "The hold threshold must be positive, or every press would be a Hold.");
        if (dragThresholdPixels <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(dragThresholdPixels), dragThresholdPixels,
                "The drag threshold must be positive, or every click with jitter would be a Swipe.");

        DoubleClickWindowSeconds = doubleClickWindowSeconds;
        HoldThresholdSeconds = holdThresholdSeconds;
        DragThresholdPixels = dragThresholdPixels;
    }

    public double DoubleClickWindowSeconds { get; }

    public double HoldThresholdSeconds { get; }

    public double DragThresholdPixels { get; }

    /// <summary>The plan's defaults: 300 ms · 400 ms · 24 px.</summary>
    public static GestureConfig Default { get; } = new();
}
