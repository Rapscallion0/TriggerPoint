using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading.Tasks;
using Serilog;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;

namespace TriggerPoint.Infrastructure.Services;

public class WindowsServiceManager : IWindowsServiceManager
{
    private readonly ILogger _logger = Log.ForContext<WindowsServiceManager>();

    public static bool IsRunningElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public IReadOnlyList<WindowsServiceItem> GetServices()
    {
        var result = new List<WindowsServiceItem>();
        try
        {
            var controllers = ServiceController.GetServices();
            foreach (var sc in controllers)
            {
                using (sc)
                {
                    string startType = "Automatic";
                    bool canStop = true;
                    bool canPause = false;
                    bool canShutdown = false;

                    try { startType = sc.StartType.ToString(); } catch { }
                    try { canStop = sc.CanStop; } catch { }
                    try { canPause = sc.CanPauseAndContinue; } catch { }
                    try { canShutdown = sc.CanShutdown; } catch { }

                    result.Add(new WindowsServiceItem
                    {
                        ServiceName = sc.ServiceName,
                        DisplayName = string.IsNullOrWhiteSpace(sc.DisplayName) ? sc.ServiceName : sc.DisplayName,
                        Status = (WindowsServiceStatus)(int)sc.Status,
                        StartType = startType,
                        CanStop = canStop,
                        CanPauseAndContinue = canPause,
                        CanShutdown = canShutdown
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to enumerate Windows services.");
        }

        return result.OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public WindowsServiceStatus GetServiceStatus(string serviceName)
    {
        return GetCurrentStatus(serviceName);
    }

    public async Task<ServiceOperationResult> StartServiceAsync(
        string serviceName, 
        bool runAsAdmin = false, 
        int timeoutSeconds = 30, 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            return new ServiceOperationResult(false, "Service name cannot be empty.", WindowsServiceStatus.Unknown);

        int timeout = timeoutSeconds > 0 ? timeoutSeconds : 30;

        if (!runAsAdmin && IsRunningElevated())
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                if (sc.Status == ServiceControllerStatus.Running)
                {
                    return new ServiceOperationResult(true, $"Service '{serviceName}' is already running.", WindowsServiceStatus.Running);
                }

                sc.Start();
                await WaitForStatusWithCancellationAsync(sc, ServiceControllerStatus.Running, TimeSpan.FromSeconds(timeout), cancellationToken);
                return new ServiceOperationResult(true, $"Service '{serviceName}' started successfully.", (WindowsServiceStatus)(int)sc.Status);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Direct start of service '{ServiceName}' failed; attempting elevated fallback.", serviceName);
            }
        }

        return await ExecuteElevatedServiceCommandAsync(serviceName, "start", timeout, cancellationToken);
    }

    public async Task<ServiceOperationResult> StopServiceAsync(
        string serviceName, 
        bool runAsAdmin = false, 
        int timeoutSeconds = 30, 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            return new ServiceOperationResult(false, "Service name cannot be empty.", WindowsServiceStatus.Unknown);

        int timeout = timeoutSeconds > 0 ? timeoutSeconds : 30;

        if (!runAsAdmin && IsRunningElevated())
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                if (sc.Status == ServiceControllerStatus.Stopped)
                {
                    return new ServiceOperationResult(true, $"Service '{serviceName}' is already stopped.", WindowsServiceStatus.Stopped);
                }

                if (!sc.CanStop)
                {
                    return new ServiceOperationResult(false, $"Service '{serviceName}' does not accept stop control.", (WindowsServiceStatus)(int)sc.Status);
                }

                sc.Stop();
                await WaitForStatusWithCancellationAsync(sc, ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(timeout), cancellationToken);
                return new ServiceOperationResult(true, $"Service '{serviceName}' stopped successfully.", (WindowsServiceStatus)(int)sc.Status);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Direct stop of service '{ServiceName}' failed; attempting elevated fallback.", serviceName);
            }
        }

        return await ExecuteElevatedServiceCommandAsync(serviceName, "stop", timeout, cancellationToken);
    }

    public async Task<ServiceOperationResult> RestartServiceAsync(
        string serviceName, 
        bool runAsAdmin = false, 
        int timeoutSeconds = 30, 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            return new ServiceOperationResult(false, "Service name cannot be empty.", WindowsServiceStatus.Unknown);

        int timeout = timeoutSeconds > 0 ? timeoutSeconds : 30;

        if (!runAsAdmin && IsRunningElevated())
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                if (sc.Status == ServiceControllerStatus.Running)
                {
                    sc.Stop();
                    await WaitForStatusWithCancellationAsync(sc, ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(timeout), cancellationToken);
                }

                sc.Start();
                await WaitForStatusWithCancellationAsync(sc, ServiceControllerStatus.Running, TimeSpan.FromSeconds(timeout), cancellationToken);
                return new ServiceOperationResult(true, $"Service '{serviceName}' restarted successfully.", (WindowsServiceStatus)(int)sc.Status);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Direct restart of service '{ServiceName}' failed; attempting elevated fallback.", serviceName);
            }
        }

