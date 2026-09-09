using System;
using System.IO;
using System.Text;
using System.Windows.Automation;
using Serilog;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Win32;

namespace TriggerPoint.Infrastructure.Services;

public class ContextFilterService : IContextFilterService
{
    private readonly ILogger _logger = Log.ForContext<ContextFilterService>();
    private IntPtr _cachedHwnd;
    private string? _cachedUrl;
    private DateTime _cacheTimestamp = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMilliseconds(350);

    public IntPtr GetForegroundWindowHandle()
    {
        return NativeMethods.GetForegroundWindow();
    }

    public string? GetForegroundProcessName()
    {
        var hWnd = GetForegroundWindowHandle();
        if (hWnd == IntPtr.Zero) return null;

        NativeMethods.GetWindowThreadProcessId(hWnd, out uint processId);
        if (processId == 0) return null;

        var hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (hProcess == IntPtr.Zero) return null;

        try
        {
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            if (NativeMethods.QueryFullProcessImageName(hProcess, 0, sb, ref size))
            {
                var fullPath = sb.ToString();
                return Path.GetFileName(fullPath);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(hProcess);
        }

        return null;
    }

    public string? GetActiveBrowserUrl(IntPtr hWnd, string? processName = null)
    {
        if (hWnd == IntPtr.Zero) return null;

        processName ??= GetForegroundProcessName();
        if (!ContextFilter.IsKnownBrowser(processName)) return null;

        // Check short-lived cache
        if (hWnd == _cachedHwnd && DateTime.UtcNow - _cacheTimestamp < CacheDuration && _cachedUrl != null)
        {
            return _cachedUrl;
        }

        string? detectedUrl = null;

        try
        {
            var root = AutomationElement.FromHandle(hWnd);
            if (root != null)
            {
                // Find edit controls (address bar)
                var editCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
                var edits = root.FindAll(TreeScope.Descendants, editCondition);

                foreach (AutomationElement edit in edits)
                {
                    if (edit.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObj) &&
                        patternObj is ValuePattern valPattern)
                    {
                        var val = valPattern.Current.Value?.Trim();
                        if (!string.IsNullOrWhiteSpace(val) &&
                            (val.Contains('.') || val.Contains("://") || val.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) || val.StartsWith("about:", StringComparison.OrdinalIgnoreCase)))
                        {
                            detectedUrl = val;
                            break;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "UI Automation URL extraction query encountered exception for process {ProcessName}", processName);
        }

        // Fallback: Window Title heuristic
        if (string.IsNullOrWhiteSpace(detectedUrl))
        {
            int length = NativeMethods.GetWindowTextLength(hWnd);
            if (length > 0)
            {
                var sb = new StringBuilder(length + 1);
                if (NativeMethods.GetWindowText(hWnd, sb, sb.Capacity) > 0)
                {
                    detectedUrl = sb.ToString();
                }
            }
        }

        _cachedHwnd = hWnd;
        _cachedUrl = detectedUrl;
        _cacheTimestamp = DateTime.UtcNow;

        return detectedUrl;
    }

    public bool ShouldExecute(TriggerItem item)
    {
        if (item.ContextFilter == null) return true;

        var currentProc = GetForegroundProcessName();
        var hWnd = GetForegroundWindowHandle();

        // Check if item defines URL rules
        bool hasUrlRules = item.ContextFilter.AllowedUrls.Count > 0 || item.ContextFilter.ExcludedUrls.Count > 0;

        if (hasUrlRules)
        {
            if (ContextFilter.IsKnownBrowser(currentProc))
            {
                var activeUrl = GetActiveBrowserUrl(hWnd, currentProc);
                bool matches = item.ContextFilter.IsActive(currentProc, activeUrl);
                _logger.Debug("Context filter evaluation for '{Name}' on browser '{Proc}' (URL: '{Url}'): {Matches}", 
                    item.Name, currentProc, activeUrl, matches);
                return matches;
            }
            else
            {
                // Not in a browser: if AllowedUrls are specified, suppress since action is browser-scoped
                if (item.ContextFilter.AllowedUrls.Count > 0)
                {
                    _logger.Debug("Context filter suppressed '{Name}': AllowedUrls specified but foreground '{Proc}' is not a recognized browser.", 
                        item.Name, currentProc);
                    return false;
                }
            }
        }

        bool procActive = item.ContextFilter.IsActiveForProcess(currentProc);
        _logger.Debug("Context filter process evaluation for '{Name}' on '{Proc}': {Active}", 
            item.Name, currentProc, procActive);
        return procActive;
    }
}
