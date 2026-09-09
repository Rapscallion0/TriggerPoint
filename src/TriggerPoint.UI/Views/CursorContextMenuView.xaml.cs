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
    public string TypeBadge => Item.ActionType switch
    {
        ActionType.Snippet => "Snippet",
        ActionType.Shell => "App",
        ActionType.Folder => "Menu",
        _ => ""
    };

    public CursorMenuItemViewModel(TriggerItem item)
    {
        Item = item;
    }
}

public partial class CursorContextMenuView : Window
{
    private readonly List<CursorMenuItemViewModel> _items;
    private readonly IActionExecutor _executor;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativeMethods.POINT lpPoint);

    public CursorContextMenuView(
        IEnumerable<TriggerItem> items, 
        IActionExecutor executor, 
        string? folderTitle = null)
    {
        InitializeComponent();
        _executor = executor;
        _items = items.Select(x => new CursorMenuItemViewModel(x)).ToList();
        ItemsList.ItemsSource = _items;

        if (!string.IsNullOrWhiteSpace(folderTitle))
        {
            HeaderBorder.Visibility = Visibility.Visible;
            HeaderTitleText.Text = folderTitle.ToUpperInvariant();
        }

        Loaded += CursorContextMenuView_Loaded;
    }

    private void CursorContextMenuView_Loaded(object sender, RoutedEventArgs e)
    {
        PositionAtCursor();
        Activate();
        Focus();
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

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        // Accelerator keys: 1-9, A-Z
        var keyStr = e.Key.ToString();
        if (e.Key >= Key.D0 && e.Key <= Key.D9)
        {
            keyStr = ((int)e.Key - (int)Key.D0).ToString();
        }
        else if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
        {
            keyStr = ((int)e.Key - (int)Key.NumPad0).ToString();
        }

        var accelMatch = _items.FirstOrDefault(x => 
            !string.IsNullOrWhiteSpace(x.AcceleratorKey) && 
            string.Equals(x.AcceleratorKey, keyStr, StringComparison.OrdinalIgnoreCase));

        if (accelMatch != null)
        {
            ExecuteItem(accelMatch.Item, DetermineOverride());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && _items.Count > 0)
        {
            ExecuteItem(_items[0].Item, DetermineOverride());
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

    private void ItemBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CursorMenuItemViewModel vm })
        {
            ExecuteItem(vm.Item, DetermineOverride());
        }
    }

    private void ExecuteItem(TriggerItem item, ExecutionOverride executionOverride)
    {
        Close();
        _ = _executor.ExecuteAsync(item, executionOverride);
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        Close();
    }
}
