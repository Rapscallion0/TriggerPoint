using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Serilog;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Win32;

namespace TriggerPoint.Infrastructure.Services;

public class Win32MacroService : IMacroService, IDisposable
{
    private readonly ILogger _logger = Log.ForContext<Win32MacroService>();

    private IntPtr _keyboardHookId = IntPtr.Zero;
    private IntPtr _mouseHookId = IntPtr.Zero;
    private NativeMethods.HookProc? _keyboardHookProc;
    private NativeMethods.HookProc? _mouseHookProc;

    private readonly List<MacroEvent> _capturedEvents = [];
    private readonly object _lock = new();
    private Stopwatch? _recordingStopwatch;
    private long _lastEventElapsedMs;
    private bool _isRecording;

    public bool IsRecording => _isRecording;

    public event Action<MacroEvent>? EventCaptured;

    public void StartRecording()
    {
        lock (_lock)
        {
            if (_isRecording) return;

            _capturedEvents.Clear();
            _recordingStopwatch = Stopwatch.StartNew();
            _lastEventElapsedMs = 0;

            _keyboardHookProc = KeyboardHookCallback;
            _mouseHookProc = MouseHookCallback;

            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            var hModule = NativeMethods.GetModuleHandle(curModule?.ModuleName);

            _keyboardHookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardHookProc, hModule, 0);
            _mouseHookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseHookProc, hModule, 0);

