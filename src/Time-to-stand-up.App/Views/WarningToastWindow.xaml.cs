using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace TimeToStandUp.App;

/// <summary>休息前的小提示:不搶焦點、不打斷操作,顯示在主螢幕工作區右下角。</summary>
public partial class WarningToastWindow : Window
{
    private const int ScreenMargin = 24;

    public WarningToastWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Reposition();
        SizeChanged += (_, _) => Reposition();
    }

    /// <summary>使用者點擊提示將它關閉。</summary>
    public event Action? Dismissed;

    public void SetRemaining(TimeSpan remaining)
    {
        var totalSeconds = Math.Max((int)Math.Ceiling(remaining.TotalSeconds), 0);
        MessageText.Text = $"{totalSeconds / 60:00}:{totalSeconds % 60:00} 後要起身走動,請先存好手邊的工作";
    }

    private void Reposition()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var screen = Forms.Screen.PrimaryScreen;
        if (hwnd == IntPtr.Zero || screen is null)
            return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        var area = screen.WorkingArea;

        NativeMethods.SetWindowPos(
            hwnd, NativeMethods.HWND_TOPMOST,
            area.Right - width - ScreenMargin, area.Bottom - height - ScreenMargin, width, height,
            NativeMethods.SWP_NOACTIVATE);
    }

    private void OnClick(object sender, MouseButtonEventArgs e) => Dismissed?.Invoke();
}
