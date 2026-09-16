using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.UI.Theme;
using TriggerPoint.UI.Views;
using Xunit;

namespace TriggerPoint.Tests;

public class TreeDensityAndDisabledItemTests
{
    private class DummyExecutor : IActionExecutor
    {
        public event EventHandler<string>? StatusChanged { add { } remove { } }
        public event EventHandler<(TriggerItem item, string message)>? ExecutionSucceeded { add { } remove { } }
        public event EventHandler<(TriggerItem item, string message)>? ExecutionFailed { add { } remove { } }
        public System.Threading.Tasks.Task ExecuteAsync(TriggerItem item, ExecutionOverride executionOverride = ExecutionOverride.Standard, IntPtr? targetHwnd = null) => System.Threading.Tasks.Task.CompletedTask;
    }

    private static void EnsureApplication()
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
        Application.Current.Resources["BorderBrush"] = new SolidColorBrush(Colors.Gray);
        Application.Current.Resources["BgInputBrush"] = new SolidColorBrush(Colors.Black);
        Application.Current.Resources["TextPrimaryBrush"] = new SolidColorBrush(Colors.White);
        Application.Current.Resources["TextSecondaryBrush"] = new SolidColorBrush(Colors.LightGray);
        Application.Current.Resources["TextMutedBrush"] = new SolidColorBrush(Colors.DarkGray);
        Application.Current.Resources["AccentBrush"] = new SolidColorBrush(Colors.DodgerBlue);
        Application.Current.Resources["AccentHoverBrush"] = new SolidColorBrush(Colors.DeepSkyBlue);
        Application.Current.Resources["BgHoverBrush"] = new SolidColorBrush(Colors.DimGray);
        Application.Current.Resources["BgSecondaryBrush"] = new SolidColorBrush(Colors.Black);
        Application.Current.Resources["BgTertiaryBrush"] = new SolidColorBrush(Colors.DarkSlateGray);
        Application.Current.Resources["BorderSubtleBrush"] = new SolidColorBrush(Colors.Gray);
        Application.Current.Resources["TagAllowedBgBrush"] = new SolidColorBrush(Colors.DarkGreen);
        Application.Current.Resources["TagAllowedBorderBrush"] = new SolidColorBrush(Colors.Green);
        Application.Current.Resources["TagAllowedTextBrush"] = new SolidColorBrush(Colors.LightGreen);
        Application.Current.Resources["TagExcludedBgBrush"] = new SolidColorBrush(Colors.DarkRed);
        Application.Current.Resources["TagExcludedBorderBrush"] = new SolidColorBrush(Colors.Red);
        Application.Current.Resources["TagExcludedTextBrush"] = new SolidColorBrush(Colors.Pink);
    }

    [Fact]
    public void AppSettings_Defaults_AreCompactTreeAndShowDisabled()
    {
        var settings = new AppSettings();
        Assert.True(settings.CompactTreeDensity, "CompactTreeDensity should default to true (compact view as default).");
        Assert.True(settings.ShowDisabledItemsInTree, "ShowDisabledItemsInTree should default to true.");
        Assert.True(settings.ConfirmRevertChanges, "ConfirmRevertChanges should default to true.");
    }

    [Fact]
    public void TriggerItem_IsEnabled_DefaultsToTrue()
    {
        var item = new TriggerItem { Name = "Test Action" };
        Assert.True(item.IsEnabled, "TriggerItem.IsEnabled should default to true.");
    }

    [Fact]
    public void ThemeManager_ApplyTreeDensity_UpdatesTreeItemPaddingResource()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplication();

                // Test Compact (true) -> vertical padding 1
                ThemeManager.ApplyTreeDensity(true);
                var compactPadding = (Thickness)Application.Current!.Resources["TreeItemPadding"];
                Assert.Equal(1, compactPadding.Top);
                Assert.Equal(1, compactPadding.Bottom);

                // Test Comfortable/Expanded (false) -> vertical padding 4
                ThemeManager.ApplyTreeDensity(false);
                var comfortablePadding = (Thickness)Application.Current!.Resources["TreeItemPadding"];
                Assert.Equal(4, comfortablePadding.Top);
                Assert.Equal(4, comfortablePadding.Bottom);

                // Reset back to Compact
                ThemeManager.ApplyTreeDensity(true);
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
            throw new InvalidOperationException($"ThemeManager density test failed: {caughtEx.Message}", caughtEx);
        }
    }

    [Fact]
    public void CommandPaletteView_ExcludesDisabledItemsFromExecutionCandidates()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplication();

                var enabledItem = new TriggerItem
                {
                    Id = Guid.NewGuid(),
                    Name = "Enabled Action",
                    IsEnabled = true,
                    ActionType = ActionType.Shell,
                    Payload = new ActionPayload { Command = "notepad.exe" }
                };

                var disabledItem = new TriggerItem
                {
                    Id = Guid.NewGuid(),
                    Name = "Disabled Action",
                    IsEnabled = false,
                    ActionType = ActionType.Shell,
                    Payload = new ActionPayload { Command = "calc.exe" }
                };

                var items = new List<TriggerItem> { enabledItem, disabledItem };
                var dummyExecutor = new DummyExecutor();
                var view = new CommandPaletteView(items, dummyExecutor);

                // Perform search for "Action"
                view.SearchTextBox.Text = "Action";

                // Ensure the items source in ResultsListBox only includes the enabled item
                var results = view.ResultsListBox.ItemsSource as IEnumerable<object>;
                Assert.NotNull(results);
                var resultList = results.OfType<PaletteItemViewModel>().Select(vm => vm.Item).ToList();

                Assert.Contains(resultList, x => x.Id == enabledItem.Id);
                Assert.DoesNotContain(resultList, x => x.Id == disabledItem.Id);
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
            throw new InvalidOperationException($"CommandPaletteView disabled items test failed: {caughtEx.Message}", caughtEx);
        }
    }

    [Fact]
    public void ConfirmationDialog_ShowDoNotAskAgain_ControlsVisibilityAndCheckboxState()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplication();

                // Dialog with showDoNotAskAgain = false
                var dlgWithout = new ConfirmationDialog("Test Message", "Test Title", "Confirm", "Cancel", showDoNotAskAgain: false);
                Assert.Equal(Visibility.Collapsed, dlgWithout.DoNotAskAgainCheck.Visibility);
                Assert.False(dlgWithout.DoNotAskAgain);

                // Dialog with showDoNotAskAgain = true
                var dlgWith = new ConfirmationDialog("Test Message", "Test Title", "Confirm", "Cancel", showDoNotAskAgain: true);
                Assert.Equal(Visibility.Visible, dlgWith.DoNotAskAgainCheck.Visibility);
                Assert.False(dlgWith.DoNotAskAgain);

                dlgWith.DoNotAskAgainCheck.IsChecked = true;
                Assert.True(dlgWith.DoNotAskAgain);
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
            throw new InvalidOperationException($"ConfirmationDialog test failed: {caughtEx.Message}", caughtEx);
        }
    }

    [Fact]
    public void ContextRules_Changes_Invoke_IsDirty()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplication();

                var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TP_DirtyTest_" + Guid.NewGuid());
                System.IO.Directory.CreateDirectory(tempDir);
                try
                {
                    var repo = new TriggerPoint.Infrastructure.Persistence.JsonConfigRepository(tempDir);
                    var item = new TriggerItem
                    {
                        Id = Guid.NewGuid(),
                        Name = "Test Action",
                        ActionType = ActionType.Shell,
                        Payload = new ActionPayload { Command = "notepad.exe" }
                    };
                    repo.SaveAsync(new[] { item }).GetAwaiter().GetResult();

                    using var listener = new TriggerPoint.Infrastructure.Win32.Win32HotkeyListener();
                    var window = new SettingsWindow(repo, listener, new DummyExecutor());
                    window.SelectTreeItem(item);

                    Assert.False(window.SaveBtn.IsEnabled);

                    // Test 1: Typing text into InlineInputBox marks dirty immediately
                    window.AllowedProcessesTagInput.InlineInputBox.Text = "devenv.exe";
                    bool dirtyAfterTyping = window.SaveBtn.IsEnabled;

                    // Test 2: Commit pending input converts to tag and stays dirty
                    bool committed = window.AllowedProcessesTagInput.CommitPendingInput();
                    bool dirtyAfterCommit = window.SaveBtn.IsEnabled;
                    var tags = window.AllowedProcessesTagInput.GetTags();

                    // Reset dirty before test window close to avoid unsaved changes dialog
                    typeof(SettingsWindow).GetMethod("SetDirty", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(window, new object[] { false });

                    // Test 3: AddTag directly invokes dirty
                    window.ExcludedProcessesTagInput.AddTag("notepad.exe");
                    bool dirtyAfterAddTag = window.SaveBtn.IsEnabled;

                    // Reset dirty before next action
                    typeof(SettingsWindow).GetMethod("SetDirty", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(window, new object[] { false });

                    // Test 4: InheritParentRulesCheck invokes dirty
                    window.InheritParentRulesCheck.IsChecked = false;
                    window.InheritParentRulesCheck.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    bool dirtyAfterCheck = window.SaveBtn.IsEnabled;

                    // Reset dirty before window.Close() to prevent unshown window Owner dialog
                    typeof(SettingsWindow).GetMethod("SetDirty", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(window, new object[] { false });
                    window.Close();

                    Assert.True(dirtyAfterTyping, "Typing into InlineInputBox should make SaveBtn enabled");
                    Assert.True(committed, "CommitPendingInput should successfully commit pending text");
                    Assert.Contains("devenv.exe", tags);
                    Assert.True(dirtyAfterCommit, "SaveBtn should remain enabled after commit");
                    Assert.True(dirtyAfterAddTag, "AddTag should make SaveBtn enabled");
                    Assert.True(dirtyAfterCheck, "Checkbox click should make SaveBtn enabled");
                }
                finally
                {
                    try
                    {
                        if (System.IO.Directory.Exists(tempDir)) System.IO.Directory.Delete(tempDir, true);
                    }
                    catch { }
                }
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
    public void SettingsWindow_CreatingOrSelectingNewItem_ResetsEditorScrollToTop()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplication();

                var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TP_ScrollTest_" + Guid.NewGuid());
                System.IO.Directory.CreateDirectory(tempDir);
                try
                {
                    var repo = new TriggerPoint.Infrastructure.Persistence.JsonConfigRepository(tempDir);
                    var item = new TriggerItem
                    {
                        Id = Guid.NewGuid(),
                        Name = "Item 1",
                        ActionType = ActionType.Shell,
                        Payload = new ActionPayload { Command = "notepad.exe" }
                    };
                    repo.SaveAsync(new[] { item }).GetAwaiter().GetResult();

                    using var listener = new TriggerPoint.Infrastructure.Win32.Win32HotkeyListener();
                    var window = new SettingsWindow(repo, listener, new DummyExecutor());
                    window.SelectTreeItem(item);

                    // Simulate scrolling down on the item
                    window.EditorScrollViewer.ScrollToVerticalOffset(150);

                    // Create a new item
                    window.CreateAndEditNewItem("Item 2", ActionType.Shell);

                    // Verify the editor scroll offset was reset to 0
                    Assert.Equal(0, window.EditorScrollViewer.VerticalOffset);
                }
                finally
                {
                    try
                    {
                        if (System.IO.Directory.Exists(tempDir)) System.IO.Directory.Delete(tempDir, true);
                    }
                    catch { }
                }
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
