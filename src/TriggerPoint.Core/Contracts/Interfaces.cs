using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TriggerPoint.Core.Models;

namespace TriggerPoint.Core.Contracts;

public enum ExecutionOverride
{
    Standard = 0,
    RunAsAdmin = 1,
    RevealInExplorer = 2,
    OpenSettings = 3
}

public interface IConfigRepository
{
    Task<IReadOnlyList<TriggerItem>> LoadAsync();
    Task SaveAsync(IEnumerable<TriggerItem> items);
    Task<AppSettings> LoadSettingsAsync();
    Task SaveSettingsAsync(AppSettings settings);
    Task ExportPackageAsync(string filePath, ConfigurationBackupPackage package);
    Task<ConfigurationBackupPackage> ReadPackageAsync(string filePath);
    Task<IReadOnlyList<RecycleBinItem>> LoadRecycleBinAsync();
    Task SaveRecycleBinAsync(IEnumerable<RecycleBinItem> items);
    Task MoveToRecycleBinAsync(IEnumerable<TriggerItem> items, IReadOnlyList<TriggerItem> allItems);
    Task MoveToRecycleBinAsync(TriggerItem item, IReadOnlyList<TriggerItem> allItems);
    Task<IReadOnlyList<TriggerItem>> RestoreFromRecycleBinAsync(IEnumerable<Guid> recycleBinItemIds);
    Task<TriggerItem?> RestoreFromRecycleBinAsync(Guid recycleBinItemId);
    Task PermanentlyDeleteFromRecycleBinAsync(IEnumerable<Guid> recycleBinItemIds);
    Task PermanentlyDeleteFromRecycleBinAsync(Guid recycleBinItemId);
    Task EmptyRecycleBinAsync();
    Task PurgeRecycleBinAsync(int retentionDays);
    string ConfigFilePath { get; }
    string BackupFilePath { get; }
    string AppSettingsFilePath { get; }
    string RecycleBinFilePath { get; }
}

public interface IShortcutListener : IDisposable
{
    void Start(IntPtr windowHandle);
    void Stop();
    void RegisterAll(IEnumerable<TriggerItem> items);
    bool IsSnoozed { get; set; }
    IReadOnlyDictionary<Guid, HotkeyConflictStatus> CurrentConflicts { get; }
    event EventHandler<TriggerItem>? HotkeyTriggered;
    event EventHandler? ConflictsUpdated;
}

public interface IActionExecutor
{
    Task ExecuteAsync(TriggerItem item, ExecutionOverride executionOverride = ExecutionOverride.Standard);
}

public interface ISnippetService
{
    Task InjectSnippetAsync(string template, IntPtr targetHwnd);
}

public interface IIconService
{
    Task<string?> ResolveIconPathAsync(string? iconPath, string? fallbackCommand);
}

public interface IContextFilterService
{
    IntPtr GetForegroundWindowHandle();
    string? GetForegroundProcessName();
    string? GetActiveBrowserUrl(IntPtr hWnd, string? processName = null);
    bool ShouldExecute(TriggerItem item);
}

public interface ITelemetryService
{
    Task RecordExecutionAsync(Guid itemId);
}

public interface IPromptDialogService
{
    Task<Dictionary<string, string>?> ShowPromptDialogAsync(IReadOnlyList<PromptToken> promptTokens);
}

public interface IToastNotificationService
{
    void ShowSuccess(string title, string message);
    void ShowError(string title, string message);
    void ShowWarning(string title, string message);
}
