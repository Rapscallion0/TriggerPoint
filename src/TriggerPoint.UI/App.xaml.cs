using System;
using System.Diagnostics;
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
using TriggerPoint.UI.Controls;
using TriggerPoint.UI.Services;
using TriggerPoint.UI.Theme;
using TriggerPoint.UI.Views;

namespace TriggerPoint.UI;

public partial class App : Application
{
    public static readonly Guid OpenSettingsActionId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public static readonly Guid CommandPaletteActionId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    public static readonly Guid CheatSheetActionId = Guid.Parse("00000000-0000-0000-0000-000000000003");
    public static readonly Guid ClipboardHistoryActionId = Guid.Parse("00000000-0000-0000-0000-000000000004");

    private IServiceProvider? _serviceProvider;
    public IServiceProvider? ServiceProvider => _serviceProvider;
    private IClipboardHistoryService? _clipboardHistoryService;
    public IClipboardHistoryService? ClipboardHistoryService => _clipboardHistoryService;
    private System.Windows.Interop.HwndSource? _clipboardHwndSource;
    private SingleInstanceService? _singleInstanceService;
    private TrayIconService? _trayIconService;
    private SettingsWindow? _settingsWindow;
    private IShortcutListener? _shortcutListener;
    private IAbbreviationExpanderService? _abbreviationExpander;
    private IActionExecutor? _executor;
    private IConfigRepository? _repository;
    public IConfigRepository? Repository => _repository;
    private ILogManagerService? _logManagerService;
    private Serilog.Core.LoggingLevelSwitch _levelSwitch = new();
    private IReadOnlyList<TriggerItem> _cachedItems = [];
    private AppSettings _cachedSettings = new();
    public AppSettings CachedSettings => _cachedSettings;
    private ChordHudView? _activeChordHud;

    public static UpdateCheckResult? LatestAvailableUpdate { get; set; }

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

        if (settings.CheatSheetHotkey != null && !settings.CheatSheetHotkey.IsEmpty)
        {
            list.Add(new TriggerItem
            {
                Id = CheatSheetActionId,
                Name = "Shortcut Cheat Sheet HUD",
                Hotkey = settings.CheatSheetHotkey,
                IsEnabled = true,
                PresentationMode = PresentationMode.Direct
            });
        }

        if (settings.EnableClipboardHistory && settings.ClipboardHotkey != null && !settings.ClipboardHotkey.IsEmpty)
        {
            list.Add(new TriggerItem
            {
                Id = ClipboardHistoryActionId,
                Name = "Clipboard History (@clip)",
                Hotkey = settings.ClipboardHotkey,
                IsEnabled = true,
                PresentationMode = PresentationMode.Direct
            });
        }

        return list;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var pathsService = new AppPathsService(e.Args);
        pathsService.EnsureDirectoriesCreated();

        var baseDir = pathsService.BaseDataDirectory;
        var logDir = pathsService.LogsDirectory;

