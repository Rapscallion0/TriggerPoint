using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
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

    private readonly Dictionary<int, List<TriggerItem>> _registeredByHotkeyId = [];
    private readonly Dictionary<Guid, int> _idByItemId = [];
    private readonly Dictionary<Guid, HotkeyConflictStatus> _currentConflicts = [];
    private readonly List<TriggerItem> _lastItems = [];
    private int _suspendCount;
    private int _nextId = 1000;

    // Chord State Machine Fields
    private IntPtr _chordHookId = IntPtr.Zero;
    private NativeMethods.HookProc? _chordHookProc;
    private Timer? _chordTimeoutTimer;
    private ShortcutBinding? _activeChordLeader;
    private List<TriggerItem> _activeChordCandidates = [];
    private readonly object _chordLock = new();

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
    public event EventHandler<ChordWaitingEventArgs>? ChordWaiting;
    public event EventHandler? ChordCompleted;

    public void Suspend()
    {
        int count = Interlocked.Increment(ref _suspendCount);
        if (count == 1)
        {
            CancelChordMode();
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
        int count = Interlocked.Decrement(ref _suspendCount);
        if (count <= 0)
        {
            Interlocked.Exchange(ref _suspendCount, 0);
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

        CancelChordMode();
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

        CancelChordMode();
        UnregisterAll();
        _currentConflicts.Clear();

        // Tier 1: Internal TriggerPoint Registry Validation
        var tier1Conflicts = HotkeyRegistryValidator.ValidateTier1Conflicts(itemsList);
        foreach (var (itemId, status) in tier1Conflicts)
        {
            _currentConflicts[itemId] = status;
            _logger.Warning("Tier 1 Internal Hotkey Conflict: {Message}", status.SystemMessage);
        }

        // Tier 2: Group non-colliding items by their LeaderBinding
        var nonColliding = itemsList
            .Where(item => item.Hotkey != null && !item.Hotkey.IsEmpty && item.IsEnabled && !tier1Conflicts.ContainsKey(item.Id))
            .ToList();

        var leaderGroups = nonColliding
            .GroupBy(item => item.Hotkey!.GetLeaderBinding())
            .ToList();

        foreach (var group in leaderGroups)
        {
            RegisterLeaderGroup(group.Key, group.ToList());
        }

        ConflictsUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void RegisterLeaderGroup(ShortcutBinding leader, List<TriggerItem> items)
    {
        if (_hwnd == IntPtr.Zero || items.Count == 0) return;

        uint mods = 0;
        if (leader.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= NativeMethods.MOD_ALT;
        if (leader.Modifiers.HasFlag(ModifierKeys.Control)) mods |= NativeMethods.MOD_CONTROL;
        if (leader.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= NativeMethods.MOD_SHIFT;
        if (leader.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= NativeMethods.MOD_WIN;
        mods |= NativeMethods.MOD_NOREPEAT;

        uint vk = (uint)leader.VirtualKey;
        int hotkeyId = _nextId++;

        bool success = NativeMethods.RegisterHotKey(_hwnd, hotkeyId, mods, vk);
        if (success)
        {
            _registeredByHotkeyId[hotkeyId] = items;
            foreach (var item in items)
            {
                _idByItemId[item.Id] = hotkeyId;
                item.ConflictStatus = HotkeyConflictStatus.None;
            }

            if (items.Count == 1 && !items[0].Hotkey!.IsChord)
            {
                _logger.Information("Registered single hotkey '{Hotkey}' for action '{ActionName}' (ID {Id})",
                    leader.DisplayText, items[0].Name, hotkeyId);
            }
            else
            {
                _logger.Information("Registered leader key '{Leader}' with {Count} chord/action bindings (ID {Id})",
                    leader.DisplayText, items.Count, hotkeyId);
            }
        }
        else
        {
            int errorCode = Marshal.GetLastWin32Error();
            if (errorCode == NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED)
            {
                foreach (var item in items)
                {
                    var initialStatus = HotkeyConflictStatus.CreateExternal(leader.DisplayText);
                    _currentConflicts[item.Id] = initialStatus;
                    item.ConflictStatus = initialStatus;
                    _ = ProbeExternalConflictAsync(item);
                }

                _logger.Warning("Tier 2 System Conflict: Leader hotkey '{Hotkey}' already registered (Win32 Error 1409)",
                    leader.DisplayText);
            }
            else
            {
                _logger.Error("Failed to register leader hotkey '{Hotkey}'. Win32 Error: {Error}",
                    leader.DisplayText, errorCode);
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
            foreach (var (id, _) in _registeredByHotkeyId)
            {
                NativeMethods.UnregisterHotKey(_hwnd, id);
            }
        }

        _registeredByHotkeyId.Clear();
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
            if (_registeredByHotkeyId.TryGetValue(hotkeyId, out var items))
            {
                if (!IsSnoozed)
                {
                    // Case 1: Exactly 1 non-chord item mapped to this key
                    if (items.Count == 1 && !items[0].Hotkey!.IsChord)
                    {
                        var single = items[0];
                        _logger.Information("Hotkey triggered: '{Hotkey}' for action '{Name}'", 
                            single.Hotkey?.DisplayText, single.Name);
                        HotkeyTriggered?.Invoke(this, single);
                    }
                    else
                    {
                        // Case 2: Leader key for Chorded sequence(s)
                        var leader = items[0].Hotkey!.GetLeaderBinding();
                        _logger.Information("Leader hotkey '{Leader}' triggered. Entering Chord Mode with {Count} candidates.", 
                            leader.DisplayText, items.Count);
                        BeginChordMode(leader, items);
                    }
                }
                else
                {
                    _logger.Debug("Hotkey ID {Id} ignored because listener is snoozed.", hotkeyId);
                }

                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    #region Chord State Machine & Low-Level Hook

    private void BeginChordMode(ShortcutBinding leader, List<TriggerItem> candidates)
    {
        lock (_chordLock)
        {
            CancelChordModeInternal();

            _activeChordLeader = leader;
            _activeChordCandidates = [.. candidates];

            // Install low-level keyboard hook
            _chordHookProc = ChordKeyboardHookCallback;
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            var hModule = NativeMethods.GetModuleHandle(curModule?.ModuleName);
            _chordHookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _chordHookProc, hModule, 0);

            // 2.5 second timeout timer
            _chordTimeoutTimer = new Timer(_ =>
            {
                _logger.Information("Chord mode timeout expired for leader '{Leader}'", leader.DisplayText);
                CancelChordMode();
            }, null, 2500, Timeout.Infinite);

            ChordWaiting?.Invoke(this, new ChordWaitingEventArgs(leader, _activeChordCandidates));
        }
    }

    private IntPtr ChordKeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
            {
                var kb = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                int vk = (int)kb.vkCode;

                // Escape cancels chord mode immediately
                if (vk == 27) // VK_ESCAPE
                {
                    _logger.Information("Chord mode cancelled via Escape.");
                    CancelChordMode();
                    return (IntPtr)1; // Swallow Escape
                }

                // If user is pressing modifier keys (Ctrl/Alt/Shift/Win), let them pass to allow modifier combination for chord
                if (IsModifierVk(vk))
                {
                    return NativeMethods.CallNextHookEx(_chordHookId, nCode, wParam, lParam);
                }

                // Check candidate matches
                TriggerItem? matched = null;
                lock (_chordLock)
                {
                    var currentMods = GetCurrentHookModifiers();
                    matched = _activeChordCandidates.FirstOrDefault(c => 
                        c.Hotkey != null && 
                        c.Hotkey.ChordVirtualKey == vk && 
                        (c.Hotkey.ChordModifiers == ModifierKeys.None || c.Hotkey.ChordModifiers == currentMods));

                    matched ??= _activeChordCandidates.FirstOrDefault(c => 
                        c.Hotkey != null && c.Hotkey.ChordVirtualKey == vk);
                }

                if (matched != null)
                {
                    _logger.Information("Chord completed: '{Leader}, {Chord}' -> executing '{Name}'", 
                        _activeChordLeader?.DisplayText, matched.Hotkey?.ChordDisplayText, matched.Name);

                    CancelChordMode();
                    HotkeyTriggered?.Invoke(this, matched);
                    return (IntPtr)1; // Swallow key stroke
                }

                // If no chord matched, check if there is a plain non-chord action under this leader
                TriggerItem? singleFallback = null;
                lock (_chordLock)
                {
                    singleFallback = _activeChordCandidates.FirstOrDefault(c => c.Hotkey != null && !c.Hotkey.IsChord);
                }

                CancelChordMode();

                if (singleFallback != null)
                {
                    _logger.Information("No chord matched; triggering default leader action '{Name}'", singleFallback.Name);
                    HotkeyTriggered?.Invoke(this, singleFallback);
                    return (IntPtr)1;
                }

                return NativeMethods.CallNextHookEx(_chordHookId, nCode, wParam, lParam);
            }
        }

        return NativeMethods.CallNextHookEx(_chordHookId, nCode, wParam, lParam);
    }

    public void CancelChordMode()
    {
        lock (_chordLock)
        {
            CancelChordModeInternal();
            ChordCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void CancelChordModeInternal()
    {
        if (_chordTimeoutTimer != null)
        {
            _chordTimeoutTimer.Dispose();
            _chordTimeoutTimer = null;
        }

        if (_chordHookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_chordHookId);
            _chordHookId = IntPtr.Zero;
        }

        _chordHookProc = null;
        _activeChordLeader = null;
        _activeChordCandidates.Clear();
    }

    private static bool IsModifierVk(int vk)
    {
        return vk is 16 or 160 or 161   // Shift / LShift / RShift
                  or 17 or 162 or 163   // Ctrl / LCtrl / RCtrl
                  or 18 or 164 or 165   // Alt / LAlt / RAlt
                  or 91 or 92;          // LWin / RWin
    }

    private static ModifierKeys GetCurrentHookModifiers()
    {
        var mods = ModifierKeys.None;
        if ((NativeMethods.GetKeyState(17) & 0x8000) != 0) mods |= ModifierKeys.Control;
        if ((NativeMethods.GetKeyState(18) & 0x8000) != 0) mods |= ModifierKeys.Alt;
        if ((NativeMethods.GetKeyState(16) & 0x8000) != 0) mods |= ModifierKeys.Shift;
        if ((NativeMethods.GetKeyState(91) & 0x8000) != 0 || (NativeMethods.GetKeyState(92) & 0x8000) != 0) mods |= ModifierKeys.Windows;
        return mods;
    }

    #endregion

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
