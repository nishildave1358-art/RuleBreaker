namespace RuleBreaker.Core.Input;

/// <summary>
/// The GDD's complete keyboard vocabulary (principle 51). Nothing else is accepted:
/// every other key is not a shortcut at all.
/// </summary>
public enum KeyboardShortcut
{
    Escape,
    Tab,
    F1,
    F2,
    R,
    Enter,
    ArrowUp,
    ArrowDown,
    ArrowLeft,
    ArrowRight,
}

/// <summary>
/// What a keyboard shortcut may do (GDD "Keyboard — Non-Gameplay Shortcuts Only").
/// Every value is a UI concern; core gameplay has no keyboard command to map to
/// (principle 52), which is why no value here is a coin action.
/// </summary>
public enum UiCommand
{
    PauseMenu,
    ToggleRuleLog,
    OpenSettings,
    ToggleHud,
    ShowReplay,
    ConfirmSelection,
    NavigateUp,
    NavigateDown,
    NavigateLeft,
    NavigateRight,
}

/// <summary>
/// Keyboard → UI mapping, straight from the GDD table. Deliberately stateless and
/// free of any gameplay reference: there is no code path from a key to an action
/// (principle 52), and the mapping is total over <see cref="KeyboardShortcut"/>.
/// </summary>
public static class KeyboardShortcuts
{
    private static readonly IReadOnlyDictionary<KeyboardShortcut, UiCommand> Bindings =
        new Dictionary<KeyboardShortcut, UiCommand>
        {
            [KeyboardShortcut.Escape] = UiCommand.PauseMenu,
            [KeyboardShortcut.Tab] = UiCommand.ToggleRuleLog,
            [KeyboardShortcut.F1] = UiCommand.OpenSettings,
            [KeyboardShortcut.F2] = UiCommand.ToggleHud,
            [KeyboardShortcut.R] = UiCommand.ShowReplay,
            [KeyboardShortcut.Enter] = UiCommand.ConfirmSelection,
            [KeyboardShortcut.ArrowUp] = UiCommand.NavigateUp,
            [KeyboardShortcut.ArrowDown] = UiCommand.NavigateDown,
            [KeyboardShortcut.ArrowLeft] = UiCommand.NavigateLeft,
            [KeyboardShortcut.ArrowRight] = UiCommand.NavigateRight,
        };

    /// <summary>Every keyboard shortcut the game recognises (exactly the GDD's ten).</summary>
    public static IReadOnlyCollection<KeyboardShortcut> All => Bindings.Keys.ToArray();

    /// <summary>The UI command for a shortcut, or null if the key is not in the GDD table.</summary>
    public static UiCommand? CommandFor(KeyboardShortcut shortcut) =>
        Bindings.TryGetValue(shortcut, out var command) ? command : null;
}
