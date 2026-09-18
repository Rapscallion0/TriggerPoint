using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Serilog;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;

namespace TriggerPoint.Infrastructure.Services;

public class GitHubUpdateService : IUpdateService
{
    private const string DefaultRepo = "Rapscallion0/TriggerPoint";
    private readonly IConfigRepository _configRepository;
    private readonly HttpClient _httpClient;
    private readonly string _repository;
    private string? _customCurrentVersion;

    public GitHubUpdateService(IConfigRepository configRepository, HttpClient? httpClient = null, string repository = DefaultRepo)
    {
        _configRepository = configRepository;
        _httpClient = httpClient ?? new HttpClient();
        _repository = repository;
    }

    /// <summary>
    /// For testing purposes to simulate different installed versions.
    /// </summary>
    public void SetCurrentVersionForTesting(string version)
    {
        _customCurrentVersion = version;
    }

    public string GetCurrentVersion()
    {
        if (!string.IsNullOrWhiteSpace(_customCurrentVersion))
        {
            return NormalizeVersionString(_customCurrentVersion);
        }

        try
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var infoVerAttr = (AssemblyInformationalVersionAttribute?)
                Attribute.GetCustomAttribute(assembly, typeof(AssemblyInformationalVersionAttribute));
            if (!string.IsNullOrEmpty(infoVerAttr?.InformationalVersion))
            {
                var v = infoVerAttr.InformationalVersion;
                var plusIdx = v.IndexOf('+');
                if (plusIdx > 0) v = v.Substring(0, plusIdx);
                return NormalizeVersionString(v);
            }

            var ver = assembly.GetName().Version;
            if (ver != null)
            {
                return $"{ver.Major}.{ver.Minor}.{ver.Build}";
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to resolve current application version.");
        }

        return "2.0.8";
    }

    public bool ShouldPerformScheduledCheck(AppSettings settings)
    {
        if (settings.UpdateFrequency == UpdateCheckFrequency.ManualOnly)
        {
            return false;
        }

        if (settings.UpdateFrequency == UpdateCheckFrequency.OnStartup)
        {
            return true;
        }

        if (settings.LastUpdateCheckUtc == null)
        {
            return true;
        }

        var elapsed = DateTime.UtcNow - settings.LastUpdateCheckUtc.Value;

        return settings.UpdateFrequency switch
        {
            UpdateCheckFrequency.Daily => elapsed >= TimeSpan.FromHours(23.5),
            UpdateCheckFrequency.Weekly => elapsed >= TimeSpan.FromDays(6.9),
            UpdateCheckFrequency.Monthly => elapsed >= TimeSpan.FromDays(29.5),
            _ => true
        };
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(bool isManualCheck = false, CancellationToken ct = default)
    {
        var currentVersionStr = GetCurrentVersion();
        var result = new UpdateCheckResult
        {
            CurrentVersion = currentVersionStr
        };

        AppSettings settings;
        try
        {
            settings = await _configRepository.LoadSettingsAsync().ConfigureAwait(false);
        }
        catch
        {
            settings = new AppSettings();
        }

        try
        {
            var url = $"https://api.github.com/repos/{_repository}/releases?per_page=30";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("TriggerPoint", currentVersionStr));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                Log.Warning("GitHub update check failed with HTTP {StatusCode}: {ErrorBody}", response.StatusCode, errorBody);
                result.ErrorMessage = response.StatusCode == System.Net.HttpStatusCode.Forbidden
                    ? "GitHub API rate limit reached. Please try again later."
                    : $"Update check failed (HTTP {response.StatusCode}).";
                return result;
            }

            var jsonStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(jsonStream, cancellationToken: ct).ConfigureAwait(false);

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                result.ErrorMessage = "Unexpected response format from GitHub Releases.";
                return result;
            }

