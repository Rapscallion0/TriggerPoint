using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TriggerPoint.UI.Controls;

public partial class ColorPickerPopup : UserControl
{
    public event EventHandler<Color?>? ColorSelected;

    private static readonly List<Color> RecentColors = [];
    private bool _isHighlightMode;
    private bool _isUpdatingHexText;

    // 10 columns x 5 rows = 50 swatches
    private static readonly string[][] SwatchPalette =
    [
        // Grayscale
        ["#000000", "#333333", "#666666", "#AAAAAA", "#FFFFFF"],
        // Red
        ["#FFCDD2", "#E57373", "#E53935", "#C62828", "#B71C1C"],
        // Orange
        ["#FFE0B2", "#FFB74D", "#FB8C00", "#EF6C00", "#E65100"],
        // Yellow / Amber
        ["#FFF9C4", "#FFF176", "#FDD835", "#FBC02D", "#F57F17"],
        // Green
        ["#C8E6C9", "#81C784", "#43A047", "#2E7D32", "#1B5E20"],
        // Teal / Cyan
        ["#B2EBF2", "#4DD0E1", "#00ACC1", "#00838F", "#006064"],
        // Blue
        ["#BBDEFB", "#64B5F6", "#1E88E5", "#1565C0", "#0D47A1"],
        // Indigo
        ["#C5CAE9", "#7986CB", "#3949AB", "#283593", "#1A237E"],
        // Purple
        ["#E1BEE7", "#BA68C8", "#8E24AA", "#6A1B9A", "#4A148C"],
        // Pink / Magenta
        ["#F8BBD0", "#F06292", "#D81B60", "#AD1457", "#880E4F"]
    ];

    public ColorPickerPopup()
    {
        InitializeComponent();
        BuildSwatchGrid();
    }

    private void BuildSwatchGrid()
    {
        SwatchesGrid.Children.Clear();

        // 5 rows x 10 columns
        for (int row = 0; row < 5; row++)
        {
            for (int col = 0; col < 10; col++)
            {
                var hex = SwatchPalette[col][row];
                var color = (Color)ColorConverter.ConvertFromString(hex);
                var swatch = CreateSwatchButton(color, hex);
                SwatchesGrid.Children.Add(swatch);
            }
        }
    }

    private Button CreateSwatchButton(Color color, string tooltipText, double size = 18)
    {
        var btn = new Button
        {
            Width = size,
            Height = size,
            Margin = new Thickness(2),
            Cursor = Cursors.Hand,
            Padding = new Thickness(0),
            Focusable = false,
            ToolTip = tooltipText,
            Tag = color
        };

        var border = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(color),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(50, 128, 128, 128))
        };

        btn.Content = border;
        btn.Click += SwatchButton_Click;

        // Custom template to remove default button chrome
        var template = new ControlTemplate(typeof(Button));
        var elemFactory = new FrameworkElementFactory(typeof(ContentPresenter));
        template.VisualTree = elemFactory;
        btn.Template = template;

        return btn;
    }

    private void SwatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Color color)
        {
            SelectColorAndClose(color);
        }
    }

    public void Show(UIElement target, bool isHighlightMode, Color? currentColor = null)
    {
        _isHighlightMode = isHighlightMode;

        if (_isHighlightMode)
        {
            PopupTitleText.Text = "Highlight Color";
            ResetBtnText.Text = "No Color";
            ResetIndicatorBorder.Background = Brushes.Transparent;
            ResetIndicatorBorder.BorderBrush = Brushes.Crimson;
        }
        else
        {
            PopupTitleText.Text = "Text Color";
            ResetBtnText.Text = "Automatic";
            ResetIndicatorBorder.Background = Application.Current.TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White;
            ResetIndicatorBorder.BorderBrush = Application.Current.TryFindResource("BorderBrush") as Brush ?? Brushes.Gray;
        }

        UpdateRecentColorsUI();

        var initialColor = currentColor ?? (_isHighlightMode ? Colors.Yellow : Colors.Black);
        SetHexInput(initialColor);

        FlyoutPopup.PlacementTarget = target;
        FlyoutPopup.IsOpen = true;
    }

    private void SetHexInput(Color color)
    {
        _isUpdatingHexText = true;
        try
        {
            HexInputBox.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            HexPreviewSwatch.Background = new SolidColorBrush(color);
        }
        finally
        {
            _isUpdatingHexText = false;
        }
    }

    private void UpdateRecentColorsUI()
    {
        RecentColorsPanel.Children.Clear();
        if (RecentColors.Count == 0)
        {
            RecentColorsSection.Visibility = Visibility.Collapsed;
            return;
        }

        RecentColorsSection.Visibility = Visibility.Visible;
        foreach (var col in RecentColors)
        {
            var hex = $"#{col.R:X2}{col.G:X2}{col.B:X2}";
            var btn = CreateSwatchButton(col, hex, size: 16);
            RecentColorsPanel.Children.Add(btn);
        }
    }

    private void SelectColorAndClose(Color? color)
    {
        if (color.HasValue)
        {
            RecentColors.Remove(color.Value);
            RecentColors.Insert(0, color.Value);
            if (RecentColors.Count > 10)
            {
                RecentColors.RemoveAt(RecentColors.Count - 1);
            }
        }

        FlyoutPopup.IsOpen = false;
        ColorSelected?.Invoke(this, color);
    }

    private void ResetColorBtn_Click(object sender, RoutedEventArgs e)
    {
        SelectColorAndClose(null);
    }

    private void ApplyHexBtn_Click(object sender, RoutedEventArgs e)
    {
        ApplyCurrentHex();
    }

    private void HexInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            ApplyCurrentHex();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            FlyoutPopup.IsOpen = false;
        }
    }

    private void HexInputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingHexText) return;

        var text = HexInputBox.Text.Trim();
        if (!text.StartsWith('#'))
        {
            text = "#" + text;
        }

        if (TryParseHex(text, out var color))
        {
            HexPreviewSwatch.Background = new SolidColorBrush(color);
        }
    }

    private void ApplyCurrentHex()
    {
        var text = HexInputBox.Text.Trim();
        if (!text.StartsWith('#')) text = "#" + text;

        if (TryParseHex(text, out var color))
        {
            SelectColorAndClose(color);
        }
    }

    private static bool TryParseHex(string text, out Color color)
    {
        color = Colors.Transparent;
        if (!Regex.IsMatch(text, @"^#[0-9A-Fa-f]{6}$")) return false;

        try
        {
            color = (Color)ColorConverter.ConvertFromString(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void MoreColorsBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var colorDialog = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true,
                AnyColor = true
            };

            var text = HexInputBox.Text.Trim();
            if (TryParseHex(text, out var currentMediaColor))
            {
                colorDialog.Color = System.Drawing.Color.FromArgb(
                    currentMediaColor.A,
                    currentMediaColor.R,
                    currentMediaColor.G,
                    currentMediaColor.B);
            }

            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var chosen = Color.FromArgb(
                    colorDialog.Color.A,
                    colorDialog.Color.R,
                    colorDialog.Color.G,
                    colorDialog.Color.B);

                SetHexInput(chosen);
                SelectColorAndClose(chosen);
            }
        }
        catch
        {
            // Fallback gracefully if WinForms dialog cannot be displayed in headless runner
        }
    }
}
