using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.UI.Views;
using Xunit;

namespace TriggerPoint.Tests;

public class CommandPaletteUnifiedSearchTests
{
    private class DummyExecutor : IActionExecutor
    {
        public event EventHandler<string>? StatusChanged { add { } remove { } }
        public event EventHandler<(TriggerItem item, string message)>? ExecutionSucceeded { add { } remove { } }
        public event EventHandler<(TriggerItem item, string message)>? ExecutionFailed { add { } remove { } }
        public Task ExecuteAsync(TriggerItem item, ExecutionOverride executionOverride = ExecutionOverride.Standard, IntPtr? targetHwnd = null) => Task.CompletedTask;
    }

    private class DummyServiceManager : IWindowsServiceManager
    {
        public List<WindowsServiceItem> Services { get; set; } = [];
        public IReadOnlyList<WindowsServiceItem> GetServices() => Services;
        public WindowsServiceStatus GetServiceStatus(string serviceName) => WindowsServiceStatus.Running;
        public Task<ServiceOperationResult> StartServiceAsync(string serviceName, bool runAsAdmin = false, int timeoutSeconds = 30, CancellationToken cancellationToken = default) => Task.FromResult(new ServiceOperationResult(true, "", WindowsServiceStatus.Running));
        public Task<ServiceOperationResult> StopServiceAsync(string serviceName, bool runAsAdmin = false, int timeoutSeconds = 30, CancellationToken cancellationToken = default) => Task.FromResult(new ServiceOperationResult(true, "", WindowsServiceStatus.Stopped));
        public Task<ServiceOperationResult> RestartServiceAsync(string serviceName, bool runAsAdmin = false, int timeoutSeconds = 30, CancellationToken cancellationToken = default) => Task.FromResult(new ServiceOperationResult(true, "", WindowsServiceStatus.Running));
        public Task<ServiceOperationResult> ToggleServiceAsync(string serviceName, bool runAsAdmin = false, int timeoutSeconds = 30, CancellationToken cancellationToken = default) => Task.FromResult(new ServiceOperationResult(true, "", WindowsServiceStatus.Running));
    }

    private class DummyClipboardService : IClipboardHistoryService
    {
        public List<ClipboardHistoryItem> History { get; set; } = [];
        public void Start(IntPtr windowHandle) { }
        public void Stop() { }
        public bool IsRunning => true;
        public void ReloadSettings() { }
        public IReadOnlyList<ClipboardHistoryItem> GetHistory() => History;
        public Task AddItemAsync(ClipboardHistoryItem item) => Task.CompletedTask;
        public void PinItem(Guid id, bool isPinned) { }
        public void TogglePin(Guid id) { }
        public void DeleteItem(Guid id) { }
        public void ClearHistory() { }
        public int PurgeHistory(int? olderThanDays = null, int? keepCount = null, bool keepPinned = true) => 0;
        public long GetStorageUsageBytes() => 0;
        public long GetTotalStorageBytes() => 0;
        public void HandleClipboardUpdateMessage() { }
        public Task<bool> PasteItemAsync(ClipboardHistoryItem item, bool pasteAsPlainText, IntPtr? targetHwnd = null) => Task.FromResult(true);
        public event EventHandler? HistoryChanged { add { } remove { } }
        public void Dispose() { }
    }

