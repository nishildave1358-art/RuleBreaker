namespace RuleBreaker.Core.Tests;

using RuleBreaker.Core.Theme;
using Xunit;

/// <summary>
/// Guards the Functional Color System: exactly the 9 GDD colors, hex ranges
/// verbatim from the GDD table. Locked principles 58–66 depend on this file;
/// if one of these ranges drifts, gameplay-meaning shifts silently.
/// </summary>
public class ThemeColorsTests
{
    [Fact]
    public void FunctionalColors_ContainsExactlyTheNineGddColors()
    {
        Assert.Equal(9, ThemeColors.FunctionalColors.Count);

        var expectedOrder = new[]
        {
            "NearBlack", "MutedGrey", "White", "Gold", "DeepRed",
            "BrightRed", "Blue", "Amber", "Purple",
        };
        Assert.Equal(expectedOrder, ThemeColors.FunctionalColors.Select(c => c.Name));
    }

    [Fact]
    public void FunctionalColors_HexRanges_MatchGdd()
    {
        Assert.Equal("#0A0A0A", ThemeColors.GetByName("NearBlack").HexMin);
        Assert.Equal("#111111", ThemeColors.GetByName("NearBlack").HexMax);
        Assert.Equal("#C9A84C", ThemeColors.GetByName("Gold").HexMin);
        Assert.Equal("#FFD700", ThemeColors.GetByName("Gold").HexMax);
        Assert.Equal("#CC0000", ThemeColors.GetByName("BrightRed").HexMin);
        Assert.Equal("#FF2222", ThemeColors.GetByName("BrightRed").HexMax);
        Assert.Equal("#1A6FCC", ThemeColors.GetByName("Blue").HexMin);
        Assert.Equal("#4499FF", ThemeColors.GetByName("Blue").HexMax);
        Assert.Equal("#E6A800", ThemeColors.GetByName("Amber").HexMin);
        Assert.Equal("#FFD000", ThemeColors.GetByName("Amber").HexMax);
        Assert.Equal("#6600CC", ThemeColors.GetByName("Purple").HexMin);
        Assert.Equal("#9944FF", ThemeColors.GetByName("Purple").HexMax);
    }

    [Fact]
    public void GetByName_IsCaseInsensitive_AndThrowsOnUnknown()
    {
        Assert.Equal(ThemeColors.GetByName("purple"), ThemeColors.GetByName("PURPLE"));

        Assert.Throws<InvalidOperationException>(() => ThemeColors.GetByName("Teal"));
    }
}
