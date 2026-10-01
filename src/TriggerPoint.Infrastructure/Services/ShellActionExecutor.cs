using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Serilog;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;

namespace TriggerPoint.Infrastructure.Services;

public class ShellActionExecutor : IActionExecutor
{
    private readonly ILogger _logger = Log.ForContext<ShellActionExecutor>();
    private readonly ISnippetService _snippetService;
    private readonly ITelemetryService _telemetryService;
    private readonly IContextFilterService _contextFilterService;
    private readonly IPromptDialogService? _promptDialogService;
    private readonly IWorkflowExecutor? _workflowExecutor;
    private readonly IMacroService? _macroService;
    private readonly IWindowsServiceManager? _windowsServiceManager;
    private readonly IToastNotificationService? _toastNotificationService;

    public event Action<TriggerItem>? OpenSettingsRequested;
    public event Action<TriggerItem, string>? ExecutionSucceeded;
    public event Action<TriggerItem, string>? ExecutionFailed;

    public ShellActionExecutor(
        ISnippetService snippetService,
        ITelemetryService telemetryService,
        IContextFilterService contextFilterService,
        IPromptDialogService? promptDialogService = null,
        IWorkflowExecutor? workflowExecutor = null,
        IMacroService? macroService = null,
        IWindowsServiceManager? windowsServiceManager = null,
        IToastNotificationService? toastNotificationService = null)
    {
        _snippetService = snippetService;
        _telemetryService = telemetryService;
        _contextFilterService = contextFilterService;
        _promptDialogService = promptDialogService;
        _workflowExecutor = workflowExecutor;
        _macroService = macroService;
        _windowsServiceManager = windowsServiceManager;
        _toastNotificationService = toastNotificationService;
    }

    public async Task ExecuteAsync(TriggerItem item, ExecutionOverride executionOverride = ExecutionOverride.Standard, IntPtr? targetHwnd = null)
    {
        if (item == null) return;

        // Context filtering check
        if (!_contextFilterService.ShouldExecute(item))
        {
            _logger.Information("Action '{Name}' bypassed due to context process filter.", item.Name);
            return;
        }

        // Open in Settings override
        if (executionOverride == ExecutionOverride.OpenSettings)
        {
            OpenSettingsRequested?.Invoke(item);
            return;
        }

        // Record telemetry
        _ = _telemetryService.RecordExecutionAsync(item.Id);

        if (item.ActionType == ActionType.Workflow)
        {
            if (_workflowExecutor != null)
            {
                await _workflowExecutor.ExecuteWorkflowAsync(item, executionOverride, targetHwnd);
            }
            else
            {
                _logger.Warning("Workflow executor is not configured for action '{Name}'", item.Name);
                ExecutionFailed?.Invoke(item, "Workflow engine service not available.");
            }
            return;
        }

        if (item.ActionType == ActionType.Snippet)
        {
            try
            {
                var effectiveHwnd = targetHwnd ?? IntPtr.Zero;
                if (effectiveHwnd == IntPtr.Zero)
                {
                    effectiveHwnd = _contextFilterService.LastExternalForegroundHwnd;
                }
                if (effectiveHwnd == IntPtr.Zero)
                {
                    effectiveHwnd = _contextFilterService.GetForegroundWindowHandle();
                }

                _logger.Information("Executing snippet '{Name}' (Type: {Type}) for target window handle {Hwnd}", item.Name, item.Payload.SnippetContentType, effectiveHwnd);
                await _snippetService.InjectSnippetAsync(
                    item.Payload.SnippetTemplate, 
                    effectiveHwnd, 
                    item.Payload.SnippetContentType, 
                    item.Payload.SnippetRtf).ConfigureAwait(false);
                ExecutionSucceeded?.Invoke(item, "Snippet injected into active window.");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to inject snippet for action '{Name}'", item.Name);
                ExecutionFailed?.Invoke(item, $"Failed to inject snippet: {ex.Message}");
            }
            return;
        }

        if (item.ActionType == ActionType.Macro)
        {
            try
            {
                if (_macroService != null && item.Payload.Macro != null && item.Payload.Macro.Events.Count > 0)
                {
                    _logger.Information("Executing macro '{Name}' ({Count} events)", item.Name, item.Payload.Macro.Events.Count);
                    await _macroService.PlayMacroAsync(item.Payload.Macro).ConfigureAwait(false);
                    ExecutionSucceeded?.Invoke(item, "Macro executed successfully.");
                }
                else
                {
                    ExecutionFailed?.Invoke(item, "Macro service not available or macro contains no events.");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to execute macro '{Name}'", item.Name);
                ExecutionFailed?.Invoke(item, $"Macro execution failed: {ex.Message}");
            }
            return;
        }

        if (item.ActionType == ActionType.Shell)
        {
            await ExecuteShellActionAsync(item, executionOverride);
            return;
        }

        if (item.ActionType == ActionType.Service)
        {
            await ExecuteServiceActionAsync(item, executionOverride);
            return;
        }
    }

    private async Task ExecuteServiceActionAsync(TriggerItem item, ExecutionOverride executionOverride)
    {
        var rawServiceName = item.Payload.ServiceName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawServiceName))
        {
            _logger.Warning("Cannot execute service action '{Name}': service name is empty.", item.Name);
            ExecutionFailed?.Invoke(item, "Windows Service name is empty.");
            _toastNotificationService?.ShowError("Service Error", $"Cannot execute '{item.Name}': Service name is not configured.");
            return;
        }

