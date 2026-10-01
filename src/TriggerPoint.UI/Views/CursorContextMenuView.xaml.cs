using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;
using TriggerPoint.Infrastructure.Win32;

namespace TriggerPoint.UI.Views;

public class CursorMenuItemViewModel
{
    public TriggerItem Item { get; }
    public string Name => Item.Name;
    public string Description => Item.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public string? AcceleratorKey { get; }
    public bool IsAutoAssigned { get; }
    public Visibility HasManualAccelerator => (!string.IsNullOrWhiteSpace(AcceleratorKey) && !IsAutoAssigned) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HasAutoAccelerator => (!string.IsNullOrWhiteSpace(AcceleratorKey) && IsAutoAssigned) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HasAccelerator => !string.IsNullOrWhiteSpace(AcceleratorKey) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HasNoAccelerator => !string.IsNullOrWhiteSpace(AcceleratorKey) ? Visibility.Collapsed : Visibility.Visible;
    public bool IsFolder => Item.ActionType == ActionType.Folder;
    public string TypeBadge => Item.ActionType switch
    {
        ActionType.Folder => "Submenu ▶",
        ActionType.Snippet => "Snippet",
        ActionType.Shell => "App",
        _ => ""
    };
    public string IconSymbol => Item.ActionType switch
    {
        ActionType.Folder => "📁",
        ActionType.Snippet => "📝",
        _ => "⚡"
    };

    public IReadOnlyList<HighlightSegment> HighlightedNameSegments { get; }

    public CursorMenuItemViewModel(TriggerItem item, string? effectiveKey = null, bool isAutoAssigned = false, IReadOnlyList<int>? matchedIndices = null)
    {
        Item = item;
        AcceleratorKey = effectiveKey ?? item.AcceleratorKey;
        IsAutoAssigned = isAutoAssigned;
        HighlightedNameSegments = BuildHighlightedSegments(item.Name, matchedIndices);
    }

    private static IReadOnlyList<HighlightSegment> BuildHighlightedSegments(string name, IReadOnlyList<int>? matchedIndices)
    {
        if (string.IsNullOrEmpty(name)) return [];
        if (matchedIndices == null || matchedIndices.Count == 0)
        {
            return [new HighlightSegment(name, false)];
        }

        var segments = new List<HighlightSegment>();
        var matchSet = new HashSet<int>(matchedIndices);
        int start = 0;
        bool inMatch = matchSet.Contains(0);

        for (int i = 1; i < name.Length; i++)
        {
            bool isCurrentMatch = matchSet.Contains(i);
            if (isCurrentMatch != inMatch)
            {
                segments.Add(new HighlightSegment(name.Substring(start, i - start), inMatch));
                start = i;
                inMatch = isCurrentMatch;
            }
        }

        if (start < name.Length)
        {
            segments.Add(new HighlightSegment(name.Substring(start), inMatch));
        }

        return segments;
    }
}

public partial class CursorContextMenuView : Window
{
    private readonly List<TriggerItem> _allItems;
    private readonly IActionExecutor _executor;
    private readonly IntPtr _targetHwnd;
    private readonly IContextFilterService? _contextFilterService;
    private readonly Stack<TriggerItem?> _navHistory = new();
    private TriggerItem? _currentFolder;
    private List<TriggerItem> _currentFolderChildren = [];
    private List<CursorMenuItemViewModel> _displayedItems = [];
    private string _filterQuery = string.Empty;

