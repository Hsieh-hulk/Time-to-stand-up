using StandUp.Core;

namespace StandUp.Tests;

public class CommandLineOptionsTests
{
    [Fact]
    public void NoArgumentsLeavesConfigValuesUntouched()
    {
        var options = CommandLineOptions.Parse(Array.Empty<string>());
        var config = new AppConfig(45, 10, true);

        var settings = options.ResolveSettings(config);

        Assert.Null(options.WorkOverride);
        Assert.Null(options.BreakOverride);
        Assert.Null(options.ConfigDirectory);
        Assert.Equal(TimeSpan.FromMinutes(45), settings.WorkInterval);
        Assert.Equal(TimeSpan.FromMinutes(10), settings.BreakDuration);
    }

    [Fact]
    public void ParsesWorkAndBreakMinutesIncludingFractions()
    {
        var options = CommandLineOptions.Parse(new[] { "--work", "1", "--break", "0.5" });

        Assert.Equal(TimeSpan.FromMinutes(1), options.WorkOverride);
        Assert.Equal(TimeSpan.FromSeconds(30), options.BreakOverride);
    }

    [Fact]
    public void OverridesReplaceConfigValuesForThisRun()
    {
        var options = CommandLineOptions.Parse(new[] { "--work", "1", "--break", "0.5" });

        var settings = options.ResolveSettings(AppConfig.Default);

        Assert.Equal(TimeSpan.FromMinutes(1), settings.WorkInterval);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.BreakDuration);
    }

    [Fact]
    public void OverridingOnlyWorkKeepsConfigBreak()
    {
        var options = CommandLineOptions.Parse(new[] { "--work", "2" });

        var settings = options.ResolveSettings(new AppConfig(30, 12, true));

        Assert.Equal(TimeSpan.FromMinutes(2), settings.WorkInterval);
        Assert.Equal(TimeSpan.FromMinutes(12), settings.BreakDuration);
    }

    [Fact]
    public void OverridesAreNotLimitedToConfigRanges()
    {
        // 命令列是測試用,小數與超出設定視窗範圍的值照舊可用。
        var options = CommandLineOptions.Parse(new[] { "--work", "0.25", "--break", "99" });

        var settings = options.ResolveSettings(AppConfig.Default);

        Assert.Equal(TimeSpan.FromSeconds(15), settings.WorkInterval);
        Assert.Equal(TimeSpan.FromMinutes(99), settings.BreakDuration);
    }

    [Fact]
    public void ParsesConfigDirectory()
    {
        var options = CommandLineOptions.Parse(new[] { "--config-dir", @"C:\temp\standup test" });

        Assert.Equal(@"C:\temp\standup test", options.ConfigDirectory);
        Assert.Null(options.WorkOverride);
    }

    [Theory]
    [InlineData("--work")]
    [InlineData("--work", "abc")]
    [InlineData("--break", "0")]
    [InlineData("--break", "-3")]
    [InlineData("--config-dir")]
    [InlineData("--config-dir", " ")]
    [InlineData("--unknown", "1")]
    public void InvalidArgumentsThrow(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(args));
    }
}
