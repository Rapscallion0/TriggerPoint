using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;

namespace TriggerPoint.Infrastructure.Persistence;

public class JsonConfigRepository : IConfigRepository
{
    private readonly string _configFilePath;
    private readonly string _backupFilePath;
    private readonly string _backupsDir;
    private readonly string _appSettingsFilePath;
    private readonly string _appSettingsBackupFilePath;
    private readonly string _recycleBinFilePath;
    private readonly ISecretsVaultService _secretsVault;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: true) }
    };

    public JsonConfigRepository(string? customDirectory = null, ISecretsVaultService? secretsVault = null)
    {
        _secretsVault = secretsVault ?? new Services.WindowsDpapiSecretsVaultService();
        var baseDir = customDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            "TriggerPoint");

        Directory.CreateDirectory(baseDir);
        _configFilePath = Path.Combine(baseDir, "triggerpoint.json");
        _backupFilePath = Path.Combine(baseDir, "triggerpoint.bak");
        _backupsDir = Path.Combine(baseDir, "backups");
        _appSettingsFilePath = Path.Combine(baseDir, "appsettings.json");
        _appSettingsBackupFilePath = Path.Combine(baseDir, "appsettings.bak");
        _recycleBinFilePath = Path.Combine(baseDir, "recyclebin.json");

        Directory.CreateDirectory(_backupsDir);
    }

    public string ConfigFilePath => _configFilePath;
    public string BackupFilePath => _backupFilePath;
    public string BackupsDirectory => _backupsDir;
    public string AppSettingsFilePath => _appSettingsFilePath;
    public string RecycleBinFilePath => _recycleBinFilePath;

    private static bool IsValidTriggerItemsFile(string filePath, out List<TriggerItem>? items)
    {
        items = null;
        try
        {
            if (!File.Exists(filePath)) return false;
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length < 4) return false;

            using var stream = File.OpenRead(filePath);
            items = JsonSerializer.Deserialize<List<TriggerItem>>(stream, JsonOptions);
            return items != null && items.Count > 0;
        }
        catch
        {
            items = null;
            return false;
        }
    }

    private void DecryptSecrets(IEnumerable<TriggerItem>? items)
    {
        if (items == null || _secretsVault == null) return;
        foreach (var item in items)
        {
            DecryptItemSecrets(item);
        }
    }

    private void DecryptItemSecrets(TriggerItem item)
    {
        if (item.Payload?.WorkflowVariables != null && _secretsVault != null)
        {
            foreach (var v in item.Payload.WorkflowVariables)
            {
                if (v.IsSecret && _secretsVault.IsProtected(v.Value))
                {
                    v.Value = _secretsVault.Unprotect(v.Value);
                }
            }
        }
    }

    private List<TriggerItem> PrepareItemsForSerialization(IEnumerable<TriggerItem> items)
    {
        var list = new List<TriggerItem>();
        foreach (var item in items)
        {
            var clone = item.Clone();
            if (clone.Payload?.WorkflowVariables != null && _secretsVault != null)
            {
                foreach (var v in clone.Payload.WorkflowVariables)
                {
                    if (v.IsSecret && !_secretsVault.IsProtected(v.Value))
                    {
                        v.Value = _secretsVault.Protect(v.Value);
                    }
                }
            }
            list.Add(clone);
        }
        return list;
    }

    private List<TriggerItem>? TryRecoverFromBackups()
    {
        // 1. Try triggerpoint.bak
        if (IsValidTriggerItemsFile(_backupFilePath, out var backupItems) && backupItems != null)
        {
            try
            {
                File.Copy(_backupFilePath, _configFilePath, true);
            }
            catch { }
            DecryptSecrets(backupItems);
            return backupItems;
        }

        // 2. Try historical snapshots in backups/
        if (Directory.Exists(_backupsDir))
        {
            var snapshotFiles = Directory.GetFiles(_backupsDir, "triggerpoint_*.json")
                .OrderByDescending(File.GetLastWriteTimeUtc);

            foreach (var file in snapshotFiles)
            {
                if (IsValidTriggerItemsFile(file, out var snapshotItems) && snapshotItems != null)
                {
                    try
                    {
                        File.Copy(file, _configFilePath, true);
                        File.Copy(file, _backupFilePath, true);
                    }
                    catch { }
                    DecryptSecrets(snapshotItems);
                    return snapshotItems;
                }
            }
        }

        return null;
    }

    private void RotateHistoricalSnapshot(string sourceFilePath)
    {
        try
        {
            if (!Directory.Exists(_backupsDir))
            {
                Directory.CreateDirectory(_backupsDir);
            }

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var destPath = Path.Combine(_backupsDir, $"triggerpoint_{timestamp}.json");
            File.Copy(sourceFilePath, destPath, true);

            // Retain the 10 newest snapshots, prune older ones
            var existing = Directory.GetFiles(_backupsDir, "triggerpoint_*.json")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Skip(10);

            foreach (var oldFile in existing)
            {
                try { File.Delete(oldFile); } catch { }
            }
        }
        catch { }
    }

    public async Task<IReadOnlyList<TriggerItem>> LoadAsync()
    {
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (File.Exists(_configFilePath))
            {
                try
                {
                    if (new FileInfo(_configFilePath).Length > 0)
                    {
                        using var stream = File.OpenRead(_configFilePath);
                        var items = await JsonSerializer.DeserializeAsync<List<TriggerItem>>(stream, JsonOptions).ConfigureAwait(false);
                        if (items != null && items.Count > 0)
                        {
                            DecryptSecrets(items);
                            return items;
                        }
                    }
                }
                catch (Exception)
                {
                    // Primary file corrupted, 0-bytes, or invalid
                }

                // If primary file was 0-bytes, empty array, or corrupted, attempt recovery
                var recovered = TryRecoverFromBackups();
                if (recovered != null && recovered.Count > 0)
                {
                    return recovered;
                }
            }
            else
            {
                // File does not exist yet; check if backups exist before creating defaults
                var recovered = TryRecoverFromBackups();
                if (recovered != null && recovered.Count > 0)
                {
                    return recovered;
                }
            }

            // Only if primary doesn't exist and no backups exist, generate defaults
            var defaults = CreateDefaultItems();
            await SaveInternalAsync(defaults, isGeneratingDefaults: true).ConfigureAwait(false);
            return defaults;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveAsync(IEnumerable<TriggerItem> items)
    {
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await SaveInternalAsync(items, isGeneratingDefaults: false).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<AppSettings> LoadSettingsAsync()
    {
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (File.Exists(_appSettingsFilePath))
            {
                try
                {
                    using var stream = File.OpenRead(_appSettingsFilePath);
                    var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions).ConfigureAwait(false);
                    if (settings != null)
                    {
                        settings.Normalize();
                        return settings;
                    }
                }
                catch (Exception)
                {
                    // Primary settings file corrupted, try backup recovery
                    if (File.Exists(_appSettingsBackupFilePath))
                    {
                        try
                        {
                            using var backupStream = File.OpenRead(_appSettingsBackupFilePath);
                            var backupSettings = await JsonSerializer.DeserializeAsync<AppSettings>(backupStream, JsonOptions).ConfigureAwait(false);
                            if (backupSettings != null)
                            {
                                File.Copy(_appSettingsBackupFilePath, _appSettingsFilePath, true);
                                backupSettings.Normalize();
                                return backupSettings;
                            }
                        }
                        catch { }
                    }
                }
            }

            var defaultSettings = new AppSettings();
            await SaveSettingsInternalAsync(defaultSettings).ConfigureAwait(false);
            return defaultSettings;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        settings.Normalize();

        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await SaveSettingsInternalAsync(settings).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task SaveSettingsInternalAsync(AppSettings settings)
    {
        var tempFilePath = _appSettingsFilePath + ".tmp";
        await using (var stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        // Rotate rolling backup ONLY IF existing appsettings.json is valid and non-empty
        if (File.Exists(_appSettingsFilePath) && new FileInfo(_appSettingsFilePath).Length > 10)
        {
            try
            {
                File.Copy(_appSettingsFilePath, _appSettingsBackupFilePath, true);
            }
            catch { }
        }

        File.Move(tempFilePath, _appSettingsFilePath, true);
    }

    private async Task SaveInternalAsync(IEnumerable<TriggerItem> items, bool isGeneratingDefaults)
    {
        var itemList = new List<TriggerItem>(items);

        // Safety Guard: Do not allow saving an empty list if existing config on disk has valid items
        if (itemList.Count == 0 && !isGeneratingDefaults && IsValidTriggerItemsFile(_configFilePath, out var existing) && existing != null && existing.Count > 0)
        {
            return;
        }

        var tempFilePath = _configFilePath + ".tmp";

        // 1. Write to temporary file and force OS flush to physical disk
        var serializedList = PrepareItemsForSerialization(itemList);
        await using (var stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
        {
            await JsonSerializer.SerializeAsync(stream, serializedList, JsonOptions).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        // 2. Rotate rolling backup ONLY IF the current primary file is non-empty and valid!
        if (IsValidTriggerItemsFile(_configFilePath, out _))
        {
            File.Copy(_configFilePath, _backupFilePath, true);
            RotateHistoricalSnapshot(_configFilePath);
        }

        // 3. Atomically replace target
        File.Move(tempFilePath, _configFilePath, true);
    }

    private static List<TriggerItem> CreateDefaultItems()
    {
        var generalFolderId = Guid.NewGuid();
        var devFolderId = Guid.NewGuid();

        return
        [
            new TriggerItem
            {
                Id = generalFolderId,
                Name = "General Tools",
                ActionType = ActionType.Folder,
                OrderIndex = 0
            },
            new TriggerItem
            {
                Id = Guid.NewGuid(),
                ParentId = generalFolderId,
                Name = "Command Palette",
                Description = "Universal fuzzy search launcher",
                Hotkey = new ShortcutBinding(ModifierKeys.Control | ModifierKeys.Shift, 80, "P"), // Ctrl + Shift + P
                PresentationMode = PresentationMode.CommandPalette,
                ActionType = ActionType.Shell,
                OrderIndex = 0
            },
            new TriggerItem
            {
                Id = Guid.NewGuid(),
                ParentId = generalFolderId,
                Name = "Cursor Context Menu",
                Description = "Lightweight popup menu at mouse pointer",
                Hotkey = new ShortcutBinding(ModifierKeys.Control | ModifierKeys.Alt, 32, "Space"), // Ctrl + Alt + Space
                PresentationMode = PresentationMode.CursorMenu,
                ActionType = ActionType.Folder,
                OrderIndex = 1
            },
            new TriggerItem
            {
                Id = Guid.NewGuid(),
                ParentId = generalFolderId,
                Name = "Calculator",
                Description = "Windows Calculator",
                AcceleratorKey = "1",
                Hotkey = new ShortcutBinding(ModifierKeys.Control | ModifierKeys.Alt, 67, "C"), // Ctrl + Alt + C
                PresentationMode = PresentationMode.Direct,
                ActionType = ActionType.Shell,
                Payload = new ActionPayload
                {
                    Command = "calc.exe"
                },
                OrderIndex = 2
            },
            new TriggerItem
            {
                Id = Guid.NewGuid(),
                ParentId = generalFolderId,
                Name = "Timestamp Stamp",
                Description = "Inserts current date and time",
                AcceleratorKey = "2",
                PresentationMode = PresentationMode.Direct,
                ActionType = ActionType.Snippet,
                Payload = new ActionPayload
                {
                    SnippetTemplate = "[{datetime}] {cursor}"
                },
                OrderIndex = 3
            },
            new TriggerItem
            {
                Id = devFolderId,
                Name = "Developer Tools",
                ActionType = ActionType.Folder,
                OrderIndex = 1
            },
            new TriggerItem
            {
                Id = Guid.NewGuid(),
                ParentId = devFolderId,
                Name = "Git Commit Message",
                Description = "Interactive prompt to format conventional commit",
                AcceleratorKey = "G",
                PresentationMode = PresentationMode.Direct,
                ActionType = ActionType.Snippet,
                Payload = new ActionPayload
                {
                    SnippetTemplate = "{choice:Type|feat=feat,fix=fix,docs=docs,refactor=refactor}({text:Scope}): {text:Description}\n\n{multiline:Body}\n{cursor}"
                },
                OrderIndex = 0
            },
            new TriggerItem
            {
                Id = Guid.NewGuid(),
                ParentId = devFolderId,
                Name = "Terminal",
                Description = "Open Windows Terminal",
                AcceleratorKey = "T",
                Hotkey = new ShortcutBinding(ModifierKeys.Control | ModifierKeys.Alt, 84, "T"), // Ctrl + Alt + T
                PresentationMode = PresentationMode.Direct,
                ActionType = ActionType.Shell,
                Payload = new ActionPayload
                {
                    Command = "wt.exe"
                },
                OrderIndex = 1
            }
        ];
    }

    public async Task ExportPackageAsync(string filePath, ConfigurationBackupPackage package)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path cannot be empty", nameof(filePath));

        var exportItems = PrepareItemsForSerialization(package.Items);

        var exportPackage = new ConfigurationBackupPackage
        {
            SchemaVersion = package.SchemaVersion,
            AppVersion = package.AppVersion,
            ExportedAt = package.ExportedAt,
            ContentType = package.ContentType,
            ScopeName = package.ScopeName,
            FolderCount = exportItems.Count(x => x.ActionType == ActionType.Folder),
            ActionCount = exportItems.Count(x => x.ActionType != ActionType.Folder),
            Settings = package.Settings,
            Items = exportItems
        };

        var tempFile = filePath + ".tmp";
        using (var stream = File.Create(tempFile))
        {
            await JsonSerializer.SerializeAsync(stream, exportPackage, JsonOptions).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        File.Move(tempFile, filePath, overwrite: true);
    }

    public async Task<ConfigurationBackupPackage> ReadPackageAsync(string filePath)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException($"File not found: {filePath}", filePath);

        using var stream = File.OpenRead(filePath);

        // 1. Try reading as modern ConfigurationBackupPackage
        try
        {
            var package = await JsonSerializer.DeserializeAsync<ConfigurationBackupPackage>(stream, JsonOptions).ConfigureAwait(false);
            if (package != null && (package.Items.Count > 0 || package.Settings != null))
            {
                if (package.FolderCount == 0 && package.ActionCount == 0 && package.Items.Count > 0)
                {
                    package.FolderCount = package.Items.Count(x => x.ActionType == ActionType.Folder);
                    package.ActionCount = package.Items.Count(x => x.ActionType != ActionType.Folder);
                }
                DecryptSecrets(package.Items);
                return package;
            }
        }
        catch
        {
            // Reset stream and attempt legacy parse below
        }

        // 2. Try backwards-compatible fallback for flat List<TriggerItem>
        stream.Position = 0;
        try
        {
            var legacyItems = await JsonSerializer.DeserializeAsync<List<TriggerItem>>(stream, JsonOptions).ConfigureAwait(false);
            if (legacyItems != null && legacyItems.Count > 0)
            {
                DecryptSecrets(legacyItems);
                return new ConfigurationBackupPackage
                {
                    SchemaVersion = 1,
                    AppVersion = "1.0.0",
                    ContentType = BackupContentType.TreeItems,
                    ExportedAt = File.GetLastWriteTimeUtc(filePath),
                    FolderCount = legacyItems.Count(x => x.ActionType == ActionType.Folder),
                    ActionCount = legacyItems.Count(x => x.ActionType != ActionType.Folder),
                    Items = legacyItems
                };
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Invalid or unsupported TriggerPoint backup file format: {ex.Message}", ex);
        }

        throw new InvalidOperationException("The backup file does not contain valid TriggerPoint configuration data.");
    }

    #region Recycle Bin Operations

    public async Task<IReadOnlyList<RecycleBinItem>> LoadRecycleBinAsync()
    {
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (File.Exists(_recycleBinFilePath))
            {
                try
                {
                    using var stream = File.OpenRead(_recycleBinFilePath);
                    var items = await JsonSerializer.DeserializeAsync<List<RecycleBinItem>>(stream, JsonOptions).ConfigureAwait(false);
                    if (items != null)
                    {
                        foreach (var r in items)
                        {
                            if (r.Item != null) DecryptSecrets([r.Item]);
                        }
                        return items;
                    }
                    return [];
                }
                catch
                {
                    return [];
                }
            }
            return [];
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveRecycleBinAsync(IEnumerable<RecycleBinItem> items)
    {
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var list = items.Select(r => new RecycleBinItem
            {
                Id = r.Id,
                DeletedAtUtc = r.DeletedAtUtc,
                OriginalParentId = r.OriginalParentId,
                OriginalPath = r.OriginalPath,
                Item = PrepareItemsForSerialization([r.Item]).FirstOrDefault() ?? r.Item
            }).ToList();

            var tempFile = _recycleBinFilePath + ".tmp";
            using (var stream = File.Create(tempFile))
            {
                await JsonSerializer.SerializeAsync(stream, list, JsonOptions).ConfigureAwait(false);
            }
            File.Move(tempFile, _recycleBinFilePath, overwrite: true);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task MoveToRecycleBinAsync(IEnumerable<TriggerItem> items, IReadOnlyList<TriggerItem> allItems)
    {
        var currentRecycleBin = (await LoadRecycleBinAsync().ConfigureAwait(false)).ToList();
        var folderLookup = allItems.ToDictionary(x => x.Id, x => x);

        foreach (var item in items)
        {
            // Build original folder path for user context
            var pathParts = new List<string>();
            var curParentId = item.ParentId;
            while (curParentId.HasValue && folderLookup.TryGetValue(curParentId.Value, out var parentFolder))
            {
                pathParts.Insert(0, parentFolder.Name);
                curParentId = parentFolder.ParentId;
            }
            string originalPath = pathParts.Count > 0 ? string.Join(" / ", pathParts) : "Root";

            // Remove any existing entry with same id
            currentRecycleBin.RemoveAll(x => x.Item.Id == item.Id);

            currentRecycleBin.Add(new RecycleBinItem
            {
                Id = item.Id,
                DeletedAtUtc = DateTime.UtcNow,
                OriginalParentId = item.ParentId,
                OriginalPath = originalPath,
                Item = item
            });
        }

        await SaveRecycleBinAsync(currentRecycleBin).ConfigureAwait(false);
    }

    public Task MoveToRecycleBinAsync(TriggerItem item, IReadOnlyList<TriggerItem> allItems)
    {
        return MoveToRecycleBinAsync([item], allItems);
    }

    public async Task<IReadOnlyList<TriggerItem>> RestoreFromRecycleBinAsync(IEnumerable<Guid> recycleBinItemIds)
    {
        var currentRecycleBin = (await LoadRecycleBinAsync().ConfigureAwait(false)).ToList();
        var idSet = new HashSet<Guid>(recycleBinItemIds);
        var restored = new List<TriggerItem>();

        currentRecycleBin.RemoveAll(rbItem =>
        {
            if (idSet.Contains(rbItem.Id) || idSet.Contains(rbItem.Item.Id))
            {
                restored.Add(rbItem.Item);
                return true;
            }
            return false;
        });

        await SaveRecycleBinAsync(currentRecycleBin).ConfigureAwait(false);
        return restored;
    }

    public async Task<TriggerItem?> RestoreFromRecycleBinAsync(Guid recycleBinItemId)
    {
        var restored = await RestoreFromRecycleBinAsync([recycleBinItemId]).ConfigureAwait(false);
        return restored.FirstOrDefault();
    }

    public async Task PermanentlyDeleteFromRecycleBinAsync(IEnumerable<Guid> recycleBinItemIds)
    {
        var currentRecycleBin = (await LoadRecycleBinAsync().ConfigureAwait(false)).ToList();
        var idSet = new HashSet<Guid>(recycleBinItemIds);

        currentRecycleBin.RemoveAll(rbItem => idSet.Contains(rbItem.Id) || idSet.Contains(rbItem.Item.Id));
        await SaveRecycleBinAsync(currentRecycleBin).ConfigureAwait(false);
    }

    public Task PermanentlyDeleteFromRecycleBinAsync(Guid recycleBinItemId)
    {
        return PermanentlyDeleteFromRecycleBinAsync([recycleBinItemId]);
    }

    public async Task EmptyRecycleBinAsync()
    {
        await SaveRecycleBinAsync([]).ConfigureAwait(false);
    }

    public async Task PurgeRecycleBinAsync(int retentionDays)
    {
        if (retentionDays <= 0) return; // 0 = Never delete

        var currentRecycleBin = (await LoadRecycleBinAsync().ConfigureAwait(false)).ToList();
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        int initialCount = currentRecycleBin.Count;
        currentRecycleBin.RemoveAll(x => x.DeletedAtUtc < cutoff);

        if (currentRecycleBin.Count != initialCount)
        {
            await SaveRecycleBinAsync(currentRecycleBin).ConfigureAwait(false);
        }
    }

    #endregion
}
