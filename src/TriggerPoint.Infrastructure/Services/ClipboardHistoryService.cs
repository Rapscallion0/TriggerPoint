using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Serilog;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Win32;

namespace TriggerPoint.Infrastructure.Services;

public class ClipboardHistoryService : IClipboardHistoryService
{
    private readonly ILogger _logger = Log.ForContext<ClipboardHistoryService>();
    private readonly IAppPathsService _appPaths;
    private readonly IConfigRepository _configRepo;
    private readonly object _lock = new();

    private IntPtr _hwnd = IntPtr.Zero;
    private bool _isListening = false;
    private volatile bool _suppressNextCapture = false;
    private AppSettings? _settings;
    private List<ClipboardHistoryItem> _items = [];

    // Known sensitive / ignore formats
    private static readonly string[] SensitiveFormats =
    [
        "Clipboard Viewer Ignore",
        "CanIncludeInClipboardHistory",
        "ExcludeClipboardContentFromMonitorProcessing"
    ];

    // Password manager process names to ignore
    private static readonly HashSet<string> SensitiveProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "1Password",
        "Bitwarden",
        "KeePass",
        "KeePassXC",
        "LastPass",
        "Dashlane",
        "Enpass"
    };

    public event EventHandler? HistoryChanged;

    public bool IsRunning => _isListening;

    public ClipboardHistoryService(IAppPathsService appPaths, IConfigRepository configRepo)
    {
        _appPaths = appPaths;
        _configRepo = configRepo;

        LoadSettingsSync();
        LoadHistorySync();
    }

    private void LoadSettingsSync()
    {
        try
        {
            _settings = _configRepo.LoadSettingsAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load AppSettings for ClipboardHistoryService. Using defaults.");
            _settings = new AppSettings();
        }
    }

    public void ReloadSettings()
    {
        lock (_lock)
        {
            LoadSettingsSync();
        }
    }

    public void Start(IntPtr windowHandle)
    {
        lock (_lock)
        {
            if (_isListening) return;
            if (windowHandle == IntPtr.Zero) return;

            _hwnd = windowHandle;
            bool success = NativeMethods.AddClipboardFormatListener(_hwnd);
            _isListening = success;
            if (success)
            {
                _logger.Information("ClipboardHistoryService registered format listener on hwnd {Hwnd}.", _hwnd);
            }
            else
            {
                int err = Marshal.GetLastWin32Error();
                _logger.Warning("Failed to register clipboard format listener on hwnd {Hwnd}. Win32 error: {Error}", _hwnd, err);
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isListening) return;

            if (_hwnd != IntPtr.Zero)
            {
                NativeMethods.RemoveClipboardFormatListener(_hwnd);
            }
            _isListening = false;
            _logger.Information("ClipboardHistoryService stopped listening.");
        }
    }

    public void HandleClipboardUpdateMessage()
    {
        if (_settings != null && !_settings.EnableClipboardHistory)
        {
            return;
        }

        if (_suppressNextCapture)
        {
            _suppressNextCapture = false;
            return;
        }

        // Check if sensitive clipboard format is present
        if (IsSensitiveClipboardPresent())
        {
            _logger.Debug("Ignoring clipboard update: sensitive/ignore format detected.");
            return;
        }

        // Check foreground process
        IntPtr fgHwnd = NativeMethods.GetForegroundWindow();
        if (fgHwnd != IntPtr.Zero)
        {
            string? procName = NativeMethods.GetProcessNameForWindow(fgHwnd);
            if (!string.IsNullOrEmpty(procName) && SensitiveProcesses.Contains(procName))
            {
                _logger.Debug("Ignoring clipboard update from password manager process: {Process}", procName);
                return;
            }
        }

        // Read clipboard data on UI Dispatcher
        if (Application.Current != null)
        {
            Application.Current.Dispatcher.InvokeAsync(CaptureClipboardDataSafe);
        }
    }

    private bool IsSensitiveClipboardPresent()
    {
        foreach (var format in SensitiveFormats)
        {
            uint id = NativeMethods.RegisterClipboardFormat(format);
            if (id != 0 && NativeMethods.IsClipboardFormatAvailable(id))
            {
                return true;
            }
        }
        return false;
    }

    private void CaptureClipboardDataSafe()
    {
        try
        {
            IDataObject? dataObj = null;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    dataObj = Clipboard.GetDataObject();
                    if (dataObj != null) break;
                }
                catch
                {
                    Thread.Sleep(20);
                }
            }

            if (dataObj == null) return;

            IntPtr fgHwnd = NativeMethods.GetForegroundWindow();
            string? sourceProcess = fgHwnd != IntPtr.Zero ? NativeMethods.GetProcessNameForWindow(fgHwnd) : null;
            long maxSizeBytes = (_settings?.ClipboardMaxItemSizeMb ?? 10) * 1024L * 1024L;

            // 1. Files
            if (dataObj.GetDataPresent(DataFormats.FileDrop))
            {
                if (dataObj.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    var fileList = files.ToList();
                    string preview = fileList.Count == 1
                        ? Path.GetFileName(fileList[0])
                        : $"{fileList.Count} files: {string.Join(", ", fileList.Take(3).Select(Path.GetFileName))}";

                    long totalFileSize = 0;
                    foreach (var f in fileList)
                    {
                        try
                        {
                            if (File.Exists(f)) totalFileSize += new FileInfo(f).Length;
                        }
                        catch { }
                    }

                    var item = new ClipboardHistoryItem
                    {
                        Type = ClipboardItemType.FileList,
                        FilePaths = fileList,
                        PlainText = string.Join(Environment.NewLine, fileList),
                        PreviewText = preview,
                        SizeBytes = totalFileSize,
                        SourceProcessName = sourceProcess
                    };

                    AddCapturedItem(item);
                    return;
                }
            }

            // 2. Image
            if (dataObj.GetDataPresent(DataFormats.Bitmap))
            {
                var bitmap = Clipboard.GetImage();
                if (bitmap != null)
                {
                    Guid id = Guid.NewGuid();
                    _appPaths.EnsureDirectoriesCreated();
                    string imageFilePath = Path.Combine(_appPaths.ClipboardClipsDirectory, $"{id}.png");

                    long imageSize = 0;
                    using (var fs = new FileStream(imageFilePath, FileMode.Create, FileAccess.Write))
                    {
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        encoder.Save(fs);
                        imageSize = fs.Length;
                    }

                    if (imageSize > maxSizeBytes)
                    {
                        _logger.Information("Ignored clipboard image: exceeds max size of {Max}MB (actual {Actual}MB).",
                            _settings?.ClipboardMaxItemSizeMb ?? 10, imageSize / (1024 * 1024));
                        try { File.Delete(imageFilePath); } catch { }
                        return;
                    }

                    var item = new ClipboardHistoryItem
                    {
                        Id = id,
                        Type = ClipboardItemType.Image,
                        ImagePath = imageFilePath,
                        ImageWidth = (int)bitmap.PixelWidth,
                        ImageHeight = (int)bitmap.PixelHeight,
                        SizeBytes = imageSize,
                        PreviewText = $"Image ({bitmap.PixelWidth}×{bitmap.PixelHeight})",
                        SourceProcessName = sourceProcess
                    };

                    AddCapturedItem(item);
                    return;
                }
            }

            // 3. Rich Text / Plain Text
            string? plainText = null;
            string? rtfText = null;

            if (dataObj.GetDataPresent(DataFormats.UnicodeText))
            {
                plainText = dataObj.GetData(DataFormats.UnicodeText) as string;
            }
            else if (dataObj.GetDataPresent(DataFormats.Text))
            {
                plainText = dataObj.GetData(DataFormats.Text) as string;
            }

            if (dataObj.GetDataPresent(DataFormats.Rtf))
            {
                rtfText = dataObj.GetData(DataFormats.Rtf) as string;
            }

            if (!string.IsNullOrWhiteSpace(plainText) || !string.IsNullOrWhiteSpace(rtfText))
            {
                plainText ??= string.Empty;
                long byteSize = System.Text.Encoding.UTF8.GetByteCount(plainText) +
                                (rtfText != null ? System.Text.Encoding.UTF8.GetByteCount(rtfText) : 0);

                if (byteSize > maxSizeBytes)
                {
                    _logger.Information("Ignored clipboard text: exceeds max size of {Max}MB.", _settings?.ClipboardMaxItemSizeMb ?? 10);
                    return;
                }

                string preview = plainText.Trim();
                if (preview.Length > 200) preview = preview[..200] + "...";
                preview = preview.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");

                var itemType = !string.IsNullOrWhiteSpace(rtfText)
                    ? ClipboardItemType.RichText
                    : ClipboardItemType.PlainText;

                var item = new ClipboardHistoryItem
                {
                    Type = itemType,
                    PlainText = plainText,
                    RichText = rtfText,
                    PreviewText = preview,
                    SizeBytes = byteSize,
                    SourceProcessName = sourceProcess
                };

                AddCapturedItem(item);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to capture clipboard data.");
        }
    }

    private void AddCapturedItem(ClipboardHistoryItem newItem)
    {
        lock (_lock)
        {
            // Deduplicate: If same as existing item, remove older duplicate and promote to top
            int existingIndex = _items.FindIndex(i => IsDuplicate(i, newItem));
            if (existingIndex >= 0)
            {
                var existing = _items[existingIndex];
                newItem.IsPinned = existing.IsPinned;
                // If the old one had an image and the new one created a new file, clean up old file
                if (existing.ImagePath != null && existing.ImagePath != newItem.ImagePath)
                {
                    try { File.Delete(existing.ImagePath); } catch { }
                }
                _items.RemoveAt(existingIndex);
            }

            _items.Insert(0, newItem);
            PruneUnpinnedItems();
            SaveHistorySync();
        }

        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsDuplicate(ClipboardHistoryItem a, ClipboardHistoryItem b)
    {
        if (a.Type != b.Type) return false;

        return a.Type switch
        {
            ClipboardItemType.PlainText => string.Equals(a.PlainText, b.PlainText, StringComparison.Ordinal),
            ClipboardItemType.RichText => string.Equals(a.PlainText, b.PlainText, StringComparison.Ordinal)
                                          && string.Equals(a.RichText, b.RichText, StringComparison.Ordinal),
            ClipboardItemType.FileList => a.FilePaths != null && b.FilePaths != null
                                          && a.FilePaths.SequenceEqual(b.FilePaths, StringComparer.OrdinalIgnoreCase),
            ClipboardItemType.Image => a.SizeBytes == b.SizeBytes && a.ImageWidth == b.ImageWidth && a.ImageHeight == b.ImageHeight,
            _ => false
        };
    }

    private void PruneUnpinnedItems()
    {
        int maxItems = _settings?.ClipboardMaxItems ?? 50;
        int retentionDays = _settings?.ClipboardRetentionDays ?? 30;
        DateTime cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        // 1. Remove expired unpinned items
        var toRemoveExpired = _items.Where(i => !i.IsPinned && i.CapturedAt < cutoff).ToList();
        foreach (var item in toRemoveExpired)
        {
            DeleteAssociatedFiles(item);
            _items.Remove(item);
        }

        // 2. Enforce max items count (retaining pinned items)
        int unpinnedCount = _items.Count(i => !i.IsPinned);
        if (unpinnedCount > maxItems)
        {
            int removeCount = unpinnedCount - maxItems;
            for (int i = _items.Count - 1; i >= 0 && removeCount > 0; i--)
            {
                if (!_items[i].IsPinned)
                {
                    DeleteAssociatedFiles(_items[i]);
                    _items.RemoveAt(i);
                    removeCount--;
                }
            }
        }
    }

    private static void DeleteAssociatedFiles(ClipboardHistoryItem item)
    {
        if (!string.IsNullOrEmpty(item.ImagePath))
        {
            try
            {
                if (File.Exists(item.ImagePath))
                {
                    File.Delete(item.ImagePath);
                }
            }
            catch { }
        }
    }

    public IReadOnlyList<ClipboardHistoryItem> GetHistory()
    {
        lock (_lock)
        {
            return _items.ToList();
        }
    }

    public Task AddItemAsync(ClipboardHistoryItem item)
    {
        AddCapturedItem(item);
        return Task.CompletedTask;
    }

    public void PinItem(Guid id, bool isPinned)
    {
        lock (_lock)
        {
            var item = _items.FirstOrDefault(i => i.Id == id);
            if (item != null)
            {
                item.IsPinned = isPinned;
                SaveHistorySync();
            }
        }
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePin(Guid id)
    {
        lock (_lock)
        {
            var item = _items.FirstOrDefault(i => i.Id == id);
            if (item != null)
            {
                item.IsPinned = !item.IsPinned;
                SaveHistorySync();
            }
        }
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteItem(Guid id)
    {
        lock (_lock)
        {
            var item = _items.FirstOrDefault(i => i.Id == id);
            if (item != null)
            {
                DeleteAssociatedFiles(item);
                _items.Remove(item);
                SaveHistorySync();
            }
        }
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearHistory()
    {
        lock (_lock)
        {
            var unpinned = _items.Where(i => !i.IsPinned).ToList();
            foreach (var item in unpinned)
            {
                DeleteAssociatedFiles(item);
                _items.Remove(item);
            }
            SaveHistorySync();
        }
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public int PurgeHistory(int? olderThanDays = null, int? keepCount = null, bool keepPinned = true)
    {
        int purged = 0;
        lock (_lock)
        {
            if (olderThanDays.HasValue && olderThanDays.Value == 0 && keepCount == null)
            {
                var toPurge = keepPinned ? _items.Where(i => !i.IsPinned).ToList() : _items.ToList();
                foreach (var item in toPurge)
                {
                    DeleteAssociatedFiles(item);
                    _items.Remove(item);
                    purged++;
                }
            }
            else
            {
                if (olderThanDays.HasValue && olderThanDays.Value > 0)
                {
                    DateTime cutoff = DateTime.UtcNow.AddDays(-olderThanDays.Value);
                    var expired = (keepPinned ? _items.Where(i => !i.IsPinned) : _items).Where(i => i.CapturedAt < cutoff).ToList();
                    foreach (var item in expired)
                    {
                        DeleteAssociatedFiles(item);
                        _items.Remove(item);
                        purged++;
                    }
                }

                if (keepCount.HasValue && keepCount.Value >= 0)
                {
                    int eligibleCount = keepPinned ? _items.Count(i => !i.IsPinned) : _items.Count;
                    if (eligibleCount > keepCount.Value)
                    {
                        int toRemove = eligibleCount - keepCount.Value;
                        for (int i = _items.Count - 1; i >= 0 && toRemove > 0; i--)
                        {
                            if (!keepPinned || !_items[i].IsPinned)
                            {
                                DeleteAssociatedFiles(_items[i]);
                                _items.RemoveAt(i);
                                purged++;
                                toRemove--;
                            }
                        }
                    }
                }
            }

            if (purged > 0)
            {
                SaveHistorySync();
            }
        }

        if (purged > 0)
        {
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
        return purged;
    }

    public long GetStorageUsageBytes()
    {
        lock (_lock)
        {
            long total = 0;
            try
            {
                if (File.Exists(_appPaths.ClipboardHistoryFilePath))
                {
                    total += new FileInfo(_appPaths.ClipboardHistoryFilePath).Length;
                }
                if (Directory.Exists(_appPaths.ClipboardClipsDirectory))
                {
                    var dir = new DirectoryInfo(_appPaths.ClipboardClipsDirectory);
                    total += dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error computing clipboard storage usage.");
            }
            return total;
        }
    }

    public long GetTotalStorageBytes() => GetStorageUsageBytes();

    public async Task<bool> PasteItemAsync(ClipboardHistoryItem item, bool pasteAsPlainText, IntPtr? targetHwnd = null)
    {
        try
        {
            _suppressNextCapture = true;

            // Set clipboard on UI thread
            if (Application.Current != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    for (int attempt = 0; attempt < 5; attempt++)
                    {
                        try
                        {
                            if (pasteAsPlainText || item.Type == ClipboardItemType.PlainText)
                            {
                                string textToPaste = item.PlainText ?? item.PreviewText;
                                Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, textToPaste), true);
                            }
                            else if (item.Type == ClipboardItemType.RichText)
                            {
                                var dataObject = new DataObject();
                                if (!string.IsNullOrEmpty(item.RichText))
                                {
                                    dataObject.SetData(DataFormats.Rtf, item.RichText);
                                }
                                if (!string.IsNullOrEmpty(item.PlainText))
                                {
                                    dataObject.SetData(DataFormats.UnicodeText, item.PlainText);
                                    dataObject.SetData(DataFormats.Text, item.PlainText);
                                }
                                Clipboard.SetDataObject(dataObject, true);
                            }
                            else if (item.Type == ClipboardItemType.FileList && item.FilePaths != null && item.FilePaths.Count > 0)
                            {
                                var coll = new StringCollection();
                                coll.AddRange(item.FilePaths.ToArray());
                                Clipboard.SetFileDropList(coll);
                            }
                            else if (item.Type == ClipboardItemType.Image && !string.IsNullOrEmpty(item.ImagePath) && File.Exists(item.ImagePath))
                            {
                                var bitmap = new BitmapImage();
                                bitmap.BeginInit();
                                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                bitmap.UriSource = new Uri(item.ImagePath, UriKind.Absolute);
                                bitmap.EndInit();
                                bitmap.Freeze();
                                Clipboard.SetImage(bitmap);
                            }
                            return;
                        }
                        catch (Exception ex)
                        {
                            if (attempt == 4)
                            {
                                _logger.Warning(ex, "Failed to set clipboard data for paste after 5 attempts.");
                            }
                            Thread.Sleep(20);
                        }
                    }
                });
            }

            // Restore focus to target window if specified
            if (targetHwnd.HasValue && targetHwnd.Value != IntPtr.Zero)
            {
                NativeMethods.SetForegroundWindow(targetHwnd.Value);
                await Task.Delay(50).ConfigureAwait(false);
            }

            // Give clipboard a moment to settle
            await Task.Delay(30).ConfigureAwait(false);

            // Send Ctrl + V
            SendPasteCommand();

            // Settle suppression flag after paste completes
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000).ConfigureAwait(false);
                _suppressNextCapture = false;
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error while pasting clipboard history item.");
            _suppressNextCapture = false;
            return false;
        }
    }

    private static void SendPasteCommand()
    {
        var inputs = new NativeMethods.INPUT[4];

        // Ctrl down
        inputs[0] = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.InputUnion { ki = new NativeMethods.KEYBDINPUT { wVk = NativeMethods.VK_CONTROL } }
        };
        // V down
        inputs[1] = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.InputUnion { ki = new NativeMethods.KEYBDINPUT { wVk = NativeMethods.VK_V } }
        };
        // V up
        inputs[2] = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.InputUnion { ki = new NativeMethods.KEYBDINPUT { wVk = NativeMethods.VK_V, dwFlags = NativeMethods.KEYEVENTF_KEYUP } }
        };
        // Ctrl up
        inputs[3] = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.InputUnion { ki = new NativeMethods.KEYBDINPUT { wVk = NativeMethods.VK_CONTROL, dwFlags = NativeMethods.KEYEVENTF_KEYUP } }
        };

        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private void LoadHistorySync()
    {
        try
        {
            if (File.Exists(_appPaths.ClipboardHistoryFilePath))
            {
                string json = File.ReadAllText(_appPaths.ClipboardHistoryFilePath);
                var data = JsonSerializer.Deserialize<ClipboardHistoryDataStore>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (data?.Items != null)
                {
                    _items = data.Items;
                    PruneUnpinnedItems();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load clipboard history from file.");
            _items = [];
        }
    }

    private void SaveHistorySync()
    {
        try
        {
            _appPaths.EnsureDirectoriesCreated();
            var data = new ClipboardHistoryDataStore
            {
                Version = 1,
                Items = _items
            };
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_appPaths.ClipboardHistoryFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to save clipboard history to file.");
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
