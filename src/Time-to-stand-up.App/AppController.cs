using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

    /// <summary>每隔幾個 tick 列舉一次行程(只在例外程式清單非空且工作中時)。</summary>
    private const int ProcessPollTicks = 2;

    private const string TrayTitle = "Time-to-stand-up";

    private readonly IClock _clock = new SystemClock();
    private readonly BreakStateMachine _machine;
    private readonly PresenceTracker _tracker;
    private readonly ProcessPauseGate _gate;
    private string? _pausedBy;
    private int _processPollCountdown;
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

    // 休息期間在所有螢幕上活動的寵物(一隻,共用)。螢幕由左到右串成一條軌道,見 PetWorld。
    private PetBrain? _pet;
    private PetWorld? _petWorld;
    private Forms.Screen[] _petScreens = Array.Empty<Forms.Screen>();
    private readonly Stopwatch _petClock = new();

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
        _gate = new ProcessPauseGate(_machine);
        _gate.PausedChanged += _ => UpdateTrayText();

        _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => OnTick();

        _frame = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AnimationFrameMs) };
        _frame.Tick += (_, _) => OnFrame();
    }

    public void Start()
    {
        CreateTray();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
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

        // 例外程式執行中:工作計時持續重設,不會到點。
        UpdateProcessPause();

        // 離開期間不跳出休息畫面:人不在,回來時再由 PresenceTracker 調整工作計時。
        _machine.Update(canInterrupt: !_tracker.IsAway && !_gate.IsPaused);

        switch (_machine.State)
        {
            case BreakState.Working:
                // 人不在時不顯示休息前提示(否則到點後會一直停在 00:00);例外程式執行中也不顯示。
                if (_tracker.IsAway || _gate.IsPaused)
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

    private void OnFrame()
    {
        if (_pet is not null)
        {
            var dt = _petClock.Elapsed.TotalSeconds;
            _petClock.Restart();
            _pet.Update(dt, CursorOnTrack());
        }

        RefreshOverlays(animated: true);
    }

    /// <summary>滑鼠游標在寵物軌道上的位置(參考單位);不在任何螢幕上時為 null。</summary>
    private (double X, double Y)? CursorOnTrack()
    {
        if (_petWorld is null)
            return null;

        var cursor = Forms.Cursor.Position;
        for (var i = 0; i < _petScreens.Length; i++)
        {
            var bounds = _petScreens[i].Bounds;
            if (!bounds.Contains(cursor) || bounds.Height <= 0)
                continue;

            var unit = PetWorld.ReferenceHeight / bounds.Height;
            return (_petWorld.ScreenStart(i) + (cursor.X - bounds.X) * unit, (cursor.Y - bounds.Y) * unit);
        }

        return null;
    }

    /// <summary>
    /// 每 <see cref="ProcessPollTicks"/> 秒列舉一次行程,其餘 tick 沿用上次結果。
    /// 清單為空或不在工作狀態時完全不列舉,不增加耗電。
    /// </summary>
    private void UpdateProcessPause()
    {
        var watched = _config.PauseProcesses;
        if (watched.Count == 0 || _machine.State != BreakState.Working)
        {
            _processPollCountdown = 0;
            SetPausedBy(null);
        }
        else if (--_processPollCountdown <= 0)
        {
            _processPollCountdown = ProcessPollTicks;
            SetPausedBy(ProcessWatchList.FindRunning(watched, RunningProcesses.GetNames()));
        }

        _gate.Update(_pausedBy is not null);
    }

    private void SetPausedBy(string? name)
    {
        if (name == _pausedBy)
            return;
        _pausedBy = name;
        UpdateTrayText();
    }

    /// <summary>系統匣提示文字;暫停中顯示是哪個程式。NotifyIcon.Text 最多 63 個字元。</summary>
    private void UpdateTrayText()
    {
        if (_tray is null)
            return;
        var text = _gate.IsPaused && _pausedBy is not null
            ? $"{TrayTitle}(暫停中:{_pausedBy})"
            : TrayTitle;
        _tray.Text = text.Length <= 63 ? text : text[..62] + "…";
    }

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
                // frame timer 不停:沙漏已靜止,但寵物要繼續活動到解鎖。
                _pet?.Celebrate();
                foreach (var overlay in _overlays)
                    overlay.ShowReadyState(AnimationsEnabled);
                break;

            case BreakState.Working:
                _frame.Stop();
                CloseOverlays();
                _pet = null;
                _petWorld = null;
                _toastDismissed = false;
                break;
        }
    }

    private void ShowOverlays()
    {
        CloseOverlays();

        // 螢幕由左到右排成寵物的軌道;螢幕配置改變時沿用同一隻寵物,只換世界。
        _petScreens = Forms.Screen.AllScreens.OrderBy(screen => screen.Bounds.X).ToArray();
        _petWorld = new PetWorld(_petScreens
            .Select(screen => PetWorld.ScreenLength(screen.Bounds.Width, screen.Bounds.Height))
            .ToArray());
        if (_pet is null)
        {
            var primaryIndex = Math.Max(0, Array.FindIndex(_petScreens, screen => screen.Primary));
            var startX = _petWorld.ScreenStart(primaryIndex) + _petWorld.ScreenLengths[primaryIndex] * 0.25;
            _pet = new PetBrain(_petWorld, PetProfile.For(_config.Pet), new Random(), startX);
            _petClock.Restart();
        }
        else
        {
            _pet.SetWorld(_petWorld);
        }

        var sprites = SpriteSheet.Load(_config.Pet);

        BreakOverlayWindow? primary = null;
        for (var i = 0; i < _petScreens.Length; i++)
        {
            var screen = _petScreens[i];
            var overlay = new BreakOverlayWindow(
                _machine, screen.Primary, screen.Bounds, _pet, sprites, _petWorld.ScreenStart(i));
            overlay.UnlockRequested += () => _machine.Unlock();
            overlay.PlatformsChanged += OnPetPlatformsChanged;
            _overlays.Add(overlay);
            overlay.Show();
            if (screen.Primary)
                primary = overlay;
        }

        primary?.Activate();
    }

    private void OnPetPlatformsChanged(IReadOnlyList<PetPlatform> platforms)
    {
        if (_pet is null || _petWorld is null)
            return;
        _petWorld = _petWorld.WithPlatforms(platforms);
        _pet.SetWorld(_petWorld);
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
        // 例外程式清單可能變了:下一個 tick 立刻重新列舉。
        _processPollCountdown = 0;
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

        // 左鍵單擊直接開設定(和選單的「設定…」同一條路徑,休息中 OpenSettings 會自己擋掉)。
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                OpenSettings();
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

    /// <summary>載入內嵌 .ico(有色底,深淺色工作列皆清楚),並挑系統小圖示尺寸的影格。</summary>
    private static Drawing.Icon CreateTrayIcon()
    {
        var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Time-to-stand-up.ico"));
        using var stream = info.Stream;
        return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }
}
