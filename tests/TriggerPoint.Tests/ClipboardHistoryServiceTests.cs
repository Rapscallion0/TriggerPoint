using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;
using TriggerPoint.Infrastructure.Persistence;
using TriggerPoint.Infrastructure.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class ClipboardHistoryServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppPathsService _appPaths;
    private readonly JsonConfigRepository _configRepo;

    public ClipboardHistoryServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TriggerPoint_ClipTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _appPaths = new AppPathsService(args: [], baseAppDirectory: _tempDir, customAppDataPath: _tempDir);
        _configRepo = new JsonConfigRepository(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public void ClipboardHistoryItem_ModelProperties_InitializeCorrectly()
    {
        var item = new ClipboardHistoryItem
        {
            Type = ClipboardItemType.PlainText,
            PlainText = "Test snippet",
            PreviewText = "Test snippet",
            SizeBytes = 12,
            SourceProcessName = "code"
        };

        Assert.NotEqual(Guid.Empty, item.Id);
        Assert.False(item.IsPinned);
        Assert.Equal("Test snippet", item.PlainText);
        Assert.Equal("code", item.SourceProcessName);
        Assert.Equal(12, item.SizeBytes);
        Assert.Equal(ClipboardItemType.PlainText, item.Type);
    }

    [Fact]
    public void ClipboardHistoryDataStore_SerializationRoundtrip_PreservesAllItems()
    {
        var store = new ClipboardHistoryDataStore
        {
            Version = 1,
            Items =
            [
                new ClipboardHistoryItem
                {
                    Type = ClipboardItemType.PlainText,
                    PlainText = "Alpha",
                    PreviewText = "Alpha",
                    IsPinned = true
                },
                new ClipboardHistoryItem
                {
                    Type = ClipboardItemType.RichText,
                    PlainText = "Beta",
                    RichText = "{\\rtf1\\ansi Beta}",
                    PreviewText = "Beta",
                    IsPinned = false
                },
                new ClipboardHistoryItem
                {
                    Type = ClipboardItemType.FileList,
                    FilePaths = ["C:\\test1.txt", "C:\\test2.txt"],
                    PlainText = "C:\\test1.txt\r\nC:\\test2.txt",
                    PreviewText = "2 files: test1.txt, test2.txt",
                    IsPinned = false
                }
            ]
        };

        var json = JsonSerializer.Serialize(store);
        var deserialized = JsonSerializer.Deserialize<ClipboardHistoryDataStore>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(3, deserialized.Items.Count);
        Assert.Equal("Alpha", deserialized.Items[0].PlainText);
        Assert.True(deserialized.Items[0].IsPinned);
        Assert.Equal(ClipboardItemType.RichText, deserialized.Items[1].Type);
        Assert.Equal("{\\rtf1\\ansi Beta}", deserialized.Items[1].RichText);
        Assert.Equal(ClipboardItemType.FileList, deserialized.Items[2].Type);
        Assert.Equal(2, deserialized.Items[2].FilePaths?.Count);
    }

    [Fact]
    public async Task ClipboardHistoryService_AddItemAsync_DeduplicatesAndPromotesToTop()
    {
        var service = new ClipboardHistoryService(_appPaths, _configRepo);

        var item1 = new ClipboardHistoryItem
        {
            Type = ClipboardItemType.PlainText,
            PlainText = "Hello World",
            PreviewText = "Hello World"
        };

        var item2 = new ClipboardHistoryItem
        {
            Type = ClipboardItemType.PlainText,
            PlainText = "Second Item",
            PreviewText = "Second Item"
        };

        await service.AddItemAsync(item1);
        await service.AddItemAsync(item2);

        var history = service.GetHistory();
        Assert.Equal(2, history.Count);
        Assert.Equal("Second Item", history[0].PlainText);
        Assert.Equal("Hello World", history[1].PlainText);

        // Add duplicate of item1
        var item1Dup = new ClipboardHistoryItem
        {
            Type = ClipboardItemType.PlainText,
            PlainText = "Hello World",
            PreviewText = "Hello World"
        };

        await service.AddItemAsync(item1Dup);

        history = service.GetHistory();
        Assert.Equal(2, history.Count);
        Assert.Equal("Hello World", history[0].PlainText); // Promoted to top
        Assert.Equal("Second Item", history[1].PlainText);
    }

    [Fact]
    public async Task ClipboardHistoryService_PinItem_And_TogglePin_UpdatesPinState()
    {
        var service = new ClipboardHistoryService(_appPaths, _configRepo);

        var item = new ClipboardHistoryItem
        {
            Type = ClipboardItemType.PlainText,
            PlainText = "Important Clip",
            PreviewText = "Important Clip"
        };

        await service.AddItemAsync(item);
        var id = service.GetHistory()[0].Id;

        // Verify initial state
        Assert.False(service.GetHistory()[0].IsPinned);

        // Toggle pin
        service.TogglePin(id);
        Assert.True(service.GetHistory()[0].IsPinned);

        // Toggle pin again
        service.TogglePin(id);
        Assert.False(service.GetHistory()[0].IsPinned);

        // Pin explicitly
        service.PinItem(id, true);
        Assert.True(service.GetHistory()[0].IsPinned);
    }

    [Fact]
    public async Task ClipboardHistoryService_PurgeHistory_KeepsPinnedItems()
    {
        var service = new ClipboardHistoryService(_appPaths, _configRepo);

        var pinned = new ClipboardHistoryItem
        {
            Type = ClipboardItemType.PlainText,
            PlainText = "Pinned Note",
            PreviewText = "Pinned Note"
        };
        var unpinned1 = new ClipboardHistoryItem
        {
            Type = ClipboardItemType.PlainText,
            PlainText = "Temp 1",
            PreviewText = "Temp 1"
        };
        var unpinned2 = new ClipboardHistoryItem
        {
            Type = ClipboardItemType.PlainText,
            PlainText = "Temp 2",
            PreviewText = "Temp 2"
        };

        await service.AddItemAsync(pinned);
        await service.AddItemAsync(unpinned1);
        await service.AddItemAsync(unpinned2);

        // Pin the first item
        var pinnedId = service.GetHistory().First(i => i.PlainText == "Pinned Note").Id;
        service.PinItem(pinnedId, true);

        Assert.Equal(3, service.GetHistory().Count);

        // Purge unpinned items
        int purged = service.PurgeHistory(olderThanDays: 0, keepPinned: true);
        Assert.Equal(2, purged);

        var remaining = service.GetHistory();
        Assert.Single(remaining);
        Assert.Equal("Pinned Note", remaining[0].PlainText);
        Assert.True(remaining[0].IsPinned);
    }

    [Fact]
    public async Task ClipboardHistoryService_DeleteItem_RemovesSpecificItem()
    {
        var service = new ClipboardHistoryService(_appPaths, _configRepo);

        var item1 = new ClipboardHistoryItem { Type = ClipboardItemType.PlainText, PlainText = "Keep Me", PreviewText = "Keep Me" };
        var item2 = new ClipboardHistoryItem { Type = ClipboardItemType.PlainText, PlainText = "Delete Me", PreviewText = "Delete Me" };

        await service.AddItemAsync(item1);
        await service.AddItemAsync(item2);

        var deleteId = service.GetHistory().First(i => i.PlainText == "Delete Me").Id;
        service.DeleteItem(deleteId);

        var remaining = service.GetHistory();
        Assert.Single(remaining);
        Assert.Equal("Keep Me", remaining[0].PlainText);
    }

    [Fact]
    public async Task ClipboardHistoryService_StorageCalculation_CalculatesFileSizes()
    {
        var service = new ClipboardHistoryService(_appPaths, _configRepo);

        var item = new ClipboardHistoryItem
        {
            Type = ClipboardItemType.PlainText,
            PlainText = "Storage Test Payload",
            PreviewText = "Storage Test Payload"
        };

        await service.AddItemAsync(item);

        long bytes = service.GetStorageUsageBytes();
        Assert.True(bytes > 0);
        Assert.Equal(bytes, service.GetTotalStorageBytes());
    }

    [Fact]
    public void ClipboardHistoryItem_FileList_DisplayTitle_FormatsSingleAndMultipleFiles()
    {
        // Single file
        var single = new ClipboardHistoryItem
        {
            Type = ClipboardItemType.FileList,
            FilePaths = [@"C:\Users\Documents\Report.docx"]
        };
        Assert.Equal("Report.docx", single.DisplayTitle);

        // Two files
        var two = new ClipboardHistoryItem
        {
            Type = ClipboardItemType.FileList,
            FilePaths = [@"C:\Users\Documents\Report.docx", @"C:\Users\Documents\Summary.xlsx"]
        };
        Assert.Equal("Report.docx, Summary.xlsx", two.DisplayTitle);

        // Three or more files
        var three = new ClipboardHistoryItem
        {
            Type = ClipboardItemType.FileList,
            FilePaths = [@"C:\Users\Documents\Report.docx", @"C:\Users\Documents\Summary.xlsx", @"C:\Users\Documents\Notes.txt", @"C:\Users\Documents\Data.csv"]
        };
        Assert.Equal("Report.docx, Summary.xlsx (+2 more)", three.DisplayTitle);
    }

    [Fact]
    public void ClipboardHistoryItem_FileList_DirectoryAndSummary_ResolvesDirectory()
    {
        // Same directory
        var sameDir = new ClipboardHistoryItem
        {
            Type = ClipboardItemType.FileList,
            FilePaths = [@"C:\Users\Documents\Report.docx", @"C:\Users\Documents\Summary.xlsx"]
        };
        Assert.Equal(@"C:\Users\Documents", sameDir.PrimaryDirectoryPath);
        Assert.Equal(@"2 files • C:\Users\Documents", sameDir.FilesSummaryText);

        // Different directories
        var diffDir = new ClipboardHistoryItem
        {
            Type = ClipboardItemType.FileList,
            FilePaths = [@"C:\FolderA\File1.txt", @"D:\FolderB\File2.txt"]
        };
        Assert.Null(diffDir.PrimaryDirectoryPath);
        Assert.Equal("2 files • 2 files across multiple locations", diffDir.FilesSummaryText);
    }

    [Fact]
    public void TrayIconService_FormatClipboardMenuText_FormatsCorrectly()
    {
        Assert.Equal("Clipboard History (Ctrl+Shift+V)", TriggerPoint.UI.Services.TrayIconService.FormatClipboardMenuText("Ctrl+Shift+V"));
        Assert.Equal("Clipboard History", TriggerPoint.UI.Services.TrayIconService.FormatClipboardMenuText(null));
        Assert.Equal("Clipboard History", TriggerPoint.UI.Services.TrayIconService.FormatClipboardMenuText(""));
    }
}
