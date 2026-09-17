using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TriggerPoint.UI.Views;

public class VariablePickerItem
{
    public string Token { get; set; } = string.Empty;
    public string Category { get; set; } = "Env"; // "Env", "System", "Workflow"
    public string Description { get; set; } = string.Empty;
    public string LiveValue { get; set; } = string.Empty;

    public string LiveValuePreview => string.IsNullOrWhiteSpace(LiveValue)
        ? string.Empty
        : $"Value: {LiveValue}";

    public string DisplayCategory => Category switch
    {
        "Env" => "Windows Env",
        "System" => "System Token",
        "Workflow" => "Workflow Var",
        _ => Category
    };
}

public partial class VariablePickerDialog : Window
{
    private readonly List<VariablePickerItem> _allItems = [];
    private string _activeCategory = "All";

    public string? SelectedToken { get; private set; }

    public VariablePickerDialog(IEnumerable<string>? workflowVariables = null)
    {
        InitializeComponent();
        BuildItems(workflowVariables);
        UpdateCategoryChipStyles();
        ApplyFilter();

        Loaded += (s, e) => SearchBox.Focus();
    }

    private void BuildItems(IEnumerable<string>? workflowVariables)
    {
        _allItems.Clear();

        // 1. Workflow Variables
        if (workflowVariables != null)
        {
            foreach (var v in workflowVariables.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(v)) continue;
                _allItems.Add(new VariablePickerItem
                {
                    Token = $"{{{v}}}",
                    Category = "Workflow",
                    Description = $"Workflow Variable: {v}",
                    LiveValue = "Defined in previous step"
                });
            }
        }

        // 2. System & Dynamic Tokens
        var sysTokens = new (string Token, string Description)[]
        {
            ("{date:yyyy-MM-dd}", "Current date in ISO format (e.g. 2026-09-16)"),
            ("{date:MM/dd/yyyy}", "Current date in US format (e.g. 09/16/2026)"),
            ("{time:HH:mm:ss}", "Current time in 24-hour format (e.g. 17:30:00)"),
            ("{datetime:yyyyMMdd_HHmmss}", "Timestamp for unique file names & backups"),
            ("{clipboard}", "Current text content from Windows Clipboard"),
            ("{clipboard:trim}", "Trimmed text content from Windows Clipboard"),
            ("{guid}", "Randomly generated UUID / GUID"),
            ("{username}", "Current Windows logon username ({username})"),
            ("{machine}", "Current computer host network name ({machine})")
        };

        foreach (var (tok, desc) in sysTokens)
        {
            string sampleVal = tok switch
            {
                "{date:yyyy-MM-dd}" => DateTime.Now.ToString("yyyy-MM-dd"),
                "{date:MM/dd/yyyy}" => DateTime.Now.ToString("MM/dd/yyyy"),
                "{time:HH:mm:ss}" => DateTime.Now.ToString("HH:mm:ss"),
                "{datetime:yyyyMMdd_HHmmss}" => DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                "{guid}" => Guid.NewGuid().ToString(),
                "{username}" => Environment.UserName,
                "{machine}" => Environment.MachineName,
                _ => "(dynamic runtime token)"
            };

            _allItems.Add(new VariablePickerItem
            {
                Token = tok,
                Category = "System",
                Description = desc,
                LiveValue = sampleVal
            });
        }

