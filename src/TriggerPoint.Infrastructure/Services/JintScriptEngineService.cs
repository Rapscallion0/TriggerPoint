using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Jint;
using Jint.Native;
using Serilog;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;

namespace TriggerPoint.Infrastructure.Services;

public class JintScriptEngineService : IScriptEngineService
{
    private readonly ILogger _logger = Log.ForContext<JintScriptEngineService>();
    private readonly IPromptDialogService _promptDialogService;
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly IToastNotificationService _toastNotificationService;
    private readonly ISnippetService _snippetService;
    private readonly IBrowserDetectionService? _browserDetectionService;
    private readonly IMacroService? _macroService;

    public Func<string, IntPtr?, Task<bool>>? ActionExecutionHandler { get; set; }

    public JintScriptEngineService(
        IPromptDialogService promptDialogService,
        IConfirmationDialogService confirmationDialogService,
        IToastNotificationService toastNotificationService,
        ISnippetService snippetService,
        IBrowserDetectionService? browserDetectionService = null,
        IMacroService? macroService = null)
    {
        _promptDialogService = promptDialogService;
        _confirmationDialogService = confirmationDialogService;
        _toastNotificationService = toastNotificationService;
        _snippetService = snippetService;
        _browserDetectionService = browserDetectionService;
        _macroService = macroService;
    }

    public async Task<ScriptExecutionResult> ExecuteAsync(
        string script,
        IDictionary<string, string>? initialVariables = null,
        bool isElevated = false,
        IntPtr? targetHwnd = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return new ScriptExecutionResult(true, null, new Dictionary<string, string>());
        }

        try
        {
            return await Task.Run(() =>
            {
                var bridge = new TriggerPointJsBridge(
                    _promptDialogService,
                    _confirmationDialogService,
                    _toastNotificationService,
                    _snippetService,
                    _browserDetectionService,
                    _macroService,
                    ActionExecutionHandler,
                    isElevated,
                    targetHwnd,
                    cancellationToken,
                    _logger);

                if (initialVariables != null)
                {
                    foreach (var kvp in initialVariables)
                    {
                        bridge.vars[kvp.Key] = kvp.Value;
                    }
                }

                try
                {
                    var engine = new Engine(options =>
                    {
                        options.TimeoutInterval(TimeSpan.FromSeconds(10));
                        options.LimitMemory(25_000_000);
                        options.MaxStatements(50_000);
                        options.CancellationToken(cancellationToken);
                    });

                    engine.SetValue("tp", bridge);

                    // Wrap in async IIFE so users can use top-level await freely
                    var wrappedScript = "(async () => {\n" + script + "\n})()";
                    engine.Evaluate(wrappedScript).UnwrapIfPromise();

                    var resultVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kvp in bridge.vars)
                    {
                        if (kvp.Value != null)
                        {
                            resultVars[kvp.Key] = kvp.Value.ToString() ?? string.Empty;
                        }
                    }

                    return new ScriptExecutionResult(true, null, resultVars);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Script execution failed");
                    var resultVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kvp in bridge.vars)
                    {
                        if (kvp.Value != null)
                        {
                            resultVars[kvp.Key] = kvp.Value.ToString() ?? string.Empty;
                        }
                    }
                    return new ScriptExecutionResult(false, ex.Message, resultVars);
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new ScriptExecutionResult(false, "Script execution cancelled.", new Dictionary<string, string>());
        }
    }
}

public class TriggerPointJsBridge
{
    private readonly IPromptDialogService _promptDialogService;
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly IToastNotificationService _toastNotificationService;
    private readonly ISnippetService _snippetService;
    private readonly IBrowserDetectionService? _browserDetectionService;
    private readonly IMacroService? _macroService;
    private readonly Func<string, IntPtr?, Task<bool>>? _actionExecutionHandler;
    private readonly bool _isElevated;
    private readonly IntPtr? _targetHwnd;
    private readonly CancellationToken _cancellationToken;
    private readonly ILogger _logger;

    public TriggerPointFsBridge fs { get; } = new();
    public TriggerPointClipboardBridge clipboard { get; } = new();
    public Dictionary<string, object?> vars { get; } = new(StringComparer.OrdinalIgnoreCase);

    public TriggerPointJsBridge(
        IPromptDialogService promptDialogService,
        IConfirmationDialogService confirmationDialogService,
        IToastNotificationService toastNotificationService,
        ISnippetService snippetService,
        IBrowserDetectionService? browserDetectionService,
        IMacroService? macroService,
        Func<string, IntPtr?, Task<bool>>? actionExecutionHandler,
        bool isElevated,
        IntPtr? targetHwnd,
        CancellationToken cancellationToken,
        ILogger logger)
    {
        _promptDialogService = promptDialogService;
        _confirmationDialogService = confirmationDialogService;
        _toastNotificationService = toastNotificationService;
        _snippetService = snippetService;
        _browserDetectionService = browserDetectionService;
        _macroService = macroService;
        _actionExecutionHandler = actionExecutionHandler;
        _isElevated = isElevated;
        _targetHwnd = targetHwnd;
        _cancellationToken = cancellationToken;
        _logger = logger;
    }

