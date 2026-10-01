using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Documents;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.UI.Views;
using Xunit;

namespace TriggerPoint.Tests;

public class CursorContextMenuViewTests
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

    private static string GetInlinesText(InlineCollection inlines)
    {
        return string.Concat(inlines.OfType<Run>().Select(r => r.Text));
    }

    [Fact]
    public void CursorContextMenuView_DynamicFooterHints_AdaptToSelectedItemRelevance()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationAndThemeResources();

                var items = new List<TriggerItem>
                {
                    new() { Id = Guid.NewGuid(), Name = "Snippet 1", ActionType = ActionType.Snippet, Payload = new ActionPayload { SnippetTemplate = "Hello World" } },
                    new() { Id = Guid.NewGuid(), Name = "Google Web", ActionType = ActionType.Shell, Payload = new ActionPayload { Command = "https://www.google.com" } },
                    new() { Id = Guid.NewGuid(), Name = "Command Prompt", ActionType = ActionType.Shell, Payload = new ActionPayload { Command = "cmd.exe" } },
                    new() { Id = Guid.NewGuid(), Name = "My Flow", ActionType = ActionType.Workflow, Payload = new ActionPayload() },
                    new() { Id = Guid.NewGuid(), Name = "My Submenu", ActionType = ActionType.Folder }
                };

                var dummyExecutor = new DummyExecutor();
                var view = new CursorContextMenuView(items, dummyExecutor);
                Assert.NotNull(view);
                Assert.NotNull(view.PrimaryHintsText);
                Assert.NotNull(view.ModifierHintsText);

                // 1. First item is Snippet 1 (selected by default)
                view.ItemsListBox.SelectedIndex = 0;
                var primaryText = GetInlinesText(view.PrimaryHintsText.Inlines);
                var modifierText = GetInlinesText(view.ModifierHintsText.Inlines);

                Assert.Contains("Paste", primaryText);
                Assert.DoesNotContain("Shift+", modifierText);
                Assert.DoesNotContain("Ctrl+", modifierText);
                Assert.Contains("Alt+", modifierText);
                Assert.Contains("Settings", modifierText);

                // 2. Second item is Web URL
                view.ItemsListBox.SelectedIndex = 1;
                primaryText = GetInlinesText(view.PrimaryHintsText.Inlines);
                modifierText = GetInlinesText(view.ModifierHintsText.Inlines);

                Assert.Contains("Open URL", primaryText);
                Assert.DoesNotContain("Shift+", modifierText);
                Assert.DoesNotContain("Ctrl+", modifierText);
                Assert.Contains("Alt+", modifierText);

                // 3. Third item is Local Executable (cmd.exe on PATH)
                view.ItemsListBox.SelectedIndex = 2;
                primaryText = GetInlinesText(view.PrimaryHintsText.Inlines);
                modifierText = GetInlinesText(view.ModifierHintsText.Inlines);

                Assert.Contains("Run", primaryText);
                Assert.Contains("Ctrl+", modifierText);
                Assert.Contains("Admin", modifierText);
                Assert.Contains("Shift+", modifierText);
                Assert.Contains("Reveal", modifierText);
                Assert.Contains("Alt+", modifierText);

                // 4. Fourth item is Workflow
                view.ItemsListBox.SelectedIndex = 3;
                primaryText = GetInlinesText(view.PrimaryHintsText.Inlines);
                modifierText = GetInlinesText(view.ModifierHintsText.Inlines);

                Assert.Contains("Run", primaryText);
                Assert.Contains("Ctrl+", modifierText);
                Assert.Contains("Admin", modifierText);
                Assert.DoesNotContain("Shift+", modifierText);
                Assert.Contains("Alt+", modifierText);

                // 5. Fifth item is Folder
                view.ItemsListBox.SelectedIndex = 4;
                primaryText = GetInlinesText(view.PrimaryHintsText.Inlines);
                modifierText = GetInlinesText(view.ModifierHintsText.Inlines);

                Assert.Contains("Open Submenu", primaryText);
                Assert.DoesNotContain("Shift+", modifierText);
                Assert.DoesNotContain("Ctrl+", modifierText);
                Assert.Contains("Alt+", modifierText);

                view.Close();
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
    public void CursorContextMenuView_TypeToFilter_FiltersAndHighlightsCorrectly()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationAndThemeResources();

                var items = new List<TriggerItem>
                {
                    new() { Id = Guid.NewGuid(), Name = "Google Chrome", ActionType = ActionType.Shell, Payload = new ActionPayload { Command = "chrome.exe" } },
                    new() { Id = Guid.NewGuid(), Name = "Google Drive", ActionType = ActionType.Shell, Payload = new ActionPayload { Command = "https://drive.google.com" } },
                    new() { Id = Guid.NewGuid(), Name = "Notepad Scratchpad", ActionType = ActionType.Shell, Payload = new ActionPayload { Command = "notepad.exe" } },
                    new() { Id = Guid.NewGuid(), Name = "Calculator", ActionType = ActionType.Shell, Payload = new ActionPayload { Command = "calc.exe" } }
                };

                var view = new CursorContextMenuView(items, new DummyExecutor());

                // 1. Initial State: No filter
                Assert.Equal(string.Empty, view.FilterQuery);
                Assert.Equal(Visibility.Collapsed, view.SearchFilterPill.Visibility);
                Assert.Equal(4, view.DisplayedItems.Count);

                // 2. Apply filter for "Google"
                view.SetFilterQueryForTesting("Google");
                Assert.Equal("Google", view.FilterQuery);
                Assert.Equal(Visibility.Visible, view.SearchFilterPill.Visibility);
                Assert.Equal("Google", view.SearchFilterText.Text);
                Assert.Equal(2, view.DisplayedItems.Count);
                Assert.Equal("Google Chrome", view.DisplayedItems[0].Name);
                Assert.Equal("Google Drive", view.DisplayedItems[1].Name);

                // Quick keys re-indexed
                Assert.Equal("1", view.DisplayedItems[0].AcceleratorKey);
                Assert.Equal("2", view.DisplayedItems[1].AcceleratorKey);

                // Highlighting segments present
                var chromeSegments = view.DisplayedItems[0].HighlightedNameSegments;
                Assert.NotEmpty(chromeSegments);
                Assert.Contains(chromeSegments, s => s.IsMatched && s.Text.Equals("Google", StringComparison.OrdinalIgnoreCase));

                // 3. Clear filter
                view.SetFilterQueryForTesting(string.Empty);
                Assert.Equal(string.Empty, view.FilterQuery);
                Assert.Equal(Visibility.Collapsed, view.SearchFilterPill.Visibility);
                Assert.Equal(4, view.DisplayedItems.Count);

                // 4. Filter with no matches shows empty notice
                view.SetFilterQueryForTesting("xyznonexistent");
                Assert.Empty(view.DisplayedItems);
                Assert.Equal(Visibility.Collapsed, view.ItemsListBox.Visibility);
                Assert.Equal(Visibility.Visible, view.EmptyFolderNotice.Visibility);
                Assert.Contains("xyznonexistent", view.EmptyFolderNotice.Text);

                view.Close();
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
    public void CursorContextMenuView_TypeToFilter_KeyboardInteractionsWork()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationAndThemeResources();

                var items = new List<TriggerItem>
                {
                    new() { Id = Guid.NewGuid(), Name = "Google Chrome", ActionType = ActionType.Shell, Payload = new ActionPayload { Command = "chrome.exe" } },
                    new() { Id = Guid.NewGuid(), Name = "Firefox", ActionType = ActionType.Shell, Payload = new ActionPayload { Command = "firefox.exe" } },
                    new() { Id = Guid.NewGuid(), Name = "Notepad", ActionType = ActionType.Shell, Payload = new ActionPayload { Command = "notepad.exe" } }
                };

                var view = new CursorContextMenuView(items, new DummyExecutor());

                // Simulate typing 'f' (Key.F)
                var keyEventArgsF = new System.Windows.Input.KeyEventArgs(
                    System.Windows.Input.Keyboard.PrimaryDevice,
                    new System.Windows.Interop.HwndSource(0, 0, 0, 0, 0, "", IntPtr.Zero),
                    0,
                    System.Windows.Input.Key.F)
                {
                    RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent
                };
                view.RaiseEvent(keyEventArgsF);

                Assert.Equal("f", view.FilterQuery);
                Assert.Equal(Visibility.Visible, view.SearchFilterPill.Visibility);
                Assert.Single(view.DisplayedItems);
                Assert.Equal("Firefox", view.DisplayedItems[0].Name);

                // Simulate typing 'i' (Key.I)
                var keyEventArgsI = new System.Windows.Input.KeyEventArgs(
                    System.Windows.Input.Keyboard.PrimaryDevice,
                    new System.Windows.Interop.HwndSource(0, 0, 0, 0, 0, "", IntPtr.Zero),
                    0,
                    System.Windows.Input.Key.I)
                {
                    RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent
                };
                view.RaiseEvent(keyEventArgsI);
                Assert.Equal("fi", view.FilterQuery);

                // Simulate Backspace (Key.Back)
                var keyEventArgsBack = new System.Windows.Input.KeyEventArgs(
                    System.Windows.Input.Keyboard.PrimaryDevice,
                    new System.Windows.Interop.HwndSource(0, 0, 0, 0, 0, "", IntPtr.Zero),
                    0,
                    System.Windows.Input.Key.Back)
                {
                    RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent
                };
                view.RaiseEvent(keyEventArgsBack);
                Assert.Equal("f", view.FilterQuery);

                // Simulate Escape (Key.Escape) -> clears filter
                var keyEventArgsEsc = new System.Windows.Input.KeyEventArgs(
                    System.Windows.Input.Keyboard.PrimaryDevice,
                    new System.Windows.Interop.HwndSource(0, 0, 0, 0, 0, "", IntPtr.Zero),
                    0,
                    System.Windows.Input.Key.Escape)
                {
                    RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent
                };
                view.RaiseEvent(keyEventArgsEsc);
                Assert.Equal(string.Empty, view.FilterQuery);
                Assert.Equal(Visibility.Collapsed, view.SearchFilterPill.Visibility);
                Assert.Equal(3, view.DisplayedItems.Count);

                view.Close();
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

