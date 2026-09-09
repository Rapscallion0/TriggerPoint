using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.UI.Views;

namespace TriggerPoint.UI.Services;

public class ToastNotificationService : IToastNotificationService
{
    private readonly IConfigRepository? _configRepository;
    private static readonly List<ToastNotificationWindow> _activeToasts = [];
    private static readonly object _lock = new();

    public ToastNotificationService(IConfigRepository? configRepository = null)
    {
        _configRepository = configRepository;
    }

    public void ShowSuccess(string title, string message)
    {
        ShowToast(ToastType.Success, title, message);
    }

    public void ShowError(string title, string message)
    {
        ShowToast(ToastType.Error, title, message);
    }

    public void ShowWarning(string title, string message)
    {
        ShowToast(ToastType.Warning, title, message);
    }

    private void ShowToast(ToastType type, string title, string message)
    {
        if (Application.Current == null) return;

        Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                var placement = ToastMonitorPlacement.PrimaryMonitor;
                if (_configRepository != null)
                {
                    try
                    {
                        var settings = await _configRepository.LoadSettingsAsync().ConfigureAwait(true);
                        placement = settings.ToastPlacement;
                    }
                    catch { }
                }

                double verticalOffset = 0;
                lock (_lock)
                {
                    // Clean up any closed toasts
                    _activeToasts.RemoveAll(t => !t.IsVisible && t.IsLoaded);

                    // If 3 or more toasts already active, close the oldest to prevent screen overflow
                    while (_activeToasts.Count >= 3)
                    {
                        var oldest = _activeToasts[0];
                        _activeToasts.RemoveAt(0);
                        try { oldest.Close(); } catch { }
                    }

                    // Compute vertical offset from visible stacked toasts
                    foreach (var active in _activeToasts)
                    {
                        double h = active.ActualHeight > 0 ? active.ActualHeight : 80;
                        verticalOffset += h + 8; // 8 DIP gap between stacked toasts
                    }
                }

                var toast = new ToastNotificationWindow(type, title, message, placement, verticalOffset);

                lock (_lock)
                {
                    _activeToasts.Add(toast);
                }

                toast.Closed += (s, e) =>
                {
                    lock (_lock)
                    {
                        _activeToasts.Remove(toast);
                    }
                };

                toast.Show();
            }
            catch { }
        });
    }
}