    public string FilterQuery => _filterQuery;
    public IReadOnlyList<CursorMenuItemViewModel> DisplayedItems => _displayedItems;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativeMethods.POINT lpPoint);

    public CursorContextMenuView(
        IEnumerable<TriggerItem> allItems, 
        IActionExecutor executor, 
        TriggerItem? initialFolder = null,
        IntPtr targetHwnd = default,
        IContextFilterService? contextFilterService = null)
    {
        InitializeComponent();
        _executor = executor;
        _allItems = allItems.ToList();
        _currentFolder = initialFolder;
        _targetHwnd = targetHwnd;
        _contextFilterService = contextFilterService;

        RenderCurrentFolder();
        Loaded += CursorContextMenuView_Loaded;
    }

    private void RenderCurrentFolder()
    {
        List<TriggerItem> rawChildren;
        if (_currentFolder != null && _currentFolder.ActionType == ActionType.Folder)
        {
            rawChildren = _allItems
                .Where(x => x.ParentId == _currentFolder.Id && x.IsEnabled)
                .OrderBy(x => x.OrderIndex)
                .ToList();
        }
        else if (_currentFolder != null)
        {
            rawChildren = _allItems
                .Where(x => x.ParentId == _currentFolder.ParentId && x.IsEnabled)
                .OrderBy(x => x.OrderIndex)
                .ToList();
        }
        else
        {
            rawChildren = _allItems
                .Where(x => !x.ParentId.HasValue && x.IsEnabled)
                .OrderBy(x => x.OrderIndex)
                .ToList();
        }

        _currentFolderChildren = _contextFilterService == null
            ? rawChildren
            : rawChildren.Where(x => _contextFilterService.ShouldExecute(x, _allItems)).ToList();

        _filterQuery = string.Empty;

        HeaderBorder.Visibility = Visibility.Visible;
        BackBtn.Visibility = _navHistory.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        HeaderTitleText.Text = BuildBreadcrumb();

        ApplyFilterAndRender();
    }

    public void SetFilterQueryForTesting(string query)
    {
        _filterQuery = query ?? string.Empty;
        ApplyFilterAndRender();
    }

    private void ClearFilterBtn_Click(object sender, RoutedEventArgs e)
    {
        _filterQuery = string.Empty;
        ApplyFilterAndRender();
    }

    private void ApplyFilterAndRender()
    {
        var autoMode = _currentFolder?.AutoNumberMode ?? FolderAutoNumberMode.Off;

        if (string.IsNullOrWhiteSpace(_filterQuery))
        {
            if (SearchFilterPill != null) SearchFilterPill.Visibility = Visibility.Collapsed;

            var resolvedKeys = MenuQuickKeyResolver.ResolveKeys(_currentFolderChildren, autoMode);
            _displayedItems = resolvedKeys.Select(r => new CursorMenuItemViewModel(r.Item, r.Key, r.IsAutoAssigned, null)).ToList();
            ItemsListBox.ItemsSource = _displayedItems;

            if (_displayedItems.Count == 0)
            {
                EmptyFolderNotice.Text = "(Folder is empty)";
                EmptyFolderNotice.Visibility = Visibility.Visible;
                ItemsListBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                EmptyFolderNotice.Visibility = Visibility.Collapsed;
                ItemsListBox.Visibility = Visibility.Visible;
                ItemsListBox.SelectedIndex = 0;
            }
        }
        else
        {
            if (SearchFilterPill != null)
            {
                SearchFilterPill.Visibility = Visibility.Visible;
                SearchFilterText.Text = _filterQuery;
            }

            var matches = new List<(TriggerItem Item, FuzzyMatchResult Result)>();
            foreach (var item in _currentFolderChildren)
            {
                var match = FuzzyMatcher.Match(item, _filterQuery);
                if (match.IsMatch)
                {
                    matches.Add((item, match));
                }
            }

            var sortedMatches = matches.OrderByDescending(m => m.Result.Score).ToList();
            var matchedItems = sortedMatches.Select(m => m.Item).ToList();

            // When filtered, re-index visible matches so 1-9 direct keys work on filtered results
            var effectiveAutoMode = autoMode == FolderAutoNumberMode.Off ? FolderAutoNumberMode.SmartFill : autoMode;
            var resolvedKeys = MenuQuickKeyResolver.ResolveKeys(matchedItems, effectiveAutoMode);

            _displayedItems = resolvedKeys.Select((r, idx) => 
                new CursorMenuItemViewModel(r.Item, r.Key, r.IsAutoAssigned, sortedMatches[idx].Result.MatchedIndices)).ToList();

            ItemsListBox.ItemsSource = _displayedItems;

            if (_displayedItems.Count == 0)
            {
                EmptyFolderNotice.Text = $"(No matching items for \"{_filterQuery}\")";
                EmptyFolderNotice.Visibility = Visibility.Visible;
                ItemsListBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                EmptyFolderNotice.Visibility = Visibility.Collapsed;
                ItemsListBox.Visibility = Visibility.Visible;
                ItemsListBox.SelectedIndex = 0;
            }
        }

        UpdateFooterHints();
    }

    private string BuildBreadcrumb()
    {
        if (_navHistory.Count == 0)
        {
            return _currentFolder?.Name.ToUpperInvariant() ?? "MENU";
        }

        var pathSegments = _navHistory
            .Reverse()
            .Concat(new[] { _currentFolder })
            .Where(f => f != null)
            .Select(f => f!.Name.ToUpperInvariant());

        return string.Join("  ›  ", pathSegments);
    }

    private void DrillDown(TriggerItem folderItem)
    {
        _navHistory.Push(_currentFolder);
        _currentFolder = folderItem;
        RenderCurrentFolder();
    }

    private void NavigateBack()
    {
        if (_navHistory.Count == 0)
        {
            Close();
            return;
        }

        var previousFolder = _navHistory.Pop();
        var exitedFolder = _currentFolder;
        _currentFolder = previousFolder;
        RenderCurrentFolder();

        if (exitedFolder != null)
        {
            var prevItem = _displayedItems.FirstOrDefault(x => x.Item.Id == exitedFolder.Id);
            if (prevItem != null)
            {
                ItemsListBox.SelectedItem = prevItem;
                ItemsListBox.ScrollIntoView(prevItem);
            }
        }
    }

    private void BackBtn_Click(object sender, RoutedEventArgs e)
    {
        NavigateBack();
    }

    private void ItemsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateFooterHints();
    }

    private InlineUIContainer CreateEnterKeyIcon(double width = 9.5, double height = 9.5)
    {
        var geom = TryFindResource("EnterKeyGeometry") as Geometry;
        var brush = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.Gray;
        var path = new System.Windows.Shapes.Path
        {
            Data = geom,
            Fill = brush,
            Width = width,
            Height = height,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 2, 0)
        };
        return new InlineUIContainer(path) { BaselineAlignment = BaselineAlignment.Center };
    }

    private void UpdateFooterHints()
    {
        if (PrimaryHintsText == null || ModifierHintsText == null) return;

        PrimaryHintsText.Inlines.Clear();
        ModifierHintsText.Inlines.Clear();

        var selectedVm = ItemsListBox.SelectedItem as CursorMenuItemViewModel;
        if (selectedVm == null)
        {
            if (_navHistory.Count > 0)
            {
                PrimaryHintsText.Inlines.Add(new Run("◀ ") { FontWeight = FontWeights.SemiBold, Foreground = TryFindResource("TextPrimaryBrush") as Brush });
                PrimaryHintsText.Inlines.Add(new Run("Back (Esc)"));
            }
            else
            {
                PrimaryHintsText.Inlines.Add(new Run("Esc Close"));
            }
            ModifierHintsText.Visibility = Visibility.Collapsed;
            return;
        }

        var shortcuts = ActionShortcutRelevanceHelper.GetContextualShortcuts(selectedVm.Item, _navHistory.Count > 0);

        // Row 1: Primary action + Navigation
        PrimaryHintsText.Inlines.Add(CreateEnterKeyIcon(10, 10));
        PrimaryHintsText.Inlines.Add(new Run(shortcuts.PrimaryActionVerb + "   •   "));

        if (_navHistory.Count > 0)
        {
            PrimaryHintsText.Inlines.Add(new Run("◀ ") { FontWeight = FontWeights.SemiBold, Foreground = TryFindResource("TextPrimaryBrush") as Brush });
            PrimaryHintsText.Inlines.Add(new Run("Back (Esc)"));
        }
        else
        {
            PrimaryHintsText.Inlines.Add(new Run("Esc Close"));
        }

        // Row 2: Relevant modifier overrides only
        bool hasModifier = false;
        var secondaryBrush = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;

        if (shortcuts.CanRunAsAdmin)
        {
            ModifierHintsText.Inlines.Add(new Run("Ctrl+") { Foreground = secondaryBrush });
            ModifierHintsText.Inlines.Add(CreateEnterKeyIcon(9.5, 9.5));
            ModifierHintsText.Inlines.Add(new Run("Admin"));
            hasModifier = true;
        }

        if (shortcuts.CanRevealInExplorer)
        {
            if (hasModifier)
            {
                ModifierHintsText.Inlines.Add(new Run("   •   "));
            }
            ModifierHintsText.Inlines.Add(new Run("Shift+") { Foreground = secondaryBrush });
            ModifierHintsText.Inlines.Add(CreateEnterKeyIcon(9.5, 9.5));
            ModifierHintsText.Inlines.Add(new Run("Reveal"));
            hasModifier = true;
        }

        if (shortcuts.CanOpenSettings)
        {
            if (hasModifier)
            {
                ModifierHintsText.Inlines.Add(new Run("   •   "));
            }
            ModifierHintsText.Inlines.Add(new Run("Alt+") { Foreground = secondaryBrush });
            ModifierHintsText.Inlines.Add(CreateEnterKeyIcon(9.5, 9.5));
            ModifierHintsText.Inlines.Add(new Run("Settings"));
            hasModifier = true;
        }

        ModifierHintsText.Visibility = hasModifier ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CursorContextMenuView_Loaded(object sender, RoutedEventArgs e)
    {
        PositionAtCursor();
        Activate();
        Focus();

        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            NativeMethods.SetForegroundWindow(handle);
        }
        catch { }

        if (ItemsListBox.Items.Count > 0)
        {
            ItemsListBox.SelectedIndex = 0;
            ItemsListBox.Focus();
            Keyboard.Focus(ItemsListBox);
        }
    }

    private void PositionAtCursor()
    {
        if (!GetCursorPos(out var pt)) return;

        // Find current monitor and work area bounds
        var hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        NativeMethods.GetMonitorInfo(hMonitor, ref monitorInfo);

        // Clamping logic: ensure window fits inside monitor work area
        double dpiScale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double targetX = pt.X / dpiScale;
        double targetY = pt.Y / dpiScale;

        double workLeft = monitorInfo.rcWork.Left / dpiScale;
        double workTop = monitorInfo.rcWork.Top / dpiScale;
        double workRight = monitorInfo.rcWork.Right / dpiScale;
        double workBottom = monitorInfo.rcWork.Bottom / dpiScale;

        double winWidth = ActualWidth > 0 ? ActualWidth : 280;
        double winHeight = ActualHeight > 0 ? ActualHeight : 250;

        // Clamp horizontally
        if (targetX + winWidth > workRight)
        {
            targetX = workRight - winWidth - 8;
        }
        if (targetX < workLeft)
        {
            targetX = workLeft + 8;
        }

        // Clamp vertically
        if (targetY + winHeight > workBottom)
        {
            targetY = workBottom - winHeight - 8;
        }
        if (targetY < workTop)
        {
            targetY = workTop + 8;
        }

        Left = targetX;
        Top = targetY;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            if (!string.IsNullOrEmpty(_filterQuery))
            {
                _filterQuery = string.Empty;
                ApplyFilterAndRender();
            }
            else if (_navHistory.Count > 0)
            {
                NavigateBack();
            }
            else
            {
                SafeClose();
            }
            e.Handled = true;
            return;
        }

        if (key == Key.Back)
        {
            if (!string.IsNullOrEmpty(_filterQuery))
            {
                _filterQuery = _filterQuery.Length > 1 
                    ? _filterQuery.Substring(0, _filterQuery.Length - 1) 
                    : string.Empty;
                ApplyFilterAndRender();
                e.Handled = true;
                return;
            }
            else if (_navHistory.Count > 0)
            {
                NavigateBack();
                e.Handled = true;
                return;
            }
        }

        if (key == Key.Left)
        {
            if (string.IsNullOrEmpty(_filterQuery) && _navHistory.Count > 0)
            {
                NavigateBack();
                e.Handled = true;
                return;
            }
        }

        if (key == Key.Right)
        {
            if (ItemsListBox.SelectedItem is CursorMenuItemViewModel { IsFolder: true } selectedFolder)
            {
                DrillDown(selectedFolder.Item);
                e.Handled = true;
                return;
            }
        }

        if (key == Key.Down)
        {
            if (ItemsListBox.Items.Count > 0)
            {
                if (ItemsListBox.SelectedIndex < ItemsListBox.Items.Count - 1)
                {
                    ItemsListBox.SelectedIndex++;
                }
                else
                {
                    ItemsListBox.SelectedIndex = 0; // Wrap around to top
                }
                ItemsListBox.ScrollIntoView(ItemsListBox.SelectedItem);
            }
            e.Handled = true;
            return;
        }

        if (key == Key.Up)
        {
            if (ItemsListBox.Items.Count > 0)
            {
                if (ItemsListBox.SelectedIndex > 0)
                {
                    ItemsListBox.SelectedIndex--;
                }
                else
                {
                    ItemsListBox.SelectedIndex = ItemsListBox.Items.Count - 1; // Wrap around to bottom
                }
                ItemsListBox.ScrollIntoView(ItemsListBox.SelectedItem);
            }
            e.Handled = true;
            return;
        }

        if (key == Key.Home)
        {
            if (ItemsListBox.Items.Count > 0)
            {
                ItemsListBox.SelectedIndex = 0;
                ItemsListBox.ScrollIntoView(ItemsListBox.SelectedItem);
            }
            e.Handled = true;
            return;
        }

        if (key == Key.End)
        {
            if (ItemsListBox.Items.Count > 0)
            {
                ItemsListBox.SelectedIndex = ItemsListBox.Items.Count - 1;
                ItemsListBox.ScrollIntoView(ItemsListBox.SelectedItem);
            }
            e.Handled = true;
            return;
        }

        if (key == Key.Space)
        {
            if (!string.IsNullOrEmpty(_filterQuery))
            {
                _filterQuery += " ";
                ApplyFilterAndRender();
                e.Handled = true;
                return;
            }
        }

        // Accelerator & Filter keys: 1-9, A-Z (excluding 0)
        string? keyStr = null;
        bool isDigitKey = false;
        if (key >= Key.D1 && key <= Key.D9)
        {
            keyStr = ((int)key - (int)Key.D0).ToString();
            isDigitKey = true;
        }
        else if (key >= Key.NumPad1 && key <= Key.NumPad9)
        {
            keyStr = ((int)key - (int)Key.NumPad0).ToString();
            isDigitKey = true;
        }
        else if (key >= Key.A && key <= Key.Z)
        {
            keyStr = key.ToString();
        }

        if (!string.IsNullOrEmpty(keyStr))
        {
            // Case 1: Numeric quick keys (1-9) always execute the matching item if found
            if (isDigitKey)
            {
                var accelMatch = _displayedItems.FirstOrDefault(x => 
                    !string.IsNullOrWhiteSpace(x.AcceleratorKey) && 
                    string.Equals(x.AcceleratorKey, keyStr, StringComparison.OrdinalIgnoreCase));

                if (accelMatch != null)
                {
                    ExecuteMatchedAccelerator(accelMatch);
                    e.Handled = true;
                    return;
                }
                else if (!string.IsNullOrEmpty(_filterQuery))
                {
                    // If no item matched '1-9' and user is filtering, append the digit to the search
                    _filterQuery += keyStr;
                    ApplyFilterAndRender();
                    e.Handled = true;
                    return;
                }
            }
            // Case 2: Letter keys (A-Z)
            else
            {
                // If filter is already active, any letter appends to filter
                if (!string.IsNullOrEmpty(_filterQuery))
                {
                    _filterQuery += keyStr.ToLowerInvariant();
                    ApplyFilterAndRender();
                    e.Handled = true;
                    return;
                }
                else
                {
                    // Filter is empty: check if there's an explicit manual accelerator
                    var manualMatch = _displayedItems.FirstOrDefault(x => 
                        !x.IsAutoAssigned && 
                        !string.IsNullOrWhiteSpace(x.AcceleratorKey) && 
                        string.Equals(x.AcceleratorKey, keyStr, StringComparison.OrdinalIgnoreCase));

                    if (manualMatch != null)
                    {
                        ExecuteMatchedAccelerator(manualMatch);
                        e.Handled = true;
                        return;
                    }

                    // Otherwise, start filtering with this letter!
                    _filterQuery = keyStr.ToLowerInvariant();
                    ApplyFilterAndRender();
                    e.Handled = true;
                    return;
                }
            }
        }

        if (key == Key.Enter)
        {
            var selectedVm = ItemsListBox.SelectedItem as CursorMenuItemViewModel 
                ?? (_displayedItems.Count > 0 ? _displayedItems[0] : null);

            if (selectedVm != null)
            {
                var execOverride = DetermineOverride();
                if (selectedVm.IsFolder)
                {
                    if (execOverride == ExecutionOverride.OpenSettings)
                    {
                        ExecuteItem(selectedVm.Item, execOverride);
                    }
                    else
                    {
                        DrillDown(selectedVm.Item);
                    }
                }
                else
                {
                    ExecuteItem(selectedVm.Item, execOverride);
                }
                e.Handled = true;
            }
        }
    }

    private void ExecuteMatchedAccelerator(CursorMenuItemViewModel accelMatch)
    {
        var execOverride = DetermineOverride();
        if (accelMatch.IsFolder)
        {
            if (execOverride == ExecutionOverride.OpenSettings)
            {
                ExecuteItem(accelMatch.Item, execOverride);
            }
            else
            {
                DrillDown(accelMatch.Item);
            }
        }
        else
        {
            ExecuteItem(accelMatch.Item, execOverride);
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

    private void ItemBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CursorMenuItemViewModel vm })
        {
            var execOverride = DetermineOverride();
            if (vm.IsFolder)
            {
                if (execOverride == ExecutionOverride.OpenSettings)
                {
                    ExecuteItem(vm.Item, execOverride);
                }
                else
                {
                    DrillDown(vm.Item);
                }
            }
            else
            {
                ExecuteItem(vm.Item, execOverride);
            }
        }
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

    private void ExecuteItem(TriggerItem item, ExecutionOverride executionOverride)
    {
        SafeClose();
        if (executionOverride == ExecutionOverride.OpenSettings)
        {
            (Application.Current as App)?.ShowSettingsWindow(item);
            return;
        }
        _ = _executor.ExecuteAsync(item, executionOverride, _targetHwnd);
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        SafeClose();
    }
}
