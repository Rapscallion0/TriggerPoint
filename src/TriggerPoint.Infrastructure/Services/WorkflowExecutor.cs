using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;

namespace TriggerPoint.Infrastructure.Services;

public class WorkflowExecutor : IWorkflowExecutor
{
    private readonly ILogger _logger = Log.ForContext<WorkflowExecutor>();
    private readonly IScriptEngineService _scriptEngineService;
    private readonly IPromptDialogService _promptDialogService;
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly IToastNotificationService _toastNotificationService;
    private readonly ISnippetService _snippetService;
    private readonly ITelemetryService _telemetryService;
    private readonly IContextFilterService _contextFilterService;
    private readonly IBrowserDetectionService? _browserDetectionService;

    private static readonly AsyncLocal<HashSet<Guid>> _callStack = new();

    public event Action<TriggerItem, string>? WorkflowSucceeded;
    public event Action<TriggerItem, string>? WorkflowFailed;

    public Func<Guid, IntPtr?, Task<bool>>? ActionExecutionHandler { get; set; }

    public WorkflowExecutor(
        IScriptEngineService scriptEngineService,
        IPromptDialogService promptDialogService,
        IConfirmationDialogService confirmationDialogService,
        IToastNotificationService toastNotificationService,
        ISnippetService snippetService,
        ITelemetryService telemetryService,
        IContextFilterService contextFilterService,
        IBrowserDetectionService? browserDetectionService = null)
    {
        _scriptEngineService = scriptEngineService;
        _promptDialogService = promptDialogService;
        _confirmationDialogService = confirmationDialogService;
        _toastNotificationService = toastNotificationService;
        _snippetService = snippetService;
        _telemetryService = telemetryService;
        _contextFilterService = contextFilterService;
        _browserDetectionService = browserDetectionService;
    }

    public async Task ExecuteWorkflowAsync(
        TriggerItem item,
        ExecutionOverride executionOverride = ExecutionOverride.Standard,
        IntPtr? targetHwnd = null,
        CancellationToken cancellationToken = default)
    {
        if (item == null) return;

        // Context filtering
        if (!_contextFilterService.ShouldExecute(item))
        {
            _logger.Information("Workflow '{Name}' bypassed due to context filter.", item.Name);
            return;
        }

        var callStack = _callStack.Value ??= [];
        if (callStack.Contains(item.Id))
        {
            var loopMsg = $"Loop detected: Workflow '{item.Name}' is already executing in this chain.";
            _logger.Error(loopMsg);
            WorkflowFailed?.Invoke(item, loopMsg);
            _toastNotificationService.ShowError("Recursion Loop Error", loopMsg);
            return;
        }

        callStack.Add(item.Id);
        try
        {
            await ExecuteWorkflowCoreAsync(item, executionOverride, targetHwnd, cancellationToken);
        }
        finally
        {
            callStack.Remove(item.Id);
        }
    }

