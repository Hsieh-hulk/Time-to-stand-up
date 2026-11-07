using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using TimeToStandUp.Core;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace TimeToStandUp.App;

/// <summary>
/// 把狀態機接到 UI:每秒推進狀態、管理休息畫面(每個螢幕一個)、休息前提示與系統匣。
/// 系統匣有「立即休息」「設定…」與「結束」;更完整的系統匣列在 docs/ROADMAP.md。
/// </summary>
internal sealed class AppController : IDisposable
{
    private const int AnimationFrameMs = 33; // 約 30 fps,動畫的上限

    /// <summary>兩次 tick 間隔超過這個值,視為電腦剛睡醒(備援:睡眠事件偶爾會漏)。</summary>
    private static readonly TimeSpan SleepGapThreshold = TimeSpan.FromSeconds(10);

    private readonly IClock _clock = new SystemClock();
    private readonly BreakStateMachine _machine;
    private readonly PresenceTracker _tracker;
    private PresenceMonitor? _presence;
    private DateTimeOffset _lastTick;
    private readonly List<BreakOverlayWindow> _overlays = new();
    private readonly DispatcherTimer _tick;
    private readonly DispatcherTimer _frame;

    private Forms.NotifyIcon? _tray;
    private Drawing.Icon? _trayIcon;
    private readonly ConfigStore _store;
    private AppConfig _config;

    private Forms.ToolStripMenuItem? _breakNowItem;
    private Forms.ToolStripMenuItem? _settingsItem;
    private Forms.ToolStripMenuItem? _exitItem;
    private WarningToastWindow? _toast;
    private bool _toastDismissed;
    private SettingsWindow? _settingsWindow;

