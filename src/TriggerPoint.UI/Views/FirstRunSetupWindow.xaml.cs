using System;
using System.Windows;
using System.Windows.Input;
using TriggerPoint.Core.Models;
using TriggerPoint.UI.Theme;

namespace TriggerPoint.UI.Views;

public partial class FirstRunSetupWindow : Window
{
    public ThemePreference SelectedTheme { get; private set; } = ThemePreference.System;
    public bool InstallStarterPack { get; private set; } = true;

    private bool _isInitializing = true;

    public FirstRunSetupWindow(ThemePreference initialTheme = ThemePreference.System, bool initialStarterPack = true)
    {
        InitializeComponent();

        SelectedTheme = initialTheme;
        InstallStarterPack = initialStarterPack;

        switch (initialTheme)
        {
            case ThemePreference.Dark:
                DarkThemeRadio.IsChecked = true;
                break;
            case ThemePreference.Light:
                LightThemeRadio.IsChecked = true;
                break;
            default:
                SystemThemeRadio.IsChecked = true;
                break;
        }

        StarterPackCheckBox.IsChecked = initialStarterPack;
        _isInitializing = false;
    }

    private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        if (DarkThemeRadio.IsChecked == true)
        {
            SelectedTheme = ThemePreference.Dark;
        }
        else if (LightThemeRadio.IsChecked == true)
        {
            SelectedTheme = ThemePreference.Light;
        }
        else
        {
            SelectedTheme = ThemePreference.System;
        }

        // Live preview of the selected theme
        ThemeManager.ApplyPreference(SelectedTheme);
    }

    private void GetStartedBtn_Click(object sender, RoutedEventArgs e)
    {
        InstallStarterPack = StarterPackCheckBox.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            GetStartedBtn_Click(this, new RoutedEventArgs());
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            InstallStarterPack = StarterPackCheckBox.IsChecked == true;
            DialogResult = true;
            Close();
        }
    }
}
