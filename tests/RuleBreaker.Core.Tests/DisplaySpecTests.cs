namespace RuleBreaker.Core.Tests;

using RuleBreaker.Core.Model;
using Xunit;

/// <summary>
/// GDD principle 53: 16:9 widescreen, 1920×1080 minimum, QHD fully supported,
/// no portrait or mobile layout.
/// </summary>
public class DisplaySpecTests
{
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    public void WidescreenResolutionsAtOrAboveTheMinimum_AreSupported(int width, int height)
    {
        Assert.True(DisplaySpec.IsSupported(width, height));
        Assert.True(DisplaySpec.IsWidescreen(width, height));
    }

    [Theory]
    [InlineData(1280, 720)]   // 16:9 but below the 1080p minimum
    [InlineData(1600, 900)]   // 16:9 but below the minimum
    [InlineData(1080, 1920)]  // portrait
    [InlineData(1200, 1920)]  // portrait with a 16:10 ratio
    [InlineData(1600, 1200)]  // 4:3
    [InlineData(1920, 1200)]  // 16:10
    public void AnythingThatIsNotWidescreenAndAtLeastFullHd_IsRejected(int width, int height)
    {
        Assert.False(DisplaySpec.IsSupported(width, height));
    }

    [Fact]
    public void TheDesignSpaceIs1080pWithA16By9Aspect()
    {
        Assert.Equal(1920, DisplaySpec.MinimumWidth);
        Assert.Equal(1080, DisplaySpec.MinimumHeight);
        Assert.Equal(16.0 / 9.0, DisplaySpec.AspectRatio, 12);
    }

    [Theory]
    [InlineData(1920, 1080, 1.0)]
    [InlineData(2560, 1440, 4.0 / 3.0)]
    [InlineData(3840, 2160, 2.0)]
    public void ScaleIsOneAtTheMinimum_AndGrowsWithTheViewport(int width, int height, double expected)
    {
        Assert.Equal(expected, DisplaySpec.ScaleFor(width, height), 12);
    }

    [Fact]
    public void ScaleIsRejectedForAnUnsupportedViewport()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DisplaySpec.ScaleFor(1280, 720));
        Assert.Throws<ArgumentOutOfRangeException>(() => DisplaySpec.ScaleFor(1080, 1920));
    }
}
