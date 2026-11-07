using TimeToStandUp.Core;

namespace TimeToStandUp.Tests;

public class ProcessWatchListTests
{
    [Theory]
    [InlineData("powerpnt", "powerpnt")]
    [InlineData("POWERPNT.EXE", "powerpnt")]
    [InlineData("  PowerPnt.exe  ", "powerpnt")]
    [InlineData(@"C:\Program Files\Microsoft Office\POWERPNT.EXE", "powerpnt")]
    [InlineData("\"C:\\x\\Foo.EXE\"", "foo")]
    [InlineData("C:/tools/obs64.exe", "obs64")]
    [InlineData("Zoom Workplace", "zoom workplace")]
    [InlineData("my.app.exe", "my.app")]
    public void NormalizeStripsPathExtensionAndCase(string input, string expected)
    {
        Assert.Equal(expected, ProcessWatchList.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".exe")]
    [InlineData(@"C:\folder\")]
    [InlineData("bad|name")]
    [InlineData("what?")]
    [InlineData("a<b")]
    public void NormalizeRejectsEmptyOrInvalidNames(string? input)
    {
        Assert.Null(ProcessWatchList.Normalize(input));
    }

    [Fact]
    public void NormalizeRejectsOverlongNames()
    {
        Assert.NotNull(ProcessWatchList.Normalize(new string('a', ProcessWatchList.MaxNameLength)));
        Assert.Null(ProcessWatchList.Normalize(new string('a', ProcessWatchList.MaxNameLength + 1)));
    }

    [Fact]
    public void NormalizeAllDeduplicatesKeepingOrderAndReportsDrops()
    {
        var list = ProcessWatchList.NormalizeAll(new[] { "Zoom.exe", "teams", "ZOOM", "bad|x" }, out var dropped);

        Assert.Equal(new[] { "zoom", "teams" }, list);
        Assert.True(dropped);
    }

    [Fact]
    public void NormalizeAllDuplicatesAloneAreNotDrops()
    {
        ProcessWatchList.NormalizeAll(new[] { "zoom", "Zoom.exe" }, out var dropped);

        Assert.False(dropped);
    }

    [Fact]
    public void FindRunningMatchesCaseInsensitively()
    {
        var watched = new[] { "powerpnt", "zoom" };

        Assert.Equal("zoom", ProcessWatchList.FindRunning(watched, new[] { "explorer", "Zoom", "devenv" }));
        Assert.Equal("powerpnt", ProcessWatchList.FindRunning(watched, new[] { "POWERPNT" }));
    }

    [Fact]
    public void FindRunningReturnsNullWhenNothingMatches()
    {
        Assert.Null(ProcessWatchList.FindRunning(new[] { "powerpnt" }, new[] { "explorer", "powerpoint" }));
        Assert.Null(ProcessWatchList.FindRunning(Array.Empty<string>(), new[] { "explorer" }));
    }
}
