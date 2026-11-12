using TimeToStandUp.Core;

namespace TimeToStandUp.Tests;

public class AppConfigTests
{
    [Fact]
    public void EffectiveTimesComeFromMinutes()
    {
        var config = new AppConfig(30, 5, true);

        Assert.Equal(TimeSpan.FromMinutes(30), config.EffectiveWorkInterval);
        Assert.Equal(TimeSpan.FromMinutes(5), config.EffectiveBreakDuration);
        Assert.Equal(TimeSpan.FromMinutes(30), config.ToBreakSettings().WorkInterval);
    }

    [Fact]
    public void DefaultsAreThirtyAndFiveAndFirstRunNotCompleted()
    {
        Assert.Equal(30, AppConfig.Default.WorkMinutes);
        Assert.Equal(5, AppConfig.Default.BreakMinutes);
        Assert.False(AppConfig.Default.FirstRunCompleted);
        Assert.Empty(AppConfig.Default.PauseProcesses);
    }

    [Fact]
    public void EqualityComparesPauseProcessesByContent()
    {
        var a = new AppConfig(30, 5, true) { PauseProcesses = new[] { "zoom", "teams" } };
        var b = new AppConfig(30, 5, true) { PauseProcesses = new List<string> { "zoom", "teams" } };
        var c = new AppConfig(30, 5, true) { PauseProcesses = new[] { "teams", "zoom" } };

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
        Assert.NotEqual(a, new AppConfig(30, 5, true));
    }

    [Fact]
    public void WithKeepsPauseProcesses()
    {
        var config = new AppConfig(30, 5, true) { PauseProcesses = new[] { "zoom" } };

        Assert.Equal(new[] { "zoom" }, (config with { WorkMinutes = 45 }).PauseProcesses);
    }

    [Fact]
    public void PetParticipatesInEqualityAndSurvivesWith()
    {
        var config = new AppConfig(30, 5, true) { Pet = PetKind.Slime };

        Assert.NotEqual(config, new AppConfig(30, 5, true) { Pet = PetKind.Shiba });
        Assert.Equal(PetKind.Slime, (config with { WorkMinutes = 45 }).Pet);
    }

    [Theory]
    [InlineData(30, 5, ReminderPreset.Standard)]
    [InlineData(60, 10, ReminderPreset.Relaxed)]
    [InlineData(30, 6, ReminderPreset.Custom)]
    [InlineData(60, 5, ReminderPreset.Custom)]
    [InlineData(45, 5, ReminderPreset.Custom)]
    [InlineData(5, 30, ReminderPreset.Custom)]
    [InlineData(1, 1, ReminderPreset.Custom)]
    public void PresetIsDerivedFromMinutes(int work, int rest, ReminderPreset expected)
    {
        Assert.Equal(expected, AppConfig.PresetFor(work, rest));
        Assert.Equal(expected, new AppConfig(work, rest, true).Preset);
    }

    [Fact]
    public void ToBreakSettingsConvertsMinutes()
    {
        var settings = new AppConfig(45, 7, true).ToBreakSettings();

        Assert.Equal(TimeSpan.FromMinutes(45), settings.WorkInterval);
        Assert.Equal(TimeSpan.FromMinutes(7), settings.BreakDuration);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(180, true)]
    [InlineData(181, false)]
    [InlineData(-1, false)]
    public void WorkMinutesRange(int minutes, bool valid)
    {
        Assert.Equal(valid, AppConfig.IsValidWorkMinutes(minutes));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(30, true)]
    [InlineData(31, false)]
    [InlineData(-1, false)]
    public void BreakMinutesRange(int minutes, bool valid)
    {
        Assert.Equal(valid, AppConfig.IsValidBreakMinutes(minutes));
    }
}

public class MinutesInputTests
{
    [Theory]
    [InlineData("1", true, 1)]
    [InlineData("180", true, 180)]
    [InlineData("  45 ", true, 45)]
    [InlineData("007", true, 7)]
    [InlineData("0", false, 0)]
    [InlineData("181", false, 181)]
    [InlineData("-1", false, 0)]
    [InlineData("+5", false, 0)]
    [InlineData("", false, 0)]
    [InlineData("   ", false, 0)]
    [InlineData(null, false, 0)]
    [InlineData("abc", false, 0)]
    [InlineData("3.5", false, 0)]
    [InlineData("3,5", false, 0)]
    [InlineData("1e1", false, 0)]
    [InlineData("99999999999", false, 0)]
    public void WorkInputBoundaries(string? text, bool valid, int value)
    {
        var result = MinutesInput.ValidateWork(text);

        Assert.Equal(valid, result.IsValid);
        Assert.Equal(value, result.Value);
        Assert.Equal(valid, result.Error is null);
    }

    [Theory]
    [InlineData("1", true, 1)]
    [InlineData("30", true, 30)]
    [InlineData("0", false, 0)]
    [InlineData("31", false, 31)]
    [InlineData("", false, 0)]
    [InlineData("五", false, 0)]
    [InlineData("2.0", false, 0)]
    public void BreakInputBoundaries(string? text, bool valid, int value)
    {
        var result = MinutesInput.ValidateBreak(text);

        Assert.Equal(valid, result.IsValid);
        Assert.Equal(value, result.Value);
        Assert.Equal(valid, result.Error is null);
    }

    [Fact]
    public void ErrorMessagesNameTheFieldAndRange()
    {
        Assert.Contains("1", MinutesInput.ValidateWork("0").Error);
        Assert.Contains("180", MinutesInput.ValidateWork("0").Error);
        Assert.Contains("30", MinutesInput.ValidateBreak("99").Error);
        Assert.Contains("整數", MinutesInput.ValidateWork("1.5").Error);
        Assert.Contains("工作間隔", MinutesInput.ValidateWork("").Error);
        Assert.Contains("休息時長", MinutesInput.ValidateBreak("").Error);
    }
}
