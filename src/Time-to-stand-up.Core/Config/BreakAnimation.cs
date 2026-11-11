using System.Text.Json.Serialization;

namespace TimeToStandUp.Core;

/// <summary>休息畫面的背景動畫主題。沙漏本身(真實的休息進度)不受影響,動畫只是疊在旁邊或後方的陪伴。</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BreakAnimation
{
    /// <summary>不顯示動畫,只有沙漏。</summary>
    None,

    /// <summary>呼吸光圈:隨 4 秒吸、6 秒吐的節奏擴張收縮。</summary>
    Breathing,

    /// <summary>星空:緩慢飄移、閃爍的星點。</summary>
    Starfield,

    /// <summary>海浪:畫面下方層疊的柔和波浪。</summary>
    Waves,

    /// <summary>伸展小人:循環示範伸懶腰、側彎、打開胸口等動作。</summary>
    Stretch,
}
