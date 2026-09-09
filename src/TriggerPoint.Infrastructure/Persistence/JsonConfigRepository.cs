using System;
using System.Collections.Generic;
using System.IO;
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
    private readonly string _appSettingsFilePath;
    private readonly string _appSettingsBackupFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonConfigRepository(string? customDirectory = null)
    {
        var baseDir = customDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            "TriggerPoint");

        Directory.CreateDirectory(baseDir);
        _configFilePath = Path.Combine(baseDir, "triggerpoint.json");
        _backupFilePath = Path.Combine(baseDir, "triggerpoint.bak");
        _appSettingsFilePath = Path.Combine(baseDir, "appsettings.json");
        _appSettingsBackupFilePath = Path.Combine(baseDir, "appsettings.bak");
    }

    public string ConfigFilePath => _configFilePath;
    public string BackupFilePath => _backupFilePath;
    public string AppSettingsFilePath => _appSettingsFilePath;

    public async Task<IReadOnlyList<TriggerItem>> LoadAsync()
    {
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (File.Exists(_configFilePath))
            {
                try
                {
                    using var stream = File.OpenRead(_configFilePath);
                    var items = await JsonSerializer.DeserializeAsync<List<TriggerItem>>(stream, JsonOptions).ConfigureAwait(false);
                    if (items != null)
                    {
                        return items;
                    }
                }
                catch (Exception)
                {
                    // Primary file corrupted, try backup recovery
                    if (File.Exists(_backupFilePath))
                    {
                        try
                        {
                            using var backupStream = File.OpenRead(_backupFilePath);
                            var backupItems = await JsonSerializer.DeserializeAsync<List<TriggerItem>>(backupStream, JsonOptions).ConfigureAwait(false);
                            if (backupItems != null)
                            {
                                // Restore primary file from backup
                                File.Copy(_backupFilePath, _configFilePath, true);
                                return backupItems;
                            }
                        }
                        catch
                        {
                            // Backup also failed; proceed to defaults
                        }
                    }
                }
            }

            // If file doesn't exist or failed to load, generate defaults
            var defaults = CreateDefaultItems();
            await SaveInternalAsync(defaults).ConfigureAwait(false);
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
            await SaveInternalAsync(items).ConfigureAwait(false);
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
        }

        if (File.Exists(_appSettingsFilePath))
        {
            File.Copy(_appSettingsFilePath, _appSettingsBackupFilePath, true);
        }

        File.Move(tempFilePath, _appSettingsFilePath, true);
    }

    private async Task SaveInternalAsync(IEnumerable<TriggerItem> items)
    {
        var itemList = new List<TriggerItem>(items);
        var tempFilePath = _configFilePath + ".tmp";

        // 1. Write to temporary file
        await using (var stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
        {
            await JsonSerializer.SerializeAsync(stream, itemList, JsonOptions).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        // 2. Rotate rolling backup if primary file exists
        if (File.Exists(_configFilePath))
        {
            File.Copy(_configFilePath, _backupFilePath, true);
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
}
