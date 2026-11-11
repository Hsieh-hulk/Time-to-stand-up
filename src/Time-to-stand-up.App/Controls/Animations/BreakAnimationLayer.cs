using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TimeToStandUp.App;

/// <summary>
/// 休息畫面的背景動畫。鋪滿整個視窗、放在內容後方,不接收滑鼠;
/// 內容以 900 DIP 高為基準設計,這裡用同樣的比例繪製,所以和前景的沙漏、文字等比縮放。
/// 時間由共用的 Stopwatch 推進,畫面由 <see cref="SetState"/> 觸發重繪(AppController 的 30 fps 計時器)。
/// 系統關閉動畫時 <see cref="Animated"/> 為 false,所有動畫停在固定的一格。
/// </summary>
public abstract class BreakAnimationLayer : FrameworkElement
{
    /// <summary>內容的基準高度,與 BreakOverlayWindow.ReferenceHeight 一致。</summary>
    protected const double ReferenceHeight = 900;

    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    protected BreakAnimationLayer()
    {
        IsHitTestVisible = false;
        Focusable = false;
        SnapsToDevicePixels = false;
    }

    protected double Progress { get; private set; }

    protected bool Animated { get; private set; }

    /// <summary>動畫時間(秒)。不播放動畫時固定為 0,畫面靜止。</summary>
    protected double Time => Animated ? Clock.Elapsed.TotalSeconds : 0;

    /// <summary>目前尺寸相對基準高度的縮放比例。</summary>
    protected double Scale => Math.Max(0.1, ActualHeight / ReferenceHeight);

    /// <param name="progress">休息進度 0~1</param>
    /// <param name="animated">是否播放動畫</param>
    public void SetState(double progress, bool animated)
    {
        Progress = progress;
        Animated = animated;
        InvalidateVisual();
    }

    protected static double SmoothStep(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    protected FormattedText CreateText(string text, double size, Brush brush)
    {
        var typeface = new Typeface(TextBlock.GetFontFamily(this), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        return new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    protected static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
