using TimeToStandUp.Core;

namespace TimeToStandUp.Tests;

public class BreakStateMachineSnoozeTests
{
    private static readonly BreakSettings Settings = new(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(5));

    private static (BreakStateMachine Machine, FakeClock Clock) Create()
    {
        var clock = new FakeClock();
        return (new BreakStateMachine(clock, Settings), clock);
    }

    private static TimeSpan Min(double minutes) => TimeSpan.FromMinutes(minutes);

    [Fact]
    public void SnoozePushesBreakBackAndLeavesWarningWindow()
    {
        var (machine, clock) = Create();
        clock.Advance(Min(29.5));
        Assert.True(machine.IsWarning);

        Assert.True(machine.Snooze(Min(5)));

        Assert.Equal(Min(5.5), machine.WorkRemaining);
        Assert.False(machine.IsWarning);
        Assert.Equal(1, machine.SnoozesRemaining);
    }

    [Fact]
    public void SnoozedBreakStillStartsAfterTheDelay()
    {
        var (machine, clock) = Create();
        clock.Advance(Min(29.5));
        machine.Snooze(Min(5));

        clock.Advance(Min(5.4));
        machine.Update();
        Assert.Equal(BreakState.Working, machine.State);

        clock.Advance(Min(0.1));
        machine.Update();
        Assert.Equal(BreakState.BreakLocked, machine.State);
    }

    [Fact]
    public void SkipRestartsWorkTimerAndUsesQuota()
    {
        var (machine, clock) = Create();
        clock.Advance(Min(29.5));

        Assert.True(machine.SkipBreak());

        Assert.Equal(Min(30), machine.WorkRemaining);
        Assert.Equal(1, machine.SnoozesRemaining);
    }

    [Fact]
    public void ThirdSnoozeOrSkipIsRejected()
    {
        var (machine, clock) = Create();
        Assert.True(machine.Snooze(Min(5)));
        Assert.True(machine.SkipBreak());
        var before = machine.WorkRemaining;

        Assert.False(machine.Snooze(Min(5)));
        Assert.False(machine.SkipBreak());
        Assert.False(machine.CanSnooze);
        Assert.Equal(before, machine.WorkRemaining);
    }

    [Fact]
    public void UnlockRestoresQuota()
    {
        var (machine, clock) = Create();
        machine.Snooze(Min(5));
        machine.Snooze(Min(5));

        machine.StartBreakNow();
        clock.Advance(Min(5));
        Assert.True(machine.Unlock());

        Assert.Equal(BreakSettings.MaxSnoozesPerCycle, machine.SnoozesRemaining);
    }

    [Fact]
    public void ResetWorkRestoresQuota()
    {
        var (machine, _) = Create();
        machine.SkipBreak();
        machine.SkipBreak();

        machine.ResetWork();

        Assert.Equal(BreakSettings.MaxSnoozesPerCycle, machine.SnoozesRemaining);
    }

    [Fact]
    public void CannotSnoozeOrSkipDuringBreak()
    {
        var (machine, clock) = Create();
        machine.StartBreakNow();

        Assert.False(machine.Snooze(Min(5)));
        Assert.False(machine.SkipBreak());

        clock.Advance(Min(5));
        machine.Update();
        Assert.Equal(BreakState.BreakReady, machine.State);
        Assert.False(machine.Snooze(Min(5)));
        Assert.False(machine.SkipBreak());
    }
}
