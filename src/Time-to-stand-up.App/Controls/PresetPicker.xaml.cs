using System;
using System.Windows;
using System.Windows.Controls;
using TimeToStandUp.Core;

namespace TimeToStandUp.App;

/// <summary>
/// 三個預設選項。選預設時輸入框同步成該預設的分鐘數,所以切到「自訂」時會帶著上一個選項的值。
/// 自訂的輸入只接受範圍內的整數,不合法時顯示提示並讓 <see cref="IsValid"/> 為 false。
/// </summary>
public partial class PresetPicker : UserControl
{
    private bool _suppress;

    public PresetPicker()
    {
        InitializeComponent();
        StandardDescription.Text = HealthCopy.StandardRecommendation;
    }

    /// <summary>選項或輸入改變時觸發。</summary>
    public event EventHandler? SelectionChanged;

    public bool IsValid => MinutesInput.ValidateWork(WorkBox.Text).IsValid
                           && MinutesInput.ValidateBreak(BreakBox.Text).IsValid;

    /// <summary>目前選到的分鐘數。只在 <see cref="IsValid"/> 為 true 時有意義。</summary>
    public int WorkMinutes => MinutesInput.ValidateWork(WorkBox.Text).Value;

    public int BreakMinutes => MinutesInput.ValidateBreak(BreakBox.Text).Value;

    /// <summary>以分鐘數設定選項:30/5 與 60/10 選到對應預設,其餘選「自訂」。</summary>
    public void SetValues(int workMinutes, int breakMinutes)
    {
        _suppress = true;
        WorkBox.Text = workMinutes.ToString();
        BreakBox.Text = breakMinutes.ToString();
        switch (AppConfig.PresetFor(workMinutes, breakMinutes))
        {
            case ReminderPreset.Standard:
                StandardRadio.IsChecked = true;
                break;
            case ReminderPreset.Relaxed:
                RelaxedRadio.IsChecked = true;
                break;
            default:
                CustomRadio.IsChecked = true;
                break;
        }
        _suppress = false;

        Refresh();
    }

    private void OnPresetChecked(object sender, RoutedEventArgs e)
    {
        if (_suppress)
            return;

        _suppress = true;
        if (ReferenceEquals(sender, StandardRadio))
            FillBoxes(AppConfig.DefaultWorkMinutes, AppConfig.DefaultBreakMinutes);
        else if (ReferenceEquals(sender, RelaxedRadio))
            FillBoxes(60, 10);
        _suppress = false;

        Refresh();
    }

    private void OnCustomTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_suppress)
            Refresh();
    }

    private void FillBoxes(int work, int rest)
    {
        WorkBox.Text = work.ToString();
        BreakBox.Text = rest.ToString();
    }

    private void Refresh()
    {
        var custom = CustomRadio.IsChecked == true;
        CustomPanel.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;

        ShowError(WorkError, custom ? MinutesInput.ValidateWork(WorkBox.Text).Error : null);
        ShowError(BreakError, custom ? MinutesInput.ValidateBreak(BreakBox.Text).Error : null);

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void ShowError(TextBlock target, string? error)
    {
        target.Text = error ?? string.Empty;
        target.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
