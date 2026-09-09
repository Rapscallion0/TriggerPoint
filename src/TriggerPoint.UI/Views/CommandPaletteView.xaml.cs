using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;

namespace TriggerPoint.UI.Views;

public class PaletteItemViewModel
{
    public TriggerItem Item { get; }
    public FuzzyMatchResult MatchResult { get; }
    public string Name => Item.Name;
    public string Description => Item.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public string HotkeyText => Item.Hotkey?.DisplayText ?? string.Empty;
    public Visibility HasHotkey => !string.IsNullOrWhiteSpace(HotkeyText) ? Visibility.Visible : Visibility.Collapsed;
    public string LaunchCountBadge => Item.UsageStats.LaunchCount > 0 ? $"⚡ {Item.UsageStats.LaunchCount}" : "";

    public string IconSymbol => Item.ActionType switch
    {
        ActionType.Snippet => "📝",
        ActionType.Shell => "⚡",
        ActionType.Folder => "📁",
        _ => "▶"
    };

    public PaletteItemViewModel(TriggerItem item, FuzzyMatchResult matchResult)
    {
        Item = item;
        MatchResult = matchResult;
    }
}

public partial class CommandPaletteView : Window
{
    private readonly List<TriggerItem> _allItems;
    private readonly IActionExecutor _executor;
    private readonly Guid? _scopedFolderId;

    public CommandPaletteView(
        IEnumerable<TriggerItem> items, 
        IActionExecutor executor, 
        Guid? scopedFolderId = null,
        string? scopedFolderName = null)
    {
        InitializeComponent();
        _executor = executor;
        _scopedFolderId = scopedFolderId;

        // Filter items if scoped to a folder
        if (scopedFolderId.HasValue)
        {
            _allItems = items.Where(x => x.ParentId == scopedFolderId.Value && x.ActionType != ActionType.Folder).ToList();
            ScopeBadge.Visibility = Visibility.Visible;
            ScopeText.Text = scopedFolderName ?? "Scoped";
        }
        else
        {
            _allItems = items.Where(x => x.ActionType != ActionType.Folder).ToList();
            ScopeBadge.Visibility = Visibility.Collapsed;
        }

        Loaded += (s, e) =>
        {
            SearchTextBox.Focus();
            FilterResults();
        };
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterResults();
    }

    private void FilterResults()
    {
        var query = SearchTextBox.Text.Trim();
        var ranked = FuzzyMatcher.FilterAndRank(_allItems, query);

        var vms = ranked.Select(r => new PaletteItemViewModel(r.Item, r)).ToList();
        ResultsListBox.ItemsSource = vms;

        if (vms.Count > 0)
        {
            ResultsListBox.SelectedIndex = 0;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            if (ResultsListBox.SelectedIndex < ResultsListBox.Items.Count - 1)
            {
                ResultsListBox.SelectedIndex++;
                ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up)
        {
            if (ResultsListBox.SelectedIndex > 0)
            {
                ResultsListBox.SelectedIndex--;
                ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            ExecuteCurrentSelection(DetermineOverride());
            e.Handled = true;
        }
    }

    private static ExecutionOverride DetermineOverride()
    {
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            return ExecutionOverride.RunAsAdmin;
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            return ExecutionOverride.RevealInExplorer;
        if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
            return ExecutionOverride.OpenSettings;
        return ExecutionOverride.Standard;
    }

    private void ResultsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ExecuteCurrentSelection(DetermineOverride());
    }

    private void ExecuteCurrentSelection(ExecutionOverride executionOverride)
    {
        if (ResultsListBox.SelectedItem is PaletteItemViewModel vm)
        {
            Close();
            _ = _executor.ExecuteAsync(vm.Item, executionOverride);
        }
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        Close();
    }
}
