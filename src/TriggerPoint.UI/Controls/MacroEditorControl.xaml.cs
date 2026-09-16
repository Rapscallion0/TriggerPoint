using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Services;
using TriggerPoint.Infrastructure.Win32;
using TriggerPoint.UI.Views;

namespace TriggerPoint.UI.Controls;

public partial class MacroEditorControl : UserControl
{
    private MacroPayload _macro = new();
    private IMacroService? _macroService;
    private bool _isUpdatingUi;

    public event EventHandler? MacroChanged;

    public MacroPayload CurrentMacro => _macro;

    public MacroEditorControl()
    {
        InitializeComponent();
    }

    public void Initialize(IMacroService? macroService, MacroPayload? macro = null)
    {
        _macroService = macroService ?? new Win32MacroService();
        _macro = macro ?? new MacroPayload();
        LoadMacroIntoUi();
    }

    public void SetMacro(MacroPayload? macro)
    {
        _macro = macro ?? new MacroPayload();
        LoadMacroIntoUi();
    }

    private void LoadMacroIntoUi()
    {
        _isUpdatingUi = true;
        try
        {
            RepeatsBox.Text = Math.Max(1, _macro.RepeatCount).ToString();

            if (_macro.PlaybackSpeed <= 0.6) SpeedCombo.SelectedIndex = 0;
            else if (_macro.PlaybackSpeed <= 1.2) SpeedCombo.SelectedIndex = 1;
            else if (_macro.PlaybackSpeed <= 1.7) SpeedCombo.SelectedIndex = 2;
            else if (_macro.PlaybackSpeed <= 3.0) SpeedCombo.SelectedIndex = 3;
            else SpeedCombo.SelectedIndex = 4;

            RebuildEventsList();
            EventsScrollViewer?.ScrollToTop();
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private void RebuildEventsList()
    {
        EventsHostPanel.Children.Clear();

        if (_macro.Events == null || _macro.Events.Count == 0)
        {
            EmptyStatePanel.Visibility = Visibility.Visible;
            EventsScrollViewer.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyStatePanel.Visibility = Visibility.Collapsed;
        EventsScrollViewer.Visibility = Visibility.Visible;

        for (int i = 0; i < _macro.Events.Count; i++)
        {
            var evt = _macro.Events[i];
            var card = CreateEventCard(evt, i);
            EventsHostPanel.Children.Add(card);
        }
    }

    private FrameworkElement CreateEventCard(MacroEvent evt, int index)
    {
        var border = new Border
        {
            Background = Application.Current.TryFindResource("CardBgBrush") as Brush ?? Brushes.DarkSlateGray,
            BorderBrush = Application.Current.TryFindResource("CardBorderBrush") as Brush ?? Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 6)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45, GridUnitType.Pixel) }); // Index
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85, GridUnitType.Pixel) }); // Badge
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });   // Inputs
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                         // Controls

        // Index
        var idxText = new TextBlock
        {
            Text = $"#{index + 1}",
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Foreground = Application.Current.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(idxText, 0);
        grid.Children.Add(idxText);

        // Badge
        var badgeBorder = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };

        var badgeText = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold
        };

        switch (evt.Type)
        {
            case MacroEventType.Delay:
                badgeBorder.Background = new SolidColorBrush(Color.FromArgb(0x28, 0x3B, 0x82, 0xF6));
                badgeText.Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA));
                badgeText.Text = "⏱ Delay";
                break;
            case MacroEventType.MouseDown:
            case MacroEventType.MouseUp:
                badgeBorder.Background = new SolidColorBrush(Color.FromArgb(0x28, 0x10, 0xB9, 0x81));
                badgeText.Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
                badgeText.Text = "🖱 Mouse";
                break;
            case MacroEventType.KeyDown:
            case MacroEventType.KeyUp:
                badgeBorder.Background = new SolidColorBrush(Color.FromArgb(0x28, 0xF5, 0x9E, 0x0B));
                badgeText.Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
                badgeText.Text = "⌨ Key";
                break;
            default:
                badgeText.Text = evt.Type.ToString();
                break;
        }

        badgeBorder.Child = badgeText;
        Grid.SetColumn(badgeBorder, 1);
        grid.Children.Add(badgeBorder);

        // Dynamic Inputs
        var inputsHost = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 6, 0)
        };

        if (evt.Type == MacroEventType.Delay)
        {
            var delayBox = new TextBox
            {
                Text = evt.DelayMs.ToString(),
                Width = 70,
                Height = 28,
                FontSize = 11.5,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 2, 6, 2)
            };
            delayBox.TextChanged += (s, e) =>
            {
                if (int.TryParse(delayBox.Text, out int ms))
                {
                    evt.DelayMs = Math.Max(1, ms);
                    NotifyChanged();
                }
            };

            inputsHost.Children.Add(new TextBlock { Text = "Pause for: ", FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Foreground = Application.Current.TryFindResource("TextSecondaryBrush") as Brush });
            inputsHost.Children.Add(delayBox);
            inputsHost.Children.Add(new TextBlock { Text = " ms", FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Foreground = Application.Current.TryFindResource("TextSecondaryBrush") as Brush });
        }
        else if (evt.Type is MacroEventType.KeyDown or MacroEventType.KeyUp)
        {
            var actionCombo = new ComboBox { Height = 28, Width = 95, FontSize = 11.5, Margin = new Thickness(0, 0, 8, 0) };
            actionCombo.Items.Add("Key Down");
            actionCombo.Items.Add("Key Up");
            actionCombo.SelectedIndex = evt.Type == MacroEventType.KeyDown ? 0 : 1;
            actionCombo.SelectionChanged += (s, e) =>
            {
                evt.Type = actionCombo.SelectedIndex == 0 ? MacroEventType.KeyDown : MacroEventType.KeyUp;
                NotifyChanged();
            };

            var keyBox = new TextBox
            {
                Text = evt.KeyName ?? $"VK_{evt.KeyCode}",
                Width = 110,
                Height = 28,
                FontSize = 11.5,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 2, 6, 2)
            };
            keyBox.TextChanged += (s, e) =>
            {
                evt.KeyName = keyBox.Text.Trim();
                if (Enum.TryParse<Key>(evt.KeyName, true, out var parsedKey))
                {
                    evt.KeyCode = KeyInterop.VirtualKeyFromKey(parsedKey);
                }
                NotifyChanged();
            };

            inputsHost.Children.Add(actionCombo);
            inputsHost.Children.Add(new TextBlock { Text = "Key: ", FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Foreground = Application.Current.TryFindResource("TextSecondaryBrush") as Brush });
            inputsHost.Children.Add(keyBox);
        }
        else if (evt.Type is MacroEventType.MouseDown or MacroEventType.MouseUp)
        {
            var actionCombo = new ComboBox { Height = 28, Width = 100, FontSize = 11.5, Margin = new Thickness(0, 0, 6, 0) };
            actionCombo.Items.Add("Mouse Down");
            actionCombo.Items.Add("Mouse Up");
            actionCombo.SelectedIndex = evt.Type == MacroEventType.MouseDown ? 0 : 1;
            actionCombo.SelectionChanged += (s, e) =>
            {
                evt.Type = actionCombo.SelectedIndex == 0 ? MacroEventType.MouseDown : MacroEventType.MouseUp;
                NotifyChanged();
            };

            var btnCombo = new ComboBox { Height = 28, Width = 80, FontSize = 11.5, Margin = new Thickness(0, 0, 8, 0) };
            btnCombo.Items.Add("Left");
            btnCombo.Items.Add("Right");
            btnCombo.Items.Add("Middle");
            btnCombo.SelectedIndex = (int)evt.MouseButton;
            btnCombo.SelectionChanged += (s, e) =>
            {
                evt.MouseButton = (MacroMouseButton)Math.Clamp(btnCombo.SelectedIndex, 0, 2);
                NotifyChanged();
            };

            var xBox = new TextBox { Text = evt.X.ToString(), Width = 55, Height = 28, FontSize = 11.5, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 2, 4, 2) };
            var yBox = new TextBox { Text = evt.Y.ToString(), Width = 55, Height = 28, FontSize = 11.5, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 2, 4, 2) };

            xBox.TextChanged += (s, e) =>
            {
                if (int.TryParse(xBox.Text, out int x))
                {
                    evt.X = x;
                    NotifyChanged();
                }
            };

            yBox.TextChanged += (s, e) =>
            {
                if (int.TryParse(yBox.Text, out int y))
                {
                    evt.Y = y;
                    NotifyChanged();
                }
            };

            var sampleBtn = new Button
            {
                Content = "🎯 Pick",
                Height = 28,
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(6, 0, 0, 0),
                Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
                ToolTip = "Set (X, Y) to current mouse cursor position"
            };
            sampleBtn.Click += (s, e) =>
            {
                if (NativeMethods.GetCursorPos(out var pt))
                {
                    xBox.Text = pt.X.ToString();
                    yBox.Text = pt.Y.ToString();
                }
            };

            inputsHost.Children.Add(actionCombo);
            inputsHost.Children.Add(btnCombo);
            inputsHost.Children.Add(new TextBlock { Text = "X:", FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0), Foreground = Application.Current.TryFindResource("TextSecondaryBrush") as Brush });
            inputsHost.Children.Add(xBox);
            inputsHost.Children.Add(new TextBlock { Text = " Y:", FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 3, 0), Foreground = Application.Current.TryFindResource("TextSecondaryBrush") as Brush });
            inputsHost.Children.Add(yBox);
            inputsHost.Children.Add(sampleBtn);
        }

        Grid.SetColumn(inputsHost, 2);
        grid.Children.Add(inputsHost);

        // Control Actions (Move Up, Move Down, Delete)
        var controlsStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var upBtn = new Button
        {
            Content = "▲",
            Width = 26,
            Height = 26,
            Margin = new Thickness(0, 0, 3, 0),
            Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
            IsEnabled = index > 0,
            ToolTip = "Move event up"
        };
        upBtn.Click += (s, e) =>
        {
            _macro.Events.RemoveAt(index);
            _macro.Events.Insert(index - 1, evt);
            RebuildEventsList();
            NotifyChanged();
        };

        var downBtn = new Button
        {
            Content = "▼",
            Width = 26,
            Height = 26,
            Margin = new Thickness(0, 0, 4, 0),
            Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
            IsEnabled = index < _macro.Events.Count - 1,
            ToolTip = "Move event down"
        };
        downBtn.Click += (s, e) =>
        {
            _macro.Events.RemoveAt(index);
            _macro.Events.Insert(index + 1, evt);
            RebuildEventsList();
            NotifyChanged();
        };

        var deleteBtn = new Button
        {
            Content = "✕",
            Width = 26,
            Height = 26,
            Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
            Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
            ToolTip = "Delete this event"
        };
        deleteBtn.Click += (s, e) =>
        {
            _macro.Events.RemoveAt(index);
            RebuildEventsList();
            NotifyChanged();
        };

        controlsStack.Children.Add(upBtn);
        controlsStack.Children.Add(downBtn);
        controlsStack.Children.Add(deleteBtn);

        Grid.SetColumn(controlsStack, 3);
        grid.Children.Add(controlsStack);

        border.Child = grid;
        return border;
    }

    private void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_macroService == null)
        {
            _macroService = new Win32MacroService();
        }

        var hud = new MacroRecordingHudWindow(_macroService)
        {
            Owner = Window.GetWindow(this)
        };

        if (hud.ShowDialog() == true && hud.RecordedMacro != null)
        {
            _macro.Events = [.. hud.RecordedMacro.Events];
            RebuildEventsList();
            NotifyChanged();
        }
    }

    private void AddEventButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();

        var keyItem = new MenuItem { Header = "⌨ Add Key Press", FontSize = 12 };
        keyItem.Click += (s, ev) =>
        {
            _macro.Events.Add(new MacroEvent { Type = MacroEventType.KeyDown, KeyCode = (int)Key.Enter, KeyName = "Enter" });
            _macro.Events.Add(new MacroEvent { Type = MacroEventType.KeyUp, KeyCode = (int)Key.Enter, KeyName = "Enter" });
            RebuildEventsList();
            NotifyChanged();
        };

        var mouseItem = new MenuItem { Header = "🖱 Add Mouse Click", FontSize = 12 };
        mouseItem.Click += (s, ev) =>
        {
            NativeMethods.GetCursorPos(out var pt);
            _macro.Events.Add(new MacroEvent { Type = MacroEventType.MouseDown, MouseButton = MacroMouseButton.Left, X = pt.X, Y = pt.Y });
            _macro.Events.Add(new MacroEvent { Type = MacroEventType.MouseUp, MouseButton = MacroMouseButton.Left, X = pt.X, Y = pt.Y });
            RebuildEventsList();
            NotifyChanged();
        };

        var delayItem = new MenuItem { Header = "⏱ Add Delay / Pause", FontSize = 12 };
        delayItem.Click += (s, ev) =>
        {
            _macro.Events.Add(new MacroEvent { Type = MacroEventType.Delay, DelayMs = 250 });
            RebuildEventsList();
            NotifyChanged();
        };

        menu.Items.Add(keyItem);
        menu.Items.Add(mouseItem);
        menu.Items.Add(delayItem);

        menu.PlacementTarget = AddEventBtn;
        menu.IsOpen = true;
    }

    private void SpeedCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi) return;
        _macro.PlaybackSpeed = SpeedCombo.SelectedIndex switch
        {
            0 => 0.5,
            1 => 1.0,
            2 => 1.5,
            3 => 2.0,
            4 => 5.0,
            _ => 1.0
        };
        NotifyChanged();
    }

    private void RepeatsBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingUi) return;
        if (int.TryParse(RepeatsBox.Text, out int r))
        {
            _macro.RepeatCount = Math.Max(1, r);
            NotifyChanged();
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (_macro.Events.Count == 0) return;
        _macro.Events.Clear();
        RebuildEventsList();
        NotifyChanged();
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_macro.Events.Count == 0) return;

        PlayBtn.IsEnabled = false;
        try
        {
            if (_macroService == null)
            {
                _macroService = new Win32MacroService();
            }

            // Brief initial delay so user can release the mouse from the button
            await System.Threading.Tasks.Task.Delay(300);
            await _macroService.PlayMacroAsync(_macro);
        }
        finally
        {
            PlayBtn.IsEnabled = true;
        }
    }

    private void NotifyChanged()
    {
        MacroChanged?.Invoke(this, EventArgs.Empty);
    }
}
