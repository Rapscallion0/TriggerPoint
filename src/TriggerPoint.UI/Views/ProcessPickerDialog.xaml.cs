using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Win32;

namespace TriggerPoint.UI.Views;

public record RunningProcessItem(string ProcessName, string Title, string Description, string Icon, int Pid);

public partial class ProcessPickerDialog : Window
{
    private readonly List<RunningProcessItem> _allItems = [];
    public string? SelectedProcessName { get; private set; }

    public ProcessPickerDialog()
    {
        InitializeComponent();
        Loaded += (s, e) =>
        {
            RefreshProcesses();
            SearchBox.Focus();
        };
    }

    public static string? PickProcess(Window owner)
    {
        var dlg = new ProcessPickerDialog { Owner = owner };
        return dlg.ShowDialog() == true ? dlg.SelectedProcessName : null;
    }

    private void RefreshProcesses()
    {
        _allItems.Clear();
        int currentPid = Environment.ProcessId;
        var windowProcessMap = new Dictionary<string, RunningProcessItem>(StringComparer.OrdinalIgnoreCase);

        // 1. Enumerate visible top-level windows
        NativeMethods.EnumWindows((hWnd, lParam) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;

            int len = NativeMethods.GetWindowTextLength(hWnd);
            if (len <= 0) return true;

            var sb = new StringBuilder(len + 1);
            if (NativeMethods.GetWindowText(hWnd, sb, sb.Capacity) <= 0) return true;

            string title = sb.ToString().Trim();
            if (string.IsNullOrWhiteSpace(title) || title is "Default IME" or "MSCTFIME UI") return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0 || pid == currentPid) return true;

            string? exePath = QueryProcessPath(pid);
            string exeName = !string.IsNullOrWhiteSpace(exePath) ? Path.GetFileName(exePath) : string.Empty;

            if (string.IsNullOrWhiteSpace(exeName))
            {
                try
                {
                    using var p = Process.GetProcessById((int)pid);
                    exeName = p.ProcessName + ".exe";
                }
                catch
                {
                    return true;
                }
            }

            string icon = ContextFilter.IsKnownBrowser(exeName) ? "🌐" : "⚡";
            string desc = !string.IsNullOrWhiteSpace(exePath) ? exePath : $"Process ID: {pid}";

            if (!windowProcessMap.TryGetValue(exeName, out var existing) ||
                (string.IsNullOrWhiteSpace(existing.Title) && !string.IsNullOrWhiteSpace(title)))
            {
                windowProcessMap[exeName] = new RunningProcessItem(exeName, title, desc, icon, (int)pid);
            }

            return true;
        }, IntPtr.Zero);

        // 2. Also inspect running processes with main windows
        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                if (proc.Id == currentPid) continue;

                string exeName = proc.ProcessName + ".exe";
                if (!windowProcessMap.ContainsKey(exeName))
                {
                    string title = !string.IsNullOrWhiteSpace(proc.MainWindowTitle)
                        ? proc.MainWindowTitle
                        : proc.ProcessName;

                    string icon = ContextFilter.IsKnownBrowser(exeName) ? "🌐" : "⚡";
                    string desc = $"Running Task (PID {proc.Id})";

                    windowProcessMap[exeName] = new RunningProcessItem(exeName, title, desc, icon, proc.Id);
                }
            }
        }
        catch
        {
            // Best effort
        }

        _allItems.AddRange(windowProcessMap.Values.OrderBy(x => x.Title));
        ApplyFilter();
    }

    private string? QueryProcessPath(uint pid)
    {
        var hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProcess != IntPtr.Zero)
        {
            try
            {
                var sb = new StringBuilder(1024);
                uint size = (uint)sb.Capacity;
                if (NativeMethods.QueryFullProcessImageName(hProcess, 0, sb, ref size))
                {
                    return sb.ToString();
                }
            }
            finally
            {
                NativeMethods.CloseHandle(hProcess);
            }
        }
        return null;
    }

    private void ApplyFilter()
    {
        string query = SearchBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            ProcessListBox.ItemsSource = _allItems;
        }
        else
        {
            ProcessListBox.ItemsSource = _allItems
                .Where(x => x.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            x.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (ProcessListBox.Items.Count > 0)
        {
            ProcessListBox.SelectedIndex = 0;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void ProcessListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectBtn.IsEnabled = ProcessListBox.SelectedItem is RunningProcessItem;
    }

    private void ProcessListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProcessListBox.SelectedItem is RunningProcessItem item)
        {
            SelectedProcessName = item.ProcessName;
            DialogResult = true;
            Close();
        }
    }

    private void SelectBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ProcessListBox.SelectedItem is RunningProcessItem item)
        {
            SelectedProcessName = item.ProcessName;
            DialogResult = true;
            Close();
        }
    }

    private void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        RefreshProcesses();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
        else if (e.Key == Key.Enter && ProcessListBox.SelectedItem is RunningProcessItem item)
        {
            SelectedProcessName = item.ProcessName;
            DialogResult = true;
            Close();
        }
    }
}
