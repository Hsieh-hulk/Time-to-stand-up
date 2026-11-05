using TimeToStandUp.Core;

namespace TimeToStandUp.Tests;

public class BreakStateMachineTests
{
    private static readonly BreakSettings Settings = new(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(5));

    private static (BreakStateMachine Machine, FakeClock Clock) Create()
    {
        var clock = new FakeClock();
        return (new BreakStateMachine(clock, Settings), clock);
    }

    private static TimeSpan Min(double minutes) => TimeSpan.FromMinutes(minutes);

    [Fact]
    public void StartsInWorking()
    {
        var (machine, _) = Create();

        Assert.Equal(BreakState.Working, machine.State);
        Assert.Equal(Min(30), machine.WorkRemaining);
    }

    [Fact]
    public void StaysWorkingBeforeIntervalElapses()
    {
        var (machine, clock) = Create();

        clock.Advance(Min(29.99));
        machine.Update();

        Assert.Equal(BreakState.Working, machine.State);
    }

    [Fact]
    public void EntersBreakLockedWhenIntervalElapses()
    {
        var (machine, clock) = Create();

        clock.Advance(Min(30));
        machine.Update();

        Assert.Equal(BreakState.BreakLocked, machine.State);
        Assert.Equal(Min(5), machine.BreakRemaining);
        Assert.Equal(0, machine.BreakProgress);
    }

    [Fact]
    public void BreakProgressFollowsElapsedTime()
    {
        var (machine, clock) = Create();
        clock.Advance(Min(30));
        machine.Update();

        clock.Advance(Min(1));
        machine.Update();

        Assert.Equal(BreakState.BreakLocked, machine.State);
        Assert.Equal(0.2, machine.BreakProgress, precision: 6);
        Assert.Equal(Min(4), machine.BreakRemaining);
    }

    [Fact]
    public void EntersBreakReadyWhenCountdownFinishes()
    {
        var (machine, clock) = Create();
        clock.Advance(Min(30));
        machine.Update();

        clock.Advance(Min(5));
        machine.Update();

        Assert.Equal(BreakState.BreakReady, machine.State);
        Assert.Equal(TimeSpan.Zero, machine.BreakRemaining);
        Assert.Equal(1, machine.BreakProgress);
    }

    [Fact]
    public void UnlockIsRejectedDuringCountdown()
    {
        var (machine, clock) = Create();
        clock.Advance(Min(30));
        machine.Update();
        clock.Advance(Min(4.99));

        Assert.False(machine.Unlock());
        Assert.Equal(BreakState.BreakLocked, machine.State);
    }

    [Fact]
    public void UnlockIsRejectedWhileWorking()
    {
        var (machine, _) = Create();

        Assert.False(machine.Unlock());
        Assert.Equal(BreakState.Working, machine.State);
    }

    [Fact]
    public void BreakReadyDoesNotCloseOnItsOwn()
    {
        var (machine, clock) = Create();
        clock.Advance(Min(30));
        machine.Update();
        clock.Advance(Min(5));
        machine.Update();
        Assert.Equal(BreakState.BreakReady, machine.State);

        clock.Advance(TimeSpan.FromHours(8));
        machine.Update();

        Assert.Equal(BreakState.BreakReady, machine.State);
    }

    [Fact]
    public void UnlockRestartsWorkTimerFromUnlockTime()
    {
        var (machine, clock) = Create();
        clock.Advance(Min(30));
        machine.Update();
        clock.Advance(Min(5));
        machine.Update();
        clock.Advance(Min(20)); // 使用者離開 20 分鐘後才回來按解鎖
        machine.Update();

        Assert.True(machine.Unlock());
        Assert.Equal(BreakState.Working, machine.State);
        Assert.Equal(Min(30), machine.WorkRemaining);

        clock.Advance(Min(29));
        machine.Update();
        Assert.Equal(BreakState.Working, machine.State);

        clock.Advance(Min(1));
        machine.Update();
        Assert.Equal(BreakState.BreakLocked, machine.State);
    }

    [Fact]
    public void UnlockWorksEvenIfUpdateWasNotCalledAfterCountdownEnded()
    {
        var (machine, clock) = Create();
        clock.Advance(Min(30));
        machine.Update();

        clock.Advance(Min(5)); // 沒有呼叫 Update

        Assert.True(machine.Unlock());
        Assert.Equal(BreakState.Working, machine.State);
    }

