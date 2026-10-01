using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Serilog;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Win32;

namespace TriggerPoint.Infrastructure.Services;

public class AbbreviationTrie
{
    private class Node
    {
        public readonly Dictionary<char, Node> Children = new();
        public TriggerItem? Item;
    }

    private readonly Node _root = new();

    public void Insert(string abbreviation, TriggerItem item)
    {
        if (string.IsNullOrEmpty(abbreviation)) return;
        var current = _root;
        foreach (char c in abbreviation)
        {
            if (!current.Children.TryGetValue(c, out var next))
            {
                next = new Node();
                current.Children[c] = next;
            }
            current = next;
        }
        current.Item = item;
    }

    public TriggerItem? MatchSuffix(string text, AbbreviationTriggerMode mode)
    {
        if (string.IsNullOrEmpty(text)) return null;

        for (int i = 0; i < text.Length; i++)
        {
            var suffix = text[i..];
            var current = _root;
            bool match = true;
            foreach (char c in suffix)
            {
                if (!current.Children.TryGetValue(c, out var next))
                {
                    match = false;
                    break;
                }
                current = next;
            }

            if (match && current.Item != null && current.Item.AbbreviationMode == mode)
            {
                return current.Item;
            }
        }

        return null;
    }

    public void Clear()
    {
        _root.Children.Clear();
        _root.Item = null;
    }
}

public class AbbreviationExpanderService : IAbbreviationExpanderService
{
    private readonly ILogger _logger = Log.ForContext<AbbreviationExpanderService>();
    private readonly ISnippetService _snippetService;
    private readonly IContextFilterService _contextFilterService;
    private readonly Func<AppSettings> _settingsProvider;

    private readonly AbbreviationTrie _trie = new();
    private readonly object _lock = new();

    private IntPtr _hookId = IntPtr.Zero;
    private NativeMethods.HookProc? _hookProc;
    private string _buffer = string.Empty;
    private volatile bool _isSynthesizing;

    private static readonly HashSet<char> Delimiters = [' ', '\t', '\n', '\r', ',', '.', ';', ':', '!', '?', '-', '_', '/'];

    public bool IsRunning => _hookId != IntPtr.Zero;

    public AbbreviationExpanderService(
        ISnippetService snippetService,
        IContextFilterService contextFilterService,
        Func<AppSettings> settingsProvider)
    {
        _snippetService = snippetService;
        _contextFilterService = contextFilterService;
        _settingsProvider = settingsProvider;
    }

