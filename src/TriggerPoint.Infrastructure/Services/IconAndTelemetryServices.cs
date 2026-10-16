using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Linq;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Infrastructure.Win32;

using System.Windows.Media;
using TriggerPoint.Core.Models;

namespace TriggerPoint.Infrastructure.Services;

public class Win32IconService : IIconService
{
    private readonly ConcurrentDictionary<string, string> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, ImageSource?> _imageSourceCache = new(StringComparer.OrdinalIgnoreCase);

    public Task<string?> ResolveIconPathAsync(string? iconPath, string? fallbackCommand)
    {
        return Task.Run(() =>
        {
            var target = !string.IsNullOrWhiteSpace(iconPath) ? iconPath : fallbackCommand;
            if (string.IsNullOrWhiteSpace(target)) return null;

            var expanded = Environment.ExpandEnvironmentVariables(target.Trim());

            // If it's already an image or icon file that exists
            if (File.Exists(expanded))
            {
                var ext = Path.GetExtension(expanded).ToLowerInvariant();
                if (ext is ".ico" or ".png" or ".jpg" or ".jpeg")
                {
                    return expanded;
                }
            }

            return expanded;
        });
    }

    public string? ResolveExecutablePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var extracted = ExtractExecutablePath(command);
        return ResolveTargetExecutable(extracted);
    }

    public static ImageSource? GetActionIcon(TriggerItem? item)
    {
        if (item == null || item.ActionType != ActionType.Shell) return null;

        // 1. Explicit IconPath
        if (!string.IsNullOrWhiteSpace(item.IconPath))
        {
            var fromPath = GetIconFromPath(item.IconPath);
            if (fromPath != null) return fromPath;
        }

        // 2. Command payload
        if (!string.IsNullOrWhiteSpace(item.Payload?.Command))
        {
            return GetIconFromCommand(item.Payload.Command);
        }

        return null;
    }

    public static ImageSource? GetIconFromCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;

        return _imageSourceCache.GetOrAdd(command, cmd =>
        {
            var target = ExtractExecutablePath(cmd);
            if (string.IsNullOrWhiteSpace(target)) return null;
            return GetIconFromPath(target);
        });
    }

    public static ImageSource? GetIconFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var resolved = ResolveTargetExecutable(path);
        if (resolved == null)
        {
            var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
            if (!File.Exists(expanded) && !Directory.Exists(expanded))
            {
                return null;
            }
            resolved = expanded;
        }

        return _imageSourceCache.GetOrAdd(resolved, p =>
        {
            try
            {
                if (File.Exists(p))
                {
                    var ext = Path.GetExtension(p).ToLowerInvariant();
                    if (ext is ".png" or ".jpg" or ".jpeg")
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.UriSource = new Uri(p, UriKind.Absolute);
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.EndInit();
                        bi.Freeze();
                        return bi;
                    }
                }

                return ExtractAssociatedIcon(p);
            }
            catch
            {
                return null;
            }
        });
    }

    public static string? ExtractExecutablePath(string rawCommand)
    {
        if (string.IsNullOrWhiteSpace(rawCommand)) return null;
        var trimmed = rawCommand.Trim();

        // 1. Handle quoted commands (e.g. "C:\Program Files\App\app.exe" --arg)
        if (trimmed.StartsWith("\""))
        {
            int nextQuote = trimmed.IndexOf('\"', 1);
            if (nextQuote > 1)
            {
                return trimmed.Substring(1, nextQuote - 1).Trim();
            }
            return trimmed.Trim('\"');
        }

        // 2. Check if entire command as-is is an existing file or directory
        var expandedFull = Environment.ExpandEnvironmentVariables(trimmed);
        if (File.Exists(expandedFull) || Directory.Exists(expandedFull))
        {
            return trimmed;
        }

        // 3. Take first token before whitespace for commands with arguments
        int spaceIndex = trimmed.IndexOf(' ');
        return spaceIndex > 0 ? trimmed.Substring(0, spaceIndex) : trimmed;
    }

    public static string? ResolveTargetExecutable(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return null;

        var expanded = Environment.ExpandEnvironmentVariables(candidate.Trim());

        // 1. Direct file check
        if (File.Exists(expanded)) return expanded;

        // 2. Relative to AppContext or BaseDirectory
        if (!Path.IsPathRooted(expanded))
        {
            var appRel = Path.Combine(AppContext.BaseDirectory, expanded);
            if (File.Exists(appRel)) return appRel;
        }

        // 3. Search %PATH%
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        var extensions = new[] { "", ".exe", ".cmd", ".bat", ".lnk" };

        if (!string.IsNullOrEmpty(pathEnv) && !Path.IsPathRooted(expanded))
        {
            var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            foreach (var dir in paths)
            {
                foreach (var ext in extensions)
                {
                    try
                    {
                        var testPath = Path.Combine(dir, expanded.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? expanded : expanded + ext);
                        if (File.Exists(testPath))
                        {
                            return testPath;
                        }
                    }
                    catch { }
                }
            }
        }

        // 4. Check Windows System32
        try
        {
            var sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            foreach (var ext in extensions)
            {
                var testPath = Path.Combine(sys32, expanded.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? expanded : expanded + ext);
                if (File.Exists(testPath)) return testPath;
            }
        }
        catch { }

        // 5. Windows App Paths Registry
        try
        {
            string appName = Path.GetFileName(expanded);
            if (!appName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                appName += ".exe";
            }

            using var hklmKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{appName}");
            var hklmVal = hklmKey?.GetValue(null) as string;
            if (!string.IsNullOrEmpty(hklmVal))
            {
                var expHklm = Environment.ExpandEnvironmentVariables(hklmVal.Trim().Trim('\"'));
                if (File.Exists(expHklm)) return expHklm;
            }

            using var hkcuKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{appName}");
            var hkcuVal = hkcuKey?.GetValue(null) as string;
            if (!string.IsNullOrEmpty(hkcuVal))
            {
                var expHkcu = Environment.ExpandEnvironmentVariables(hkcuVal.Trim().Trim('\"'));
                if (File.Exists(expHkcu)) return expHkcu;
            }
        }
        catch { }

        return null;
    }

    public static BitmapSource? ExtractAssociatedIcon(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());

        try
        {
            var shinfo = new NativeMethods.SHFILEINFO();
            var flags = NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_SMALLICON;

            if (!File.Exists(expanded) && !Directory.Exists(expanded))
            {
                flags |= NativeMethods.SHGFI_USEFILEATTRIBUTES;
            }

            var res = NativeMethods.SHGetFileInfo(
                expanded,
                NativeMethods.FILE_ATTRIBUTE_NORMAL,
                ref shinfo,
                (uint)System.Runtime.InteropServices.Marshal.SizeOf(shinfo),
                flags);

            if (res != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
            {
                try
                {
                    var bs = Imaging.CreateBitmapSourceFromHIcon(
                        shinfo.hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    bs.Freeze();
                    return bs;
                }
                finally
                {
                    NativeMethods.DestroyIcon(shinfo.hIcon);
                }
            }
        }
        catch { }

        return null;
    }
}

public class TelemetryService : ITelemetryService
{
    private readonly IConfigRepository _repository;

    public TelemetryService(IConfigRepository repository)
    {
        _repository = repository;
    }

    public async Task RecordExecutionAsync(Guid itemId)
    {
        try
        {
            var items = await _repository.LoadAsync().ConfigureAwait(false);
            var item = items.FirstOrDefault(x => x.Id == itemId);
            if (item != null)
            {
                item.UsageStats.LaunchCount++;
                item.UsageStats.LastExecutedUtc = DateTime.UtcNow;
                await _repository.SaveAsync(items).ConfigureAwait(false);
            }
        }
        catch { }
    }
}
