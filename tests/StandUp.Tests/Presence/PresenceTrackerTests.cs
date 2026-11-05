using StandUp.Core;

namespace StandUp.Tests;

public class PresenceTrackerTests
{
    private static readonly BreakSettings Settings = new(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(5));

    private sealed class Fixture
    {
        public FakeClock Clock { get; } = new();
        public BreakStateMachine Machine { get; }
        public PresenceTracker Tracker { get; }
        public List<(TimeSpan Away, bool Reset)> Returns { get; } = new();

        public Fixture()
        {
            Machine = new BreakStateMachine(Clock, Settings);
            Tracker = new PresenceTracker(Clock, Machine);
            Tracker.Returned += (away, reset) => Returns.Add((away, reset));
        }
    }

    private static TimeSpan Min(double minutes) => TimeSpan.FromMinutes(minutes);

    [Fact]
    public void ShortAwayIsNotCountedAsWork()
    {
        var f = new Fixture();
        f.Clock.Advance(Min(20));

        f.Tracker.SetAway(AwayReason.Locked, true);
        Assert.True(f.Tracker.IsAway);
        f.Clock.Advance(Min(2));
        f.Tracker.SetAway(AwayReason.Locked, false);

        Assert.False(f.Tracker.IsAway);
        Assert.Equal(Min(10), f.Machine.WorkRemaining);
        Assert.Equal(new[] { (Min(2), false) }, f.Returns);
    }

    [Fact]
    public void AwayAtLeastTheBreakDurationCountsAsRest()
    {
        var f = new Fixture();
        f.Clock.Advance(Min(25));

        f.Tracker.SetAway(AwayReason.Locked, true);
        f.Clock.Advance(Min(5));
        f.Tracker.SetAway(AwayReason.Locked, false);

        Assert.Equal(Min(30), f.Machine.WorkRemaining);
        Assert.Equal(new[] { (Min(5), true) }, f.Returns);
    }

    [Fact]
    public void OverlappingReasonsReturnOnlyWhenAllAreCleared()
    {
        var f = new Fixture();
        f.Clock.Advance(Min(10));

        f.Tracker.SetAway(AwayReason.Locked, true);
        f.Clock.Advance(Min(1));
        f.Tracker.SetAway(AwayReason.Suspended, true);
        f.Clock.Advance(Min(1));
        f.Tracker.SetAway(AwayReason.Suspended, false);
        Assert.True(f.Tracker.IsAway);
        Assert.Empty(f.Returns);

        f.Clock.Advance(Min(1));
        f.Tracker.SetAway(AwayReason.Locked, false);

        Assert.Equal(new[] { (Min(3), false) }, f.Returns);
        Assert.Equal(Min(20), f.Machine.WorkRemaining);
    }

    [Fact]
    public void AwayStartsFromTheGivenSinceNotFromDetection()
    {
        var f = new Fixture();
        f.Clock.Advance(Min(15));

        // 偵測到時離開已 3 分鐘:離開從 3 分鐘前算起。
        f.Tracker.SetAway(AwayReason.Suspended, true, since: f.Clock.UtcNow - Min(3));
        f.Clock.Advance(Min(1));
        f.Tracker.SetAway(AwayReason.Suspended, false);

        Assert.Equal(new[] { (Min(4), false) }, f.Returns);
        // 已過 16 分鐘,排除 4 分鐘 → 工作 12 分鐘,剩 18 分鐘。
        Assert.Equal(Min(18), f.Machine.WorkRemaining);
    }

    [Fact]
    public void EarliestStartWinsWhenAReasonStartedEarlier()
    {
        var f = new Fixture();
        f.Clock.Advance(Min(15));

        f.Tracker.SetAway(AwayReason.Locked, true);
        f.Tracker.SetAway(AwayReason.Suspended, true, since: f.Clock.UtcNow - Min(2));
        f.Clock.Advance(Min(1));
        f.Tracker.SetAway(AwayReason.Suspended, false);
        f.Tracker.SetAway(AwayReason.Locked, false);

        Assert.Equal(new[] { (Min(3), false) }, f.Returns);
    }

