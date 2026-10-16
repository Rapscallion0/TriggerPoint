using System;
using System.Diagnostics;
using TriggerPoint.Infrastructure.Win32;
using Xunit;

namespace TriggerPoint.Tests;

public class WindowTargetingTests
{
    [Fact]
    public void IsWindowCloaked_ZeroHandle_ReturnsFalseWithoutThrowing()
    {
        bool cloaked = NativeMethods.IsWindowCloaked(IntPtr.Zero);
        Assert.False(cloaked);
    }

    [Fact]
    public void IsIconic_ZeroHandle_ReturnsFalseWithoutThrowing()
    {
        bool iconic = NativeMethods.IsIconic(IntPtr.Zero);
        Assert.False(iconic);
    }

    [Fact]
    public void GetRealProcessIdForWindow_ZeroHandle_ReturnsZero()
    {
        uint pid = NativeMethods.GetRealProcessIdForWindow(IntPtr.Zero);
        Assert.Equal(0u, pid);
    }

    [Fact]
    public void GetWindowExtendedBounds_ZeroHandle_ReturnsFalse()
    {
        bool success = NativeMethods.GetWindowExtendedBounds(IntPtr.Zero, out var rect);
        Assert.False(success);
        Assert.Equal(0, rect.Left);
        Assert.Equal(0, rect.Top);
    }

    [Fact]
    public void ContextFilter_IsKnownBrowser_RecognizesCommonBrowsers()
    {
        Assert.True(TriggerPoint.Core.Models.ContextFilter.IsKnownBrowser("chrome.exe"));
        Assert.True(TriggerPoint.Core.Models.ContextFilter.IsKnownBrowser("msedge.exe"));
        Assert.True(TriggerPoint.Core.Models.ContextFilter.IsKnownBrowser("firefox.exe"));
        Assert.True(TriggerPoint.Core.Models.ContextFilter.IsKnownBrowser("brave.exe"));
        Assert.True(TriggerPoint.Core.Models.ContextFilter.IsKnownBrowser("opera"));
        Assert.True(TriggerPoint.Core.Models.ContextFilter.IsKnownBrowser("vivaldi"));
        Assert.True(TriggerPoint.Core.Models.ContextFilter.IsKnownBrowser("arc"));

        Assert.False(TriggerPoint.Core.Models.ContextFilter.IsKnownBrowser("notepad.exe"));
        Assert.False(TriggerPoint.Core.Models.ContextFilter.IsKnownBrowser("code.exe"));
        Assert.False(TriggerPoint.Core.Models.ContextFilter.IsKnownBrowser(null));
        Assert.False(TriggerPoint.Core.Models.ContextFilter.IsKnownBrowser(string.Empty));
    }

    [Fact]
    public void WindowTargetHighlightView_CanInstantiateOnStaThread()
    {
        Exception? caught = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                var view = new TriggerPoint.UI.Views.WindowTargetHighlightView();
                Assert.NotNull(view);
                Assert.True(view.Topmost);
                Assert.False(view.IsHitTestVisible);
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(caught);
    }

    [Fact]
    public void WindowTargetTooltipView_CanInstantiateAndSetPromptOnStaThread()
    {
        Exception? caught = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                var view = new TriggerPoint.UI.Views.WindowTargetTooltipView();
                Assert.NotNull(view);
                Assert.True(view.Topmost);
                Assert.False(view.IsHitTestVisible);

                view.SetPrompt("Click to select: Notepad");
                Assert.Equal("Click to select: Notepad", view.TooltipTitleText.Text);

                view.SetPrompt("");
                Assert.Equal("Click application window to select", view.TooltipTitleText.Text);
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(caught);
    }

    [Fact]
    public void WindowTargetingOverlay_CanInstantiateOnStaThread()
    {
        Exception? caught = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                var overlay = new TriggerPoint.UI.Views.WindowTargetingOverlay(null, "Select window test");
                Assert.NotNull(overlay);
                Assert.Equal(IntPtr.Zero, overlay.TargetHwnd);
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(caught);
    }
}

