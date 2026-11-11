using System;
using System.Windows;
using System.Windows.Media;

namespace TimeToStandUp.App;

/// <summary>
/// 伸展小人:站在沙漏右側,循環示範向上延伸、左右側彎、打開胸口等動作,底下標示目前的動作。
/// 以關節角度做關鍵影格並用 smoothstep 補間;手臂角度是相對於軀幹(再加上軀幹傾斜),所以側彎時手臂會自然跟著傾斜。
/// 不播放動畫時停在「向上延伸」。
/// </summary>
public sealed class StretchAnimation : BreakAnimationLayer
{
    // 小人以「地面中心點」為原點、單位為基準 DIP(y 向下):腳在 y=+160,與沙漏底部大致同高。
    private const double GroundY = 160;
    private const double HipY = 40;
    private const double TorsoLength = 105;
    private const double ShoulderHalfWidth = 20;
    private const double NeckLength = 40;
    private const double HeadRadius = 22;
    private const double UpperArm = 58;
    private const double Forearm = 54;
    private const double FootSpread = 24;

    private static readonly Pen LimbPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0xE0, 0xE8, 0xEE, 0xF4)), 9)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
        LineJoin = PenLineJoin.Round,
    });

    private static readonly Brush HeadBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xE0, 0xE8, 0xEE, 0xF4)));
    private static readonly Brush ShadowBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0x00, 0x00)));
    private static readonly Brush CaptionBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xB0, 0x9F, 0xB3, 0xC4)));

    // 手臂角度:0 = 垂直向下、90 = 朝畫面右、180 = 向上(畫面左邊那隻用負值)。
    private static readonly Pose Rest = new(0, -10, -6, 10, 6);
    private static readonly Pose Up = new(0, -168, -176, 168, 176);
    private static readonly Pose Right = Up with { Lean = 16 };
    private static readonly Pose Left = Up with { Lean = -16 };
    private static readonly Pose Cactus = new(0, -90, -178, 90, 178);

    private const double Cycle = 17;

    private static readonly Key[] Keys =
    {
        new(0.0, Rest, "放鬆"),
        new(2.0, Up, "向上延伸"),
        new(3.5, Up, "向上延伸"),
        new(5.2, Right, "向右側彎"),
        new(6.5, Right, "向右側彎"),
        new(8.5, Left, "向左側彎"),
        new(9.8, Left, "向左側彎"),
        new(11.5, Up, "向上延伸"),
        new(13.0, Cactus, "打開胸口"),
        new(15.0, Cactus, "打開胸口"),
        new(Cycle, Rest, "放鬆"),
    };

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        var s = Scale;
        var x = Math.Min(width / 2 + 480 * s, width - 140 * s);
        if (x < width / 2 + 280 * s)
            return; // 螢幕太窄,放不下就不畫,避免蓋到文字

        var (pose, caption) = Sample(Animated ? Time % Cycle : 2.0);

        dc.PushTransform(new TranslateTransform(x, height / 2));
        dc.PushTransform(new ScaleTransform(s, s));
        DrawFigure(dc, pose);
        dc.DrawEllipse(ShadowBrush, null, new Point(0, GroundY + 2), 72, 8);
        dc.Pop();
        dc.Pop();

        var label = CreateText(caption, 22 * s, CaptionBrush);
        dc.DrawText(label, new Point(x - label.Width / 2, height / 2 + (GroundY + 34) * s));
    }

    private static (Pose Pose, string Caption) Sample(double t)
    {
        for (var i = 0; i < Keys.Length - 1; i++)
        {
            var from = Keys[i];
            var to = Keys[i + 1];
            if (t > to.Time)
                continue;

            var eased = SmoothStep((t - from.Time) / (to.Time - from.Time));
            return (Pose.Lerp(from.Pose, to.Pose, eased), to.Name);
        }

        return (Rest, Keys[^1].Name);
    }

    private static void DrawFigure(DrawingContext dc, Pose pose)
    {
        var lean = Radians(pose.Lean);
        var up = new Vector(Math.Sin(lean), -Math.Cos(lean)); // 沿軀幹向上
        var side = new Vector(Math.Cos(lean), Math.Sin(lean)); // 垂直軀幹、朝畫面右

        var hip = new Point(0, HipY);
        var shoulder = hip + up * TorsoLength;
        var leftShoulder = shoulder - side * ShoulderHalfWidth;
        var rightShoulder = shoulder + side * ShoulderHalfWidth;
        var head = shoulder + up * (NeckLength + HeadRadius - 8);

        // 腿
        dc.DrawLine(LimbPen, hip, new Point(-FootSpread, GroundY));
        dc.DrawLine(LimbPen, hip, new Point(FootSpread, GroundY));

        // 軀幹與肩線
        dc.DrawLine(LimbPen, hip, shoulder);
        dc.DrawLine(LimbPen, leftShoulder, rightShoulder);

        // 手臂
        DrawArm(dc, leftShoulder, pose.LeftUpper + pose.Lean, pose.LeftFore + pose.Lean);
        DrawArm(dc, rightShoulder, pose.RightUpper + pose.Lean, pose.RightFore + pose.Lean);

        // 頸與頭
        dc.DrawLine(LimbPen, shoulder, shoulder + up * (NeckLength - HeadRadius + 8));
        dc.DrawEllipse(HeadBrush, null, head, HeadRadius, HeadRadius);
    }

    private static void DrawArm(DrawingContext dc, Point shoulder, double upperDegrees, double foreDegrees)
    {
        var elbow = shoulder + Direction(upperDegrees) * UpperArm;
        var hand = elbow + Direction(foreDegrees) * Forearm;
        dc.DrawLine(LimbPen, shoulder, elbow);
        dc.DrawLine(LimbPen, elbow, hand);
    }

    private static Vector Direction(double degrees)
    {
        var radians = Radians(degrees);
        return new Vector(Math.Sin(radians), Math.Cos(radians));
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180;

    private readonly record struct Pose(double Lean, double LeftUpper, double LeftFore, double RightUpper, double RightFore)
    {
        public static Pose Lerp(Pose a, Pose b, double t) => new(
            a.Lean + (b.Lean - a.Lean) * t,
            a.LeftUpper + (b.LeftUpper - a.LeftUpper) * t,
            a.LeftFore + (b.LeftFore - a.LeftFore) * t,
            a.RightUpper + (b.RightUpper - a.RightUpper) * t,
            a.RightFore + (b.RightFore - a.RightFore) * t);
    }

    private readonly record struct Key(double Time, Pose Pose, string Name);
}
