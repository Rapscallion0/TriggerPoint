using System;
using System.IO;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class ShortcutValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyOrWhitespace_ReturnsEmptyCommand(string? command)
    {
        var result = ShortcutValidator.Validate(command);
        Assert.Equal(ShortcutValidationStatus.EmptyCommand, result.Status);
        Assert.False(result.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public void Validate_ExistingTempFile_ReturnsValid()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            var result = ShortcutValidator.Validate(tempFile);
            Assert.Equal(ShortcutValidationStatus.Valid, result.Status);
            Assert.True(result.IsValid);
            Assert.Equal(tempFile, result.ResolvedPath);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void Validate_ExistingDirectory_ReturnsValid()
    {
        string tempDir = Path.GetTempPath();
        var result = ShortcutValidator.Validate(tempDir);
        Assert.Equal(ShortcutValidationStatus.Valid, result.Status);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_NonExistentFile_ReturnsFileNotFound()
    {
        string nonExistent = Path.Combine(Path.GetTempPath(), $"NonExistent_{Guid.NewGuid():N}.exe");
        var result = ShortcutValidator.Validate(nonExistent);
        Assert.Equal(ShortcutValidationStatus.FileNotFound, result.Status);
        Assert.False(result.IsValid);
        Assert.Contains("Target file not found", result.Message);
    }

    [Theory]
    [InlineData("https://www.google.com")]
    [InlineData("http://localhost:8080")]
    [InlineData("mailto:test@example.com")]
    [InlineData("steam://rungameid/730")]
    public void Validate_WebOrProtocolUrls_ReturnsValid(string url)
    {
        var result = ShortcutValidator.Validate(url);
        Assert.Equal(ShortcutValidationStatus.Valid, result.Status);
        Assert.True(result.IsValid);
        Assert.Equal(url, result.ResolvedPath);
    }

    [Fact]
    public void Validate_EnvironmentVariableExpansion_ResolvesValid()
    {
        string envPath = @"%WINDIR%\explorer.exe";
        var result = ShortcutValidator.Validate(envPath);
        Assert.Equal(ShortcutValidationStatus.Valid, result.Status);
        Assert.True(result.IsValid);
        Assert.True(File.Exists(result.ResolvedPath));
    }

    [Theory]
    [InlineData("cmd.exe")]
    [InlineData("notepad.exe")]
    public void Validate_PathExecutable_ResolvesValid(string exeName)
    {
        var result = ShortcutValidator.Validate(exeName);
        Assert.Equal(ShortcutValidationStatus.Valid, result.Status);
        Assert.True(result.IsValid);
        Assert.NotNull(result.ResolvedPath);
        Assert.True(File.Exists(result.ResolvedPath));
    }

    [Fact]
    public void Validate_TriggerItem_Folder_ReturnsNotApplicable()
    {
        var item = new TriggerItem
        {
            ActionType = ActionType.Folder,
            Name = "My Folder"
        };
        var result = ShortcutValidator.Validate(item);
        Assert.Equal(ShortcutValidationStatus.NotApplicable, result.Status);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_TriggerItem_ShellMissingFile_ReturnsFileNotFound()
    {
        var item = new TriggerItem
        {
            ActionType = ActionType.Shell,
            Name = "Dead link",
            Payload = new ActionPayload
            {
                Command = @"C:\NonExistentDirectory\FakeApp.exe"
            }
        };
        var result = ShortcutValidator.Validate(item);
        Assert.Equal(ShortcutValidationStatus.FileNotFound, result.Status);
        Assert.False(result.IsValid);
    }
}
