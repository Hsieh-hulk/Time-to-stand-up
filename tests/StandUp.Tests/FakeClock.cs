using StandUp.Core;

namespace StandUp.Tests;

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan span) => UtcNow += span;
}
