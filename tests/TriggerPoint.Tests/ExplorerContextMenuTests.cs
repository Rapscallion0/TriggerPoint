using System;
using System.IO;
using TriggerPoint.Infrastructure.Win32;
using Xunit;

namespace TriggerPoint.Tests;

public class ExplorerContextMenuTests
{
    [Fact]
    public void AddActionArgPrefix_MatchesExpectedFlag()
    {
        Assert.Equal("--add-action", ExplorerContextMenuHelper.AddActionArgPrefix);
    }

    [Fact]
    public void ArgumentExtraction_ParsesPathCorrectly()
    {
        var testPath = @"C:\Tools\MyScript.bat";
        var rawArgs = new[] { "--add-action", testPath };

        string? extractedPath = null;
        for (int i = 0; i < rawArgs.Length; i++)
        {
            if (string.Equals(rawArgs[i], ExplorerContextMenuHelper.AddActionArgPrefix, StringComparison.OrdinalIgnoreCase) && i + 1 < rawArgs.Length)
            {
                extractedPath = rawArgs[i + 1].Trim('"');
                break;
            }
            if (rawArgs[i].StartsWith(ExplorerContextMenuHelper.AddActionArgPrefix + "=", StringComparison.OrdinalIgnoreCase))
            {
                extractedPath = rawArgs[i].Substring(ExplorerContextMenuHelper.AddActionArgPrefix.Length + 1).Trim('"');
                break;
            }
        }

        Assert.Equal(testPath, extractedPath);
    }

    [Fact]
    public void ArgumentExtraction_SupportsEqualsSignSyntax()
    {
        var testPath = @"C:\Folder With Spaces\App.exe";
        var rawArgs = new[] { $"--add-action=\"{testPath}\"" };

        string? extractedPath = null;
        for (int i = 0; i < rawArgs.Length; i++)
        {
            if (string.Equals(rawArgs[i], ExplorerContextMenuHelper.AddActionArgPrefix, StringComparison.OrdinalIgnoreCase) && i + 1 < rawArgs.Length)
            {
                extractedPath = rawArgs[i + 1].Trim('"');
                break;
            }
            if (rawArgs[i].StartsWith(ExplorerContextMenuHelper.AddActionArgPrefix + "=", StringComparison.OrdinalIgnoreCase))
            {
                extractedPath = rawArgs[i].Substring(ExplorerContextMenuHelper.AddActionArgPrefix.Length + 1).Trim('"');
                break;
            }
        }

        Assert.Equal(testPath, extractedPath);
    }

    [Fact]
    public void RegisterAndUnregister_ModifiesRegistrySuccessfully()
    {
        var dummyPath = Environment.ProcessPath ?? @"C:\Windows\notepad.exe";
        
        bool registered = ExplorerContextMenuHelper.Register(dummyPath);
        Assert.True(registered);
        Assert.True(ExplorerContextMenuHelper.IsRegistered());

        bool unregistered = ExplorerContextMenuHelper.Unregister();
        Assert.True(unregistered);
        Assert.False(ExplorerContextMenuHelper.IsRegistered());
    }
}
