using System;
using System.Windows;
using System.Windows.Media;

namespace TimeToStandUp.App;

/// <summary>星空:星點緩慢向右飄移並邊緣循環、各自以不同節奏閃爍。不播放動畫時停在固定一格。</summary>
public sealed class StarfieldAnimation : BreakAnimationLayer
{
    private const int StarCount = 110;
    private const int AlphaLevels = 8;

    private readonly Star[] _stars = CreateStars();

    // 兩種星色 × 數個透明度,預先建好避免每一格都配置新的筆刷。
    private static readonly Brush[][] Brushes =
    {
        CreateBrushes(Color.FromRgb(0xFF, 0xF1, 0xCC)),
        CreateBrushes(Color.FromRgb(0xCF, 0xE3, 0xF5)),
    };

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        var s = Scale;
        var time = Time;
        foreach (var star in _stars)
        {
            var x = (star.X + time * star.Drift) % 1.0;
            var y = star.Y + Math.Sin(time * star.Speed * 0.3 + star.Phase) * 0.004;
            var twinkle = 0.5 + 0.5 * Math.Sin(time * star.Speed + star.Phase);
            var level = Math.Clamp((int)(twinkle * AlphaLevels), 0, AlphaLevels - 1);

            dc.DrawEllipse(
                Brushes[star.Warm ? 0 : 1][level],
                null,
                new Point(x * width, y * height),
                star.Radius * s,
                star.Radius * s);
        }
    }

    private static Star[] CreateStars()
    {
        var random = new Random(20240607); // 固定種子,每次休息看到同一片星空
        var stars = new Star[StarCount];
        for (var i = 0; i < stars.Length; i++)
        {
            stars[i] = new Star(
                X: random.NextDouble(),
                Y: random.NextDouble(),
                Radius: 0.9 + random.NextDouble() * 1.9,
                Phase: random.NextDouble() * Math.PI * 2,
                Speed: 0.6 + random.NextDouble() * 1.6,
                Drift: 0.0025 + random.NextDouble() * 0.008,
                Warm: random.NextDouble() < 0.4);
        }

        return stars;
    }

    private static Brush[] CreateBrushes(Color color)
    {
        var brushes = new Brush[AlphaLevels];
        for (var i = 0; i < AlphaLevels; i++)
        {
            var alpha = 0.15 + 0.85 * (i + 1) / AlphaLevels;
            brushes[i] = Freeze(new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255), color.R, color.G, color.B)));
        }

        return brushes;
    }

    private readonly record struct Star(double X, double Y, double Radius, double Phase, double Speed, double Drift, bool Warm);
}
