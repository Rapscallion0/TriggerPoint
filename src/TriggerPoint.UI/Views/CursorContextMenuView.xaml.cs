using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Win32;

namespace TriggerPoint.UI.Views;

public class CursorMenuItemViewModel
{
    public TriggerItem Item { get; }
    public string Name => Item.Name;
    public string Description => Item.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public string? AcceleratorKey => Item.AcceleratorKey;
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

    public CursorMenuItemViewModel(TriggerItem item)
    {
        Item = item;
    }
}

public partial class CursorContextMenuView : Window
{
    private readonly List<TriggerItem> _allItems;
    private readonly IActionExecutor _executor;
    private readonly IntPtr _targetHwnd;
    private readonly Stack<TriggerItem?> _navHistory = new();
    private TriggerItem? _currentFolder;
    private List<CursorMenuItemViewModel> _displayedItems = new();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativeMethods.POINT lpPoint);

    public CursorContextMenuView(
        IEnumerable<TriggerItem> allItems, 
        IActionExecutor executor, 
        TriggerItem? initialFolder = null,
        IntPtr targetHwnd = default)
    {
        InitializeComponent();
        _executor = executor;
        _allItems = allItems.ToList();
        _currentFolder = initialFolder;
        _targetHwnd = targetHwnd;

        RenderCurrentFolder();
        Loaded += CursorContextMenuView_Loaded;
    }

    private void RenderCurrentFolder()
    {
        List<TriggerItem> children;
        if (_currentFolder != null && _currentFolder.ActionType == ActionType.Folder)
        {
            children = _allItems
                .Where(x => x.ParentId == _currentFolder.Id && x.IsEnabled)
                .OrderBy(x => x.OrderIndex)
                .ToList();
        }
        else if (_currentFolder != null)
        {
            children = _allItems
                .Where(x => x.ParentId == _currentFolder.ParentId && x.IsEnabled)
                .OrderBy(x => x.OrderIndex)
                .ToList();
        }
        else
        {
            children = _allItems
                .Where(x => !x.ParentId.HasValue && x.IsEnabled)
                .OrderBy(x => x.OrderIndex)
                .ToList();
        }

        _displayedItems = children.Select(x => new CursorMenuItemViewModel(x)).ToList();
        ItemsListBox.ItemsSource = _displayedItems;

        if (_displayedItems.Count == 0)
        {
            EmptyFolderNotice.Visibility = Visibility.Visible;
            ItemsListBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            EmptyFolderNotice.Visibility = Visibility.Collapsed;
            ItemsListBox.Visibility = Visibility.Visible;
            ItemsListBox.SelectedIndex = 0;
        }

        HeaderBorder.Visibility = Visibility.Visible;
        BackBtn.Visibility = _navHistory.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        HeaderTitleText.Text = BuildBreadcrumb();
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
            if (_navHistory.Count > 0)
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

        if (key == Key.Left || key == Key.Back)
        {
            if (_navHistory.Count > 0)
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

        // Accelerator keys: 1-9, A-Z
        var keyStr = key.ToString();
        if (key >= Key.D0 && key <= Key.D9)
        {
            keyStr = ((int)key - (int)Key.D0).ToString();
        }
        else if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            keyStr = ((int)key - (int)Key.NumPad0).ToString();
        }

        var accelMatch = _displayedItems.FirstOrDefault(x => 
            !string.IsNullOrWhiteSpace(x.AcceleratorKey) && 
            string.Equals(x.AcceleratorKey, keyStr, StringComparison.OrdinalIgnoreCase));

        if (accelMatch != null)
        {
            if (accelMatch.IsFolder)
            {
                DrillDown(accelMatch.Item);
            }
            else
            {
                ExecuteItem(accelMatch.Item, DetermineOverride());
            }
            e.Handled = true;
            return;
        }

        if (key == Key.Enter)
        {
            var selectedVm = ItemsListBox.SelectedItem as CursorMenuItemViewModel 
                ?? (_displayedItems.Count > 0 ? _displayedItems[0] : null);

            if (selectedVm != null)
            {
                if (selectedVm.IsFolder)
                {
                    DrillDown(selectedVm.Item);
                }
                else
                {
                    ExecuteItem(selectedVm.Item, DetermineOverride());
                }
                e.Handled = true;
            }
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
            if (vm.IsFolder)
            {
                DrillDown(vm.Item);
            }
            else
            {
                ExecuteItem(vm.Item, DetermineOverride());
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
        _ = _executor.ExecuteAsync(item, executionOverride, _targetHwnd);
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        SafeClose();
    }
}
