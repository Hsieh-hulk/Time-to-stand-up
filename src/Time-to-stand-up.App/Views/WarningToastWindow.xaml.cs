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

    /// <summary>使用者按下延後,參數為延後時間。</summary>
    public event Action<TimeSpan>? SnoozeRequested;

    /// <summary>使用者按下「略過這次」。</summary>
    public event Action? SkipRequested;

    /// <summary>額度用完時隱藏延後/略過按鈕。</summary>
    public void SetSnoozesRemaining(int remaining)
    {
        SnoozePanel.Visibility = remaining > 0 ? Visibility.Visible : Visibility.Collapsed;
        QuotaText.Text = remaining > 0
            ? $"本輪還可以延後或略過 {remaining} 次"
            : "本輪已無法再延後,該起來動一動了";
    }

    private void OnSnoozeClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && int.TryParse(tag, out var minutes))
            SnoozeRequested?.Invoke(TimeSpan.FromMinutes(minutes));
    }

    private void OnSkipClick(object sender, RoutedEventArgs e) => SkipRequested?.Invoke();

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
