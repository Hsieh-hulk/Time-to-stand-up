using TimeToStandUp.Core;

namespace TimeToStandUp.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "Time-to-stand-upTests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private ConfigStore Store => new(_dir);

    private string WriteRaw(string json)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, ConfigStore.FileName);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void MissingFileGivesDefaultsWithoutCreatingAnything()
    {
        var result = Store.Load();

        Assert.Equal(ConfigLoadStatus.Missing, result.Status);
        Assert.Equal(AppConfig.Default, result.Config);
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public void SaveThenLoadRoundTrips()
    {
        var config = new AppConfig(45, 12, true);

        Assert.True(Store.Save(config));
        var result = Store.Load();

        Assert.Equal(ConfigLoadStatus.Loaded, result.Status);
        Assert.Equal(config, result.Config);
    }

    [Fact]
    public void SaveCreatesMissingDirectoryAndLeavesNoTempFile()
    {
        Store.Save(AppConfig.Default);

        Assert.True(File.Exists(Path.Combine(_dir, ConfigStore.FileName)));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void SaveOverwritesExistingFile()
    {
        Store.Save(new AppConfig(10, 2, false));
        Store.Save(new AppConfig(20, 3, true));

        Assert.Equal(new AppConfig(20, 3, true), Store.Load().Config);
    }

    [Fact]
    public void SaveWritesIndentedCamelCaseJson()
    {
        Store.Save(new AppConfig(60, 5, true));

        var text = File.ReadAllText(Path.Combine(_dir, ConfigStore.FileName));
        Assert.Contains("\"workMinutes\": 60", text);
        Assert.Contains("\"breakMinutes\": 5", text);
        Assert.Contains("\"firstRunCompleted\": true", text);
        Assert.Contains(Environment.NewLine, text.TrimEnd());
    }

    [Fact]
    public void SaveWritesOnlyTheStoredFieldsAndNoDerivedPreset()
    {
        Store.Save(new AppConfig(60, 5, true));

        using var document = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(_dir, ConfigStore.FileName)));
        var names = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        Assert.Equal(
            new[] { "workMinutes", "breakMinutes", "firstRunCompleted", "theme", "smartPause", "pauseProcesses", "animation" },
            names);
    }

    [Fact]
    public void PauseProcessesRoundTrip()
    {
        var config = new AppConfig(30, 5, true) { PauseProcesses = new[] { "powerpnt", "zoom" } };

        Assert.True(Store.Save(config));
        var result = Store.Load();

        Assert.Equal(ConfigLoadStatus.Loaded, result.Status);
        Assert.Equal(config, result.Config);
        Assert.Equal(new[] { "powerpnt", "zoom" }, result.Config.PauseProcesses);
    }

    [Fact]
    public void OldFileWithoutPauseProcessesGivesEmptyListWithoutCountingAsReset()
    {
        WriteRaw("{\"workMinutes\": 40, \"breakMinutes\": 6, \"firstRunCompleted\": true, \"smartPause\": true}");

        var result = Store.Load();

        Assert.Equal(ConfigLoadStatus.Loaded, result.Status);
        Assert.Empty(result.Config.PauseProcesses);
    }

    [Fact]
    public void PauseProcessesAreNormalizedAndDuplicatesMergedWithoutCountingAsReset()
    {
        WriteRaw("{\"workMinutes\": 40, \"breakMinutes\": 6, \"firstRunCompleted\": true, "
            + "\"pauseProcesses\": [\"C:\\\\Office\\\\POWERPNT.EXE\", \"powerpnt\", \"Zoom.exe\"]}");

        var result = Store.Load();

        Assert.Equal(ConfigLoadStatus.Loaded, result.Status);
        Assert.Equal(new[] { "powerpnt", "zoom" }, result.Config.PauseProcesses);
    }

    [Theory]
    [InlineData("\"notepad\"")]
    [InlineData("{\"a\": 1}")]
    [InlineData("[\"notepad\", 3]")]
    [InlineData("[null]")]
    public void WrongTypedPauseProcessesFallBackToEmptyAlone(string json)
    {
        WriteRaw("{\"workMinutes\": 40, \"breakMinutes\": 6, \"firstRunCompleted\": true, \"pauseProcesses\": " + json + "}");

        var result = Store.Load();

        Assert.Equal(ConfigLoadStatus.FieldsReset, result.Status);
        Assert.Equal(new AppConfig(40, 6, true), result.Config);
    }

    [Fact]
    public void InvalidPauseProcessNamesAreDroppedAndOthersKept()
    {
        WriteRaw("{\"workMinutes\": 40, \"breakMinutes\": 6, \"firstRunCompleted\": true, "
            + "\"pauseProcesses\": [\"\", \"bad|name\", \"teams\"]}");

        var result = Store.Load();

        Assert.Equal(ConfigLoadStatus.FieldsReset, result.Status);
        Assert.Equal(new[] { "teams" }, result.Config.PauseProcesses);
    }

    [Fact]
    public void PauseProcessesBeyondTheLimitAreTruncated()
    {
        var names = Enumerable.Range(0, ProcessWatchList.MaxEntries + 5).Select(i => $"\"app{i}\"");
        WriteRaw("{\"workMinutes\": 40, \"breakMinutes\": 6, \"firstRunCompleted\": true, "
            + "\"pauseProcesses\": [" + string.Join(", ", names) + "]}");

        var result = Store.Load();

        Assert.Equal(ConfigLoadStatus.FieldsReset, result.Status);
        Assert.Equal(ProcessWatchList.MaxEntries, result.Config.PauseProcesses.Count);
        Assert.Equal("app0", result.Config.PauseProcesses[0]);
    }

    [Fact]
    public void LegacyDevFieldsAreIgnored()
    {
        // 舊版有開發者模式;這些欄位現在是未知欄位,讀取時忽略,不算被重設。
        WriteRaw("{\"workMinutes\": 40, \"breakMinutes\": 6, \"firstRunCompleted\": true, "
            + "\"devMode\": true, \"devWorkSeconds\": 20, \"devBreakSeconds\": \"x\"}");

        var result = Store.Load();

        Assert.Equal(ConfigLoadStatus.Loaded, result.Status);
        Assert.Equal(new AppConfig(40, 6, true), result.Config);
    }

    [Fact]
    public void SmartPauseRoundTrip()
    {
        var config = new AppConfig(30, 5, true, SmartPause: false);

        Store.Save(config);

        Assert.Equal(config, Store.Load().Config);
    }

    [Fact]
    public void OldFileWithoutSmartPauseFieldsUsesDefaultsWithoutCountingAsReset()
    {
        WriteRaw("{\"workMinutes\": 40, \"breakMinutes\": 6, \"firstRunCompleted\": true, \"theme\": \"Dark\"}");

        var result = Store.Load();

        Assert.Equal(ConfigLoadStatus.Loaded, result.Status);
        Assert.True(result.Config.SmartPause);
    }

    [Fact]
    public void LegacyIdleMinutesFieldIsIgnored()
    {
        WriteRaw("{\"workMinutes\": 40, \"breakMinutes\": 6, \"firstRunCompleted\": true, \"idleMinutes\": 99}");

        var result = Store.Load();

        Assert.Equal(ConfigLoadStatus.Loaded, result.Status);
        Assert.Equal(new AppConfig(40, 6, true), result.Config);
    }

    [Theory]
    [InlineData("\"smartPause\": \"no\"")]
    public void InvalidSmartPauseFieldsFallBackAlone(string field)
    {
        WriteRaw("{\"workMinutes\": 40, \"breakMinutes\": 6, \"firstRunCompleted\": true, " + field + "}");

        var result = Store.Load();

        Assert.Equal(ConfigLoadStatus.FieldsReset, result.Status);
        Assert.Equal(new AppConfig(40, 6, true), result.Config);
    }

    [Theory]
    [InlineData(AppTheme.System)]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Light)]
    public void ThemeRoundTripsAsAReadableName(AppTheme theme)
    {
        Store.Save(new AppConfig(30, 5, true, theme));

        var text = File.ReadAllText(Path.Combine(_dir, ConfigStore.FileName));
        Assert.Contains($"\"theme\": \"{theme}\"", text);
        Assert.Equal(theme, Store.Load().Config.Theme);
    }

    [Fact]
    public void OldFileWithoutThemeFollowsSystemWithoutCountingAsReset()
    {
        WriteRaw("{\"workMinutes\": 40, \"breakMinutes\": 6, \"firstRunCompleted\": true}");

        var result = Store.Load();

        Assert.Equal(ConfigLoadStatus.Loaded, result.Status);
        Assert.Equal(AppTheme.System, result.Config.Theme);
        Assert.Equal(40, result.Config.WorkMinutes);
    }

    [Theory]
    [InlineData("\"purple\"")]
    [InlineData("7")]
    [InlineData("\"5\"")]
    public void UnknownThemeFallsBackToSystemAndKeepsOtherFields(string themeJson)
    {
        WriteRaw("{\"workMinutes\": 40, \"breakMinutes\": 6, \"firstRunCompleted\": true, \"theme\": " + themeJson + "}");

        var result = Store.Load();

        Assert.Equal(ConfigLoadStatus.FieldsReset, result.Status);
        Assert.Equal(new AppConfig(40, 6, true, AppTheme.System), result.Config);
    }

    [Theory]
    [InlineData(BreakAnimation.None)]
    [InlineData(BreakAnimation.Breathing)]
    [InlineData(BreakAnimation.Starfield)]
    [InlineData(BreakAnimation.Waves)]
    [InlineData(BreakAnimation.Stretch)]
    public void AnimationRoundTripsAsAReadableName(BreakAnimation animation)
    {
        Store.Save(new AppConfig(30, 5, true) { Animation = animation });

        var text = File.ReadAllText(Path.Combine(_dir, ConfigStore.FileName));
        Assert.Contains($"\"animation\": \"{animation}\"", text);
        Assert.Equal(animation, Store.Load().Config.Animation);
    }

    [Fact]
    public void OldFileWithoutAnimationUsesDefaultWithoutCountingAsReset()
    {
        WriteRaw("{\"workMinutes\": 40, \"breakMinutes\": 6, \"firstRunCompleted\": true}");

        var result = Store.Load();

        Assert.Equal(ConfigLoadStatus.Loaded, result.Status);
        Assert.Equal(AppConfig.DefaultAnimation, result.Config.Animation);
    }

    [Theory]
    [InlineData("\"fireworks\"")]
    [InlineData("7")]
    [InlineData("\"5\"")]
    public void UnknownAnimationFallsBackToDefaultAndKeepsOtherFields(string animationJson)
    {
        WriteRaw("{\"workMinutes\": 40, \"breakMinutes\": 6, \"firstRunCompleted\": true, \"animation\": " + animationJson + "}");

        var result = Store.Load();

        Assert.Equal(ConfigLoadStatus.FieldsReset, result.Status);
        Assert.Equal(new AppConfig(40, 6, true), result.Config);
    }

    [Fact]
    public void SaveFailureReturnsFalseInsteadOfThrowing()
    {
        // 資料夾路徑被一個檔案佔住,無法建立資料夾。
        var blocker = Path.GetTempFileName();
        try
        {
            var store = new ConfigStore(Path.Combine(blocker, "sub"));
            Assert.False(store.Save(AppConfig.Default));
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    [InlineData("{\"workMinutes\": 30,")]
    [InlineData("[1, 2, 3]")]
    [InlineData("\"hello\"")]
    [InlineData("42")]
    public void CorruptFileGivesDefaultsAndIsRenamedToBak(string content)
    {
        var path = WriteRaw(content);

        var result = Store.Load();

        Assert.Equal(ConfigLoadStatus.Corrupt, result.Status);
        Assert.Equal(AppConfig.Default, result.Config);
        Assert.False(File.Exists(path));
        Assert.Equal(content, File.ReadAllText(Path.Combine(_dir, ConfigStore.BackupFileName)));
    }

    [Fact]
    public void CorruptFileReplacesAnOlderBackup()
    {
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(_dir).FullName, ConfigStore.BackupFileName), "old");
        WriteRaw("{ broken");

        Store.Load();

        Assert.Equal("{ broken", File.ReadAllText(Path.Combine(_dir, ConfigStore.BackupFileName)));
    }

    [Fact]
    public void CanSaveNormallyAfterCorruptFileWasMovedAside()
    {
        WriteRaw("{ broken");
        Store.Load();

        Assert.True(Store.Save(new AppConfig(25, 4, true)));
        Assert.Equal(new AppConfig(25, 4, true), Store.Load().Config);
    }

    [Theory]
    [InlineData("{\"workMinutes\": 0, \"breakMinutes\": 10, \"firstRunCompleted\": true}", 30, 10, true)]
    [InlineData("{\"workMinutes\": 181, \"breakMinutes\": 10, \"firstRunCompleted\": true}", 30, 10, true)]
    [InlineData("{\"workMinutes\": -5, \"breakMinutes\": 10, \"firstRunCompleted\": true}", 30, 10, true)]
    [InlineData("{\"workMinutes\": 90, \"breakMinutes\": 0, \"firstRunCompleted\": true}", 90, 5, true)]
    [InlineData("{\"workMinutes\": 90, \"breakMinutes\": 31, \"firstRunCompleted\": true}", 90, 5, true)]
    public void OutOfRangeFieldFallsBackAloneAndOthersAreKept(string json, int work, int rest, bool firstRun)
    {
        WriteRaw(json);

        var result = Store.Load();

        Assert.Equal(ConfigLoadStatus.FieldsReset, result.Status);
        Assert.Equal(new AppConfig(work, rest, firstRun), result.Config);
        Assert.True(File.Exists(Path.Combine(_dir, ConfigStore.FileName)));
        Assert.False(File.Exists(Path.Combine(_dir, ConfigStore.BackupFileName)));
    }

    [Theory]
    [InlineData("{\"workMinutes\": \"45\", \"breakMinutes\": 10, \"firstRunCompleted\": true}", 30, 10, true)]
    [InlineData("{\"workMinutes\": 45.5, \"breakMinutes\": 10, \"firstRunCompleted\": true}", 30, 10, true)]
    [InlineData("{\"workMinutes\": 45, \"breakMinutes\": null, \"firstRunCompleted\": true}", 45, 5, true)]
    [InlineData("{\"workMinutes\": 45, \"breakMinutes\": [1], \"firstRunCompleted\": true}", 45, 5, true)]
    [InlineData("{\"workMinutes\": 45, \"breakMinutes\": 10, \"firstRunCompleted\": \"yes\"}", 45, 10, false)]
    [InlineData("{\"workMinutes\": 45, \"breakMinutes\": 10, \"firstRunCompleted\": 1}", 45, 10, false)]
    public void WrongTypedFieldFallsBackAloneAndOthersAreKept(string json, int work, int rest, bool firstRun)
    {
        WriteRaw(json);

        var result = Store.Load();

        Assert.Equal(ConfigLoadStatus.FieldsReset, result.Status);
        Assert.Equal(new AppConfig(work, rest, firstRun), result.Config);
    }

    [Fact]
    public void BoundaryValuesAreAccepted()
    {
        WriteRaw("{\"workMinutes\": 180, \"breakMinutes\": 30, \"firstRunCompleted\": true}");
        Assert.Equal(new AppConfig(180, 30, true), Store.Load().Config);

        WriteRaw("{\"workMinutes\": 1, \"breakMinutes\": 1, \"firstRunCompleted\": false}");
        Assert.Equal(new AppConfig(1, 1, false), Store.Load().Config);
    }

    [Fact]
    public void UnknownFieldsAreIgnored()
    {
        WriteRaw("{\"workMinutes\": 40, \"breakMinutes\": 8, \"firstRunCompleted\": true, "
                 + "\"futureThing\": {\"a\": [1, 2]}, \"language\": \"zh\"}");

        var result = Store.Load();

        Assert.Equal(ConfigLoadStatus.Loaded, result.Status);
        Assert.Equal(new AppConfig(40, 8, true), result.Config);
    }

    [Fact]
    public void MissingFieldsUseDefaultsWithoutCountingAsReset()
    {
        WriteRaw("{\"workMinutes\": 40}");

        var result = Store.Load();

        Assert.Equal(ConfigLoadStatus.Loaded, result.Status);
        Assert.Equal(new AppConfig(40, 5, false), result.Config);
    }

    [Fact]
    public void FieldNamesAreMatchedCaseInsensitively()
    {
        WriteRaw("{\"WorkMinutes\": 40, \"BREAKMINUTES\": 8, \"FirstRunCompleted\": true}");

        Assert.Equal(new AppConfig(40, 8, true), Store.Load().Config);
    }

    [Fact]
    public void FileWithUtf8BomIsRead()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(
            Path.Combine(_dir, ConfigStore.FileName),
            "{\"workMinutes\": 40, \"breakMinutes\": 8, \"firstRunCompleted\": true}",
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Assert.Equal(new AppConfig(40, 8, true), Store.Load().Config);
    }

    [Fact]
    public void LockedFileGivesDefaultsAndIsLeftAlone()
    {
        var path = WriteRaw("{\"workMinutes\": 40, \"breakMinutes\": 8, \"firstRunCompleted\": true}");

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = Store.Load();

            Assert.Equal(ConfigLoadStatus.Unreadable, result.Status);
            Assert.Equal(AppConfig.Default, result.Config);
        }

        Assert.True(File.Exists(path));
        Assert.Equal(new AppConfig(40, 8, true), Store.Load().Config);
    }
}