        if (_windowsServiceManager == null)
        {
            _logger.Warning("Windows service manager is not configured for action '{Name}'", item.Name);
            ExecutionFailed?.Invoke(item, "Windows service manager service not available.");
            _toastNotificationService?.ShowError("Service Error", "Windows Service Manager is not available on this system.");
            return;
        }

        var serviceName = await Core.Services.PlaceholderParser.EvaluateAsync(rawServiceName).ConfigureAwait(false);
        var operation = item.Payload.ServiceOperation;
        int timeoutSeconds = item.Payload.ServiceTimeoutSeconds > 0 ? item.Payload.ServiceTimeoutSeconds : 30;
        bool runAsAdmin = item.Payload.ServiceRunAsAdmin;

        string opVerb = operation switch
        {
            ServiceOperation.Start => "Starting",
            ServiceOperation.Stop => "Stopping",
            ServiceOperation.Restart => "Restarting",
            _ => "Toggling"
        };

        using var cts = new CancellationTokenSource();

        using var progress = _toastNotificationService?.ShowProgress(
            "Windows Service",
            $"⏳ {opVerb} service '{serviceName}'...",
            onCancel: () => cts.Cancel(),
            cancelButtonText: "Cancel");

        try
        {
            ServiceOperationResult result;
            switch (operation)
            {
                case ServiceOperation.Start:
                    result = await _windowsServiceManager.StartServiceAsync(serviceName, runAsAdmin, timeoutSeconds, cts.Token);
                    break;
                case ServiceOperation.Stop:
                    result = await _windowsServiceManager.StopServiceAsync(serviceName, runAsAdmin, timeoutSeconds, cts.Token);
                    break;
                case ServiceOperation.Restart:
                    result = await _windowsServiceManager.RestartServiceAsync(serviceName, runAsAdmin, timeoutSeconds, cts.Token);
                    break;
                case ServiceOperation.Toggle:
                default:
                    result = await _windowsServiceManager.ToggleServiceAsync(serviceName, runAsAdmin, timeoutSeconds, cts.Token);
                    break;
            }

            if (result.Success)
            {
                _logger.Information("Service action '{Name}' completed: {Message}", item.Name, result.Message);
                progress?.ReportSuccess(result.Message);
                ExecutionSucceeded?.Invoke(item, result.Message);
            }
            else
            {
                _logger.Warning("Service action '{Name}' failed: {Message}", item.Name, result.Message);
                progress?.ReportError(result.Message);
                ExecutionFailed?.Invoke(item, result.Message);
            }
        }
        catch (OperationCanceledException)
        {
            progress?.ReportError($"Operation cancelled for service '{serviceName}'.");
            _logger.Information("Service action '{Name}' was cancelled by user.", item.Name);
            ExecutionFailed?.Invoke(item, $"Operation cancelled by user for service '{serviceName}'.");
        }
        catch (Exception ex)
        {
            progress?.ReportError($"Error: {ex.Message}");
            _logger.Error(ex, "Unexpected error executing service action '{Name}'", item.Name);
            ExecutionFailed?.Invoke(item, ex.Message);
        }
    }

    private async Task ExecuteShellActionAsync(TriggerItem item, ExecutionOverride executionOverride)
    {
        var rawCommand = item.Payload.Command?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawCommand))
        {
            _logger.Warning("Cannot execute shell action '{Name}': command is empty.", item.Name);
            ExecutionFailed?.Invoke(item, "Application / Command path is empty.");
            return;
        }

        var rawArguments = item.Payload.Arguments ?? string.Empty;
        var rawWorkingDir = item.Payload.WorkingDirectory ?? string.Empty;

        // Universal Prompt Token evaluation across Command, Arguments, and WorkingDirectory
        var combinedText = $"{rawCommand} {rawArguments} {rawWorkingDir}";
        var promptTokens = Core.Services.PlaceholderParser.ExtractPromptTokens(combinedText);
        Dictionary<string, string>? promptResponses = null;

        if (promptTokens.Count > 0 && _promptDialogService != null)
        {
            promptResponses = await _promptDialogService.ShowPromptDialogAsync(promptTokens).ConfigureAwait(true);
            if (promptResponses == null)
            {
                _logger.Information("Shell action '{Name}' cancelled by user during prompt dialog.", item.Name);
                return;
            }
        }

        var command = await Core.Services.PlaceholderParser.EvaluateAsync(rawCommand, promptResponses: promptResponses).ConfigureAwait(false);
        var arguments = await Core.Services.PlaceholderParser.EvaluateAsync(rawArguments, promptResponses: promptResponses).ConfigureAwait(false);
        var workingDirectory = await Core.Services.PlaceholderParser.EvaluateAsync(rawWorkingDir, promptResponses: promptResponses).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(command))
        {
            _logger.Warning("Cannot execute shell action '{Name}': command is empty.", item.Name);
            ExecutionFailed?.Invoke(item, "Application / Command path is empty.");
            return;
        }

        // Reveal in Windows Explorer
        if (executionOverride == ExecutionOverride.RevealInExplorer)
        {
            try
            {
                var expandedPath = Environment.ExpandEnvironmentVariables(command);
                if (!File.Exists(expandedPath) && !Directory.Exists(expandedPath) && !Path.IsPathRooted(expandedPath))
                {
                    var appRelative = Path.Combine(AppContext.BaseDirectory, expandedPath);
                    if (File.Exists(appRelative) || Directory.Exists(appRelative))
                    {
                        expandedPath = appRelative;
                    }
                }

                if (!File.Exists(expandedPath) && !Directory.Exists(expandedPath))
                {
                    var resolvedFromPath = ResolveExecutableFromPath(expandedPath);
                    if (!string.IsNullOrEmpty(resolvedFromPath))
                    {
                        expandedPath = resolvedFromPath;
                    }
                }

                if (File.Exists(expandedPath) || Directory.Exists(expandedPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{expandedPath}\"",
                        UseShellExecute = true
                    });
                    ExecutionSucceeded?.Invoke(item, $"Revealed in Explorer: {Path.GetFileName(expandedPath)}");
                    return;
                }
                else
                {
                    _logger.Warning("Reveal in Explorer failed: '{Command}' does not target an existing file or directory.", command);
                    ExecutionFailed?.Invoke(item, $"Cannot reveal: '{command}' is not a local file or directory.");
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to reveal in explorer: {Command}", command);
                ExecutionFailed?.Invoke(item, $"Failed to reveal in explorer: {ex.Message}");
                return;
            }
        }

        var expandedCommand = Environment.ExpandEnvironmentVariables(command);
        if (!Path.IsPathRooted(expandedCommand))
        {
            var appRelative = Path.Combine(AppContext.BaseDirectory, expandedCommand);
            if (File.Exists(appRelative) || Directory.Exists(appRelative))
            {
                expandedCommand = appRelative;
            }
        }

        if (!Core.Services.ProtocolValidator.IsSafeUrl(expandedCommand, out var rejectReason))
        {
            _logger.Warning("Blocked unsafe shell command/URL '{Command}': {Reason}", expandedCommand, rejectReason);
            ExecutionFailed?.Invoke(item, rejectReason ?? "Execution blocked by security policy.");
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = expandedCommand,
                UseShellExecute = true
            };

        // Arguments
        if (!string.IsNullOrWhiteSpace(arguments))
        {
            psi.Arguments = Environment.ExpandEnvironmentVariables(arguments);
        }

        // Working directory
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            var expandedWorkDir = Environment.ExpandEnvironmentVariables(workingDirectory);
            if (!Path.IsPathRooted(expandedWorkDir))
            {
                var appRelativeDir = Path.Combine(AppContext.BaseDirectory, expandedWorkDir);
                if (Directory.Exists(appRelativeDir))
                {
                    expandedWorkDir = appRelativeDir;
                }
            }
            psi.WorkingDirectory = expandedWorkDir;
        }
            else
            {
                // Fallback: parent directory if command is a file path
                try
                {
                    var fullPath = Path.GetFullPath(psi.FileName);
                    var parent = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
                    {
                        psi.WorkingDirectory = parent;
                    }
                }
                catch { }
            }

            // Run as Administrator
            if (item.Payload.RunAsAdmin || executionOverride == ExecutionOverride.RunAsAdmin)
            {
                psi.Verb = "runas";
            }

            _logger.Information("Launching process '{FileName}' with args '{Args}'", psi.FileName, psi.Arguments);
            var proc = Process.Start(psi);
            if (proc != null && !string.IsNullOrWhiteSpace(item.Payload.TargetDisplay) && !item.Payload.TargetDisplay.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                var targetDisp = item.Payload.TargetDisplay;
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
            ExecutionSucceeded?.Invoke(item, $"Launched: {item.Name}");
        }
        catch (System.ComponentModel.Win32Exception win32Ex)
        {
            string detail = win32Ex.NativeErrorCode switch
            {
                2 => $"Target file not found:\n{command}",
                3 => $"Path not found:\n{command}",
                5 => $"Access denied (admin privileges may be required):\n{command}",
                _ => $"{win32Ex.Message}:\n{command}"
            };
            _logger.Error(win32Ex, "Failed to launch shell process '{Command}' for action '{Name}'", command, item.Name);
            ExecutionFailed?.Invoke(item, detail);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to launch shell process '{Command}' for action '{Name}'", command, item.Name);
            ExecutionFailed?.Invoke(item, $"{ex.Message}:\n{command}");
        }
    }

    private static string? ResolveExecutableFromPath(string fileName)
    {
        if (Path.IsPathRooted(fileName)) return null;
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return null;

        var extensions = new[] { "", ".exe", ".cmd", ".bat", ".ps1" };
        var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in paths)
        {
            foreach (var ext in extensions)
            {
                try
                {
                    var testPath = Path.Combine(dir, fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ext);
                    if (File.Exists(testPath))
                    {
                        return testPath;
                    }
                }
                catch { }
            }
        }
        return null;
    }
}

