using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;

namespace TriggerPoint.UI.Views;

public partial class ServicePropertiesDialog : Window
{
    private readonly WindowsServiceItem _serviceItem;
    private readonly IWindowsServiceManager _serviceManager;
    private WindowsServiceDetails? _details;

    public ServicePropertiesDialog(WindowsServiceItem serviceItem, IWindowsServiceManager serviceManager, Window? owner = null)
    {
        InitializeComponent();
        _serviceItem = serviceItem ?? throw new ArgumentNullException(nameof(serviceItem));
        _serviceManager = serviceManager ?? throw new ArgumentNullException(nameof(serviceManager));

        if (owner != null && owner.IsLoaded && owner.IsVisible)
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        LoadServiceDetails();
    }

    private void LoadServiceDetails()
    {
        try
        {
            _details = _serviceManager.GetServiceDetails(_serviceItem.ServiceName);
        }
        catch (Exception ex)
        {
            _details = new WindowsServiceDetails
            {
                ServiceName = _serviceItem.ServiceName,
                DisplayName = _serviceItem.DisplayName,
                Status = _serviceItem.Status,
                StartType = _serviceItem.StartType,
                Description = $"Failed to read details: {ex.Message}",
                CanStop = _serviceItem.CanStop,
                CanPauseAndContinue = _serviceItem.CanPauseAndContinue,
                CanShutdown = _serviceItem.CanShutdown
            };
        }

        if (_details == null)
        {
            _details = new WindowsServiceDetails
            {
                ServiceName = _serviceItem.ServiceName,
                DisplayName = string.IsNullOrWhiteSpace(_serviceItem.DisplayName) ? _serviceItem.ServiceName : _serviceItem.DisplayName,
                Status = _serviceItem.Status,
                StartType = _serviceItem.StartType,
                Description = "Service details could not be queried from Windows Service Control Manager.",
                CanStop = _serviceItem.CanStop,
                CanPauseAndContinue = _serviceItem.CanPauseAndContinue,
                CanShutdown = _serviceItem.CanShutdown
            };
        }

        UpdateUiFromDetails();
    }

    private void UpdateUiFromDetails()
    {
        if (_details == null) return;

        ServiceDisplayNameText.Text = _details.DisplayName;
        ServiceNameText.Text = _details.ServiceName;
        DescriptionText.Text = string.IsNullOrWhiteSpace(_details.Description)
            ? "No description provided for this service."
            : _details.Description;

        BinaryPathText.Text = string.IsNullOrWhiteSpace(_details.BinaryPath)
            ? "(Not specified or not available)"
            : _details.BinaryPath;

        StartupTypeText.Text = _details.StartType;
        ServiceAccountText.Text = string.IsNullOrWhiteSpace(_details.ServiceAccount)
            ? "LocalSystem"
            : _details.ServiceAccount;

        ProcessIdText.Text = _details.ProcessId > 0
            ? _details.ProcessId.ToString()
            : (_details.Status == WindowsServiceStatus.Running ? "Active" : "None");

        CanStopText.Text = _details.CanStop ? "Yes" : "No";
        CanPauseText.Text = _details.CanPauseAndContinue ? "Yes" : "No";
        CanShutdownText.Text = _details.CanShutdown ? "Yes" : "No";

        // Status Badge & Controls
        StatusEmojiText.Text = _details.StatusBadgeEmoji;
        StatusLabelText.Text = _details.StatusText.ToUpperInvariant();

        ApplyStatusStyling(_details.Status);

        bool isRunning = _details.Status == WindowsServiceStatus.Running;
        bool isStopped = _details.Status == WindowsServiceStatus.Stopped;
        bool isPending = _details.Status is WindowsServiceStatus.StartPending or WindowsServiceStatus.StopPending or WindowsServiceStatus.ContinuePending or WindowsServiceStatus.PausePending;

        StartBtn.IsEnabled = !isRunning && !isPending;
        StopBtn.IsEnabled = isRunning && _details.CanStop && !isPending;
        RestartBtn.IsEnabled = isRunning && !isPending;
    }

