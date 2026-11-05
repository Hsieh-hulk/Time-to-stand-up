using System.Globalization;

namespace StandUp.Core;

/// <summary>設定視窗的分鐘輸入驗證:只接受範圍內的整數。</summary>
public readonly record struct MinutesInput(bool IsValid, int Value, string? Error)
{
    public static MinutesInput ValidateWork(string? text) =>
        Validate(text, "工作間隔", AppConfig.MinWorkMinutes, AppConfig.MaxWorkMinutes);

    public static MinutesInput ValidateBreak(string? text) =>
        Validate(text, "休息時長", AppConfig.MinBreakMinutes, AppConfig.MaxBreakMinutes);

    public static MinutesInput Validate(string? text, string label, int min, int max)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return new(false, 0, $"請輸入{label}(分鐘)");

        // NumberStyles.None:只接受 0–9 的數字,不接受正負號、小數點或千分位。
        if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            return new(false, 0, $"{label}必須是整數分鐘");

        if (value < min || value > max)
            return new(false, value, $"{label}必須介於 {min} 到 {max} 分鐘");

        return new(true, value, null);
    }
}
