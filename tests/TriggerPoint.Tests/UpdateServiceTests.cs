using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Persistence;
using TriggerPoint.Infrastructure.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class UpdateServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly JsonConfigRepository _repo;

    public UpdateServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "TriggerPoint_UpdateTest_" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
        _repo = new JsonConfigRepository(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch { }
    }

    [Theory]
    [InlineData("2.0.9", "2.0.8", 1)]
    [InlineData("v2.1.0", "2.0.8", 1)]
    [InlineData("2.0.8", "2.0.8", 0)]
    [InlineData("v2.0.8", "v2.0.8", 0)]
    [InlineData("2.0.7", "2.0.8", -1)]
    [InlineData("3.0.0", "2.9.9", 1)]
    [InlineData("2.0.8+abcdef", "2.0.8", 0)]
    [InlineData("v2.0.9-preview", "2.0.8", 1)]
    public void CompareVersions_Tests(string v1, string v2, int expectedSign)
    {
        int result = GitHubUpdateService.CompareVersions(v1, v2);
        int sign = Math.Sign(result);
        Assert.Equal(expectedSign, sign);
    }

    [Fact]
    public void ShouldPerformScheduledCheck_ManualOnly_ReturnsFalse()
    {
        var service = new GitHubUpdateService(_repo);
        var settings = new AppSettings
        {
            UpdateFrequency = UpdateCheckFrequency.ManualOnly,
            LastUpdateCheckUtc = null
        };

        Assert.False(service.ShouldPerformScheduledCheck(settings));
    }

    [Fact]
    public void ShouldPerformScheduledCheck_OnStartup_ReturnsTrue()
    {
        var service = new GitHubUpdateService(_repo);
        var settings = new AppSettings
        {
            UpdateFrequency = UpdateCheckFrequency.OnStartup,
            LastUpdateCheckUtc = DateTime.UtcNow
        };

        Assert.True(service.ShouldPerformScheduledCheck(settings));
    }

    [Fact]
    public void ShouldPerformScheduledCheck_Daily_RespectsInterval()
    {
        var service = new GitHubUpdateService(_repo);
        var settings = new AppSettings
        {
            UpdateFrequency = UpdateCheckFrequency.Daily,
            LastUpdateCheckUtc = DateTime.UtcNow.AddHours(-5)
        };

        Assert.False(service.ShouldPerformScheduledCheck(settings));

        settings.LastUpdateCheckUtc = DateTime.UtcNow.AddHours(-25);
        Assert.True(service.ShouldPerformScheduledCheck(settings));
    }

    [Fact]
    public void ShouldPerformScheduledCheck_Weekly_RespectsInterval()
    {
        var service = new GitHubUpdateService(_repo);
        var settings = new AppSettings
        {
            UpdateFrequency = UpdateCheckFrequency.Weekly,
            LastUpdateCheckUtc = DateTime.UtcNow.AddDays(-3)
        };

        Assert.False(service.ShouldPerformScheduledCheck(settings));

        settings.LastUpdateCheckUtc = DateTime.UtcNow.AddDays(-8);
        Assert.True(service.ShouldPerformScheduledCheck(settings));
    }

    [Fact]
    public async Task CheckForUpdatesAsync_NewReleaseAvailable_ReturnsUpdateInfoAndChangelog()
    {
        var jsonResponse = @"[
            {
                ""tag_name"": ""v2.0.9"",
                ""name"": ""TriggerPoint v2.0.9 - Performance Boost"",
                ""body"": ""- Added update checker\n- Improved memory usage"",
                ""published_at"": ""2026-09-18T10:00:00Z"",
                ""html_url"": ""https://github.com/Rapscallion0/TriggerPoint/releases/tag/v2.0.9"",
                ""draft"": false,
                ""prerelease"": false,
                ""assets"": [
                    {
                        ""name"": ""TriggerPointSetup.exe"",
                        ""browser_download_url"": ""https://github.com/Rapscallion0/TriggerPoint/releases/download/v2.0.9/TriggerPointSetup.exe"",
                        ""size"": 25000000
                    }
                ]
            }
        ]";

        var handler = new MockHttpMessageHandler(jsonResponse, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var service = new GitHubUpdateService(_repo, httpClient);
        service.SetCurrentVersionForTesting("2.0.8");

        var result = await service.CheckForUpdatesAsync(isManualCheck: true);

        Assert.True(result.IsUpdateAvailable);
        Assert.NotNull(result.LatestUpdate);
        Assert.Equal("2.0.9", result.LatestUpdate.Version);
        Assert.Equal("TriggerPointSetup.exe", result.LatestUpdate.FileName);
        Assert.Equal(25000000, result.LatestUpdate.FileSizeBytes);
        Assert.Contains("Performance Boost", result.LatestUpdate.Title);
        Assert.Contains("Added update checker", result.CombinedChangelog);

        // Verify settings were updated
        var savedSettings = await _repo.LoadSettingsAsync();
        Assert.NotNull(savedSettings.LastUpdateCheckUtc);
        Assert.Equal("2.0.9", savedSettings.LastVersionFound);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_AggregatesIntermediateChangelogs()
    {
        var jsonResponse = @"[
            {
                ""tag_name"": ""v2.0.9"",
                ""name"": ""v2.0.9"",
                ""body"": ""Changelog for 2.0.9"",
                ""published_at"": ""2026-09-18T10:00:00Z"",
                ""draft"": false,
                ""prerelease"": false,
                ""assets"": []
            },
            {
                ""tag_name"": ""v2.0.8"",
                ""name"": ""v2.0.8"",
                ""body"": ""Changelog for 2.0.8"",
                ""published_at"": ""2026-09-17T10:00:00Z"",
                ""draft"": false,
                ""prerelease"": false,
                ""assets"": []
            },
            {
                ""tag_name"": ""v2.0.7"",
                ""name"": ""v2.0.7"",
                ""body"": ""Changelog for 2.0.7"",
                ""published_at"": ""2026-09-16T10:00:00Z"",
                ""draft"": false,
                ""prerelease"": false,
                ""assets"": []
            }
        ]";

        var handler = new MockHttpMessageHandler(jsonResponse, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var service = new GitHubUpdateService(_repo, httpClient);
        service.SetCurrentVersionForTesting("2.0.6");

        var result = await service.CheckForUpdatesAsync(isManualCheck: true);

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(3, result.IntermediateReleases.Count);
        Assert.Contains("Changelog for 2.0.9", result.CombinedChangelog);
        Assert.Contains("Changelog for 2.0.8", result.CombinedChangelog);
        Assert.Contains("Changelog for 2.0.7", result.CombinedChangelog);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_IgnoredVersion_FlagsIsIgnored()
    {
        var jsonResponse = @"[
            {
                ""tag_name"": ""v2.0.9"",
                ""name"": ""v2.0.9"",
                ""body"": ""Release 2.0.9"",
                ""published_at"": ""2026-09-18T10:00:00Z"",
                ""draft"": false,
                ""prerelease"": false,
                ""assets"": []
            }
        ]";

        var settings = await _repo.LoadSettingsAsync();
        settings.IgnoredUpdateVersion = "2.0.9";
        await _repo.SaveSettingsAsync(settings);

        var handler = new MockHttpMessageHandler(jsonResponse, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var service = new GitHubUpdateService(_repo, httpClient);
        service.SetCurrentVersionForTesting("2.0.8");

        var result = await service.CheckForUpdatesAsync(isManualCheck: false);

        Assert.True(result.IsUpdateAvailable);
        Assert.True(result.IsIgnored);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_PreReleaseSkipped_WhenDisabled()
    {
        var jsonResponse = @"[
            {
                ""tag_name"": ""v2.1.0-beta"",
                ""name"": ""v2.1.0-beta"",
                ""body"": ""Beta Release"",
                ""published_at"": ""2026-09-18T10:00:00Z"",
                ""draft"": false,
                ""prerelease"": true,
                ""assets"": []
            },
            {
                ""tag_name"": ""v2.0.8"",
                ""name"": ""v2.0.8"",
                ""body"": ""Stable Release"",
                ""published_at"": ""2026-09-17T10:00:00Z"",
                ""draft"": false,
                ""prerelease"": false,
                ""assets"": []
            }
        ]";

        var settings = await _repo.LoadSettingsAsync();
        settings.IncludePreReleases = false;
        await _repo.SaveSettingsAsync(settings);

        var handler = new MockHttpMessageHandler(jsonResponse, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var service = new GitHubUpdateService(_repo, httpClient);
        service.SetCurrentVersionForTesting("2.0.8");

        var result = await service.CheckForUpdatesAsync(isManualCheck: true);

        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_HandlesRateLimitingGracefully()
    {
        var handler = new MockHttpMessageHandler("API rate limit exceeded", HttpStatusCode.Forbidden);
        var httpClient = new HttpClient(handler);
        var service = new GitHubUpdateService(_repo, httpClient);
        service.SetCurrentVersionForTesting("2.0.8");

        var result = await service.CheckForUpdatesAsync(isManualCheck: true);

        Assert.False(result.IsUpdateAvailable);
        Assert.False(result.IsSuccess);
        Assert.Contains("rate limit", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AppSettings_UpdateProperties_RoundTripSuccessfully()
    {
        var settings = new AppSettings
        {
            UpdateFrequency = UpdateCheckFrequency.Weekly,
            LastUpdateCheckUtc = DateTime.UtcNow,
            LastVersionFound = "2.1.0",
            IgnoredUpdateVersion = "2.0.9",
            IncludePreReleases = true,
            SilentInstallUpdates = false,
            LastKnownAppVersion = "2.0.8"
        };

        await _repo.SaveSettingsAsync(settings);
        var loaded = await _repo.LoadSettingsAsync();

        Assert.Equal(UpdateCheckFrequency.Weekly, loaded.UpdateFrequency);
        Assert.NotNull(loaded.LastUpdateCheckUtc);
        Assert.Equal("2.1.0", loaded.LastVersionFound);
        Assert.Equal("2.0.9", loaded.IgnoredUpdateVersion);
        Assert.True(loaded.IncludePreReleases);
        Assert.False(loaded.SilentInstallUpdates);
        Assert.Equal("2.0.8", loaded.LastKnownAppVersion);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseContent;
        private readonly HttpStatusCode _statusCode;

        public MockHttpMessageHandler(string responseContent, HttpStatusCode statusCode)
        {
            _responseContent = responseContent;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseContent, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
