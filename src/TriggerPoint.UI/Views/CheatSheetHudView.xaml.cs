using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Win32;

namespace TriggerPoint.UI.Views;

public class CheatSheetItemViewModel
{
    public TriggerItem Item { get; init; } = new();
    public string HotkeyText { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string ActionTypeText { get; init; } = "ACTION";
    public string ScopeText { get; init; } = "Global";
    public Brush ScopeBgBrush { get; init; } = Brushes.Transparent;
    public Brush ScopeBorderBrush { get; init; } = Brushes.Transparent;
    public Brush ScopeForegroundBrush { get; init; } = Brushes.Gray;
    public bool IsContextSpecific { get; init; }
}

public partial class CheatSheetHudView : Window
{
    private readonly List<CheatSheetItemViewModel> _allItems = [];
    private readonly Action<TriggerItem>? _onExecute;
    private readonly AppSettings? _appSettings;
    private readonly string? _activeProcessName;
    private bool _isLoaded;
    private DateTime _activatedTimestamp = DateTime.MinValue;

    public CheatSheetHudView(
        IEnumerable<TriggerItem> items,
        AppSettings appSettings,
        string? activeProcessName = null,
        Action<TriggerItem>? onExecute = null)
    {
        InitializeComponent();
        _appSettings = appSettings;
        _activeProcessName = activeProcessName;
        _onExecute = onExecute;

        Activated += (s, e) => _activatedTimestamp = DateTime.UtcNow;
        Loaded += Window_Loaded;
        BuildViewModels(items);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Smooth entrance animation
        if (_appSettings?.EnableUiAnimations == true)
        {
            var anim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(OpacityProperty, anim);
        }

        _activatedTimestamp = DateTime.UtcNow;
        SearchBox.Focus();
        _isLoaded = true;
    }

    private void BuildViewModels(IEnumerable<TriggerItem> items)
    {
        _allItems.Clear();

        string procName = !string.IsNullOrWhiteSpace(_activeProcessName) ? _activeProcessName : "Windows";
        ActiveContextText.Text = $"Active: {procName}";

        var accentBrush = Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.CornflowerBlue;
        var accentSubtle = Application.Current.TryFindResource("AccentSubtleBrush") as Brush ?? new SolidColorBrush(Color.FromArgb(0x20, 0x3B, 0x82, 0xF6));
        var greenBrush = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
        var greenSubtle = new SolidColorBrush(Color.FromArgb(0x20, 0x10, 0xB9, 0x81));
        var borderSubtle = Application.Current.TryFindResource("BorderSubtleBrush") as Brush ?? Brushes.DimGray;
        var textMuted = Application.Current.TryFindResource("TextMutedBrush") as Brush ?? Brushes.Gray;

        foreach (var item in items)
        {
            if (item.Hotkey == null || item.Hotkey.IsEmpty || !item.IsEnabled) continue;

            bool isContextMatch = false;
            if (!string.IsNullOrWhiteSpace(_activeProcessName) && item.ContextFilter != null)
            {
                if (item.ContextFilter.AllowedProcesses.Any(p => p.Equals(_activeProcessName, StringComparison.OrdinalIgnoreCase)))
                {
                    isContextMatch = true;
                }
            }

            string scopeText = isContextMatch ? $"App: {procName}" : (item.PresentationMode == PresentationMode.Direct ? "Direct" : "Global");
            var scopeBg = isContextMatch ? greenSubtle : (item.PresentationMode == PresentationMode.Direct ? accentSubtle : Brushes.Transparent);
            var scopeBorder = isContextMatch ? greenBrush : (item.PresentationMode == PresentationMode.Direct ? accentBrush : borderSubtle);
            var scopeFg = isContextMatch ? greenBrush : (item.PresentationMode == PresentationMode.Direct ? accentBrush : textMuted);

            string typeText = item.ActionType switch
            {
                ActionType.Shell => "APP",
                ActionType.Snippet => "SNIPPET",
                ActionType.Workflow => "WORKFLOW",
                ActionType.Folder => "FOLDER",
                _ => "SYSTEM"
            };

            string detail = !string.IsNullOrWhiteSpace(item.Description)
                ? item.Description
                : (!string.IsNullOrWhiteSpace(item.Payload.Command) ? item.Payload.Command : item.Payload.SnippetTemplate);

            _allItems.Add(new CheatSheetItemViewModel
            {
                Item = item,
                HotkeyText = item.Hotkey.DisplayText,
                Name = item.Name,
                Detail = detail,
                ActionTypeText = typeText,
                ScopeText = scopeText,
                ScopeBgBrush = scopeBg,
                ScopeBorderBrush = scopeBorder,
                ScopeForegroundBrush = scopeFg,
                IsContextSpecific = isContextMatch
            });
        }

        // Sort: Active App specific first, then system, then alphabetical
        var sorted = _allItems
            .OrderByDescending(x => x.IsContextSpecific)
            .ThenBy(x => x.Name)
            .ToList();

        _allItems.Clear();
        _allItems.AddRange(sorted);

        FilterItems(string.Empty);
    }

    private void FilterItems(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            ShortcutsList.ItemsSource = _allItems;
            CountSummaryText.Text = $"{_allItems.Count} shortcut{(_allItems.Count == 1 ? "" : "s")} available";
        }
        else
        {
            var filtered = _allItems
                .Where(x => x.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                            x.HotkeyText.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                            x.Detail.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

            ShortcutsList.ItemsSource = filtered;
            CountSummaryText.Text = $"{filtered.Count} match{(filtered.Count == 1 ? "" : "es")}";
        }

        if (ShortcutsList.Items.Count > 0)
        {
            ShortcutsList.SelectedIndex = 0;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterItems(SearchBox.Text);
    }

    private void ShortcutsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ExecuteSelected();
    }

    private void ExecuteSelected()
    {
        if (ShortcutsList.SelectedItem is CheatSheetItemViewModel selected)
        {
            Close();
            _onExecute?.Invoke(selected.Item);
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            ExecuteSelected();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            if (ShortcutsList.SelectedIndex < ShortcutsList.Items.Count - 1)
            {
                ShortcutsList.SelectedIndex++;
                ShortcutsList.ScrollIntoView(ShortcutsList.SelectedItem);
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up)
        {
            if (ShortcutsList.SelectedIndex > 0)
            {
                ShortcutsList.SelectedIndex--;
                ShortcutsList.ScrollIntoView(ShortcutsList.SelectedItem);
            }
            e.Handled = true;
            return;
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!_isLoaded) return;
        // Grace period: ignore deactivation within 400ms of activation to prevent immediate closure from tray menu handoff
        if ((DateTime.UtcNow - _activatedTimestamp).TotalMilliseconds < 400)
        {
            return;
        }
        Close();
    }
}