        // Bootstrap logger to guarantee early diagnostics are never lost before settings load
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.File(
                Path.Combine(logDir, "triggerpoint-.log"),
                rollingInterval: RollingInterval.Day,
                flushToDiskInterval: TimeSpan.FromSeconds(1),
                shared: true,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}")
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        // Global unhandled exception handlers for fatal logging and crash prevention
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            Log.Fatal(ex, "FATAL: Unhandled AppDomain exception encountered. IsTerminating={IsTerminating}", args.IsTerminating);
            Log.CloseAndFlush();
        };

        DispatcherUnhandledException += (s, args) =>
        {
            Log.Error(args.Exception, "Unhandled Dispatcher exception caught.");
            Log.CloseAndFlush();
            // Prevent termination if possible
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            Log.Error(args.Exception, "Unobserved TaskException caught.");
            Log.CloseAndFlush();
            args.SetObserved();
        };

        // 1. Setup Configuration Repository early to load AppSettings
        var bootstrapVault = pathsService.IsPortable
            ? (ISecretsVaultService)new PortableAesSecretsVaultService(pathsService.VaultKeyFilePath)
            : new WindowsDpapiSecretsVaultService();
        var tempRepo = new JsonConfigRepository(baseDir, bootstrapVault);
        AppSettings appSettings;
        try
        {
            appSettings = await tempRepo.LoadSettingsAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load AppSettings during bootstrap; using defaults.");
            appSettings = new AppSettings();
        }

        // 2. Configure Serilog logging with dynamic LoggingLevelSwitch and configured settings
        _levelSwitch = new Serilog.Core.LoggingLevelSwitch(LogManagerService.ToLogEventLevel(appSettings.LogLevel));

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(_levelSwitch)
            .WriteTo.File(
                Path.Combine(logDir, "triggerpoint-.log"), 
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: (long)appSettings.LogSplitThresholdMb * 1024L * 1024L,
                rollOnFileSizeLimit: true,
                shared: true,
                flushToDiskInterval: TimeSpan.FromSeconds(1),
                retainedFileCountLimit: appSettings.LogRetentionDays,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}")
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        var startupArgs = e.Args ?? [];

        Log.Information("TriggerPoint daemon started. Version: {Version}, LogLevel: {LogLevel}, Retention: {Retention}d, Threshold: {SplitThreshold}MB, Args: {Args}", 
            typeof(App).Assembly.GetName().Version, appSettings.LogLevel, appSettings.LogRetentionDays, appSettings.LogSplitThresholdMb, string.Join(" ", startupArgs));

        // 2.1 Upgrade Detection & Pre-Upgrade Immutable Backup
        var runningVersion = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        var infoAttr = (System.Reflection.AssemblyInformationalVersionAttribute?)
            Attribute.GetCustomAttribute(typeof(App).Assembly, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
        if (!string.IsNullOrEmpty(infoAttr?.InformationalVersion))
        {
            var v = infoAttr.InformationalVersion;
            var plusIdx = v.IndexOf('+');
            if (plusIdx > 0) v = v.Substring(0, plusIdx);
            runningVersion = v.Trim().TrimStart('v', 'V');
        }

        if (!string.IsNullOrWhiteSpace(appSettings.LastKnownAppVersion) &&
            !string.Equals(appSettings.LastKnownAppVersion, runningVersion, StringComparison.OrdinalIgnoreCase))
        {
            Log.Information("Version upgrade detected: v{OldVersion} -> v{NewVersion}. Creating pre-upgrade backup.",
                appSettings.LastKnownAppVersion, runningVersion);
            try
            {
                await tempRepo.CreatePreUpgradeBackupAsync(appSettings.LastKnownAppVersion, runningVersion);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to create pre-upgrade backup.");
            }

            appSettings.LastKnownAppVersion = runningVersion;
            try
            {
                await tempRepo.SaveSettingsAsync(appSettings);
            }
            catch { }
        }
        else if (string.IsNullOrWhiteSpace(appSettings.LastKnownAppVersion))
        {
            appSettings.LastKnownAppVersion = runningVersion;
            try
            {
                await tempRepo.SaveSettingsAsync(appSettings);
            }
            catch { }
        }

        bool isPostUpdate = startupArgs.Any(a => string.Equals(a, "--post-update", StringComparison.OrdinalIgnoreCase));

        // Self-elevation via Task Scheduler if RunAsAdminAtStartup is configured and currently running un-elevated
        if (appSettings.RunAsAdminAtStartup && 
            !TriggerPoint.Infrastructure.Services.WindowsServiceManager.IsRunningElevated() &&
            !startupArgs.Contains("--no-self-elevate", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = "/run /tn \"TriggerPoint\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit(1500);
                    if (proc.ExitCode == 0)
                    {
                        Log.Information("Delegated execution to elevated Task Scheduler instance 'TriggerPoint'. Exiting un-elevated process.");
                        Shutdown();
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to self-elevate via Task Scheduler 'TriggerPoint'. Continuing in standard mode.");
            }
        }

        // 3. Single-Instance Enforcement
        string? scope = pathsService.IsPortable ? SingleInstanceService.ComputeScopeHash(pathsService.BaseDataDirectory) : null;
        _singleInstanceService = new SingleInstanceService(scope);
        if (!_singleInstanceService.TryAcquire())
        {
            Log.Information("Secondary instance detected (Scope: {Scope}). Signaling primary instance and shutting down.", scope ?? "default");
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
            await SingleInstanceService.SignalPrimaryInstanceAsync(string.Join(" ", startupArgs), scope);
            Shutdown();
            return;
        }

        _singleInstanceService.SecondInstanceSignaled += OnSecondInstanceSignaled;

        // 4. Initialize Theme Manager with user's theme preference
        _cachedSettings = appSettings;
        ThemeManager.Initialize(appSettings.Theme, appSettings.AccentColor, appSettings.CustomAccentHex);

        // 4.0 First-Run Setup (Portable edition or fresh unconfigured install)
        // If triggerpoint.json already exists with valid data or is non-empty, this is NOT a fresh install!
        bool configAlreadyExists = File.Exists(pathsService.ConfigFilePath) && 
                                   new FileInfo(pathsService.ConfigFilePath).Length > 10;

        if (configAlreadyExists && !appSettings.HasCompletedInitialSetup)
        {
            appSettings.HasCompletedInitialSetup = true;
            try { await tempRepo.SaveSettingsAsync(appSettings); } catch { }
        }

        bool isFirstRun = !appSettings.HasCompletedInitialSetup && 
                          !configAlreadyExists &&
                          (!File.Exists(pathsService.AppSettingsFilePath) || !File.Exists(pathsService.ConfigFilePath));

        if (isFirstRun && !startupArgs.Contains("--minimized", StringComparer.OrdinalIgnoreCase))
        {
            var setupWindow = new FirstRunSetupWindow(appSettings.Theme, initialStarterPack: true);
            if (setupWindow.ShowDialog() == true)
            {
                appSettings.Theme = setupWindow.SelectedTheme;
                appSettings.HasCompletedInitialSetup = true;
                ThemeManager.ApplyPreference(appSettings.Theme);
                await tempRepo.InitializeSetupAsync(appSettings.Theme, setupWindow.InstallStarterPack);
            }
            else
            {
                appSettings.HasCompletedInitialSetup = true;
                await tempRepo.SaveSettingsAsync(appSettings);
            }
        }

        // 4.1 Reconcile any orphaned registry entries from previous portable sessions or missing drives
        ExplorerContextMenuHelper.ReconcileOrphanedRegistrations();

        // 4.2 Proactively listen for USB drive ejection/removal to scrub host registry
        System.Windows.Interop.ComponentDispatcher.ThreadFilterMessage += (ref System.Windows.Interop.MSG msg, ref bool handled) =>
        {
            const int WM_DEVICECHANGE = 0x0219;
            const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
            if (msg.message == WM_DEVICECHANGE && (int)msg.wParam == DBT_DEVICEREMOVECOMPLETE)
            {
                ExplorerContextMenuHelper.ReconcileOrphanedRegistrations();
            }
        };

        // 4.3 Listen for process exit & session ending to scrub host integrations if enabled
        AppDomain.CurrentDomain.ProcessExit += (s, ev) =>
        {
            try
            {
                if (appSettings.CleanupHostIntegrationOnExit || pathsService.IsPortable)
                {
                    ExplorerContextMenuHelper.UnregisterPortableIntegrations();
                }
            }
            catch { }
        };

        Microsoft.Win32.SystemEvents.SessionEnding += (s, ev) =>
        {
            try
            {
                if (appSettings.CleanupHostIntegrationOnExit || pathsService.IsPortable)
                {
                    ExplorerContextMenuHelper.UnregisterPortableIntegrations();
                }
            }
            catch { }
        };

        // 5. Setup Dependency Injection
        var services = new ServiceCollection();
        services.AddSingleton<IAppPathsService>(pathsService);
        _logManagerService = new LogManagerService(_levelSwitch, logDir);
        services.AddSingleton<ILogManagerService>(_logManagerService);

        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        _repository = _serviceProvider.GetRequiredService<IConfigRepository>();
        _shortcutListener = _serviceProvider.GetRequiredService<IShortcutListener>();
        _executor = _serviceProvider.GetRequiredService<IActionExecutor>();
        var toastService = _serviceProvider.GetRequiredService<IToastNotificationService>();
        _clipboardHistoryService = _serviceProvider.GetRequiredService<IClipboardHistoryService>();
        try
        {
            var hwndParams = new System.Windows.Interop.HwndSourceParameters("TriggerPoint_ClipboardListener")
            {
                Width = 0,
                Height = 0,
                PositionX = -32000,
                PositionY = -32000,
                WindowStyle = 0x800000
            };
            _clipboardHwndSource = new System.Windows.Interop.HwndSource(hwndParams);
            _clipboardHwndSource.AddHook(ClipboardWndProc);
            _clipboardHistoryService.Start(_clipboardHwndSource.Handle);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to initialize clipboard format listener window.");
        }

        // Wire executor open settings and toast notifications
        if (_executor is ShellActionExecutor shellExec)
        {
            shellExec.OpenSettingsRequested += item => Dispatcher.Invoke(() => ShowSettingsWindow(item));
            shellExec.ExecutionSucceeded += (item, detail) =>
            {
                // Snippet text expansions and Windows Services provide their own dedicated feedback
                if (item.ActionType == ActionType.Snippet || item.ActionType == ActionType.Service)
                {
                    return;
                }

                if (appSettings.ShowSuccessToasts)
                {
                    toastService.ShowSuccess(item.Name, detail);
                }
            };
            shellExec.ExecutionFailed += (item, error) =>
            {
                // Service operations render errors directly on their progress HUD
                if (item.ActionType == ActionType.Service)
                {
                    return;
                }

                toastService.ShowError($"Failed to launch '{item.Name}'", error);
            };
        }

        // 6. Initialize Win32 Hotkey Listener
        _shortcutListener.Start(IntPtr.Zero);
        _shortcutListener.HotkeyTriggered += ShortcutListener_HotkeyTriggered;
        _shortcutListener.ChordWaiting += (s, e) => Dispatcher.Invoke(() => ShowChordHud(e.Leader, e.Candidates));
        _shortcutListener.ChordCompleted += (s, e) => Dispatcher.Invoke(HideChordHud);

        // 7. Setup Settings Window
        var contextFilterService = _serviceProvider.GetRequiredService<IContextFilterService>();
        contextFilterService.SetAllItemsProvider(() =>
        {
            if (_cachedItems.Count == 0 && _repository != null)
            {
                try
                {
                    _cachedItems = _repository.LoadAsync().GetAwaiter().GetResult();
                }
                catch
                {
                    // fallback to empty if repository read fails synchronously
                }
            }
            return _cachedItems;
        });
        var workflowExecutor = _serviceProvider.GetRequiredService<IWorkflowExecutor>();
        var browserDetectionService = _serviceProvider.GetRequiredService<IBrowserDetectionService>();

        async Task<bool> ExecuteItemOrFolderAsync(TriggerItem targetItem, IntPtr? hwnd)
        {
            if (targetItem.ActionType == ActionType.Folder)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (targetItem.PresentationMode == PresentationMode.CursorMenu)
                    {
                        OpenCursorMenu(targetItem, hwnd ?? IntPtr.Zero);
                    }
                    else
                    {
                        OpenCommandPalette(targetItem, hwnd ?? IntPtr.Zero);
                    }
                });
                return true;
            }

            await _executor.ExecuteAsync(targetItem, ExecutionOverride.Standard, hwnd);
            return true;
        }

        workflowExecutor.ActionExecutionHandler = async (targetId, hwnd) =>
        {
            if (_repository == null || _executor == null) return false;
            var allItems = await _repository.LoadAsync();
            var targetItem = allItems.FirstOrDefault(x => x.Id == targetId);
            if (targetItem == null) return false;
            return await ExecuteItemOrFolderAsync(targetItem, hwnd);
        };

        var scriptEngineService = _serviceProvider.GetRequiredService<IScriptEngineService>();
        scriptEngineService.ActionExecutionHandler = async (idOrName, hwnd) =>
        {
            if (_repository == null || _executor == null) return false;
            var allItems = await _repository.LoadAsync();
            var targetItem = allItems.FirstOrDefault(x =>
                x.Id.ToString().Equals(idOrName, StringComparison.OrdinalIgnoreCase) ||
                x.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase));
            if (targetItem == null) return false;
            return await ExecuteItemOrFolderAsync(targetItem, hwnd);
        };

        // 8. Load and register hotkeys (including global application shortcuts)
        var items = await _repository.LoadAsync();
        _cachedItems = items;
        var allItems = new List<TriggerItem>(items);
        allItems.AddRange(CreateVirtualApplicationItems(appSettings));
        _shortcutListener.RegisterAll(allItems);

        _abbreviationExpander = _serviceProvider.GetRequiredService<IAbbreviationExpanderService>();
        _abbreviationExpander.UpdateSnippets(items);
        _abbreviationExpander.Start();

        // Suspend global hotkeys while user records a shortcut so keys pass cleanly to UI
        HotkeyRecorderControl.RecordingStarted += (s, e) => _shortcutListener.Suspend();
        HotkeyRecorderControl.RecordingStopped += (s, e) => _shortcutListener.Resume();

        // 9. Setup System Tray Icon
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
            commandPaletteHotkeyText: appSettings.CommandPaletteHotkey?.DisplayText ?? "Ctrl+Shift+Space",
            openSettingsHotkeyText: appSettings.OpenSettingsHotkey?.DisplayText ?? "Ctrl+Alt+T",
            checkForUpdatesAction: () => Dispatcher.InvokeAsync(async () => await PerformManualUpdateCheckAsync()),
            openCheatSheetAction: () => Dispatcher.Invoke(() => OpenCheatSheetHud()),
            cheatSheetHotkeyText: appSettings.CheatSheetHotkey?.DisplayText ?? "Ctrl+Shift+/",
            openClipboardAction: () => Dispatcher.Invoke(() => OpenCommandPalette(initialFilter: CommandPaletteFilterType.Clipboard)),
            clipboardHotkeyText: appSettings.ClipboardHotkey?.DisplayText ?? "Ctrl+Shift+V",
            enableClipboardHistory: appSettings.EnableClipboardHistory);

        if (TryExtractAddActionPath(startupArgs, out var addPath))
        {
            ShowSettingsWindowAndCreateActionForPath(addPath);
        }
        else if (isPostUpdate)
        {
            ShowSettingsWindow();
        }
        else
        {
            // Show action settings window if not configured to start minimized or if launched with --settings
            bool shouldStartMinimized = appSettings.StartMinimized || startupArgs.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
            if (!shouldStartMinimized || startupArgs.Contains("--settings", StringComparer.OrdinalIgnoreCase))
            {
                ShowSettingsWindow();
            }
        }

        if (isPostUpdate)
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(250);
                ShowSettingsWindow();
                if (_settingsWindow != null)
                {
                    string msg = $"TriggerPoint has been successfully updated to v{runningVersion}!\n\nAll custom shortcuts, actions, and settings have been safely preserved.";
                    ModernMessageDialog.ShowAlert(
                        _settingsWindow,
                        "🎉 Update Installed Successfully",
                        msg,
                        ModernDialogType.Info);
                }
                toastService.ShowSuccess(
                    "TriggerPoint Updated",
                    $"Successfully updated to v{runningVersion}!");
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // 10. Defer background maintenance (recycle bin purge & shortcut health check) by 3 seconds for instant cold startup
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(3000);
                await _repository.PurgeRecycleBinAsync(appSettings.RecycleBinRetentionDays);

                if (appSettings.ValidateShortcutsOnStartup)
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
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Deferred background maintenance encountered an error.");
            }
        });

        // 11. Optional delayed startup update check
        _ = Task.Run(async () =>
        {
            try
            {
                // Delay 4 seconds so startup and UI loading remain totally instantaneous
                await Task.Delay(4000);

                var updateService = _serviceProvider?.GetService<IUpdateService>();
                if (updateService == null) return;

                // Check if update check is scheduled
                if (updateService.ShouldPerformScheduledCheck(appSettings))
                {
                    var result = await updateService.CheckForUpdatesAsync(isManualCheck: false);
                    if (result.IsUpdateAvailable && !result.IsIgnored && result.LatestUpdate != null)
                    {
                        LatestAvailableUpdate = result;
                        Dispatcher.Invoke(() =>
                        {
                            _settingsWindow?.NotifyUpdateAvailable(result);
                            _trayIconService?.SetUpdateAvailable(result);

                            bool isAutostart = IsWindowsStartupLaunch(startupArgs, appSettings);

                            if (!isAutostart && appSettings.ShowUpdateDialogOnStartup)
                            {
                                // Manual application launch: prompt with update dialog box
                                ShowLatestUpdateDialog();
                            }
                            else
                            {
                                // Windows autostart / restart (or dialog prompt disabled):
                                // Keep toast open until hover or dismissed, with 1-click Review & Update
                                toastService.ShowActionToast(
                                    "TriggerPoint Update Available",
                                    $"Version v{result.LatestUpdate.Version} is available! Click to review improvements.",
                                    actionButtonText: "Review & Update",
                                    onAction: () => ShowLatestUpdateDialog(),
                                    cancelButtonText: "Dismiss",
                                    keepOpenUntilHoverOrDismiss: true);
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Background update check encountered an error.");
            }
        });

        Log.Information("TriggerPoint daemon initialization complete. Running in background.");
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ISecretsVaultService>(sp =>
        {
            var paths = sp.GetRequiredService<IAppPathsService>();
            if (paths.IsPortable)
            {
                return new PortableAesSecretsVaultService(paths.VaultKeyFilePath);
            }
            return new WindowsDpapiSecretsVaultService();
        });
        services.AddSingleton<IConfigRepository>(sp => new JsonConfigRepository(sp.GetRequiredService<IAppPathsService>().BaseDataDirectory, sp.GetRequiredService<ISecretsVaultService>()));
        services.AddSingleton<IPromptDialogService, InteractivePromptDialog>();
        services.AddSingleton<IConfirmationDialogService, ConfirmationDialog>();
        services.AddSingleton<ISnippetService, Win32SnippetService>();
        services.AddSingleton<IContextFilterService, ContextFilterService>();
        services.AddSingleton<IIconService, Win32IconService>();
        services.AddSingleton<ITelemetryService, TelemetryService>();
        services.AddSingleton<IProductivityService, ProductivityService>();
        services.AddSingleton<IShortcutListener, Win32HotkeyListener>();
        services.AddSingleton<IBrowserDetectionService, BrowserDetectionService>();
        services.AddSingleton<IScriptEngineService, JintScriptEngineService>();
        services.AddSingleton<IWorkflowExecutor, WorkflowExecutor>();
        services.AddSingleton<IMacroService, Win32MacroService>();
        services.AddSingleton<IActionExecutor, ShellActionExecutor>();
        services.AddSingleton<IToastNotificationService, ToastNotificationService>();
        services.AddSingleton<IWorkflowTemplateService, WorkflowTemplateService>();
        services.AddSingleton<IUpdateService, GitHubUpdateService>();
        services.AddSingleton<IWindowsServiceManager, WindowsServiceManager>();
        services.AddSingleton<IClipboardHistoryService, ClipboardHistoryService>();
        services.AddSingleton<IAbbreviationExpanderService>(sp => new AbbreviationExpanderService(
            sp.GetRequiredService<ISnippetService>(),
            sp.GetRequiredService<IContextFilterService>(),
            () => (Application.Current as App)?.CachedSettings ?? new AppSettings(),
            sp.GetService<IProductivityService>(),
            sp.GetService<ITelemetryService>(),
            sp.GetService<IToastNotificationService>()));
    }

    private IntPtr ClipboardWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_CLIPBOARDUPDATE = 0x031D;
        if (msg == WM_CLIPBOARDUPDATE)
        {
            _clipboardHistoryService?.HandleClipboardUpdateMessage();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void ShortcutListener_HotkeyTriggered(object? sender, TriggerItem item)
    {
        // Record productivity telemetry for hotkey activation
        _serviceProvider?.GetService<IProductivityService>()?.RecordHotkeyTrigger(item.Id, item.Name);

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

            if (item.Id == CheatSheetActionId)
            {
                OpenCheatSheetHud(targetHwnd: targetHwnd);
                return;
            }

            if (item.Id == ClipboardHistoryActionId)
            {
                OpenCommandPalette(targetHwnd: targetHwnd, initialFilter: CommandPaletteFilterType.Clipboard);
                return;
            }

            var filterService = _serviceProvider?.GetService<IContextFilterService>();
            if (filterService != null && !filterService.ShouldExecute(item, _cachedItems))
            {
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

    public void OpenCursorMenu(TriggerItem triggerItem, IntPtr targetHwnd = default)
    {
        if (_repository == null || _executor == null) return;

        _ = Task.Run(async () =>
        {
            var allItems = await _repository.LoadAsync();

            await Dispatcher.InvokeAsync(() =>
            {
                var filterService = _serviceProvider?.GetService<IContextFilterService>();
                var menu = new CursorContextMenuView(allItems, _executor, triggerItem, targetHwnd, filterService);
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

    private void OpenCommandPalette(TriggerItem? triggerItem = null, IntPtr targetHwnd = default, CommandPaletteFilterType initialFilter = CommandPaletteFilterType.All)
    {
        if (_repository == null || _executor == null)
        {
            Log.Warning("OpenCommandPalette aborted: repository or executor is not initialized.");
            return;
        }

        _ = Task.Run(async () =>
        {
            try
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
                    try
                    {
                        PaletteItemViewModel.DefaultAbbreviationCaseSensitive = _cachedSettings?.AbbreviationCaseSensitive ?? true;
                        var serviceManager = _serviceProvider?.GetService<IWindowsServiceManager>();
                        var palette = new CommandPaletteView(allItems, _executor, _repository, scopeId, scopeName, targetHwnd, serviceManager, _clipboardHistoryService, initialFilter);
                        palette.Show();
                        palette.Activate();
                        try
                        {
                            var handle = new System.Windows.Interop.WindowInteropHelper(palette).Handle;
                            NativeMethods.SetForegroundWindow(handle);
                        }
                        catch { }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to instantiate or show CommandPaletteView on Dispatcher.");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to prepare Command Palette items in background task.");
            }
        });
    }

    private void OpenCheatSheetHud(IntPtr targetHwnd = default)
    {
        if (_repository == null || _executor == null) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var settings = await _repository.LoadSettingsAsync();
                var items = await _repository.LoadAsync();
                var allItems = new List<TriggerItem>(items);
                allItems.AddRange(CreateVirtualApplicationItems(settings));

                var filterService = _serviceProvider?.GetService<IContextFilterService>();
                string? activeProcName = filterService?.GetForegroundProcessName();

                await Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        var hud = new CheatSheetHudView(
                            allItems,
                            settings,
                            activeProcName,
                            onExecute: async targetItem =>
                            {
                                if (targetItem.Id == OpenSettingsActionId)
                                {
                                    ShowSettingsWindow();
                                }
                                else if (targetItem.Id == CommandPaletteActionId)
                                {
                                    OpenCommandPalette(targetHwnd: targetHwnd);
                                }
                                else if (targetItem.Id == ClipboardHistoryActionId)
                                {
                                    OpenCommandPalette(targetHwnd: targetHwnd, initialFilter: CommandPaletteFilterType.Clipboard);
                                }
                                else if (_executor != null)
                                {
                                    await _executor.ExecuteAsync(targetItem, ExecutionOverride.Standard, targetHwnd);
                                }
                            });

                        hud.Show();
                        hud.Activate();
                        try
                        {
                            var handle = new System.Windows.Interop.WindowInteropHelper(hud).Handle;
                            NativeMethods.SetForegroundWindow(handle);
                        }
                        catch { }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to instantiate or show CheatSheetHudView.");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to prepare Cheat Sheet HUD in background task.");
            }
        });
    }

    private void ShowChordHud(ShortcutBinding leader, IReadOnlyList<TriggerItem> candidates)
    {
        HideChordHud();
        try
        {
            _activeChordHud = new ChordHudView(leader, candidates);
            _activeChordHud.Show();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to display Chord HUD.");
        }
    }

    private void HideChordHud()
    {
        try
        {
            if (_activeChordHud != null)
            {
                _activeChordHud.Close();
                _activeChordHud = null;
            }
        }
        catch { }
    }

    public void ShowSettingsWindowAndCreate(string initialName, Guid? parentFolderId = null)
    {
        ShowSettingsWindow();
        _settingsWindow?.CreateAndEditNewItem(initialName, parentFolderId: parentFolderId);
    }

    public void ShowSettingsWindowAndCreateActionForPath(string path)
    {
        ShowSettingsWindow();
        _settingsWindow?.CreateAndEditShellActionForPath(path);
    }

    private void EnsureSettingsWindowCreated()
    {
        if (_settingsWindow != null || _serviceProvider == null || _repository == null || _shortcutListener == null || _executor == null || _logManagerService == null)
            return;

        var contextFilterService = _serviceProvider.GetRequiredService<IContextFilterService>();
        var workflowExecutor = _serviceProvider.GetRequiredService<IWorkflowExecutor>();
        var browserDetectionService = _serviceProvider.GetRequiredService<IBrowserDetectionService>();
        var macroService = _serviceProvider.GetRequiredService<IMacroService>();
        var workflowTemplateService = _serviceProvider.GetRequiredService<IWorkflowTemplateService>();
        var updateService = _serviceProvider.GetRequiredService<IUpdateService>();

        var serviceManager = _serviceProvider.GetService<IWindowsServiceManager>();

        _settingsWindow = new SettingsWindow(
            _repository,
            _shortcutListener,
            _executor,
            contextFilterService,
            _logManagerService,
            workflowExecutor,
            browserDetectionService,
            macroService,
            workflowTemplateService,
            updateService,
            _abbreviationExpander,
            serviceManager);

        if (LatestAvailableUpdate != null)
        {
            _settingsWindow.NotifyUpdateAvailable(LatestAvailableUpdate);
        }

        MainWindow = _settingsWindow;
    }

    public void ShowSettingsWindow(TriggerItem? focusedItem = null)
    {
        EnsureSettingsWindowCreated();
        if (_settingsWindow == null) return;

        bool wasHidden = !_settingsWindow.IsVisible || _settingsWindow.WindowState == WindowState.Minimized;
        if (_settingsWindow.WindowState == WindowState.Minimized)
        {
            _settingsWindow.WindowState = WindowState.Normal;
        }

        if (wasHidden)
        {
            _settingsWindow.ApplyWindowPlacement();
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

    public async Task ShowApplicationSettingsWindowAsync(ApplicationSettingsWindow.SettingsCategory? initialCategory = null)
    {
        if (_repository == null || _logManagerService == null) return;

        var updateService = _serviceProvider?.GetService<IUpdateService>();
        var pathsService = _serviceProvider?.GetService<IAppPathsService>();
        var prodService = _serviceProvider?.GetService<IProductivityService>();
        var clipService = _clipboardHistoryService;
        var appSettingsWin = initialCategory.HasValue
            ? new ApplicationSettingsWindow(_repository, _logManagerService, updateService, initialCategory.Value, pathsService, prodService, clipService)
            : new ApplicationSettingsWindow(_repository, _logManagerService, updateService, ApplicationSettingsWindow.SettingsCategory.Appearance, pathsService, prodService, clipService);

        if (_settingsWindow != null && _settingsWindow.IsVisible && _settingsWindow.WindowState != WindowState.Minimized)
        {
            appSettingsWin.Owner = _settingsWindow;
        }

        appSettingsWin.ShowDialog();

        await ReloadApplicationSettingsAndHotkeysAsync();
    }

    public void ShowApplicationSettingsWindow(ApplicationSettingsWindow.SettingsCategory? initialCategory = null)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            await ShowApplicationSettingsWindowAsync(initialCategory);
        });
    }

    public async Task PerformManualUpdateCheckAsync()
    {
        if (_serviceProvider == null || _repository == null) return;
        var updateService = _serviceProvider.GetRequiredService<IUpdateService>();
        var toastService = _serviceProvider.GetRequiredService<IToastNotificationService>();
        var pathsService = _serviceProvider.GetService<IAppPathsService>();

        // Immediate visual progress indicator so the user knows their click was registered
        using var progressHandle = toastService.ShowProgress("TriggerPoint Updates", "Checking GitHub Releases for updates...");

        try
        {
            var result = await updateService.CheckForUpdatesAsync(isManualCheck: true);

            // Close progress indicator before showing result dialog
            progressHandle?.Dispose();

            if (result.IsUpdateAvailable && result.LatestUpdate != null)
            {
                LatestAvailableUpdate = result;
                _settingsWindow?.NotifyUpdateAvailable(result);
                _trayIconService?.SetUpdateAvailable(result);

                var dlg = new UpdateAvailableDialog(result, updateService, _repository, pathsService);
                dlg.Show();
                dlg.Activate();
            }
            else if (result.IsSuccess)
            {
                LatestAvailableUpdate = result;
                bool viewNotes = ModernMessageDialog.ShowConfirm(
                    null,
                    "TriggerPoint is Up to Date",
                    $"You are running TriggerPoint v{updateService.GetCurrentVersion()}.\nNo newer releases were found on GitHub.\n\nWould you like to view the latest release notes?",
                    primaryText: "📋 View Release Notes",
                    secondaryText: "Close");

                if (viewNotes && result.LatestUpdate != null)
                {
                    var viewDlg = new UpdateAvailableDialog(result, updateService, _repository, pathsService, isViewOnly: true);
                    viewDlg.Show();
                    viewDlg.Activate();
                }
            }
            else
            {
                ModernMessageDialog.ShowAlert(
                    null,
                    "Update Check Failed",
                    result.ErrorMessage ?? "Could not connect to GitHub to check for updates.",
                    ModernDialogType.Warning);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to perform manual update check.");
            progressHandle?.Dispose();
            ModernMessageDialog.ShowAlert(
                null,
                "Update Check Error",
                $"An unexpected error occurred while checking for updates:\n{ex.Message}",
                ModernDialogType.Error);
        }
    }

    public void ShowLatestUpdateDialog()
    {
        if (LatestAvailableUpdate == null || _serviceProvider == null || _repository == null) return;
        var updateService = _serviceProvider.GetService<IUpdateService>();
        if (updateService == null) return;
        var pathsService = _serviceProvider.GetService<IAppPathsService>();

        Dispatcher.Invoke(() =>
        {
            var dlg = new UpdateAvailableDialog(LatestAvailableUpdate, updateService, _repository, pathsService);
            dlg.Show();
            dlg.Activate();
        });
    }

    public static bool IsWindowsStartupLaunch(string[] startupArgs, AppSettings appSettings)
    {
        // 1. Explicit CLI arguments registered in Windows Startup or Task Scheduler
        if (startupArgs.Any(a => string.Equals(a, "--autostart", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // 2. Safeguard for existing installs: If RunAtStartup is configured and system uptime is < 5 minutes,
        // TriggerPoint was spawned as part of the Windows user session boot sequence.
        if (appSettings.RunAtStartup && Environment.TickCount64 < 300_000)
        {
            return true;
        }

        return false;
    }

    public async Task ReloadApplicationSettingsAndHotkeysAsync()
    {
        if (_repository == null || _shortcutListener == null) return;
        try
        {
            var settings = await _repository.LoadSettingsAsync();
            _cachedSettings = settings;
            PaletteItemViewModel.DefaultAbbreviationCaseSensitive = settings.AbbreviationCaseSensitive;
            var items = await _repository.LoadAsync();
            _cachedItems = items;
            var allItems = new List<TriggerItem>(items);
            allItems.AddRange(CreateVirtualApplicationItems(settings));
            _shortcutListener.RegisterAll(allItems);
            _abbreviationExpander?.UpdateSnippets(allItems);
            if (settings.EnableAbbreviationExpander && _abbreviationExpander?.IsRunning == false)
            {
                _abbreviationExpander.Start();
            }
            else if (!settings.EnableAbbreviationExpander && _abbreviationExpander?.IsRunning == true)
            {
                _abbreviationExpander.Stop();
            }
            _trayIconService?.UpdateCommandPaletteHotkey(settings.CommandPaletteHotkey?.DisplayText);
            _trayIconService?.UpdateOpenSettingsHotkey(settings.OpenSettingsHotkey?.DisplayText);
            _trayIconService?.UpdateCheatSheetHotkey(settings.CheatSheetHotkey?.DisplayText);
            _trayIconService?.UpdateClipboardHistoryHotkey(settings.ClipboardHotkey?.DisplayText, settings.EnableClipboardHistory);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to reload application settings and hotkeys.");
        }
    }

    public void UpdateCachedSettings(AppSettings settings)
    {
        _cachedSettings = settings;
        PaletteItemViewModel.DefaultAbbreviationCaseSensitive = settings.AbbreviationCaseSensitive;
        if (_abbreviationExpander != null)
        {
            if (settings.EnableAbbreviationExpander)
            {
                if (!_abbreviationExpander.IsRunning)
                {
                    _abbreviationExpander.Start();
                }
                var itemsToUpdate = (_cachedItems != null && _cachedItems.Count > 0)
                    ? _cachedItems
                    : (_repository != null ? _repository.LoadAsync().GetAwaiter().GetResult() : []);
                if (itemsToUpdate != null && itemsToUpdate.Count > 0)
                {
                    _abbreviationExpander.UpdateSnippets(itemsToUpdate);
                }
            }
            else if (_abbreviationExpander.IsRunning)
            {
                _abbreviationExpander.Stop();
            }
        }
    }

    public void SetCachedItems(IReadOnlyList<TriggerItem> items)
    {
        _cachedItems = items;
    }

    private void OnSecondInstanceSignaled(string message)
    {
        Dispatcher.Invoke(() =>
        {
            Log.Information("Secondary instance signaled message: '{Message}'", message);
            if (TryExtractAddActionPath(message, out var addPath))
            {
                ShowSettingsWindowAndCreateActionForPath(addPath);
                return;
            }

            if (!message.Contains("--minimized", StringComparison.OrdinalIgnoreCase))
            {
                ShowSettingsWindow();
            }
        });
    }

    private static bool TryExtractAddActionPath(string[] args, out string path)
    {
        path = string.Empty;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--add-action", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                path = args[i + 1].Trim('\"');
                return !string.IsNullOrWhiteSpace(path);
            }
        }
        return false;
    }

    private static bool TryExtractAddActionPath(string rawMessage, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(rawMessage)) return false;

        const string prefix = "--add-action";
        int idx = rawMessage.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var rest = rawMessage[(idx + prefix.Length)..].Trim().Trim('\"');
            if (!string.IsNullOrWhiteSpace(rest))
            {
                path = rest;
                return true;
            }
        }
        return false;
    }

    private void ExitApplication()
    {
        Log.Information("Shutting down TriggerPoint daemon...");

        if (_settingsWindow != null)
        {
            _settingsWindow.IsExiting = true;
            _settingsWindow.Close();
        }

        try
        {
            var paths = _serviceProvider?.GetService<IAppPathsService>();
            if (paths?.IsPortable == true)
            {
                ExplorerContextMenuHelper.UnregisterPortableIntegrations();
            }
        }
        catch { }

        _trayIconService?.Dispose();
        _shortcutListener?.Dispose();
        _abbreviationExpander?.Dispose();
        _clipboardHistoryService?.Stop();
        _clipboardHwndSource?.Dispose();
        _singleInstanceService?.Dispose();

        Log.CloseAndFlush();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("TriggerPoint exiting with code {ExitCode}.", e.ApplicationExitCode);
        try
        {
            var paths = _serviceProvider?.GetService<IAppPathsService>();
            if (paths?.IsPortable == true)
            {
                ExplorerContextMenuHelper.UnregisterPortableIntegrations();
            }
        }
        catch { }

        _trayIconService?.Dispose();
        _shortcutListener?.Dispose();
        _abbreviationExpander?.Dispose();
        _clipboardHistoryService?.Stop();
        _clipboardHwndSource?.Dispose();
        _singleInstanceService?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
