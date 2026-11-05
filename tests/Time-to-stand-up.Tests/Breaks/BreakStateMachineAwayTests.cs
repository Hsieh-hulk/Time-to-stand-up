using TimeToStandUp.Core;

namespace TimeToStandUp.Tests;

/// <summary>離開相關的狀態機行為:ExcludeAway、ResetWork、Update(canInterrupt)。</summary>
public class BreakStateMachineAwayTests
{
    private static readonly BreakSettings Settings = new(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(5));

    private static (BreakStateMachine Machine, FakeClock Clock) Create()
    {
        var clock = new FakeClock();
        return (new BreakStateMachine(clock, Settings), clock);
    }

    private static TimeSpan Min(double minutes) => TimeSpan.FromMinutes(minutes);

    private static void EnterBreak(BreakStateMachine machine, FakeClock clock)
    {
        clock.Advance(Min(30));
        machine.Update();
        Assert.Equal(BreakState.BreakLocked, machine.State);
    }

    [Fact]
    public void ExcludeAwayPushesWorkRemainingBack()
    {
        var (machine, clock) = Create();
        clock.Advance(Min(20));

        Assert.True(machine.ExcludeAway(Min(4)));

        Assert.Equal(Min(14), machine.WorkRemaining);
    }

    [Fact]
    public void ExcludeAwayNeverExcludesMoreThanTheWorkedTime()
    {
        var (machine, clock) = Create();
        clock.Advance(Min(3));

        machine.ExcludeAway(Min(10));

        Assert.Equal(Min(30), machine.WorkRemaining);
        clock.Advance(Min(1));
        Assert.Equal(Min(29), machine.WorkRemaining);
    }

    [Fact]
    public void ExcludeAwayIgnoresZeroOrNegative()
    {
        var (machine, clock) = Create();
        clock.Advance(Min(10));

        Assert.False(machine.ExcludeAway(TimeSpan.Zero));
        Assert.False(machine.ExcludeAway(Min(-1)));
        Assert.Equal(Min(20), machine.WorkRemaining);
    }

    [Fact]
    public void ResetWorkRestartsTheFullInterval()
    {
        var (machine, clock) = Create();
        clock.Advance(Min(25));

        Assert.True(machine.ResetWork());

        Assert.Equal(Min(30), machine.WorkRemaining);
    }

    [Fact]
    public void ExcludeAwayAndResetWorkDoNothingDuringBreak()
    {
        var (machine, clock) = Create();
        EnterBreak(machine, clock);
        clock.Advance(Min(1));

        Assert.False(machine.ExcludeAway(Min(1)));
        Assert.False(machine.ResetWork());
        Assert.Equal(BreakState.BreakLocked, machine.State);
        Assert.Equal(Min(4), machine.BreakRemaining);
    }

    [Fact]
    public void CannotInterruptKeepsWorkingPastTheInterval()
    {
        var (machine, clock) = Create();
        clock.Advance(Min(35));

        machine.Update(canInterrupt: false);

        Assert.Equal(BreakState.Working, machine.State);
        Assert.Equal(TimeSpan.Zero, machine.WorkRemaining);

        machine.Update();
        Assert.Equal(BreakState.BreakLocked, machine.State);
    }

    [Fact]
    public void CannotInterruptDoesNotStopTheBreakCountdown()
    {
        var (machine, clock) = Create();
        EnterBreak(machine, clock);
        clock.Advance(Min(5));

        machine.Update(canInterrupt: false);

        Assert.Equal(BreakState.BreakReady, machine.State);
    }

    [Fact]
    public void LongSleepDuringBreakStillNeedsUnlockAndWorkStartsAtUnlock()
    {
        // 使用者的決定:休息中離開(吃飯、午休)回來仍要按解鎖,工作計時從按下解鎖才開始。
        var (machine, clock) = Create();
        EnterBreak(machine, clock);

        clock.Advance(TimeSpan.FromHours(2));
        machine.Update();
        Assert.Equal(BreakState.BreakReady, machine.State);

        clock.Advance(Min(3));
        Assert.True(machine.Unlock());
        Assert.Equal(BreakState.Working, machine.State);
        Assert.Equal(Min(30), machine.WorkRemaining);
    }
}