    public void runMacro(string macroJson)
    {
        if (string.IsNullOrWhiteSpace(macroJson) || _macroService == null) return;
        try
        {
            var macro = System.Text.Json.JsonSerializer.Deserialize<MacroPayload>(macroJson);
            if (macro != null && macro.Events.Count > 0)
            {
                _macroService.PlayMacroAsync(macro, 1.0, _cancellationToken).GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to run macro in script");
        }
    }

    public string? prompt(string label, JsValue? optionsVal = null)
    {
        var labelStr = label ?? "Enter value";
        var defaultVal = string.Empty;
        var choicesStr = string.Empty;
        var typeStr = string.Empty;
        double? minVal = null;
        double? maxVal = null;
        var dateFormat = string.Empty;

        if (optionsVal != null && optionsVal.IsObject())
        {
            var opt = optionsVal.AsObject();
            if (opt.HasProperty("default")) defaultVal = opt.Get("default").AsString();
            if (opt.HasProperty("choices")) choicesStr = opt.Get("choices").AsString();
            if (opt.HasProperty("type")) typeStr = opt.Get("type").AsString();
            if (opt.HasProperty("min") && !opt.Get("min").IsNull() && !opt.Get("min").IsUndefined()) minVal = opt.Get("min").AsNumber();
            if (opt.HasProperty("max") && !opt.Get("max").IsNull() && !opt.Get("max").IsUndefined()) maxVal = opt.Get("max").AsNumber();
            if (opt.HasProperty("dateFormat")) dateFormat = opt.Get("dateFormat").AsString();
        }

        var token = new PromptToken
        {
            RawTag = "script_prompt",
            Label = labelStr,
            DefaultValue = defaultVal,
            MinNumber = minVal,
            MaxNumber = maxVal,
            DateFormat = string.IsNullOrWhiteSpace(dateFormat) ? "yyyy-MM-dd" : dateFormat
        };

        if (string.Equals(typeStr, "number", StringComparison.OrdinalIgnoreCase))
        {
            token.Type = TokenType.PromptNumber;
        }
        else if (string.Equals(typeStr, "date", StringComparison.OrdinalIgnoreCase))
        {
            token.Type = TokenType.PromptDatePicker;
        }
        else if (string.Equals(typeStr, "multiline", StringComparison.OrdinalIgnoreCase))
        {
            token.Type = TokenType.PromptMultiline;
        }
        else if (!string.IsNullOrWhiteSpace(choicesStr) || string.Equals(typeStr, "choice", StringComparison.OrdinalIgnoreCase))
        {
            token.Type = TokenType.PromptChoice;
            var parts = choicesStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var p in parts)
            {
                token.Choices.Add(new ChoiceOption(p, p));
            }
        }
        else
        {
            token.Type = TokenType.PromptText;
        }

        var response = _promptDialogService.ShowPromptDialogAsync([token]).GetAwaiter().GetResult();
        if (response != null && response.TryGetValue("script_prompt", out var val))
        {
            return val;
        }
        return null;
    }

    public bool confirm(string message, string? title = null, string? confirmText = null, string? cancelText = null)
    {
        return _confirmationDialogService.ShowConfirmationAsync(
            message ?? "Confirm action?",
            title ?? "TriggerPoint Confirmation",
            string.IsNullOrWhiteSpace(confirmText) ? "Confirm" : confirmText,
            string.IsNullOrWhiteSpace(cancelText) ? "Cancel" : cancelText).GetAwaiter().GetResult();
    }

    public void alert(string message, string? title = null)
    {
        _toastNotificationService.ShowWarning(title ?? "TriggerPoint Alert", message ?? string.Empty);
    }

    public void notify(string title, string message)
    {
        _toastNotificationService.ShowSuccess(title ?? "TriggerPoint", message ?? string.Empty);
    }

