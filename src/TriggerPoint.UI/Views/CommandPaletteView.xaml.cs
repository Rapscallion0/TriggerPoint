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
using TriggerPoint.Infrastructure.Win32;

namespace TriggerPoint.UI.Views;

public class PaletteItemViewModel
{
    public TriggerItem Item { get; }
    public FuzzyMatchResult MatchResult { get; }
    public string Name => Item.Name;
    public string Description => Item.Description;
    public string? ParentPath { get; }

    public string SecondaryDetail
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Description))
                return Description;

            if (!string.IsNullOrWhiteSpace(ParentPath))
                return $"📁 {ParentPath}";

            if (Item.ActionType == ActionType.Shell && !string.IsNullOrWhiteSpace(Item.Payload.Command))
                return Item.Payload.Command;

            if (Item.ActionType == ActionType.Snippet && !string.IsNullOrWhiteSpace(Item.Payload.SnippetTemplate))
            {
                var clean = Item.Payload.SnippetTemplate.Replace("\r", " ").Replace("\n", " ").Trim();
                return clean.Length > 60 ? clean.Substring(0, 57) + "..." : clean;
            }

            return string.Empty;
        }
    }

    public Visibility HasSecondaryDetail => !string.IsNullOrWhiteSpace(SecondaryDetail) ? Visibility.Visible : Visibility.Collapsed;
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

    public Brush IconBrush
    {
        get
        {
            string key = Item.ActionType switch
            {
                ActionType.Folder => "FolderBrush",
                ActionType.Shell => "ShellBrush",
                ActionType.Snippet => "SnippetBrush",
                _ => "AccentBrush"
            };
            return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
        }
    }

    public PaletteItemViewModel(TriggerItem item, FuzzyMatchResult matchResult, string? parentPath = null)
    {
        Item = item;
        MatchResult = matchResult;
        ParentPath = parentPath;
    }
}

public partial class CommandPaletteView : Window
{
    private readonly List<TriggerItem> _allItems;
    private readonly IActionExecutor _executor;
    private readonly IntPtr _targetHwnd;
    private readonly Guid? _scopedFolderId;
    private readonly Dictionary<Guid, string> _folderPaths;

    public CommandPaletteView(
        IEnumerable<TriggerItem> items, 
        IActionExecutor executor, 
        Guid? scopedFolderId = null,
        string? scopedFolderName = null,
        IntPtr targetHwnd = default)
    {
        InitializeComponent();
        _executor = executor;
        _targetHwnd = targetHwnd;
        _scopedFolderId = scopedFolderId;
        _folderPaths = BuildFolderPaths(items);

        // Filter items if scoped to a folder (recursively includes subfolders)
        if (scopedFolderId.HasValue)
        {
            var descendantFolderIds = new HashSet<Guid> { scopedFolderId.Value };
            bool added;
            do
            {
                added = false;
                foreach (var item in items.Where(x => x.ActionType == ActionType.Folder && x.ParentId.HasValue))
                {
                    if (descendantFolderIds.Contains(item.ParentId!.Value) && descendantFolderIds.Add(item.Id))
                    {
                        added = true;
                    }
                }
            } while (added);

            _allItems = items.Where(x => x.ActionType != ActionType.Folder 
                                      && x.ParentId.HasValue 
                                      && descendantFolderIds.Contains(x.ParentId.Value)).ToList();
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
            try
            {
                var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                NativeMethods.SetForegroundWindow(handle);
            }
            catch { }

            SearchTextBox.Focus();
            Keyboard.Focus(SearchTextBox);
            FilterResults();
        };
    }

    private static Dictionary<Guid, string> BuildFolderPaths(IEnumerable<TriggerItem> allItems)
    {
        var itemsList = allItems.ToList();
        var folderDict = itemsList.Where(x => x.ActionType == ActionType.Folder).ToDictionary(x => x.Id);
        var result = new Dictionary<Guid, string>();

        foreach (var folder in folderDict.Values)
        {
            var segments = new List<string>();
            var curr = folder;
            while (curr != null)
            {
                segments.Insert(0, curr.Name);
                curr = curr.ParentId.HasValue && folderDict.TryGetValue(curr.ParentId.Value, out var parent) ? parent : null;
            }
            result[folder.Id] = string.Join(" › ", segments);
        }

        return result;
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterResults();
    }

    private void FilterResults()
    {
        var query = SearchTextBox.Text.Trim();
        var ranked = FuzzyMatcher.FilterAndRank(_allItems, query);

        var vms = ranked.Select(r => new PaletteItemViewModel(
            r.Item, 
            r, 
            r.Item.ParentId.HasValue && _folderPaths.TryGetValue(r.Item.ParentId.Value, out var path) ? path : null)).ToList();
        ResultsListBox.ItemsSource = vms;

        if (vms.Count > 0)
        {
            ResultsListBox.SelectedIndex = 0;
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            SafeClose();
            e.Handled = true;
            return;
        }

        if (key == Key.Down)
        {
            if (ResultsListBox.Items.Count > 0)
            {
                if (ResultsListBox.SelectedIndex < ResultsListBox.Items.Count - 1)
                {
                    ResultsListBox.SelectedIndex++;
                }
                else
                {
                    ResultsListBox.SelectedIndex = 0; // Wrap around to top
                }
                ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);
            }
            e.Handled = true;
            return;
        }

        if (key == Key.Up)
        {
            if (ResultsListBox.Items.Count > 0)
            {
                if (ResultsListBox.SelectedIndex > 0)
                {
                    ResultsListBox.SelectedIndex--;
                }
                else
                {
                    ResultsListBox.SelectedIndex = ResultsListBox.Items.Count - 1; // Wrap around to bottom
                }
                ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);
            }
            e.Handled = true;
            return;
        }

        if (key == Key.Enter)
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

    private bool _isClosing;

    private void SafeClose()
    {
        if (_isClosing) return;
        _isClosing = true;
        try
        {
            Close();
        }
        catch { }
    }

    private void ResultsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ExecuteCurrentSelection(DetermineOverride());
    }

    private void ExecuteCurrentSelection(ExecutionOverride executionOverride)
    {
        if (ResultsListBox.SelectedItem is PaletteItemViewModel vm)
        {
            SafeClose();
            _ = _executor.ExecuteAsync(vm.Item, executionOverride, _targetHwnd);
        }
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        SafeClose();
    }
}
