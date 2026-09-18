using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Win32;

namespace TriggerPoint.UI.Views;

public record ChordOptionViewModel(string KeyDisplay, string ActionName);

public partial class ChordHudView : Window
{
    public ChordHudView(ShortcutBinding leader, IReadOnlyList<TriggerItem> candidates)
    {
        InitializeComponent();

        LeaderText.Text = leader.PrimaryDisplayText;

        var chordItems = candidates
            .Where(c => c.Hotkey != null && c.Hotkey.IsChord)
            .Take(6)
            .Select(c => new ChordOptionViewModel(c.Hotkey!.ChordDisplayText, c.Name))
            .ToList();

        if (chordItems.Count > 0)
        {
            ChordOptionsItemsControl.ItemsSource = chordItems;
            ChordOptionsItemsControl.Visibility = Visibility.Visible;
        }
        else
        {
            ChordOptionsItemsControl.Visibility = Visibility.Collapsed;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        PositionBottomCenter();
    }

    private void PositionBottomCenter()
    {
        double dpiScale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

        // Position on active monitor at mouse pointer
        if (NativeMethods.GetCursorPos(out var pt))
        {
            var hMon = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (NativeMethods.GetMonitorInfo(hMon, ref mi))
            {
                double workLeft = mi.rcWork.Left / dpiScale;
                double workWidth = (mi.rcWork.Right - mi.rcWork.Left) / dpiScale;
                double workBottom = mi.rcWork.Bottom / dpiScale;

                UpdateLayout();
                double w = ActualWidth > 0 ? ActualWidth : 420;
                double h = ActualHeight > 0 ? ActualHeight : 60;

                Left = workLeft + Math.Max(0, (workWidth - w) / 2.0);
                Top = workBottom - h - 45;
                return;
            }
        }

        // Fallback to Primary screen work area
        var workArea = SystemParameters.WorkArea;
        UpdateLayout();
        double winW = ActualWidth > 0 ? ActualWidth : 420;
        double winH = ActualHeight > 0 ? ActualHeight : 60;

        Left = workArea.Left + Math.Max(0, (workArea.Width - winW) / 2.0);
        Top = workArea.Bottom - winH - 45;
    }
}
