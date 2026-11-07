namespace TimeToStandUp.Core;

/// <summary>
/// 「例外程式」清單的名稱規則:只比對行程名稱(不看路徑),不分大小寫。
/// 存檔與比對都用正規化後的名稱:小寫、不含路徑與 .exe,例如 <c>C:\Office\POWERPNT.EXE</c> → <c>powerpnt</c>。
/// </summary>
public static class ProcessWatchList
{
    public const int MaxEntries = 50;
    public const int MaxNameLength = 100;

    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

    /// <summary>把使用者輸入或行程名稱正規化;空白、含非法字元或過長回傳 null。</summary>
    public static string? Normalize(string? name)
    {
        if (name is null)
            return null;

        var text = name.Trim().Trim('"').Trim();
        // 不用 Path.GetFileName:測試可能在非 Windows 執行,兩種分隔符號都要處理。
        var slash = text.LastIndexOfAny(new[] { '\\', '/' });
        if (slash >= 0)
            text = text[(slash + 1)..];
        if (text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            text = text[..^4];
        text = text.Trim();

        if (text.Length == 0 || text.Length > MaxNameLength || text.IndexOfAny(InvalidChars) >= 0)
            return null;
        return text.ToLowerInvariant();
    }

    /// <summary>
    /// 正規化整份清單:丟掉不合法的名稱、去重(保留先出現的順序)、最多 <see cref="MaxEntries"/> 筆。
    /// <paramref name="dropped"/> 表示是否有名稱因不合法或超量被丟掉(重複不算)。
    /// </summary>
    public static IReadOnlyList<string> NormalizeAll(IEnumerable<string?> names, out bool dropped)
    {
        dropped = false;
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in names)
        {
            var name = Normalize(raw);
            if (name is null)
            {
                dropped = true;
                continue;
            }
            if (!seen.Add(name))
                continue;
            if (result.Count >= MaxEntries)
            {
                dropped = true;
                continue;
            }
            result.Add(name);
        }
        return result;
    }

    /// <summary>
    /// 回傳清單中第一個正在執行的程式名稱;都沒有執行時回傳 null。
    /// <paramref name="runningNames"/> 是行程名稱(例如 Process.ProcessName),會先正規化再比對。
    /// </summary>
    public static string? FindRunning(IReadOnlyList<string> watched, IEnumerable<string> runningNames)
    {
        if (watched.Count == 0)
            return null;

        var running = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in runningNames)
        {
            if (Normalize(raw) is { } name)
                running.Add(name);
        }

        foreach (var entry in watched)
        {
            if (Normalize(entry) is { } name && running.Contains(name))
                return name;
        }
        return null;
    }
}
