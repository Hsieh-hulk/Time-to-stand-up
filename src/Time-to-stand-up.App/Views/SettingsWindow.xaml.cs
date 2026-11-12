using System;
using System.Windows;
using TimeToStandUp.Core;

namespace TimeToStandUp.App;

/// <summary>
/// 設定視窗:提醒節奏、開機自動啟動、例外程式、主題。沒有密碼等保護機制(見 docs/ROADMAP.md)。
/// 休息一開始,呼叫端會用 <see cref="CloseWithoutSaving"/> 關掉它並丟棄未儲存的修改。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppConfig _current;
    private AppTheme _theme;
    private bool _saved;
    private readonly bool _autoStartInitial;

    public SettingsWindow(AppConfig current)
    {
        InitializeComponent();
        GlassBackdrop.Attach(this);
        _current = current;
        _theme = current.Theme;

        RestoreSavedSize(current);
        _lastNormalSize = new Size(Width, Height);

        SizeChanged += (_, e) =>
        {
            // 最大化時的尺寸不記,下次開啟用最後一次的一般大小。
            if (WindowState == WindowState.Normal)
                _lastNormalSize = e.NewSize;
            UpdateScale();
        };
        UpdateScale();

        ThemeButton.Content = ThemeIcon();
        // 沒按「儲存」就關掉,主題要回到儲存過的那一個。
        Closed += (_, _) =>
        {
            if (!_saved)
                ThemeManager.SetTheme(_current.Theme);
            // 儲存、取消、X、休息開始被強制關閉,都會記住這次的大小。
            SizeRemembered?.Invoke((int)Math.Round(_lastNormalSize.Width), (int)Math.Round(_lastNormalSize.Height));
        };

        _autoStartInitial = AutoStartManager.IsEnabled();
        AutoStartBox.IsChecked = _autoStartInitial;

        ApplyHint.Text = "儲存後立即生效,已累計的工作時間會保留。";
        Picker.SetValues(current.WorkMinutes, current.BreakMinutes);
        ProcessEditor.SetItems(current.PauseProcesses);
        PetPicker.Selected = current.Pet;
        Picker.SelectionChanged += (_, _) => UpdateSaveEnabled();
        UpdateSaveEnabled();
    }

    private Size _lastNormalSize;

    /// <summary>視窗關閉時觸發(寬、高),呼叫端存進設定檔。不經過 <see cref="SaveRequested"/>,不會重新套用計時。</summary>
    public event Action<int, int>? SizeRemembered;

    /// <summary>沿用上次的大小;限制在最小值與目前螢幕工作區之內(例如換到較小的螢幕)。位置仍置中。</summary>
    private void RestoreSavedSize(AppConfig config)
    {
        if (config.SettingsWidth is not { } width || config.SettingsHeight is not { } height)
            return;

        var area = SystemParameters.WorkArea;
        Width = Math.Clamp(width, MinWidth, Math.Max(MinWidth, area.Width));
        Height = Math.Clamp(height, MinHeight, Math.Max(MinHeight, area.Height));
    }

    private void UpdateSaveEnabled() => SaveButton.IsEnabled = Picker.IsValid;

    private const double DesignSize = 560;

    /// <summary>文字、圖示與框架一起等比例縮放:以 560 為基準,隨視窗大小放大,深淺色相同。</summary>
    private void UpdateScale()
    {
        double scale = Math.Min(Width / DesignSize, Height / DesignSize);
        if (IsLoaded)
            scale = Math.Min(ActualWidth / DesignSize, ActualHeight / DesignSize);
        scale = Math.Clamp(scale, 1.0, 2.5);
        UiScale.ScaleX = scale;
        UiScale.ScaleY = scale;
    }

    /// <summary>按下「儲存」且輸入合法時觸發,參數為要儲存並套用的設定。</summary>
    public event Action<AppConfig>? SaveRequested;

    /// <summary>關閉視窗並丟棄未儲存的修改(休息開始時由呼叫端使用)。</summary>
    public void CloseWithoutSaving() => Close();

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!Picker.IsValid)
            return;

        var autoStart = AutoStartBox.IsChecked == true;
        if (autoStart != _autoStartInitial && !AutoStartManager.SetEnabled(autoStart))
        {
            MessageBox.Show(this, "無法變更開機自動啟動的設定。", "Time-to-stand-up",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _saved = true;
        // 用 with 保留視窗沒有編輯的欄位(例如智慧暫停),避免儲存時被洗回預設。
        SaveRequested?.Invoke(_current with
        {
            WorkMinutes = Picker.WorkMinutes,
            BreakMinutes = Picker.BreakMinutes,
            Theme = _theme,
            PauseProcesses = ProcessEditor.Items,
            Pet = PetPicker.Selected,
        });
        Close();
    }

    private void OnThemeClick(object sender, RoutedEventArgs e)
    {
        _theme = ThemeManager.Toggle();
        ThemeButton.Content = ThemeIcon();
    }

    /// <summary>按鈕顯示「按下去會變成什麼」:現在是深色就顯示太陽,淺色就顯示月亮。</summary>
    private static string ThemeIcon() => ThemeManager.IsLight ? "" : "";

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>只回到視窗內的預設選項(30/5),按「儲存」才會真的寫入。</summary>
    private void OnResetClick(object sender, RoutedEventArgs e) =>
        Picker.SetValues(AppConfig.DefaultWorkMinutes, AppConfig.DefaultBreakMinutes);
}
