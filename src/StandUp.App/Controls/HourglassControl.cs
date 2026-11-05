using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using StandUp.Core;

namespace StandUp.App;

/// <summary>
/// 以向量圖形繪製的沙漏(SPEC 3.2.1)。固定使用 200×320 的邏輯座標,外層用 LayoutTransform 隨解析度縮放。
/// 沙量由 <see cref="HourglassModel"/> 依休息進度計算,是真實的進度表示,不是循環裝飾動畫。
/// </summary>
public sealed class HourglassControl : FrameworkElement
{
    private const double LogicalWidth = 200;
    private const double LogicalHeight = 320;

    private const double BulbTop = 20;
    private const double Neck = 160;
    private const double BulbBottom = 300;
    private const double BulbHeight = Neck - BulbTop;
    private const double CenterX = LogicalWidth / 2;

    // 瓶口寬 160、瓶頸寬 16,比例必須與 HourglassModel.DefaultNeckRatio 一致。
    private static readonly Geometry TopBulb = Freeze(Geometry.Parse("M 20,20 L 180,20 L 108,160 L 92,160 Z"));
    private static readonly Geometry BottomBulb = Freeze(Geometry.Parse("M 92,160 L 108,160 L 180,300 L 20,300 Z"));

    private static readonly Brush SandBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0xC0, 0x68)));
    private static readonly Brush GlassFill = Freeze(new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush CapBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x7A, 0x5C, 0x3A)));
    private static readonly Pen GlassPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x9F, 0xB8, 0xCC)), 3)
    {
        LineJoin = PenLineJoin.Round,
    });

    private static readonly Stopwatch Animation = Stopwatch.StartNew();

    private double _progress;
    private bool _streamActive = true;
    private bool _animateStream;

    /// <param name="progress">休息進度 0~1</param>
    /// <param name="streamActive">是否顯示細沙流</param>
    /// <param name="animateStream">細沙流是否有流動動畫(系統關閉動畫時為 false)</param>
    public void SetState(double progress, bool streamActive, bool animateStream)
    {
        _progress = progress;
        _streamActive = streamActive;
        _animateStream = animateStream;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(LogicalWidth, LogicalHeight);

    protected override void OnRender(DrawingContext dc)
    {
        var levels = HourglassModel.Compute(_progress);

        dc.DrawRoundedRectangle(CapBrush, null, new Rect(8, 6, 184, 14), 4, 4);
        dc.DrawRoundedRectangle(CapBrush, null, new Rect(8, 300, 184, 14), 4, 4);

        dc.DrawGeometry(GlassFill, null, TopBulb);
        dc.DrawGeometry(GlassFill, null, BottomBulb);

        var topSurface = Neck - levels.TopHeightFraction * BulbHeight;
        if (levels.TopHeightFraction > 0)
            DrawSand(dc, TopBulb, topSurface, Neck);

        var bottomSurface = BulbBottom - levels.BottomHeightFraction * BulbHeight;
        if (levels.BottomHeightFraction > 0)
            DrawSand(dc, BottomBulb, bottomSurface, BulbBottom);

        if (_streamActive && levels.StreamActive)
            DrawStream(dc, bottomSurface);

        dc.DrawGeometry(null, GlassPen, TopBulb);
        dc.DrawGeometry(null, GlassPen, BottomBulb);
    }

    private static void DrawSand(DrawingContext dc, Geometry bulb, double surfaceY, double floorY)
    {
        dc.PushClip(bulb);
        dc.DrawRectangle(SandBrush, null, new Rect(0, surfaceY, LogicalWidth, floorY - surfaceY));
        dc.Pop();
    }

    private void DrawStream(DrawingContext dc, double bottomSurfaceY)
    {
        var pen = new Pen(SandBrush, 2.5);
        if (_animateStream)
        {
            // 負的 offset 讓虛線圖樣沿著線往下移動,看起來像沙粒落下。
            pen.DashStyle = new DashStyle(new[] { 0.5, 1.5 }, -Animation.Elapsed.TotalSeconds * 10);
            pen.DashCap = PenLineCap.Round;
        }

        dc.DrawLine(pen, new Point(CenterX, Neck - 2), new Point(CenterX, bottomSurfaceY));
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
