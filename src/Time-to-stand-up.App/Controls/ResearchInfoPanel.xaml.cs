using System.Windows;
using System.Windows.Controls;
using TimeToStandUp.Core;

namespace TimeToStandUp.App;

/// <summary>研究依據、研究限制與免責聲明。內容全部來自 <see cref="HealthCopy"/>。</summary>
public partial class ResearchInfoPanel : UserControl
{
    public ResearchInfoPanel()
    {
        InitializeComponent();

        HeadingText.Text = LetterSpacing.Apply(HealthCopy.ResearchHeading);
        IntroText.Text = LetterSpacing.Apply(HealthCopy.ResearchIntro);
        StudyList.ItemsSource = HealthCopy.Studies;
        LimitationsHeadingText.Text = LetterSpacing.Apply(HealthCopy.LimitationsHeading);
        LimitationsText.Text = LetterSpacing.Apply(HealthCopy.StudyLimitations);
        CannotVerifyText.Text = LetterSpacing.Apply(HealthCopy.CannotVerifyWalking);
        DisclaimerText.Text = LetterSpacing.Apply(HealthCopy.Disclaimer);
        AboutHeadingText.Text = LetterSpacing.Apply(HealthCopy.AboutHeading);
        AboutBodyText.Text = LetterSpacing.Apply(HealthCopy.AboutText);
    }

    /// <summary>是否在最後顯示「關於」段落。首次啟動畫面不需要。</summary>
    public bool ShowAbout
    {
        get => AboutSection.Visibility == Visibility.Visible;
        set => AboutSection.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }
}
