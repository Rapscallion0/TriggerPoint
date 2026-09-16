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

    public event Action<TriggerItem>? OpenSettingsRequested;
    public event Action<TriggerItem, string>? ExecutionSucceeded;
    public event Action<TriggerItem, string>? ExecutionFailed;

    public ShellActionExecutor(
        ISnippetService snippetService,
        ITelemetryService telemetryService,
        IContextFilterService contextFilterService,
        IPromptDialogService? promptDialogService = null,
        IWorkflowExecutor? workflowExecutor = null,
        IMacroService? macroService = null)
    {
        _snippetService = snippetService;
        _telemetryService = telemetryService;
        _contextFilterService = contextFilterService;
        _promptDialogService = promptDialogService;
        _workflowExecutor = workflowExecutor;
        _macroService = macroService;
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

                _logger.Information("Executing snippet '{Name}' for target window handle {Hwnd}", item.Name, effectiveHwnd);
                await _snippetService.InjectSnippetAsync(item.Payload.SnippetTemplate, effectiveHwnd).ConfigureAwait(false);
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

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Environment.ExpandEnvironmentVariables(command),
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
            psi.WorkingDirectory = Environment.ExpandEnvironmentVariables(workingDirectory);
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