            _isRecording = true;
            _logger.Information("Macro recording started (Keyboard hook: {KbHook}, Mouse hook: {MouseHook})", _keyboardHookId != IntPtr.Zero, _mouseHookId != IntPtr.Zero);
        }
    }

    public MacroPayload StopRecording()
    {
        lock (_lock)
        {
            if (!_isRecording)
            {
                return new MacroPayload { Events = [.. _capturedEvents] };
            }

            _isRecording = false;

            if (_keyboardHookId != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_keyboardHookId);
                _keyboardHookId = IntPtr.Zero;
            }

            if (_mouseHookId != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_mouseHookId);
                _mouseHookId = IntPtr.Zero;
            }

            _keyboardHookProc = null;
            _mouseHookProc = null;
            _recordingStopwatch?.Stop();

            _logger.Information("Macro recording stopped. Captured {Count} events.", _capturedEvents.Count);
            return new MacroPayload { Events = [.. _capturedEvents] };
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _isRecording)
        {
            var kb = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            int msg = wParam.ToInt32();

            bool isDown = msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
            bool isUp = msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;

            if (isDown || isUp)
            {
                RecordTimestampDelta();

                var key = KeyInterop.KeyFromVirtualKey((int)kb.vkCode);
                var keyName = key != Key.None ? key.ToString() : $"VK_{(int)kb.vkCode}";

                var evt = new MacroEvent
                {
                    Type = isDown ? MacroEventType.KeyDown : MacroEventType.KeyUp,
                    KeyCode = (int)kb.vkCode,
                    KeyName = keyName
                };

                lock (_lock)
                {
                    _capturedEvents.Add(evt);
                }

                EventCaptured?.Invoke(evt);
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _isRecording)
        {
            var ms = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            int msg = wParam.ToInt32();

            MacroEventType? type = null;
            MacroMouseButton button = MacroMouseButton.Left;

            switch (msg)
            {
                case NativeMethods.WM_LBUTTONDOWN:
                    type = MacroEventType.MouseDown;
                    button = MacroMouseButton.Left;
                    break;
                case NativeMethods.WM_LBUTTONUP:
                    type = MacroEventType.MouseUp;
                    button = MacroMouseButton.Left;
                    break;
                case NativeMethods.WM_RBUTTONDOWN:
                    type = MacroEventType.MouseDown;
                    button = MacroMouseButton.Right;
                    break;
                case NativeMethods.WM_RBUTTONUP:
                    type = MacroEventType.MouseUp;
                    button = MacroMouseButton.Right;
                    break;
                case NativeMethods.WM_MBUTTONDOWN:
                    type = MacroEventType.MouseDown;
                    button = MacroMouseButton.Middle;
                    break;
                case NativeMethods.WM_MBUTTONUP:
                    type = MacroEventType.MouseUp;
                    button = MacroMouseButton.Middle;
                    break;
            }

            if (type.HasValue)
            {
                RecordTimestampDelta();

                var evt = new MacroEvent
                {
                    Type = type.Value,
                    MouseButton = button,
                    X = ms.pt.X,
                    Y = ms.pt.Y
                };

                lock (_lock)
                {
                    _capturedEvents.Add(evt);
                }

                EventCaptured?.Invoke(evt);
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    private void RecordTimestampDelta()
    {
        if (_recordingStopwatch == null) return;

        long nowMs = _recordingStopwatch.ElapsedMilliseconds;
        long delta = nowMs - _lastEventElapsedMs;
        _lastEventElapsedMs = nowMs;

        // Record delay step if significant (> 15ms)
        if (delta >= 15)
        {
            var delayEvt = new MacroEvent
            {
                Type = MacroEventType.Delay,
                DelayMs = (int)delta
            };

            lock (_lock)
            {
                _capturedEvents.Add(delayEvt);
            }

            EventCaptured?.Invoke(delayEvt);
        }
    }

    public async Task PlayMacroAsync(MacroPayload macro, double speedMultiplier = 1.0, CancellationToken cancellationToken = default)
    {
        if (macro == null || macro.Events == null || macro.Events.Count == 0) return;

        double speed = speedMultiplier > 0 ? speedMultiplier : (macro.PlaybackSpeed > 0 ? macro.PlaybackSpeed : 1.0);
        int repeats = Math.Max(1, macro.RepeatCount);

        _logger.Information("Playing macro ({EventsCount} events, Speed: {Speed}x, Repeats: {Repeats})",
            macro.Events.Count, speed, repeats);

        for (int r = 0; r < repeats; r++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var evt in macro.Events)
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (evt.Type)
                {
                    case MacroEventType.Delay:
                    {
                        int delay = (int)Math.Max(1, evt.DelayMs / speed);
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        break;
                    }

                    case MacroEventType.MouseMove:
                    {
                        NativeMethods.SetCursorPos(evt.X, evt.Y);
                        break;
                    }

                    case MacroEventType.MouseDown:
                    case MacroEventType.MouseUp:
                    {
                        NativeMethods.SetCursorPos(evt.X, evt.Y);
                        await Task.Delay(5, cancellationToken).ConfigureAwait(false);

                        uint flags = 0;
                        if (evt.MouseButton == MacroMouseButton.Left)
                            flags = evt.Type == MacroEventType.MouseDown ? NativeMethods.MOUSEEVENTF_LEFTDOWN : NativeMethods.MOUSEEVENTF_LEFTUP;
                        else if (evt.MouseButton == MacroMouseButton.Right)
                            flags = evt.Type == MacroEventType.MouseDown ? NativeMethods.MOUSEEVENTF_RIGHTDOWN : NativeMethods.MOUSEEVENTF_RIGHTUP;
                        else if (evt.MouseButton == MacroMouseButton.Middle)
                            flags = evt.Type == MacroEventType.MouseDown ? NativeMethods.MOUSEEVENTF_MIDDLEDOWN : NativeMethods.MOUSEEVENTF_MIDDLEUP;

                        var mouseInput = new NativeMethods.INPUT
                        {
                            type = NativeMethods.INPUT_MOUSE,
                            u = new NativeMethods.InputUnion
                            {
                                mi = new NativeMethods.MOUSEINPUT
                                {
                                    dx = 0,
                                    dy = 0,
                                    mouseData = 0,
                                    dwFlags = flags,
                                    time = 0,
                                    dwExtraInfo = IntPtr.Zero
                                }
                            }
                        };

                        NativeMethods.SendInput(1, [mouseInput], Marshal.SizeOf<NativeMethods.INPUT>());
                        break;
                    }

                    case MacroEventType.KeyDown:
                    case MacroEventType.KeyUp:
                    {
                        uint flags = evt.Type == MacroEventType.KeyUp ? NativeMethods.KEYEVENTF_KEYUP : 0;
                        var kbInput = new NativeMethods.INPUT
                        {
                            type = NativeMethods.INPUT_KEYBOARD,
                            u = new NativeMethods.InputUnion
                            {
                                ki = new NativeMethods.KEYBDINPUT
                                {
                                    wVk = (ushort)evt.KeyCode,
                                    wScan = 0,
                                    dwFlags = flags,
                                    time = 0,
                                    dwExtraInfo = IntPtr.Zero
                                }
                            }
                        };

                        NativeMethods.SendInput(1, [kbInput], Marshal.SizeOf<NativeMethods.INPUT>());
                        break;
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        StopRecording();
    }
}
