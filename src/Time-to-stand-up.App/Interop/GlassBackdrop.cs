using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace TimeToStandUp.App;

/// <summary>
/// 讓視窗背後是系統的 Acrylic 模糊(Windows 11 22H2 以上)。
/// 更舊的系統沒有這個效果,改用接近不透明的後備底色,文字照樣清楚。
/// </summary>
internal static class GlassBackdrop
{
    private const int MinBuildForBackdrop = 22621;

    /// <summary>在視窗建構時呼叫一次;真正的 DWM 設定等到有 HWND 之後才做。</summary>
    public static void Attach(Window window)
    {
        window.SetResourceReference(Window.BackgroundProperty, "Glass.WindowFallbackBrush");

        void OnThemeChanged() => ApplyDarkMode(window);

        window.SourceInitialized += (_, _) =>
        {
            Apply(window);
            ThemeManager.Changed += OnThemeChanged;
        };
        window.Closed += (_, _) => ThemeManager.Changed -= OnThemeChanged;
    }

    private static void Apply(Window window)
    {
        ApplyDarkMode(window);

        if (Environment.OSVersion.Version.Build < MinBuildForBackdrop)
            return;

        var hwnd = new WindowInteropHelper(window).Handle;
        var backdrop = NativeMethods.DWMSBT_TRANSIENTWINDOW;
        if (NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) != 0)
            return;

        var margins = new NativeMethods.Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);
        HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;

        window.SetResourceReference(Window.BackgroundProperty, "Glass.WindowBrush");
    }

    private static void ApplyDarkMode(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var dark = ThemeManager.IsLight ? 0 : 1;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }
}
