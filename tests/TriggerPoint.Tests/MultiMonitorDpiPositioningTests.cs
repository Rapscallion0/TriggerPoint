using TriggerPoint.Infrastructure.Win32;
using Xunit;

namespace TriggerPoint.Tests;

public class MultiMonitorDpiPositioningTests
{
    [Fact]
    public void ContextMenu_StandardDpi_PrimaryMonitor_PositionsAtCursorMinusShadowMargin()
    {
        // 100% DPI (96 DPI): 8 DIP shadow margin = 8 physical pixels
        var cursor = new NativeMethods.POINT(500, 400);
        var workArea = new NativeMethods.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1040 };

        var placement = WindowPositioningHelper.CalculateContextMenuPlacement(
            cursor, workArea, monitorDpi: 96, contentWidthDip: 300, contentHeightDip: 200, shadowMarginDip: 8.0);

        Assert.Equal(500 - 8, placement.X);
        Assert.Equal(400 - 8, placement.Y);
    }

    [Fact]
    public void ContextMenu_150Dpi_SecondaryMonitor_ScalesSizeAndMarginAccurately()
    {
        // 150% DPI (144 DPI): scale = 1.5. 8 DIP shadow margin = 12 physical pixels
        var cursor = new NativeMethods.POINT(2500, 600);
        var workArea = new NativeMethods.RECT { Left = 1920, Top = 0, Right = 4480, Bottom = 1400 };

        var placement = WindowPositioningHelper.CalculateContextMenuPlacement(
            cursor, workArea, monitorDpi: 144, contentWidthDip: 300, contentHeightDip: 200, shadowMarginDip: 8.0);

        Assert.Equal(2500 - 12, placement.X);
        Assert.Equal(600 - 12, placement.Y);
    }

    [Fact]
    public void ContextMenu_200Dpi_RightScreenEdge_ClampsInsideWorkArea()
    {
        // 200% DPI (192 DPI): scale = 2.0. WinWidth = 300 * 2.0 = 600 px.
        // Screen right = 3840. Cursor near right edge at 3800.
        var cursor = new NativeMethods.POINT(3800, 1000);
        var workArea = new NativeMethods.RECT { Left = 0, Top = 0, Right = 3840, Bottom = 2100 };

        var placement = WindowPositioningHelper.CalculateContextMenuPlacement(
            cursor, workArea, monitorDpi: 192, contentWidthDip: 300, contentHeightDip: 400, shadowMarginDip: 8.0);

        // Clamped: Right - WinWidth = 3840 - 600 = 3240
        Assert.Equal(3240, placement.X);
        Assert.Equal(1000 - 16, placement.Y);
    }

    [Fact]
    public void ContextMenu_NegativeScreenCoordinates_LeftMonitor_PositionsAndClampsCorrectly()
    {
        // Secondary monitor to the left: WorkArea [-1920, 0] to [0, 1040], 96 DPI.
        var workArea = new NativeMethods.RECT { Left = -1920, Top = 0, Right = 0, Bottom = 1040 };

        // 1. Unclamped placement in negative coordinate space
        var cursorMid = new NativeMethods.POINT(-1000, 500);
        var placementMid = WindowPositioningHelper.CalculateContextMenuPlacement(
            cursorMid, workArea, monitorDpi: 96, contentWidthDip: 300, contentHeightDip: 200, shadowMarginDip: 8.0);

        Assert.Equal(-1000 - 8, placementMid.X);
        Assert.Equal(500 - 8, placementMid.Y);

        // 2. Dual-edge clamping near right boundary (0) and bottom taskbar (1040)
        var cursorCorner = new NativeMethods.POINT(-100, 1000);
        var placementCorner = WindowPositioningHelper.CalculateContextMenuPlacement(
            cursorCorner, workArea, monitorDpi: 96, contentWidthDip: 300, contentHeightDip: 200, shadowMarginDip: 8.0);

        // Clamped horizontally: Right (0) - WinWidth (300) = -300
        Assert.Equal(-300, placementCorner.X);
        // Clamped vertically: Bottom (1040) - WinHeight (200) = 840
        Assert.Equal(840, placementCorner.Y);
    }

    [Fact]
    public void ContextMenu_TopLeftCorner_ClampsToLeftAndTopEdges()
    {
        var cursor = new NativeMethods.POINT(2, 2);
        var workArea = new NativeMethods.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };

        var placement = WindowPositioningHelper.CalculateContextMenuPlacement(
            cursor, workArea, monitorDpi: 96, contentWidthDip: 280, contentHeightDip: 250, shadowMarginDip: 8.0);

        Assert.Equal(0, placement.X);
        Assert.Equal(0, placement.Y);
    }

    [Fact]
    public void HighlightBounds_Standard100Dpi_CalculatesDIPBoundsExactly()
    {
        var targetWindowPhys = new NativeMethods.RECT { Left = 100, Top = 100, Right = 900, Bottom = 700 };
        var monitorPhys = new NativeMethods.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };

        var bounds = WindowPositioningHelper.CalculateHighlightDIPBounds(
            targetWindowPhys, monitorPhys, monitorTopLeftDipX: 0, monitorTopLeftDipY: 0, monitorDpi: 96);

        Assert.Equal(100, bounds.Left);
        Assert.Equal(100, bounds.Top);
        Assert.Equal(800, bounds.Width);
        Assert.Equal(600, bounds.Height);
    }

    [Fact]
    public void HighlightBounds_150Dpi_NormalizesPhysicalBoundsToAvoidOvershooting()
    {
        // Monitor is at physical (1920, 0), DPI 144 (1.5x)
        var monitorPhys = new NativeMethods.RECT { Left = 1920, Top = 0, Right = 4480, Bottom = 1440 };
        // Target window on this monitor is at physical (2220, 150), size 1500 x 900
        var targetWindowPhys = new NativeMethods.RECT { Left = 2220, Top = 150, Right = 3720, Bottom = 1050 };

        var bounds = WindowPositioningHelper.CalculateHighlightDIPBounds(
            targetWindowPhys, monitorPhys, monitorTopLeftDipX: 1920, monitorTopLeftDipY: 0, monitorDpi: 144);

        // dxPhys = 2220 - 1920 = 300. dxDip = 300 / 1.5 = 200. Left = 1920 + 200 = 2120
        Assert.Equal(2120, bounds.Left, precision: 2);
        // dyPhys = 150. dyDip = 150 / 1.5 = 100
        Assert.Equal(100, bounds.Top, precision: 2);
        // widthDip = 1500 / 1.5 = 1000 (when rendered at 1.5x by DWM, equals exact 1500 physical px!)
        Assert.Equal(1000, bounds.Width, precision: 2);
        // heightDip = 900 / 1.5 = 600 (when rendered at 1.5x by DWM, equals exact 900 physical px!)
        Assert.Equal(600, bounds.Height, precision: 2);
    }

    [Fact]
    public void HighlightBounds_NegativeCoordinates_LeftMonitor125Dpi()
    {
        // Monitor on the left: physical [-1920, 0] to [0, 1080], DPI 120 (1.25x)
        var monitorPhys = new NativeMethods.RECT { Left = -1920, Top = 0, Right = 0, Bottom = 1080 };
        // Target window at physical [-1670, 250], size 1250 x 625
        var targetWindowPhys = new NativeMethods.RECT { Left = -1670, Top = 250, Right = -420, Bottom = 875 };

        var bounds = WindowPositioningHelper.CalculateHighlightDIPBounds(
            targetWindowPhys, monitorPhys, monitorTopLeftDipX: -1920, monitorTopLeftDipY: 0, monitorDpi: 120);

        // dxPhys = -1670 - (-1920) = 250. dxDip = 250 / 1.25 = 200. Left = -1920 + 200 = -1720
        Assert.Equal(-1720, bounds.Left, precision: 2);
        Assert.Equal(200, bounds.Top, precision: 2);
        Assert.Equal(1000, bounds.Width, precision: 2);
        Assert.Equal(500, bounds.Height, precision: 2);
    }

    [Fact]
    public void TooltipPlacement_FlipsAndClampsWithinActiveMonitorCanvasWorkArea()
    {
        // Active monitor bounds on canvas: Left = 1920, Top = 0, Width = 1700, Height = 960
        // Mouse near bottom-right corner at (3550, 920)
        var placement = WindowPositioningHelper.CalculateTooltipPosition(
            mouseCanvasX: 3550,
            mouseCanvasY: 920,
            tooltipWidthDip: 260,
            tooltipHeightDip: 34,
            monCanvasLeft: 1920,
            monCanvasTop: 0,
            monCanvasWidth: 1700,
            monCanvasHeight: 960,
            margin: 16.0);

        // Should flip to left of mouse: 3550 - 260 - 20 = 3270
        Assert.Equal(3270, placement.Left);
        // Should flip above mouse: 920 - 34 - 20 = 866
        Assert.Equal(866, placement.Top);
    }
}
