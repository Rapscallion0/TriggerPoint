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
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMilliseconds(500);

    public IntPtr LastExternalForegroundHwnd { get; set; } = IntPtr.Zero;

    public virtual IntPtr GetForegroundWindowHandle()
    {
        return NativeMethods.GetForegroundWindow();
    }

    public virtual string? GetForegroundProcessName()
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

    public virtual string? GetActiveBrowserUrl(IntPtr hWnd, string? processName = null)
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
            var extractionTask = Task.Run(() =>
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
                                return val;
                            }
                        }
                    }
                }
                return null;
            });

            if (extractionTask.Wait(45))
            {
                detectedUrl = extractionTask.Result;
            }
            else
            {
                _logger.Debug("UI Automation URL extraction timed out (>45ms) for window {HWnd}", hWnd);
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

    private Func<IReadOnlyList<TriggerItem>>? _allItemsProvider;

    public void SetAllItemsProvider(Func<IReadOnlyList<TriggerItem>>? provider)
    {
        _allItemsProvider = provider;
    }

    public bool ShouldExecute(TriggerItem item)
    {
        return ShouldExecute(item, _allItemsProvider?.Invoke());
    }

    public bool ShouldExecute(TriggerItem item, IReadOnlyList<TriggerItem>? allItems)
    {
        return ShouldExecuteInternal(item, allItems ?? _allItemsProvider?.Invoke(), new HashSet<Guid>());
    }

    private bool ShouldExecuteInternal(TriggerItem item, IReadOnlyList<TriggerItem>? allItems, HashSet<Guid> visited)
    {
        if (item == null) return true;
        if (!visited.Add(item.Id))
        {
            return true; // prevent circular dependency
        }

        // 1. If inheriting from parent folder, parent rules must also pass
        if (item.InheritContextFilter && item.ParentId.HasValue && allItems != null)
        {
            var parent = allItems.FirstOrDefault(x => x.Id == item.ParentId.Value);
            if (parent != null)
            {
                if (!ShouldExecuteInternal(parent, allItems, visited))
                {
                    _logger.Debug("Context filter suppressed '{Name}': Parent folder '{ParentName}' context rules failed.", item.Name, parent.Name);
                    return false;
                }
            }
        }

        // 2. Evaluate item's own context filter
        return EvaluateSelfContextFilter(item);
    }

    private bool EvaluateSelfContextFilter(TriggerItem item)
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

    public List<TriggerItem> GetInheritanceChain(TriggerItem item, IReadOnlyList<TriggerItem> allItems)
    {
        var chain = new List<TriggerItem>();
        if (allItems == null || item == null) return chain;

        var current = item;
        var visited = new HashSet<Guid> { current.Id };
        while (current.ParentId.HasValue)
        {
            var parent = allItems.FirstOrDefault(x => x.Id == current.ParentId.Value);
            if (parent == null || !visited.Add(parent.Id)) break;

            if (parent.ContextFilter != null && (
                parent.ContextFilter.AllowedProcesses.Count > 0 ||
                parent.ContextFilter.ExcludedProcesses.Count > 0 ||
                parent.ContextFilter.AllowedUrls.Count > 0 ||
                parent.ContextFilter.ExcludedUrls.Count > 0))
            {
                chain.Add(parent);
            }
            if (!parent.InheritContextFilter) break;
            current = parent;
        }
        return chain;
    }
}
