using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;

namespace TriggerPoint.UI.Controls;

public partial class SearchableServicePickerControl : UserControl
{
    private List<WindowsServiceItem> _allServices = new();
    private List<WindowsServiceItem> _filteredServices = new();
    private IWindowsServiceManager? _serviceManager;
    private bool _isInternalSelection;
    private long _lastClosedTimestamp;

    public static readonly DependencyProperty SelectedServiceNameProperty =
        DependencyProperty.Register(
            nameof(SelectedServiceName),
            typeof(string),
            typeof(SearchableServicePickerControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedServiceNameChanged));

    public string SelectedServiceName
    {
        get => (string)GetValue(SelectedServiceNameProperty);
        set => SetValue(SelectedServiceNameProperty, value);
    }

    public WindowsServiceItem? SelectedServiceItem { get; private set; }
    public string SelectedServiceDisplayName => SelectedServiceItem?.DisplayName ?? SelectedServiceName;

    public event EventHandler<WindowsServiceItem?>? SelectedServiceChanged;
    public event EventHandler<string?>? ServiceNameChanged;

    public SearchableServicePickerControl()
    {
        InitializeComponent();
    }

    public void InitializeServices(IEnumerable<WindowsServiceItem>? services, IWindowsServiceManager? serviceManager = null)
    {
        _serviceManager = serviceManager;
        _allServices = services?.OrderBy(s => s.DisplayName).ToList() ?? new();
        ApplyFilter(string.Empty);
        UpdateClosedCardView();
    }

