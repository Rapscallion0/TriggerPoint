using System;
using System.IO;
using TriggerPoint.Core.Models;

namespace TriggerPoint.Core.Services;

/// <summary>
/// Encapsulates the contextual shortcuts and helpers applicable to a specific TriggerItem.
/// </summary>
public sealed class ContextualShortcutInfo
{
    public string PrimaryActionVerb { get; set; } = "Run";
    public bool CanRunAsAdmin { get; set; }
    public bool CanRevealInExplorer { get; set; }
    public bool CanOpenSettings { get; set; } = true;
    public string NavigationBackText { get; set; } = "Esc Close";
    public string? CopyLabel { get; set; }
}

/// <summary>
/// Determines which keyboard shortcuts and execution overrides are valid and relevant
/// for a given TriggerItem, ensuring context menus and command palettes display only
/// actionable shortcuts.
/// </summary>
public static class ActionShortcutRelevanceHelper
{
    /// <summary>
    /// Checks whether the command string represents a web URL.
    /// </summary>
    public static bool IsWebUrl(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        var trimmed = command.Trim();
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves the command string to an existing local file or directory path,
    /// expanding environment variables and searching system PATH if necessary.
    /// Returns null if the target does not resolve to an existing file or directory on disk.
    /// </summary>
    public static string? ResolveTargetLocalPath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        if (IsWebUrl(command)) return null;

        var clean = command.Trim();
        if (clean.StartsWith("\"") && clean.EndsWith("\"") && clean.Length > 1)
        {
            clean = clean[1..^1].Trim();
        }

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(clean);
            if (File.Exists(expanded) || Directory.Exists(expanded))
            {
                return expanded;
            }

            // If not rooted, attempt searching system PATH for executables/scripts
            if (!Path.IsPathRooted(expanded))
            {
                var resolved = ResolveExecutableFromPath(expanded);
                if (!string.IsNullOrEmpty(resolved) && (File.Exists(resolved) || Directory.Exists(resolved)))
                {
                    return resolved;
                }
            }
        }
        catch
        {
            // Invalid path characters or environment expansion failures
        }

        return null;
    }

    private static string? ResolveExecutableFromPath(string fileName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return null;

        var extensions = new[] { "", ".exe", ".cmd", ".bat", ".ps1", ".com" };
        var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in paths)
        {
            foreach (var ext in extensions)
            {
                try
                {
                    var testPath = Path.Combine(dir, fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ext);
                    if (File.Exists(testPath))
                    {
                        return testPath;
                    }
                }
                catch { }
            }
        }
        return null;
    }

    /// <summary>
    /// Returns true if the item represents an existing local file, folder, or executable on disk,
    /// making the "Shift+Enter Reveal in Explorer" shortcut valid.
    /// </summary>
    public static bool CanRevealInExplorer(TriggerItem? item)
    {
        if (item == null || item.ActionType != ActionType.Shell) return false;
        return ResolveTargetLocalPath(item.Payload?.Command) != null;
    }

    /// <summary>
    /// Returns true if the item supports elevated execution via "Ctrl+Enter Run as Admin".
    /// If the item is already configured to always run as administrator (RunAsAdmin == true),
    /// normal Enter already elevates, so the separate Ctrl+Enter modifier is unnecessary.
    /// </summary>
    public static bool CanRunAsAdmin(TriggerItem? item)
    {
        if (item == null) return false;

        if (item.ActionType == ActionType.Snippet || item.ActionType == ActionType.Folder)
        {
            return false;
        }

        if (item.ActionType == ActionType.Shell)
        {
            if (IsWebUrl(item.Payload?.Command)) return false;
            if (string.IsNullOrWhiteSpace(item.Payload?.Command)) return false;
            if (item.Payload?.RunAsAdmin == true) return false; // Already runs as admin on Enter
            return true;
        }

        if (item.ActionType == ActionType.Workflow)
        {
            if (item.Payload?.RunAsAdmin == true) return false; // Already elevated
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the primary action verb for the item (used for "Enter [Verb]").
    /// </summary>
    public static string GetPrimaryActionVerb(TriggerItem? item)
    {
        if (item == null) return "Run";

        return item.ActionType switch
        {
            ActionType.Folder => "Open Submenu",
            ActionType.Snippet => "Paste",
            ActionType.Shell => IsWebUrl(item.Payload?.Command) 
                ? "Open URL" 
                : (item.Payload?.RunAsAdmin == true ? "Run as Admin" : "Run"),
            ActionType.Workflow => item.Payload?.RunAsAdmin == true ? "Run as Admin" : "Run",
            _ => "Run"
        };
    }

    /// <summary>
    /// Computes all relevant contextual shortcuts for the specified item and navigation state.
    /// </summary>
    public static ContextualShortcutInfo GetContextualShortcuts(TriggerItem? item, bool isInsideSubfolder = false)
    {
        var info = new ContextualShortcutInfo
        {
            PrimaryActionVerb = GetPrimaryActionVerb(item),
            CanRunAsAdmin = CanRunAsAdmin(item),
            CanRevealInExplorer = CanRevealInExplorer(item),
            CanOpenSettings = item != null,
            NavigationBackText = isInsideSubfolder ? "◀ Back (Esc)" : "Esc Close"
        };

        if (item != null)
        {
            info.CopyLabel = item.ActionType switch
            {
                ActionType.Snippet => "Copy Snippet",
                ActionType.Shell => IsWebUrl(item.Payload?.Command) ? "Copy Link" : "Copy Command",
                ActionType.Workflow => "Copy Summary",
                ActionType.Folder => "Copy Name",
                _ => "Copy"
            };
        }

        return info;
    }
}
