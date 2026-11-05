using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using StandUp.Core;

namespace StandUp.App;

/// <summary>
/// 全螢幕置頂的休息畫面,每個螢幕一個。主螢幕顯示沙漏與解鎖按鈕,
/// 其他螢幕只顯示深色底與簡化的倒數文字(SPEC 3.2.1)。
/// 視窗只能由 <see cref="CloseForReal"/> 關閉,Alt+F4 等一般關閉動作會被擋下。
/// </summary>
public partial class BreakOverlayWindow : Window
{
    private const double ReferenceHeight = 900;
    private static readonly TimeSpan UnlockArmDelay = TimeSpan.FromMilliseconds(1500);

    private readonly BreakStateMachine _machine;
    private readonly bool _isPrimary;
    private readonly System.Drawing.Rectangle _bounds;
    private bool _allowClose;
    private bool _readyShown;

    public BreakOverlayWindow(BreakStateMachine machine, bool isPrimary, System.Drawing.Rectangle physicalBounds)
    {
        InitializeComponent();
        _machine = machine;
        _isPrimary = isPrimary;
        _bounds = physicalBounds;

        DisclaimerText.Text = HealthCopy.OverlayDisclaimer;
        Hourglass.Visibility = isPrimary ? Visibility.Visible : Visibility.Collapsed;
        DisclaimerText.Visibility = isPrimary ? Visibility.Visible : Visibility.Collapsed;
        if (!isPrimary)
        {
            CountdownText.Margin = new Thickness(0, 24, 0, 0);
            TitleText.FontSize = 36;
            CountdownText.FontSize = 96;
        }

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) =>
        {
            ApplyBounds();
            UpdateScale();
        };
        SizeChanged += (_, _) => UpdateScale();
        Closing += OnClosing;

        ShowLockedState();
        Refresh(animated: false);
    }

    /// <summary>主螢幕的解鎖按鈕被按下。</summary>
    public event Action? UnlockRequested;

    public bool IsPrimary => _isPrimary;

    /// <summary>依狀態機目前的進度更新倒數文字與沙漏。</summary>
    public void Refresh(bool animated)
    {
        var remaining = _machine.BreakRemaining;
        var totalSeconds = (int)Math.Ceiling(remaining.TotalSeconds);
        CountdownText.Text = $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";

        if (!_isPrimary)
            return;

        var locked = _machine.State == BreakState.BreakLocked;
        Hourglass.SetState(_machine.BreakProgress, streamActive: locked, animateStream: locked && animated);
    }

    /// <summary>倒數完成:沙流停止,顯示完成狀態,主螢幕出現解鎖按鈕。</summary>
    public void ShowReadyState(bool animated)
    {
        if (_readyShown)
            return;
        _readyShown = true;

        Refresh(animated: false);
        TitleText.Text = "可以繼續了";

        if (_isPrimary)
        {
            HintText.Text = "準備好了就按下解鎖,重新開始計時。";
            UnlockButton.Visibility = Visibility.Visible;
            ArmUnlockButton();
        }
        else
        {
            HintText.Text = "請到主螢幕按下解鎖。";
        }

        if (animated)
        {
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(700));
            TitleText.BeginAnimation(OpacityProperty, fade);
            UnlockButton.BeginAnimation(OpacityProperty, fade);
        }
    }

    /// <summary>重新套用實體像素的位置與大小,並確保維持在最上層。</summary>
    public void ApplyBounds()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        NativeMethods.SetWindowPos(
            hwnd, NativeMethods.HWND_TOPMOST,
            _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height,
            NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>讓視窗真的關閉(一般的關閉動作會被攔截)。</summary>
    public void CloseForReal()
    {
        _allowClose = true;
        Close();
    }

    /// <summary>
    /// 按鈕剛出現的前一小段時間不接受點擊,避免使用者正好在操作滑鼠時被剛出現的按鈕誤觸而解鎖。
    /// </summary>
    private void ArmUnlockButton()
    {
        UnlockButton.IsEnabled = false;
        var timer = new DispatcherTimer { Interval = UnlockArmDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            UnlockButton.IsEnabled = true;
        };
        timer.Start();
    }

    private void ShowLockedState()
    {
        TitleText.Text = "站起來走動一下吧";
        HintText.Text = _isPrimary
            ? HealthCopy.OverlayHint
            : string.Empty;
        HintText.Visibility = _isPrimary ? Visibility.Visible : Visibility.Collapsed;
        UnlockButton.Visibility = Visibility.Collapsed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ApplyBounds();
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // 視窗被移到不同 DPI 的螢幕時,WPF 會依建議矩形重設大小;事後再蓋回實體像素的全螢幕範圍。
        if (msg == NativeMethods.WM_DPICHANGED)
            Dispatcher.BeginInvoke(new Action(ApplyBounds));
        return IntPtr.Zero;
    }

    private void UpdateScale()
    {
        if (ActualHeight <= 0)
            return;
        var scale = Math.Clamp(ActualHeight / ReferenceHeight, 0.4, 4);
        ContentPanel.LayoutTransform = new ScaleTransform(scale, scale);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose)
            e.Cancel = true;
    }

    private void OnUnlockClick(object sender, RoutedEventArgs e) => UnlockRequested?.Invoke();
}
