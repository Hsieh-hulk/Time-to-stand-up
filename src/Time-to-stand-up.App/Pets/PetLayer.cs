using System;
using System.Windows;
using System.Windows.Media;
using TimeToStandUp.Core;

namespace TimeToStandUp.App;

/// <summary>
/// 把寵物(<see cref="PetBrain"/>)畫出來。休息畫面上每個視窗各有一個,共用同一隻寵物:
/// 寵物在軌道上的位置落在這個螢幕的區間時才看得到,跨越螢幕邊界時兩邊各畫出自己那一半。
/// 設定頁的預覽也用它,只是把檢視範圍(<see cref="ViewTop"/>、<see cref="ViewHeight"/>)縮到地面附近。
/// 鋪滿所在區域、不接收滑鼠。
/// </summary>
public sealed class PetLayer : FrameworkElement
{
    private PetBrain? _brain;
    private SpriteSheet? _sheet;
    private double _segmentStart;

    public PetLayer()
    {
        IsHitTestVisible = false;
        Focusable = false;
        ClipToBounds = true;
        // 像素圖放大時保持銳利,不要被內插糊掉。
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
    }

    /// <summary>這個元素的高度對應多少參考單位。預設是整個螢幕(900)。</summary>
    public double ViewHeight { get; set; } = PetWorld.ReferenceHeight;

    /// <summary>元素頂端對應的 y(參考單位)。預設 0,即螢幕頂端。</summary>
    public double ViewTop { get; set; }

    /// <param name="segmentStart">這個螢幕在軌道上的起點(參考單位)。</param>
    public void Attach(PetBrain brain, SpriteSheet sheet, double segmentStart)
    {
        _brain = brain;
        _sheet = sheet;
        _segmentStart = segmentStart;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_brain is null || _sheet is null || ActualHeight <= 0 || ViewHeight <= 0)
            return;

        var scale = ActualHeight / ViewHeight;
        var density = DevicePixelsPerUnit();

        // 每個精靈像素對應整數個裝置像素,放大後每一格才會一樣大。
        var devicePixels = Math.Max(1, Math.Round(_brain.Size * scale * density / _sheet.Cell));
        var size = devicePixels * _sheet.Cell / density;

        var left = Snap((_brain.X - _segmentStart) * scale - size / 2, density);
        var top = Snap((_brain.Y - ViewTop) * scale - size, density);
        if (left > ActualWidth || left + size < 0)
            return; // 寵物在別的螢幕上

        var frame = _sheet.Frame(_brain.Animation, _brain.AnimationTime, _brain.AnimationProgress);
        var rect = new Rect(left, top, size, size);

        if (_brain.Facing < 0)
        {
            // 精靈圖只畫朝右,朝左時以自身中心水平翻轉。
            dc.PushTransform(new ScaleTransform(-1, 1, left + size / 2, 0));
            dc.DrawImage(frame, rect);
            dc.Pop();
        }
        else
        {
            dc.DrawImage(frame, rect);
        }
    }

    /// <summary>
    /// 這個元素的 1 個座標單位等於幾個裝置像素:螢幕 DPI 乘上祖先的縮放
    /// (設定視窗會用 LayoutTransform 放大整個介面)。
    /// </summary>
    private double DevicePixelsPerUnit()
    {
        var density = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (PresentationSource.FromVisual(this)?.RootVisual is Visual root
            && !ReferenceEquals(root, this)
            && TransformToAncestor(root) is MatrixTransform transform
            && transform.Matrix.M11 > 0)
        {
            density *= transform.Matrix.M11;
        }

        return density;
    }

    private static double Snap(double value, double density) => Math.Round(value * density) / density;
}
