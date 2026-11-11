using System;
using System.Windows;
using System.Windows.Media;

namespace TimeToStandUp.App;

/// <summary>海浪:畫面下方三層速度與相位不同的正弦波,顏色由遠到近漸亮。不播放動畫時停在固定一格。</summary>
public sealed class WavesAnimation : BreakAnimationLayer
{
    private const double Step = 12; // 取樣間距(DIP)

    private static readonly Layer[] Layers =
    {
        new(Baseline: 0.82, Amplitude: 16, Wavelength: 640, Speed: 0.35, Phase: 0.0, Alpha: 0x1A),
        new(Baseline: 0.86, Amplitude: 20, Wavelength: 480, Speed: -0.50, Phase: 1.7, Alpha: 0x22),
        new(Baseline: 0.91, Amplitude: 24, Wavelength: 360, Speed: 0.70, Phase: 3.1, Alpha: 0x2A),
    };

    private static readonly Brush[] Brushes = Array.ConvertAll(
        Layers,
        layer => Freeze(new SolidColorBrush(Color.FromArgb(layer.Alpha, 0x5F, 0xA8, 0xD3))));

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        var s = Scale;
        var time = Time;
        for (var i = 0; i < Layers.Length; i++)
        {
            var layer = Layers[i];
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                var baseline = height * layer.Baseline;
                var k = 2 * Math.PI / (layer.Wavelength * s);
                var shift = time * layer.Speed * 2 * Math.PI + layer.Phase;

                ctx.BeginFigure(new Point(0, height), isFilled: true, isClosed: true);
                var x = 0.0;
                while (true)
                {
                    var y = baseline + Math.Sin(x * k + shift) * layer.Amplitude * s;
                    ctx.LineTo(new Point(x, y), isStroked: false, isSmoothJoin: false);
                    if (x >= width)
                        break;
                    x = Math.Min(width, x + Step * s);
                }

                ctx.LineTo(new Point(width, height), isStroked: false, isSmoothJoin: false);
            }

            geometry.Freeze();
            dc.DrawGeometry(Brushes[i], null, geometry);
        }
    }

    private readonly record struct Layer(double Baseline, double Amplitude, double Wavelength, double Speed, double Phase, byte Alpha);
}
