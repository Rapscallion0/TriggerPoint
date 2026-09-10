using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;
using Serilog;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;

namespace TriggerPoint.Infrastructure.Services;

public class BrowserDetectionService : IBrowserDetectionService
{
    private static readonly ILogger _logger = Log.ForContext<BrowserDetectionService>();

    private List<BrowserInfo>? _cachedBrowsers;

    public IReadOnlyList<BrowserInfo> GetInstalledBrowsers()
    {
        if (_cachedBrowsers != null)
        {
            return _cachedBrowsers;
        }

        var browsers = new Dictionary<string, BrowserInfo>(StringComparer.OrdinalIgnoreCase);

        // 1. Scan standard registry keys for registered Windows browsers
        ScanRegistryBrowsers(Registry.CurrentUser, browsers);
        ScanRegistryBrowsers(Registry.LocalMachine, browsers);

        // 2. Scan standard well-known executable paths if not already discovered
        ScanWellKnownPaths(browsers);

        // 3. For each discovered browser, enumerate profiles
        foreach (var browser in browsers.Values)
        {
            browser.Profiles = DiscoverProfiles(browser);
        }

        _cachedBrowsers = browsers.Values.OrderBy(b => b.Name).ToList();
        return _cachedBrowsers;
    }

    public IReadOnlyList<BrowserProfileInfo> GetProfiles(string browserId)
    {
        if (string.IsNullOrWhiteSpace(browserId) || browserId.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var browsers = GetInstalledBrowsers();
        var match = browsers.FirstOrDefault(b => b.Id.Equals(browserId, StringComparison.OrdinalIgnoreCase));
        return match?.Profiles ?? [];
    }

    public bool LaunchUrl(string url, string? browserId = null, string? profileId = null, bool newWindow = false)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        // Ensure URL has a protocol
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("file://", StringComparison.OrdinalIgnoreCase) &&
            !url.Contains("://"))
        {
            url = "https://" + url;
        }

        if (string.IsNullOrWhiteSpace(browserId) || browserId.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return LaunchDefaultBrowser(url, newWindow);
        }

        var browsers = GetInstalledBrowsers();
        var browser = browsers.FirstOrDefault(b => b.Id.Equals(browserId, StringComparison.OrdinalIgnoreCase));
        if (browser == null || string.IsNullOrWhiteSpace(browser.ExecutablePath) || !File.Exists(browser.ExecutablePath))
        {
            _logger.Warning("Targeted browser '{BrowserId}' not found. Falling back to default browser for URL: {Url}", browserId, url);
            return LaunchDefaultBrowser(url, newWindow);
        }

