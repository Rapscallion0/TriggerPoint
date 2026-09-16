using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TriggerPoint.UI.Controls;

public enum TagVariant
{
    Default,
    Allowed,
    Excluded
}

public class PillViewModel : INotifyPropertyChanged
{
    private string _value;
    private string _editingValue;
    private bool _isEditing;

    public string Value
    {
        get => _value;
        set
        {
            if (_value != value)
            {
                _value = value;
                OnPropertyChanged(nameof(Value));
                OnPropertyChanged(nameof(Icon));
            }
        }
    }

    public string EditingValue
    {
        get => _editingValue;
        set
        {
            if (_editingValue != value)
            {
                _editingValue = value;
                OnPropertyChanged(nameof(EditingValue));
            }
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (_isEditing != value)
            {
                _isEditing = value;
                OnPropertyChanged(nameof(IsEditing));
                OnPropertyChanged(nameof(DisplayVisibility));
                OnPropertyChanged(nameof(EditVisibility));
            }
        }
    }

    public Visibility DisplayVisibility => IsEditing ? Visibility.Collapsed : Visibility.Visible;
    public Visibility EditVisibility => IsEditing ? Visibility.Visible : Visibility.Collapsed;

    public string Icon
    {
        get
        {
            if (Value.Contains("://") || Value.Contains('*') || Value.Contains('/') || Value.StartsWith("localhost", StringComparison.OrdinalIgnoreCase))
            {
                return "🌐";
            }
            return "⚡";
        }
    }

    public PillViewModel(string value)
    {
        _value = value;
        _editingValue = value;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}

public partial class TagInputControl : UserControl
{
    public ObservableCollection<PillViewModel> Tags { get; } = [];

    public event EventHandler? TagsChanged;

    public static readonly DependencyProperty VariantProperty =
        DependencyProperty.Register(
            nameof(Variant),
            typeof(TagVariant),
            typeof(TagInputControl),
            new PropertyMetadata(TagVariant.Default, (d, e) =>
            {
                if (d is TagInputControl control)
                {
                    control.UpdateVariantStyles();
                }
            }));

    public TagVariant Variant
    {
        get => (TagVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public static readonly DependencyProperty PillBackgroundProperty =
        DependencyProperty.Register(nameof(PillBackground), typeof(Brush), typeof(TagInputControl));

    public Brush PillBackground
    {
        get => (Brush)GetValue(PillBackgroundProperty);
        set => SetValue(PillBackgroundProperty, value);
    }

    public static readonly DependencyProperty PillBorderBrushProperty =
        DependencyProperty.Register(nameof(PillBorderBrush), typeof(Brush), typeof(TagInputControl));

    public Brush PillBorderBrush
    {
        get => (Brush)GetValue(PillBorderBrushProperty);
        set => SetValue(PillBorderBrushProperty, value);
    }

    public static readonly DependencyProperty PillForegroundProperty =
        DependencyProperty.Register(nameof(PillForeground), typeof(Brush), typeof(TagInputControl));

    public Brush PillForeground
    {
        get => (Brush)GetValue(PillForegroundProperty);
        set => SetValue(PillForegroundProperty, value);
    }

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(
            nameof(Placeholder),
            typeof(string),
            typeof(TagInputControl),
            new PropertyMetadata("Type and press Enter to add...", (d, e) =>
            {
                if (d is TagInputControl control)
                {
                    control.PlaceholderBlock.Text = (string)e.NewValue;
                }
            }));

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public TagInputControl()
    {
        InitializeComponent();
        UpdateVariantStyles();
        PillsItemsControl.ItemsSource = Tags;
        UpdatePlaceholderVisibility();
    }

    private void UpdateVariantStyles()
    {
        switch (Variant)
        {
            case TagVariant.Allowed:
                SetResourceReference(PillBackgroundProperty, "TagAllowedBgBrush");
                SetResourceReference(PillBorderBrushProperty, "TagAllowedBorderBrush");
                SetResourceReference(PillForegroundProperty, "TagAllowedTextBrush");
                break;
            case TagVariant.Excluded:
                SetResourceReference(PillBackgroundProperty, "TagExcludedBgBrush");
                SetResourceReference(PillBorderBrushProperty, "TagExcludedBorderBrush");
                SetResourceReference(PillForegroundProperty, "TagExcludedTextBrush");
                break;
            default:
                SetResourceReference(PillBackgroundProperty, "BgTertiaryBrush");
                SetResourceReference(PillBorderBrushProperty, "BorderBrush");
                SetResourceReference(PillForegroundProperty, "TextPrimaryBrush");
                break;
        }
    }

    private bool _isUpdatingTags;

    public void SetTags(IEnumerable<string>? items)
    {
        _isUpdatingTags = true;
        try
        {
            if (InlineInputBox != null)
            {
                InlineInputBox.Text = string.Empty;
            }
            Tags.Clear();
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (!string.IsNullOrWhiteSpace(item))
                    {
                        Tags.Add(new PillViewModel(item.Trim()));
                    }
                }
            }
            UpdatePlaceholderVisibility();
        }
        finally
        {
            _isUpdatingTags = false;
        }
    }

