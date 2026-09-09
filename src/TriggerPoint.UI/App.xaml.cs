using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;
using TriggerPoint.Infrastructure.Persistence;
using TriggerPoint.Infrastructure.Services;
using TriggerPoint.Infrastructure.Win32;
using TriggerPoint.UI.Services;
using TriggerPoint.UI.Theme;
using TriggerPoint.UI.Views;

namespace TriggerPoint.UI;

public partial class App : Application
{
    public static readonly Guid OpenSettingsActionId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public static readonly Guid CommandPaletteActionId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private IServiceProvider? _serviceProvider;
    private SingleInstanceService? _singleInstanceService;
    private TrayIconService? _trayIconService;
    private SettingsWindow? _settingsWindow;
    private IShortcutListener? _shortcutListener;
    private IActionExecutor? _executor;
    private IConfigRepository? _repository;
    private ILogManagerService? _logManagerService;
    private Serilog.Core.LoggingLevelSwitch _levelSwitch = new();

    public static List<TriggerItem> CreateVirtualApplicationItems(AppSettings? settings)
    {
        var list = new List<TriggerItem>();
        if (settings == null) return list;

        if (settings.OpenSettingsHotkey != null && !settings.OpenSettingsHotkey.IsEmpty)
        {
            list.Add(new TriggerItem
            {
                Id = OpenSettingsActionId,
                Name = "Open Action Manager",
                Hotkey = settings.OpenSettingsHotkey,
                IsEnabled = true,
                PresentationMode = PresentationMode.Direct
            });
        }

        if (settings.CommandPaletteHotkey != null && !settings.CommandPaletteHotkey.IsEmpty)
        {
            list.Add(new TriggerItem
            {
                Id = CommandPaletteActionId,
                Name = "Open Command Palette",
                Hotkey = settings.CommandPaletteHotkey,
                IsEnabled = true,
                PresentationMode = PresentationMode.Direct
            });
        }

        return list;
    }

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
                fileSizeLimitBytes: (long)appSettings.LogSplitThresholdMb * 1024L * 1024L,
                rollOnFileSizeLimit: true,
                shared: true,
                retainedFileCountLimit: appSettings.LogRetentionDays,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}")
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("Starting TriggerPoint daemon (LogLevel: {LogLevel}, Retention: {Retention} days, SplitThreshold: {SplitThreshold}MB)...", 
            appSettings.LogLevel, appSettings.LogRetentionDays, appSettings.LogSplitThresholdMb);

        // 3. Single-Instance Enforcement
        _singleInstanceService = new SingleInstanceService();
        if (!_singleInstanceService.TryAcquire())
        {
            Log.Information("Secondary instance detected. Signaling primary instance and shutting down.");
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
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
        var toastService = _serviceProvider.GetRequiredService<IToastNotificationService>();

        // Wire executor open settings and toast notifications
        if (_executor is ShellActionExecutor shellExec)
        {
            shellExec.OpenSettingsRequested += item => Dispatcher.Invoke(() => ShowSettingsWindow(item));
            shellExec.ExecutionSucceeded += (item, detail) =>
            {
                if (appSettings.ShowSuccessToasts)
                {
                    toastService.ShowSuccess(item.Name, detail);
                }
            };
            shellExec.ExecutionFailed += (item, error) =>
            {
                toastService.ShowError($"Failed to launch '{item.Name}'", error);
            };
        }

        // 6. Initialize Win32 Hotkey Listener
        _shortcutListener.Start(IntPtr.Zero);
        _shortcutListener.HotkeyTriggered += ShortcutListener_HotkeyTriggered;

        // 7. Setup Settings Window
        var contextFilterService = _serviceProvider.GetRequiredService<IContextFilterService>();
        _settingsWindow = new SettingsWindow(_repository, _shortcutListener, _executor, contextFilterService, _logManagerService);
        MainWindow = _settingsWindow;

        // 8. Startup Recycle Bin Purge
        try
        {
            await _repository.PurgeRecycleBinAsync(appSettings.RecycleBinRetentionDays);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to purge recycle bin on startup.");
        }

        // 9. Load and register hotkeys (including global application shortcuts)
        var items = await _repository.LoadAsync();
        var allItems = new List<TriggerItem>(items);
        allItems.AddRange(CreateVirtualApplicationItems(appSettings));
        _shortcutListener.RegisterAll(allItems);

        // Optional startup shortcut health check
        if (appSettings.ValidateShortcutsOnStartup)
        {
            _ = Task.Run(() =>
            {
                int brokenCount = items.Where(x => x.ActionType == ActionType.Shell && x.IsEnabled).Count(x => !ShortcutValidator.Validate(x).IsValid);
                if (brokenCount > 0)
                {
                    Dispatcher.Invoke(() =>
                    {
                        toastService.ShowWarning(
                            "Shortcut Health Check",
                            $"{brokenCount} action{(brokenCount == 1 ? " has a" : "s have")} missing target files. Open Settings to inspect.");
                    });
                }
            });
        }

        // 10. Setup System Tray Icon
        _trayIconService = new TrayIconService(
            _shortcutListener,
            openSettingsAction: () => Dispatcher.Invoke(() => ShowSettingsWindow()),
            openPaletteAction: () => Dispatcher.Invoke(() => OpenCommandPalette()),
            reloadConfigAction: async () =>
            {
                await ReloadApplicationSettingsAndHotkeysAsync();
                _trayIconService?.ShowNotification("Configuration Reloaded", "Configuration and shortcuts reloaded.");
            },
            exitAction: () => Dispatcher.Invoke(ExitApplication),
            openAppSettingsAction: () => Dispatcher.Invoke(async () => await ShowApplicationSettingsWindowAsync()),
            commandPaletteHotkeyText: appSettings.CommandPaletteHotkey?.DisplayText ?? "Alt+Space",
            openSettingsHotkeyText: appSettings.OpenSettingsHotkey?.DisplayText ?? "Ctrl+Alt+T");

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
        services.AddSingleton<IToastNotificationService, ToastNotificationService>();
    }

    private void ShortcutListener_HotkeyTriggered(object? sender, TriggerItem item)
    {
        // Capture active foreground window BEFORE TriggerPoint displays any UI
        var targetHwnd = NativeMethods.GetForegroundWindow();
        if (_serviceProvider != null)
        {
            var filterService = _serviceProvider.GetService<IContextFilterService>();
            if (filterService != null && targetHwnd != IntPtr.Zero)
            {
                filterService.LastExternalForegroundHwnd = targetHwnd;
            }
        }

        Dispatcher.Invoke(async () =>
        {
            if (item.Id == OpenSettingsActionId)
            {
                ShowSettingsWindow();
                return;
            }

            if (item.Id == CommandPaletteActionId)
            {
                OpenCommandPalette(targetHwnd: targetHwnd);
                return;
            }

            switch (item.PresentationMode)
            {
                case PresentationMode.Direct:
                    if (_executor != null)
                    {
                        await _executor.ExecuteAsync(item, ExecutionOverride.Standard, targetHwnd);
                    }
                    break;

                case PresentationMode.CursorMenu:
                    OpenCursorMenu(item, targetHwnd);
                    break;

                case PresentationMode.CommandPalette:
                    OpenCommandPalette(item, targetHwnd);
                    break;
            }
        });
    }

    private void OpenCursorMenu(TriggerItem triggerItem, IntPtr targetHwnd = default)
    {
        if (_repository == null || _executor == null) return;

        _ = Task.Run(async () =>
        {
            var allItems = await _repository.LoadAsync();

            await Dispatcher.InvokeAsync(() =>
            {
                var menu = new CursorContextMenuView(allItems, _executor, triggerItem, targetHwnd);
                menu.Show();
                menu.Activate();
                try
                {
                    var handle = new System.Windows.Interop.WindowInteropHelper(menu).Handle;
                    NativeMethods.SetForegroundWindow(handle);
                }
                catch { }
            });
        });
    }

    private void OpenCommandPalette(TriggerItem? triggerItem = null, IntPtr targetHwnd = default)
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
                var palette = new CommandPaletteView(allItems, _executor, scopeId, scopeName, targetHwnd);
                palette.Show();
                palette.Activate();
                try
                {
                    var handle = new System.Windows.Interop.WindowInteropHelper(palette).Handle;
                    NativeMethods.SetForegroundWindow(handle);
                }
                catch { }
            });
        });
    }

    public void ShowSettingsWindow(TriggerItem? focusedItem = null)
    {
        if (_settingsWindow == null) return;

        if (_settingsWindow.WindowState == WindowState.Minimized)
        {
            _settingsWindow.WindowState = WindowState.Normal;
        }

        _settingsWindow.Show();
        _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
        _settingsWindow.Focus();

        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(_settingsWindow).EnsureHandle();
            uint currentThreadId = NativeMethods.GetCurrentThreadId();
            IntPtr foregroundHwnd = NativeMethods.GetForegroundWindow();
            uint foregroundThreadId = NativeMethods.GetWindowThreadProcessId(foregroundHwnd, out _);

            bool attached = false;
            if (currentThreadId != foregroundThreadId && foregroundThreadId != 0)
            {
                attached = NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, true);
            }

            try
            {
                NativeMethods.BringWindowToTop(handle);
                NativeMethods.SetForegroundWindow(handle);
            }
            finally
            {
                if (attached)
                {
                    NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, false);
                }
            }

            // Temporarily toggle Topmost to pop in front of other active windows (e.g. Explorer)
            _settingsWindow.Topmost = true;
            _settingsWindow.Topmost = false;
        }
        catch { }

        if (focusedItem != null)
        {
            _settingsWindow.SelectTreeItem(focusedItem);
        }
    }

    public async Task ShowApplicationSettingsWindowAsync()
    {
        if (_repository == null || _logManagerService == null) return;

        var appSettingsWin = new ApplicationSettingsWindow(_repository, _logManagerService);
        appSettingsWin.ShowDialog();

        await ReloadApplicationSettingsAndHotkeysAsync();
    }

    public void ShowApplicationSettingsWindow()
    {
        _ = ShowApplicationSettingsWindowAsync();
    }

    public async Task ReloadApplicationSettingsAndHotkeysAsync()
    {
        if (_repository == null || _shortcutListener == null) return;
        try
        {
            var settings = await _repository.LoadSettingsAsync();
            var items = await _repository.LoadAsync();
            var allItems = new List<TriggerItem>(items);
            allItems.AddRange(CreateVirtualApplicationItems(settings));
            _shortcutListener.RegisterAll(allItems);
            _trayIconService?.UpdateCommandPaletteHotkey(settings.CommandPaletteHotkey?.DisplayText);
            _trayIconService?.UpdateOpenSettingsHotkey(settings.OpenSettingsHotkey?.DisplayText);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to reload application settings and hotkeys.");
        }
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
