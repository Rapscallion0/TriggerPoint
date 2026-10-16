using System;
using System.IO;
using System.Threading;
using System.Windows;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Services;
using TriggerPoint.UI.Views;
using Xunit;

namespace TriggerPoint.Tests;

public class AppIconResolutionTests
{
    [Fact]
    public void ExtractExecutablePath_ParsesCorrectly()
    {
        Assert.Null(Win32IconService.ExtractExecutablePath(""));
        Assert.Null(Win32IconService.ExtractExecutablePath("   "));
        Assert.Equal("calc.exe", Win32IconService.ExtractExecutablePath("calc.exe"));
        Assert.Equal(@"C:\Program Files\App\app.exe", Win32IconService.ExtractExecutablePath("\"C:\\Program Files\\App\\app.exe\" --arg1 --arg2"));
        Assert.Equal("cmd.exe", Win32IconService.ExtractExecutablePath("cmd.exe /c echo hello"));
    }

    [Fact]
    public void ResolveTargetExecutable_FindsSystemExecutables()
    {
        var resolved = Win32IconService.ResolveTargetExecutable("cmd.exe");
        Assert.NotNull(resolved);
        Assert.True(File.Exists(resolved));

        var nonexistent = Win32IconService.ResolveTargetExecutable("this_exe_does_not_exist_xyz_12345.exe");
        Assert.Null(nonexistent);
    }

    [Fact]
    public void GetActionIcon_ReturnsIconOnlyForShellWithValidTarget()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                // Shell action with existing executable (cmd.exe)
                var shellItem = new TriggerItem
                {
                    Name = "Command Prompt",
                    ActionType = ActionType.Shell,
                    Payload = new ActionPayload { Command = "cmd.exe" }
                };
                var icon = Win32IconService.GetActionIcon(shellItem);
                Assert.NotNull(icon);

                // Snippet action should never return an App Icon
                var snippetItem = new TriggerItem
                {
                    Name = "My Snippet",
                    ActionType = ActionType.Snippet,
                    Payload = new ActionPayload { SnippetTemplate = "Hello" }
                };
                Assert.Null(Win32IconService.GetActionIcon(snippetItem));

                // Shell action with nonexistent target should return null gracefully
                var nonexistentShell = new TriggerItem
                {
                    Name = "Fake App",
                    ActionType = ActionType.Shell,
                    Payload = new ActionPayload { Command = "nonexistent_fake_binary_999.exe" }
                };
                Assert.Null(Win32IconService.GetActionIcon(nonexistentShell));
            }
            catch (Exception ex)
            {
                caughtEx = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);

        if (caughtEx != null) throw caughtEx;
    }

    [Fact]
    public void PaletteItemViewModel_AppIconVisibility_MatchesIconPresence()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                // Shell action with cmd.exe -> HasAppIcon is true
                var shellItem = new TriggerItem
                {
                    Name = "Command Prompt",
                    ActionType = ActionType.Shell,
                    Payload = new ActionPayload { Command = "cmd.exe" }
                };
                var shellVm = new PaletteItemViewModel(shellItem, null);
                Assert.True(shellVm.HasAppIcon);
                Assert.Equal(Visibility.Visible, shellVm.AppIconVisibility);
                Assert.Equal(Visibility.Collapsed, shellVm.StandaloneIconVisibility);

                // Snippet action -> HasAppIcon is false
                var snippetItem = new TriggerItem
                {
                    Name = "Snippet",
                    ActionType = ActionType.Snippet,
                    Payload = new ActionPayload { SnippetTemplate = "test" }
                };
                var snippetVm = new PaletteItemViewModel(snippetItem, null);
                Assert.False(snippetVm.HasAppIcon);
                Assert.Equal(Visibility.Collapsed, snippetVm.AppIconVisibility);
                Assert.Equal(Visibility.Visible, snippetVm.StandaloneIconVisibility);
            }
            catch (Exception ex)
            {
                caughtEx = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);

        if (caughtEx != null) throw caughtEx;
    }

    [Fact]
    public void CursorMenuItemViewModel_AppIconVisibility_MatchesIconPresence()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var shellItem = new TriggerItem
                {
                    Name = "Command Prompt",
                    ActionType = ActionType.Shell,
                    Payload = new ActionPayload { Command = "cmd.exe" }
                };
                var shellVm = new CursorMenuItemViewModel(shellItem, "1");
                Assert.True(shellVm.HasAppIcon);
                Assert.Equal(Visibility.Visible, shellVm.AppIconVisibility);
                Assert.Equal(Visibility.Collapsed, shellVm.StandaloneIconVisibility);
                Assert.Equal(Visibility.Visible, shellVm.HasAccelerator);

                var snippetItem = new TriggerItem
                {
                    Name = "Snippet",
                    ActionType = ActionType.Snippet,
                    Payload = new ActionPayload { SnippetTemplate = "test" }
                };
                var snippetVm = new CursorMenuItemViewModel(snippetItem);
                Assert.False(snippetVm.HasAppIcon);
                Assert.Equal(Visibility.Collapsed, snippetVm.AppIconVisibility);
                Assert.Equal(Visibility.Visible, snippetVm.StandaloneIconVisibility);
            }
            catch (Exception ex)
            {
                caughtEx = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);

        if (caughtEx != null) throw caughtEx;
    }
}
