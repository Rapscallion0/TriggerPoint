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
}
