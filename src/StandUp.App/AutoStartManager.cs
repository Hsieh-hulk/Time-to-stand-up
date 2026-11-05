using System;
using Microsoft.Win32;

namespace StandUp.App;

/// <summary>
/// 開機自動啟動:在 HKCU\...\Run 寫入或移除一筆值(不需要系統管理員權限)。
/// 是否啟用以登錄檔為準,不寫進 config.json,這樣使用者從工作管理員停用時也會同步。
/// </summary>
internal static class AutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "StandUp";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>設定開機啟動,成功回傳 true。</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enabled)
            {
                var path = Environment.ProcessPath;
                if (string.IsNullOrEmpty(path))
                    return false;
                key.SetValue(ValueName, $"\"{path}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