    /// <param name="settings">這次執行實際使用的設定(設定檔加上命令列覆寫)。</param>
    /// <param name="config">設定檔目前的內容,設定視窗以它為起點。</param>
    public AppController(BreakSettings settings, AppConfig config, ConfigStore store)
    {
        _config = config;
        _store = store;
        _machine = new BreakStateMachine(_clock, settings);
        _machine.StateChanged += OnStateChanged;
        _tracker = new PresenceTracker(_clock, _machine);
        _tracker.Returned += OnReturned;

        _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => OnTick();

        _frame = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AnimationFrameMs) };
        _frame.Tick += (_, _) => OnFrame();
    }

    public void Start()
    {
        CreateTray();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _presence = new PresenceMonitor();
        _presence.AwayChanged += OnAwayChanged;
        _lastTick = _clock.UtcNow;
        _tick.Start();
    }

    public void Dispose()
    {
        _tick.Stop();
        _frame.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        if (_presence is not null)
        {
            _presence.AwayChanged -= OnAwayChanged;
            _presence.Dispose();
        }
        CloseSettingsWindow();
        CloseOverlays();
        CloseToast();

        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _trayIcon?.Dispose();
    }

    private static bool AnimationsEnabled => SystemParameters.ClientAreaAnimation;

    private void OnTick()
    {
        var now = _clock.UtcNow;
        if (_config.SmartPause)
        {
            // 睡眠事件沒處理到(上次回來早於上次 tick),用 tick 間隔補算這段離開。
            if (now - _lastTick > SleepGapThreshold && _tracker.LastReturnAt < _lastTick)
                _tracker.ReportGap(_lastTick);
        }
        _lastTick = now;

        // 離開期間不跳出休息畫面:人不在,回來時再由 PresenceTracker 調整工作計時。
        _machine.Update(canInterrupt: !_tracker.IsAway);

        switch (_machine.State)
        {
            case BreakState.Working:
                // 人不在時不顯示休息前提示(否則到點後會一直停在 00:00)。
                if (_tracker.IsAway)
                    CloseToast();
                else
                    UpdateToast();
                break;

            case BreakState.BreakLocked:
                // 系統關閉動畫時沒有 30 fps 的 frame timer,改由這個每秒一次的 tick 更新。
                if (!AnimationsEnabled)
                    RefreshOverlays(animated: false);
                KeepOverlaysOnTop();
                break;

            case BreakState.BreakReady:
                KeepOverlaysOnTop();
                break;
        }
    }

    private void OnFrame() => RefreshOverlays(animated: true);

    private void OnAwayChanged(AwayReason reason, bool on, DateTimeOffset at)
    {
        if (!_config.SmartPause)
            return;
        _tracker.SetAway(reason, on, on ? at : null);
    }

    /// <summary>關閉智慧暫停時清掉所有離開原因,之後的工作時間照常累計。</summary>
    private void ClearAway()
    {
        _tracker.SetAway(AwayReason.Locked, false);
        _tracker.SetAway(AwayReason.Suspended, false);
    }

    private void OnReturned(TimeSpan away, bool reset)
    {
        if (!reset || _tray is null)
            return;
        var minutes = Math.Max(1, (int)Math.Round(away.TotalMinutes));
        _tray.ShowBalloonTip(
            5000,
            "Time-to-stand-up",
            $"剛才離開了約 {minutes} 分鐘,已當作休息,重新開始計時。",
            Forms.ToolTipIcon.Info);
    }

    private void OnStateChanged(BreakState from, BreakState to)
    {
        UpdateTrayState();

        // 休息一開始就丟棄未儲存的修改,避免有人在休息中途改短休息時長。
        if (to != BreakState.Working)
            CloseSettingsWindow();

        switch (to)
        {
            case BreakState.BreakLocked:
                CloseToast();
                ShowOverlays();
                if (AnimationsEnabled)
                    _frame.Start();
                break;

            case BreakState.BreakReady:
                _frame.Stop();
                foreach (var overlay in _overlays)
                    overlay.ShowReadyState(AnimationsEnabled);
                break;

            case BreakState.Working:
                _frame.Stop();
                CloseOverlays();
                _toastDismissed = false;
                break;
        }
    }

    private void ShowOverlays()
    {
        CloseOverlays();

        BreakOverlayWindow? primary = null;
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var overlay = new BreakOverlayWindow(_machine, screen.Primary, screen.Bounds);
            overlay.UnlockRequested += () => _machine.Unlock();
            _overlays.Add(overlay);
            overlay.Show();
            if (screen.Primary)
                primary = overlay;
        }

        primary?.Activate();
    }

    private void CloseOverlays()
    {
        foreach (var overlay in _overlays)
            overlay.CloseForReal();
        _overlays.Clear();
    }

    private void RefreshOverlays(bool animated)
    {
        foreach (var overlay in _overlays)
            overlay.Refresh(animated);
    }

    /// <summary>休息期間每秒重新蓋回全螢幕範圍並把焦點拉回主螢幕,避免被其他視窗搶走。</summary>
    private void KeepOverlaysOnTop()
    {
        foreach (var overlay in _overlays)
        {
            overlay.ApplyBounds();
            if (overlay.IsPrimary && !overlay.IsActive)
                overlay.Activate();
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // 這個事件可能在非 UI 執行緒觸發。
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_machine.State == BreakState.Working)
                return;

            var wasReady = _machine.State == BreakState.BreakReady;
            ShowOverlays();
            if (wasReady)
            {
                foreach (var overlay in _overlays)
                    overlay.ShowReadyState(animated: false);
            }
            else
            {
                RefreshOverlays(AnimationsEnabled);
            }
        }));
    }

    private void UpdateToast()
    {
        if (!_machine.IsWarning || _toastDismissed)
        {
            CloseToast();
            return;
        }

        if (_toast is null)
        {
            _toast = new WarningToastWindow();
            _toast.Dismissed += () =>
            {
                _toastDismissed = true;
                CloseToast();
            };
            _toast.SnoozeRequested += delay =>
            {
                if (_machine.Snooze(delay))
                    CloseToast();
            };
            _toast.SkipRequested += () =>
            {
                if (_machine.SkipBreak())
                    CloseToast();
            };
            _toast.SetRemaining(_machine.WorkRemaining);
            _toast.SetSnoozesRemaining(_machine.SnoozesRemaining);
            _toast.Show();
        }

        _toast.SetRemaining(_machine.WorkRemaining);
        _toast.SetSnoozesRemaining(_machine.SnoozesRemaining);
    }

    private void CloseToast()
    {
        _toast?.Close();
        _toast = null;
    }

    private void OpenSettings()
    {
        if (_machine.State != BreakState.Working)
            return;

        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var window = new SettingsWindow(_config);
        window.SaveRequested += OnSettingsSaved;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_settingsWindow, window))
                _settingsWindow = null;
        };
        _settingsWindow = window;
        window.Show();
        window.Activate();
    }

    private void CloseSettingsWindow()
    {
        var window = _settingsWindow;
        _settingsWindow = null;
        window?.CloseWithoutSaving();
    }

    private void OnSettingsSaved(AppConfig newConfig)
    {
        // 這個分支只會在 Working 狀態發生(設定視窗在休息開始時就已經關閉),
        // 但狀態機自己也會擋,所以不會有任何狀態把休息時長偷偷改掉。
        if (!_machine.ApplySettings(newConfig.ToBreakSettings()))
            return;

        _config = newConfig;
        if (!newConfig.SmartPause)
            ClearAway();
        _toastDismissed = false;
        if (!_store.Save(newConfig))
        {
            MessageBox.Show(
                $"無法寫入設定檔({_store.ConfigPath})。新設定這次執行中有效,但不會被記住。",
                "Time-to-stand-up",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void CreateTray()
    {
        _breakNowItem = new Forms.ToolStripMenuItem("立即休息");
        _breakNowItem.Click += (_, _) => _machine.StartBreakNow();

        // 「設定…」沒有密碼等保護(見 docs/ROADMAP.md),和「立即休息」「結束」一樣只在 Working 狀態可用。
        _settingsItem = new Forms.ToolStripMenuItem("設定…");
        _settingsItem.Click += (_, _) => OpenSettings();

        // 「結束」不需驗證,只在 Working 狀態可用(休息中不能用它逃避休息)。
        _exitItem = new Forms.ToolStripMenuItem("結束");
        _exitItem.Click += (_, _) =>
        {
            if (_machine.State == BreakState.Working)
                Application.Current.Shutdown();
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_breakNowItem);
        menu.Items.Add(_settingsItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_exitItem);

        _trayIcon = CreateTrayIcon();
        _tray = new Forms.NotifyIcon
        {
            Icon = _trayIcon,
            Text = "Time-to-stand-up",
            ContextMenuStrip = menu,
            Visible = true,
        };

        UpdateTrayState();
    }

    private void UpdateTrayState()
    {
        var working = _machine.State == BreakState.Working;
        if (_breakNowItem is not null)
            _breakNowItem.Enabled = working;
        if (_settingsItem is not null)
            _settingsItem.Enabled = working;
        if (_exitItem is not null)
            _exitItem.Enabled = working;
    }

    /// <summary>工作列(系統匣所在)目前是否為淺色;讀不到時視為深色(Windows 11 預設)。</summary>
    private static bool TaskbarIsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
    }

    /// <summary>依工作列深淺色載入內嵌 .ico:淺色用黑色人物、深色用白色人物,並挑系統小圖示尺寸的影格。</summary>
    private static Drawing.Icon CreateTrayIcon()
    {
        var file = TaskbarIsLight() ? "Time-to-stand-up.ico" : "Time-to-stand-up-white.ico";
        var info = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{file}"));
        using var stream = info.Stream;
        return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle))
            return;
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_tray is null) return;
            var old = _trayIcon;
            _trayIcon = CreateTrayIcon();
            _tray.Icon = _trayIcon;
            old?.Dispose();
        });
    }
}
