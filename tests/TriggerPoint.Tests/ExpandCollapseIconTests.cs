using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Persistence;
using TriggerPoint.UI.Views;
using Xunit;

namespace TriggerPoint.Tests;

public class ExpandCollapseIconTests
{
    private class DummyShortcutListener : IShortcutListener
    {
        public bool IsSnoozed { get; set; }
        public IReadOnlyDictionary<Guid, HotkeyConflictStatus> CurrentConflicts { get; } = new Dictionary<Guid, HotkeyConflictStatus>();
        public event EventHandler<TriggerItem>? HotkeyTriggered { add { } remove { } }
        public event EventHandler? ConflictsUpdated { add { } remove { } }
        public event EventHandler<bool>? SnoozeChanged { add { } remove { } }

        public void Start(IntPtr windowHandle) { }
        public void Stop() { }
        public void RegisterAll(IEnumerable<TriggerItem> items) { }
        public void Suspend() { }
        public void Resume() { }
        public void Dispose() { }
    }

    private class DummyExecutor : IActionExecutor
    {
        public Task ExecuteAsync(TriggerItem item, ExecutionOverride executionOverride = ExecutionOverride.Standard, IntPtr? targetHwnd = null) => Task.CompletedTask;
    }

    [Fact]
    public void ExpandCollapseGeometries_AreFrozenAndValid()
    {
        Assert.NotNull(SettingsWindow.CollapseAllGeometry);
        Assert.NotNull(SettingsWindow.ExpandAllGeometry);

        Assert.True(SettingsWindow.CollapseAllGeometry.IsFrozen);
        Assert.True(SettingsWindow.ExpandAllGeometry.IsFrozen);

        // Verify geometries have non-empty bounds
        Assert.False(SettingsWindow.CollapseAllGeometry.Bounds.IsEmpty);
        Assert.False(SettingsWindow.ExpandAllGeometry.Bounds.IsEmpty);
    }

    [Fact]
    public void SettingsWindow_ExpandCollapseButtons_InitializeWithVectorPaths()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TriggerPoint_IconTest_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        Exception? caughtEx = null;
        try
        {
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

                    TriggerPoint.UI.Theme.ThemeManager.Initialize(ThemePreference.Dark);

                    var repo = new JsonConfigRepository(tempDir);
                    var shortcutListener = new DummyShortcutListener();
                    var executor = new DummyExecutor();

                    var window = new SettingsWindow(repo, shortcutListener, executor);
                    Assert.NotNull(window.ToggleExpandAllBtn);
                    Assert.NotNull(window.TreeExpandAllIcon);
                    Assert.NotNull(window.WorkflowToggleAllExpandBtn);
                    Assert.NotNull(window.WorkflowToggleAllIcon);

                    // Both should use the unified CollapseAll vector geometry on initial load
                    Assert.Equal(SettingsWindow.CollapseAllGeometry.ToString(), window.TreeExpandAllIcon.Data.ToString());
                    Assert.Equal(SettingsWindow.CollapseAllGeometry.ToString(), window.WorkflowToggleAllIcon.Data.ToString());
                }
                catch (Exception ex)
                {
                    caughtEx = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(5000);

            if (caughtEx != null)
            {
                throw new InvalidOperationException($"SettingsWindow expand/collapse buttons failed initialization: {caughtEx.Message}", caughtEx);
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch { }
        }
    }
}
