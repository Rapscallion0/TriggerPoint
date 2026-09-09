using System;
using System.Windows;
using TriggerPoint.Core.Contracts;
using TriggerPoint.UI.Views;

namespace TriggerPoint.UI.Services;

public class ToastNotificationService : IToastNotificationService
{
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

    private static void ShowToast(ToastType type, string title, string message)
    {
        if (Application.Current == null) return;

        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var toast = new ToastNotificationWindow(type, title, message);
                toast.Show();
            }
            catch { }
        });
    }
}