    private static void OnSelectedServiceNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SearchableServicePickerControl picker && !picker._isInternalSelection)
        {
            picker.ResolveAndApplyService(e.NewValue as string);
        }
    }

    public void SetSelectedService(string? serviceName)
    {
        SelectedServiceName = serviceName ?? string.Empty;
        ResolveAndApplyService(serviceName);
    }

    private void ResolveAndApplyService(string? serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            SelectedServiceItem = null;
        }
        else
        {
            SelectedServiceItem = _allServices.FirstOrDefault(s =>
                string.Equals(s.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.DisplayName, serviceName, StringComparison.OrdinalIgnoreCase));

            if (SelectedServiceItem == null)
            {
                // Fallback virtual item for custom/unregistered service name
                SelectedServiceItem = new WindowsServiceItem
                {
                    ServiceName = serviceName,
                    DisplayName = serviceName,
                    Status = WindowsServiceStatus.Unknown
                };
            }
        }

        UpdateClosedCardView();
        SelectedServiceChanged?.Invoke(this, SelectedServiceItem);
        ServiceNameChanged?.Invoke(this, SelectedServiceName);
    }

    /// <summary>
    /// Forces a re-query of the currently selected service's status from the Windows Service Control Manager
    /// and refreshes the live status pill badge and icon.
    /// </summary>
    public void RefreshLiveStatus()
    {
        if (_serviceManager != null && !string.IsNullOrWhiteSpace(SelectedServiceName))
        {
            try
            {
                var liveStatus = _serviceManager.GetServiceStatus(SelectedServiceName);
                if (SelectedServiceItem != null)
                {
                    SelectedServiceItem.Status = liveStatus;
                }
            }
            catch { }
        }

        UpdateClosedCardView();
    }

    private void UpdateClosedCardView()
    {
        if (string.IsNullOrWhiteSpace(SelectedServiceName))
        {
            DisplayNameBlock.Text = "Select or search Windows service...";
            DisplayNameBlock.Foreground = Application.Current.TryFindResource("TextMutedBrush") as Brush ?? Brushes.Gray;
            ServiceNameBlock.Visibility = Visibility.Collapsed;
            StatusPillBorder.Visibility = Visibility.Collapsed;
            ClearButton.Visibility = Visibility.Collapsed;
            IconText.Text = "⚙️";
            return;
        }

        string displayName = SelectedServiceItem?.DisplayName ?? SelectedServiceName;
        DisplayNameBlock.Text = displayName;
        DisplayNameBlock.Foreground = Application.Current.TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White;

        ServiceNameBlock.Text = $"({SelectedServiceName})";
        ServiceNameBlock.Visibility = Visibility.Visible;
        ClearButton.Visibility = Visibility.Visible;

        // Status badge pill
        var status = SelectedServiceItem?.Status ?? WindowsServiceStatus.Unknown;
        if (_serviceManager != null && !string.IsNullOrWhiteSpace(SelectedServiceName))
        {
            try
            {
                var liveStatus = _serviceManager.GetServiceStatus(SelectedServiceName);
                if (liveStatus != WindowsServiceStatus.Unknown)
                {
                    status = liveStatus;
                }
            }
            catch { }
        }

        if (status != WindowsServiceStatus.Unknown)
        {
            StatusPillText.Text = status switch
            {
                WindowsServiceStatus.Running => "🟢 Running",
                WindowsServiceStatus.Stopped => "⚪ Stopped",
                WindowsServiceStatus.Paused => "🟡 Paused",
                WindowsServiceStatus.StartPending => "⏳ Starting...",
                WindowsServiceStatus.StopPending => "⏳ Stopping...",
                _ => $"🔵 {status}"
            };

            string brushKey = status switch
            {
                WindowsServiceStatus.Running => "SnippetBrush",
                WindowsServiceStatus.Stopped => "TextSecondaryBrush",
                WindowsServiceStatus.Paused => "ShellBrush",
                _ => "AccentBrush"
            };

            StatusPillText.Foreground = Application.Current.TryFindResource(brushKey) as Brush ?? Brushes.CornflowerBlue;
            StatusPillBorder.BorderBrush = StatusPillText.Foreground;
            StatusPillBorder.Visibility = Visibility.Visible;
            IconText.Text = status == WindowsServiceStatus.Running ? "🟢" : "⚙️";
        }
        else
        {
            StatusPillBorder.Visibility = Visibility.Collapsed;
            IconText.Text = "⚙️";
        }
    }

    private void PickerCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep && ClearButton.IsAncestorOf(dep))
        {
            return;
        }

        e.Handled = true;
    }

    private void PickerCard_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep && ClearButton.IsAncestorOf(dep))
        {
            return;
        }

        e.Handled = true;

        if (Environment.TickCount64 - _lastClosedTimestamp < 250)
        {
            return;
        }

        ServicePickerPopup.IsOpen = !ServicePickerPopup.IsOpen;
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _isInternalSelection = true;
        try
        {
            SelectedServiceName = string.Empty;
            SelectedServiceItem = null;
            UpdateClosedCardView();
            SelectedServiceChanged?.Invoke(this, null);
            ServiceNameChanged?.Invoke(this, string.Empty);
        }
        finally
        {
            _isInternalSelection = false;
        }
    }

    private void ServicePickerPopup_Opened(object sender, EventArgs e)
    {
        SearchBox.Text = string.Empty;
        ApplyFilter(string.Empty);

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        });

        if (SelectedServiceItem != null)
        {
            var match = _filteredServices.FirstOrDefault(s =>
                string.Equals(s.ServiceName, SelectedServiceItem.ServiceName, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                ServicesListBox.SelectedItem = match;
                ServicesListBox.ScrollIntoView(match);
            }
        }
    }

    private void ServicePickerPopup_Closed(object sender, EventArgs e)
    {
        _lastClosedTimestamp = Environment.TickCount64;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string query = SearchBox.Text.Trim();
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchBtn.Visibility = string.IsNullOrEmpty(query) ? Visibility.Collapsed : Visibility.Visible;
        ApplyFilter(query);
    }

    private void ClearSearchBtn_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        SearchBox.Focus();
    }

    private bool _isFiltering;

    private void ApplyFilter(string query)
    {
        _isFiltering = true;
        try
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                _filteredServices = new List<WindowsServiceItem>(_allServices);
                CustomServiceOptionBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                _filteredServices = _allServices.Where(s =>
                    s.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    s.ServiceName.Contains(query, StringComparison.OrdinalIgnoreCase)
                ).ToList();

                bool hasExact = _filteredServices.Any(s =>
                    string.Equals(s.ServiceName, query, StringComparison.OrdinalIgnoreCase));

                if (!hasExact && !string.IsNullOrWhiteSpace(query))
                {
                    CustomServiceOptionText.Text = $"Use manual service name: \"{query}\"";
                    CustomServiceOptionBorder.Visibility = Visibility.Visible;
                }
                else
                {
                    CustomServiceOptionBorder.Visibility = Visibility.Collapsed;
                }
            }

            ServicesListBox.ItemsSource = _filteredServices;
            CountSummaryText.Text = _allServices.Count > 0
                ? $"Showing {_filteredServices.Count} of {_allServices.Count} services"
                : "No services available";
        }
        finally
        {
            _isFiltering = false;
        }
    }

    private void ServicesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isFiltering && ServicePickerPopup.IsOpen && ServicesListBox.SelectedItem is WindowsServiceItem item)
        {
            SelectServiceItem(item);
        }
    }

    private void SelectServiceItem(WindowsServiceItem item)
    {
        _isInternalSelection = true;
        try
        {
            SelectedServiceName = item.ServiceName;
            SelectedServiceItem = item;
            UpdateClosedCardView();
            ServicePickerPopup.IsOpen = false;
            SelectedServiceChanged?.Invoke(this, item);
            ServiceNameChanged?.Invoke(this, item.ServiceName);
        }
        finally
        {
            _isInternalSelection = false;
        }
    }

    private void CustomServiceOption_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        string manual = SearchBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(manual))
        {
            SelectManualServiceName(manual);
        }
    }

    private void SelectManualServiceName(string serviceName)
    {
        _isInternalSelection = true;
        try
        {
            SelectedServiceName = serviceName;
            SelectedServiceItem = new WindowsServiceItem
            {
                ServiceName = serviceName,
                DisplayName = serviceName,
                Status = WindowsServiceStatus.Unknown
            };
            UpdateClosedCardView();
            ServicePickerPopup.IsOpen = false;
            SelectedServiceChanged?.Invoke(this, SelectedServiceItem);
            ServiceNameChanged?.Invoke(this, serviceName);
        }
        finally
        {
            _isInternalSelection = false;
        }
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            if (ServicesListBox.Items.Count > 0)
            {
                ServicesListBox.Focus();
                if (ServicesListBox.SelectedIndex < 0) ServicesListBox.SelectedIndex = 0;
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Enter)
        {
            if (ServicesListBox.SelectedItem is WindowsServiceItem item)
            {
                SelectServiceItem(item);
                e.Handled = true;
            }
            else if (!string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SelectManualServiceName(SearchBox.Text.Trim());
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Escape)
        {
            ServicePickerPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void ServicesListBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (ServicesListBox.SelectedItem is WindowsServiceItem item)
            {
                SelectServiceItem(item);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Escape)
        {
            ServicePickerPopup.IsOpen = false;
            e.Handled = true;
        }
    }
}
