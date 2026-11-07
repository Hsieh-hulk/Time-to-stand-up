using TimeToStandUp.Core;

namespace TimeToStandUp.Tests;

public class ProcessPauseGateTests
{
    private static readonly BreakSettings Settings = new(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(5));

    private sealed class Fixture
    {
        public FakeClock Clock { get; } = new();
        public BreakStateMachine Machine { get; }
        public ProcessPauseGate Gate { get; }
        public List<bool> Changes { get; } = new();

        public Fixture()
        {
            Machine = new BreakStateMachine(Clock, Settings);
            Gate = new ProcessPauseGate(Machine);
            Gate.PausedChanged += paused => Changes.Add(paused);
        }

        /// <summary>模擬 AppController 的每秒 tick:先更新閘門,再推進狀態機。</summary>
        public void Tick(bool running, TimeSpan duration)
        {
            var end = Clock.UtcNow + duration;
            while (Clock.UtcNow < end)
            {
                Clock.Advance(TimeSpan.FromSeconds(1));
                Gate.Update(running);
                Machine.Update(canInterrupt: !Gate.IsPaused);
            }
        }
    }

    private static TimeSpan Min(double minutes) => TimeSpan.FromMinutes(minutes);

    [Fact]
    public void RunningExemptProcessKeepsWorkFromAccumulating()
    {
        var f = new Fixture();

        f.Tick(running: true, Min(45));

        Assert.True(f.Gate.IsPaused);
        Assert.Equal(BreakState.Working, f.Machine.State);
        Assert.Equal(Min(30), f.Machine.WorkRemaining);
        Assert.False(f.Machine.IsWarning);
    }

    [Fact]
    public void ClosingTheProcessRestartsWorkFromZero()
    {
        var f = new Fixture();
        f.Tick(running: false, Min(20));
        f.Tick(running: true, Min(10));

        f.Tick(running: false, Min(29));
        Assert.Equal(BreakState.Working, f.Machine.State);
        Assert.False(f.Gate.IsPaused);

        f.Tick(running: false, Min(1));
        Assert.Equal(BreakState.BreakLocked, f.Machine.State);
    }

    [Fact]
    public void WorkAccumulatedBeforeTheProcessStartedIsDiscarded()
    {
        var f = new Fixture();
        f.Tick(running: false, Min(29.5));
        Assert.True(f.Machine.IsWarning);

        f.Tick(running: true, TimeSpan.FromSeconds(1));

        Assert.False(f.Machine.IsWarning);
        Assert.Equal(Min(30), f.Machine.WorkRemaining);
    }

    [Fact]
    public void PausedChangedFiresOnlyOnTransitions()
    {
        var f = new Fixture();

        f.Tick(running: true, Min(1));
        f.Tick(running: false, Min(1));
        f.Tick(running: true, Min(1));

        Assert.Equal(new[] { true, false, true }, f.Changes);
    }

    [Fact]
    public void ProcessStartedDuringBreakDoesNotShortenTheBreak()
    {
        var f = new Fixture();
        f.Machine.StartBreakNow();

        f.Tick(running: true, Min(2));

        Assert.False(f.Gate.IsPaused);
        Assert.Equal(BreakState.BreakLocked, f.Machine.State);
        Assert.Equal(Min(3), f.Machine.BreakRemaining);

        f.Tick(running: true, Min(3));
        Assert.Equal(BreakState.BreakReady, f.Machine.State);
    }

    [Fact]
    public void PauseStartsAfterUnlockIfTheProcessIsStillRunning()
    {
        var f = new Fixture();
        f.Machine.StartBreakNow();
        f.Tick(running: true, Min(5));
        Assert.True(f.Machine.Unlock());

        f.Tick(running: true, Min(40));

        Assert.True(f.Gate.IsPaused);
        Assert.Equal(BreakState.Working, f.Machine.State);
        Assert.Equal(Min(30), f.Machine.WorkRemaining);
    }

    [Fact]
    public void PauseRestoresSnoozeAllowance()
    {
        var f = new Fixture();
        f.Tick(running: false, Min(29.5));
        Assert.True(f.Machine.SkipBreak());
        Assert.True(f.Machine.SkipBreak());
        Assert.Equal(0, f.Machine.SnoozesRemaining);

        f.Tick(running: true, TimeSpan.FromSeconds(1));

        Assert.Equal(BreakSettings.MaxSnoozesPerCycle, f.Machine.SnoozesRemaining);
    }

    [Fact]
    public void WorksAlongsidePresenceTracker()
    {
        var f = new Fixture();
        var tracker = new PresenceTracker(f.Clock, f.Machine);
        f.Tick(running: true, Min(5));

        tracker.SetAway(AwayReason.Locked, true);
        f.Clock.Advance(Min(2));
        tracker.SetAway(AwayReason.Locked, false);
        f.Tick(running: true, Min(1));

        Assert.Equal(Min(30), f.Machine.WorkRemaining);

        f.Tick(running: false, Min(10));
        Assert.Equal(Min(20), f.Machine.WorkRemaining);
    }
}
