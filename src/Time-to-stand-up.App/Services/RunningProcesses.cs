using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using TimeToStandUp.Core;

namespace TimeToStandUp.App;

/// <summary>挑選視窗中的一個候選程式:正規化後的名稱與目前的視窗標題(只用來顯示)。</summary>
internal sealed record ProcessCandidate(string Name, string WindowTitle);

/// <summary>
/// 讀取目前執行中的行程。只讀名稱與主視窗標題,不讀路徑或命令列,也不寫入任何記錄。
/// 讀不到的行程(權限不足、剛好結束)直接略過,不丟例外。
/// </summary>
internal static class RunningProcesses
{
    /// <summary>所有執行中行程的名稱(不含 .exe)。</summary>
    public static List<string> GetNames()
    {
        var names = new List<string>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                names.Add(process.ProcessName);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }
        return names;
    }

    /// <summary>
    /// 挑選視窗用:有可見主視窗的程式,依名稱去重、排序,排除自己。
    /// 背景程式(沒有主視窗)不列出,仍可手動輸入名稱加入。
    /// </summary>
    public static List<ProcessCandidate> GetCandidates()
    {
        var selfId = Environment.ProcessId;
        var byName = new Dictionary<string, ProcessCandidate>(StringComparer.Ordinal);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == selfId || process.MainWindowHandle == IntPtr.Zero)
                    continue;
                var title = process.MainWindowTitle;
                if (string.IsNullOrWhiteSpace(title))
                    continue;
                if (ProcessWatchList.Normalize(process.ProcessName) is { } name && !byName.ContainsKey(name))
                    byName[name] = new ProcessCandidate(name, title);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }
        return byName.Values.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
    }
}
