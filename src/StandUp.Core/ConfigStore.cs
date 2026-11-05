using System.Text;
using System.Text.Json;

namespace StandUp.Core;

public enum ConfigLoadStatus
{
    /// <summary>檔案存在且所有欄位都正常。</summary>
    Loaded,

    /// <summary>檔案不存在,使用預設值。</summary>
    Missing,

    /// <summary>JSON 損毀,使用預設值,壞檔已改名為 config.json.bak。</summary>
    Corrupt,

    /// <summary>JSON 可讀,但有欄位超出範圍或型別不對,只有那些欄位回復預設。</summary>
    FieldsReset,

    /// <summary>檔案存在但讀不到(例如被鎖住),使用預設值,檔案原封不動。</summary>
    Unreadable,
}

public sealed record ConfigLoadResult(AppConfig Config, ConfigLoadStatus Status);

/// <summary>
/// 讀寫 config.json。讀取絕不丟例外(最差情況回預設值);
/// 損毀的檔案會改名保留而不是被悄悄蓋掉;寫入先寫暫存檔再取代。
/// </summary>
public sealed class ConfigStore
{
    public const string FileName = "config.json";
    public const string BackupFileName = "config.json.bak";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _directory;

    public ConfigStore(string directory)
    {
        _directory = directory;
    }

    /// <summary>%AppData%\StandUp</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StandUp");

    public string ConfigPath => Path.Combine(_directory, FileName);

    public string BackupPath => Path.Combine(_directory, BackupFileName);

    public ConfigLoadResult Load()
    {
        string json;
        try
        {
            if (!File.Exists(ConfigPath))
                return new(AppConfig.Default, ConfigLoadStatus.Missing);
            json = File.ReadAllText(ConfigPath, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(AppConfig.Default, ConfigLoadStatus.Unreadable);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return Corrupt();
            return Interpret(document.RootElement);
        }
        catch (JsonException)
        {
            return Corrupt();
        }
    }

    /// <summary>寫入設定。失敗(磁碟滿、沒有權限等)回傳 false,不丟例外。</summary>
    public bool Save(AppConfig config)
    {
        var temp = ConfigPath + ".tmp";
        try
        {
            Directory.CreateDirectory(_directory);
            var json = JsonSerializer.Serialize(config, WriteOptions);
            File.WriteAllText(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temp, ConfigPath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            return false;
        }
    }

    private static ConfigLoadResult Interpret(JsonElement root)
    {
        var reset = false;

        var work = ReadMinutes(root, "workMinutes", AppConfig.DefaultWorkMinutes,
            AppConfig.IsValidWorkMinutes, ref reset);
        var rest = ReadMinutes(root, "breakMinutes", AppConfig.DefaultBreakMinutes,
            AppConfig.IsValidBreakMinutes, ref reset);
        var firstRun = ReadBool(root, "firstRunCompleted", false, ref reset);

        var theme = ReadTheme(root, ref reset);

        return new(
            new AppConfig(work, rest, firstRun, theme),
            reset ? ConfigLoadStatus.FieldsReset : ConfigLoadStatus.Loaded);
    }

    // 舊版設定檔沒有 theme 欄位,視為跟隨系統;有欄位但不是已知的名稱才算被重設。
    private static AppTheme ReadTheme(JsonElement root, ref bool reset)
    {
        if (!TryFind(root, "theme", out var element))
            return AppTheme.System;

        if (element.ValueKind == JsonValueKind.String
            && Enum.TryParse<AppTheme>(element.GetString(), ignoreCase: true, out var value)
            && Enum.IsDefined(value))
            return value;

        reset = true;
        return AppTheme.System;
    }

    // 欄位不存在(例如舊版設定檔)用預設值,但不算「被重設」;存在卻不合法才算。
    private static int ReadMinutes(JsonElement root, string name, int fallback, Func<int, bool> isValid, ref bool reset)
    {
        if (!TryFind(root, name, out var element))
            return fallback;

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value) && isValid(value))
            return value;

        reset = true;
        return fallback;
    }

    private static bool ReadBool(JsonElement root, string name, bool fallback, ref bool reset)
    {
        if (!TryFind(root, name, out var element))
            return fallback;

        if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return element.GetBoolean();

        reset = true;
        return fallback;
    }

    private static bool TryFind(JsonElement root, string name, out JsonElement element)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                element = property.Value;
                return true;
            }
        }

        element = default;
        return false;
    }

    private ConfigLoadResult Corrupt()
    {
        try
        {
            File.Move(ConfigPath, BackupPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 改名失敗就算了:仍然用預設值,不閃退。
        }

        return new(AppConfig.Default, ConfigLoadStatus.Corrupt);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
