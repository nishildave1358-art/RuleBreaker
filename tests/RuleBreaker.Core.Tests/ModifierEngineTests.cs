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

    [Fact]
    public void Resolve_FakeOriginInstruction_ClassifiedFake_NoDirective()
    {
        var trap = new Instruction(
            "TRP_CLICK_NOW", RuleCategory.Trap, "CLICK NOW", BaseDirective: null, IsFakeByOrigin: true);

        var result = _engine.Resolve(trap);

        Assert.True(result.IsFake);
        Assert.Null(result.FinalDirective);
    }

    [Fact]
    public void Resolve_CategoryWithoutBaseDirective_ThrowsUntilPhaseOnePipeline()
    {
        var trigger = new Instruction(
            "TRG_RATE", RuleCategory.Trigger, "Click When You See RATE", BaseDirective: null);

        var ex = Assert.Throws<InvalidOperationException>(() => _engine.Resolve(trigger));
        Assert.Contains("Phase 1", ex.Message);
    }

    [Fact]
    public void Resolve_NullInstruction_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _engine.Resolve(null!));
    }
}
