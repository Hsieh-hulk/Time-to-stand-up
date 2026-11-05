using System;
using System.Windows;
using Microsoft.Win32;
using TimeToStandUp.Core;

namespace TimeToStandUp.App;

/// <summary>
/// 把 Windows 的鎖定、睡眠事件轉成 <see cref="AwayReason"/>。
/// SystemEvents 的事件可能在非 UI 執行緒觸發,一律轉回 UI 執行緒再通知。
/// </summary>
internal sealed class PresenceMonitor : IDisposable
{
    public PresenceMonitor()
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    /// <summary>
    /// 參數為 (原因, 是否開始離開, 事件發生的時間)。在 UI 執行緒觸發。
    /// 時間在收到系統事件當下就記下:睡眠前轉回 UI 執行緒的工作可能要等到喚醒後才執行。
    /// </summary>
    public event Action<AwayReason, bool, DateTimeOffset>? AwayChanged;

    public void Dispose()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.ConsoleDisconnect:
            case SessionSwitchReason.RemoteDisconnect:
                Raise(AwayReason.Locked, true);
                break;
            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.ConsoleConnect:
            case SessionSwitchReason.RemoteConnect:
                Raise(AwayReason.Locked, false);
                break;
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                Raise(AwayReason.Suspended, true);
                break;
            case PowerModes.Resume:
                Raise(AwayReason.Suspended, false);
                break;
        }
    }

    private void Raise(AwayReason reason, bool on)
    {
        var at = DateTimeOffset.UtcNow;
        Application.Current?.Dispatcher.BeginInvoke(new Action(() => AwayChanged?.Invoke(reason, on, at)));
    }
}
