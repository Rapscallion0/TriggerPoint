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

namespace TriggerPoint.Infrastructure.Services;

public class Win32IconService : IIconService
{
    private readonly ConcurrentDictionary<string, string> _iconCache = new(StringComparer.OrdinalIgnoreCase);

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
