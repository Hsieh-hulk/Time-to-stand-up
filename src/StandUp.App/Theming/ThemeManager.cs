using System;
using System.Windows;
using Microsoft.Win32;
using StandUp.Core;

namespace StandUp.App;

/// <summary>
/// 決定玻璃主題(深色/淺色):設定為 System 時跟隨 Windows 的「應用程式模式」並即時反應系統切換,
/// 否則固定用使用者選的那一種。主題只是 Application 層的一組畫刷,
/// 樣式用 DynamicResource 讀取,所以換主題不必重建視窗。
/// </summary>
internal static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static ResourceDictionary? _current;

    public static bool IsLight { get; private set; }

    /// <summary>目前的設定(System / Dark / Light),不是解析後的結果。</summary>
    public static AppTheme Setting { get; private set; } = AppTheme.System;

    /// <summary>主題換過之後觸發(已在 UI 執行緒)。</summary>
    public static event Action? Changed;

    public static void Initialize(AppTheme setting)
    {
        SetTheme(setting);
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color))
                return;

            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (Setting == AppTheme.System)
                    SetTheme(AppTheme.System);
            });
        };
    }

    public static void SetTheme(AppTheme setting)
    {
        Setting = setting;
        var light = setting switch
        {
            AppTheme.Light => true,
            AppTheme.Dark => false,
            _ => ReadSystemIsLight(),
        };

        if (_current is not null && light == IsLight)
        {
            Changed?.Invoke();
            return;
        }

        Apply(light);
    }

    /// <summary>在深色與淺色之間切換,回傳切換後明確的設定(Dark 或 Light)。</summary>
    public static AppTheme Toggle()
    {
        var next = IsLight ? AppTheme.Dark : AppTheme.Light;
        SetTheme(next);
        return next;
    }

    private static void Apply(bool light)
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current is not null)
            merged.Remove(_current);

        var name = light ? "Glass.Light" : "Glass.Dark";
        _current = new ResourceDictionary { Source = new Uri($"/StandUp;component/Theming/Themes/{name}.xaml", UriKind.Relative) };
        merged.Add(_current);

        IsLight = light;
        Changed?.Invoke();
    }

    private static bool ReadSystemIsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch
        {
            return false;
        }
    }
}
