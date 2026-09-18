using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Serilog;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;

namespace TriggerPoint.UI.Views;

public partial class UpdateAvailableDialog : Window
{
    private readonly UpdateCheckResult _updateResult;
    private readonly UpdateInfo _updateInfo;
    private readonly IUpdateService _updateService;
    private readonly IConfigRepository _configRepository;
    private CancellationTokenSource? _downloadCts;
    private bool _isDownloading = false;

    public UpdateAvailableDialog(
        UpdateCheckResult updateResult,
        IUpdateService updateService,
        IConfigRepository configRepository)
    {
        InitializeComponent();

        _updateResult = updateResult;
        _updateInfo = updateResult.LatestUpdate ?? throw new ArgumentNullException(nameof(updateResult.LatestUpdate));
        _updateService = updateService;
        _configRepository = configRepository;

        PopulateDialogData();
    }

    private void PopulateDialogData()
    {
        CurrentVersionText.Text = $"v{_updateResult.CurrentVersion}";
        TargetVersionText.Text = $"v{_updateInfo.Version}";
        ReleaseDateText.Text = $"Released {_updateInfo.PublishedAt:MMM dd, yyyy}";

        if (_updateInfo.FileSizeBytes > 0)
        {
            double mb = _updateInfo.FileSizeBytes / (1024.0 * 1024.0);
            DownloadSizeText.Text = $"Size: {mb:F1} MB";
        }
        else
        {
            DownloadSizeText.Text = "Setup Installer";
        }

        if (!string.IsNullOrWhiteSpace(_updateInfo.Title) && !_updateInfo.Title.Equals(_updateInfo.TagName, StringComparison.OrdinalIgnoreCase))
        {
            UpdateSummaryText.Text = $"{_updateInfo.Title}\nA new release of TriggerPoint is available with improvements and fixes.";
        }

        // Populate Changelog content
        if (!string.IsNullOrWhiteSpace(_updateResult.CombinedChangelog))
        {
            ChangelogContentText.Text = _updateResult.CombinedChangelog;
        }
        else if (!string.IsNullOrWhiteSpace(_updateInfo.ReleaseNotes))
        {
            ChangelogContentText.Text = _updateInfo.ReleaseNotes;
        }
        else
        {
            ChangelogContentText.Text = "No release notes were provided with this update.";
        }

        if (string.IsNullOrWhiteSpace(_updateInfo.HtmlUrl))
        {
            ViewOnGitHubBtn.Visibility = Visibility.Collapsed;
        }
    }

    private void ToggleChangelogBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ChangelogBorder.Visibility == Visibility.Visible)
        {
            ChangelogBorder.Visibility = Visibility.Collapsed;
            ChangelogExpanderArrow.Text = "▶ ";
            ChangelogExpanderLabel.Text = "View Improvements & Changelog";
        }
        else
        {
            ChangelogBorder.Visibility = Visibility.Visible;
            ChangelogExpanderArrow.Text = "▼ ";
            ChangelogExpanderLabel.Text = "Hide Changelog";
        }
    }

    private void ViewOnGitHubBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_updateInfo.HtmlUrl))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _updateInfo.HtmlUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to launch GitHub release URL.");
            }
        }
    }

    private async void IgnoreVersionBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = await _configRepository.LoadSettingsAsync();
            settings.IgnoredUpdateVersion = _updateInfo.Version;
            await _configRepository.SaveSettingsAsync(settings);
            Log.Information("User chose to ignore version {Version}", _updateInfo.Version);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to persist ignored version in AppSettings.");
        }

        Close();
    }

    private void UpdateLaterBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isDownloading)
        {
            _downloadCts?.Cancel();
        }
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_isDownloading)
            {
                _downloadCts?.Cancel();
            }
            Close();
        }
    }

    private async void UpdateNowBtn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_updateInfo.DownloadUrl))
        {
            // If no direct binary asset was attached, offer to open GitHub releases in browser
            var result = MessageBox.Show(
                "Direct installer download is unavailable for this release. Would you like to view the download on GitHub?",
                "TriggerPoint Update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes && !string.IsNullOrWhiteSpace(_updateInfo.HtmlUrl))
            {
                Process.Start(new ProcessStartInfo { FileName = _updateInfo.HtmlUrl, UseShellExecute = true });
            }
            Close();
            return;
        }

        _isDownloading = true;
        _downloadCts = new CancellationTokenSource();

        // Switch button states
        UpdateNowBtn.Visibility = Visibility.Collapsed;
        UpdateLaterBtn.Visibility = Visibility.Collapsed;
        IgnoreVersionBtn.Visibility = Visibility.Collapsed;
        CancelDownloadBtn.Visibility = Visibility.Visible;
        DownloadProgressPanel.Visibility = Visibility.Visible;

        DownloadStatusText.Text = "Connecting to GitHub...";
        DownloadPercentText.Text = "0%";
        DownloadProgressBar.Value = 0;
        DownloadSpeedText.Text = "Preparing download...";

        var progress = new Progress<UpdateDownloadProgress>(p =>
        {
            Dispatcher.Invoke(() =>
            {
                DownloadProgressBar.Value = p.Percent;
                DownloadPercentText.Text = $"{p.Percent:F0}%";

                double receivedMb = p.BytesReceived / (1024.0 * 1024.0);
                double totalMb = p.TotalBytes / (1024.0 * 1024.0);
                double speedMb = p.BytesPerSecond / (1024.0 * 1024.0);

                if (p.TotalBytes > 0)
                {
                    DownloadSpeedText.Text = $"{receivedMb:F1} MB of {totalMb:F1} MB ({speedMb:F1} MB/s)";
                    DownloadStatusText.Text = "Downloading update package...";
                }
                else
                {
                    DownloadSpeedText.Text = $"{receivedMb:F1} MB downloaded ({speedMb:F1} MB/s)";
                }
            });
        });

        try
        {
            var downloadedFile = await _updateService.DownloadUpdateAsync(_updateInfo, progress, _downloadCts.Token);

            DownloadStatusText.Text = "Verifying installer & launching...";
            DownloadPercentText.Text = "100%";
            DownloadProgressBar.Value = 100;

            AppSettings settings;
            try
            {
                settings = await _configRepository.LoadSettingsAsync();
            }
            catch
            {
                settings = new AppSettings();
            }

            // Record current version as previous version before upgrading
            settings.LastKnownAppVersion = _updateResult.CurrentVersion;
            try { await _configRepository.SaveSettingsAsync(settings); } catch { }

            // Small delay to let user see 100% completion
            await Task.Delay(350);

            _updateService.LaunchInstallerAndExit(downloadedFile, silent: settings.SilentInstallUpdates);
        }
        catch (OperationCanceledException)
        {
            Log.Information("Update download was canceled by user.");
            ResetDownloadState();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to download update installer.");
            DownloadStatusText.Text = "Download failed.";
            DownloadSpeedText.Text = ex.Message;
            MessageBox.Show($"Failed to download the update:\n{ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
            ResetDownloadState();
        }
    }

    private void CancelDownloadBtn_Click(object sender, RoutedEventArgs e)
    {
        _downloadCts?.Cancel();
    }

    private void ResetDownloadState()
    {
        _isDownloading = false;
        DownloadProgressPanel.Visibility = Visibility.Collapsed;
        CancelDownloadBtn.Visibility = Visibility.Collapsed;
        UpdateNowBtn.Visibility = Visibility.Visible;
        UpdateLaterBtn.Visibility = Visibility.Visible;
        IgnoreVersionBtn.Visibility = Visibility.Visible;
    }
}
