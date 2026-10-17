namespace RuleBreaker.Core.Model;

/// <summary>
/// The GDD's display specification (principle 53): 16:9 widescreen, 1920 × 1080
/// minimum, QHD fully supported, no portrait or mobile layout. This is the
/// coordinate space gestures are measured in, so it belongs to the core rather
/// than to the presentation layer that draws it (Phase 9).
///
/// Version 1 is PC-only (principle 49); there is deliberately no portrait branch.
/// </summary>
public static class DisplaySpec
{
    public const int MinimumWidth = 1920;
    public const int MinimumHeight = 1080;

    /// <summary>16:9 exactly.</summary>
    public const double AspectRatio = 16.0 / 9.0;

    /// <summary>Tolerance for integer resolutions that round to 16:9 (e.g. 1366 × 768).</summary>
    public const double AspectRatioTolerance = 0.01;

    /// <summary>True when the viewport is widescreen 16:9 (within integer-rounding tolerance).</summary>
    public static bool IsWidescreen(int width, int height) =>
        width > 0 && height > 0 &&
        Math.Abs((double)width / height - AspectRatio) <= AspectRatioTolerance;

    /// <summary>
    /// True when the resolution meets the GDD minimum: 16:9 and at least
    /// 1920 × 1080. Larger 16:9 resolutions (QHD, 4K) are supported and scale.
    /// </summary>
    public static bool IsSupported(int width, int height) =>
        width >= MinimumWidth && height >= MinimumHeight && IsWidescreen(width, height);

    /// <summary>
    /// The design-space scale factor: 1.0 at the 1080p minimum, &gt; 1 for larger
    /// 16:9 viewports. Presentation multiplies layout metrics by this so the HUD
    /// stays readable up to QHD/4K (principle 53).
    /// </summary>
    public static double ScaleFor(int width, int height)
    {
        if (!IsSupported(width, height))
            throw new ArgumentOutOfRangeException(
                nameof(width), $"{width}x{height}",
                "Only 16:9 resolutions at or above 1920x1080 are supported (principle 53).");

        return (double)width / MinimumWidth;
    }
}
