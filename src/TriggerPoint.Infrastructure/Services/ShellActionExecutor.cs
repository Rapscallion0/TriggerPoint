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

    public ShellActionExecutor(
        ISnippetService snippetService,
        ITelemetryService telemetryService,
        IContextFilterService contextFilterService)
    {
        _snippetService = snippetService;
        _telemetryService = telemetryService;
        _contextFilterService = contextFilterService;
    }

    public async Task ExecuteAsync(TriggerItem item, ExecutionOverride executionOverride = ExecutionOverride.Standard)
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
            var targetHwnd = _contextFilterService.GetForegroundWindowHandle();
            await _snippetService.InjectSnippetAsync(item.Payload.SnippetTemplate, targetHwnd).ConfigureAwait(false);
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
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to reveal in explorer: {Command}", command);
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
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to launch shell process '{Command}' for action '{Name}'", command, item.Name);
        }
    }
}
