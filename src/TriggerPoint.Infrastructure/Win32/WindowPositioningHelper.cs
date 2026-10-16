using System;

namespace TriggerPoint.Infrastructure.Win32;

/// <summary>
/// Provides pure, deterministic geometric calculations for multi-monitor, mixed-DPI window positioning,
/// work-area boundary clamping, and DPI-normalized overlay bounds.
/// </summary>
public static class WindowPositioningHelper
{
    public readonly record struct OverlayBounds(double Left, double Top, double Width, double Height);
    public readonly record struct TooltipPlacement(double Left, double Top);

    /// <summary>
    /// Calculates physical pixel coordinates for placing a cursor context menu right at the cursor tip,
    /// compensating for drop-shadow insets and strictly clamping within the target monitor's working area.
    /// </summary>
    public static NativeMethods.POINT CalculateContextMenuPlacement(
        NativeMethods.POINT cursorPos,
        NativeMethods.RECT workArea,
        uint monitorDpi,
        double contentWidthDip,
        double contentHeightDip,
        double shadowMarginDip = 8.0)
    {
        double scale = monitorDpi > 0 ? monitorDpi / 96.0 : 1.0;
        if (scale <= 0) scale = 1.0;

        int marginPx = (int)Math.Round(shadowMarginDip * scale);
        int winWidthPx = (int)Math.Round(contentWidthDip * scale);
        int winHeightPx = (int)Math.Round(contentHeightDip * scale);

        // Align the visible border (inset by shadow margin) directly with the cursor tip
        int targetX = cursorPos.X - marginPx;
        int targetY = cursorPos.Y - marginPx;

        // Clamp against the monitor working area (which natively excludes taskbars and handles any orientation)
        if (targetX + winWidthPx > workArea.Right)
        {
            targetX = workArea.Right - winWidthPx;
        }
        if (targetX < workArea.Left)
        {
            targetX = workArea.Left;
        }

        if (targetY + winHeightPx > workArea.Bottom)
        {
            targetY = workArea.Bottom - winHeightPx;
        }
        if (targetY < workArea.Top)
        {
            targetY = workArea.Top;
        }

        return new NativeMethods.POINT(targetX, targetY);
    }

    /// <summary>
    /// Normalizes physical window device coordinates into DPI-scaled canvas coordinates on a multi-monitor overlay,
    /// preventing cross-monitor bitmap stretching and aspect-ratio distortion.
    /// </summary>
    public static OverlayBounds CalculateHighlightDIPBounds(
        NativeMethods.RECT targetWindowPhysBounds,
        NativeMethods.RECT monitorPhysBounds,
        double monitorTopLeftDipX,
        double monitorTopLeftDipY,
        uint monitorDpi)
    {
        double scale = monitorDpi > 0 ? monitorDpi / 96.0 : 1.0;
        if (scale <= 0) scale = 1.0;

        double dxPhys = targetWindowPhysBounds.Left - monitorPhysBounds.Left;
        double dyPhys = targetWindowPhysBounds.Top - monitorPhysBounds.Top;
        double wPhys = targetWindowPhysBounds.Width;
        double hPhys = targetWindowPhysBounds.Height;

        double leftDip = monitorTopLeftDipX + (dxPhys / scale);
        double topDip = monitorTopLeftDipY + (dyPhys / scale);
        double widthDip = wPhys / scale;
        double heightDip = hPhys / scale;

        return new OverlayBounds(leftDip, topDip, widthDip, heightDip);
    }

    /// <summary>
    /// Calculates the position for the targeting overlay tooltip, ensuring it stays on the active monitor
    /// near the cursor without jumping across monitor borders or overlapping screen edges.
    /// </summary>
    public static TooltipPlacement CalculateTooltipPosition(
        double mouseCanvasX,
        double mouseCanvasY,
        double tooltipWidthDip,
        double tooltipHeightDip,
        double monCanvasLeft,
        double monCanvasTop,
        double monCanvasWidth,
        double monCanvasHeight,
        double margin = 16.0)
    {
        double tipLeft = mouseCanvasX + 20;
        double tipTop = mouseCanvasY + 20;

        double monRight = monCanvasLeft + monCanvasWidth;
        double monBottom = monCanvasTop + monCanvasHeight;

        if (tipLeft + tooltipWidthDip > monRight - margin)
        {
            tipLeft = mouseCanvasX - tooltipWidthDip - 20;
        }
        if (tipTop + tooltipHeightDip > monBottom - margin)
        {
            tipTop = mouseCanvasY - tooltipHeightDip - 20;
        }

        // Clamp safely within active monitor canvas boundaries
        double maxLeft = Math.Max(monCanvasLeft + margin, monRight - tooltipWidthDip - margin);
        double maxTop = Math.Max(monCanvasTop + margin, monBottom - tooltipHeightDip - margin);

        tipLeft = Math.Max(monCanvasLeft + margin, Math.Min(tipLeft, maxLeft));
        tipTop = Math.Max(monCanvasTop + margin, Math.Min(tipTop, maxTop));

        return new TooltipPlacement(tipLeft, tipTop);
    }
}
