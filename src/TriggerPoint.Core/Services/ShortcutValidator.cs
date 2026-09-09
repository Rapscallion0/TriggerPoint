using System;
using System.IO;
using TriggerPoint.Core.Models;

namespace TriggerPoint.Core.Services;

public enum ShortcutValidationStatus
{
    Valid,
    FileNotFound,
    CommandNotFound,
    EmptyCommand,
    EmptySnippet,
    NotApplicable
}

public sealed record ShortcutValidationResult(
    ShortcutValidationStatus Status,
    string Message,
    string? ResolvedPath = null)
{
    public bool IsValid => Status == ShortcutValidationStatus.Valid || Status == ShortcutValidationStatus.NotApplicable;
}

public static class ShortcutValidator
{
    public static ShortcutValidationResult Validate(string? command, string? workingDirectory = null)
    {
        var cmd = command?.Trim();
        if (string.IsNullOrWhiteSpace(cmd))
            return new ShortcutValidationResult(ShortcutValidationStatus.EmptyCommand, "Command / Application Path is empty", null);

        // Web URLs & Custom Protocols (http://, https://, mailto:, steam://, etc.)
        if (Uri.TryCreate(cmd, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeMailto || !cmd.Contains('\\')))
        {
            if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeMailto || uri.IsAbsoluteUri)
            {
                return new ShortcutValidationResult(ShortcutValidationStatus.Valid, $"Web URL / protocol: {cmd}", cmd);
            }
        }

        string expanded;
        try
        {
            expanded = Environment.ExpandEnvironmentVariables(cmd);
        }
        catch
        {
            expanded = cmd;
        }

        // Path with slashes
        if (expanded.Contains('\\') || expanded.Contains('/'))
        {
            try
            {
                if (File.Exists(expanded))
                    return new ShortcutValidationResult(ShortcutValidationStatus.Valid, $"Target file exists: {Path.GetFileName(expanded)}", expanded);

                if (Directory.Exists(expanded))
                    return new ShortcutValidationResult(ShortcutValidationStatus.Valid, $"Target folder exists: {Path.GetFileName(expanded)}", expanded);

                return new ShortcutValidationResult(ShortcutValidationStatus.FileNotFound, $"Target file not found: {expanded}", expanded);
            }
            catch (Exception ex)
            {
                return new ShortcutValidationResult(ShortcutValidationStatus.FileNotFound, $"Invalid file path: {ex.Message}", expanded);
            }
        }

        // Working directory relative check
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            try
            {
                var workDir = Environment.ExpandEnvironmentVariables(workingDirectory);
                var combined = Path.Combine(workDir, expanded);
                if (File.Exists(combined))
                    return new ShortcutValidationResult(ShortcutValidationStatus.Valid, $"Target file exists: {combined}", combined);
            }
            catch { }
        }

        // PATH search
        if (IsCommandInPath(expanded, out var foundPath))
        {
            return new ShortcutValidationResult(ShortcutValidationStatus.Valid, $"Command found in PATH: {foundPath ?? expanded}", foundPath ?? expanded);
        }

        return new ShortcutValidationResult(ShortcutValidationStatus.CommandNotFound, $"Command or file '{expanded}' not found on system", expanded);
    }

    public static ShortcutValidationResult Validate(TriggerItem item)
    {
        if (item == null)
            return new ShortcutValidationResult(ShortcutValidationStatus.NotApplicable, "Item is null");

        if (item.ActionType == ActionType.Folder)
            return new ShortcutValidationResult(ShortcutValidationStatus.NotApplicable, "Folder container", null);

        if (item.ActionType == ActionType.Snippet)
        {
            if (string.IsNullOrWhiteSpace(item.Payload.SnippetTemplate))
                return new ShortcutValidationResult(ShortcutValidationStatus.EmptySnippet, "Snippet template is empty", null);

            return new ShortcutValidationResult(ShortcutValidationStatus.Valid, "Valid text snippet", null);
        }

        if (item.ActionType == ActionType.Shell)
        {
            return Validate(item.Payload.Command, item.Payload.WorkingDirectory);
        }

        return new ShortcutValidationResult(ShortcutValidationStatus.Valid, "Valid");
    }

    private static bool IsCommandInPath(string command, out string? foundPath)
    {
        foundPath = null;
        try
        {
            if (File.Exists(command))
            {
                foundPath = Path.GetFullPath(command);
                return true;
            }

            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM;.PS1")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var dir in paths)
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;

                    var directPath = Path.Combine(dir, command);
                    if (File.Exists(directPath))
                    {
                        foundPath = directPath;
                        return true;
                    }

                    if (!Path.HasExtension(command))
                    {
                        foreach (var ext in extensions)
                        {
                            var extPath = Path.Combine(dir, command + ext);
                            if (File.Exists(extPath))
                            {
                                foundPath = extPath;
                                return true;
                            }
                        }
                    }
                }
                catch { }
            }
        }
        catch { }

        return false;
    }
}
