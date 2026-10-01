using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
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
            new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
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

    [Fact]
    public void SettingsWindow_EditingItemNameAndDesc_SetsDirtyState()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplication();

                var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TP_DirtyTest_" + Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(tempDir);
                try
                {
                    var repo = new TriggerPoint.Infrastructure.Persistence.JsonConfigRepository(tempDir);
                    var workflow = new TriggerItem
                    {
                        Id = Guid.NewGuid(),
                        Name = "My Workflow",
                        Description = "Original Desc",
                        ActionType = ActionType.Workflow,
                        Payload = new ActionPayload { WorkflowSteps = [] }
                    };
                    var snippet = new TriggerItem
                    {
                        Id = Guid.NewGuid(),
                        Name = "My Snippet",
                        Description = "Snippet Desc",
                        ActionType = ActionType.Snippet,
                        Payload = new ActionPayload { SnippetTemplate = "Hello" }
                    };
                    var macro = new TriggerItem
                    {
                        Id = Guid.NewGuid(),
                        Name = "My Macro",
                        Description = "Macro Desc",
                        ActionType = ActionType.Macro,
                        Payload = new ActionPayload { Macro = new MacroPayload() }
                    };
                    repo.SaveAsync(new[] { workflow, snippet, macro }).GetAwaiter().GetResult();

                    using var listener = new TriggerPoint.Infrastructure.Win32.Win32HotkeyListener();
                    var window = new SettingsWindow(repo, listener, new DummyExecutor());

                    // 1. Workflow
                    window.SelectTreeItem(workflow);
                    Assert.False(window.SaveBtn.IsEnabled);

                    window.ItemNameBox.Text = "My Workflow Renamed";
                    Assert.True(window.SaveBtn.IsEnabled, "Editing workflow name should enable SaveBtn");

                    window.ItemNameBox.Text = "My Workflow";
                    Assert.False(window.SaveBtn.IsEnabled, "Reverting workflow name should disable SaveBtn");

                    window.ItemDescBox.Text = "Changed Desc";
                    Assert.True(window.SaveBtn.IsEnabled, "Editing workflow description should enable SaveBtn");

                    window.ItemDescBox.Text = "Original Desc";
                    Assert.False(window.SaveBtn.IsEnabled, "Reverting workflow description should disable SaveBtn");

                    // 2. Snippet
                    window.SelectTreeItem(snippet);
                    Assert.False(window.SaveBtn.IsEnabled);

                    window.ItemNameBox.Text = "My Snippet Renamed";
                    Assert.True(window.SaveBtn.IsEnabled, "Editing snippet name should enable SaveBtn");

                    window.ItemNameBox.Text = "My Snippet";
                    Assert.False(window.SaveBtn.IsEnabled, "Reverting snippet name should disable SaveBtn");

                    window.ItemDescBox.Text = "New Snippet Desc";
                    Assert.True(window.SaveBtn.IsEnabled, "Editing snippet desc should enable SaveBtn");

                    // 3. Macro
                    typeof(SettingsWindow).GetMethod("SetDirty", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(window, new object[] { false });
                    window.SelectTreeItem(macro);
                    Assert.False(window.SaveBtn.IsEnabled);

                    window.ItemNameBox.Text = "My Macro Renamed";
                    Assert.True(window.SaveBtn.IsEnabled, "Editing macro name should enable SaveBtn");

                    window.ItemDescBox.Text = "New Macro Desc";
                    Assert.True(window.SaveBtn.IsEnabled, "Editing macro desc should enable SaveBtn");

                    // Revert before close
                    typeof(SettingsWindow).GetMethod("SetDirty", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(window, new object[] { false });
                    window.Close();
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
    public void SettingsWindow_CreatedSnippetAndMacro_EditingNameAndDesc_SetsDirtyState()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplication();

                var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TP_CreateDirtyTest_" + Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(tempDir);
                try
                {
                    var repo = new TriggerPoint.Infrastructure.Persistence.JsonConfigRepository(tempDir);
                    using var listener = new TriggerPoint.Infrastructure.Win32.Win32HotkeyListener();
                    var window = new SettingsWindow(repo, listener, new DummyExecutor());

                    // Create Snippet
                    window.CreateAndEditNewItem("My Snippet", ActionType.Snippet);
                    Assert.True(window.SaveBtn.IsEnabled);

                    // Save configuration
                    var saveTask = (Task<bool>)typeof(SettingsWindow).GetMethod("SaveConfigurationCoreAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, null)!;
                    saveTask.GetAwaiter().GetResult();
                    Assert.False(window.SaveBtn.IsEnabled);

                    // Edit name
                    window.ItemNameBox.Text = "My Snippet Renamed";
                    Assert.True(window.SaveBtn.IsEnabled, "Editing created snippet name should enable SaveBtn");

                    // Revert name
                    window.ItemNameBox.Text = "My Snippet";
                    Assert.False(window.SaveBtn.IsEnabled, "Reverting created snippet name should disable SaveBtn");

                    // Edit desc
                    window.ItemDescBox.Text = "My Snippet Desc";
                    Assert.True(window.SaveBtn.IsEnabled, "Editing created snippet desc should enable SaveBtn");

                    // Save configuration
                    saveTask = (Task<bool>)typeof(SettingsWindow).GetMethod("SaveConfigurationCoreAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, null)!;
                    saveTask.GetAwaiter().GetResult();
                    Assert.False(window.SaveBtn.IsEnabled);

                    // Create Macro
                    window.CreateAndEditNewItem("My Macro", ActionType.Macro);
                    Assert.True(window.SaveBtn.IsEnabled);

                    // Save configuration
                    saveTask = (Task<bool>)typeof(SettingsWindow).GetMethod("SaveConfigurationCoreAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, null)!;
                    saveTask.GetAwaiter().GetResult();
                    Assert.False(window.SaveBtn.IsEnabled);

                    // Edit name
                    window.ItemNameBox.Text = "My Macro Renamed";
                    Assert.True(window.SaveBtn.IsEnabled, "Editing created macro name should enable SaveBtn");

                    // Revert name
                    window.ItemNameBox.Text = "My Macro";
                    Assert.False(window.SaveBtn.IsEnabled, "Reverting created macro name should disable SaveBtn");

                    // Edit desc
                    window.ItemDescBox.Text = "My Macro Desc";
                    Assert.True(window.SaveBtn.IsEnabled, "Editing created macro desc should enable SaveBtn");

                    window.Close();
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
        thread.Join(10000);

        if (caughtEx != null) throw caughtEx;
    }

    [Fact]
    public void AccentButtonStyle_InLightMode_ResolvesWhiteForegroundOnChildText()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplication();
                TriggerPoint.UI.Theme.ThemeManager.ApplyTheme(TriggerPoint.UI.Theme.AppTheme.Light);

                var win = new Window();
                var sp = new StackPanel();
                var accentStyle = Application.Current.TryFindResource("AccentButtonStyle") as Style;

                var btnEnabled = new Button { Style = accentStyle, Content = "💾 Save" };
                var btnDisabled = new Button { Style = accentStyle, Content = "💾 Save (Disabled)", IsEnabled = false };

                sp.Children.Add(btnEnabled);
                sp.Children.Add(btnDisabled);
                win.Content = sp;

                win.Show();
                win.UpdateLayout();
                btnEnabled.ApplyTemplate();
                btnDisabled.ApplyTemplate();

                // Inspect visual children of enabled button (may be TextBlock or AccessText depending on RecognizesAccessKey)
                var tbEnabled = (DependencyObject?)FindVisualChild<TextBlock>(btnEnabled) ?? FindVisualChild<AccessText>(btnEnabled);
                Assert.NotNull(tbEnabled);
                var enabledBrush = (SolidColorBrush)(tbEnabled is TextBlock tbe ? tbe.Foreground : ((AccessText)tbEnabled).Foreground);
                Assert.Equal(Colors.White, enabledBrush.Color);

                // Inspect visual children of disabled button
                var tbDisabled = (DependencyObject?)FindVisualChild<TextBlock>(btnDisabled) ?? FindVisualChild<AccessText>(btnDisabled);
                Assert.NotNull(tbDisabled);
                var disabledBrush = (SolidColorBrush)(tbDisabled is TextBlock tbd ? tbd.Foreground : ((AccessText)tbDisabled).Foreground);
                Assert.Equal(Colors.White, disabledBrush.Color);

                win.Close();
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
    public void Test_UserActualConfig_SnippetAndMacro()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplication();

                string configPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TriggerPoint");
                if (!System.IO.File.Exists(System.IO.Path.Combine(configPath, "triggerpoint.json"))) return;

                var repo = new TriggerPoint.Infrastructure.Persistence.JsonConfigRepository(configPath);
                var items = repo.LoadAsync().GetAwaiter().GetResult().ToList();

                var snippet = items.FirstOrDefault(x => x.ActionType == ActionType.Snippet);
                var macro = items.FirstOrDefault(x => x.ActionType == ActionType.Macro);

                using var listener = new TriggerPoint.Infrastructure.Win32.Win32HotkeyListener();
                var window = new SettingsWindow(repo, listener, new DummyExecutor());

                if (snippet != null)
                {
                    string origName = snippet.Name;
                    string origDesc = snippet.Description ?? "";

                    window.SelectTreeItem(snippet);
                    Assert.False(window.SaveBtn.IsEnabled, "Snippet initial clean");

                    window.ItemNameBox.Text = origName + " Edited";
                    Assert.True(window.SaveBtn.IsEnabled, "Snippet name edit should set dirty");

                    window.ItemNameBox.Text = origName;
                    Assert.False(window.SaveBtn.IsEnabled, "Snippet name revert should unset dirty");

                    window.ItemDescBox.Text = origDesc + " Edited";
                    Assert.True(window.SaveBtn.IsEnabled, "Snippet desc edit should set dirty");

                    window.ItemDescBox.Text = origDesc;
                    Assert.False(window.SaveBtn.IsEnabled, "Snippet desc revert should unset dirty");
                }

                if (macro != null)
                {
                    string origName = macro.Name;
                    string origDesc = macro.Description ?? "";

                    typeof(SettingsWindow).GetMethod("SetDirty", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(window, new object[] { false });
                    window.SelectTreeItem(macro);
                    Assert.False(window.SaveBtn.IsEnabled, "Macro initial clean");

                    window.ItemNameBox.Text = origName + " Edited";
                    Assert.True(window.SaveBtn.IsEnabled, "Macro name edit should set dirty");

                    window.ItemNameBox.Text = origName;
                    Assert.False(window.SaveBtn.IsEnabled, "Macro name revert should unset dirty");

                    window.ItemDescBox.Text = origDesc + " Edited";
                    Assert.True(window.SaveBtn.IsEnabled, "Macro desc edit should set dirty");

                    window.ItemDescBox.Text = origDesc;
                    Assert.False(window.SaveBtn.IsEnabled, "Macro desc revert should unset dirty");
                }

                typeof(SettingsWindow).GetMethod("SetDirty", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(window, new object[] { false });
                window.Close();
            }
            catch (Exception ex)
            {
                caughtEx = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(10000);

        if (caughtEx != null) throw caughtEx;
    }

    [Fact]
    public void Test_RealUi_WithWindowShow()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplication();
                SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));

                string configPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TriggerPoint");
                if (!System.IO.File.Exists(System.IO.Path.Combine(configPath, "triggerpoint.json"))) return;

                var repo = new TriggerPoint.Infrastructure.Persistence.JsonConfigRepository(configPath);
                using var listener = new TriggerPoint.Infrastructure.Win32.Win32HotkeyListener();
                var window = new SettingsWindow(repo, listener, new DummyExecutor());

                window.Show();

                // Wait for data load on the dispatcher
                while (!(bool)typeof(SettingsWindow).GetField("_isDataLoaded", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!)
                {
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                    Thread.Sleep(20);
                }

                // DoEvents
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                var items = (List<TriggerItem>)typeof(SettingsWindow).GetField("_items", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
                var snippet = items.FirstOrDefault(x => x.ActionType == ActionType.Snippet);
                var macro = items.FirstOrDefault(x => x.ActionType == ActionType.Macro);

                if (snippet != null)
                {
                    var snippetVm = (TriggerTreeItemViewModel)typeof(SettingsWindow).GetMethod("FindViewModel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, new[] { typeof(TriggerItem) })!.Invoke(window, new object[] { snippet })!;
                    snippetVm.IsSelected = true;
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                    Assert.False(window.SaveBtn.IsEnabled, "Real UI snippet initial clean");

                    string orig = window.ItemNameBox.Text;
                    window.ItemNameBox.Text = orig + " Edited";
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                    Assert.True(window.SaveBtn.IsEnabled, "Real UI snippet name edit should set dirty");

                    window.ItemNameBox.Text = orig;
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                    Assert.False(window.SaveBtn.IsEnabled, "Real UI snippet name revert should unset dirty");

                    string origDesc = window.ItemDescBox.Text;
                    window.ItemDescBox.Text = origDesc + " Edited";
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                    Assert.True(window.SaveBtn.IsEnabled, "Real UI snippet desc edit should set dirty");

                    window.ItemDescBox.Text = origDesc;
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                    Assert.False(window.SaveBtn.IsEnabled, "Real UI snippet desc revert should unset dirty");
                }

                if (macro != null)
                {
                    var macroVm = (TriggerTreeItemViewModel)typeof(SettingsWindow).GetMethod("FindViewModel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, new[] { typeof(TriggerItem) })!.Invoke(window, new object[] { macro })!;
                    macroVm.IsSelected = true;
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                    Assert.False(window.SaveBtn.IsEnabled, "Real UI macro initial clean");

                    string orig = window.ItemNameBox.Text;
                    window.ItemNameBox.Text = orig + " Edited";
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                    Assert.True(window.SaveBtn.IsEnabled, "Real UI macro name edit should set dirty");

                    window.ItemNameBox.Text = orig;
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                    Assert.False(window.SaveBtn.IsEnabled, "Real UI macro name revert should unset dirty");

                    string origDesc = window.ItemDescBox.Text;
                    window.ItemDescBox.Text = origDesc + " Edited";
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                    Assert.True(window.SaveBtn.IsEnabled, "Real UI macro desc edit should set dirty");

                    window.ItemDescBox.Text = origDesc;
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                    Assert.False(window.SaveBtn.IsEnabled, "Real UI macro desc revert should unset dirty");
                }

                typeof(SettingsWindow).GetMethod("SetDirty", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(window, new object[] { false });
                window.Close();
            }
            catch (Exception ex)
            {
                caughtEx = new Exception($"Error in STA thread: {ex}\nStackTrace: {ex.StackTrace}");
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(10000);

        if (caughtEx != null) throw caughtEx;
    }

    [Fact]
    public void Test_RecycledItem_CannotToggleEnabled_AndTestButtonIsHidden()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplication();
                SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));

                string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TP_RecycleBinUiTest_" + Guid.NewGuid());
                System.IO.Directory.CreateDirectory(tempDir);
                try
                {
                    var repo = new TriggerPoint.Infrastructure.Persistence.JsonConfigRepository(tempDir);
                    var activeItem = new TriggerItem
                    {
                        Id = Guid.NewGuid(),
                        Name = "Active Shell",
                        ActionType = ActionType.Shell,
                        IsEnabled = true
                    };
                    var deletedItem = new TriggerItem
                    {
                        Id = Guid.NewGuid(),
                        Name = "Deleted Snippet",
                        ActionType = ActionType.Snippet,
                        IsEnabled = true
                    };

                    repo.SaveAsync(new[] { activeItem }).GetAwaiter().GetResult();
                    repo.MoveToRecycleBinAsync(deletedItem, new[] { activeItem, deletedItem }).GetAwaiter().GetResult();

                    using var listener = new TriggerPoint.Infrastructure.Win32.Win32HotkeyListener();
                    var window = new SettingsWindow(repo, listener, new DummyExecutor());

                    window.Show();

                    while (!(bool)typeof(SettingsWindow).GetField("_isDataLoaded", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!)
                    {
                        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                        Thread.Sleep(20);
                    }

                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                    var roots = (System.Collections.ObjectModel.ObservableCollection<TriggerTreeItemViewModel>)
                        typeof(SettingsWindow).GetField("_treeRoots", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
                    var binRoot = roots.FirstOrDefault(x => x.IsRecycleBinRoot);
                    Assert.NotNull(binRoot);
                    Assert.Single(binRoot.Children);
                    var recycledVm = binRoot.Children[0];

                    // 1. Select recycled item
                    window.SelectTreeItem(deletedItem);
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                    Assert.Equal(Visibility.Visible, window.ItemEnabledCheck.Visibility);
                    Assert.False(window.ItemEnabledCheck.IsEnabled, "Recycled item enabled checkbox must be disabled");
                    Assert.Equal(Visibility.Collapsed, window.TestActionBtn.Visibility);
                    Assert.False(window.SaveBtn.IsEnabled, "Save button must be disabled for recycled item");
                    Assert.Equal(Visibility.Collapsed, window.SaveBtn.Visibility);

                    // 2. Attempt to trigger ItemEnabledCheck click
                    window.ItemEnabledCheck.IsChecked = false;
                    typeof(SettingsWindow).GetMethod("ItemEnabledCheck_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                        .Invoke(window, new object[] { window.ItemEnabledCheck, new RoutedEventArgs() });

                    Assert.True(recycledVm.Item.IsEnabled, "Recycled item IsEnabled must not be changed");
                    Assert.False(window.SaveBtn.IsEnabled, "Recycled item change must not trigger SaveBtn");
                    Assert.Equal(Visibility.Collapsed, window.SaveBtn.Visibility);

                    // 3. Select Recycle Bin Root
                    window.SelectTreeItem(binRoot.Item);
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                    Assert.Equal(Visibility.Collapsed, window.ItemEnabledCheck.Visibility);
                    Assert.Equal(Visibility.Collapsed, window.TestActionBtn.Visibility);
                    Assert.False(window.SaveBtn.IsEnabled);
                    Assert.Equal(Visibility.Collapsed, window.SaveBtn.Visibility);

                    // 4. Select active item
                    window.SelectTreeItem(activeItem);
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                    Assert.Equal(Visibility.Visible, window.ItemEnabledCheck.Visibility);
                    Assert.True(window.ItemEnabledCheck.IsEnabled, "Active item enabled checkbox must be enabled");
                    Assert.Equal(Visibility.Visible, window.TestActionBtn.Visibility);
                    Assert.Equal(Visibility.Visible, window.SaveBtn.Visibility);

                    window.Close();
                }
                finally
                {
                    try { System.IO.Directory.Delete(tempDir, true); } catch { }
                }
            }
            catch (Exception ex)
            {
                caughtEx = new Exception($"Error in STA thread: {ex}\nStackTrace: {ex.StackTrace}");
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(10000);

        if (caughtEx != null) throw caughtEx;
    }

    [Fact]
    public void TogglingItemEnabled_AutoSavesAndDoesNotMarkFormDirty()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplication();
                SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));

                string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TP_EnabledTest_" + Guid.NewGuid());
                System.IO.Directory.CreateDirectory(tempDir);
                try
                {
                    var repo = new TriggerPoint.Infrastructure.Persistence.JsonConfigRepository(tempDir);

                    var activeItem = new TriggerItem
                    {
                        Id = Guid.NewGuid(),
                        Name = "Active Action",
                        ActionType = ActionType.Shell,
                        IsEnabled = true,
                        Payload = new ActionPayload { Command = "calc.exe" }
                    };
                    repo.SaveAsync(new[] { activeItem }).GetAwaiter().GetResult();

                    using var listener = new TriggerPoint.Infrastructure.Win32.Win32HotkeyListener();
                    var window = new SettingsWindow(repo, listener, new DummyExecutor());

                    window.Show();

                    while (!(bool)typeof(SettingsWindow).GetField("_isDataLoaded", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!)
                    {
                        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                        Thread.Sleep(20);
                    }

                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                    window.SelectTreeItem(activeItem);
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                    Assert.False(window.SaveBtn.IsEnabled, "Form must initially be clean");

                    // Toggle enabled via ItemEnabledCheck
                    window.ItemEnabledCheck.IsChecked = false;
                    typeof(SettingsWindow).GetMethod("ItemEnabledCheck_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                        .Invoke(window, new object[] { window.ItemEnabledCheck, new RoutedEventArgs() });

                    Assert.False(activeItem.IsEnabled, "Item IsEnabled must be toggled to false");
                    Assert.False(window.SaveBtn.IsEnabled, "SaveBtn must remain disabled (not marked dirty)");

                    // Re-toggle via ContextToggleEnabledItem
                    typeof(SettingsWindow).GetMethod("ContextToggleEnabledItem_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                        .Invoke(window, new object[] { window, new RoutedEventArgs() });

                    Assert.True(activeItem.IsEnabled, "Item IsEnabled must be toggled back to true");
                    Assert.False(window.SaveBtn.IsEnabled, "SaveBtn must still remain disabled");

                    window.Close();
                }
                finally
                {
                    try { System.IO.Directory.Delete(tempDir, true); } catch { }
                }
            }
            catch (Exception ex)
            {
                caughtEx = new Exception($"Error in STA thread: {ex}\nStackTrace: {ex.StackTrace}");
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(10000);

        if (caughtEx != null) throw caughtEx;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var nested = FindVisualChild<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }
}
