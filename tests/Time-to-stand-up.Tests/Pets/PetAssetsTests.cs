using System.Buffers.Binary;
using System.Text.Json;
using TimeToStandUp.Core;

namespace TimeToStandUp.Tests;

/// <summary>
/// 每種寵物的精靈圖(tools/make_pet_sprites.py 產生、內嵌在 App 裡)都要齊全,
/// 而且動畫表要有 <see cref="PetBrain"/> 會用到的每一個動畫名稱。
/// </summary>
public class PetAssetsTests
{
    private static readonly string AssetsDirectory = Path.Combine(
        FindRepositoryRoot(), "src", "Time-to-stand-up.App", "Assets", "Pets");

    public static IEnumerable<object[]> AllPets() =>
        Enum.GetValues<PetKind>().Select(kind => new object[] { kind });

    [Theory]
    [MemberData(nameof(AllPets))]
    public void EveryPetHasASpriteSheetCoveringItsAnimations(PetKind kind)
    {
        var name = PetProfile.AssetName(kind);
        var pngPath = Path.Combine(AssetsDirectory, name + ".png");
        var jsonPath = Path.Combine(AssetsDirectory, name + ".json");
        Assert.True(File.Exists(pngPath), $"missing {pngPath}");
        Assert.True(File.Exists(jsonPath), $"missing {jsonPath}");

        using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var cell = document.RootElement.GetProperty("cell").GetInt32();
        var animations = document.RootElement.GetProperty("animations");

        var required = PetProfile.BaseAnimations
            .Concat(PetProfile.For(kind).Specials.Select(special => special.Animation));
        foreach (var animation in required)
            Assert.True(animations.TryGetProperty(animation, out _), $"{name} is missing animation '{animation}'");

        // 動畫表描述的每一格都必須落在圖片範圍內。
        var (width, height) = ReadPngSize(pngPath);
        foreach (var animation in animations.EnumerateObject())
        {
            var row = animation.Value.GetProperty("row").GetInt32();
            var frames = animation.Value.GetProperty("frames").GetInt32();
            Assert.True(frames > 0, $"{name}/{animation.Name} has no frames");
            Assert.True(animation.Value.GetProperty("fps").GetDouble() > 0);
            Assert.True(frames * cell <= width, $"{name}/{animation.Name} runs past the sheet width");
            Assert.True((row + 1) * cell <= height, $"{name}/{animation.Name} runs past the sheet height");
        }
    }

    [Fact]
    public void AssetNamesAreLowerCaseKindNames()
    {
        Assert.Equal("cat", PetProfile.AssetName(PetKind.Cat));
        Assert.Equal("shiba", PetProfile.AssetName(PetKind.Shiba));
        Assert.Equal("slime", PetProfile.AssetName(PetKind.Slime));
        Assert.Equal("sandsprite", PetProfile.AssetName(PetKind.SandSprite));
    }

    [Fact]
    public void EachPetHasItsOwnProfile()
    {
        var profiles = Enum.GetValues<PetKind>().Select(PetProfile.For).ToArray();

        Assert.Equal(profiles.Length, profiles.Select(p => (p.Size, p.WalkSpeed, p.RunSpeed)).Distinct().Count());
        Assert.Equal(
            profiles.Length,
            profiles.Select(p => string.Join(",", p.Specials.Select(s => s.Animation))).Distinct().Count());
        Assert.All(profiles, profile =>
        {
            Assert.True(profile.RunSpeed > profile.WalkSpeed);
            Assert.NotEmpty(profile.Specials);
            Assert.All(profile.Specials, special => Assert.True(special.Seconds > 0));
        });
    }

    private static (int Width, int Height) ReadPngSize(string path)
    {
        var header = new byte[24];
        using var stream = File.OpenRead(path);
        Assert.Equal(header.Length, stream.Read(header, 0, header.Length));
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, header[..8]);
        return (
            BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4)),
            BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4)));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Time-to-stand-up.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("找不到 Time-to-stand-up.sln");
    }
}