            var parsedReleases = new List<UpdateInfo>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.TryGetProperty("draft", out var draftProp) && draftProp.GetBoolean())
                {
                    continue;
                }

                bool isPreRelease = element.TryGetProperty("prerelease", out var preProp) && preProp.GetBoolean();
                if (isPreRelease && !settings.IncludePreReleases)
                {
                    continue;
                }

                var tagName = element.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
                var normalizedVer = NormalizeVersionString(tagName);
                if (string.IsNullOrWhiteSpace(normalizedVer))
                {
                    continue;
                }

                var title = element.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? tagName : tagName;
                var body = element.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
                var htmlUrl = element.TryGetProperty("html_url", out var htmlProp) ? htmlProp.GetString() ?? "" : "";
                DateTimeOffset publishedAt = element.TryGetProperty("published_at", out var pubProp) && pubProp.TryGetDateTimeOffset(out var dt) ? dt : DateTimeOffset.UtcNow;

                string downloadUrl = "";
                string fileName = "TriggerPointSetup.exe";
                long fileSizeBytes = 0;

                if (element.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assetsProp.EnumerateArray())
                    {
                        var assetName = asset.TryGetProperty("name", out var anProp) ? anProp.GetString() ?? "" : "";
                        if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                            assetName.Equals("TriggerPointSetup.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.TryGetProperty("browser_download_url", out var bdlProp) ? bdlProp.GetString() ?? "" : "";
                            fileName = assetName;
                            if (asset.TryGetProperty("size", out var sizeProp))
                            {
                                fileSizeBytes = sizeProp.GetInt64();
                            }
                            break;
                        }
                    }
                }

                parsedReleases.Add(new UpdateInfo
                {
                    Version = normalizedVer,
                    TagName = tagName,
                    Title = string.IsNullOrWhiteSpace(title) ? tagName : title,
                    ReleaseNotes = body,
                    PublishedAt = publishedAt,
                    DownloadUrl = downloadUrl,
                    FileName = fileName,
                    FileSizeBytes = fileSizeBytes,
                    HtmlUrl = htmlUrl,
                    IsPreRelease = isPreRelease
                });
            }

            // Sort releases descending by SemVer
            parsedReleases.Sort((a, b) => CompareVersions(b.Version, a.Version));

            if (parsedReleases.Count == 0)
            {
                result.IsUpdateAvailable = false;
                return result;
            }

            var latest = parsedReleases[0];

            // Compare latest version against current version
            if (CompareVersions(latest.Version, currentVersionStr) > 0)
            {
                result.IsUpdateAvailable = true;
                result.LatestUpdate = latest;

                // Collect all intermediate releases strictly newer than current version
                var newerReleases = parsedReleases.Where(r => CompareVersions(r.Version, currentVersionStr) > 0).ToList();
                result.IntermediateReleases = newerReleases;
                result.CombinedChangelog = BuildCombinedChangelog(newerReleases);

                if (!string.IsNullOrEmpty(settings.IgnoredUpdateVersion) &&
                    string.Equals(NormalizeVersionString(settings.IgnoredUpdateVersion), latest.Version, StringComparison.OrdinalIgnoreCase))
                {
                    result.IsIgnored = true;
                }
            }
            else
            {
                result.IsUpdateAvailable = false;
                result.LatestUpdate = latest;
            }

            // Update settings with last check timestamp and version found
            settings.LastUpdateCheckUtc = DateTime.UtcNow;
            settings.LastVersionFound = latest.Version;
            try
            {
                await _configRepository.SaveSettingsAsync(settings).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to save updated settings after update check.");
            }

            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while checking for updates.");
            result.ErrorMessage = $"Failed to check for updates: {ex.Message}";
            return result;
        }
    }

    public async Task<string> DownloadUpdateAsync(UpdateInfo updateInfo, IProgress<UpdateDownloadProgress>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(updateInfo.DownloadUrl))
        {
            throw new InvalidOperationException("No download URL available for this update.");
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "TriggerPoint", "Updates");
        Directory.CreateDirectory(tempDir);

        var destinationFile = Path.Combine(tempDir, $"TriggerPointSetup-{updateInfo.Version}.exe");

        // If file already exists and matches expected size, reuse it
        if (File.Exists(destinationFile) && updateInfo.FileSizeBytes > 0)
        {
            var fi = new FileInfo(destinationFile);
            if (fi.Length == updateInfo.FileSizeBytes)
            {
                progress?.Report(new UpdateDownloadProgress
                {
                    BytesReceived = fi.Length,
                    TotalBytes = fi.Length,
                    BytesPerSecond = 0
                });
                return destinationFile;
            }
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, updateInfo.DownloadUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("TriggerPoint", GetCurrentVersion()));

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? updateInfo.FileSizeBytes;

        var partFile = destinationFile + ".download";
        if (File.Exists(partFile))
        {
            try { File.Delete(partFile); } catch { }
        }

        var stopwatch = Stopwatch.StartNew();
        long totalBytesRead = 0;
        long lastBytesSample = 0;
        var lastSampleTime = stopwatch.Elapsed;

        await using (var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var fileStream = new FileStream(partFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            var buffer = new byte[81920];
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, ct).ConfigureAwait(false);
                totalBytesRead += bytesRead;

                var elapsed = stopwatch.Elapsed;
                double bytesPerSec = 0;
                var sampleElapsed = elapsed - lastSampleTime;
                if (sampleElapsed.TotalSeconds >= 0.4)
                {
                    bytesPerSec = (totalBytesRead - lastBytesSample) / sampleElapsed.TotalSeconds;
                    lastBytesSample = totalBytesRead;
                    lastSampleTime = elapsed;
                }

                progress?.Report(new UpdateDownloadProgress
                {
                    BytesReceived = totalBytesRead,
                    TotalBytes = totalBytes,
                    BytesPerSecond = bytesPerSec
                });
            }
        }

        if (File.Exists(destinationFile))
        {
            try { File.Delete(destinationFile); } catch { }
        }

        File.Move(partFile, destinationFile, true);

        progress?.Report(new UpdateDownloadProgress
        {
            BytesReceived = totalBytesRead,
            TotalBytes = totalBytesRead,
            BytesPerSecond = 0
        });

        return destinationFile;
    }

    public void LaunchInstallerAndExit(string installerPath, bool silent = true)
    {
        if (!File.Exists(installerPath))
        {
            throw new FileNotFoundException("Installer executable not found.", installerPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = silent ? "/SILENT /SUPPRESSMSGBOXES /NORESTART" : "",
            UseShellExecute = true
        };

        Log.Information("Launching installer at {InstallerPath} with args: '{Args}'", installerPath, startInfo.Arguments);
        Process.Start(startInfo);

        // Terminate TriggerPoint cleanly
        if (Application.Current != null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Application.Current.Shutdown();
            });
        }
        else
        {
            Environment.Exit(0);
        }
    }

    public static string NormalizeVersionString(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return "";
        var v = version.Trim();
        if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            v = v.Substring(1);
        }
        var plusIdx = v.IndexOf('+');
        if (plusIdx >= 0) v = v.Substring(0, plusIdx);

        var dashIdx = v.IndexOf('-');
        if (dashIdx >= 0) v = v.Substring(0, dashIdx);

        return v.Trim();
    }

    public static int CompareVersions(string v1, string v2)
    {
        var norm1 = NormalizeVersionString(v1);
        var norm2 = NormalizeVersionString(v2);

        if (Version.TryParse(norm1, out var parsed1) && Version.TryParse(norm2, out var parsed2))
        {
            return parsed1.CompareTo(parsed2);
        }

        var p1 = norm1.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToList();
        var p2 = norm2.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToList();

        while (p1.Count < 3) p1.Add(0);
        while (p2.Count < 3) p2.Add(0);

        for (int i = 0; i < Math.Max(p1.Count, p2.Count); i++)
        {
            int val1 = i < p1.Count ? p1[i] : 0;
            int val2 = i < p2.Count ? p2[i] : 0;
            if (val1 != val2)
            {
                return val1.CompareTo(val2);
            }
        }

        return 0;
    }

    public static string BuildCombinedChangelog(IEnumerable<UpdateInfo> releases)
    {
        var sb = new StringBuilder();
        foreach (var r in releases)
        {
            sb.AppendLine($"### {r.Title} ({r.PublishedAt:MMM dd, yyyy})");
            if (!string.IsNullOrWhiteSpace(r.ReleaseNotes))
            {
                sb.AppendLine(r.ReleaseNotes.Trim());
            }
            else
            {
                sb.AppendLine("Maintenance release with performance and stability improvements.");
            }
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}
