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
    void Suspend();
    void Resume();
    bool IsSnoozed { get; set; }
    IReadOnlyDictionary<Guid, HotkeyConflictStatus> CurrentConflicts { get; }
    event EventHandler<TriggerItem>? HotkeyTriggered;
    event EventHandler? ConflictsUpdated;
    event EventHandler<bool>? SnoozeChanged;
}

public interface IActionExecutor
{
    Task ExecuteAsync(TriggerItem item, ExecutionOverride executionOverride = ExecutionOverride.Standard, IntPtr? targetHwnd = null);
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
    IntPtr LastExternalForegroundHwnd { get; set; }
    IntPtr GetForegroundWindowHandle();
    string? GetForegroundProcessName();
    string? GetActiveBrowserUrl(IntPtr hWnd, string? processName = null);
    bool ShouldExecute(TriggerItem item);
    bool ShouldExecute(TriggerItem item, IReadOnlyList<TriggerItem>? allItems);
    void SetAllItemsProvider(Func<IReadOnlyList<TriggerItem>>? provider);
    List<TriggerItem> GetInheritanceChain(TriggerItem item, IReadOnlyList<TriggerItem> allItems);
}

public interface ITelemetryService
{
    Task RecordExecutionAsync(Guid itemId);
}

public interface IPromptDialogService
{
    Task<Dictionary<string, string>?> ShowPromptDialogAsync(
        IReadOnlyList<PromptToken> promptTokens,
        string? title = null,
        string? subtitle = null);

    Task<Dictionary<string, string>?> ShowPromptDialogAsync(IReadOnlyList<PromptToken> promptTokens)
        => ShowPromptDialogAsync(promptTokens, null, null);
}

public interface IToastNotificationService
{
    void ShowSuccess(string title, string message);
    void ShowError(string title, string message);
    void ShowWarning(string title, string message);
}

public interface IConfirmationDialogService
{
    Task<bool> ShowConfirmationAsync(
        string message, 
        string title = "TriggerPoint Confirmation", 
        string confirmButtonText = "Confirm", 
        string cancelButtonText = "Cancel");
}

public sealed record ScriptExecutionResult(
    bool Success, 
    string? ErrorMessage, 
    IReadOnlyDictionary<string, string> Variables);

public interface IScriptEngineService
{
    Func<string, IntPtr?, Task<bool>>? ActionExecutionHandler { get; set; }

    Task<ScriptExecutionResult> ExecuteAsync(
        string script, 
        IDictionary<string, string>? initialVariables = null, 
        bool isElevated = false,
        IntPtr? targetHwnd = null,
        System.Threading.CancellationToken cancellationToken = default);
}

public interface IWorkflowExecutor
{
    Func<Guid, IntPtr?, Task<bool>>? ActionExecutionHandler { get; set; }

    Task ExecuteWorkflowAsync(
        TriggerItem item, 
        ExecutionOverride executionOverride = ExecutionOverride.Standard, 
        IntPtr? targetHwnd = null, 
        System.Threading.CancellationToken cancellationToken = default);

    Task<bool> ExecuteSingleStepAsync(
        WorkflowStep step, 
        TriggerItem parentItem, 
        IntPtr? targetHwnd = null, 
        System.Threading.CancellationToken cancellationToken = default);
}

public interface IBrowserDetectionService
{
    IReadOnlyList<BrowserInfo> GetInstalledBrowsers();
    IReadOnlyList<BrowserProfileInfo> GetProfiles(string browserId);
    bool LaunchUrl(string url, string? browserId = null, string? profileId = null, bool newWindow = false);
}

public interface IMacroService
{
    bool IsRecording { get; }
    event Action<MacroEvent>? EventCaptured;
    void StartRecording();
    MacroPayload StopRecording();
    Task PlayMacroAsync(MacroPayload macro, double speedMultiplier = 1.0, System.Threading.CancellationToken cancellationToken = default);
}

public interface IWorkflowTemplateService
{
    string TemplatesDirectory { get; }
    IReadOnlyList<WorkflowPreset> GetAllTemplates();
    IReadOnlyList<string> GetCategories();
    WorkflowPreset? GetTemplateById(string id);
    bool SaveTemplate(WorkflowPreset template);
    bool DeleteTemplate(string id);
    void EnsureDefaultTemplates();
}

