namespace RuleBreaker.Core.Tests.Systems;

using RuleBreaker.Core.Engine;
using RuleBreaker.Core.Model;
using RuleBreaker.Core.Systems;
using Xunit;

/// <summary>
/// The GDD combo multipliers, score payout (plan ruling R-12) and the 10/20/30+
/// milestone thresholds (principle 48).
/// </summary>
public class ComboScoreTests
{
    // ── helpers ────────────────────────────────────────────────────────────────
    private static TurnOutcome Correct(int turn) => new(
        TurnOutcomeKind.Correct, turn, "ACT_CLICK", CoinAction.Click, CoinAction.Click,
        SourceTrace.Real, AutoCardCheckRequired: false);

    private static TurnOutcome Wrong(int turn, SourceTrace source = SourceTrace.Real) => new(
        TurnOutcomeKind.IncorrectAction, turn, "ACT_CLICK", CoinAction.Click, CoinAction.Swipe,
        source, AutoCardCheckRequired: true);

    private static RunState NewRun(Difficulty difficulty = Difficulty.Medium, int lives = RunState.DefaultStartingLives)
        => new(difficulty, lives);

    // ── the GDD combo table ────────────────────────────────────────────────────
    [Theory]
    [InlineData(0, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(9, 2)]
    [InlineData(10, 3)]
    [InlineData(19, 3)]
    [InlineData(20, 5)]
    [InlineData(21, 5)]
    [InlineData(100, 5)]
    public void Multiplier_MatchesTheGddComboTable(int consecutiveCorrect, int expected)
    {
        Assert.Equal(expected, ComboRules.MultiplierFor(consecutiveCorrect));
    }

    [Fact]
    public void RegisterCorrect_GrowsTheChain_AndTheMultiplierFollows()
    {
        var combo = new ComboMeter();

        Assert.Equal(0, combo.ConsecutiveCorrect);
        Assert.Equal(1, combo.Multiplier);

        for (var i = 1; i <= 4; i++)
        {
            combo.RegisterCorrect();
            Assert.Equal(i, combo.ConsecutiveCorrect);
            Assert.Equal(1, combo.Multiplier);
        }

        combo.RegisterCorrect(); // 5th
        Assert.Equal(2, combo.Multiplier);

        for (var i = 6; i <= 10; i++) combo.RegisterCorrect(); // 10th
        Assert.Equal(3, combo.Multiplier);

        for (var i = 11; i <= 20; i++) combo.RegisterCorrect(); // 20th
        Assert.Equal(5, combo.Multiplier);
    }

    [Fact]
    public void Reset_ReturnsTheChainAndMultiplierToTheStart()
    {
        var combo = new ComboMeter();
        for (var i = 0; i < 7; i++) combo.RegisterCorrect();

        combo.Reset();

        Assert.Equal(0, combo.ConsecutiveCorrect);
        Assert.Equal(1, combo.Multiplier);
    }

    [Fact]
    public void NegativeChains_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ComboRules.MultiplierFor(-1));
    }

    // ── principle 48: milestone thresholds 10 / 20 / 30+ ───────────────────────
    [Theory]
    [InlineData(10, ComboMilestone.Ten)]
    [InlineData(20, ComboMilestone.Twenty)]
    [InlineData(30, ComboMilestone.ThirtyPlus)]
    public void Milestones_FireAtTheGddThresholds(int correct, ComboMilestone expected)
    {
        Assert.Equal(expected, ComboRules.MilestoneFor(correct));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(11)]
    [InlineData(19)]
    [InlineData(21)]
    [InlineData(29)]
    [InlineData(31)]
    [InlineData(100)]
    public void Milestones_DoNotRepeatBetweenThresholds(int correct)
    {
        // A reward is earned once per chain: passing 30 does not pay out every turn.
        Assert.Equal(ComboMilestone.None, ComboRules.MilestoneFor(correct));
    }

    // ── plan ruling R-12: 100 per correct decision × the current multiplier ─────
    [Fact]
    public void CorrectDecisions_ScoreOneHundredTimesTheMultiplier()
    {
        var run = NewRun();

        // Turns 1–4 at 1x: 4 × 100.
        for (var turn = 1; turn <= 4; turn++) run.Settle(Correct(turn));
        Assert.Equal(400, run.Score);
        Assert.Equal(1, run.Multiplier);

        // The 5th decision completes the 2x set, so it is the first paid at 2x.
        var fifth = run.Settle(Correct(5));
        Assert.Equal(200, fifth.ScoreAwarded);
        Assert.Equal(600, run.Score);
        Assert.Equal(2, run.Multiplier);

        // 6th–9th stay at 2x.
        for (var turn = 6; turn <= 9; turn++) run.Settle(Correct(turn));
        Assert.Equal(1400, run.Score);

        // The 10th decision completes the 3x set and is paid at 3x.
        var tenth = run.Settle(Correct(10));
        Assert.Equal(300, tenth.ScoreAwarded);
        Assert.Equal(1700, run.Score);
        Assert.Equal(3, run.Multiplier);

        for (var turn = 11; turn <= 19; turn++) run.Settle(Correct(turn));
        Assert.Equal(4400, run.Score);

        // The 20th decision completes the 5x set.
        var twentieth = run.Settle(Correct(20));
        Assert.Equal(500, twentieth.ScoreAwarded);
        Assert.Equal(4900, run.Score);
        Assert.Equal(5, run.Multiplier);
    }

    [Fact]
    public void Milestone_IsReportedOnTheSettlementThatCrossesIt()
    {
        var run = NewRun();
        var milestoneTurns = new List<(int Turn, ComboMilestone Milestone)>();

        for (var turn = 1; turn <= 30; turn++)
            milestoneTurns.Add((turn, run.Settle(Correct(turn)).Milestone));

        Assert.Equal(ComboMilestone.Ten, milestoneTurns.Single(m => m.Turn == 10).Milestone);
        Assert.Equal(ComboMilestone.Twenty, milestoneTurns.Single(m => m.Turn == 20).Milestone);
        Assert.Equal(ComboMilestone.ThirtyPlus, milestoneTurns.Single(m => m.Turn == 30).Milestone);

        var others = milestoneTurns.Where(m => m.Turn is not (10 or 20 or 30));
        Assert.All(others, m => Assert.Equal(ComboMilestone.None, m.Milestone));
    }

    [Fact]
    public void Failures_NeverSubtractScore_AndPayNothing()
    {
        var run = NewRun(Difficulty.Hard);
        run.Settle(Correct(1));
        run.Settle(Correct(2));
        var scoreBefore = run.Score;

        var wrong = run.Settle(Wrong(3));

        Assert.Equal(0, wrong.ScoreAwarded);
        Assert.Equal(scoreBefore, run.Score);
        Assert.Equal(0, run.ConsecutiveCorrect);
    }
}