    public bool CommitPendingInput()
    {
        if (InlineInputBox != null && !string.IsNullOrWhiteSpace(InlineInputBox.Text))
        {
            var text = InlineInputBox.Text.Trim();
            InlineInputBox.Text = string.Empty;
            if (text.Contains(','))
            {
                var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                bool anyAdded = false;
                foreach (var part in parts)
                {
                    if (AddTag(part)) anyAdded = true;
                }
                return anyAdded;
            }
            return AddTag(text);
        }
        return false;
    }

    public List<string> GetTags()
    {
        return Tags.Select(x => x.Value).ToList();
    }

    public bool AddTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return false;

        string clean = tag.Trim();
        if (clean.EndsWith(',')) clean = clean[..^1].Trim();
        if (string.IsNullOrWhiteSpace(clean)) return false;

        if (Tags.Any(x => x.Value.Equals(clean, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        Tags.Add(new PillViewModel(clean));
        UpdatePlaceholderVisibility();
        TagsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool RemoveTag(string tag)
    {
        var existing = Tags.FirstOrDefault(x => x.Value.Equals(tag, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            Tags.Remove(existing);
            UpdatePlaceholderVisibility();
            TagsChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return false;
    }

    private void PillBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && (sender as FrameworkElement)?.DataContext is PillViewModel vm)
        {
            vm.EditingValue = vm.Value;
            vm.IsEditing = true;
            e.Handled = true;
        }
    }

    private void PillEditBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.Visibility == Visibility.Visible)
        {
            tb.Focus();
            tb.SelectAll();
        }
    }

    private void PillEditBox_KeyDown(object sender, KeyEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PillViewModel vm) return;

        if (e.Key == Key.Enter)
        {
            CommitPillEdit(vm);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelPillEdit(vm);
            e.Handled = true;
        }
    }

    private void PillEditBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PillViewModel vm && vm.IsEditing)
        {
            CommitPillEdit(vm);
        }
    }

    private void CommitPillEdit(PillViewModel vm)
    {
        string newText = vm.EditingValue?.Trim() ?? string.Empty;
        if (newText.EndsWith(',')) newText = newText[..^1].Trim();

        if (string.IsNullOrWhiteSpace(newText))
        {
            Tags.Remove(vm);
            UpdatePlaceholderVisibility();
            TagsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Check if new value duplicates another pill
        if (Tags.Any(x => x != vm && x.Value.Equals(newText, StringComparison.OrdinalIgnoreCase)))
        {
            Tags.Remove(vm); // remove duplicate
        }
        else
        {
            vm.Value = newText;
        }

        vm.IsEditing = false;
        TagsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CancelPillEdit(PillViewModel vm)
    {
        vm.EditingValue = vm.Value;
        vm.IsEditing = false;
    }

    private void InlineInputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && !string.IsNullOrWhiteSpace(InlineInputBox.Text))
        {
            CommitPendingInput();
            // Do not set e.Handled = true so focus moves to next control
        }
    }

    private void InlineInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.OemComma)
        {
            CommitPendingInput();
            e.Handled = true;
        }
        else if (e.Key == Key.Back && string.IsNullOrEmpty(InlineInputBox.Text) && Tags.Count > 0)
        {
            Tags.RemoveAt(Tags.Count - 1);
            UpdatePlaceholderVisibility();
            TagsChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void InlineInputBox_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitPendingInput();
    }

    private void InlineInputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePlaceholderVisibility();
        if (_isUpdatingTags) return;

        // If user pasted comma-delimited items
        var text = InlineInputBox.Text;
        if (text.Contains(','))
        {
            var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                AddTag(part);
            }
            InlineInputBox.Text = string.Empty;
        }

        TagsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RemovePillBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PillViewModel vm })
        {
            Tags.Remove(vm);
            UpdatePlaceholderVisibility();
            TagsChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (sender is Button { Tag: string val })
        {
            RemoveTag(val);
        }
    }

    private void ContainerBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        InlineInputBox.Focus();
    }

    private void UpdatePlaceholderVisibility()
    {
        if (PlaceholderBlock != null)
        {
            PlaceholderBlock.Visibility = (string.IsNullOrEmpty(InlineInputBox.Text) && Tags.Count == 0)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }
}
