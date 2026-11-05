using System.Text.Json.Serialization;

namespace TimeToStandUp.Core;

/// <summary>設定視窗中三個預設選項。選項不另存,由分鐘數推導。</summary>
public enum ReminderPreset
{
    /// <summary>工作 30 分鐘 / 休息 5 分鐘(建議,對應 SPEC 3.0 的研究)。</summary>
    Standard,

    /// <summary>工作 60 分鐘 / 休息 10 分鐘。</summary>
    Relaxed,

    Custom,
}

/// <summary>
/// 寫進 config.json 的設定。分鐘數一律是整數。
/// <paramref name="SmartPause"/>:鎖定、睡眠時不計入工作時間(不偵測鍵鼠閒置,看影片時不會被誤判)。
/// </summary>
public sealed record AppConfig(
    int WorkMinutes,
    int BreakMinutes,
    bool FirstRunCompleted,
    AppTheme Theme = AppTheme.System,
    bool SmartPause = true)
{
    // 測試時要縮短時間,用命令列 --work / --break(可為小數分鐘),不寫回設定檔。

    [JsonIgnore]
    public TimeSpan EffectiveWorkInterval => TimeSpan.FromMinutes(WorkMinutes);

    [JsonIgnore]
    public TimeSpan EffectiveBreakDuration => TimeSpan.FromMinutes(BreakMinutes);

    public const int MinWorkMinutes = 1;
    public const int MaxWorkMinutes = 180;
    public const int MinBreakMinutes = 1;
    public const int MaxBreakMinutes = 30;

    public const int DefaultWorkMinutes = 30;
    public const int DefaultBreakMinutes = 5;

    public static AppConfig Default { get; } = new(DefaultWorkMinutes, DefaultBreakMinutes, false);

    public static bool IsValidWorkMinutes(int minutes) => minutes is >= MinWorkMinutes and <= MaxWorkMinutes;

    public static bool IsValidBreakMinutes(int minutes) => minutes is >= MinBreakMinutes and <= MaxBreakMinutes;

    /// <summary>目前的分鐘數對應哪個預設選項:30/5 = 標準、60/10 = 較寬鬆、其他 = 自訂。</summary>
    [JsonIgnore]
    public ReminderPreset Preset => PresetFor(WorkMinutes, BreakMinutes);

    public static ReminderPreset PresetFor(int workMinutes, int breakMinutes) => (workMinutes, breakMinutes) switch
    {
        (30, 5) => ReminderPreset.Standard,
        (60, 10) => ReminderPreset.Relaxed,
        _ => ReminderPreset.Custom,
    };

    public BreakSettings ToBreakSettings() => new(EffectiveWorkInterval, EffectiveBreakDuration);
}
