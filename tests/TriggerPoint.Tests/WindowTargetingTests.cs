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
}

