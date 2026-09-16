using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Interop;
using Serilog;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;

namespace TriggerPoint.Infrastructure.Win32;

public class Win32HotkeyListener : IShortcutListener
{
    private readonly ILogger _logger = Log.ForContext<Win32HotkeyListener>();
    private IntPtr _hwnd = IntPtr.Zero;
    private HwndSource? _hwndSource;
    private bool _isStarted;
    private bool _isSnoozed;

    private readonly Dictionary<int, TriggerItem> _registeredById = [];
    private readonly Dictionary<Guid, int> _idByItemId = [];
    private readonly Dictionary<Guid, HotkeyConflictStatus> _currentConflicts = [];
    private readonly List<TriggerItem> _lastItems = [];
    private int _suspendCount;
    private int _nextId = 1000;

    public bool IsSnoozed
    {
        get => _isSnoozed;
        set
        {
            if (_isSnoozed != value)
            {
                _isSnoozed = value;
                _logger.Information("Hotkey listener snoozed state changed to: {IsSnoozed}", _isSnoozed);
                SnoozeChanged?.Invoke(this, _isSnoozed);
            }
        }
    }

    public IReadOnlyDictionary<Guid, HotkeyConflictStatus> CurrentConflicts => _currentConflicts;

    public event EventHandler<TriggerItem>? HotkeyTriggered;
    public event EventHandler? ConflictsUpdated;
    public event EventHandler<bool>? SnoozeChanged;

    public void Suspend()
    {
        int count = System.Threading.Interlocked.Increment(ref _suspendCount);
        if (count == 1)
        {
            UnregisterAll();
            _logger.Information("Win32HotkeyListener suspended (OS hotkeys temporarily unregistered).");
        }
        else
        {
            _logger.Debug("Win32HotkeyListener nested suspend (count: {Count}).", count);
        }
    }

    public void Resume()
    {
        int count = System.Threading.Interlocked.Decrement(ref _suspendCount);
        if (count <= 0)
        {
            System.Threading.Interlocked.Exchange(ref _suspendCount, 0);
            _logger.Information("Win32HotkeyListener resumed (restoring OS hotkeys).");
            if (_lastItems.Count > 0)
            {
                RegisterAll(_lastItems);
            }
        }
        else
        {
            _logger.Debug("Win32HotkeyListener nested resume (remaining count: {Count}).", count);
        }
    }

    public void Start(IntPtr windowHandle)
    {
        if (_isStarted) return;

        if (windowHandle != IntPtr.Zero)
        {
            _hwnd = windowHandle;
            _hwndSource = HwndSource.FromHwnd(_hwnd);
            _hwndSource?.AddHook(WndProc);
        }
        else
        {
            // Create a message-only HwndSource
            var parameters = new HwndSourceParameters("TriggerPoint_MsgWindow")
            {
                ParentWindow = NativeMethods.HWND_MESSAGE
            };
            _hwndSource = new HwndSource(parameters);
            _hwnd = _hwndSource.Handle;
            _hwndSource.AddHook(WndProc);
        }

        _isStarted = true;
        _logger.Information("Win32HotkeyListener started on HWND {Hwnd}", _hwnd);
    }

    public void Stop()
    {
        if (!_isStarted) return;

        UnregisterAll();

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource.Dispose();
            _hwndSource = null;
        }

