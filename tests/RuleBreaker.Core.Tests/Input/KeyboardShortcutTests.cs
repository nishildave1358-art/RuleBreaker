namespace RuleBreaker.Core.Tests.Input;

using System.Reflection;
using RuleBreaker.Core.Input;
using RuleBreaker.Core.Model;
using RuleBreaker.Core.Systems;
using Xunit;

/// <summary>
/// GDD principles 51–52: the keyboard is UI-only, and core gameplay never touches
/// it. The mapping is pinned to the GDD table, and the guarantee is structural —
/// there is no keyboard-parameter entry point on the session to call.
/// </summary>
public class KeyboardShortcutTests
{
    [Theory]
    [InlineData(KeyboardShortcut.Escape, UiCommand.PauseMenu)]
    [InlineData(KeyboardShortcut.Tab, UiCommand.ToggleRuleLog)]
    [InlineData(KeyboardShortcut.F1, UiCommand.OpenSettings)]
    [InlineData(KeyboardShortcut.F2, UiCommand.ToggleHud)]
    [InlineData(KeyboardShortcut.R, UiCommand.ShowReplay)]
    [InlineData(KeyboardShortcut.Enter, UiCommand.ConfirmSelection)]
    [InlineData(KeyboardShortcut.ArrowUp, UiCommand.NavigateUp)]
    [InlineData(KeyboardShortcut.ArrowDown, UiCommand.NavigateDown)]
    [InlineData(KeyboardShortcut.ArrowLeft, UiCommand.NavigateLeft)]
    [InlineData(KeyboardShortcut.ArrowRight, UiCommand.NavigateRight)]
    public void EveryShortcut_MapsToTheGddUiFunction(KeyboardShortcut shortcut, UiCommand expected)
    {
        Assert.Equal(expected, KeyboardShortcuts.CommandFor(shortcut));
    }

    [Fact]
    public void TheKeyboardVocabulary_IsExactlyTheGddTable()
    {
        var expected = new[]
        {
            KeyboardShortcut.Escape,
            KeyboardShortcut.Tab,
            KeyboardShortcut.F1,
            KeyboardShortcut.F2,
            KeyboardShortcut.R,
            KeyboardShortcut.Enter,
            KeyboardShortcut.ArrowUp,
            KeyboardShortcut.ArrowDown,
            KeyboardShortcut.ArrowLeft,
            KeyboardShortcut.ArrowRight,
        };

        Assert.Equal(expected.OrderBy(k => k), KeyboardShortcuts.All.OrderBy(k => k));
        Assert.Equal(expected.OrderBy(k => k), Enum.GetValues<KeyboardShortcut>().OrderBy(k => k));
    }

    [Fact]
    public void EveryKeyboardCommand_IsAUiConcern_NotACoinAction()
    {
        var coinActions = Enum.GetNames<CoinAction>();

        foreach (var shortcut in Enum.GetValues<KeyboardShortcut>())
        {
            var command = KeyboardShortcuts.CommandFor(shortcut);
            Assert.NotNull(command); // no gameplay key is unmapped

            Assert.DoesNotContain(command!.Value.ToString(), coinActions);
        }
    }

    [Fact]
    public void GameSession_HasNoKeyboardEntryPoint()
    {
        var keyboardShapedParameters = typeof(GameSession)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .SelectMany(method => method.GetParameters())
            .Where(parameter =>
                parameter.ParameterType == typeof(KeyboardShortcut) ||
                parameter.ParameterType == typeof(UiCommand) ||
                parameter.ParameterType == typeof(ConsoleKey))
            .ToArray();

        Assert.Empty(keyboardShapedParameters);
    }

    [Fact]
    public void PressingEveryShortcut_DoesNotChangeGameplayState()
    {
        var session = new GameSession();
        session.ShowInstruction(new Instruction("act_click", RuleCategory.Action, "Click", new Directive(CoinAction.Click)));
        session.OpenResponseWindow();
        session.Advance(1.25);

        var before = Snapshot(session);

        foreach (var shortcut in Enum.GetValues<KeyboardShortcut>())
        {
            var command = KeyboardShortcuts.CommandFor(shortcut);
            Assert.NotNull(command);
        }

        Assert.Equal(before, Snapshot(session));
    }

    private static (TurnPhase, int, double, CoinAction?, bool, double) Snapshot(GameSession session) =>
        (
            session.Phase,
            session.TurnNumber,
            session.Timer.RemainingSeconds,
            session.AccumulatedInput,
            session.IsPaused,
            session.ActivationWindowRemainingSeconds
        );
}