    private static readonly HashSet<string> SensitiveInterpreters = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd", "cmd.exe",
        "powershell", "powershell.exe",
        "pwsh", "pwsh.exe",
        "cscript", "cscript.exe",
        "wscript", "wscript.exe",
        "mshta", "mshta.exe"
    };

    public void openUrl(string url, string? browser = null, string? profile = null, bool newWindow = false)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        var expanded = Environment.ExpandEnvironmentVariables(url.Trim());

        if (!TriggerPoint.Core.Services.ProtocolValidator.IsSafeUrl(expanded, out var rejectReason))
        {
            _logger.Warning("Script URL blocked: {Reason}", rejectReason);
            _toastNotificationService.ShowWarning("Security Block", rejectReason ?? "Unsafe URL scheme blocked.");
            return;
        }

        _logger.Information("Script opening URL: {Url} (Browser: {Browser}, Profile: {Profile}, NewWindow: {NewWindow})", expanded, browser, profile, newWindow);
        if (_browserDetectionService != null)
        {
            _browserDetectionService.LaunchUrl(expanded, browser, profile, newWindow);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = expanded,
            UseShellExecute = true
        });
    }

    public void launch(string cmd, string? args = null, string? workDir = null, bool runAsAdmin = false, string? targetDisplay = null)
    {
        if (string.IsNullOrWhiteSpace(cmd)) return;
        var expandedCmd = Environment.ExpandEnvironmentVariables(cmd.Trim());

        var baseName = Path.GetFileName(expandedCmd);
        if (SensitiveInterpreters.Contains(baseName) || runAsAdmin || _isElevated)
        {
            if (_confirmationDialogService != null)
            {
                var allowed = _confirmationDialogService.ShowConfirmationAsync(
                    $"A script is requesting to launch a privileged or system shell:\n\n{expandedCmd} {args}\n\nDo you want to allow this process to execute?",
                    "Security Guardrail",
                    "Allow Execution",
                    "Block Execution").GetAwaiter().GetResult();
                if (!allowed)
                {
                    _logger.Warning("Script execution of '{Cmd}' blocked by user security guardrail.", expandedCmd);
                    throw new InvalidOperationException($"Process launch blocked by user security guardrail: '{expandedCmd}'.");
                }
            }
        }

        var psi = new ProcessStartInfo
        {
            FileName = expandedCmd,
            UseShellExecute = true
        };

        if (!string.IsNullOrWhiteSpace(args))
        {
            psi.Arguments = Environment.ExpandEnvironmentVariables(args.Trim());
        }

        if (!string.IsNullOrWhiteSpace(workDir))
        {
            psi.WorkingDirectory = Environment.ExpandEnvironmentVariables(workDir.Trim());
        }

        if (_isElevated || runAsAdmin)
        {
            psi.Verb = "runas";
        }

        _logger.Information("Script launching process: {Cmd} {Args}", psi.FileName, psi.Arguments);
        var proc = Process.Start(psi);
        if (proc != null && !string.IsNullOrWhiteSpace(targetDisplay) && !targetDisplay.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
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
                            Win32.NativeMethods.MoveWindowToTargetDisplay(proc.MainWindowHandle, targetDisplay);
                            break;
                        }
                    }
                }
                catch { }
            });
        }
    }

    public void delay(int ms)
    {
        Thread.Sleep(Math.Max(1, ms));
    }

    public void injectSnippet(string template)
    {
        if (string.IsNullOrEmpty(template)) return;
        var effectiveHwnd = _targetHwnd ?? IntPtr.Zero;
        _snippetService.InjectSnippetAsync(template, effectiveHwnd).GetAwaiter().GetResult();
    }

    public bool executeAction(string idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName) || _actionExecutionHandler == null) return false;
        return _actionExecutionHandler(idOrName.Trim(), _targetHwnd).GetAwaiter().GetResult();
    }
}

public class TriggerPointFsBridge
{
    private static bool IsRestrictedSystemPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        try
        {
            var fullPath = Path.GetFullPath(path);
            var winDir = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            if (fullPath.StartsWith(winDir, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        catch { }
        return false;
    }

    public bool exists(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        return File.Exists(expanded) || Directory.Exists(expanded);
    }

    public bool createDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        if (IsRestrictedSystemPath(expanded))
        {
            throw new InvalidOperationException($"Filesystem creation within Windows system directory is blocked by security policy: '{expanded}'");
        }
        Directory.CreateDirectory(expanded);
        return true;
    }

    public void openInExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        if (File.Exists(expanded))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{expanded}\"",
                UseShellExecute = true
            });
        }
        else
        {
            if (!Directory.Exists(expanded)) Directory.CreateDirectory(expanded);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{expanded}\"",
                UseShellExecute = true
            });
        }
    }
}

public class TriggerPointClipboardBridge
{
    public string get()
    {
        string text = string.Empty;
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            try
            {
                if (Clipboard.ContainsText()) text = Clipboard.GetText();
            }
            catch { }
        });
        return text;
    }

    public void set(string text)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            try
            {
                Clipboard.SetText(text ?? string.Empty);
            }
            catch { }
        });
    }
}
