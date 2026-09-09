using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Serilog;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Services;
using TriggerPoint.UI.Theme;

namespace TriggerPoint.UI.Views;

public partial class ApplicationSettingsWindow : Window
{
    private static readonly ILogger Logger = Log.ForContext<ApplicationSettingsWindow>();
    private const string StartupRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRegistryValueName = "TriggerPoint";

    private readonly IConfigRepository _repository;
    private readonly ILogManagerService _logManagerService;
    private AppSettings _currentSettings = new();

    public ApplicationSettingsWindow(IConfigRepository repository, ILogManagerService logManagerService)
    {
        InitializeComponent();
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logManagerService = logManagerService ?? throw new ArgumentNullException(nameof(logManagerService));

        Loaded += async (s, e) => await LoadCurrentSettingsAsync();
    }

    private async System.Threading.Tasks.Task LoadCurrentSettingsAsync()
    {
        try
        {
            _currentSettings = await _repository.LoadSettingsAsync();

            // Populate Log Level
            LogLevelCombo.SelectedIndex = _currentSettings.LogLevel switch
            {
                LogLevelOption.Verbose => 0,
                LogLevelOption.Debug => 1,
                LogLevelOption.Information => 2,
                LogLevelOption.Warning => 3,
                LogLevelOption.Error => 4,
                LogLevelOption.Fatal => 5,
                _ => 2
            };

            // Populate Retention Days
            RetentionDaysCombo.SelectedIndex = _currentSettings.LogRetentionDays switch
            {
                1 => 0,
                3 => 1,
                7 => 2,
                14 => 3,
                30 => 4,
                90 => 5,
                _ => 2
            };
            RetentionDaysText.Text = $"{_currentSettings.LogRetentionDays} days";

            // Populate Theme
            ThemeCombo.SelectedIndex = _currentSettings.Theme switch
            {
                ThemePreference.System => 0,
                ThemePreference.Dark => 1,
                ThemePreference.Light => 2,
                _ => 0
            };

            // Populate Startup & Minimized
            RunAtStartupCheck.IsChecked = IsRunAtStartupConfigured() || _currentSettings.RunAtStartup;
            StartMinimizedCheck.IsChecked = _currentSettings.StartMinimized;

            SettingsStatusText.Text = "Settings loaded.";
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to load application settings.");
            SettingsStatusText.Text = "Failed to load settings.";
        }
    }

    private void RetentionDaysCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RetentionDaysCombo == null || RetentionDaysText == null) return;
        int days = GetSelectedRetentionDays();
        RetentionDaysText.Text = $"{days} days";
    }

    private int GetSelectedRetentionDays() => RetentionDaysCombo.SelectedIndex switch
    {
        0 => 1,
        1 => 3,
        2 => 7,
        3 => 14,
        4 => 30,
        5 => 90,
        _ => 7
    };

    private async void SaveSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var level = LogLevelCombo.SelectedIndex switch
            {
                0 => LogLevelOption.Verbose,
                1 => LogLevelOption.Debug,
                2 => LogLevelOption.Information,
                3 => LogLevelOption.Warning,
                4 => LogLevelOption.Error,
                5 => LogLevelOption.Fatal,
                _ => LogLevelOption.Information
            };

            var retentionDays = GetSelectedRetentionDays();
            var themePref = ThemeCombo.SelectedIndex switch
            {
                0 => ThemePreference.System,
                1 => ThemePreference.Dark,
                2 => ThemePreference.Light,
                _ => ThemePreference.System
            };

            bool runStartup = RunAtStartupCheck.IsChecked == true;
            bool startMinimized = StartMinimizedCheck.IsChecked == true;

            _currentSettings.LogLevel = level;
            _currentSettings.LogRetentionDays = retentionDays;
            _currentSettings.Theme = themePref;
            _currentSettings.RunAtStartup = runStartup;
            _currentSettings.StartMinimized = startMinimized;

            // 1. Persist to appsettings.json
            await _repository.SaveSettingsAsync(_currentSettings);

            // 2. Dynamically apply log level at runtime without app restart
            _logManagerService.UpdateLogLevel(level);

            // 3. Apply theme preference immediately
            ThemeManager.ApplyPreference(themePref);

            // 4. Configure Windows Startup Registry Key
            SetRunAtStartup(runStartup, startMinimized);

            Logger.Information("Application settings successfully updated. LogLevel={LogLevel}, RetentionDays={RetentionDays}, Theme={Theme}, Startup={Startup}",
                level, retentionDays, themePref, runStartup);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to save application settings.");
            ModernMessageDialog.ShowAlert(this, "Settings Error", $"Failed to save application settings: {ex.Message}", ModernDialogType.Error);
        }
    }

    private void OpenLogsFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        _logManagerService.OpenLogDirectory();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
    }

    private static bool IsRunAtStartupConfigured()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, false);
            var val = key?.GetValue(AppRegistryValueName) as string;
            return !string.IsNullOrWhiteSpace(val);
        }
        catch
        {
            return false;
        }
    }

    private static void SetRunAtStartup(bool enable, bool startMinimized)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    string args = startMinimized ? " --minimized" : "";
                    key.SetValue(AppRegistryValueName, $"\"{exePath}\"{args}");
                }
            }
            else
            {
                key.DeleteValue(AppRegistryValueName, false);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to update Windows startup registry key.");
        }
    }
}
