using System;
using System.Threading;
using System.Threading.Tasks;
using TriggerPoint.Core.Models;

namespace TriggerPoint.Core.Contracts;

public interface IUpdateService
{
    Task<UpdateCheckResult> CheckForUpdatesAsync(bool isManualCheck = false, CancellationToken ct = default);
    Task<string> DownloadUpdateAsync(UpdateInfo updateInfo, IProgress<UpdateDownloadProgress>? progress = null, CancellationToken ct = default);
    void LaunchInstallerAndExit(string installerPath, bool silent = true);
    bool ShouldPerformScheduledCheck(AppSettings settings);
    string GetCurrentVersion();
}
