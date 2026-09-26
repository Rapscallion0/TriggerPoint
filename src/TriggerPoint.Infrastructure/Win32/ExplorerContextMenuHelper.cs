using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Serilog;

namespace TriggerPoint.Infrastructure.Win32;

public static class ExplorerContextMenuHelper
{
    private static readonly ILogger Logger = Log.ForContext(typeof(ExplorerContextMenuHelper));

    public const string AddActionArgPrefix = "--add-action";
    private const string MenuTitle = "Add to TriggerPoint";
    private const string FileShellKey = @"Software\Classes\*\shell\TriggerPoint";
    private const string AllFilesystemObjectsShellKey = @"Software\Classes\AllFilesystemObjects\shell\TriggerPoint";
    private const string DirectoryShellKey = @"Software\Classes\Directory\shell\TriggerPoint";
    private const string BackgroundShellKey = @"Software\Classes\Directory\Background\shell\TriggerPoint";
    private const string DriveShellKey = @"Software\Classes\Drive\shell\TriggerPoint";

    public static bool IsRegistered()
    {
        try
        {
            using var fileKey = Registry.CurrentUser.OpenSubKey(FileShellKey, false);
            if (fileKey != null) return true;

            using var allObjKey = Registry.CurrentUser.OpenSubKey(AllFilesystemObjectsShellKey, false);
            return allObjKey != null;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Error checking Windows Explorer context menu registration.");
            return false;
        }
    }

    public static bool Register(string? executablePath = null, bool isPortable = false)
    {
        try
        {
            executablePath ??= Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                Logger.Warning("Cannot register Explorer context menu: Executable path not found.");
                return false;
            }

            string fileCmd;
            string bgCmd;

            if (isPortable)
            {
                // Tier 3: Self-pruning dead man's switch command wrapper
                string purgeScript = "reg delete \"HKCU\\Software\\Classes\\*\\shell\\TriggerPoint\" /f & " +
                                     "reg delete \"HKCU\\Software\\Classes\\Directory\\shell\\TriggerPoint\" /f & " +
                                     "reg delete \"HKCU\\Software\\Classes\\Directory\\Background\\shell\\TriggerPoint\" /f & " +
                                     "reg delete \"HKCU\\Software\\Classes\\Drive\\shell\\TriggerPoint\" /f & " +
                                     "reg delete \"HKCU\\Software\\Classes\\AllFilesystemObjects\\shell\\TriggerPoint\" /f";

                fileCmd = $"cmd.exe /c \"if exist \\\"{executablePath}\\\" (start \\\"\\\" \\\"{executablePath}\\\" --add-action \\\"%1\\\") else ({purgeScript})\"";
                bgCmd = $"cmd.exe /c \"if exist \\\"{executablePath}\\\" (start \\\"\\\" \\\"{executablePath}\\\" --add-action \\\"%V\\\") else ({purgeScript})\"";
            }
            else
            {
                fileCmd = $"\"{executablePath}\" --add-action \"%1\"";
                bgCmd = $"\"{executablePath}\" --add-action \"%V\"";
            }

            RegisterKey(FileShellKey, executablePath, fileCmd, isPortable);
            RegisterKey(AllFilesystemObjectsShellKey, executablePath, fileCmd, isPortable);
            RegisterKey(DirectoryShellKey, executablePath, fileCmd, isPortable);
            RegisterKey(BackgroundShellKey, executablePath, bgCmd, isPortable);
            RegisterKey(DriveShellKey, executablePath, fileCmd, isPortable);

            Logger.Information("Successfully registered 'Add to TriggerPoint' in Windows Explorer context menu (IsPortable={IsPortable}).", isPortable);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to register Windows Explorer context menu.");
            return false;
        }
    }

    public static void ReconcileOrphanedRegistrations(string? currentExePath = null, bool isPortable = false)
    {
        try
        {
            if (!IsRegistered()) return;

            currentExePath ??= Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(currentExePath)) return;

            using var key = Registry.CurrentUser.OpenSubKey(FileShellKey, false);
            if (key == null) return;

            var targetPath = key.GetValue("TriggerPointTargetPath") as string;
            var wasPortable = (key.GetValue("TriggerPointPortable") as int? ?? 0) == 1;

            if (wasPortable || isPortable)
            {
                if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath) ||
                    !string.Equals(targetPath, currentExePath, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Information("Reconciling stale Explorer context menu from {OldPath} to {NewPath}", targetPath, currentExePath);
                    Register(currentExePath, isPortable);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Context menu reconciliation encountered an error.");
        }
    }

    public static bool Unregister()
    {
        try
        {
            UnregisterKey(FileShellKey);
            UnregisterKey(AllFilesystemObjectsShellKey);
            UnregisterKey(DirectoryShellKey);
            UnregisterKey(BackgroundShellKey);
            UnregisterKey(DriveShellKey);

            Logger.Information("Successfully unregistered 'Add to TriggerPoint' from Windows Explorer context menu.");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to unregister Windows Explorer context menu.");
            return false;
        }
    }

    public static bool UnregisterPortableIntegrations()
    {
        return Unregister();
    }

    private static void RegisterKey(string subKeyPath, string exePath, string commandString, bool isPortable = false)
    {
        using var rootKey = Registry.CurrentUser.CreateSubKey(subKeyPath, true);
        if (rootKey != null)
        {
            rootKey.SetValue("", MenuTitle);
            rootKey.SetValue("Icon", $"{exePath},0");
            rootKey.SetValue("TriggerPointTargetPath", exePath);
            rootKey.SetValue("TriggerPointPortable", isPortable ? 1 : 0, RegistryValueKind.DWord);

            using var cmdKey = rootKey.CreateSubKey("command", true);
            cmdKey?.SetValue("", commandString);
        }
    }

    private static void UnregisterKey(string subKeyPath)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(subKeyPath, false);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to delete registry key {SubKeyPath} (it may not exist).", subKeyPath);
        }
    }
}