        try
        {
            string arguments;
            if (browser.Kind == BrowserKind.Firefox)
            {
                var winArg = newWindow ? "-new-window " : "";
                arguments = !string.IsNullOrWhiteSpace(profileId)
                    ? $"{winArg}-P \"{profileId}\" \"{url}\""
                    : $"{winArg}\"{url}\"";
            }
            else // Chromium or generic
            {
                var winArg = newWindow ? "--new-window " : "";
                arguments = !string.IsNullOrWhiteSpace(profileId)
                    ? $"{winArg}--profile-directory=\"{profileId}\" \"{url}\""
                    : $"{winArg}\"{url}\"";
            }

            var psi = new ProcessStartInfo
            {
                FileName = browser.ExecutablePath,
                Arguments = arguments,
                UseShellExecute = false
            };

            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to launch targeted browser '{BrowserId}'. Falling back to default.", browserId);
            return LaunchDefaultBrowser(url, newWindow);
        }
    }

    private static bool LaunchDefaultBrowser(string url, bool newWindow = false)
    {
        try
        {
            if (newWindow)
            {
                var defaultExe = GetDefaultBrowserExecutable();
                if (!string.IsNullOrWhiteSpace(defaultExe) && File.Exists(defaultExe))
                {
                    bool isFirefox = defaultExe.Contains("firefox", StringComparison.OrdinalIgnoreCase);
                    var arg = isFirefox ? $"-new-window \"{url}\"" : $"--new-window \"{url}\"";
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = defaultExe,
                        Arguments = arg,
                        UseShellExecute = false
                    });
                    return true;
                }
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to launch default browser for URL: {Url}", url);
            return false;
        }
    }

    private static string? GetDefaultBrowserExecutable()
    {
        try
        {
            using var userChoice = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice")
                ?? Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice");
            var progId = userChoice?.GetValue("ProgId") as string;
            if (!string.IsNullOrWhiteSpace(progId))
            {
                using var cmdKey = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
                var cmd = cmdKey?.GetValue(null) as string;
                if (!string.IsNullOrWhiteSpace(cmd))
                {
                    if (cmd.StartsWith('"'))
                    {
                        int end = cmd.IndexOf('"', 1);
                        if (end > 1) return cmd[1..end];
                    }
                    else
                    {
                        var parts = cmd.Split(' ');
                        return parts[0];
                    }
                }
            }
        }
        catch { }
        return null;
    }

    private static void ScanRegistryBrowsers(RegistryKey rootKey, Dictionary<string, BrowserInfo> browsers)
    {
        try
        {
            using var internetClients = rootKey.OpenSubKey(@"SOFTWARE\Clients\StartMenuInternet");
            if (internetClients == null) return;

            foreach (var subKeyName in internetClients.GetSubKeyNames())
            {
                using var clientKey = internetClients.OpenSubKey(subKeyName);
                if (clientKey == null) continue;

                var displayName = clientKey.GetValue(null) as string ?? subKeyName;
                using var commandKey = clientKey.OpenSubKey(@"shell\open\command");
                var rawCommand = commandKey?.GetValue(null) as string;

                if (string.IsNullOrWhiteSpace(rawCommand)) continue;

                var exePath = ExtractExePath(rawCommand);
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) continue;

                var (id, kind) = ClassifyBrowser(exePath, displayName);
                if (!browsers.ContainsKey(id))
                {
                    browsers[id] = new BrowserInfo
                    {
                        Id = id,
                        Name = displayName,
                        ExecutablePath = exePath,
                        Kind = kind
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Error reading StartMenuInternet registry keys.");
        }
    }

    private static void ScanWellKnownPaths(Dictionary<string, BrowserInfo> browsers)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var candidates = new (string Id, string Name, BrowserKind Kind, string[] Paths)[]
        {
            ("chrome", "Google Chrome", BrowserKind.Chromium,
            [
                Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe")
            ]),
            ("edge", "Microsoft Edge", BrowserKind.Chromium,
            [
                Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe")
            ]),
            ("brave", "Brave Browser", BrowserKind.Chromium,
            [
                Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
                Path.Combine(programFiles, "BraveSoftware", "Brave-Browser", "Application", "brave.exe")
            ]),
            ("firefox", "Mozilla Firefox", BrowserKind.Firefox,
            [
                Path.Combine(programFiles, "Mozilla Firefox", "firefox.exe"),
                Path.Combine(programFilesX86, "Mozilla Firefox", "firefox.exe"),
                Path.Combine(localAppData, "Mozilla Firefox", "firefox.exe")
            ]),
            ("vivaldi", "Vivaldi", BrowserKind.Chromium,
            [
                Path.Combine(localAppData, "Vivaldi", "Application", "vivaldi.exe"),
                Path.Combine(programFiles, "Vivaldi", "Application", "vivaldi.exe")
            ]),
            ("opera", "Opera", BrowserKind.Chromium,
            [
                Path.Combine(localAppData, "Programs", "Opera", "opera.exe"),
                Path.Combine(localAppData, "Programs", "Opera GX", "opera.exe")
            ])
        };

        foreach (var c in candidates)
        {
            if (browsers.ContainsKey(c.Id)) continue;

            foreach (var path in c.Paths)
            {
                if (File.Exists(path))
                {
                    browsers[c.Id] = new BrowserInfo
                    {
                        Id = c.Id,
                        Name = c.Name,
                        ExecutablePath = path,
                        Kind = c.Kind
                    };
                    break;
                }
            }
        }
    }

    private static (string Id, BrowserKind Kind) ClassifyBrowser(string exePath, string displayName)
    {
        var fileName = Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
        var lowerName = displayName.ToLowerInvariant();

        if (fileName.Contains("chrome") || lowerName.Contains("chrome"))
            return ("chrome", BrowserKind.Chromium);

        if (fileName.Contains("msedge") || lowerName.Contains("edge"))
            return ("edge", BrowserKind.Chromium);

        if (fileName.Contains("brave") || lowerName.Contains("brave"))
            return ("brave", BrowserKind.Chromium);

        if (fileName.Contains("firefox") || lowerName.Contains("firefox"))
            return ("firefox", BrowserKind.Firefox);

        if (fileName.Contains("vivaldi") || lowerName.Contains("vivaldi"))
            return ("vivaldi", BrowserKind.Chromium);

        if (fileName.Contains("opera") || lowerName.Contains("opera"))
            return ("opera", BrowserKind.Chromium);

        return (fileName, BrowserKind.Other);
    }

    private static string ExtractExePath(string rawCommand)
    {
        rawCommand = rawCommand.Trim();
        if (rawCommand.StartsWith('\"'))
        {
            var endQuote = rawCommand.IndexOf('\"', 1);
            if (endQuote > 1)
            {
                return rawCommand[1..endQuote];
            }
        }

        var spaceIdx = rawCommand.IndexOf(' ');
        return spaceIdx > 0 ? rawCommand[..spaceIdx] : rawCommand;
    }

    public static List<BrowserProfileInfo> DiscoverProfiles(BrowserInfo browser)
    {
        var profiles = new List<BrowserProfileInfo>();

        try
        {
            if (browser.Kind == BrowserKind.Firefox)
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var iniPath = Path.Combine(appData, "Mozilla", "Firefox", "profiles.ini");
                if (File.Exists(iniPath))
                {
                    profiles.AddRange(ParseFirefoxProfiles(iniPath));
                }
            }
            else if (browser.Kind == BrowserKind.Chromium)
            {
                var userDataDir = GetChromiumUserDataDirectory(browser.Id);
                if (!string.IsNullOrWhiteSpace(userDataDir) && Directory.Exists(userDataDir))
                {
                    profiles.AddRange(ParseChromiumProfiles(userDataDir));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to discover profiles for {BrowserName}", browser.Name);
        }

        return profiles;
    }

    public static string? GetChromiumUserDataDirectory(string browserId)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        return browserId.ToLowerInvariant() switch
        {
            "chrome" => Path.Combine(localAppData, "Google", "Chrome", "User Data"),
            "edge" => Path.Combine(localAppData, "Microsoft", "Edge", "User Data"),
            "brave" => Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data"),
            "vivaldi" => Path.Combine(localAppData, "Vivaldi", "User Data"),
            "opera" => Path.Combine(appData, "Opera Software", "Opera Stable"),
            _ => null
        };
    }

    public static List<BrowserProfileInfo> ParseChromiumProfiles(string userDataDir)
    {
        var profiles = new List<BrowserProfileInfo>();

        var localStatePath = Path.Combine(userDataDir, "Local State");
        if (File.Exists(localStatePath))
        {
            try
            {
                var json = File.ReadAllText(localStatePath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("profile", out var profileObj) &&
                    profileObj.TryGetProperty("info_cache", out var infoCache))
                {
                    foreach (var prop in infoCache.EnumerateObject())
                    {
                        var folderName = prop.Name; // "Default", "Profile 1", etc.
                        string displayName = folderName;

                        if (prop.Value.TryGetProperty("name", out var nameProp) && !string.IsNullOrWhiteSpace(nameProp.GetString()))
                        {
                            var customName = nameProp.GetString()!;
                            displayName = customName.Equals(folderName, StringComparison.OrdinalIgnoreCase)
                                ? folderName
                                : $"{customName} ({folderName})";
                        }

                        profiles.Add(new BrowserProfileInfo
                        {
                            Id = folderName,
                            DisplayName = displayName
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to parse Chromium Local State from {Path}", localStatePath);
            }
        }

        // Fallback or augment if info_cache didn't return profiles
        if (profiles.Count == 0)
        {
            var defaultDir = Path.Combine(userDataDir, "Default");
            if (Directory.Exists(defaultDir))
            {
                profiles.Add(new BrowserProfileInfo { Id = "Default", DisplayName = "Default" });
            }

            try
            {
                foreach (var dir in Directory.GetDirectories(userDataDir, "Profile *"))
                {
                    var name = Path.GetFileName(dir);
                    if (!profiles.Any(p => p.Id.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        profiles.Add(new BrowserProfileInfo { Id = name, DisplayName = name });
                    }
                }
            }
            catch { }
        }

        return profiles;
    }

    public static List<BrowserProfileInfo> ParseFirefoxProfiles(string iniPath)
    {
        var profiles = new List<BrowserProfileInfo>();

        try
        {
            var lines = File.ReadAllLines(iniPath);
            string? currentName = null;
            bool isDefault = false;

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    if (!string.IsNullOrWhiteSpace(currentName))
                    {
                        profiles.Add(new BrowserProfileInfo
                        {
                            Id = currentName,
                            DisplayName = isDefault ? $"{currentName} (Default)" : currentName
                        });
                    }
                    currentName = null;
                    isDefault = false;
                    continue;
                }

                if (line.StartsWith("Name=", StringComparison.OrdinalIgnoreCase))
                {
                    currentName = line[5..].Trim();
                }
                else if (line.StartsWith("Default=1", StringComparison.OrdinalIgnoreCase))
                {
                    isDefault = true;
                }
            }

            if (!string.IsNullOrWhiteSpace(currentName))
            {
                profiles.Add(new BrowserProfileInfo
                {
                    Id = currentName,
                    DisplayName = isDefault ? $"{currentName} (Default)" : currentName
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to parse Firefox profiles.ini at {Path}", iniPath);
        }

        return profiles;
    }
}