    private void ApplyStatusStyling(WindowsServiceStatus status)
    {
        Brush bgBrush;
        Brush borderBrush;
        Brush textBrush;

        switch (status)
        {
            case WindowsServiceStatus.Running:
                bgBrush = TryFindResource("SuccessBgBrush") as Brush ?? new SolidColorBrush(Color.FromArgb(40, 46, 204, 113));
                borderBrush = TryFindResource("SuccessBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(46, 204, 113));
                textBrush = TryFindResource("SuccessBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(46, 204, 113));
                break;
            case WindowsServiceStatus.Paused:
                bgBrush = TryFindResource("WarningBgBrush") as Brush ?? new SolidColorBrush(Color.FromArgb(40, 241, 196, 15));
                borderBrush = TryFindResource("WarningBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(241, 196, 15));
                textBrush = TryFindResource("WarningBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(241, 196, 15));
                break;
            case WindowsServiceStatus.StartPending:
            case WindowsServiceStatus.StopPending:
            case WindowsServiceStatus.ContinuePending:
            case WindowsServiceStatus.PausePending:
                bgBrush = TryFindResource("AccentSubtleBrush") as Brush ?? new SolidColorBrush(Color.FromArgb(40, 52, 152, 219));
                borderBrush = TryFindResource("AccentBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(52, 152, 219));
                textBrush = TryFindResource("AccentBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(52, 152, 219));
                break;
            default: // Stopped or Unknown
                bgBrush = TryFindResource("BgSecondaryBrush") as Brush ?? new SolidColorBrush(Color.FromArgb(40, 127, 140, 141));
                borderBrush = TryFindResource("BorderSubtleBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(127, 140, 141));
                textBrush = TryFindResource("TextSecondaryBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(189, 195, 199));
                break;
        }

        StatusPillBorder.Background = bgBrush;
        StatusPillBorder.BorderBrush = borderBrush;
        StatusLabelText.Foreground = textBrush;
    }

    private async void StartBtn_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteServiceActionAsync("Starting", () => _serviceManager.StartServiceAsync(_serviceItem.ServiceName, runAsAdmin: false));
    }

    private async void StopBtn_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteServiceActionAsync("Stopping", () => _serviceManager.StopServiceAsync(_serviceItem.ServiceName, runAsAdmin: false));
    }

    private async void RestartBtn_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteServiceActionAsync("Restarting", () => _serviceManager.RestartServiceAsync(_serviceItem.ServiceName, runAsAdmin: false));
    }

    private async Task ExecuteServiceActionAsync(string actionVerb, Func<Task<ServiceOperationResult>> action)
    {
        StartBtn.IsEnabled = false;
        StopBtn.IsEnabled = false;
        RestartBtn.IsEnabled = false;
        ActionFeedbackText.Text = $"{actionVerb} service... ⏳";

        try
        {
            var result = await action();
            ActionFeedbackText.Text = result.Success ? result.Message : $"⚠️ {result.Message}";
            _serviceItem.Status = result.NewStatus;
        }
        catch (Exception ex)
        {
            ActionFeedbackText.Text = $"⚠️ Error: {ex.Message}";
        }
        finally
        {
            LoadServiceDetails();
        }
    }

    private void RefreshStatusBtn_Click(object sender, RoutedEventArgs e)
    {
        LoadServiceDetails();
        ActionFeedbackText.Text = "Status refreshed 🔄";
    }

    private void CopyServiceNameBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_details?.ServiceName ?? _serviceItem.ServiceName);
            ActionFeedbackText.Text = "Copied service key 📋";
        }
        catch (Exception ex)
        {
            ActionFeedbackText.Text = $"Copy failed: {ex.Message}";
        }
    }

    private void CopyBinaryPathBtn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_details?.BinaryPath)) return;
        try
        {
            Clipboard.SetText(_details.BinaryPath);
            ActionFeedbackText.Text = "Copied binary path 📋";
        }
        catch (Exception ex)
        {
            ActionFeedbackText.Text = $"Copy failed: {ex.Message}";
        }
    }

    private void RevealBinaryBtn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_details?.BinaryPath)) return;

        try
        {
            string raw = _details.BinaryPath.Trim();
            string filePath = raw;

            // Handle quoted paths: "C:\Program Files\App\service.exe" -arg
            if (raw.StartsWith('\"'))
            {
                int endQuote = raw.IndexOf('\"', 1);
                if (endQuote > 1)
                {
                    filePath = raw.Substring(1, endQuote - 1);
                }
            }
            else
            {
                // Find first space that is followed by a slash or hyphen parameter
                int spaceIndex = raw.IndexOf(".exe ", StringComparison.OrdinalIgnoreCase);
                if (spaceIndex > 0)
                {
                    filePath = raw.Substring(0, spaceIndex + 4);
                }
            }

            filePath = Environment.ExpandEnvironmentVariables(filePath);

            if (File.Exists(filePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{filePath}\"",
                    UseShellExecute = true
                });
                ActionFeedbackText.Text = "Revealed in Explorer 📁";
            }
            else if (Directory.Exists(Path.GetDirectoryName(filePath)))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{Path.GetDirectoryName(filePath)}\"",
                    UseShellExecute = true
                });
                ActionFeedbackText.Text = "Opened directory 📁";
            }
            else
            {
                ActionFeedbackText.Text = "Executable path not found on disk ⚠️";
            }
        }
        catch (Exception ex)
        {
            ActionFeedbackText.Text = $"Reveal error: {ex.Message}";
        }
    }

    private void OpenServicesMscBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _serviceManager.OpenServicesMsc(_details?.ServiceName ?? _serviceItem.ServiceName);
            ActionFeedbackText.Text = "Opened Services MMC ↗";
        }
        catch (Exception ex)
        {
            ActionFeedbackText.Text = $"MMC error: {ex.Message}";
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException) { }
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}

