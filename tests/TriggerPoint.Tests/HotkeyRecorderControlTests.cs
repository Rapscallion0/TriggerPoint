using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using TriggerPoint.Core.Models;
using TriggerPoint.UI.Controls;
using Xunit;

namespace TriggerPoint.Tests;

public class HotkeyRecorderControlTests
{
    private static void RunInSta(Action action)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current == null)
                {
                    new Application();
                }

                if (!Application.Current!.Resources.MergedDictionaries.Any(d => d.Source?.OriginalString?.Contains("ThemeResources.xaml") == true))
                {
                    Application.Current!.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/TriggerPoint;component/Theme/ThemeResources.xaml", UriKind.Absolute)
                    });
                }
                Application.Current.Resources["BorderBrush"] ??= new SolidColorBrush(Colors.Gray);
                Application.Current.Resources["AccentBrush"] ??= new SolidColorBrush(Colors.DodgerBlue);

                action();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);

        if (caught != null)
        {
            throw new InvalidOperationException($"STA test failed: {caught.Message}", caught);
        }
    }

    [Fact]
    public void HotkeyRecorderControl_StartAndStopRecording_FiresBalancedEvents()
    {
        RunInSta(() =>
        {
            int startedCount = 0;
            int stoppedCount = 0;

            EventHandler onStarted = (s, e) => startedCount++;
            EventHandler onStopped = (s, e) => stoppedCount++;

            HotkeyRecorderControl.RecordingStarted += onStarted;
            HotkeyRecorderControl.RecordingStopped += onStopped;

            try
            {
                var recorder = new HotkeyRecorderControl();

                recorder.StartRecording();
                Assert.True(recorder.IsRecording);
                Assert.Equal(1, startedCount);
                Assert.Equal(0, stoppedCount);

                recorder.StopRecording(cancelled: false);
                Assert.False(recorder.IsRecording);
                Assert.Equal(1, startedCount);
                Assert.Equal(1, stoppedCount);

                // Subsequent stop should be a no-op and not fire stopped event again
                recorder.StopRecording(cancelled: true);
                Assert.Equal(1, stoppedCount);
            }
            finally
            {
                HotkeyRecorderControl.RecordingStarted -= onStarted;
                HotkeyRecorderControl.RecordingStopped -= onStopped;
            }
        });
    }

    [Fact]
    public void HotkeyRecorderControl_MultipleRecorders_RaisesEventsOnlyOnTransitions()
    {
        RunInSta(() =>
        {
            int startedCount = 0;
            int stoppedCount = 0;

            EventHandler onStarted = (s, e) => startedCount++;
            EventHandler onStopped = (s, e) => stoppedCount++;

            HotkeyRecorderControl.RecordingStarted += onStarted;
            HotkeyRecorderControl.RecordingStopped += onStopped;

            try
            {
                var recorder1 = new HotkeyRecorderControl();
                var recorder2 = new HotkeyRecorderControl();

                // Recorder 1 starts -> 0 to 1 active recorders (fire started)
                recorder1.StartRecording();
                Assert.Equal(1, startedCount);
                Assert.Equal(0, stoppedCount);

                // Recorder 2 starts -> 1 to 2 active recorders (no extra started event)
                recorder2.StartRecording();
                Assert.Equal(1, startedCount);
                Assert.Equal(0, stoppedCount);

                // Recorder 1 stops -> 2 to 1 active recorders (no stopped event yet)
                recorder1.StopRecording(cancelled: false);
                Assert.Equal(1, startedCount);
                Assert.Equal(0, stoppedCount);

                // Recorder 2 stops -> 1 to 0 active recorders (fire stopped)
                recorder2.StopRecording(cancelled: false);
                Assert.Equal(1, startedCount);
                Assert.Equal(1, stoppedCount);
            }
            finally
            {
                HotkeyRecorderControl.RecordingStarted -= onStarted;
                HotkeyRecorderControl.RecordingStopped -= onStopped;
            }
        });
    }

    [Fact]
    public void HotkeyRecorderControl_UnloadedWhileRecording_StopsRecordingCleanly()
    {
        RunInSta(() =>
        {
            int startedCount = 0;
            int stoppedCount = 0;

            EventHandler onStarted = (s, e) => startedCount++;
            EventHandler onStopped = (s, e) => stoppedCount++;

            HotkeyRecorderControl.RecordingStarted += onStarted;
            HotkeyRecorderControl.RecordingStopped += onStopped;

            try
            {
                var recorder = new HotkeyRecorderControl();
                recorder.StartRecording();
                Assert.True(recorder.IsRecording);
                Assert.Equal(1, startedCount);
                Assert.Equal(0, stoppedCount);

                // Simulate control unloading (e.g. window closing)
                recorder.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                Assert.False(recorder.IsRecording);
                Assert.Equal(1, stoppedCount);
            }
            finally
            {
                HotkeyRecorderControl.RecordingStarted -= onStarted;
                HotkeyRecorderControl.RecordingStopped -= onStopped;
            }
        });
    }
}