    [Fact]
    public void WakingFromSleepPastBreakDurationGoesStraightToBreakReady()
    {
        var (machine, clock) = Create();
        clock.Advance(Min(30));
        machine.Update();
        Assert.Equal(BreakState.BreakLocked, machine.State);

        clock.Advance(TimeSpan.FromHours(3)); // 休息畫面期間電腦睡眠
        machine.Update();

        Assert.Equal(BreakState.BreakReady, machine.State);
    }

    [Fact]
    public void WakingFromSleepMidCountdownKeepsRemainingTime()
    {
        var (machine, clock) = Create();
        clock.Advance(Min(30));
        machine.Update();

        clock.Advance(Min(2));
        machine.Update();

        Assert.Equal(BreakState.BreakLocked, machine.State);
        Assert.Equal(Min(3), machine.BreakRemaining);
    }

    [Fact]
    public void StartBreakNowEntersBreakLockedFromWorking()
    {
        var (machine, clock) = Create();
        clock.Advance(Min(3));

        Assert.True(machine.StartBreakNow());

        Assert.Equal(BreakState.BreakLocked, machine.State);
        Assert.Equal(Min(5), machine.BreakRemaining);
    }

    [Fact]
    public void StartBreakNowIsIgnoredOutsideWorking()
    {
        var (machine, _) = Create();
        machine.StartBreakNow();

        Assert.False(machine.StartBreakNow());
        Assert.Equal(BreakState.BreakLocked, machine.State);
    }

    [Fact]
    public void WarningWindowStartsOneMinuteBeforeBreak()
    {
        var (machine, clock) = Create();

        clock.Advance(Min(28.9));
        Assert.False(machine.IsWarning);

        clock.Advance(Min(0.9)); // 剩 1.2 → 0.2 分鐘
        clock.Advance(Min(0.1));
        Assert.True(machine.IsWarning);

        clock.Advance(Min(1));
        machine.Update();
        Assert.False(machine.IsWarning); // 已進入休息
    }

    [Fact]
    public void WarningLeadShrinksForVeryShortIntervals()
    {
        var settings = new BreakSettings(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10));
        var clock = new FakeClock();
        var machine = new BreakStateMachine(clock, settings);

        Assert.Equal(TimeSpan.FromSeconds(15), settings.WarningLead);
        Assert.False(machine.IsWarning);

