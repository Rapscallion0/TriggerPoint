using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.UI.Views;
using Xunit;

namespace TriggerPoint.Tests;

public class CommandPaletteViewTests
{
    private class DummyExecutor : IActionExecutor
    {
        public event EventHandler<string>? StatusChanged { add { } remove { } }
        public event EventHandler<(TriggerItem item, string message)>? ExecutionSucceeded { add { } remove { } }
        public event EventHandler<(TriggerItem item, string message)>? ExecutionFailed { add { } remove { } }
        public System.Threading.Tasks.Task ExecuteAsync(TriggerItem item, ExecutionOverride executionOverride = ExecutionOverride.Standard, IntPtr? targetHwnd = null) => System.Threading.Tasks.Task.CompletedTask;
    }

    private static void EnsureApplicationAndThemeResources()
    {
        if (Application.Current == null)
        {
            new Application();
        }

        if (!Application.Current!.Resources.MergedDictionaries.Any(d => d.Source?.OriginalString?.Contains("ThemeResources.xaml") == true))
        {
            Application.Current!.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/TriggerPoint;component/Theme/ThemeResources.xaml", UriKind.Absolute)
            });
        }
    }

    [Fact]
    public void CommandPaletteView_InstantiatesAndInitializesComponentWithoutException()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationAndThemeResources();

                var items = new List<TriggerItem>
                {
                    new() { Id = Guid.NewGuid(), Name = "Test App", ActionType = ActionType.Shell, Payload = new ActionPayload { Command = "notepad.exe" } },
                    new() { Id = Guid.NewGuid(), Name = "Test Snippet", ActionType = ActionType.Snippet, Payload = new ActionPayload { SnippetTemplate = "Hello" } },
                    new() { Id = Guid.NewGuid(), Name = "Test Folder", ActionType = ActionType.Folder }
                };

                var dummyExecutor = new DummyExecutor();
                var view = new CommandPaletteView(items, dummyExecutor);
                Assert.NotNull(view);
                Assert.NotNull(view.PrefixAutoCompletePopup);
                Assert.NotNull(view.PrefixSuggestionsList);
                Assert.NotNull(view.SortContextMenu);
                Assert.NotNull(view.SortItemSmart);
                Assert.NotNull(view.SortItemAlpha);
                Assert.NotNull(view.SortItemFreq);
                Assert.NotNull(view.SortItemRecent);
                Assert.NotNull(view.SortItemTree);
            }
            catch (Exception ex)
            {
                caughtEx = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);

        if (caughtEx != null)
        {
            throw new InvalidOperationException($"CommandPaletteView failed to initialize: {caughtEx.Message}", caughtEx);
        }
    }

    [Fact]
    public void CommandPaletteView_GetPrefixSuggestions_MatchesCorrectly()
    {
        // Empty or non-prefix returns empty
        Assert.Empty(CommandPaletteView.GetPrefixSuggestions(""));
        Assert.Empty(CommandPaletteView.GetPrefixSuggestions("notepad"));
        Assert.Empty(CommandPaletteView.GetPrefixSuggestions("@app query")); // space means query entered

        // Single '@' returns all 5 prefixes
        var all = CommandPaletteView.GetPrefixSuggestions("@");
        Assert.Equal(5, all.Count);
        Assert.Contains(all, x => x.Prefix == "@all");
        Assert.Contains(all, x => x.Prefix == "@app");
        Assert.Contains(all, x => x.Prefix == "@snip");
        Assert.Contains(all, x => x.Prefix == "@flow");
        Assert.Contains(all, x => x.Prefix == "@folder");

        // Partial prefix '@sn' matches snippet
        var snipOnly = CommandPaletteView.GetPrefixSuggestions("@sn");
        Assert.Single(snipOnly);
        Assert.Equal("@snip", snipOnly[0].Prefix);
        Assert.Equal(CommandPaletteFilterType.Snippet, snipOnly[0].FilterType);

        // Matching by title without '@': '@work'
        var wf = CommandPaletteView.GetPrefixSuggestions("@work");
        Assert.Single(wf);
        Assert.Equal("@flow", wf[0].Prefix);
    }

    [Fact]
    public void CommandPaletteView_FilterPills_ClickingSwitchesFilterCorrectly()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationAndThemeResources();

                var items = new List<TriggerItem>
                {
                    new() { Id = Guid.NewGuid(), Name = "App", ActionType = ActionType.Shell, Payload = new ActionPayload { Command = "notepad.exe" } },
                    new() { Id = Guid.NewGuid(), Name = "Snippet", ActionType = ActionType.Snippet, Payload = new ActionPayload { SnippetTemplate = "txt" } },
                    new() { Id = Guid.NewGuid(), Name = "Flow", ActionType = ActionType.Workflow },
                    new() { Id = Guid.NewGuid(), Name = "Folder", ActionType = ActionType.Folder }
                };

                var dummyExecutor = new DummyExecutor();
                var view = new CommandPaletteView(items, dummyExecutor);

                // Default is All
                Assert.Equal(CommandPaletteFilterType.All, view.ActiveFilter);

                // Click App pill
                view.FilterPillApp.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.Equal(CommandPaletteFilterType.App, view.ActiveFilter);

                // Click Snippet pill
                view.FilterPillSnippet.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.Equal(CommandPaletteFilterType.Snippet, view.ActiveFilter);

                // Click Workflow pill
                view.FilterPillWorkflow.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.Equal(CommandPaletteFilterType.Workflow, view.ActiveFilter);

                // Click Folder pill
                view.FilterPillFolder.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.Equal(CommandPaletteFilterType.Folder, view.ActiveFilter);

                // Click All pill
                view.FilterPillAll.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.Equal(CommandPaletteFilterType.All, view.ActiveFilter);
            }
            catch (Exception ex)
            {
                caughtEx = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);

        if (caughtEx != null)
        {
            throw new InvalidOperationException($"Test failed: {caughtEx.Message}", caughtEx);
        }
    }

    [Fact]
    public void CommandPaletteView_OrderByActionTree_ReturnsExactDepthFirstPreorder()
    {
        var rootFolder1 = new TriggerItem { Id = Guid.NewGuid(), Name = "Work", ActionType = ActionType.Folder, OrderIndex = 0 };
        var subFolder1 = new TriggerItem { Id = Guid.NewGuid(), Name = "Scripts", ActionType = ActionType.Folder, ParentId = rootFolder1.Id, OrderIndex = 0 };
        var scriptItem = new TriggerItem { Id = Guid.NewGuid(), Name = "Deploy", ActionType = ActionType.Shell, ParentId = subFolder1.Id, OrderIndex = 0 };
        var workItem = new TriggerItem { Id = Guid.NewGuid(), Name = "Start Work", ActionType = ActionType.Shell, ParentId = rootFolder1.Id, OrderIndex = 1 };
        var rootFolder2 = new TriggerItem { Id = Guid.NewGuid(), Name = "Personal", ActionType = ActionType.Folder, OrderIndex = 1 };
        var personalItem = new TriggerItem { Id = Guid.NewGuid(), Name = "Notes", ActionType = ActionType.Snippet, ParentId = rootFolder2.Id, OrderIndex = 0 };
        var rootAction = new TriggerItem { Id = Guid.NewGuid(), Name = "Calculator", ActionType = ActionType.Shell, OrderIndex = 0 };

        // Pass in arbitrary shuffle
        var unordered = new List<TriggerItem>
        {
            rootAction,
            personalItem,
            rootFolder2,
            workItem,
            scriptItem,
            subFolder1,
            rootFolder1
        };

        var ordered = CommandPaletteView.OrderByActionTree(unordered);

        // Expected Preorder:
        // 1. Work (Root Folder 0)
        // 2. Scripts (Subfolder 0)
        // 3. Deploy (Action inside Scripts)
        // 4. Start Work (Action inside Work)
        // 5. Personal (Root Folder 1)
        // 6. Notes (Action inside Personal)
        // 7. Calculator (Root Action)
        Assert.Equal(7, ordered.Count);
        Assert.Equal("Work", ordered[0].Name);
        Assert.Equal("Scripts", ordered[1].Name);
        Assert.Equal("Deploy", ordered[2].Name);
        Assert.Equal("Start Work", ordered[3].Name);
        Assert.Equal("Personal", ordered[4].Name);
        Assert.Equal("Notes", ordered[5].Name);
        Assert.Equal("Calculator", ordered[6].Name);
    }
}
