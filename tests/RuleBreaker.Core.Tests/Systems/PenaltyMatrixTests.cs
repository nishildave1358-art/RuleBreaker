namespace RuleBreaker.Core.Tests.Systems;

using RuleBreaker.Core.Engine;
using RuleBreaker.Core.Model;
using RuleBreaker.Core.Systems;
using Xunit;

/// <summary>
/// The GDD penalty matrix, its combo-reset and life-loss consequences, and the
/// auto-card trigger rules the penalty flow depends on — principles 16, 24,
/// 26–28, 41–42 and ruling R-8.
/// </summary>
public class PenaltyMatrixTests
{
    // ── the full GDD "Penalty Table by Difficulty and Source" ───────────────────
    [Theory]
    // Real instruction mistake / timer expiry: normal penalty on every tier.
    [InlineData(Difficulty.Easy, TurnOutcomeKind.IncorrectAction, SourceTrace.Real, PenaltyKind.Full)]
    [InlineData(Difficulty.Medium, TurnOutcomeKind.IncorrectAction, SourceTrace.Real, PenaltyKind.Full)]
    [InlineData(Difficulty.Hard, TurnOutcomeKind.IncorrectAction, SourceTrace.Real, PenaltyKind.Full)]
    [InlineData(Difficulty.Nightmare, TurnOutcomeKind.IncorrectAction, SourceTrace.Real, PenaltyKind.Full)]
    [InlineData(Difficulty.Easy, TurnOutcomeKind.TimerExpiry, SourceTrace.Real, PenaltyKind.Full)]
    [InlineData(Difficulty.Medium, TurnOutcomeKind.TimerExpiry, SourceTrace.Real, PenaltyKind.Full)]
    [InlineData(Difficulty.Hard, TurnOutcomeKind.TimerExpiry, SourceTrace.Real, PenaltyKind.Full)]
    [InlineData(Difficulty.Nightmare, TurnOutcomeKind.TimerExpiry, SourceTrace.Real, PenaltyKind.Full)]
    // Fake instruction mistake: Easy none, Medium combo reset only, Hard/Nightmare full.
    [InlineData(Difficulty.Easy, TurnOutcomeKind.IncorrectAction, SourceTrace.FakeOrigin, PenaltyKind.None)]
    [InlineData(Difficulty.Medium, TurnOutcomeKind.IncorrectAction, SourceTrace.FakeOrigin, PenaltyKind.ComboResetOnly)]
    [InlineData(Difficulty.Hard, TurnOutcomeKind.IncorrectAction, SourceTrace.FakeOrigin, PenaltyKind.Full)]
    [InlineData(Difficulty.Nightmare, TurnOutcomeKind.IncorrectAction, SourceTrace.FakeOrigin, PenaltyKind.Full)]
    // A correct decision never costs anything, whatever it traces to (principle 24).
    [InlineData(Difficulty.Easy, TurnOutcomeKind.Correct, SourceTrace.Real, PenaltyKind.None)]
    [InlineData(Difficulty.Medium, TurnOutcomeKind.Correct, SourceTrace.Real, PenaltyKind.None)]
    [InlineData(Difficulty.Hard, TurnOutcomeKind.Correct, SourceTrace.FakeOrigin, PenaltyKind.None)]
    [InlineData(Difficulty.Nightmare, TurnOutcomeKind.Correct, SourceTrace.FakeOrigin, PenaltyKind.None)]
    public void PenaltyTable_MatchesTheGddRowForEverySourceAndTier(
        Difficulty difficulty,
        TurnOutcomeKind kind,
        SourceTrace source,
        PenaltyKind expected)
    {
        Assert.Equal(expected, PenaltyRules.For(kind, source, difficulty));
    }

    [Fact]
    public void TimerExpiry_IsAFullPenaltyOnEveryTier_WithNoEasyException()
    {
        foreach (var difficulty in DifficultyRules.All)
        {
            Assert.Equal(
                PenaltyKind.Full,
                PenaltyRules.For(TurnOutcomeKind.TimerExpiry, SourceTrace.Real, difficulty));
            Assert.Equal(
                PenaltyKind.Full,
                PenaltyRules.For(TurnOutcomeKind.TimerExpiry, SourceTrace.FakeOrigin, difficulty));
        }
    }

    [Fact]
    public void RealInstructionMistake_IsAFullPenaltyOnEveryTier()
    {
        foreach (var difficulty in DifficultyRules.All)
        {
            Assert.Equal(
                PenaltyKind.Full,
                PenaltyRules.For(TurnOutcomeKind.IncorrectAction, SourceTrace.Real, difficulty));
        }
    }

