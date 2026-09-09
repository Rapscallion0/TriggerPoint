using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Win32;

namespace TriggerPoint.UI.Views;

public partial class InteractivePromptDialog : Window, IPromptDialogService
{
    private readonly Dictionary<string, Func<string>> _valueExtractors = [];
    public Dictionary<string, string>? Results { get; private set; }

    public InteractivePromptDialog()
    {
        InitializeComponent();
    }

    public InteractivePromptDialog(IReadOnlyList<PromptToken> tokens) : this()
    {
        BuildForm(tokens);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        CenterOnActiveScreen();
    }

    private void CenterOnActiveScreen()
    {
        if (!NativeMethods.GetCursorPos(out var pt)) return;

        var hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(hMonitor, ref monitorInfo)) return;

        double dpiScale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double workLeft = monitorInfo.rcWork.Left / dpiScale;
        double workTop = monitorInfo.rcWork.Top / dpiScale;
        double workWidth = (monitorInfo.rcWork.Right - monitorInfo.rcWork.Left) / dpiScale;
        double workHeight = (monitorInfo.rcWork.Bottom - monitorInfo.rcWork.Top) / dpiScale;

        double winWidth = ActualWidth > 0 ? ActualWidth : Width;
        double winHeight = ActualHeight > 0 ? ActualHeight : 280;

