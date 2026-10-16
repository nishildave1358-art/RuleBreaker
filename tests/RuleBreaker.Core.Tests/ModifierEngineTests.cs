namespace RuleBreaker.Core.Tests;

using RuleBreaker.Core.Engine;
using RuleBreaker.Core.Model;
using Xunit;

public class ModifierEngineTests
{
    private readonly ModifierEngine _engine = new();

    [Fact]
    public void Resolve_StandaloneActionInstruction_ReturnsItsDirective()
    {
        var instruction = new Instruction(
            "ACT_CLICK", RuleCategory.Action, "Click Coin", new Directive(CoinAction.Click));

        var result = _engine.Resolve(instruction);

        Assert.False(result.IsFake);
        Assert.Equal(CoinAction.Click, result.FinalDirective!.Action);
    }

    /// <summary>
    /// Phase 0 contract replacement: a trap instruction is fake by origin. In the
    /// empty-rule-set path the engine now returns a concrete Result with IsFake=true
    /// AND a correct directive (NoInput), per the Phase 1 ResolutionResult contract
    /// (exactly one FinalDirective always — never null, never 2+). Source tracking
    /// uses IsFakeCurrentlyDisplayed, not the presence/absence of a directive.
    /// </summary>
    [Fact]
    public void Resolve_FakeOriginTrap_ReturnsFakeResultWithNoInputDirective()
    {
        var trap = new Instruction(
            "TRP_CLICK_NOW", RuleCategory.Trap, "CLICK NOW",
            BaseDirective: null, IsFakeByOrigin: true);

        var result = _engine.Resolve(trap);

        Assert.True(result.IsFake);
        Assert.Equal(CoinAction.None, result.FinalDirective!.Action);
        Assert.True(result.IsFakeCurrentlyDisplayed);
        Assert.Equal("TRP_CLICK_NOW", result.RuleId);
    }

    [Fact]
    public void Resolve_FakeOriginTrap_EmptyRuleSet_MirrorsEmptyRulePath()
    {
        // The empty-rule-set path and the empty-context path must agree on
        // how a fake-origin trap is classified (GDD: always exactly one
        // ResolutionResult — fake with a concrete directive, never null).
        var trap = new Instruction(
            "TRP_CLICK_NOW", RuleCategory.Trap, "CLICK NOW",
            BaseDirective: null, IsFakeByOrigin: true);

        var ctx = new ResolutionContext(new RuleSetState(), 0);
        var result = _engine.Resolve(trap, ctx);

        Assert.True(result.IsFake);
        Assert.Equal(CoinAction.None, result.FinalDirective!.Action);
        Assert.True(result.IsFakeCurrentlyDisplayed);
        Assert.Equal("TRP_CLICK_NOW", result.RuleId);
    }

    /// <summary>
    /// Phase 1 contract: any category without a wired base directive no longer
    /// throws — the engine now resolves it to NoInput (safe "do nothing" until the
    /// relevant subsystem wires its semantics: triggers in Phase 4, memory in Phase
    /// 5). A Trigger with BaseDirective==null (no wired trigger word) therefore
    /// resolves to NoInput, not an exception.
    /// </summary>
    [Fact]
    public void Resolve_UnwiredTrigger_ReturnsNoInputWithoutThrowing()
    {
        var trigger = new Instruction(
            "TRG_RATE", RuleCategory.Trigger, "Click When You See RATE",
            BaseDirective: null);

        var result = _engine.Resolve(trigger);

        Assert.False(result.IsFake);
        Assert.Equal(CoinAction.None, result.FinalDirective!.Action);
    }

    /// <summary>
    /// Phase 0 retained behavior: null input is still rejected at the entrypoint.
    /// </summary>
    [Fact]
    public void Resolve_NullInstruction_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _engine.Resolve(null!));
    }
}
