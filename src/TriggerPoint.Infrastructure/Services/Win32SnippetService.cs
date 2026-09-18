using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using Serilog;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
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

    public async Task InjectSnippetAsync(
        string template, 
        IntPtr targetHwnd, 
        SnippetContentType contentType = SnippetContentType.PlainText, 
        string? rtfContent = null)
    {
        if (string.IsNullOrEmpty(template) && string.IsNullOrEmpty(rtfContent)) return;

        // Rich Text Injection Path
        if (contentType == SnippetContentType.RichText && !string.IsNullOrWhiteSpace(rtfContent))
        {
            await InjectRichSnippetAsync(template, rtfContent, targetHwnd).ConfigureAwait(false);
            return;
        }

        // Plain Text Injection Path
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

        // 2. Evaluate template tokens (static tokens + prompt responses + context)
        string? activeWinTitle = null;
        string? activeProcName = null;
        if (targetHwnd != IntPtr.Zero)
        {
            activeWinTitle = NativeMethods.GetWindowTitle(targetHwnd);
            activeProcName = NativeMethods.GetProcessNameForWindow(targetHwnd);
        }

        var evaluatedText = await PlaceholderParser.EvaluateAsync(
            template,
            clipboardProvider: () => Task.FromResult(GetClipboardTextSafe()),
            promptResponses: promptResponses,
            activeWindowTitle: activeWinTitle,
            activeProcessName: activeProcName).ConfigureAwait(false);

        // 3. Process {cursor} token to extract clean text and caret offset
        var (cleanText, caretOffset) = PlaceholderParser.ProcessCursorPosition(evaluatedText);
        if (string.IsNullOrEmpty(cleanText)) return;

        // 4. Restore focus to target window
        if (targetHwnd != IntPtr.Zero)
        {
            await RestoreFocusToWindowAsync(targetHwnd).ConfigureAwait(false);
        }

        // Ensure no lingering modifier keys interfere with injection
        ReleaseLingeringModifiers();

        // 5. Hybrid text injection
        // Text up to 250 characters can be typed directly and instantly without touching clipboard
        if (cleanText.Length <= 250)
        {
            _logger.Information("Injecting snippet ({Length} chars) via SendInput Unicode.", cleanText.Length);
            SendUnicodeString(cleanText);
        }
        else
        {
            _logger.Information("Injecting large snippet ({Length} chars) via stashed clipboard sequencing.", cleanText.Length);
            await InjectViaClipboardSequencingAsync(cleanText).ConfigureAwait(false);
        }

        // 6. Reposition caret if {cursor} was present
        if (caretOffset > 0)
        {
            await Task.Delay(40).ConfigureAwait(false);
            SendLeftArrowKeys(caretOffset);
        }
    }

    private async Task InjectRichSnippetAsync(string plainFallback, string rtf, IntPtr targetHwnd)
    {
        // 1. Check for interactive prompts
        var promptTokens = PlaceholderParser.ExtractPromptTokens(plainFallback);
        Dictionary<string, string>? promptResponses = null;

        if (promptTokens.Count > 0)
        {
            promptResponses = await _promptDialogService.ShowPromptDialogAsync(promptTokens).ConfigureAwait(true);
            if (promptResponses == null)
            {
                _logger.Information("Rich snippet injection cancelled by user during prompt dialog.");
                return;
            }
        }

        // 2. Context inspection
        string? activeWinTitle = null;
        string? activeProcName = null;
        if (targetHwnd != IntPtr.Zero)
        {
            activeWinTitle = NativeMethods.GetWindowTitle(targetHwnd);
            activeProcName = NativeMethods.GetProcessNameForWindow(targetHwnd);
        }

        // 3. Evaluate FlowDocument tokens
        var (evalRtf, evalHtml, evalPlain, caretOffset) = RichTextService.EvaluateFlowDocument(
            rtf,
            plainFallback,
            promptResponses,
            () => Task.FromResult(GetClipboardTextSafe()),
            DateTime.Now,
            activeWinTitle,
            activeProcName);

        // 4. Restore focus to target window
        if (targetHwnd != IntPtr.Zero)
        {
            await RestoreFocusToWindowAsync(targetHwnd).ConfigureAwait(false);
        }

        // Ensure no lingering modifier keys interfere with injection
        ReleaseLingeringModifiers();

        // 5. Inject via multi-format clipboard sequencing
        _logger.Information("Injecting rich text snippet ({PlainLen} chars) via multi-format clipboard sequencing.", evalPlain.Length);
        await InjectViaClipboardRichSequencingAsync(evalRtf, evalHtml, evalPlain).ConfigureAwait(false);

        // 6. Reposition caret if {cursor} was present
        if (caretOffset > 0)
        {
            await Task.Delay(40).ConfigureAwait(false);
            SendLeftArrowKeys(caretOffset);
        }
    }

    private static async Task RestoreFocusToWindowAsync(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;

        NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);

        uint targetThreadId = NativeMethods.GetWindowThreadProcessId(hWnd, out _);
        uint currentThreadId = NativeMethods.GetCurrentThreadId();

        if (targetThreadId != currentThreadId)
        {
            NativeMethods.AttachThreadInput(currentThreadId, targetThreadId, true);
            NativeMethods.BringWindowToTop(hWnd);
            NativeMethods.SetForegroundWindow(hWnd);
            NativeMethods.AttachThreadInput(currentThreadId, targetThreadId, false);
        }
        else
        {
            NativeMethods.BringWindowToTop(hWnd);
            NativeMethods.SetForegroundWindow(hWnd);
        }

        // Poll briefly until the target window becomes foreground or timeout
        for (int i = 0; i < 6; i++)
        {
            if (NativeMethods.GetForegroundWindow() == hWnd)
                break;
            await Task.Delay(25).ConfigureAwait(false);
        }

        // Brief delay to let the target application stabilize focus
        await Task.Delay(50).ConfigureAwait(false);
    }

    private void ReleaseLingeringModifiers()
    {
        var modifiers = new byte[] { NativeMethods.VK_CONTROL, NativeMethods.VK_SHIFT, NativeMethods.VK_MENU, NativeMethods.VK_LWIN, NativeMethods.VK_RWIN };
        var inputs = new NativeMethods.INPUT[modifiers.Length];
        for (int i = 0; i < modifiers.Length; i++)
        {
            inputs[i] = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.InputUnion
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = modifiers[i],
                        dwFlags = NativeMethods.KEYEVENTF_KEYUP
                    }
                }
            };
        }
        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent == 0)
        {
            int err = Marshal.GetLastWin32Error();
            _logger.Warning("ReleaseLingeringModifiers SendInput failed with error {Err}", err);
        }
    }

    private void SendUnicodeString(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var inputList = new List<NativeMethods.INPUT>();

        for (int i = 0; i < normalized.Length; i++)
        {
            char c = normalized[i];
            if (c == '\n')
            {
                // Send Enter key
                inputList.Add(new NativeMethods.INPUT
                {
                    type = NativeMethods.INPUT_KEYBOARD,
                    u = new NativeMethods.InputUnion
                    {
                        ki = new NativeMethods.KEYBDINPUT { wVk = NativeMethods.VK_RETURN, dwFlags = 0 }
                    }
                });
                inputList.Add(new NativeMethods.INPUT
                {
                    type = NativeMethods.INPUT_KEYBOARD,
                    u = new NativeMethods.InputUnion
                    {
                        ki = new NativeMethods.KEYBDINPUT { wVk = NativeMethods.VK_RETURN, dwFlags = NativeMethods.KEYEVENTF_KEYUP }
                    }
                });
            }
            else
            {
                inputList.Add(new NativeMethods.INPUT
                {
                    type = NativeMethods.INPUT_KEYBOARD,
                    u = new NativeMethods.InputUnion
                    {
                        ki = new NativeMethods.KEYBDINPUT { wVk = 0, wScan = c, dwFlags = NativeMethods.KEYEVENTF_UNICODE }
                    }
                });
                inputList.Add(new NativeMethods.INPUT
                {
                    type = NativeMethods.INPUT_KEYBOARD,
                    u = new NativeMethods.InputUnion
                    {
                        ki = new NativeMethods.KEYBDINPUT { wVk = 0, wScan = c, dwFlags = NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP }
                    }
                });
            }
        }

        var inputs = inputList.ToArray();
        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent == 0)
        {
            int err = Marshal.GetLastWin32Error();
            _logger.Error("SendUnicodeString SendInput failed with error code {ErrorCode} for {Count} input events.", err, inputs.Length);
        }
        else
        {
            _logger.Information("SendUnicodeString successfully emitted {SentCount} of {TotalCount} input events.", sent, inputs.Length);
        }
    }

    private async Task InjectViaClipboardSequencingAsync(string text)
    {
        string? previousText = null;

        // Backup existing clipboard text with retry
        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        previousText = Clipboard.GetText();
                    }
                    Clipboard.SetText(text);
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt == 2)
                    {
                        _logger.Warning(ex, "Failed to write snippet text to clipboard after 3 attempts.");
                    }
                    await Task.Delay(25);
                }
            }
        });

        // Simulate Ctrl + V
        SendPasteCommand();

        // Allow target application sufficient time to process paste
        await Task.Delay(500).ConfigureAwait(false);

        // Restore original clipboard state if there was one (do not clear if user had empty clipboard)
        if (previousText != null)
        {
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        Clipboard.SetText(previousText);
                        return;
                    }
                    catch (Exception ex)
                    {
                        if (attempt == 2)
                        {
                            _logger.Warning(ex, "Failed to restore original clipboard contents after 3 attempts.");
                        }
                        await Task.Delay(25);
                    }
                }
            });
        }
    }

    private async Task InjectViaClipboardRichSequencingAsync(string rtf, string html, string plainText)
    {
        string? previousText = null;

        // Backup existing clipboard text with retry and set rich data object
        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        previousText = Clipboard.GetText();
                    }

                    var dataObject = new DataObject();
                    if (!string.IsNullOrEmpty(rtf))
                    {
                        dataObject.SetData(DataFormats.Rtf, rtf);
                    }
                    if (!string.IsNullOrEmpty(html))
                    {
                        dataObject.SetData(DataFormats.Html, html);
                    }
                    if (!string.IsNullOrEmpty(plainText))
                    {
                        dataObject.SetData(DataFormats.UnicodeText, plainText);
                        dataObject.SetData(DataFormats.Text, plainText);
                    }

                    Clipboard.SetDataObject(dataObject, true);
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt == 2)
                    {
                        _logger.Warning(ex, "Failed to write rich snippet data to clipboard after 3 attempts.");
                    }
                    await Task.Delay(25);
                }
            }
        });

        // Simulate Ctrl + V
        SendPasteCommand();

        // Allow target application sufficient time to process paste
        await Task.Delay(500).ConfigureAwait(false);

        // Restore original clipboard state if there was one
        if (previousText != null)
        {
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        Clipboard.SetText(previousText);
                        return;
                    }
                    catch (Exception ex)
                    {
                        if (attempt == 2)
                        {
                            _logger.Warning(ex, "Failed to restore original clipboard contents after 3 attempts.");
                        }
                        await Task.Delay(25);
                    }
                }
            });
        }
    }

    private void SendPasteCommand()
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

        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent == 0)
        {
            int err = Marshal.GetLastWin32Error();
            _logger.Error("SendPasteCommand SendInput failed with error code {ErrorCode}", err);
        }
        else
        {
            _logger.Information("SendPasteCommand successfully sent {SentCount} inputs.", sent);
        }
    }

    private void SendLeftArrowKeys(int count)
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

        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent == 0)
        {
            int err = Marshal.GetLastWin32Error();
            _logger.Warning("SendLeftArrowKeys SendInput failed with error code {ErrorCode}", err);
        }
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