        // 3. Curated Windows Environment Variables with friendly descriptions
        var curatedEnv = new (string Token, string Description)[]
        {
            ("%USERPROFILE%", "User home profile directory (C:\\Users\\<user>)"),
            ("%APPDATA%", "Roaming AppData directory (%APPDATA%)"),
            ("%LOCALAPPDATA%", "Local AppData directory (%LOCALAPPDATA%)"),
            ("%TEMP%", "Temporary files directory (%TEMP%)"),
            ("%TMP%", "Temporary files directory (%TMP%)"),
            ("%HOMEDRIVE%", "Host drive letter for user profile (e.g. C:)"),
            ("%HOMEPATH%", "Home path relative to drive (e.g. \\Users\\<user>)"),
            ("%PUBLIC%", "Public shared profile directory (C:\\Users\\Public)"),
            ("%USERNAME%", "Current logon username"),
            ("%USERDOMAIN%", "Domain or workgroup name"),
            ("%WINDIR%", "Windows installation directory (C:\\Windows)"),
            ("%SYSTEMROOT%", "Windows root directory (C:\\Windows)"),
            ("%SYSTEMDRIVE%", "Windows operating system drive (C:)"),
            ("%PROGRAMFILES%", "64-bit Program Files directory"),
            ("%PROGRAMFILES(X86)%", "32-bit Program Files directory"),
            ("%PROGRAMDATA%", "Shared Application Data directory"),
            ("%ALLUSERSPROFILE%", "All Users Profile directory"),
            ("%COMMONPROGRAMFILES%", "Common Files directory"),
            ("%COMSPEC%", "Executable path to Command Prompt (cmd.exe)"),
            ("%PATH%", "Search path for executable programs"),
            ("%COMPUTERNAME%", "Host computer network name"),
            ("%PROCESSOR_ARCHITECTURE%", "CPU architecture (AMD64, ARM64, x86)"),
            ("%NUMBER_OF_PROCESSORS%", "Logical CPU processor count"),
            ("%OS%", "Operating system identification")
        };

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (tok, desc) in curatedEnv)
        {
            string key = tok.Trim('%');
            seenKeys.Add(key);
            string live = Environment.GetEnvironmentVariable(key) ?? Environment.ExpandEnvironmentVariables(tok);
            _allItems.Add(new VariablePickerItem
            {
                Token = tok,
                Category = "Env",
                Description = desc,
                LiveValue = live
            });
        }

        // 4. Live Windows Environment Variables
        try
        {
            IDictionary liveDict = Environment.GetEnvironmentVariables();
            foreach (DictionaryEntry entry in liveDict)
            {
                string key = entry.Key?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key) || seenKeys.Contains(key)) continue;

                string val = entry.Value?.ToString() ?? string.Empty;
                _allItems.Add(new VariablePickerItem
                {
                    Token = $"%{key}%",
                    Category = "Env",
                    Description = $"Windows Environment Variable: {key}",
                    LiveValue = val
                });
                seenKeys.Add(key);
            }
        }
        catch
        {
            // Non-fatal if environment variables cannot be read
        }
    }

    private void ApplyFilter()
    {
        string search = SearchBox?.Text?.Trim() ?? string.Empty;

        var query = _allItems.AsEnumerable();

        if (!string.Equals(_activeCategory, "All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(item => string.Equals(item.Category, _activeCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(item =>
                item.Token.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Description.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.LiveValue.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var results = query.OrderBy(item => item.Category switch
        {
            "Workflow" => 0,
            "System" => 1,
            _ => 2
        }).ThenBy(item => item.Token).ToList();

        VariablesListBox.ItemsSource = results;

        if (results.Count > 0)
        {
            VariablesListBox.SelectedIndex = 0;
            VariablesListBox.ScrollIntoView(results[0]);
        }
        else
        {
            VariablesListBox.SelectedIndex = -1;
        }

        UpdateChipCounts();
    }

    private void UpdateChipCounts()
    {
        int allCount = _allItems.Count;
        int envCount = _allItems.Count(i => i.Category == "Env");
        int sysCount = _allItems.Count(i => i.Category == "System");
        int wfCount = _allItems.Count(i => i.Category == "Workflow");

        ChipAll.Content = $"All ({allCount})";
        ChipEnv.Content = $"Windows Env ({envCount})";
        ChipSystem.Content = $"System Tokens ({sysCount})";
        ChipWorkflow.Content = $"Workflow Vars ({wfCount})";
    }

    private void UpdateCategoryChipStyles()
    {
        var primaryStyle = TryFindResource("PrimaryButtonStyle") as Style;
        var secondaryStyle = TryFindResource("SecondaryButtonStyle") as Style;

        ChipAll.Style = _activeCategory == "All" ? primaryStyle : secondaryStyle;
        ChipEnv.Style = _activeCategory == "Env" ? primaryStyle : secondaryStyle;
        ChipSystem.Style = _activeCategory == "System" ? primaryStyle : secondaryStyle;
        ChipWorkflow.Style = _activeCategory == "Workflow" ? primaryStyle : secondaryStyle;
    }

    private void CategoryChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string category)
        {
            _activeCategory = category;
            UpdateCategoryChipStyles();
            ApplyFilter();
            SearchBox.Focus();
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            if (VariablesListBox.Items.Count > 0)
            {
                int next = Math.Min(VariablesListBox.Items.Count - 1, VariablesListBox.SelectedIndex + 1);
                VariablesListBox.SelectedIndex = next;
                VariablesListBox.ScrollIntoView(VariablesListBox.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (VariablesListBox.Items.Count > 0)
            {
                int prev = Math.Max(0, VariablesListBox.SelectedIndex - 1);
                VariablesListBox.SelectedIndex = prev;
                VariablesListBox.ScrollIntoView(VariablesListBox.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            CommitSelection();
            e.Handled = true;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !SearchBox.IsKeyboardFocused)
        {
            CommitSelection();
            e.Handled = true;
        }
    }

    private void VariablesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        InsertBtn.IsEnabled = VariablesListBox.SelectedItem is VariablePickerItem;
    }

    private void VariablesListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        CommitSelection();
    }

    private void CommitSelection()
    {
        if (VariablesListBox.SelectedItem is VariablePickerItem selected)
        {
            SelectedToken = selected.Token;
            DialogResult = true;
            Close();
        }
    }

    private void InsertBtn_Click(object sender, RoutedEventArgs e)
    {
        CommitSelection();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
