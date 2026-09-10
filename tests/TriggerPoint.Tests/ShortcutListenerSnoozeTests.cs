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
}
