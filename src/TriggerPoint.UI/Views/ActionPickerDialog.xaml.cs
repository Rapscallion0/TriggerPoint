using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TriggerPoint.Core.Models;

namespace TriggerPoint.UI.Views;

public partial class ActionPickerDialog : Window
{
    private readonly List<ActionPickerItemViewModel> _allItems = [];
    private List<ActionPickerItemViewModel> _filteredItems = [];

    public TriggerItem? SelectedItem { get; private set; }

    public ActionPickerDialog(IEnumerable<TriggerItem> items, Guid? currentItemId, Guid? preselectedItemId)
    {
        InitializeComponent();

        var itemList = items.ToList();
        var itemMap = itemList.ToDictionary(i => i.Id);

        var eligible = itemList
            .Where(i => !currentItemId.HasValue || i.Id != currentItemId.Value)
            .OrderBy(i => i.ActionType == ActionType.Folder ? 0 : 1)
            .ThenBy(i => i.Name);

        foreach (var item in eligible)
        {
            string path = BuildPath(item, itemMap);
            _allItems.Add(new ActionPickerItemViewModel(item, path));
        }

        Loaded += (s, e) =>
        {
            ApplyFilter(string.Empty);

            if (preselectedItemId.HasValue)
            {
                var match = _filteredItems.FirstOrDefault(vm => vm.Item.Id == preselectedItemId.Value);
                if (match != null)
                {
                    ItemsListBox.SelectedItem = match;
                    ItemsListBox.ScrollIntoView(match);
                }
            }

            SearchBox.Focus();
        };
    }

    private static string BuildPath(TriggerItem item, Dictionary<Guid, TriggerItem> map)
    {
        var parts = new List<string>();
        var current = item;
        while (current.ParentId.HasValue && map.TryGetValue(current.ParentId.Value, out var parent))
        {
            parts.Insert(0, parent.Name);
            current = parent;
        }

        return parts.Count > 0 ? string.Join("  ›  ", parts) : "Root Level";
    }

    private void ApplyFilter(string query)
    {
        var trimmed = query.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            _filteredItems = [.. _allItems];
        }
        else
        {
            _filteredItems = _allItems
                .Where(vm => vm.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                             vm.PathDisplay.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                             vm.TypeBadgeText.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        ItemsListBox.ItemsSource = _filteredItems;
        StatusCountText.Text = $"{_filteredItems.Count} item{(_filteredItems.Count == 1 ? "" : "s")}";

        if (_filteredItems.Count > 0 && ItemsListBox.SelectedItem == null)
        {
            ItemsListBox.SelectedIndex = 0;
        }
        UpdateSelectButton();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter(SearchBox.Text);
    }

    private void ItemsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectButton();
    }

    private void UpdateSelectButton()
    {
        SelectBtn.IsEnabled = ItemsListBox.SelectedItem is ActionPickerItemViewModel;
    }

    private void ItemsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsListBox.SelectedItem is ActionPickerItemViewModel selected)
        {
            SelectedItem = selected.Item;
            DialogResult = true;
            Close();
        }
    }

    private void SelectBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsListBox.SelectedItem is ActionPickerItemViewModel selected)
        {
            SelectedItem = selected.Item;
            DialogResult = true;
            Close();
        }
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (ItemsListBox.SelectedItem is ActionPickerItemViewModel selected)
            {
                SelectedItem = selected.Item;
                DialogResult = true;
                Close();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Down && SearchBox.IsFocused)
        {
            if (_filteredItems.Count > 0)
            {
                int nextIndex = Math.Min(ItemsListBox.SelectedIndex + 1, _filteredItems.Count - 1);
                ItemsListBox.SelectedIndex = nextIndex;
                ItemsListBox.ScrollIntoView(_filteredItems[nextIndex]);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Up && SearchBox.IsFocused)
        {
            if (_filteredItems.Count > 0)
            {
                int prevIndex = Math.Max(ItemsListBox.SelectedIndex - 1, 0);
                ItemsListBox.SelectedIndex = prevIndex;
                ItemsListBox.ScrollIntoView(_filteredItems[prevIndex]);
                e.Handled = true;
            }
        }
    }
}

public class ActionPickerItemViewModel
{
    public TriggerItem Item { get; }
    public string Name => Item.Name;
    public string IconSymbol { get; }
    public string PathDisplay { get; }
    public string TypeBadgeText { get; }

    public ActionPickerItemViewModel(TriggerItem item, string pathDisplay)
    {
        Item = item;
        PathDisplay = pathDisplay;
        IconSymbol = item.ActionType switch
        {
            ActionType.Folder => "📁",
            ActionType.Workflow => "🧱",
            ActionType.Shell => "⚡",
            ActionType.Snippet => "📝",
            _ => "🔹"
        };
        TypeBadgeText = item.ActionType switch
        {
            ActionType.Folder => item.PresentationMode == PresentationMode.Direct ? "Folder" : "Folder (Popup)",
            ActionType.Workflow => "Workflow",
            ActionType.Shell => "App & Command",
            ActionType.Snippet => "Snippet",
            _ => "Action"
        };
    }
}
