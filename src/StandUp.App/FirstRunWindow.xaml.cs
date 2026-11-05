using System;
using System.Windows;
using StandUp.Core;

namespace StandUp.App;

/// <summary>
/// 第一次執行時顯示:研究依據、預設選項(預選 30/5)與「開始使用」。
/// 按下「開始使用」才算完成;用右上角 X 關閉視為取消啟動(<see cref="Result"/> 維持 null)。
/// </summary>
public partial class FirstRunWindow : Window
{
    private AppTheme _theme;

    public FirstRunWindow(AppConfig current)
    {
        InitializeComponent();
        GlassBackdrop.Attach(this);

        _theme = current.Theme;
        ThemeButton.Content = ThemeIcon();

        SettingsHint.Text = "之後可以從系統匣的「設定…」隨時調整。";

        // 首次啟動一律預選建議的 30/5;若設定檔已有其他有效數值(例如 firstRunCompleted 被手動改成 false)則沿用。
        Picker.SetValues(current.WorkMinutes, current.BreakMinutes);
        Picker.SelectionChanged += (_, _) => StartButton.IsEnabled = Picker.IsValid;
        StartButton.IsEnabled = Picker.IsValid;
    }

    /// <summary>按下「開始使用」後的設定(firstRunCompleted = true);取消則為 null。</summary>
    public AppConfig? Result { get; private set; }

    private void OnStartClick(object sender, RoutedEventArgs e)
    {
        if (!Picker.IsValid)
            return;

        Result = new AppConfig(Picker.WorkMinutes, Picker.BreakMinutes, FirstRunCompleted: true, Theme: _theme);
        DialogResult = true;
    }

    private void OnThemeClick(object sender, RoutedEventArgs e)
    {
        _theme = ThemeManager.Toggle();
        ThemeButton.Content = ThemeIcon();
    }

    /// <summary>按鈕顯示「按下去會變成什麼」:現在是深色就顯示太陽,淺色就顯示月亮。</summary>
    private static string ThemeIcon() => ThemeManager.IsLight ? "" : "";
}
