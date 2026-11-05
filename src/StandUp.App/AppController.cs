using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using StandUp.Core;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace StandUp.App;

/// <summary>
/// 把狀態機接到 UI:每秒推進狀態、管理休息畫面(每個螢幕一個)、休息前提示與系統匣。
/// M2 的系統匣有「立即休息」「設定…」與「結束」,完整系統匣是 M6。
/// </summary>
internal sealed class AppController : IDisposable
{
    private const int AnimationFrameMs = 33; // 約 30 fps,動畫的上限

    private readonly BreakStateMachine _machine;
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
        _machine = new BreakStateMachine(new SystemClock(), settings);
        _machine.StateChanged += OnStateChanged;

        _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => OnTick();

        _frame = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AnimationFrameMs) };
        _frame.Tick += (_, _) => OnFrame();
    }

    public void Start()
    {
        CreateTray();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _tick.Start();
    }

    public void Dispose()
    {
        _tick.Stop();
        _frame.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
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
        _machine.Update();

        switch (_machine.State)
        {
            case BreakState.Working:
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
            _toast.SetRemaining(_machine.WorkRemaining);
            _toast.Show();
        }

        _toast.SetRemaining(_machine.WorkRemaining);
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
        _toastDismissed = false;
        if (!_store.Save(newConfig))
        {
            MessageBox.Show(
                $"無法寫入設定檔({_store.ConfigPath})。新設定這次執行中有效,但不會被記住。",
                "StandUp 站起來",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void CreateTray()
    {
        _breakNowItem = new Forms.ToolStripMenuItem("立即休息");
        _breakNowItem.Click += (_, _) => _machine.StartBreakNow();

        // 「設定…」沒有任何保護:SPEC 3.5 尚未定案(M5),定案前和「立即休息」「結束」一樣只在 Working 狀態可用。
        _settingsItem = new Forms.ToolStripMenuItem("設定…");
        _settingsItem.Click += (_, _) => OpenSettings();

        // M1 的測試輔助。「結束」是否需要驗證取決於 SPEC 3.5(尚未定案),定案前只在 Working 狀態可用。
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
            Text = "StandUp 站起來",
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

    /// <summary>用程式畫一個簡單的圖示(橘底白字「站」),不依賴外部圖檔。</summary>
    private static Drawing.Icon CreateTrayIcon()
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Drawing.Color.Transparent);
            using var fill = new Drawing.SolidBrush(Drawing.Color.FromArgb(0xE0, 0x8A, 0x2E));
            g.FillEllipse(fill, 1, 1, 30, 30);
            using var font = new Drawing.Font("Microsoft JhengHei UI", 17, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
            var format = new Drawing.StringFormat
            {
                Alignment = Drawing.StringAlignment.Center,
                LineAlignment = Drawing.StringAlignment.Center,
            };
            g.DrawString("站", font, Drawing.Brushes.White, new Drawing.RectangleF(0, 1, 32, 32), format);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temp = Drawing.Icon.FromHandle(handle);
            return (Drawing.Icon)temp.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }
}
