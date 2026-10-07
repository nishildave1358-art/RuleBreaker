namespace RuleBreaker.Core.Theme;

/// <summary>
/// The GDD Functional Color System — functional, not decorative (locked principles 58–66).
/// This class is the single source of truth for all game colors; the Godot theme
/// layer (Phase 9) reads from here and no other file may hardcode a gameplay color.
/// Hex ranges are copied verbatim from the GDD "FUNCTIONAL COLOR SYSTEM" table.
/// </summary>
public static class ThemeColors
{
    public static readonly IReadOnlyList<FunctionalColor> FunctionalColors = new[]
    {
        new FunctionalColor("NearBlack", "Base environment (backgrounds, void)", "#0A0A0A", "#111111"),
        new FunctionalColor("MutedGrey", "Secondary surfaces (table, inactive panels)", "#333333", "#555555"),
        new FunctionalColor("White", "Primary text and standard cards", "#E8E8E8", "#FFFFFF"),
        new FunctionalColor("Gold", "Coin, score, combo, active lives", "#C9A84C", "#FFD700"),
        new FunctionalColor("DeepRed", "Atmosphere (ambient tension lighting)", "#8B0000", "#B00000"),
        new FunctionalColor("BrightRed", "Warnings, penalties, danger states", "#CC0000", "#FF2222"),
        new FunctionalColor("Blue", "Trusted / verified / protected", "#1A6FCC", "#4499FF"),
        new FunctionalColor("Amber", "Timers, countdowns, turn tracking", "#E6A800", "#FFD000"),
        new FunctionalColor("Purple", "Modifier rules, Modifier Engine status", "#6600CC", "#9944FF"),
    };

    public static FunctionalColor GetByName(string name) =>
        FunctionalColors.First(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>One functional color: display name, gameplay meaning, and the GDD hex range.</summary>
public sealed record FunctionalColor(string Name, string Role, string HexMin, string HexMax);
