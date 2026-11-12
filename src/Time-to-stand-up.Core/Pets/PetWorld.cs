namespace TimeToStandUp.Core;

/// <summary>寵物可以站上去的平台(例如沙漏頂蓋):軌道上的左右範圍與頂面高度。</summary>
public readonly record struct PetPlatform(double Left, double Right, double Top);

/// <summary>
/// 寵物活動的世界。所有螢幕依左到右串成一條一維「軌道」,單位是參考單位:
/// 每個螢幕都當成 900 高,長度 = 實體寬 × 900 / 實體高。這樣螢幕的解析度、位置、DPI 不同都不影響邏輯,
/// 每個視窗只要用自己的 高度/900 把落在自己區間的寵物畫出來。y 向下,0 是螢幕頂端。
/// </summary>
public sealed class PetWorld
{
    public const double ReferenceHeight = 900;

    /// <summary>地面(寵物腳底)的 y。留一點距離,不踩到畫面底部的說明文字。</summary>
    public const double GroundY = 844;

    private readonly double[] _starts;

    public PetWorld(IReadOnlyList<double> screenLengths, IReadOnlyList<PetPlatform>? platforms = null)
    {
        if (screenLengths.Count == 0)
            throw new ArgumentException("至少要有一個螢幕", nameof(screenLengths));

        ScreenLengths = screenLengths.ToArray();
        _starts = new double[ScreenLengths.Count];
        var total = 0.0;
        for (var i = 0; i < ScreenLengths.Count; i++)
        {
            _starts[i] = total;
            total += ScreenLengths[i];
        }

        Length = total;
        Platforms = platforms?.ToArray() ?? Array.Empty<PetPlatform>();
    }

    public IReadOnlyList<double> ScreenLengths { get; }

    public IReadOnlyList<PetPlatform> Platforms { get; }

    /// <summary>整條軌道的長度。</summary>
    public double Length { get; }

    /// <summary>第 index 個螢幕在軌道上的起點。</summary>
    public double ScreenStart(int index) => _starts[index];

    public PetWorld WithPlatforms(IReadOnlyList<PetPlatform> platforms) => new(ScreenLengths, platforms);

    /// <summary>實體像素大小的螢幕在軌道上占的長度。</summary>
    public static double ScreenLength(double pixelWidth, double pixelHeight) =>
        pixelHeight <= 0 ? 0 : pixelWidth * ReferenceHeight / pixelHeight;
}
