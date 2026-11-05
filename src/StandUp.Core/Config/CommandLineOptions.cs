using System.Globalization;

namespace StandUp.Core;

/// <summary>
/// 命令列參數:<c>--work 1 --break 0.5</c>(單位:分鐘,可為小數)會「覆寫」設定檔的值,
/// 只影響這次執行,不寫回檔案。<c>--idle</c> 同樣覆寫閒置門檻。
/// <c>--config-dir &lt;路徑&gt;</c> 改用指定資料夾存放 config.json(測試用)。
/// </summary>
public sealed class CommandLineOptions
{
    private CommandLineOptions(TimeSpan? workOverride, TimeSpan? breakOverride, TimeSpan? idleOverride, string? configDirectory)
    {
        WorkOverride = workOverride;
        BreakOverride = breakOverride;
        IdleOverride = idleOverride;
        ConfigDirectory = configDirectory;
    }

    public TimeSpan? WorkOverride { get; }

    public TimeSpan? BreakOverride { get; }

    /// <summary><c>--idle</c>:覆寫閒置門檻,只影響這次執行。</summary>
    public TimeSpan? IdleOverride { get; }

    public string? ConfigDirectory { get; }

    /// <summary>設定檔的值加上命令列覆寫後,這次執行實際使用的設定。</summary>
    public BreakSettings ResolveSettings(AppConfig config) => new(
        WorkOverride ?? config.EffectiveWorkInterval,
        BreakOverride ?? config.EffectiveBreakDuration);

    /// <summary>這次執行實際使用的閒置門檻。</summary>
    public TimeSpan ResolveIdleThreshold(AppConfig config) =>
        IdleOverride ?? config.EffectiveIdleThreshold;

    /// <summary>解析失敗時丟出 <see cref="ArgumentException"/>,訊息可直接顯示給使用者。</summary>
    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        TimeSpan? work = null;
        TimeSpan? rest = null;
        TimeSpan? idle = null;
        string? configDirectory = null;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--work":
                    work = ReadMinutes(args, ++i, "--work");
                    break;
                case "--break":
                    rest = ReadMinutes(args, ++i, "--break");
                    break;
                case "--idle":
                    idle = ReadMinutes(args, ++i, "--idle");
                    break;
                case "--config-dir":
                    if (++i >= args.Count || string.IsNullOrWhiteSpace(args[i]))
                        throw new ArgumentException("--config-dir 需要一個資料夾路徑");
                    configDirectory = args[i];
                    break;
                default:
                    throw new ArgumentException($"不認得的參數:{args[i]}");
            }
        }

        return new CommandLineOptions(work, rest, idle, configDirectory);
    }

    private static TimeSpan ReadMinutes(IReadOnlyList<string> args, int index, string name)
    {
        if (index >= args.Count)
            throw new ArgumentException($"{name} 需要一個分鐘數");

        if (!double.TryParse(args[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes)
            || double.IsNaN(minutes) || double.IsInfinity(minutes) || minutes <= 0)
            throw new ArgumentException($"{name} 的值必須是大於 0 的數字(分鐘):{args[index]}");

        return TimeSpan.FromMinutes(minutes);
    }
}
