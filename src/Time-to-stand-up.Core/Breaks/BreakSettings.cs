namespace TimeToStandUp.Core;

/// <summary>工作間隔與休息時長。預設 30 / 5 分鐘(對應 SPEC 3.0 的 Diaz 2023)。</summary>
public sealed class BreakSettings
{
    public static readonly TimeSpan MaxWarningLead = TimeSpan.FromMinutes(1);

    /// <summary>每個工作週期最多可延後或略過幾次(兩者共用額度)。</summary>
    public const int MaxSnoozesPerCycle = 2;

    /// <summary>休息前提示上提供的延後選項。</summary>
    public static readonly IReadOnlyList<TimeSpan> SnoozeOptions =
        new[] { TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15) };

    public BreakSettings(TimeSpan workInterval, TimeSpan breakDuration)
    {
        if (workInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(workInterval), "工作間隔必須大於 0");
        if (breakDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(breakDuration), "休息時長必須大於 0");

        WorkInterval = workInterval;
        BreakDuration = breakDuration;
    }

    public static BreakSettings Default { get; } =
        new(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(5));

    public TimeSpan WorkInterval { get; }

    public TimeSpan BreakDuration { get; }

    /// <summary>休息前多久開始提示。正常為 1 分鐘;工作間隔很短時縮為間隔的一半,避免一開始就提示。</summary>
    public TimeSpan WarningLead =>
        WorkInterval / 2 < MaxWarningLead ? WorkInterval / 2 : MaxWarningLead;
}
