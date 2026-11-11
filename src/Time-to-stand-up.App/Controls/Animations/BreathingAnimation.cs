using System;
using System.Windows;
using System.Windows.Media;

namespace TimeToStandUp.App;

/// <summary>
/// 呼吸光圈:以沙漏為中心,光暈隨 4 秒吸氣、6 秒吐氣的節奏擴張與收縮,
/// 底部有「吸氣」「吐氣」提示。不播放動畫時停在半開的狀態。
/// </summary>
public sealed class BreathingAnimation : BreakAnimationLayer
{
    private const double InhaleSeconds = 4;
    private const double ExhaleSeconds = 6;
    private const double Cycle = InhaleSeconds + ExhaleSeconds;

    private static readonly Color Halo = Color.FromRgb(0x5F, 0xA8, 0xD3);
    private static readonly Brush TextBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xB0, 0x9F, 0xB3, 0xC4)));

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        var s = Scale;
        var center = new Point(width / 2, height / 2);

        // 0 = 吐盡、1 = 吸滿。
        double breath;
        bool inhaling;
        if (!Animated)
        {
            breath = 0.5;
            inhaling = true;
        }
        else
        {
            var phase = Time % Cycle;
            inhaling = phase < InhaleSeconds;
            breath = inhaling
                ? SmoothStep(phase / InhaleSeconds)
                : 1 - SmoothStep((phase - InhaleSeconds) / ExhaleSeconds);
        }

        // 三層光暈由外而內,內層比外層更亮,吸氣時一起擴張。
        DrawHalo(dc, center, (250 + 110 * breath) * s, 0.10 + 0.10 * breath);
        DrawHalo(dc, center, (190 + 80 * breath) * s, 0.12 + 0.12 * breath);
        DrawHalo(dc, center, (140 + 50 * breath) * s, 0.14 + 0.14 * breath);

        var label = CreateText(inhaling ? "吸氣" : "吐氣", 22 * s, TextBrush);
        dc.DrawText(label, new Point(center.X - label.Width / 2, height - 100 * s));
    }

    private static void DrawHalo(DrawingContext dc, Point center, double radius, double peakAlpha)
    {
        var brush = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb((byte)Math.Round(peakAlpha * 255), Halo.R, Halo.G, Halo.B), 0.0),
                new GradientStop(Color.FromArgb((byte)Math.Round(peakAlpha * 0.55 * 255), Halo.R, Halo.G, Halo.B), 0.6),
                new GradientStop(Color.FromArgb(0, Halo.R, Halo.G, Halo.B), 1.0),
            },
        };
        brush.Freeze();
        dc.DrawEllipse(brush, null, center, radius, radius);
    }
}
