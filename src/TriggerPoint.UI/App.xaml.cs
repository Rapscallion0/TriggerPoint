using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Persistence;
using TriggerPoint.Infrastructure.Services;
using TriggerPoint.Infrastructure.Win32;
using TriggerPoint.UI.Services;
using TriggerPoint.UI.Theme;
using TriggerPoint.UI.Views;

namespace TriggerPoint.UI;

public partial class App : Application
{
    private IServiceProvider? _serviceProvider;
    private SingleInstanceService? _singleInstanceService;
    private TrayIconService? _trayIconService;
    private SettingsWindow? _settingsWindow;
    private IShortcutListener? _shortcutListener;
    private IActionExecutor? _executor;
    private IConfigRepository? _repository;
    private ILogManagerService? _logManagerService;
    private Serilog.Core.LoggingLevelSwitch _levelSwitch = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global unhandled exception handlers for fatal logging
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            Log.Fatal(args.ExceptionObject as Exception, "FATAL: Unhandled AppDomain exception encountered.");
            Log.CloseAndFlush();
        };

        DispatcherUnhandledException += (s, args) =>
        {
            Log.Error(args.Exception, "Unhandled Dispatcher exception caught.");
            // Prevent termination if possible
            args.Handled = true;
        };

        // 1. Setup Configuration Repository early to load AppSettings
        var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TriggerPoint");
        var logDir = Path.Combine(baseDir, "logs");
        Directory.CreateDirectory(logDir);

        var tempRepo = new JsonConfigRepository(baseDir);
        AppSettings appSettings;
        try
        {
            appSettings = await tempRepo.LoadSettingsAsync();
        }
        catch
        {
            appSettings = new AppSettings();
        }

        // 2. Configure Serilog logging with dynamic LoggingLevelSwitch
        _levelSwitch = new Serilog.Core.LoggingLevelSwitch(LogManagerService.ToLogEventLevel(appSettings.LogLevel));

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(_levelSwitch)
            .WriteTo.File(
                Path.Combine(logDir, "triggerpoint-.log"), 
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: appSettings.LogRetentionDays,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}")
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("Starting TriggerPoint daemon (LogLevel: {LogLevel}, Retention: {Retention} days)...", 
            appSettings.LogLevel, appSettings.LogRetentionDays);

        // 3. Single-Instance Enforcement
        _singleInstanceService = new SingleInstanceService();
        if (!_singleInstanceService.TryAcquire())
        {
            Log.Information("Secondary instance detected. Signaling primary instance and shutting down.");
            await SingleInstanceService.SignalPrimaryInstanceAsync(string.Join(" ", e.Args));
            Shutdown();
            return;
        }

        _singleInstanceService.SecondInstanceSignaled += OnSecondInstanceSignaled;

        // 4. Initialize Theme Manager with user's theme preference
        ThemeManager.Initialize(appSettings.Theme);

        // 5. Setup Dependency Injection
        var services = new ServiceCollection();
        _logManagerService = new LogManagerService(_levelSwitch, logDir);
        services.AddSingleton<ILogManagerService>(_logManagerService);

        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        _repository = _serviceProvider.GetRequiredService<IConfigRepository>();
        _shortcutListener = _serviceProvider.GetRequiredService<IShortcutListener>();
        _executor = _serviceProvider.GetRequiredService<IActionExecutor>();

        // Wire executor open settings request
        if (_executor is ShellActionExecutor shellExec)
        {
            shellExec.OpenSettingsRequested += item => Dispatcher.Invoke(() => ShowSettingsWindow(item));
        }

        // 6. Initialize Win32 Hotkey Listener
        _shortcutListener.Start(IntPtr.Zero);
        _shortcutListener.HotkeyTriggered += ShortcutListener_HotkeyTriggered;

        // 7. Setup Settings Window
        var contextFilterService = _serviceProvider.GetRequiredService<IContextFilterService>();
        _settingsWindow = new SettingsWindow(_repository, _shortcutListener, _executor, contextFilterService, _logManagerService);
        MainWindow = _settingsWindow;

        // 8. Load and register hotkeys
        var items = await _repository.LoadAsync();
        _shortcutListener.RegisterAll(items);

        // 9. Setup System Tray Icon
        _trayIconService = new TrayIconService(
            _shortcutListener,
            openSettingsAction: () => Dispatcher.Invoke(() => ShowSettingsWindow()),
            openPaletteAction: () => Dispatcher.Invoke(() => OpenCommandPalette()),
            reloadConfigAction: async () =>
            {
                var reloaded = await _repository.LoadAsync();
                _shortcutListener.RegisterAll(reloaded);
                _trayIconService?.ShowNotification("Configuration Reloaded", $"Loaded {reloaded.Count} shortcuts.");
            },
            exitAction: () => Dispatcher.Invoke(ExitApplication),
            openAppSettingsAction: () => Dispatcher.Invoke(() => ShowApplicationSettingsWindow()));

        // If explicitly requested with --settings, show action settings window
        if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
        {
            ShowSettingsWindow();
        }

        Log.Information("TriggerPoint daemon initialization complete. Running in background.");
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IConfigRepository, JsonConfigRepository>();
        services.AddSingleton<IPromptDialogService, InteractivePromptDialog>();
        services.AddSingleton<ISnippetService, Win32SnippetService>();
        services.AddSingleton<IContextFilterService, ContextFilterService>();
        services.AddSingleton<IIconService, Win32IconService>();
        services.AddSingleton<ITelemetryService, TelemetryService>();
        services.AddSingleton<IShortcutListener, Win32HotkeyListener>();
        services.AddSingleton<IActionExecutor, ShellActionExecutor>();
    }

    private void ShortcutListener_HotkeyTriggered(object? sender, TriggerItem item)
    {
        Dispatcher.Invoke(async () =>
        {
            switch (item.PresentationMode)
            {
                case PresentationMode.Direct:
                    if (_executor != null)
                    {
                        await _executor.ExecuteAsync(item);
                    }
                    break;

                case PresentationMode.CursorMenu:
                    OpenCursorMenu(item);
                    break;

                case PresentationMode.CommandPalette:
                    OpenCommandPalette(item);
                    break;
            }
        });
    }

    private void OpenCursorMenu(TriggerItem triggerItem)
    {
        if (_repository == null || _executor == null) return;

        // Fetch children if folder, otherwise sibling/root actions
        _ = Task.Run(async () =>
        {
            var allItems = await _repository.LoadAsync();
            var targetItems = triggerItem.ActionType == ActionType.Folder
                ? allItems.Where(x => x.ParentId == triggerItem.Id).ToList()
                : allItems.Where(x => x.ActionType != ActionType.Folder).ToList();

            await Dispatcher.InvokeAsync(() =>
            {
                var menu = new CursorContextMenuView(targetItems, _executor, triggerItem.Name);
                menu.Show();
            });
        });
    }

    private void OpenCommandPalette(TriggerItem? triggerItem = null)
    {
        if (_repository == null || _executor == null) return;

        _ = Task.Run(async () =>
        {
            var allItems = await _repository.LoadAsync();
            Guid? scopeId = null;
            string? scopeName = null;

            if (triggerItem != null && triggerItem.ActionType == ActionType.Folder)
            {
                scopeId = triggerItem.Id;
                scopeName = triggerItem.Name;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                var palette = new CommandPaletteView(allItems, _executor, scopeId, scopeName);
                palette.Show();
            });
        });
    }

    public void ShowSettingsWindow(TriggerItem? focusedItem = null)
    {
        if (_settingsWindow == null) return;

        _settingsWindow.Show();
        _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
        _settingsWindow.Focus();

        if (focusedItem != null)
        {
            _settingsWindow.SelectTreeItem(focusedItem);
        }
    }

    public void ShowApplicationSettingsWindow()
    {
        if (_repository == null || _logManagerService == null) return;

        var appSettingsWin = new ApplicationSettingsWindow(_repository, _logManagerService);
        if (_settingsWindow != null && _settingsWindow.IsVisible)
        {
            appSettingsWin.Owner = _settingsWindow;
        }
        appSettingsWin.ShowDialog();
    }

    private void OnSecondInstanceSignaled(string message)
    {
        Dispatcher.Invoke(() =>
        {
            Log.Information("Secondary instance signaled message: '{Message}'", message);
            ShowSettingsWindow();
        });
    }

    private void ExitApplication()
    {
        Log.Information("Shutting down TriggerPoint daemon...");

        if (_settingsWindow != null)
        {
            _settingsWindow.IsExiting = true;
            _settingsWindow.Close();
        }

        _trayIconService?.Dispose();
        _shortcutListener?.Dispose();
        _singleInstanceService?.Dispose();

        Log.CloseAndFlush();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIconService?.Dispose();
        _shortcutListener?.Dispose();
        _singleInstanceService?.Dispose();
        base.OnExit(e);
    }
}