        _hwnd = IntPtr.Zero;
        _isStarted = false;
        _logger.Information("Win32HotkeyListener stopped.");
    }

    public void RegisterAll(IEnumerable<TriggerItem> items)
    {
        var itemsList = items.ToList();
        _lastItems.Clear();
        _lastItems.AddRange(itemsList);

        if (_suspendCount > 0)
        {
            _logger.Information("Win32HotkeyListener is currently suspended. Items cached but not registered with OS.");
            return;
        }

        UnregisterAll();
        _currentConflicts.Clear();

        // Tier 1: Internal TriggerPoint Registry Validation
        var tier1Conflicts = HotkeyRegistryValidator.ValidateTier1Conflicts(itemsList);
        foreach (var (itemId, status) in tier1Conflicts)
        {
            _currentConflicts[itemId] = status;
            _logger.Warning("Tier 1 Internal Hotkey Conflict: {Message}", status.SystemMessage);
        }

        // Tier 2: Win32 OS Registration for non-colliding items
        foreach (var item in itemsList)
        {
            if (item.Hotkey == null || item.Hotkey.IsEmpty || !item.IsEnabled)
            {
                item.ConflictStatus = HotkeyConflictStatus.None;
                continue;
            }

            // Skip items flagged in Tier 1
            if (tier1Conflicts.ContainsKey(item.Id))
            {
                continue;
            }

            RegisterSingleItem(item);
        }

        ConflictsUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void RegisterSingleItem(TriggerItem item)
    {
        if (_hwnd == IntPtr.Zero || item.Hotkey == null) return;

        uint mods = 0;
        if (item.Hotkey.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= NativeMethods.MOD_ALT;
        if (item.Hotkey.Modifiers.HasFlag(ModifierKeys.Control)) mods |= NativeMethods.MOD_CONTROL;
        if (item.Hotkey.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= NativeMethods.MOD_SHIFT;
        if (item.Hotkey.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= NativeMethods.MOD_WIN;
        mods |= NativeMethods.MOD_NOREPEAT;

        uint vk = (uint)item.Hotkey.VirtualKey;
        int hotkeyId = _nextId++;

        bool success = NativeMethods.RegisterHotKey(_hwnd, hotkeyId, mods, vk);
        if (success)
        {
            _registeredById[hotkeyId] = item;
            _idByItemId[item.Id] = hotkeyId;
            item.ConflictStatus = HotkeyConflictStatus.None;
            _logger.Information("Registered hotkey '{Hotkey}' for action '{ActionName}' (ID {Id})", 
                item.Hotkey.DisplayText, item.Name, hotkeyId);
        }
        else
        {
            int errorCode = Marshal.GetLastWin32Error();
            if (errorCode == NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED)
            {
                // Tier 2 External Conflict
                var initialStatus = HotkeyConflictStatus.CreateExternal(item.Hotkey.DisplayText);
                _currentConflicts[item.Id] = initialStatus;
                item.ConflictStatus = initialStatus;
                _logger.Warning("Tier 2 System Conflict: Hotkey '{Hotkey}' already registered (Win32 Error 1409)", 
                    item.Hotkey.DisplayText);

                // Run asynchronous IShellLink heuristic probe
                _ = ProbeExternalConflictAsync(item);
            }
            else
            {
                _logger.Error("Failed to register hotkey '{Hotkey}'. Win32 Error: {Error}", 
                    item.Hotkey.DisplayText, errorCode);
            }
        }
    }

    private async Task ProbeExternalConflictAsync(TriggerItem item)
    {
        if (item.Hotkey == null) return;

        var detectedApp = await ShellLinkScanner.FindConflictingShortcutAsync(item.Hotkey).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(detectedApp))
        {
            var updatedStatus = HotkeyConflictStatus.CreateExternal(item.Hotkey.DisplayText, detectedApp);
            _currentConflicts[item.Id] = updatedStatus;
            item.ConflictStatus = updatedStatus;
            _logger.Information("Heuristic probe identified conflicting shortcut: '{App}' for hotkey '{Hotkey}'", 
                detectedApp, item.Hotkey.DisplayText);

            ConflictsUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UnregisterAll()
    {
        if (_hwnd != IntPtr.Zero)
        {
            foreach (var (id, item) in _registeredById)
            {
                NativeMethods.UnregisterHotKey(_hwnd, id);
            }
        }

        _registeredById.Clear();
        _idByItemId.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            if (_suspendCount > 0)
            {
                _logger.Debug("WM_HOTKEY ignored because listener is suspended.");
                handled = true;
                return IntPtr.Zero;
            }

            int hotkeyId = wParam.ToInt32();
            if (_registeredById.TryGetValue(hotkeyId, out var item))
            {
                if (!IsSnoozed)
                {
                    _logger.Information("Hotkey triggered: '{Hotkey}' for action '{Name}'", 
                        item.Hotkey?.DisplayText, item.Name);
                    HotkeyTriggered?.Invoke(this, item);
                }
                else
                {
                    _logger.Debug("Hotkey '{Hotkey}' ignored because listener is snoozed.", item.Hotkey?.DisplayText);
                }

                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