    private async Task ExecuteWorkflowCoreAsync(
        TriggerItem item,
        ExecutionOverride executionOverride,
        IntPtr? targetHwnd,
        CancellationToken cancellationToken)
    {
        _ = _telemetryService.RecordExecutionAsync(item.Id);

        var isElevated = executionOverride == ExecutionOverride.RunAsAdmin || item.Payload.RunAsAdmin;
        var effectiveHwnd = targetHwnd ?? IntPtr.Zero;
        if (effectiveHwnd == IntPtr.Zero)
        {
            effectiveHwnd = _contextFilterService.LastExternalForegroundHwnd;
        }
        if (effectiveHwnd == IntPtr.Zero)
        {
            effectiveHwnd = _contextFilterService.GetForegroundWindowHandle();
        }

        // Script Mode
        if (item.Payload.WorkflowMode == WorkflowMode.Script)
        {
            _logger.Information("Executing workflow '{Name}' in Script Mode", item.Name);
            var scriptResult = await _scriptEngineService.ExecuteAsync(
                item.Payload.ScriptSource,
                initialVariables: null,
                isElevated: isElevated,
                targetHwnd: effectiveHwnd,
                cancellationToken: cancellationToken);

            if (scriptResult.Success)
            {
                WorkflowSucceeded?.Invoke(item, "Script workflow completed successfully.");
            }
            else
            {
                var err = scriptResult.ErrorMessage ?? "Script execution error";
                _logger.Warning("Script workflow '{Name}' failed: {Error}", item.Name, err);
                WorkflowFailed?.Invoke(item, err);
                _toastNotificationService.ShowError("Script Error", $"Workflow '{item.Name}' encountered an error:\n{err}");
            }
            return;
        }

        // Visual Mode
        _logger.Information("Executing workflow '{Name}' in Visual Mode with {Count} steps", item.Name, item.Payload.WorkflowSteps.Count);
        var contextVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        int stepNum = 0;
        foreach (var step in item.Payload.WorkflowSteps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            stepNum++;

            if (!step.IsEnabled)
            {
                _logger.Debug("Skipping disabled step {Num} in workflow '{Name}'", stepNum, item.Name);
                continue;
            }

            try
            {
                var stepSuccess = await ExecuteStepAsync(step, contextVariables, isElevated, effectiveHwnd, cancellationToken);
                if (!stepSuccess)
                {
                    if (step.OnError == StepErrorPolicy.StopWorkflow)
                    {
                        _logger.Information("Workflow '{Name}' stopped at step {Num} ('{StepName}') per OnError policy.", item.Name, stepNum, step.Name);
                        return;
                    }
                    _logger.Information("Step {Num} failed/cancelled but continuing per OnError policy.", stepNum);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception executing step {Num} ('{StepName}') in workflow '{Name}'", stepNum, step.Name, item.Name);
                if (step.OnError == StepErrorPolicy.StopWorkflow)
                {
                    WorkflowFailed?.Invoke(item, $"Step {stepNum} failed: {ex.Message}");
                    _toastNotificationService.ShowError("Workflow Error", $"Step '{step.Name}' failed:\n{ex.Message}");
                    return;
                }
            }
        }

        WorkflowSucceeded?.Invoke(item, $"Workflow '{item.Name}' completed.");
    }

    private async Task<bool> ExecuteStepAsync(
        WorkflowStep step,
        Dictionary<string, string> contextVariables,
        bool isElevated,
        IntPtr effectiveHwnd,
        CancellationToken cancellationToken)
    {
        switch (step.StepType)
        {
            case WorkflowStepType.Prompt:
            {
                var tokens = new List<PromptToken>();
                if (step.PromptFields != null && step.PromptFields.Count > 0)
                {
                    foreach (var f in step.PromptFields)
                    {
                        var varName = string.IsNullOrWhiteSpace(f.VariableName) ? "input" : f.VariableName.Trim();
                        var label = string.IsNullOrWhiteSpace(f.Label) ? "Enter value" : f.Label;
                        var token = new PromptToken
                        {
                            RawTag = varName,
                            Label = label,
                            DefaultValue = ResolveVariables(f.DefaultValue, contextVariables),
                            Type = f.Type,
                            MinNumber = f.MinNumber,
                            MaxNumber = f.MaxNumber,
                            DateFormat = string.IsNullOrWhiteSpace(f.DateFormat) ? "yyyy-MM-dd" : f.DateFormat
                        };

                        if (f.Type == TokenType.PromptChoice && !string.IsNullOrWhiteSpace(f.Choices))
                        {
                            var parts = f.Choices.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            foreach (var p in parts)
                            {
                                token.Choices.Add(new ChoiceOption(p, p));
                            }
                        }
                        tokens.Add(token);
                    }
                }
                else
                {
                    var label = string.IsNullOrWhiteSpace(step.PromptLabel) ? "Enter value" : step.PromptLabel;
                    var varName = string.IsNullOrWhiteSpace(step.VariableName) ? "input" : step.VariableName.Trim();

                    var token = new PromptToken
                    {
                        RawTag = varName,
                        Label = label,
                        DefaultValue = ResolveVariables(step.PromptDefaultValue, contextVariables),
                        Type = step.PromptType,
                        MinNumber = step.PromptMinNumber,
                        MaxNumber = step.PromptMaxNumber,
                        DateFormat = string.IsNullOrWhiteSpace(step.PromptDateFormat) ? "yyyy-MM-dd" : step.PromptDateFormat
                    };

                    if (step.PromptType == TokenType.PromptChoice && !string.IsNullOrWhiteSpace(step.PromptChoices))
                    {
                        var parts = step.PromptChoices.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        foreach (var p in parts)
                        {
                            token.Choices.Add(new ChoiceOption(p, p));
                        }
                    }
                    tokens.Add(token);
                }

                var title = string.IsNullOrWhiteSpace(step.PromptTitle) ? null : ResolveVariables(step.PromptTitle, contextVariables);
                var subtitle = string.IsNullOrWhiteSpace(step.PromptSubtitle) ? null : ResolveVariables(step.PromptSubtitle, contextVariables);

                var response = await _promptDialogService.ShowPromptDialogAsync(tokens, title, subtitle);
                if (response == null)
                {
                    // User cancelled
                    return false;
                }

                foreach (var (k, v) in response)
                {
                    contextVariables[k] = v;
                }
                return true;
            }

            case WorkflowStepType.OpenUrl:
            {
                var url = ResolveVariables(step.Url, contextVariables);
                if (string.IsNullOrWhiteSpace(url)) return false;

                var expanded = Environment.ExpandEnvironmentVariables(url.Trim());
                _logger.Information("Opening URL: {Url} (Browser: {Browser}, Profile: {Profile}, NewWindow: {NewWindow})", expanded, step.BrowserTarget, step.BrowserProfile, step.OpenInNewWindow);
                if (_browserDetectionService != null)
                {
                    return _browserDetectionService.LaunchUrl(expanded, step.BrowserTarget, step.BrowserProfile, step.OpenInNewWindow);
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = expanded,
                    UseShellExecute = true
                });
                return true;
            }

            case WorkflowStepType.EnsureDirectory:
            {
                var rawPath = ResolveVariables(step.DirectoryPath, contextVariables);
                if (string.IsNullOrWhiteSpace(rawPath)) return false;

                var path = Environment.ExpandEnvironmentVariables(rawPath.Trim());

                if (!Directory.Exists(path))
                {
                    if (step.DirectoryMissingPolicy == DirectoryMissingPolicy.PromptToCreate)
                    {
                        var confirm = await _confirmationDialogService.ShowConfirmationAsync(
                            $"Folder \"{path}\" does not exist.\nWould you like to create it?",
                            "Folder Not Found",
                            "Create Folder",
                            "Cancel");

                        if (!confirm)
                        {
                            return false; // User declined
                        }
                        Directory.CreateDirectory(path);
                    }
                    else if (step.DirectoryMissingPolicy == DirectoryMissingPolicy.CreateSilently)
                    {
                        Directory.CreateDirectory(path);
                    }
                    else if (step.DirectoryMissingPolicy == DirectoryMissingPolicy.Fail)
                    {
                        _toastNotificationService.ShowWarning("Directory Missing", $"Directory '{path}' does not exist.");
                        return false;
                    }
                }

                if (step.OpenInExplorer)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{path}\"",
                        UseShellExecute = true
                    });
                }
                return true;
            }

            case WorkflowStepType.LaunchApp:
            {
                var rawCmd = ResolveVariables(step.Command, contextVariables);
                if (string.IsNullOrWhiteSpace(rawCmd)) return false;

                var cmd = Environment.ExpandEnvironmentVariables(rawCmd.Trim());
                var psi = new ProcessStartInfo
                {
                    FileName = cmd,
                    UseShellExecute = true
                };

                if (!string.IsNullOrWhiteSpace(step.Arguments))
                {
                    var rawArgs = ResolveVariables(step.Arguments, contextVariables);
                    psi.Arguments = Environment.ExpandEnvironmentVariables(rawArgs.Trim());
                }

                if (!string.IsNullOrWhiteSpace(step.WorkingDirectory))
                {
                    var rawDir = ResolveVariables(step.WorkingDirectory, contextVariables);
                    psi.WorkingDirectory = Environment.ExpandEnvironmentVariables(rawDir.Trim());
                }

                if (isElevated || step.RunAsAdmin)
                {
                    psi.Verb = "runas";
                }

                _logger.Information("Launching workflow process: {Cmd} {Args}", psi.FileName, psi.Arguments);
                var proc = Process.Start(psi);
                if (proc != null && !string.IsNullOrWhiteSpace(step.TargetDisplay) && !step.TargetDisplay.Equals("default", StringComparison.OrdinalIgnoreCase))
                {
                    var targetDisp = step.TargetDisplay;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            for (int i = 0; i < 25; i++)
                            {
                                await Task.Delay(100);
                                proc.Refresh();
                                if (proc.HasExited) break;
                                if (proc.MainWindowHandle != IntPtr.Zero)
                                {
                                    Win32.NativeMethods.MoveWindowToTargetDisplay(proc.MainWindowHandle, targetDisp);
                                    break;
                                }
                            }
                        }
                        catch { }
                    });
                }
                return true;
            }

            case WorkflowStepType.InjectSnippet:
            {
                var template = ResolveVariables(step.SnippetTemplate, contextVariables);
                if (string.IsNullOrEmpty(template)) return false;

                await _snippetService.InjectSnippetAsync(template, effectiveHwnd);
                return true;
            }

            case WorkflowStepType.Delay:
            {
                var ms = Math.Max(10, step.DelayMs);
                await Task.Delay(ms, cancellationToken);
                return true;
            }

            case WorkflowStepType.RunScript:
            {
                if (string.IsNullOrWhiteSpace(step.InlineScript)) return true;

                var res = await _scriptEngineService.ExecuteAsync(
                    step.InlineScript,
                    contextVariables,
                    isElevated,
                    effectiveHwnd,
                    cancellationToken);

                if (res.Success)
                {
                    foreach (var kvp in res.Variables)
                    {
                        contextVariables[kvp.Key] = kvp.Value;
                    }
                    return true;
                }
                return false;
            }

            case WorkflowStepType.ExecuteAction:
            {
                if (!step.TargetItemId.HasValue || step.TargetItemId.Value == Guid.Empty)
                {
                    _logger.Warning("ExecuteAction step has no target action configured.");
                    return false;
                }

                var targetId = step.TargetItemId.Value;
                var stack = _callStack.Value ??= [];
                if (stack.Contains(targetId))
                {
                    var loopErr = $"Recursion loop detected: Target action '{targetId}' is already executing in the active call chain.";
                    _logger.Error(loopErr);
                    throw new InvalidOperationException(loopErr);
                }

                if (ActionExecutionHandler != null)
                {
                    stack.Add(targetId);
                    try
                    {
                        return await ActionExecutionHandler(targetId, effectiveHwnd);
                    }
                    finally
                    {
                        stack.Remove(targetId);
                    }
                }

                _logger.Warning("No ActionExecutionHandler configured on WorkflowExecutor.");
                return false;
            }

            default:
                return true;
        }
    }

    public async Task<bool> ExecuteSingleStepAsync(
        WorkflowStep step, 
        TriggerItem parentItem, 
        IntPtr? targetHwnd = null, 
        CancellationToken cancellationToken = default)
    {
        if (step == null) return false;

        var contextVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // If step references placeholders (e.g. {ticket}), extract and prompt for test values if prompt dialog service is available
        var textToCheck = $"{step.Url} {step.Command} {step.Arguments} {step.WorkingDirectory} {step.DirectoryPath} {step.SnippetTemplate}";
        var matches = System.Text.RegularExpressions.Regex.Matches(textToCheck, @"\{([a-zA-Z0-9_]+)\}");
        var neededTokens = new List<PromptToken>();
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var key = match.Groups[1].Value;
            if (!contextVariables.ContainsKey(key) && !neededTokens.Any(t => t.RawTag.Equals(key, StringComparison.OrdinalIgnoreCase)))
            {
                neededTokens.Add(new PromptToken
                {
                    Type = TokenType.PromptText,
                    RawTag = key,
                    Label = $"Test value for '{key}'",
                    DefaultValue = string.Empty
                });
            }
        }

        if (neededTokens.Count > 0 && _promptDialogService != null)
        {
            var answers = await _promptDialogService.ShowPromptDialogAsync(neededTokens);
            if (answers == null)
            {
                return false; // User cancelled prompt
            }
            foreach (var kvp in answers)
            {
                contextVariables[kvp.Key] = kvp.Value;
            }
        }

        var effectiveHwnd = targetHwnd ?? _contextFilterService.LastExternalForegroundHwnd;
        return await ExecuteStepAsync(step, contextVariables, parentItem.Payload.RunAsAdmin, effectiveHwnd, cancellationToken);
    }

    public static string ResolveVariables(string? template, IReadOnlyDictionary<string, string> variables)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;

        // Uses PlaceholderParser to resolve static tokens ({date}, {clipboard}, {env:...}) as well as user variables
        return PlaceholderParser.EvaluateAsync(
            template,
            clipboardProvider: null,
            promptResponses: variables).GetAwaiter().GetResult();
    }
}
