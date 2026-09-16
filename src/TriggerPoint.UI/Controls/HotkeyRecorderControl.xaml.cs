using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TriggerPoint.Core.Models;
using ModifierKeys = TriggerPoint.Core.Models.ModifierKeys;

namespace TriggerPoint.UI.Controls;

public partial class HotkeyRecorderControl : UserControl
{
    public static readonly DependencyProperty BindingProperty =
        DependencyProperty.Register(
            nameof(Binding),
            typeof(ShortcutBinding),
            typeof(HotkeyRecorderControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBindingChanged));

    public ShortcutBinding? Binding
    {
        get => (ShortcutBinding?)GetValue(BindingProperty);
        set => SetValue(BindingProperty, value);
    }

    public event EventHandler<ShortcutBinding?>? BindingRecorded;
    public static event EventHandler? RecordingStarted;
    public static event EventHandler? RecordingStopped;

    private static readonly object _syncLock = new();
    private static readonly HashSet<HotkeyRecorderControl> _activeRecorders = [];

    private bool _isRecording;
    public bool IsRecording => _isRecording;

    public HotkeyRecorderControl()
    {
        InitializeComponent();
        UpdateUi();

        PreviewKeyDown += HotkeyRecorderControl_PreviewKeyDown;
        PreviewKeyUp += HotkeyRecorderControl_PreviewKeyUp;
        GotFocus += (s, e) => StartRecording();
        LostFocus += (s, e) => StopRecording(cancelled: true);
        Unloaded += (s, e) => StopRecording(cancelled: true);
    }

    private static void OnBindingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HotkeyRecorderControl control)
        {
            control.UpdateUi();
        }
    }

    private void UpdateUi()
    {
        if (_isRecording)
        {
            RecordingBorder.BorderBrush = (Brush)(Application.Current?.TryFindResource("AccentBrush") ?? Brushes.DodgerBlue);
            RecordingBorder.BorderThickness = new Thickness(1.5);
            ClearButton.Visibility = Visibility.Visible;
        }
        else
        {
            RecordingBorder.BorderBrush = (Brush)(Application.Current?.TryFindResource("BorderBrush") ?? Brushes.Gray);
            RecordingBorder.BorderThickness = new Thickness(1);

            if (Binding != null && !Binding.IsEmpty)
            {
                PromptText.Visibility = Visibility.Collapsed;
                HotkeyDisplayText.Text = Binding.DisplayText;
                KeyBadge.Visibility = Visibility.Visible;
                ClearButton.Visibility = Visibility.Visible;
            }
            else
            {
                PromptText.Text = "Click to record shortcut...";
                PromptText.Visibility = Visibility.Visible;
                KeyBadge.Visibility = Visibility.Collapsed;
                ClearButton.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void RecordingBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;
        Focus();
        StartRecording();
        e.Handled = true;
    }

    public void StartRecording()
    {
        if (_isRecording) return;
        _isRecording = true;
        lock (_syncLock)
        {
            _activeRecorders.Add(this);
            if (_activeRecorders.Count == 1)
            {
                RecordingStarted?.Invoke(this, EventArgs.Empty);
            }
        }
        PromptText.Text = "Recording... Press keys";
        PromptText.Visibility = Visibility.Visible;
        KeyBadge.Visibility = Visibility.Collapsed;
        UpdateUi();
    }

    public void StopRecording(bool cancelled)
    {
        if (!_isRecording) return;
        _isRecording = false;
        lock (_syncLock)
        {
            _activeRecorders.Remove(this);
            if (_activeRecorders.Count == 0)
            {
                RecordingStopped?.Invoke(this, EventArgs.Empty);
            }
        }
        UpdateUi();
    }

    private void HotkeyRecorderControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isRecording) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Escape cancels recording
        if (key == Key.Escape)
        {
            StopRecording(cancelled: true);
            e.Handled = true;
            return;
        }

        // Collect current modifiers
        var modifiers = GetCurrentModifiers();

        // If the pressed key is a modifier, show current progress (e.g. "Ctrl + ...")
        if (IsModifierKey(key))
        {
            ShowIncompleteModifierPreview(modifiers);
            e.Handled = true;
            return;
        }

        // User pressed a non-modifier trigger key!
        int vk = KeyInterop.VirtualKeyFromKey(key);
        string keyName = FormatKeyName(key);

        var newBinding = new ShortcutBinding(modifiers, vk, keyName);
        Binding = newBinding;
        BindingRecorded?.Invoke(this, newBinding);

        StopRecording(cancelled: false);
        e.Handled = true;
    }

    private void HotkeyRecorderControl_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!_isRecording) return;

        var modifiers = GetCurrentModifiers();
        if (modifiers == ModifierKeys.None)
        {
            // All modifiers were released without a trigger key
            PromptText.Text = "Press keys now...";
            PromptText.Visibility = Visibility.Visible;
            KeyBadge.Visibility = Visibility.Collapsed;
        }
        else
        {
            ShowIncompleteModifierPreview(modifiers);
        }

        e.Handled = true;
    }

    private void ShowIncompleteModifierPreview(ModifierKeys modifiers)
    {
        if (modifiers == ModifierKeys.None) return;

        var sb = new StringBuilder();
        if (modifiers.HasFlag(ModifierKeys.Control)) sb.Append("Ctrl + ");
        if (modifiers.HasFlag(ModifierKeys.Alt)) sb.Append("Alt + ");
        if (modifiers.HasFlag(ModifierKeys.Shift)) sb.Append("Shift + ");
        if (modifiers.HasFlag(ModifierKeys.Windows)) sb.Append("Win + ");
        sb.Append("...");

        PromptText.Visibility = Visibility.Collapsed;
        HotkeyDisplayText.Text = sb.ToString();
        KeyBadge.Visibility = Visibility.Visible;
    }

    private static ModifierKeys GetCurrentModifiers()
    {
        var modifiers = ModifierKeys.None;
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            modifiers |= ModifierKeys.Control;
        if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
            modifiers |= ModifierKeys.Alt;
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            modifiers |= ModifierKeys.Shift;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
            modifiers |= ModifierKeys.Windows;

        return modifiers;
    }

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl or
               Key.LeftAlt or Key.RightAlt or
               Key.LeftShift or Key.RightShift or
               Key.LWin or Key.RWin;
    }

    private static string FormatKeyName(Key key)
    {
        return key switch
        {
            Key.Space => "Space",
            Key.Return => "Enter",
            Key.Back => "Backspace",
            Key.Tab => "Tab",
            Key.Insert => "Insert",
            Key.Delete => "Delete",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Up => "Up",
            Key.Down => "Down",
            _ => key.ToString()
        };
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        Binding = null;
        BindingRecorded?.Invoke(this, null);
        StopRecording(cancelled: false);
        e.Handled = true;
    }
}