        clock.Advance(TimeSpan.FromSeconds(16));
        Assert.True(machine.IsWarning);
    }

    [Fact]
    public void ClockMovingBackwardsDoesNotProduceNegativeElapsedTime()
    {
        var (machine, clock) = Create();
        clock.Advance(Min(10));
        clock.Advance(-Min(60));

        machine.Update();

        Assert.Equal(BreakState.Working, machine.State);
        Assert.Equal(Min(30), machine.WorkRemaining);
    }

    [Fact]
    public void RaisesStateChangedForEachTransition()
    {
        var (machine, clock) = Create();
        var transitions = new List<(BreakState, BreakState)>();
        machine.StateChanged += (from, to) => transitions.Add((from, to));

        clock.Advance(Min(30));
        machine.Update();
        clock.Advance(Min(5));
        machine.Update();
        machine.Unlock();

        Assert.Equal(
            new[]
            {
                (BreakState.Working, BreakState.BreakLocked),
                (BreakState.BreakLocked, BreakState.BreakReady),
                (BreakState.BreakReady, BreakState.Working),
            },
            transitions);
    }

    private static BreakSettings Minutes(double work, double rest) => new(Min(work), Min(rest));

    [Theory]
    [InlineData(30, 60, 40)] // 20 分鐘後把 30 改成 60,還剩 40 分鐘
    [InlineData(30, 25, 5)]
    [InlineData(30, 20.5, 0.5)]
    [InlineData(30, 30, 10)]
    [InlineData(30, 1, 0)]
    public void ApplySettingsKeepsAccumulatedWorkTime(double oldInterval, double newInterval, double expectedRemaining)
    {
        var clock = new FakeClock();
        var machine = new BreakStateMachine(clock, Minutes(oldInterval, 5));
        clock.Advance(Min(20));
        machine.Update();

        Assert.True(machine.ApplySettings(Minutes(newInterval, 5)));

        Assert.Equal(BreakState.Working, machine.State);
        Assert.Equal(Min(expectedRemaining), machine.WorkRemaining);
        Assert.Equal(Min(newInterval), machine.Settings.WorkInterval);
    }

    [Fact]
    public void ApplySettingsWithShorterIntervalThanElapsedEntersBreakOnNextUpdate()
    {
        var (machine, clock) = Create();
        clock.Advance(Min(20));
        machine.Update();

        machine.ApplySettings(Minutes(15, 5));
        Assert.Equal(BreakState.Working, machine.State); // 套用本身不切換狀態

        machine.Update();
        Assert.Equal(BreakState.BreakLocked, machine.State);
        Assert.Equal(Min(5), machine.BreakRemaining);
    }

    [Fact]
    public void ApplySettingsWithLongerIntervalDelaysTheBreak()
    {
        var (machine, clock) = Create();
        clock.Advance(Min(20));
        machine.ApplySettings(Minutes(60, 5));

        clock.Advance(Min(39));
        machine.Update();
        Assert.Equal(BreakState.Working, machine.State);

        clock.Advance(Min(1));
        machine.Update();
        Assert.Equal(BreakState.BreakLocked, machine.State);
    }

    [Fact]
    public void ApplySettingsChangesBreakDurationForTheNextBreak()
    {
        var (machine, clock) = Create();
        clock.Advance(Min(10));
        machine.ApplySettings(Minutes(30, 12));

        clock.Advance(Min(20));
        machine.Update();

        Assert.Equal(BreakState.BreakLocked, machine.State);
        Assert.Equal(Min(12), machine.BreakRemaining);
    }

    [Fact]
    public void ApplySettingsUpdatesWarningWindow()
    {
        var (machine, clock) = Create();
        clock.Advance(Min(10));
        Assert.False(machine.IsWarning);

        machine.ApplySettings(Minutes(10.5, 5)); // 只剩 30 秒

        Assert.True(machine.IsWarning);
    }

    [Fact]
    public void ApplySettingsIsRejectedDuringBreakLocked()
    {
        var (machine, clock) = Create();
        clock.Advance(Min(30));
        machine.Update();
        clock.Advance(Min(1));

        Assert.False(machine.ApplySettings(Minutes(60, 1)));

        Assert.Same(Settings, machine.Settings);
        machine.Update();
        Assert.Equal(BreakState.BreakLocked, machine.State);
        Assert.Equal(Min(4), machine.BreakRemaining);
    }

    [Fact]
    public void ApplySettingsIsRejectedDuringBreakReady()
    {
        var (machine, clock) = Create();
        clock.Advance(Min(30));
        machine.Update();
        clock.Advance(Min(5));
        machine.Update();

        Assert.False(machine.ApplySettings(Minutes(60, 1)));

        Assert.Same(Settings, machine.Settings);
        Assert.Equal(BreakState.BreakReady, machine.State);
    }

    [Fact]
    public void NewSettingsTakeEffectAfterUnlockOnceBackToWorking()
    {
        var (machine, clock) = Create();
        clock.Advance(Min(30));
        machine.Update();
        clock.Advance(Min(5));
        machine.Update();
        Assert.False(machine.ApplySettings(Minutes(60, 1)));
        machine.Unlock();

        Assert.True(machine.ApplySettings(Minutes(60, 1)));
        Assert.Equal(Min(60), machine.WorkRemaining);
    }

    [Fact]
    public void ApplySettingsDoesNotRaiseStateChanged()
    {
        var (machine, clock) = Create();
        var raised = false;
        machine.StateChanged += (_, _) => raised = true;
        clock.Advance(Min(20));

        machine.ApplySettings(Minutes(15, 5));

        Assert.False(raised);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SettingsRejectNonPositiveDurations(double minutes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BreakSettings(Min(minutes), Min(5)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BreakSettings(Min(30), Min(minutes)));
    }

    [Fact]
    public void DefaultSettingsAreThirtyMinutesWorkFiveMinutesBreak()
    {
        Assert.Equal(Min(30), BreakSettings.Default.WorkInterval);
        Assert.Equal(Min(5), BreakSettings.Default.BreakDuration);
        Assert.Equal(Min(1), BreakSettings.Default.WarningLead);
    }
}
