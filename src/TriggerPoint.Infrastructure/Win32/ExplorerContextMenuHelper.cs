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

    public static bool Register(string? executablePath = null)
    {
        try
        {
            executablePath ??= Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                Logger.Warning("Cannot register Explorer context menu: Executable path not found.");
                return false;
            }

            string fileCmd = $"\"{executablePath}\" --add-action \"%1\"";
            string bgCmd = $"\"{executablePath}\" --add-action \"%V\"";

            RegisterKey(FileShellKey, executablePath, fileCmd);
            RegisterKey(AllFilesystemObjectsShellKey, executablePath, fileCmd);
            RegisterKey(DirectoryShellKey, executablePath, fileCmd);
            RegisterKey(BackgroundShellKey, executablePath, bgCmd);
            RegisterKey(DriveShellKey, executablePath, fileCmd);

            Logger.Information("Successfully registered 'Add to TriggerPoint' in Windows Explorer context menu.");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to register Windows Explorer context menu.");
            return false;
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

    private static void RegisterKey(string subKeyPath, string exePath, string commandString)
    {
        using var rootKey = Registry.CurrentUser.CreateSubKey(subKeyPath, true);
        if (rootKey != null)
        {
            rootKey.SetValue("", MenuTitle);
            rootKey.SetValue("Icon", $"{exePath},0");

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