        Left = workLeft + Math.Max(0, (workWidth - winWidth) / 2.0);
        Top = workTop + Math.Max(0, (workHeight - winHeight) / 2.0);
    }

    private void BuildForm(IReadOnlyList<PromptToken> tokens)
    {
        FieldsContainer.Children.Clear();
        _valueExtractors.Clear();

        UIElement? firstInputControl = null;

        foreach (var token in tokens)
        {
            var fieldWrapper = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

            // Label
            var labelText = new TextBlock
            {
                Text = token.Label,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 5),
                Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextPrimaryBrush")
            };
            fieldWrapper.Children.Add(labelText);

            switch (token.Type)
            {
                case TokenType.PromptChoice:
                    var combo = new ComboBox
                    {
                        ItemsSource = token.Choices,
                        DisplayMemberPath = nameof(ChoiceOption.DisplayName),
                        SelectedIndex = 0,
                        Height = 32,
                        FontSize = 13
                    };
                    if (!string.IsNullOrEmpty(token.DefaultValue))
                    {
                        var matchIdx = token.Choices.FindIndex(c => 
                            string.Equals(c.Value, token.DefaultValue, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(c.DisplayName, token.DefaultValue, StringComparison.OrdinalIgnoreCase));
                        if (matchIdx >= 0)
                        {
                            combo.SelectedIndex = matchIdx;
                        }
                    }
                    _valueExtractors[token.RawTag] = () =>
                    {
                        if (combo.SelectedItem is ChoiceOption choice)
                        {
                            return choice.Value;
                        }
                        return combo.Text;
                    };
                    fieldWrapper.Children.Add(combo);
                    firstInputControl ??= combo;
                    break;

                case TokenType.PromptNumber:
                    var numBox = new TextBox
                    {
                        Style = (Style)Application.Current.FindResource("ModernTextBoxStyle"),
                        Height = 32
                    };
                    if (!string.IsNullOrEmpty(token.DefaultValue))
                    {
                        numBox.Text = token.DefaultValue;
                    }
                    else if (token.MinNumber.HasValue)
                    {
                        numBox.Text = token.MinNumber.Value.ToString();
                    }
                    numBox.PreviewTextInput += (s, e) =>
                    {
                        e.Handled = !double.TryParse(e.Text, out _) && e.Text != "-" && e.Text != ".";
                    };
                    _valueExtractors[token.RawTag] = () => numBox.Text.Trim();
                    fieldWrapper.Children.Add(numBox);
                    firstInputControl ??= numBox;
                    break;

                case TokenType.PromptMultiline:
                    var multiBox = new TextBox
                    {
                        Style = (Style)Application.Current.FindResource("ModernTextBoxStyle"),
                        AcceptsReturn = true,
                        TextWrapping = TextWrapping.Wrap,
                        Height = 80,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                    };
                    if (!string.IsNullOrEmpty(token.DefaultValue))
                    {
                        multiBox.Text = token.DefaultValue;
                        multiBox.SelectAll();
                    }
                    _valueExtractors[token.RawTag] = () => multiBox.Text;
                    fieldWrapper.Children.Add(multiBox);
                    firstInputControl ??= multiBox;
                    break;

                case TokenType.PromptDatePicker:
                    var fmt = string.IsNullOrWhiteSpace(token.DateFormat) ? "yyyy-MM-dd" : token.DateFormat;
                    DateTime initialDate = DateTime.Today;
                    if (!string.IsNullOrEmpty(token.DefaultValue) && DateTime.TryParse(token.DefaultValue, out var parsedDef))
                    {
                        initialDate = parsedDef;
                    }

                    var datePicker = new DatePicker
                    {
                        SelectedDate = initialDate,
                        Height = 32,
                        FontSize = 13
                    };
                    _valueExtractors[token.RawTag] = () =>
                    {
                        var d = datePicker.SelectedDate ?? DateTime.Today;
                        try
                        {
                            return d.ToString(fmt, System.Globalization.CultureInfo.CurrentCulture);
                        }
                        catch (FormatException)
                        {
                            return d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.CurrentCulture);
                        }
                    };
                    fieldWrapper.Children.Add(datePicker);
                    firstInputControl ??= datePicker;
                    break;

                case TokenType.PromptText:
                default:
                    var textBox = new TextBox
                    {
                        Style = (Style)Application.Current.FindResource("ModernTextBoxStyle"),
                        Height = 32
                    };
                    if (!string.IsNullOrEmpty(token.DefaultValue))
                    {
                        textBox.Text = token.DefaultValue;
                        textBox.SelectAll();
                    }
                    _valueExtractors[token.RawTag] = () => textBox.Text.Trim();
                    fieldWrapper.Children.Add(textBox);
                    firstInputControl ??= textBox;
                    break;
            }

            FieldsContainer.Children.Add(fieldWrapper);
        }

        Loaded += (s, e) =>
        {
            CenterOnActiveScreen();
            if (firstInputControl != null)
            {
                firstInputControl.Focus();
                Keyboard.Focus(firstInputControl);
                FocusManager.SetFocusedElement(this, firstInputControl);
            }
        };
    }

    private void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        Submit();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Results = null;
        DialogResult = false;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Results = null;
            DialogResult = false;
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            bool isCtrl = (Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control;
            if (isCtrl)
            {
                Submit();
                e.Handled = true;
                return;
            }

            // If a combobox popup is open, let Enter select the dropdown item without submitting the dialog
            if (Keyboard.FocusedElement is ComboBoxItem ||
                Keyboard.FocusedElement is ComboBox { IsDropDownOpen: true })
            {
                return;
            }

            // If focused on multiline textbox, let normal Enter insert a new line
            if (Keyboard.FocusedElement is TextBox tb && tb.AcceptsReturn)
            {
                return;
            }

            Submit();
            e.Handled = true;
        }
    }

    private void Submit()
    {
        var dict = new Dictionary<string, string>();
        foreach (var (tag, extractor) in _valueExtractors)
        {
            dict[tag] = extractor();
        }
        Results = dict;
        DialogResult = true;
        Close();
    }

    public async System.Threading.Tasks.Task<Dictionary<string, string>?> ShowPromptDialogAsync(IReadOnlyList<PromptToken> promptTokens)
    {
        return await Dispatcher.InvokeAsync(() =>
        {
            var dlg = new InteractivePromptDialog(promptTokens);
            var result = dlg.ShowDialog();
            return result == true ? dlg.Results : null;
        });
    }
}
