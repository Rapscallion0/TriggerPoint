using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using TriggerPoint.Core.Models;

namespace TriggerPoint.Infrastructure.Win32;

public static class ShellLinkScanner
{
    private const byte HOTKEYF_SHIFT = 0x01;
    private const byte HOTKEYF_CONTROL = 0x02;
    private const byte HOTKEYF_ALT = 0x04;
    private const byte HOTKEYF_EXT = 0x08;

    public static async Task<string?> FindConflictingShortcutAsync(ShortcutBinding binding)
    {
        return await Task.Run(() =>
        {
            try
            {
                var searchDirs = new List<string>
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\Start Menu\Programs")
                };

                // Convert binding to IShellLink hotkey format
                ushort targetHotkey = ToShellLinkHotkey(binding);
                if (targetHotkey == 0) return null;

                foreach (var dir in searchDirs)
                {
                    if (!Directory.Exists(dir)) continue;

                    var lnkFiles = Directory.GetFiles(dir, "*.lnk", SearchOption.AllDirectories);
                    foreach (var file in lnkFiles)
                    {
                        var hotkey = GetShortcutHotkey(file);
                        if (hotkey == targetHotkey)
                        {
                            var appName = Path.GetFileNameWithoutExtension(file);
                            return appName;
                        }
                    }
                }
            }
            catch
            {
                // Heuristic probe is best-effort and non-fatal
            }

            return null;
        }).ConfigureAwait(false);
    }

    public static (string TargetPath, string Arguments, string WorkingDirectory, string Description)? ResolveShortcut(string lnkPath)
    {
        if (!File.Exists(lnkPath) || !lnkPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            return null;

        var link = (NativeMethods.IShellLinkW)new NativeMethods.ShellLink();
        try
        {
            var persist = (NativeMethods.IPersistFile)link;
            persist.Load(lnkPath, 0);

            var pathSb = new StringBuilder(260);
            link.GetPath(pathSb, pathSb.Capacity, IntPtr.Zero, 0);

            var argsSb = new StringBuilder(260);
            link.GetArguments(argsSb, argsSb.Capacity);

            var dirSb = new StringBuilder(260);
            link.GetWorkingDirectory(dirSb, dirSb.Capacity);

            var descSb = new StringBuilder(260);
            link.GetDescription(descSb, descSb.Capacity);

            return (pathSb.ToString(), argsSb.ToString(), dirSb.ToString(), descSb.ToString());
        }
        catch
        {
            return null;
        }
        finally
        {
            if (Marshal.IsComObject(link))
            {
                Marshal.ReleaseComObject(link);
            }
        }
    }

    private static ushort ToShellLinkHotkey(ShortcutBinding binding)
    {
        byte vk = (byte)binding.VirtualKey;
        byte mods = 0;

        if (binding.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= HOTKEYF_SHIFT;
        if (binding.Modifiers.HasFlag(ModifierKeys.Control)) mods |= HOTKEYF_CONTROL;
        if (binding.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= HOTKEYF_ALT;
        if (binding.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= HOTKEYF_EXT;

        return (ushort)((mods << 8) | vk);
    }

    private static ushort GetShortcutHotkey(string filePath)
    {
        var link = (NativeMethods.IShellLinkW)new NativeMethods.ShellLink();
        try
        {
            var persist = (NativeMethods.IPersistFile)link;
            persist.Load(filePath, 0); // STGM_READ = 0
            link.GetHotkey(out var hotkey);
            return hotkey;
        }
        catch
        {
            return 0;
        }
        finally
        {
            if (Marshal.IsComObject(link))
            {
                Marshal.ReleaseComObject(link);
            }
        }
    }
}
