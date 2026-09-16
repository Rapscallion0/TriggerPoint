using System;
using TriggerPoint.Infrastructure.Win32;
using Xunit;

namespace TriggerPoint.Tests;

public class ShortcutListenerSnoozeTests
{
    [Fact]
    public void Win32HotkeyListener_DefaultSnoozeState_IsFalse()
    {
        using var listener = new Win32HotkeyListener();
        Assert.False(listener.IsSnoozed);
    }

    [Fact]
    public void Win32HotkeyListener_SettingIsSnoozed_RaisesSnoozeChangedEvent()
    {
        using var listener = new Win32HotkeyListener();
        bool? eventReceivedValue = null;
        int eventCount = 0;

        listener.SnoozeChanged += (sender, isSnoozed) =>
        {
            eventReceivedValue = isSnoozed;
            eventCount++;
        };

        listener.IsSnoozed = true;
        Assert.True(listener.IsSnoozed);
        Assert.True(eventReceivedValue);
        Assert.Equal(1, eventCount);

        // Setting to same value should not fire event again
        listener.IsSnoozed = true;
        Assert.Equal(1, eventCount);

        // Setting back to false
        listener.IsSnoozed = false;
        Assert.False(listener.IsSnoozed);
        Assert.False(eventReceivedValue);
        Assert.Equal(2, eventCount);
    }

    [Fact]
    public void Win32HotkeyListener_SuspendAndResume_TracksSuspensionCorrectly()
    {
        using var listener = new Win32HotkeyListener();

        // Single suspend and resume
        listener.Suspend();
        // Should not throw or crash when resuming
        listener.Resume();

        // Nested re-entrant suspend and resume
        listener.Suspend();
        listener.Suspend();
        listener.Resume();
        listener.Resume();
    }

    [Fact]
    public void Win32HotkeyListener_RegisterAllWhileSuspended_CachesAndRegistersOnResume()
    {
        using var listener = new Win32HotkeyListener();
        var item = new TriggerPoint.Core.Models.TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Test Snippet",
            IsEnabled = true,
            Hotkey = new TriggerPoint.Core.Models.ShortcutBinding(
                TriggerPoint.Core.Models.ModifierKeys.Control | TriggerPoint.Core.Models.ModifierKeys.Shift,
                0x54, // 'T' virtual key
                "Ctrl + Shift + T")
        };

        // Suspend listener
        listener.Suspend();

        // Register items while suspended (should cache without registering with OS)
        listener.RegisterAll([item]);

        // Resume listener (should trigger registration of cached items)
        listener.Resume();
    }
}
