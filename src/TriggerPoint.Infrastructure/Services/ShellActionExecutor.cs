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

    public event Action<TriggerItem>? OpenSettingsRequested;
    public event Action<TriggerItem, string>? ExecutionSucceeded;
    public event Action<TriggerItem, string>? ExecutionFailed;

    public ShellActionExecutor(
        ISnippetService snippetService,
        ITelemetryService telemetryService,
        IContextFilterService contextFilterService)
    {
        _snippetService = snippetService;
        _telemetryService = telemetryService;
        _contextFilterService = contextFilterService;
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

        if (item.ActionType == ActionType.Shell)
        {
            ExecuteShellAction(item, executionOverride);
        }
    }

    private void ExecuteShellAction(TriggerItem item, ExecutionOverride executionOverride)
    {
        var command = item.Payload.Command?.Trim() ?? string.Empty;
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
            if (!string.IsNullOrWhiteSpace(item.Payload.Arguments))
            {
                psi.Arguments = Environment.ExpandEnvironmentVariables(item.Payload.Arguments);
            }

            // Working directory
            if (!string.IsNullOrWhiteSpace(item.Payload.WorkingDirectory))
            {
                psi.WorkingDirectory = Environment.ExpandEnvironmentVariables(item.Payload.WorkingDirectory);
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
            Process.Start(psi);
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
}