    [Theory]
    [InlineData(Difficulty.Easy, PenaltyKind.None)]
    [InlineData(Difficulty.Medium, PenaltyKind.ComboResetOnly)]
    [InlineData(Difficulty.Hard, PenaltyKind.Full)]
    [InlineData(Difficulty.Nightmare, PenaltyKind.Full)]
    public void FakeInstructionMistake_FollowsTheSourceBRow(Difficulty difficulty, PenaltyKind expected)
    {
        Assert.Equal(expected, PenaltyRules.FakeInstructionMistake(difficulty));
    }

    // ── consequences: combo reset and life loss ─────────────────────────────────
    [Theory]
    [InlineData(PenaltyKind.None, false)]
    [InlineData(PenaltyKind.ComboResetOnly, true)]
    [InlineData(PenaltyKind.Full, true)]
    public void ComboReset_MatchesTheGddResetTable(PenaltyKind penalty, bool resets)
    {
        // GDD: real mistake resets on all tiers; fake mistake does not reset on
        // Easy, resets everywhere else; timer expiry resets; a card-absorbed
        // mistake (PenaltyKind.None) never resets.
        Assert.Equal(resets, PenaltyRules.ResetsCombo(penalty));
    }

    [Theory]
    [InlineData(PenaltyKind.None, false)]
    [InlineData(PenaltyKind.ComboResetOnly, false)]
    [InlineData(PenaltyKind.Full, true)]
    public void LifeLoss_OnlyHappensOnFullPenalties(PenaltyKind penalty, bool costsLife)
    {
        Assert.Equal(costsLife, PenaltyRules.CostsLife(penalty));
    }

    // ── principles 41–42 and R-8: auto-card trigger rules ───────────────────────
    [Fact]
    public void Joker_FiresOnIncorrectActionsOnly_NeverOnTimerExpiry()
    {
        Assert.True(AutoCardRules.CanFire(AutoCardTrigger.Joker, TurnOutcomeKind.IncorrectAction));
        Assert.False(AutoCardRules.CanFire(AutoCardTrigger.Joker, TurnOutcomeKind.TimerExpiry)); // principle 41
        Assert.False(AutoCardRules.CanFire(AutoCardTrigger.Joker, TurnOutcomeKind.Correct));
    }

    [Fact]
    public void Shield_FiresOnAnyFailure_IncludingTimerExpiry()
    {
        Assert.True(AutoCardRules.CanFire(AutoCardTrigger.Shield, TurnOutcomeKind.IncorrectAction));
        Assert.True(AutoCardRules.CanFire(AutoCardTrigger.Shield, TurnOutcomeKind.TimerExpiry)); // principle 42
        Assert.False(AutoCardRules.CanFire(AutoCardTrigger.Shield, TurnOutcomeKind.Correct));
    }

    [Fact]
    public void AutoCardOrder_PutsJokerBeforeShield_OnAnIncorrectAction()
    {
        // R-8: Joker is the more specific trigger, so it is offered first and the
        // failure is absorbed once — never by both cards.
        Assert.Equal(
            new[] { AutoCardTrigger.Joker, AutoCardTrigger.Shield },
            AutoCardRules.FireOrder(TurnOutcomeKind.IncorrectAction));
        Assert.Equal(AutoCardTrigger.Joker, AutoCardRules.FirstEligible(TurnOutcomeKind.IncorrectAction));
    }

    [Fact]
    public void AutoCardOrder_OffersOnlyShield_OnTimerExpiry()
    {
        Assert.Equal(
            new[] { AutoCardTrigger.Shield },
            AutoCardRules.FireOrder(TurnOutcomeKind.TimerExpiry));
        Assert.Equal(AutoCardTrigger.Shield, AutoCardRules.FirstEligible(TurnOutcomeKind.TimerExpiry));
    }

    [Fact]
    public void NoAutoCardIsOffered_ForACorrectDecision()
    {
        Assert.Empty(AutoCardRules.FireOrder(TurnOutcomeKind.Correct));
        Assert.Null(AutoCardRules.FirstEligible(TurnOutcomeKind.Correct));
    }

    [Fact]
    public void UnknownPenaltyInputs_AreRejectedRatherThanGuessed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PenaltyRules.For((TurnOutcomeKind)99, SourceTrace.Real, Difficulty.Easy));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PenaltyRules.For(TurnOutcomeKind.Correct, SourceTrace.Real, (Difficulty)99));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AutoCardRules.FireOrder((TurnOutcomeKind)99));
    }
}
