using TimeToStandUp.Core;

namespace TimeToStandUp.Tests;

public class PetWorldTests
{
    [Fact]
    public void ScreensAreLaidEndToEndInReferenceUnits()
    {
        // 2560×1440 與 1920×1080 都是 16:9,換成 900 高後一樣長。
        var world = new PetWorld(new[] { PetWorld.ScreenLength(2560, 1440), PetWorld.ScreenLength(1920, 1080) });

        Assert.Equal(1600, world.ScreenLengths[0], 6);
        Assert.Equal(1600, world.ScreenLengths[1], 6);
        Assert.Equal(0, world.ScreenStart(0));
        Assert.Equal(1600, world.ScreenStart(1), 6);
        Assert.Equal(3200, world.Length, 6);
    }

    [Fact]
    public void WithPlatformsKeepsScreens()
    {
        var world = new PetWorld(new[] { 1600.0 }).WithPlatforms(new[] { new PetPlatform(700, 900, 300) });

        Assert.Single(world.Platforms);
        Assert.Equal(1600, world.Length);
    }
}

public class PetBrainTests
{
    private const double Dt = 1.0 / 30;
    private static readonly PetProfile Profile = PetProfile.For(PetKind.Cat);

    private static PetWorld TwoScreens(bool withPlatforms = true) => new(
        new[] { 1600.0, 1600.0 },
        withPlatforms
            ? new[] { new PetPlatform(708, 892, 296), new PetPlatform(650, 950, 640) }
            : null);