        return await ExecuteElevatedServiceCommandAsync(serviceName, "restart", timeout, cancellationToken);
    }

    public async Task<ServiceOperationResult> ToggleServiceAsync(
        string serviceName, 
        bool runAsAdmin = false, 
        int timeoutSeconds = 30, 
        CancellationToken cancellationToken = default)
    {
        var status = GetCurrentStatus(serviceName);
        if (status == WindowsServiceStatus.Running || status == WindowsServiceStatus.StartPending)
        {
            return await StopServiceAsync(serviceName, runAsAdmin, timeoutSeconds, cancellationToken);
        }
        else
        {
            return await StartServiceAsync(serviceName, runAsAdmin, timeoutSeconds, cancellationToken);
        }
    }

    private static async Task WaitForStatusWithCancellationAsync(
        ServiceController sc,
        ServiceControllerStatus desiredStatus,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sc.Refresh();
            if (sc.Status == desiredStatus) return;
            await Task.Delay(250, cancellationToken);
        }
        sc.Refresh();
        if (sc.Status != desiredStatus)
        {
            throw new System.TimeoutException($"Service '{sc.ServiceName}' did not reach {desiredStatus} within {timeout.TotalSeconds:F0} seconds.");
        }
    }

    private async Task<ServiceOperationResult> ExecuteElevatedServiceCommandAsync(
        string serviceName, 
        string operation, 
        int timeoutSeconds, 
        CancellationToken cancellationToken)
    {
        try
        {
            ProcessStartInfo psi;
            if (operation.Equals("restart", StringComparison.OrdinalIgnoreCase))
            {
                psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c net stop \"{serviceName}\" & net start \"{serviceName}\"",
                    Verb = "runas",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
            }
            else
            {
                psi = new ProcessStartInfo
                {
                    FileName = "net.exe",
                    Arguments = $"{operation} \"{serviceName}\"",
                    Verb = "runas",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
            }

            var proc = Process.Start(psi);
            if (proc == null)
            {
                return new ServiceOperationResult(false, "Failed to launch elevated service command.", GetCurrentStatus(serviceName));
            }

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await proc.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                try { if (!proc.HasExited) proc.Kill(); } catch { }
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                return new ServiceOperationResult(false, $"Service '{serviceName}' {operation} timed out after {timeoutSeconds}s.", GetCurrentStatus(serviceName));
            }

            var newStatus = GetCurrentStatus(serviceName);
            bool success = proc.ExitCode == 0 ||
                (operation == "start" && newStatus == WindowsServiceStatus.Running) ||
                (operation == "stop" && newStatus == WindowsServiceStatus.Stopped) ||
                (operation == "restart" && newStatus == WindowsServiceStatus.Running);

            string msg = success
                ? $"Service '{serviceName}' {operation} completed successfully."
                : $"Service '{serviceName}' {operation} finished with exit code {proc.ExitCode}.";

            return new ServiceOperationResult(success, msg, newStatus);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED (User cancelled UAC prompt)
            return new ServiceOperationResult(false, "Administrator elevation prompt was cancelled.", GetCurrentStatus(serviceName));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to execute elevated service operation '{Operation}' for '{ServiceName}'.", operation, serviceName);
            return new ServiceOperationResult(false, $"Error executing {operation}: {ex.Message}", GetCurrentStatus(serviceName));
        }
    }

    private WindowsServiceStatus GetCurrentStatus(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            return (WindowsServiceStatus)(int)sc.Status;
        }
        catch
        {
            return WindowsServiceStatus.Unknown;
        }
    }
}
