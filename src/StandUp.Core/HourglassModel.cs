namespace StandUp.Core;

/// <summary>
/// 沙漏某一時刻的狀態。
/// Fill 是沙的「體積比例」(與剩餘時間成線性);HeightFraction 是對應的沙面高度占球體高度的比例,
/// 用來繪圖(球體是梯形,體積與高度並非線性)。
/// </summary>
public readonly record struct HourglassLevels(
    double TopFill,
    double BottomFill,
    double TopHeightFraction,
    double BottomHeightFraction,
    bool StreamActive);

/// <summary>
/// 沙漏模型(SPEC 3.2.1):沙量隨休息進度線性移動,細沙在倒數期間持續流下。
/// 球體以梯形近似:寬度從瓶頸(neckRatio × 瓶口寬)線性變到瓶口寬,體積因此有解析解。
/// </summary>
public static class HourglassModel
{
    /// <summary>瓶頸寬度占球體最寬處的比例。</summary>
    public const double DefaultNeckRatio = 0.1;

    public static HourglassLevels Compute(double progress, double neckRatio = DefaultNeckRatio)
    {
        var p = double.IsNaN(progress) ? 0 : Math.Clamp(progress, 0, 1);
        var topFill = 1 - p;
        var bottomFill = p;

        return new HourglassLevels(
            topFill,
            bottomFill,
            TopHeightFraction(topFill, neckRatio),
            BottomHeightFraction(bottomFill, neckRatio),
            StreamActive: p < 1);
    }

    /// <summary>上半球:沙從瓶頸往上堆,回傳體積比例 fill 對應的沙面高度比例(0 = 瓶頸,1 = 瓶口)。</summary>
    public static double TopHeightFraction(double fill, double neckRatio = DefaultNeckRatio)
    {
        fill = Math.Clamp(fill, 0, 1);
        // 寬度 w(t) = n + k·t(t 從瓶頸量起,最寬處 = 1),面積 A(t) = n·t + k·t²/2,總面積 (n+1)/2。
        var k = 1 - neckRatio;
        var total = (neckRatio + 1) / 2;
        return (-neckRatio + Math.Sqrt(neckRatio * neckRatio + 2 * k * fill * total)) / k;
    }

    /// <summary>下半球:沙從底部往瓶頸堆,回傳體積比例 fill 對應的沙面高度比例(0 = 底部,1 = 瓶頸)。</summary>
    public static double BottomHeightFraction(double fill, double neckRatio = DefaultNeckRatio)
    {
        fill = Math.Clamp(fill, 0, 1);
        // 寬度 w(s) = 1 − k·s(s 從底部量起),面積 A(s) = s − k·s²/2。
        var k = 1 - neckRatio;
        var total = (neckRatio + 1) / 2;
        var discriminant = Math.Max(1 - 2 * k * fill * total, 0);
        return (1 - Math.Sqrt(discriminant)) / k;
    }
}