    private static PetBrain Create(PetWorld world, int seed = 1, double startX = 400) =>
        new(world, Profile, new Random(seed), startX);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(42)]
    public void StaysInsideTheWorldAndNeverSinksBelowGround(int seed)
    {
        var world = TwoScreens();
        var brain = Create(world, seed);

        for (var i = 0; i < 30 * 60 * 10; i++) // 10 分鐘
        {
            brain.Update(Dt);

            Assert.InRange(brain.X, 0, world.Length);
            Assert.True(brain.Y <= PetWorld.GroundY + 1e-6, $"sank to {brain.Y}");
            Assert.True(double.IsFinite(brain.Y));
            Assert.Contains(brain.Facing, new[] { -1, 1 });
        }
    }

    [Theory]
    [InlineData(PetKind.Cat)]
    [InlineData(PetKind.Shiba)]
    [InlineData(PetKind.Slime)]
    [InlineData(PetKind.SandSprite)]
    public void EveryPetProfileStaysInsideTheWorldAndUsesOnlyItsOwnAnimations(PetKind kind)
    {
        var profile = PetProfile.For(kind);
        var allowed = PetProfile.BaseAnimations.Concat(profile.Specials.Select(s => s.Animation)).ToHashSet();
        var world = TwoScreens();
        var brain = new PetBrain(world, profile, new Random(11), startX: 400);
        var seen = new HashSet<string>();

        for (var i = 0; i < 30 * 60 * 10; i++) // 10 分鐘
        {
            brain.Update(Dt);

            Assert.InRange(brain.X, 0, world.Length);
            Assert.True(brain.Y <= PetWorld.GroundY + 1e-6, $"{kind} sank to {brain.Y}");
            Assert.Contains(brain.Animation, allowed);
            seen.Add(brain.Animation);
        }

        // 每個特殊動作在 10 分鐘內都該出現;不睡覺的寵物不會播 sleep。
        foreach (var special in profile.Specials)
            Assert.Contains(special.Animation, seen);
        if (!profile.Sleeps)
            Assert.DoesNotContain("sleep", seen);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void RestsOnlyOnGroundOrOnAPlatformTop(int seed)
    {
        var world = TwoScreens();
        var brain = Create(world, seed);

        for (var i = 0; i < 30 * 60 * 10; i++)
        {
            brain.Update(Dt);
            if (brain.Activity is PetActivity.Hop or PetActivity.Jump)
                continue;

            if (brain.OnPlatform)
                Assert.Contains(world.Platforms, p => Math.Abs(p.Top - brain.Y) < 1e-6 && brain.X >= p.Left && brain.X <= p.Right);
            else
                Assert.Equal(PetWorld.GroundY, brain.Y, 6);
        }
    }

    [Fact]
    public void WithinTenMinutesItRoamsBothScreensAndDoesEverything()
    {
        var world = TwoScreens();
        var brain = Create(world, seed: 5);
        var seen = new HashSet<PetActivity>();
        var animations = new HashSet<string>();
        var leftScreen = false;
        var rightScreen = false;
        var visitedPlatform = false;

        for (var i = 0; i < 30 * 60 * 10; i++)
        {
            brain.Update(Dt);
            seen.Add(brain.Activity);
            animations.Add(brain.Animation);
            leftScreen |= brain.X < 1600;
            rightScreen |= brain.X > 1600;
            visitedPlatform |= brain.OnPlatform;
        }

        Assert.True(leftScreen && rightScreen, "should cross between screens");
        Assert.True(visitedPlatform, "should jump onto a platform");
        foreach (var activity in new[]
                 {
                     PetActivity.Idle, PetActivity.Walk, PetActivity.Run, PetActivity.Hop,
                     PetActivity.Jump, PetActivity.Sit, PetActivity.Special,
                 })
            Assert.Contains(activity, seen);
        Assert.Contains("stretch", animations);
        Assert.Contains("yawn", animations);
    }

    [Fact]
    public void SameSeedReproducesTheSamePath()
    {
        var a = Create(TwoScreens(), seed: 9);
        var b = Create(TwoScreens(), seed: 9);

        for (var i = 0; i < 3000; i++)
        {
            a.Update(Dt);
            b.Update(Dt);
        }

        Assert.Equal(a.X, b.X);
        Assert.Equal(a.Y, b.Y);
        Assert.Equal(a.Animation, b.Animation);
    }

    [Fact]
    public void TimedAnimationsReportProgressAndLoopsDoNot()
    {
        var brain = Create(TwoScreens(), seed: 3);

        for (var i = 0; i < 30 * 60 * 5; i++)
        {
            brain.Update(Dt);
            if (brain.Activity is PetActivity.Hop or PetActivity.Jump or PetActivity.Special)
                Assert.InRange(brain.AnimationProgress ?? -1, 0, 1);
            else
                Assert.Null(brain.AnimationProgress);
        }
    }

    [Fact]
    public void NoticesANearbyCursorAndWalksOverToSitFacingIt()
    {
        var world = TwoScreens(withPlatforms: false);
        var brain = Create(world, seed: 1, startX: 400);
        var cursor = (X: 600.0, Y: 800.0);

        var becameCurious = false;
        for (var i = 0; i < 30 * 8; i++)
        {
            brain.Update(Dt, cursor);
            becameCurious |= brain.Activity == PetActivity.Curious;
            if (becameCurious && brain.Animation == "sit")
                break;
        }

        Assert.True(becameCurious);
        Assert.Equal("sit", brain.Animation);
        Assert.InRange(Math.Abs(brain.X - cursor.X), 0, 120);
        Assert.Equal(1, brain.Facing); // 坐在游標左邊,面向它
    }

    [Fact]
    public void IgnoresACursorThatIsFarAway()
    {
        var brain = Create(TwoScreens(withPlatforms: false), seed: 1, startX: 400);

        for (var i = 0; i < 30 * 5; i++)
        {
            brain.Update(Dt, (brain.X + 900, 100));
            Assert.NotEqual(PetActivity.Curious, brain.Activity);
        }
    }

    [Fact]
    public void CelebrateHopsThenSitsUntilUnlocked()
    {
        var brain = Create(TwoScreens(), seed: 4);
        for (var i = 0; i < 30 * 20; i++)
            brain.Update(Dt);

        brain.Celebrate();
        var hopped = false;
        for (var i = 0; i < 30 * 15; i++)
        {
            brain.Update(Dt);
            hopped |= brain.Activity == PetActivity.Hop;
        }

        Assert.True(hopped);
        for (var i = 0; i < 30 * 60; i++)
        {
            brain.Update(Dt);
            Assert.Equal(PetActivity.Sit, brain.Activity);
        }
    }

    [Fact]
    public void ShrinkingTheWorldPullsThePetBackInsideAndOffMissingPlatforms()
    {
        var brain = Create(TwoScreens(), seed: 5, startX: 3000);
        // 走到確定站在平台上為止。
        for (var i = 0; i < 30 * 60 * 10 && !(brain.OnPlatform && brain.Activity is not PetActivity.Jump); i++)
            brain.Update(Dt);
        Assert.True(brain.OnPlatform);

        var smaller = new PetWorld(new[] { 500.0 });
        brain.SetWorld(smaller);

        Assert.False(brain.OnPlatform);
        Assert.Equal(PetWorld.GroundY, brain.Y, 6);
        Assert.InRange(brain.X, 0, smaller.Length);

        for (var i = 0; i < 30 * 60; i++)
        {
            brain.Update(Dt);
            Assert.InRange(brain.X, 0, smaller.Length);
        }
    }

    [Fact]
    public void MovingAPlatformCarriesThePetStandingOnIt()
    {
        var world = TwoScreens();
        var brain = Create(world, seed: 5);
        for (var i = 0; i < 30 * 60 * 10 && !(brain.OnPlatform && brain.Activity is not PetActivity.Jump); i++)
            brain.Update(Dt);
        Assert.True(brain.OnPlatform);

        // 版面往上移 40(例如解鎖按鈕出現)。
        var moved = world.WithPlatforms(world.Platforms.Select(p => p with { Top = p.Top - 40 }).ToArray());
        var before = brain.Y;
        brain.SetWorld(moved);

        Assert.True(brain.OnPlatform);
        Assert.Equal(before - 40, brain.Y, 6);
    }
}
