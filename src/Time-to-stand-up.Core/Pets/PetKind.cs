using System.Text.Json.Serialization;

namespace TimeToStandUp.Core;

/// <summary>休息畫面上活動的寵物。</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PetKind
{
    Cat,
    Shiba,
    Slime,
    SandSprite,
}

/// <summary>寵物的一個特殊動作:精靈圖中的動畫名稱與播放秒數。</summary>
public sealed record PetSpecial(string Animation, double Seconds);

/// <summary>每種寵物的行為參數。長度單位是「參考單位」(見 <see cref="PetWorld"/>)。</summary>
public sealed record PetProfile(
    double Size,
    double WalkSpeed,
    double RunSpeed,
    IReadOnlyList<PetSpecial> Specials,
    bool Sleeps)
{
    /// <summary>每種寵物都有的基本動畫;精靈圖必須全部提供。</summary>
    public static IReadOnlyList<string> BaseAnimations { get; } =
        new[] { "idle", "walk", "run", "jump", "sit", "sleep" };

    /// <summary>精靈圖資源的檔名(不含副檔名):Assets/Pets/&lt;名稱&gt;.png 與 .json。</summary>
    public static string AssetName(PetKind kind) => kind.ToString().ToLowerInvariant();

    public static PetProfile For(PetKind kind) => kind switch
    {
        // 柴犬:比貓好動,跑得最快。
        PetKind.Shiba => new PetProfile(
            Size: 100,
            WalkSpeed: 110,
            RunSpeed: 330,
            Specials: new[] { new PetSpecial("run_in_place", 2.4), new PetSpecial("play_bow", 2.4) },
            Sleeps: true),

        // 史萊姆:用彈跳移動所以慢,精力旺盛不睡覺。
        PetKind.Slime => new PetProfile(
            Size: 88,
            WalkSpeed: 70,
            RunSpeed: 200,
            Specials: new[] { new PetSpecial("multi_hop", 2.2), new PetSpecial("squish", 2.6) },
            Sleeps: false),

        // 沙漏精靈:小小一隻,所以畫大一點;睡覺時化成一堆沙。
        PetKind.SandSprite => new PetProfile(
            Size: 104,
            WalkSpeed: 80,
            RunSpeed: 230,
            Specials: new[] { new PetSpecial("handstand", 3.0), new PetSpecial("burrow", 3.2) },
            Sleeps: true),

        _ => new PetProfile(
            Size: 96,
            WalkSpeed: 90,
            RunSpeed: 260,
            Specials: new[] { new PetSpecial("stretch", 2.4), new PetSpecial("yawn", 2.0) },
            Sleeps: true),
    };
}