    [Fact]
    public void TurningOnAnActiveReasonAgainKeepsTheOriginalStart()
    {
        var f = new Fixture();
        f.Clock.Advance(Min(15));

        f.Tracker.SetAway(AwayReason.Suspended, true, since: f.Clock.UtcNow - Min(3));
        f.Clock.Advance(Min(1));
        f.Tracker.SetAway(AwayReason.Suspended, true, since: f.Clock.UtcNow - Min(1));
        f.Tracker.SetAway(AwayReason.Suspended, false);

        Assert.Equal(new[] { (Min(4), false) }, f.Returns);
    }

    [Fact]
    public void ClearingAReasonThatIsNotSetDoesNothing()
    {
        var f = new Fixture();
        f.Clock.Advance(Min(10));

        f.Tracker.SetAway(AwayReason.Locked, false);

        Assert.Empty(f.Returns);
        Assert.Equal(DateTimeOffset.MinValue, f.Tracker.LastReturnAt);
    }

    [Fact]
    public void FutureSinceIsTreatedAsNow()
    {
        var f = new Fixture();
        f.Clock.Advance(Min(10));

        f.Tracker.SetAway(AwayReason.Suspended, true, since: f.Clock.UtcNow + Min(5));
        f.Clock.Advance(Min(1));
        f.Tracker.SetAway(AwayReason.Suspended, false);

        Assert.Equal(new[] { (Min(1), false) }, f.Returns);
    }

    [Fact]
    public void AwayDuringBreakDoesNotTouchTheCountdownOrRaiseReturned()
    {
        var f = new Fixture();
        f.Clock.Advance(Min(30));
        f.Machine.Update();

        f.Tracker.SetAway(AwayReason.Locked, true);
        f.Clock.Advance(Min(2));
        f.Tracker.SetAway(AwayReason.Locked, false);

        Assert.Equal(BreakState.BreakLocked, f.Machine.State);
        Assert.Equal(Min(3), f.Machine.BreakRemaining);
        Assert.Empty(f.Returns);
        Assert.Equal(f.Clock.UtcNow, f.Tracker.LastReturnAt);
    }

    [Fact]
    public void ReportGapActsLikeACompletedAbsence()
    {
        var f = new Fixture();
        f.Clock.Advance(Min(20));
        var lastTick = f.Clock.UtcNow;

        f.Clock.Advance(Min(90));
        f.Tracker.ReportGap(lastTick);

        Assert.Equal(new[] { (Min(90), true) }, f.Returns);
        Assert.Equal(Min(30), f.Machine.WorkRemaining);
    }

    [Fact]
    public void ReportGapIsIgnoredWhileAway()
    {
        var f = new Fixture();
        f.Clock.Advance(Min(20));
        f.Tracker.SetAway(AwayReason.Suspended, true);
        var lastTick = f.Clock.UtcNow;
        f.Clock.Advance(Min(2));

        f.Tracker.ReportGap(lastTick);

        Assert.Empty(f.Returns);
        Assert.True(f.Tracker.IsAway);
    }

    [Fact]
    public void LateSuspendEventAfterAGapIsNotCountedTwice()
    {
        // 喚醒後先跑到 tick(以間隔備援算了一次),之後才處理睡眠前排隊的 Suspend/Resume。
        var f = new Fixture();
        f.Clock.Advance(Min(10));
        var suspendAt = f.Clock.UtcNow;
        f.Clock.Advance(Min(3));
        f.Tracker.ReportGap(suspendAt);

        f.Tracker.SetAway(AwayReason.Suspended, true, since: suspendAt);
        f.Tracker.SetAway(AwayReason.Suspended, false);

        Assert.Single(f.Returns);
        // 工作 10 分鐘 + 睡眠 3 分鐘(只排除一次)→ 剩 20 分鐘。
        Assert.Equal(Min(20), f.Machine.WorkRemaining);
    }

    [Fact]
    public void LastReturnAtLetsTheCallerSkipADuplicateGap()
    {
        // 睡眠事件已處理(Resume 時清掉 Suspended),之後第一次 tick 才發現間隔:LastReturnAt 晚於上次 tick,不應再算一次。
        var f = new Fixture();
        f.Clock.Advance(Min(20));
        var lastTick = f.Clock.UtcNow;
        f.Tracker.SetAway(AwayReason.Suspended, true);
        f.Clock.Advance(Min(2));
        f.Tracker.SetAway(AwayReason.Suspended, false);

        Assert.True(f.Tracker.LastReturnAt > lastTick);
        Assert.Single(f.Returns);
    }
}
