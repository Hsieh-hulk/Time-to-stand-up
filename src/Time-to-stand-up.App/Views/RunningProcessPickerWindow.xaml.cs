using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TimeToStandUp.App;

/// <summary>列出目前有視窗的程式,讓使用者挑選要加入例外清單的程式(可多選)。</summary>
public partial class RunningProcessPickerWindow : Window
{
    /// <summary>清單中的一列;<see cref="AlreadyAdded"/> 只用來顯示提示。</summary>
    public sealed record Row(string Name, string WindowTitle, bool AlreadyAdded);

    private readonly HashSet<string> _existing;

    public RunningProcessPickerWindow(IEnumerable<string> existing)
    {
        InitializeComponent();
        GlassBackdrop.Attach(this);
        _existing = new HashSet<string>(existing);
        Loaded += (_, _) =>
        {
            Reload();
            CandidateList.Focus();
        };
    }

    /// <summary>按「加入」後選取的程式名稱(已正規化)。</summary>
    public IReadOnlyList<string> SelectedNames { get; private set; } = new List<string>();

    private void Reload()
    {
        var rows = RunningProcesses.GetCandidates()
            .Select(c => new Row(c.Name, c.WindowTitle, _existing.Contains(c.Name)))
            .ToList();
        CandidateList.ItemsSource = rows;
        EmptyHint.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateAddEnabled();
    }

    private void UpdateAddEnabled() =>
        AddButton.IsEnabled = CandidateList.SelectedItems.Cast<Row>().Any(r => !r.AlreadyAdded);

    private void Accept()
    {
        SelectedNames = CandidateList.SelectedItems.Cast<Row>()
            .Where(r => !r.AlreadyAdded)
            .Select(r => r.Name)
            .ToList();
        if (SelectedNames.Count == 0)
            return;
        DialogResult = true;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateAddEnabled();

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(CandidateList, source) is ListBoxItem)
            Accept();
    }

    private void OnAddClick(object sender, RoutedEventArgs e) => Accept();

    private void OnRefreshClick(object sender, RoutedEventArgs e) => Reload();
}