    public void UpdateSnippets(IEnumerable<TriggerItem> items)
    {
        lock (_lock)
        {
            _trie.Clear();
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (item.IsEnabled &&
                        item.ActionType == ActionType.Snippet &&
                        !string.IsNullOrWhiteSpace(item.Abbreviation))
                    {
                        _trie.Insert(item.Abbreviation.Trim(), item);
                    }
                }
            }
        }

        var settings = _settingsProvider?.Invoke();
        if (settings != null && settings.EnableAbbreviationExpander && !IsRunning)
        {
            Start();
        }
    }

    public TriggerItem? MatchSuffix(string text, AbbreviationTriggerMode mode)
    {
        lock (_lock)
        {
            return _trie.MatchSuffix(text, mode);
        }
    }

    public void Start()
    {
        if (IsRunning) return;

        var settings = _settingsProvider();
        if (!settings.EnableAbbreviationExpander)
        {
            _logger.Information("Abbreviation Expander is disabled by user settings; skipping hook activation.");
            return;
        }

        _hookProc = HookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        var moduleHandle = NativeMethods.GetModuleHandle(curModule?.ModuleName);

        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _hookProc,
            moduleHandle,
            0);

        if (_hookId == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            _logger.Error("Failed to install low-level keyboard hook for Abbreviation Expander. Error: {ErrorCode}", err);
        }
        else
        {
            _logger.Information("Abbreviation Expander keyboard hook successfully installed.");
        }
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            _hookProc = null;
            _logger.Information("Abbreviation Expander keyboard hook uninstalled.");
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !_isSynthesizing)
        {
            int msg = (int)wParam;
            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
            {
                var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                ProcessKeyDown(hookStruct.vkCode, hookStruct.scanCode);
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void ProcessKeyDown(uint vkCode, uint scanCode)
    {
        var settings = _settingsProvider();
        if (!settings.EnableAbbreviationExpander)
        {
            _buffer = string.Empty;
            return;
        }

        // 1. Backspace: remove last character from buffer
        if (vkCode == 0x08) // VK_BACK
        {
            if (_buffer.Length > 0)
            {
                _buffer = _buffer[..^1];
            }
            return;
        }

        // 2. Navigation, control keys, and shortcuts: reset buffer
        if (vkCode == 0x1B || // VK_ESCAPE
            (vkCode >= 0x21 && vkCode <= 0x28) || // PageUp, PageDown, End, Home, Left, Up, Right, Down
            vkCode == 0x2E) // VK_DELETE
        {
            _buffer = string.Empty;
            return;
        }

        // Modifiers: Ctrl, Alt, Win pressed along with key -> reset buffer
        bool ctrlDown = (NativeMethods.GetKeyState(0x11) & 0x8000) != 0; // VK_CONTROL
        bool altDown = (NativeMethods.GetKeyState(0x12) & 0x8000) != 0;  // VK_MENU
        bool winDown = (NativeMethods.GetKeyState(0x5B) & 0x8000) != 0 || (NativeMethods.GetKeyState(0x5C) & 0x8000) != 0;

        if (ctrlDown || altDown || winDown)
        {
            _buffer = string.Empty;
            return;
        }

        // 3. Resolve character representation
        char? typedChar = null;
        if (vkCode == 0x0D) // VK_RETURN
        {
            typedChar = '\n';
        }
        else if (vkCode == 0x09) // VK_TAB
        {
            typedChar = '\t';
        }
        else
        {
            var keyState = new byte[256];
            NativeMethods.GetKeyboardState(keyState);
            var sb = new StringBuilder(4);
            var hkl = NativeMethods.GetKeyboardLayout(0);
            int convResult = NativeMethods.ToUnicodeEx(vkCode, scanCode, keyState, sb, sb.Capacity, 0, hkl);
            if (convResult > 0 && sb.Length > 0)
            {
                typedChar = sb[0];
            }
        }

        if (!typedChar.HasValue) return;

        char ch = typedChar.Value;

        // 4. Check for Delimiter matching vs Immediate matching
        TriggerItem? matchedItem = null;
        int backspaceCount = 0;

        lock (_lock)
        {
            if (Delimiters.Contains(ch))
            {
                // Delimiter typed: check if preceding buffer matches any delimiter-triggered snippet
                matchedItem = _trie.MatchSuffix(_buffer, AbbreviationTriggerMode.Delimiter);
                if (matchedItem != null && !string.IsNullOrEmpty(matchedItem.Abbreviation))
                {
                    backspaceCount = matchedItem.Abbreviation.Length + 1; // erase delimiter + abbreviation
                }
            }

            if (matchedItem == null)
            {
                // Append character to buffer
                _buffer += ch;
                if (_buffer.Length > 32)
                {
                    _buffer = _buffer[^32..];
                }

                // Check for Immediate matching
                matchedItem = _trie.MatchSuffix(_buffer, AbbreviationTriggerMode.Immediate);
                if (matchedItem != null && !string.IsNullOrEmpty(matchedItem.Abbreviation))
                {
                    backspaceCount = matchedItem.Abbreviation.Length;
                }
            }
        }

        if (matchedItem != null && backspaceCount > 0)
        {
            _buffer = string.Empty;
            TriggerExpansion(matchedItem, backspaceCount, settings);
        }
    }

    private void TriggerExpansion(TriggerItem item, int backspaceCount, AppSettings settings)
    {
        // Global exclusion checks
        if (settings.AbbreviationSuppressInFullScreenGames)
        {
            try
            {
                NativeMethods.SHQueryUserNotificationState(out var qState);
                if (qState == NativeMethods.QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN)
                {
                    _logger.Information("Abbreviation expansion suppressed due to fullscreen 3D state.");
                    return;
                }
            }
            catch { }
        }

        var foregroundHwnd = NativeMethods.GetForegroundWindow();
        var procName = _contextFilterService.GetForegroundProcessName();

        if (!string.IsNullOrEmpty(procName) && settings.AbbreviationGlobalExcludedProcesses != null)
        {
            if (settings.AbbreviationGlobalExcludedProcesses.Any(p => p.Equals(procName, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.Information("Abbreviation expansion suppressed: process '{Proc}' is globally excluded.", procName);
                return;
            }
        }

        // Per-trigger process & URL filter checks
        if (!_contextFilterService.ShouldExecute(item))
        {
            _logger.Information("Abbreviation expansion suppressed for '{Name}' by item context filter.", item.Name);
            return;
        }

        // Perform expansion
        _ = Task.Run(async () =>
        {
            _isSynthesizing = true;
            try
            {
                // Send backspaces via SendInput
                SendBackspaces(backspaceCount);

                await Task.Delay(30);

                await _snippetService.InjectSnippetAsync(
                    item.Payload.SnippetTemplate,
                    foregroundHwnd,
                    item.Payload.SnippetContentType,
                    item.Payload.SnippetRtf);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to inject abbreviation snippet expansion for '{Name}'.", item.Name);
            }
            finally
            {
                await Task.Delay(50);
                _isSynthesizing = false;
            }
        });
    }

    private static void SendBackspaces(int count)
    {
        if (count <= 0) return;

        var inputs = new NativeMethods.INPUT[count * 2];
        for (int i = 0; i < count; i++)
        {
            inputs[i * 2] = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.InputUnion
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = 0x08, // VK_BACK
                        wScan = 0,
                        dwFlags = 0,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            inputs[i * 2 + 1] = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.InputUnion
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = 0x08,
                        wScan = 0,
                        dwFlags = NativeMethods.KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
        }

        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
