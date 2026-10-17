namespace TriggerPoint.Core.Contracts;

public interface IAppPathsService
{
    bool IsPortable { get; }
    string AppDirectory { get; }
    string BaseDataDirectory { get; }
    string ConfigFilePath { get; }
    string BackupFilePath { get; }
    string BackupsDirectory { get; }
    string AppSettingsFilePath { get; }
    string AppSettingsBackupFilePath { get; }
    string RecycleBinFilePath { get; }
    string LogsDirectory { get; }
    string UpdateDirectory { get; }
    string VaultKeyFilePath { get; }
    string ProductivityFilePath { get; }
    string ClipboardHistoryFilePath { get; }
    string ClipboardClipsDirectory { get; }
    void EnsureDirectoriesCreated();
}
