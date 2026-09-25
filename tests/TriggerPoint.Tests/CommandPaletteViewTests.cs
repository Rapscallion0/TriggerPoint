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

        // Single '@' returns all 7 prefixes
        var all = CommandPaletteView.GetPrefixSuggestions("@");
        Assert.Equal(7, all.Count);
        Assert.Contains(all, x => x.Prefix == "@all");
        Assert.Contains(all, x => x.Prefix == "@app");
        Assert.Contains(all, x => x.Prefix == "@snip");
        Assert.Contains(all, x => x.Prefix == "@flow");
        Assert.Contains(all, x => x.Prefix == "@folder");
        Assert.Contains(all, x => x.Prefix == "@calc");
        Assert.Contains(all, x => x.Prefix == "@current");

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

    [Fact]
    public void CommandPaletteView_CalcMode_SelectsItemAndPopulatesContextualShortcuts()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationAndThemeResources();

                var items = new List<TriggerItem>();
                var dummyExecutor = new DummyExecutor();
                var view = new CommandPaletteView(items, dummyExecutor);

                // Type an equation into the search box
                view.SearchTextBox.Text = "=68*2";

                // Verify calculation item is auto-selected without clicking
                Assert.NotNull(view.ResultsListBox.SelectedItem);
                var selVm = view.ResultsListBox.SelectedItem as PaletteItemViewModel;
                Assert.NotNull(selVm);
                Assert.True(selVm.IsCalculatorResult);
                Assert.Equal("136", selVm.CalcResult?.FormattedResult);

                // Verify footer hints immediately reflect calculation actions (Approach 1)
                Assert.Contains("Ctrl+C Copy Answer", view.FooterHintsText.Text);
                Assert.Contains("Ctrl+Shift+C Copy Question & Answer", view.FooterHintsText.Text);
                Assert.Contains("Enter Paste Answer", view.FooterHintsText.Text);
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
    public void CommandPaletteView_CalcMode_UnifiedReferenceAndContextualShortcuts_Lifecycle()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationAndThemeResources();

                var items = new List<TriggerItem>();
                var dummyExecutor = new DummyExecutor();
                var view = new CommandPaletteView(items, dummyExecutor);

                // Step 1: Open calculator mode with just '='
                view.SearchTextBox.Text = "=";

                Assert.Equal(Visibility.Visible, view.CalculatorGuideCard.Visibility);
                Assert.Equal(Visibility.Collapsed, view.FilterPillsBar.Visibility);
                Assert.Equal(Visibility.Visible, view.CalcDraftBanner.Visibility);
                Assert.Equal(Visibility.Collapsed, view.CalcResultBanner.Visibility);
                Assert.Contains("Click keypad to insert operators", view.FooterHintsText.Text);
                Assert.Contains("Esc Exit Calculator", view.FooterHintsText.Text);
                Assert.DoesNotContain("Run as Admin", view.FooterHintsText.Text);
                Assert.DoesNotContain("Edit in Action Manager", view.FooterHintsText.Text);
                Assert.DoesNotContain("Copy Answer", view.FooterHintsText.Text);

                // Step 2: Incomplete intermediate equation: '=65+'
                view.SearchTextBox.Text = "=65+";

                Assert.Equal(Visibility.Visible, view.CalculatorGuideCard.Visibility);
                Assert.Equal(Visibility.Collapsed, view.FilterPillsBar.Visibility);
                Assert.Equal(Visibility.Visible, view.CalcDraftBanner.Visibility);
                Assert.Equal(Visibility.Collapsed, view.CalcResultBanner.Visibility);
                Assert.Contains("Click keypad to insert operators", view.FooterHintsText.Text);
                Assert.DoesNotContain("Run as Admin", view.FooterHintsText.Text);

                // Step 3: Complete equation: '=65+3'
                view.SearchTextBox.Text = "=65+3";

                Assert.Equal(Visibility.Visible, view.CalculatorGuideCard.Visibility);
                Assert.Equal(Visibility.Collapsed, view.FilterPillsBar.Visibility);
                Assert.Equal(Visibility.Collapsed, view.CalcDraftBanner.Visibility);
                Assert.Equal(Visibility.Visible, view.CalcResultBanner.Visibility);
                Assert.Equal("68", view.CalcResultValueText.Text);
                Assert.Contains("65+3", view.CalcResultValueText.ToolTip as string ?? string.Empty);
                Assert.Contains("Enter Paste Answer", view.FooterHintsText.Text);
                Assert.Contains("Ctrl+C Copy Answer (68)", view.FooterHintsText.Text);
                Assert.Contains("Ctrl+Shift+C Copy Question & Answer", view.FooterHintsText.Text);
                Assert.Contains("Tab Chain", view.FooterHintsText.Text);
                Assert.DoesNotContain("Run as Admin", view.FooterHintsText.Text);
                Assert.DoesNotContain("Edit in Action Manager", view.FooterHintsText.Text);

                // Step 4: Clear to non-calc query 'test'
                view.SearchTextBox.Text = "test";

                Assert.Equal(Visibility.Collapsed, view.CalculatorGuideCard.Visibility);
                Assert.Equal(Visibility.Visible, view.FilterPillsBar.Visibility);

                // Step 5: Test date calculation '=next friday' without query duplication
                view.SearchTextBox.Text = "=next friday";
                Assert.Contains("Friday", view.CalcResultValueText.Text);
                Assert.DoesNotContain("Next Friday", view.CalcResultDescText.Text);
                Assert.Contains("from now", view.CalcResultDescText.Text);
                Assert.Equal(28.0, view.CalcResultPasteBtn.Width);
                Assert.Equal(28.0, view.CalcResultCopyValueBtn.Width);
                Assert.Equal(28.0, view.CalcResultCopyFullBtn.Width);
                Assert.Equal(28.0, view.CalcResultChainBtn.Width);
                Assert.Equal(Visibility.Visible, view.CalcResultMoreUnitsBtn.Visibility);
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
    public void CommandPaletteView_SearchBoxButtons_HaveCustomTemplatesAndStyling()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationAndThemeResources();

                var items = new List<TriggerItem>();
                var dummyExecutor = new DummyExecutor();
                var view = new CommandPaletteView(items, dummyExecutor);

                // Verify ActionManagerBtn has custom HeaderIconButtonStyle
                Assert.NotNull(view.ActionManagerBtn.Style);
                Assert.NotNull(view.ActionManagerBtn.Template);
                Assert.True(view.ActionManagerBtn.Template.Triggers.Count > 0);
                Assert.Equal(28.0, view.ActionManagerBtn.Width);
                Assert.Equal(28.0, view.ActionManagerBtn.Height);

                // Verify AppSettingsBtn has custom HeaderIconButtonStyle
                Assert.NotNull(view.AppSettingsBtn.Style);
                Assert.NotNull(view.AppSettingsBtn.Template);
                Assert.True(view.AppSettingsBtn.Template.Triggers.Count > 0);
                Assert.Equal(28.0, view.AppSettingsBtn.Width);
                Assert.Equal(28.0, view.AppSettingsBtn.Height);

                // Verify HelpOverlayBtn has custom HeaderIconButtonStyle
                Assert.NotNull(view.HelpOverlayBtn.Style);
                Assert.NotNull(view.HelpOverlayBtn.Template);
                Assert.True(view.HelpOverlayBtn.Template.Triggers.Count > 0);
                Assert.Equal(28.0, view.HelpOverlayBtn.Width);
                Assert.Equal(28.0, view.HelpOverlayBtn.Height);

                // Verify SortModeBtn has custom HeaderSortButtonStyle
                Assert.NotNull(view.SortModeBtn.Style);
                Assert.NotNull(view.SortModeBtn.Template);
                Assert.True(view.SortModeBtn.Template.Triggers.Count > 0);
                Assert.Equal(26.0, view.SortModeBtn.Height);

                // Verify BackButton has custom HeaderIconButtonStyle
                Assert.NotNull(view.BackButton.Style);
                Assert.NotNull(view.BackButton.Template);
                Assert.Equal(28.0, view.BackButton.Width);
                Assert.Equal(28.0, view.BackButton.Height);

                // Verify ScopeDismissBtn has custom ScopeDismissButtonStyle
                Assert.NotNull(view.ScopeDismissBtn.Style);
                Assert.NotNull(view.ScopeDismissBtn.Template);
                Assert.Equal(18.0, view.ScopeDismissBtn.Width);
                Assert.Equal(18.0, view.ScopeDismissBtn.Height);

                // Verify FolderExecutionConfirmOverlay and controls exist
                Assert.NotNull(view.FolderExecutionConfirmOverlay);
                Assert.Equal(Visibility.Collapsed, view.FolderExecutionConfirmOverlay.Visibility);
                Assert.NotNull(view.CloseFolderConfirmBtn);
                Assert.NotNull(view.FolderConfirmTitleText);
                Assert.NotNull(view.FolderConfirmSubtitleText);
                Assert.NotNull(view.FolderConfirmActionsList);
                Assert.NotNull(view.FolderConfirmIncludeSubfoldersCheck);
                Assert.NotNull(view.FolderConfirmCancelBtn);
                Assert.NotNull(view.FolderConfirmRunBtn);
                Assert.NotNull(view.FolderConfirmRunBtnText);

                // Verify Empty State dynamic text blocks exist
                Assert.NotNull(view.EmptyStateIconText);
                Assert.NotNull(view.EmptyStateTitleText);
                Assert.NotNull(view.EmptyStatePromptText);
                Assert.NotNull(view.CreateActionEmptyBtnText);
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
    public void ResolveFolderActions_RespectsIncludeSubfoldersAndOrderIndex()
    {
        var rootFolder = new TriggerItem { Id = Guid.NewGuid(), Name = "Root Dev", ActionType = ActionType.Folder, IsEnabled = true };
        var subFolder = new TriggerItem { Id = Guid.NewGuid(), ParentId = rootFolder.Id, Name = "Sub Tools", ActionType = ActionType.Folder, IsEnabled = true };
        var deepSubFolder = new TriggerItem { Id = Guid.NewGuid(), ParentId = subFolder.Id, Name = "Deep", ActionType = ActionType.Folder, IsEnabled = true };

        var directAction1 = new TriggerItem { Id = Guid.NewGuid(), ParentId = rootFolder.Id, Name = "Git Pull", ActionType = ActionType.Shell, OrderIndex = 2, IsEnabled = true };
        var directAction2 = new TriggerItem { Id = Guid.NewGuid(), ParentId = rootFolder.Id, Name = "Clean Build", ActionType = ActionType.Shell, OrderIndex = 1, IsEnabled = true };
        var disabledDirectAction = new TriggerItem { Id = Guid.NewGuid(), ParentId = rootFolder.Id, Name = "Old Script", ActionType = ActionType.Shell, OrderIndex = 0, IsEnabled = false };

        var subAction1 = new TriggerItem { Id = Guid.NewGuid(), ParentId = subFolder.Id, Name = "Docker Up", ActionType = ActionType.Shell, OrderIndex = 1, IsEnabled = true };
        var deepAction1 = new TriggerItem { Id = Guid.NewGuid(), ParentId = deepSubFolder.Id, Name = "DB Migrate", ActionType = ActionType.Shell, OrderIndex = 1, IsEnabled = true };

        var allItems = new List<TriggerItem>
        {
            rootFolder, subFolder, deepSubFolder,
            directAction1, directAction2, disabledDirectAction,
            subAction1, deepAction1
        };

        // Subfolder detection
        Assert.True(CommandPaletteView.FolderHasSubfolders(rootFolder, allItems));
        Assert.True(CommandPaletteView.FolderHasSubfolders(subFolder, allItems));
        Assert.False(CommandPaletteView.FolderHasSubfolders(deepSubFolder, allItems));

        // Direct only (includeSubfolders = false)
        var directOnly = CommandPaletteView.ResolveFolderActions(rootFolder, allItems, includeSubfolders: false);
        Assert.Equal(2, directOnly.Count);
        Assert.Equal("Clean Build", directOnly[0].Name); // OrderIndex 1 before 2
        Assert.Equal("Git Pull", directOnly[1].Name);

        // Recursive (includeSubfolders = true)
        var recursive = CommandPaletteView.ResolveFolderActions(rootFolder, allItems, includeSubfolders: true);
        Assert.Equal(4, recursive.Count);
        Assert.Equal("Clean Build", recursive[0].Name);
        Assert.Equal("Git Pull", recursive[1].Name);
        Assert.Equal("Docker Up", recursive[2].Name);
        Assert.Equal("DB Migrate", recursive[3].Name);
        Assert.DoesNotContain(recursive, x => x.Name == "Old Script"); // Disabled excluded
    }

    [Fact]
    public void CommandPaletteView_DrillIntoFolder_ResetsActiveFilterToAll()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationAndThemeResources();

                var folder = new TriggerItem { Id = Guid.NewGuid(), Name = "Dev Tools", ActionType = ActionType.Folder, IsEnabled = true };
                var childItem = new TriggerItem { Id = Guid.NewGuid(), ParentId = folder.Id, Name = "Run App", ActionType = ActionType.Shell, IsEnabled = true };

                var items = new List<TriggerItem> { folder, childItem };
                var view = new CommandPaletteView(items, new DummyExecutor());

                // Simulate filtering by folder (e.g. typing @folder or clicking the folder pill)
                view.SearchTextBox.Text = "@folder";
                Assert.Equal(CommandPaletteFilterType.Folder, view.ActiveFilter);

                // Now simulate drilling into the folder via reflection or calling private method / enter
                var drillMethod = typeof(CommandPaletteView).GetMethod("DrillIntoFolder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.NotNull(drillMethod);
                drillMethod.Invoke(view, new object[] { folder });

                // ActiveFilter MUST be reset to All so child Shell/Snippet actions inside the folder are shown
                Assert.Equal(CommandPaletteFilterType.All, view.ActiveFilter);
                Assert.Equal(string.Empty, view.SearchTextBox.Text);

                // Results should contain the child item
                var resultItems = view.ResultsListBox.ItemsSource as IEnumerable<PaletteItemViewModel>;
                Assert.NotNull(resultItems);
                Assert.Contains(resultItems, r => r.Item?.Name == "Run App");
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
}
