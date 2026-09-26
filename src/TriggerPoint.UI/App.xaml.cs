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

    private IServiceProvider? _serviceProvider;
    public IServiceProvider? ServiceProvider => _serviceProvider;
    private SingleInstanceService? _singleInstanceService;
    private TrayIconService? _trayIconService;
    private SettingsWindow? _settingsWindow;
    private IShortcutListener? _shortcutListener;
    private IActionExecutor? _executor;
    private IConfigRepository? _repository;
    public IConfigRepository? Repository => _repository;
    private ILogManagerService? _logManagerService;
    private Serilog.Core.LoggingLevelSwitch _levelSwitch = new();
    private IReadOnlyList<TriggerItem> _cachedItems = [];
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

        Log.Information("TriggerPoint daemon started. Version: {Version}, LogLevel: {LogLevel}, Retention: {Retention}d, Threshold: {SplitThreshold}MB, Args: {Args}", 
            typeof(App).Assembly.GetName().Version, appSettings.LogLevel, appSettings.LogRetentionDays, appSettings.LogSplitThresholdMb, string.Join(" ", e.Args));

        // 3. Single-Instance Enforcement
        string? scope = pathsService.IsPortable ? SingleInstanceService.ComputeScopeHash(pathsService.BaseDataDirectory) : null;
        _singleInstanceService = new SingleInstanceService(scope);
        if (!_singleInstanceService.TryAcquire())
        {
            Log.Information("Secondary instance detected (Scope: {Scope}). Signaling primary instance and shutting down.", scope ?? "default");
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
            await SingleInstanceService.SignalPrimaryInstanceAsync(string.Join(" ", e.Args), scope);
            Shutdown();
            return;
        }

        _singleInstanceService.SecondInstanceSignaled += OnSecondInstanceSignaled;

        // 4. Initialize Theme Manager with user's theme preference
        ThemeManager.Initialize(appSettings.Theme);

        // 4.0 First-Run Setup (Portable edition or fresh unconfigured install)
        bool isFirstRun = !appSettings.HasCompletedInitialSetup && 
                          (!File.Exists(pathsService.AppSettingsFilePath) || !File.Exists(pathsService.ConfigFilePath));

        if (isFirstRun && !e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase))
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

        // Wire executor open settings and toast notifications
        if (_executor is ShellActionExecutor shellExec)
        {
            shellExec.OpenSettingsRequested += item => Dispatcher.Invoke(() => ShowSettingsWindow(item));
            shellExec.ExecutionSucceeded += (item, detail) =>
            {
                // Snippet text expansions are inline and must never display disruptive desktop toasts
                if (item.ActionType == ActionType.Snippet)
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
            checkForUpdatesAction: () => Dispatcher.Invoke(async () => await PerformManualUpdateCheckAsync()),
            openCheatSheetAction: () => Dispatcher.Invoke(() => OpenCheatSheetHud()),
            cheatSheetHotkeyText: appSettings.CheatSheetHotkey?.DisplayText ?? "Ctrl+Shift+/");

        if (TryExtractAddActionPath(e.Args, out var addPath))
        {
            ShowSettingsWindowAndCreateActionForPath(addPath);
        }
        else
        {
            // Show action settings window if not configured to start minimized or if launched with --settings
            bool shouldStartMinimized = appSettings.StartMinimized || e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
            if (!shouldStartMinimized || e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
            {
                ShowSettingsWindow();
            }
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

        // 11. Optional delayed startup update check and "What's New" notification
        _ = Task.Run(async () =>
        {
            try
            {
                // Delay 4 seconds so startup and UI loading remain totally instantaneous
                await Task.Delay(4000);

                var updateService = _serviceProvider?.GetService<IUpdateService>();
                if (updateService == null) return;

                // Check for "What's New" on first run after update
                var currentVer = updateService.GetCurrentVersion();
                if (!string.IsNullOrEmpty(appSettings.LastKnownAppVersion) &&
                    GitHubUpdateService.CompareVersions(currentVer, appSettings.LastKnownAppVersion) > 0)
                {
                    Dispatcher.Invoke(() =>
                    {
                        toastService.ShowSuccess(
                            "TriggerPoint Updated",
                            $"Successfully updated to v{currentVer}! Zero bloat, maximum speed.");
                    });
                    appSettings.LastKnownAppVersion = currentVer;
                    try { await _repository.SaveSettingsAsync(appSettings); } catch { }
                }
                else if (string.IsNullOrEmpty(appSettings.LastKnownAppVersion))
                {
                    appSettings.LastKnownAppVersion = currentVer;
                    try { await _repository.SaveSettingsAsync(appSettings); } catch { }
                }

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

                            // Unobtrusive toast that notifies the user
                            toastService.ShowSuccess(
                                "TriggerPoint Update Available",
                                $"Version v{result.LatestUpdate.Version} is available! Open Action Manager to review improvements.");
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
        services.AddSingleton<IShortcutListener, Win32HotkeyListener>();
        services.AddSingleton<IBrowserDetectionService, BrowserDetectionService>();
        services.AddSingleton<IScriptEngineService, JintScriptEngineService>();
        services.AddSingleton<IWorkflowExecutor, WorkflowExecutor>();
        services.AddSingleton<IMacroService, Win32MacroService>();
        services.AddSingleton<IActionExecutor, ShellActionExecutor>();
        services.AddSingleton<IToastNotificationService, ToastNotificationService>();
        services.AddSingleton<IWorkflowTemplateService, WorkflowTemplateService>();
        services.AddSingleton<IUpdateService, GitHubUpdateService>();
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

            if (item.Id == CheatSheetActionId)
            {
                OpenCheatSheetHud(targetHwnd: targetHwnd);
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

    private void OpenCommandPalette(TriggerItem? triggerItem = null, IntPtr targetHwnd = default)
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
                        var palette = new CommandPaletteView(allItems, _executor, _repository, scopeId, scopeName, targetHwnd);
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
            updateService);

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
        var appSettingsWin = initialCategory.HasValue
            ? new ApplicationSettingsWindow(_repository, _logManagerService, updateService, initialCategory.Value, pathsService)
            : new ApplicationSettingsWindow(_repository, _logManagerService, updateService, pathsService: pathsService);
        appSettingsWin.ShowDialog();

        await ReloadApplicationSettingsAndHotkeysAsync();
    }

    public void ShowApplicationSettingsWindow(ApplicationSettingsWindow.SettingsCategory? initialCategory = null)
    {
        _ = ShowApplicationSettingsWindowAsync(initialCategory);
    }

    public async Task PerformManualUpdateCheckAsync()
    {
        if (_serviceProvider == null || _repository == null) return;
        var updateService = _serviceProvider.GetRequiredService<IUpdateService>();
        var toastService = _serviceProvider.GetRequiredService<IToastNotificationService>();

        try
        {
            var result = await updateService.CheckForUpdatesAsync(isManualCheck: true);
            if (result.IsUpdateAvailable)
            {
                LatestAvailableUpdate = result;
                _settingsWindow?.NotifyUpdateAvailable(result);
                _trayIconService?.SetUpdateAvailable(result);

                var dlg = new UpdateAvailableDialog(result, updateService, _repository);
                dlg.ShowDialog();
            }
            else if (result.IsSuccess)
            {
                toastService.ShowSuccess("TriggerPoint", $"You're up to date! TriggerPoint v{updateService.GetCurrentVersion()} is the newest release.");
            }
            else
            {
                toastService.ShowWarning("Update Check", result.ErrorMessage ?? "Could not check for updates.");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to perform manual update check.");
        }
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
            _trayIconService?.UpdateCheatSheetHotkey(settings.CheatSheetHotkey?.DisplayText);
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
        _singleInstanceService?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
