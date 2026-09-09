using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using Serilog;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Services;
using TriggerPoint.Infrastructure.Win32;

namespace TriggerPoint.Infrastructure.Services;

public class Win32SnippetService : ISnippetService
{
    private readonly ILogger _logger = Log.ForContext<Win32SnippetService>();
    private readonly IPromptDialogService _promptDialogService;

    public Win32SnippetService(IPromptDialogService promptDialogService)
    {
        _promptDialogService = promptDialogService;
    }

    public async Task InjectSnippetAsync(string template, IntPtr targetHwnd)
    {
        if (string.IsNullOrEmpty(template)) return;

        // 1. Check for interactive prompts
        var promptTokens = PlaceholderParser.ExtractPromptTokens(template);
        Dictionary<string, string>? promptResponses = null;

        if (promptTokens.Count > 0)
        {
            promptResponses = await _promptDialogService.ShowPromptDialogAsync(promptTokens).ConfigureAwait(true);
            if (promptResponses == null)
            {
                _logger.Information("Snippet injection cancelled by user during prompt dialog.");
                return;
            }
        }

        // 2. Evaluate template tokens (static tokens + prompt responses)
        var evaluatedText = await PlaceholderParser.EvaluateAsync(
            template,
            clipboardProvider: () => Task.FromResult(GetClipboardTextSafe()),
            promptResponses: promptResponses).ConfigureAwait(false);

        // 3. Process {cursor} token to extract clean text and caret offset
        var (cleanText, caretOffset) = PlaceholderParser.ProcessCursorPosition(evaluatedText);
        if (string.IsNullOrEmpty(cleanText)) return;

        // 4. Restore focus to target window
        if (targetHwnd != IntPtr.Zero)
        {
            RestoreFocusToWindow(targetHwnd);
            await Task.Delay(60).ConfigureAwait(false);
        }

        // 5. Hybrid text injection
        bool isSingleLine = !cleanText.Contains('\n') && !cleanText.Contains('\r');
        if (isSingleLine && cleanText.Length <= 40)
        {
            _logger.Information("Injecting single-line snippet via SendInput Unicode.");
            SendUnicodeString(cleanText);
        }
        else
        {
            _logger.Information("Injecting multi-line snippet via stashed clipboard sequencing.");
            await InjectViaClipboardSequencingAsync(cleanText).ConfigureAwait(false);
        }

        // 6. Reposition caret if {cursor} was present
        if (caretOffset > 0)
        {
            await Task.Delay(30).ConfigureAwait(false);
            SendLeftArrowKeys(caretOffset);
        }
    }

    private static void RestoreFocusToWindow(IntPtr hWnd)
    {
        uint targetThreadId = NativeMethods.GetWindowThreadProcessId(hWnd, out _);
        uint currentThreadId = NativeMethods.GetCurrentThreadId();

        if (targetThreadId != currentThreadId)
        {
            NativeMethods.AttachThreadInput(currentThreadId, targetThreadId, true);
            NativeMethods.SetForegroundWindow(hWnd);
            NativeMethods.AttachThreadInput(currentThreadId, targetThreadId, false);
        }
        else
        {
            NativeMethods.SetForegroundWindow(hWnd);
        }
    }

    private static void SendUnicodeString(string text)
    {
        var inputs = new NativeMethods.INPUT[text.Length * 2];
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            inputs[i * 2] = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.InputUnion
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = NativeMethods.KEYEVENTF_UNICODE
                    }
                }
            };
            inputs[(i * 2) + 1] = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.InputUnion
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP
                    }
                }
            };
        }

        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private async Task InjectViaClipboardSequencingAsync(string text)
    {
        string? previousText = null;

        // Backup existing clipboard text
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    previousText = Clipboard.GetText();
                }
                Clipboard.SetText(text);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to write snippet text to clipboard.");
            }
        });

        // Simulate Ctrl + V
        SendPasteCommand();

        // Wait for target application to process paste
        await Task.Delay(75).ConfigureAwait(false);

        // Restore original clipboard state
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (previousText != null)
                {
                    Clipboard.SetText(previousText);
                }
                else
                {
                    Clipboard.Clear();
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to restore original clipboard contents.");
            }
        });
    }

    private static void SendPasteCommand()
    {
        var inputs = new NativeMethods.INPUT[4];

        // Ctrl down
        inputs[0] = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.InputUnion { ki = new NativeMethods.KEYBDINPUT { wVk = NativeMethods.VK_CONTROL } }
        };
        // V down
        inputs[1] = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.InputUnion { ki = new NativeMethods.KEYBDINPUT { wVk = NativeMethods.VK_V } }
        };
        // V up
        inputs[2] = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.InputUnion { ki = new NativeMethods.KEYBDINPUT { wVk = NativeMethods.VK_V, dwFlags = NativeMethods.KEYEVENTF_KEYUP } }
        };
        // Ctrl up
        inputs[3] = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.InputUnion { ki = new NativeMethods.KEYBDINPUT { wVk = NativeMethods.VK_CONTROL, dwFlags = NativeMethods.KEYEVENTF_KEYUP } }
        };

        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static void SendLeftArrowKeys(int count)
    {
        var inputs = new NativeMethods.INPUT[count * 2];
        for (int i = 0; i < count; i++)
        {
            inputs[i * 2] = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.InputUnion { ki = new NativeMethods.KEYBDINPUT { wVk = NativeMethods.VK_LEFT } }
            };
            inputs[(i * 2) + 1] = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.InputUnion { ki = new NativeMethods.KEYBDINPUT { wVk = NativeMethods.VK_LEFT, dwFlags = NativeMethods.KEYEVENTF_KEYUP } }
            };
        }

        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static string GetClipboardTextSafe()
    {
        string text = string.Empty;
        if (Application.Current != null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        text = Clipboard.GetText();
                    }
                }
                catch { }
            });
        }
        return text;
    }
}
