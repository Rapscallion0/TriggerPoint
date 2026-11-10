using System;
using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<IntPtr, AutomationElement> _cachedUrlElements = new();

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

    public virtual string? GetWindowTitle(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return null;
        int length = NativeMethods.GetWindowTextLength(hWnd);
        if (length <= 0) return null;

        var sb = new StringBuilder(length + 1);
        if (NativeMethods.GetWindowText(hWnd, sb, sb.Capacity) > 0)
        {
            var title = sb.ToString().Trim();
            return string.IsNullOrEmpty(title) ? null : title;
        }
        return null;
    }

    public virtual string? GetActiveBrowserUrl(IntPtr hWnd, string? processName = null)
        => GetActiveBrowserUrl(hWnd, processName, 60);

    public virtual string? GetActiveBrowserUrl(IntPtr hWnd, string? processName, int timeoutMs)
    {
        if (hWnd == IntPtr.Zero) return null;

        processName ??= GetForegroundProcessName();
        if (!ContextFilter.IsKnownBrowser(processName)) return null;

        // Check short-lived cache for fast path (e.g. background hotkey / snippet evaluation)
        if (timeoutMs <= 100 && hWnd == _cachedHwnd && DateTime.UtcNow - _cacheTimestamp < CacheDuration && !string.IsNullOrWhiteSpace(_cachedUrl))
        {
            return _cachedUrl;
        }

        bool hasCachedElement = _cachedUrlElements.ContainsKey(hWnd);
        // Allow sufficient time on cold discovery per browser hWnd so the address bar AutomationElement is cached;
        // subsequent reads use the cached AutomationElement and complete in < 2ms.
        int effectiveTimeoutMs = hasCachedElement ? Math.Max(timeoutMs, 60) : Math.Max(timeoutMs, 400);

        string? detectedUrl = null;

        try
        {
            var extractionTask = Task.Run(() =>
            {
                // 1. Fast path: read directly from cached address-bar / document AutomationElement (< 2ms)
                if (_cachedUrlElements.TryGetValue(hWnd, out var cachedEl))
                {
                    try
                    {
                        var fastVal = TryReadElementUrl(cachedEl);
                        if (fastVal != null)
                        {
                            return fastVal;
                        }
                    }
                    catch (ElementNotAvailableException)
                    {
                        _cachedUrlElements.TryRemove(hWnd, out _);
                    }
                    catch
                    {
                        _cachedUrlElements.TryRemove(hWnd, out _);
                    }
                }

                var root = AutomationElement.FromHandle(hWnd);
                if (root == null) return null;

                // 2. Targeted toolbar address-bar lookup (stops at browser toolbar before entering web DOM)
                var fastBarCondition = new OrCondition(
                    new PropertyCondition(AutomationElement.ClassNameProperty, "OmniboxViewViews"),
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "urlbar-input"),
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "addressEditBox"));

                var fastBar = root.FindFirst(TreeScope.Descendants, fastBarCondition);
                if (fastBar != null)
                {
                    var val = TryReadElementUrl(fastBar);
                    if (val != null)
                    {
                        CacheUrlElement(hWnd, fastBar);
                        return val;
                    }
                }

                // 3. Web Document root lookup (supports Chrome/Edge installed PWAs & --app windows without an address bar)
                var docCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document);
                var docElement = root.FindFirst(TreeScope.Descendants, docCondition);
                if (docElement != null)
                {
                    var docUrl = TryReadElementUrl(docElement);
                    if (docUrl != null)
                    {
                        CacheUrlElement(hWnd, docElement);
                        return docUrl;
                    }
                }

                // 4. Fallback: scan Edit controls, rejecting email fields and multi-word text inputs
                var editCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
                var edits = root.FindAll(TreeScope.Descendants, editCondition);

                foreach (AutomationElement edit in edits)
                {
                    var val = TryReadElementUrl(edit);
                    if (val != null)
                    {
                        CacheUrlElement(hWnd, edit);
                        return val;
                    }
                }

                return null;
            });

            if (extractionTask.Wait(effectiveTimeoutMs))
            {
                detectedUrl = extractionTask.Result;
            }
            else
            {
                _logger.Debug("UI Automation URL extraction timed out (>{TimeoutMs}ms) for window {HWnd}", effectiveTimeoutMs, hWnd);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "UI Automation URL extraction query encountered exception for process {ProcessName}", processName);
        }

        if (!string.IsNullOrWhiteSpace(detectedUrl))
        {
            _cachedHwnd = hWnd;
            _cachedUrl = detectedUrl;
            _cacheTimestamp = DateTime.UtcNow;
            return detectedUrl;
        }

        // Fallback: Window / Tab Title heuristic (do not overwrite _cachedUrl so transient UIA timeouts don't poison URL cache)
        return GetWindowTitle(hWnd);
    }

    private void CacheUrlElement(IntPtr hWnd, AutomationElement element)
    {
        if (_cachedUrlElements.Count >= 16)
        {
            _cachedUrlElements.Clear();
        }
        _cachedUrlElements[hWnd] = element;
    }

    private static string? TryReadElementUrl(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObj) &&
            patternObj is ValuePattern valPattern)
        {
            var val = valPattern.Current.Value?.Trim();
            if (IsLikelyBrowserUrl(val))
            {
                return val;
            }
        }
        return null;
    }

    public static bool IsLikelyBrowserUrl(string? val)
    {
        if (string.IsNullOrWhiteSpace(val)) return false;
        val = val.Trim();

        // Address bar URLs do not contain spaces or newlines (whereas inline email bodies/subjects do)
        if (val.AsSpan().IndexOfAny(" \r\n\t".AsSpan()) >= 0)
            return false;

        // Reject bare email addresses (e.g. "person@company.com" in Gmail's To/Cc field)
        int atIdx = val.IndexOf('@');
        if (atIdx >= 0)
        {
            int slashIdx = val.IndexOf('/');
            bool hasScheme = val.Contains("://", StringComparison.Ordinal);
            if (!hasScheme && (slashIdx < 0 || atIdx < slashIdx))
                return false;
        }

        return val.Contains('.') ||
               val.Contains("://", StringComparison.Ordinal) ||
               val.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
               val.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
               val.StartsWith("chrome:", StringComparison.OrdinalIgnoreCase) ||
               val.StartsWith("edge:", StringComparison.OrdinalIgnoreCase) ||
               val.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
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
                var activeTitle = GetWindowTitle(hWnd);
                bool matches = item.ContextFilter.IsActive(currentProc, activeUrl, activeTitle);
                _logger.Debug("Context filter evaluation for '{Name}' on browser '{Proc}' (URL: '{Url}', Title: '{Title}'): {Matches}", 
                    item.Name, currentProc, activeUrl, activeTitle, matches);
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
