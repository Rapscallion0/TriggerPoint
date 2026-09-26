using System;
using System.IO;
using System.Linq;
using TriggerPoint.Core.Contracts;

namespace TriggerPoint.Core.Services;

public class AppPathsService : IAppPathsService
{
    public const string DataFolder = "data";
    public const string PortableDatFile = "portable.dat";
    public const string DotPortableFile = ".portable";

    public bool IsPortable { get; }
    public string AppDirectory { get; }
    public string BaseDataDirectory { get; }
    public string ConfigFilePath { get; }
    public string BackupFilePath { get; }
    public string BackupsDirectory { get; }
    public string AppSettingsFilePath { get; }
    public string AppSettingsBackupFilePath { get; }
    public string RecycleBinFilePath { get; }
    public string LogsDirectory { get; }
    public string UpdateDirectory { get; }
    public string VaultKeyFilePath { get; }

    public AppPathsService(string[]? args = null, string? baseAppDirectory = null, string? customAppDataPath = null)
    {
        baseAppDirectory ??= AppContext.BaseDirectory;
        AppDirectory = baseAppDirectory;
        args ??= Environment.GetCommandLineArgs();

        // 1. Check CLI for explicit custom --data-dir
        string? explicitDataDir = ExtractArgumentValue(args, "--data-dir") 
                                  ?? ExtractArgumentValue(args, "-data-dir");

        if (!string.IsNullOrWhiteSpace(explicitDataDir))
        {
            IsPortable = true;
            BaseDataDirectory = Path.GetFullPath(explicitDataDir);
        }
        else if (HasFlag(args, "--portable") || HasFlag(args, "-portable"))
        {
            // 2. Explicit CLI --portable switch
            IsPortable = true;
            BaseDataDirectory = Path.Combine(baseAppDirectory, DataFolder);
        }
        else if (Directory.Exists(Path.Combine(baseAppDirectory, DataFolder)) ||
                 File.Exists(Path.Combine(baseAppDirectory, PortableDatFile)) ||
                 File.Exists(Path.Combine(baseAppDirectory, DotPortableFile)))
        {
            // 3. Sentinel detection in app folder
            IsPortable = true;
            BaseDataDirectory = Path.Combine(baseAppDirectory, DataFolder);
        }
        else
        {
            // 4. Default: Standard installed mode (%APPDATA%\TriggerPoint)
            IsPortable = false;
            var roaming = customAppDataPath ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            BaseDataDirectory = Path.Combine(roaming, "TriggerPoint");
        }

        ConfigFilePath = Path.Combine(BaseDataDirectory, "triggerpoint.json");
        BackupFilePath = Path.Combine(BaseDataDirectory, "triggerpoint.bak");
        BackupsDirectory = Path.Combine(BaseDataDirectory, "backups");
        AppSettingsFilePath = Path.Combine(BaseDataDirectory, "appsettings.json");
        AppSettingsBackupFilePath = Path.Combine(BaseDataDirectory, "appsettings.bak");
        RecycleBinFilePath = Path.Combine(BaseDataDirectory, "recyclebin.json");
        LogsDirectory = Path.Combine(BaseDataDirectory, "logs");
        UpdateDirectory = Path.Combine(BaseDataDirectory, "update");
        VaultKeyFilePath = Path.Combine(BaseDataDirectory, "vault.key");

        SetProcessEnvironmentVariables(baseAppDirectory);
    }

    public void EnsureDirectoriesCreated()
    {
        try
        {
            Directory.CreateDirectory(BaseDataDirectory);
            Directory.CreateDirectory(BackupsDirectory);
            Directory.CreateDirectory(LogsDirectory);
        }
        catch
        {
            // Best effort; callers handle IO failures gracefully
        }
    }

    private void SetProcessEnvironmentVariables(string baseAppDir)
    {
        try
        {
            Environment.SetEnvironmentVariable("TRIGGERPOINT_DIR", baseAppDir, EnvironmentVariableTarget.Process);
            var driveRoot = Path.GetPathRoot(baseAppDir);
            if (!string.IsNullOrEmpty(driveRoot))
            {
                Environment.SetEnvironmentVariable("TRIGGERPOINT_DRIVE", driveRoot, EnvironmentVariableTarget.Process);
            }
            Environment.SetEnvironmentVariable("TRIGGERPOINT_DATA", BaseDataDirectory, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("TRIGGERPOINT_PORTABLE", IsPortable ? "1" : "0", EnvironmentVariableTarget.Process);
        }
        catch
        {
            // Ignore environment variable setting failures
        }
    }

    private static bool HasFlag(string[] args, string flag)
    {
        return args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ExtractArgumentValue(string[] args, string flag)
    {
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                {
                    return args[i + 1];
                }
            }
            else if (arg.StartsWith(flag + "=", StringComparison.OrdinalIgnoreCase))
            {
                return arg[(flag.Length + 1)..];
            }
            else if (arg.StartsWith(flag + ":", StringComparison.OrdinalIgnoreCase))
            {
                return arg[(flag.Length + 1)..];
            }
        }
        return null;
    }
}