    private static void EnsureApplicationAndThemeResources()
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
    }

    [Fact]
    public void CommandPalette_AllFilter_EmptyQuery_ReturnsOnlyActionsAndVirtualSystemActions()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationAndThemeResources();

                var actions = new List<TriggerItem>
                {
                    new() { Id = Guid.NewGuid(), Name = "Visual Studio", ActionType = ActionType.Shell, IsEnabled = true },
                    new() { Id = Guid.NewGuid(), Name = "Notepad", ActionType = ActionType.Shell, IsEnabled = true }
                };

                var dummyClip = new DummyClipboardService
                {
                    History =
                    [
                        new() { Id = Guid.NewGuid(), Type = ClipboardItemType.PlainText, PreviewText = "Snippet Text", PlainText = "Snippet Text" },
                        new() { Id = Guid.NewGuid(), Type = ClipboardItemType.PlainText, PreviewText = "Invoice Date", PlainText = "Invoice Date" }
                    ]
                };

                var dummySvc = new DummyServiceManager
                {
                    Services =
                    [
                        new() { ServiceName = "Spooler", DisplayName = "Print Spooler", Status = WindowsServiceStatus.Running },
                        new() { ServiceName = "Docker", DisplayName = "Docker Desktop Service", Status = WindowsServiceStatus.Running }
                    ]
                };

                var view = new CommandPaletteView(
                    actions,
                    new DummyExecutor(),
                    repository: null,
                    scopedFolderId: null,
                    scopedFolderName: null,
                    targetHwnd: default,
                    serviceManager: dummySvc,
                    clipboardService: dummyClip,
                    initialFilter: CommandPaletteFilterType.All);

                view.FilterResults();

                // With empty search in "All" filter, list must only contain action items and section headers for actions
                var items = (view.ResultsListBox.ItemsSource as IEnumerable<PaletteItemViewModel>)?.ToList();
                Assert.NotNull(items);

                // Should NOT contain any clipboard items
                Assert.DoesNotContain(items, x => x.IsClipboardItem);

                // Should NOT contain any services
                Assert.DoesNotContain(items, x => x.IsServiceItem);

                // Should contain the action names
                Assert.Contains(items, x => x.Item?.Name == "Visual Studio");
                Assert.Contains(items, x => x.Item?.Name == "Notepad");
            }
            catch (Exception ex)
            {
                caughtEx = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);

        if (caughtEx != null) throw new InvalidOperationException($"Test failed: {caughtEx.Message}", caughtEx);
    }

    [Fact]
    public void CommandPalette_AllFilter_ActiveQuery_ReturnsActionsClipboardAndServices_WithSectionHeaders()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationAndThemeResources();

                var actions = new List<TriggerItem>
                {
                    new() { Id = Guid.NewGuid(), Name = "Postgres Database Client", ActionType = ActionType.Shell, IsEnabled = true },
                    new() { Id = Guid.NewGuid(), Name = "Notepad", ActionType = ActionType.Shell, IsEnabled = true }
                };

                var dummyClip = new DummyClipboardService
                {
                    History =
                    [
                        new() { Id = Guid.NewGuid(), Type = ClipboardItemType.PlainText, PreviewText = "Postgres connection string", PlainText = "Postgres connection string: localhost:5432" },
                        new() { Id = Guid.NewGuid(), Type = ClipboardItemType.PlainText, PreviewText = "Invoice Notes", PlainText = "Invoice Notes" }
                    ]
                };

                var dummySvc = new DummyServiceManager
                {
                    Services =
                    [
                        new() { ServiceName = "postgresql-x64-16", DisplayName = "postgresql-x64-16 - PostgreSQL Server", Status = WindowsServiceStatus.Running },
                        new() { ServiceName = "Spooler", DisplayName = "Print Spooler", Status = WindowsServiceStatus.Running }
                    ]
                };

                var view = new CommandPaletteView(
                    actions,
                    new DummyExecutor(),
                    repository: null,
                    scopedFolderId: null,
                    scopedFolderName: null,
                    targetHwnd: default,
                    serviceManager: dummySvc,
                    clipboardService: dummyClip,
                    initialFilter: CommandPaletteFilterType.All);

                // Act: Type "postgres"
                view.SearchTextBox.Text = "postgres";

                var items = (view.ResultsListBox.ItemsSource as IEnumerable<PaletteItemViewModel>)?.ToList();
                Assert.NotNull(items);

                // Verify section headers exist because multiple categories match
                Assert.Contains(items, x => x.IsSectionHeader && x.SectionHeaderText.StartsWith("ACTIONS (1)"));
                Assert.Contains(items, x => x.IsSectionHeader && x.SectionHeaderText.StartsWith("CLIPBOARD HISTORY (1)"));
                Assert.Contains(items, x => x.IsSectionHeader && x.SectionHeaderText.StartsWith("WINDOWS SERVICES (1)"));

                // Verify items exist
                Assert.Contains(items, x => x.Item?.Name == "Postgres Database Client");
                Assert.Contains(items, x => x.IsClipboardItem && x.ClipboardItem?.PreviewText == "Postgres connection string");
                Assert.Contains(items, x => x.IsServiceItem && x.ServiceItem?.ServiceName == "postgresql-x64-16");

                // Badge count should reflect the total number of selectable matches (3)
                Assert.Equal("All (3)", view.FilterCountAll.Text);
            }
            catch (Exception ex)
            {
                caughtEx = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);

        if (caughtEx != null) throw new InvalidOperationException($"Test failed: {caughtEx.Message}", caughtEx);
    }

    [Fact]
    public void CommandPalette_AllFilter_RespectsMaxConfiguredLimits()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationAndThemeResources();

                var actions = new List<TriggerItem>
                {
                    new() { Id = Guid.NewGuid(), Name = "Server Manager", ActionType = ActionType.Shell, IsEnabled = true },
                    new() { Id = Guid.NewGuid(), Name = "Server Log Viewer", ActionType = ActionType.Shell, IsEnabled = true }
                };

                var clips = Enumerable.Range(1, 10).Select(i => new ClipboardHistoryItem
                {
                    Id = Guid.NewGuid(),
                    Type = ClipboardItemType.PlainText,
                    PreviewText = $"Server config item #{i}",
                    PlainText = $"Server config item #{i}"
                }).ToList();

                var svcs = Enumerable.Range(1, 10).Select(i => new WindowsServiceItem
                {
                    ServiceName = $"ServerSvc{i}",
                    DisplayName = $"Server Background Service #{i}",
                    Status = WindowsServiceStatus.Running
                }).ToList();

                var dummyClip = new DummyClipboardService { History = clips };
                var dummySvc = new DummyServiceManager { Services = svcs };

                var view = new CommandPaletteView(
                    actions,
                    new DummyExecutor(),
                    repository: null,
                    scopedFolderId: null,
                    scopedFolderName: null,
                    targetHwnd: default,
                    serviceManager: dummySvc,
                    clipboardService: dummyClip,
                    initialFilter: CommandPaletteFilterType.All);

                // Configure limits: 3 for clips, 4 for services
                view._maxClipboardResultsInAll = 3;
                view._maxServicesResultsInAll = 4;

                view.SearchTextBox.Text = "server";

                var items = (view.ResultsListBox.ItemsSource as IEnumerable<PaletteItemViewModel>)?.ToList();
                Assert.NotNull(items);

                // Section headers should reflect capped counts
                Assert.Contains(items, x => x.IsSectionHeader && x.SectionHeaderText == "ACTIONS (2)");
                Assert.Contains(items, x => x.IsSectionHeader && x.SectionHeaderText == "CLIPBOARD HISTORY (3)");
                Assert.Contains(items, x => x.IsSectionHeader && x.SectionHeaderText == "WINDOWS SERVICES (4)");

                // Verifying individual count of items
                int actionCount = items.Count(x => !x.IsSectionHeader && !x.IsClipboardItem && !x.IsServiceItem);
                int clipCount = items.Count(x => x.IsClipboardItem);
                int svcCount = items.Count(x => x.IsServiceItem);

                Assert.Equal(2, actionCount);
                Assert.Equal(3, clipCount);
                Assert.Equal(4, svcCount);

                // Badge count: 2 + 3 + 4 = 9
                Assert.Equal("All (9)", view.FilterCountAll.Text);
            }
            catch (Exception ex)
            {
                caughtEx = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);

        if (caughtEx != null) throw new InvalidOperationException($"Test failed: {caughtEx.Message}", caughtEx);
    }

    [Fact]
    public void CommandPalette_AllFilter_ExclusionSettingsRespected()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationAndThemeResources();

                var actions = new List<TriggerItem>
                {
                    new() { Id = Guid.NewGuid(), Name = "Backup Tool", ActionType = ActionType.Shell, IsEnabled = true }
                };

                var dummyClip = new DummyClipboardService
                {
                    History =
                    [
                        new() { Id = Guid.NewGuid(), Type = ClipboardItemType.PlainText, PreviewText = "Backup script", PlainText = "Backup script" }
                    ]
                };

                var dummySvc = new DummyServiceManager
                {
                    Services =
                    [
                        new() { ServiceName = "BackupSvc", DisplayName = "Backup Service", Status = WindowsServiceStatus.Running }
                    ]
                };

                var view = new CommandPaletteView(
                    actions,
                    new DummyExecutor(),
                    repository: null,
                    scopedFolderId: null,
                    scopedFolderName: null,
                    targetHwnd: default,
                    serviceManager: dummySvc,
                    clipboardService: dummyClip,
                    initialFilter: CommandPaletteFilterType.All);

                // Exclude clipboard and services
                view._includeClipboardInAll = false;
                view._includeServicesInAll = false;

                view.SearchTextBox.Text = "backup";

                var items = (view.ResultsListBox.ItemsSource as IEnumerable<PaletteItemViewModel>)?.ToList();
                Assert.NotNull(items);

                // Should ONLY have the action
                Assert.DoesNotContain(items, x => x.IsClipboardItem);
                Assert.DoesNotContain(items, x => x.IsServiceItem);
                Assert.Contains(items, x => x.Item?.Name == "Backup Tool");

                // With only 1 category matching, no section headers should be displayed
                Assert.DoesNotContain(items, x => x.IsSectionHeader);

                // Badge should show 1
                Assert.Equal("All (1)", view.FilterCountAll.Text);
            }
            catch (Exception ex)
            {
                caughtEx = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);

        if (caughtEx != null) throw new InvalidOperationException($"Test failed: {caughtEx.Message}", caughtEx);
    }

    [Fact]
    public void CommandPalette_AllFilter_SingleCategoryMatch_OmitsSectionHeaders()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationAndThemeResources();

                var actions = new List<TriggerItem>
                {
                    new() { Id = Guid.NewGuid(), Name = "Calculator Action", ActionType = ActionType.Shell, IsEnabled = true }
                };

                var dummyClip = new DummyClipboardService
                {
                    History =
                    [
                        new() { Id = Guid.NewGuid(), Type = ClipboardItemType.PlainText, PreviewText = "Something else entirely", PlainText = "XYZ" }
                    ]
                };

                var dummySvc = new DummyServiceManager
                {
                    Services =
                    [
                        new() { ServiceName = "Spooler", DisplayName = "Print Spooler", Status = WindowsServiceStatus.Running }
                    ]
                };

                var view = new CommandPaletteView(
                    actions,
                    new DummyExecutor(),
                    repository: null,
                    scopedFolderId: null,
                    scopedFolderName: null,
                    targetHwnd: default,
                    serviceManager: dummySvc,
                    clipboardService: dummyClip,
                    initialFilter: CommandPaletteFilterType.All);

                view.SearchTextBox.Text = "calculator action";

                var items = (view.ResultsListBox.ItemsSource as IEnumerable<PaletteItemViewModel>)?.ToList();
                Assert.NotNull(items);

                // Only action matched, so no section headers!
                Assert.DoesNotContain(items, x => x.IsSectionHeader);
                Assert.Contains(items, x => x.Item?.Name == "Calculator Action");
            }
            catch (Exception ex)
            {
                caughtEx = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);

        if (caughtEx != null) throw new InvalidOperationException($"Test failed: {caughtEx.Message}", caughtEx);
    }
}
