using TimeToStandUp.Core;

namespace TimeToStandUp.App;

/// <summary>依主題建立休息畫面的背景動畫。設定頁的主題選擇與預覽也會用這個工廠。</summary>
public static class BreakAnimations
{
    /// <summary><see cref="BreakAnimation.None"/> 回傳 null。</summary>
    public static BreakAnimationLayer? Create(BreakAnimation animation) => animation switch
    {
        BreakAnimation.Breathing => new BreathingAnimation(),
        BreakAnimation.Starfield => new StarfieldAnimation(),
        BreakAnimation.Waves => new WavesAnimation(),
        BreakAnimation.Stretch => new StretchAnimation(),
        _ => null,
    };
}
