using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using TriggerPoint.UI.Views;
using Xunit;

namespace TriggerPoint.Tests;

public class VariablePickerDialogTests
{
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
    public void VariablePickerDialog_Instantiates_AndPopulatesCuratedAndLiveItems()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationAndThemeResources();

                var workflowVars = new List<string> { "ticketId", "customerEmail" };
                var dialog = new VariablePickerDialog(workflowVars);

                Assert.NotNull(dialog);
                Assert.NotNull(dialog.VariablesListBox);
                Assert.NotNull(dialog.SearchBox);
                Assert.NotNull(dialog.ChipAll);
                Assert.NotNull(dialog.ChipEnv);
                Assert.NotNull(dialog.ChipSystem);
                Assert.NotNull(dialog.ChipWorkflow);

                var items = dialog.VariablesListBox.ItemsSource as IEnumerable<VariablePickerItem>;
                Assert.NotNull(items);
                var itemList = items.ToList();

                // Must contain workflow variables
                Assert.Contains(itemList, i => i.Token == "{ticketId}" && i.Category == "Workflow");
                Assert.Contains(itemList, i => i.Token == "{customerEmail}" && i.Category == "Workflow");

                // Must contain system tokens
                Assert.Contains(itemList, i => i.Token == "{date:yyyy-MM-dd}" && i.Category == "System");
                Assert.Contains(itemList, i => i.Token == "{clipboard}" && i.Category == "System");

                // Must contain standard Windows environment variables
                Assert.Contains(itemList, i => i.Token == "%USERPROFILE%" && i.Category == "Env");
                Assert.Contains(itemList, i => i.Token == "%TEMP%" && i.Category == "Env");
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
            throw new InvalidOperationException($"VariablePickerDialog initialization failed: {caughtEx.Message}", caughtEx);
        }
    }

    [Fact]
    public void VariablePickerDialog_SearchFilter_FiltersResultsDynamically()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationAndThemeResources();

                var dialog = new VariablePickerDialog();
                dialog.SearchBox.Text = "TEMP";

                var items = (dialog.VariablesListBox.ItemsSource as IEnumerable<VariablePickerItem>)?.ToList();
                Assert.NotNull(items);
                Assert.True(items.Count > 0);
                Assert.All(items, i =>
                    Assert.True(
                        i.Token.Contains("TEMP", StringComparison.OrdinalIgnoreCase) ||
                        i.Description.Contains("TEMP", StringComparison.OrdinalIgnoreCase) ||
                        i.LiveValue.Contains("TEMP", StringComparison.OrdinalIgnoreCase)));
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
            throw new InvalidOperationException($"Search filtering failed: {caughtEx.Message}", caughtEx);
        }
    }
}
