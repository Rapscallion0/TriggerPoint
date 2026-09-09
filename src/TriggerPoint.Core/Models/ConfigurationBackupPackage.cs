using System;
using System.Collections.Generic;

namespace TriggerPoint.Core.Models;

public enum BackupContentType
{
    FullBackup,
    TreeItems,
    AppSettings
}

public class ConfigurationBackupPackage
{
    public int SchemaVersion { get; set; } = 1;
    public string AppVersion { get; set; } = "2.0.0";
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
    public BackupContentType ContentType { get; set; } = BackupContentType.FullBackup;
    public string? ScopeName { get; set; }
    public int FolderCount { get; set; }
    public int ActionCount { get; set; }
    public AppSettings? Settings { get; set; }
    public List<TriggerItem> Items { get; set; } = [];

    public static ConfigurationBackupPackage CreateFullBackup(IEnumerable<TriggerItem> items, AppSettings settings)
    {
        var itemList = new List<TriggerItem>(items);
        return new ConfigurationBackupPackage
        {
            ContentType = BackupContentType.FullBackup,
            ScopeName = "Full Configuration",
            FolderCount = itemList.Count(x => x.ActionType == ActionType.Folder),
            ActionCount = itemList.Count(x => x.ActionType != ActionType.Folder),
            Settings = settings,
            Items = itemList
        };
    }

    public static ConfigurationBackupPackage CreateTreeItems(IEnumerable<TriggerItem> items, string scopeName)
    {
        var itemList = new List<TriggerItem>(items);
        return new ConfigurationBackupPackage
        {
            ContentType = BackupContentType.TreeItems,
            ScopeName = scopeName,
            FolderCount = itemList.Count(x => x.ActionType == ActionType.Folder),
            ActionCount = itemList.Count(x => x.ActionType != ActionType.Folder),
            Items = itemList
        };
    }

    public static ConfigurationBackupPackage CreateAppSettings(AppSettings settings)
    {
        return new ConfigurationBackupPackage
        {
            ContentType = BackupContentType.AppSettings,
            ScopeName = "Application Settings",
            FolderCount = 0,
            ActionCount = 0,
            Settings = settings,
            Items = []
        };
    }
}

