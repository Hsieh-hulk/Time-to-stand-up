using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TimeToStandUp.Core;

namespace TimeToStandUp.App;

/// <summary>
/// 編輯例外程式清單。所有名稱都以 <see cref="ProcessWatchList.Normalize"/> 正規化後存放,
/// 所以 <c>C:\x\Foo.EXE</c> 與 <c>foo</c> 視為同一個程式。
/// </summary>
public partial class ProcessListEditor : UserControl
{
    private readonly ObservableCollection<string> _items = new();

    public ProcessListEditor()
    {
        InitializeComponent();
        EntryList.ItemsSource = _items;
        _items.CollectionChanged += (_, _) => UpdateState();
        UpdateState();
    }

    /// <summary>目前清單(已正規化、去重)。</summary>
    public IReadOnlyList<string> Items => _items.ToArray();

    public void SetItems(IEnumerable<string> names)
    {
        _items.Clear();
        foreach (var name in ProcessWatchList.NormalizeAll(names, out _))
            _items.Add(name);
    }

    /// <summary>加入一個名稱;失敗時回傳錯誤訊息,成功回傳 null。</summary>
    private string? TryAdd(string? raw)
    {
        var name = ProcessWatchList.Normalize(raw);
        if (name is null)
            return "請輸入程式名稱,例如 POWERPNT.EXE(不可含 \\ / : * ? \" < > | 等字元)。";
        if (_items.Contains(name))
        {
            EntryList.SelectedItem = name;
            return $"「{name}」已在清單中。";
        }
        if (_items.Count >= ProcessWatchList.MaxEntries)
            return $"最多只能加入 {ProcessWatchList.MaxEntries} 個程式。";

        _items.Add(name);
        EntryList.SelectedItem = name;
        EntryList.ScrollIntoView(name);
        return null;
    }

    private void AddFromBox()
    {
        var error = TryAdd(NameBox.Text);
        ShowError(error);
        if (error is null)
            NameBox.Clear();
    }

    private void ShowError(string? message)
    {
        ErrorText.Text = message ?? "";
        ErrorText.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateState()
    {
        EmptyHint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RemoveButton.IsEnabled = EntryList.SelectedItem is not null;
    }

    private void OnNameChanged(object sender, TextChangedEventArgs e)
    {
        AddButton.IsEnabled = !string.IsNullOrWhiteSpace(NameBox.Text);
        ShowError(null);
    }

    // Enter 只加入清單,不觸發設定視窗的預設按鈕「儲存」。
    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;
        if (!string.IsNullOrWhiteSpace(NameBox.Text))
            AddFromBox();
    }

    private void OnAddClick(object sender, RoutedEventArgs e) => AddFromBox();

    private void OnRemoveClick(object sender, RoutedEventArgs e) => RemoveSelected();

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete)
            return;
        e.Handled = true;
        RemoveSelected();
    }

    private void RemoveSelected()
    {
        if (EntryList.SelectedItem is not string name)
            return;
        var index = EntryList.SelectedIndex;
        _items.Remove(name);
        if (_items.Count > 0)
        {
            EntryList.SelectedIndex = System.Math.Min(index, _items.Count - 1);
            (EntryList.ItemContainerGenerator.ContainerFromIndex(EntryList.SelectedIndex) as ListBoxItem)?.Focus();
        }
        else
        {
            NameBox.Focus();
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateState();

    private void OnPickClick(object sender, RoutedEventArgs e)
    {
        var picker = new RunningProcessPickerWindow(_items) { Owner = Window.GetWindow(this) };
        if (picker.ShowDialog() != true)
            return;
        string? lastError = null;
        foreach (var name in picker.SelectedNames)
            lastError = TryAdd(name) ?? lastError;
        ShowError(lastError);
    }
}
