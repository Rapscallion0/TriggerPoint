using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Win32;

namespace TriggerPoint.UI.Views;

public partial class InteractivePromptDialog : Window, IPromptDialogService
{
    private readonly Dictionary<string, Func<string>> _valueExtractors = [];
    private readonly List<Func<string?>> _validators = [];
    public Dictionary<string, string>? Results { get; private set; }
    private UIElement? _firstInputControl;
    private bool _hasInitialFocusBeenSet;

    public UIElement? FirstInputControl => _firstInputControl;

    public InteractivePromptDialog()
    {
        InitializeComponent();
    }

    public InteractivePromptDialog(IReadOnlyList<PromptToken> tokens, string? title = null, string? subtitle = null) : this()
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            PromptTitleText.Text = title;
        }
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            PromptSubtitleText.Text = subtitle;
        }
        BuildForm(tokens);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        CenterOnActiveScreen();
        ForceForeground();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        ForceForeground();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(FocusFirstInput));
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (!_hasInitialFocusBeenSet)
        {
            _hasInitialFocusBeenSet = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(FocusFirstInput));
        }
    }

    private void ForceForeground()
    {
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);

            uint currentThreadId = NativeMethods.GetCurrentThreadId();
            IntPtr foregroundHwnd = NativeMethods.GetForegroundWindow();
            uint foregroundThreadId = foregroundHwnd != IntPtr.Zero
                ? NativeMethods.GetWindowThreadProcessId(foregroundHwnd, out _)
                : 0;

            bool attached = false;
            if (currentThreadId != foregroundThreadId && foregroundThreadId != 0)
            {
                attached = NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, true);
            }

            try
            {
                NativeMethods.BringWindowToTop(handle);
                NativeMethods.SetForegroundWindow(handle);
            }
            finally
            {
                if (attached)
                {
                    NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, false);
                }
            }

            Activate();
            Focus();
        }
        catch { }
    }

    private void FocusFirstInput()
    {
        if (_firstInputControl == null) return;

        try
        {
            Activate();
            Focus();

            if (_firstInputControl is DatePicker dp)
            {
                dp.ApplyTemplate();
                if (dp.Template?.FindName("PART_TextBox", dp) is UIElement dpTextBox)
                {
                    dpTextBox.Focus();
                    Keyboard.Focus(dpTextBox);
                    FocusManager.SetFocusedElement(this, dpTextBox);
                    return;
                }
            }
            else if (_firstInputControl is TextBox tb)
            {
                tb.Focus();
                Keyboard.Focus(tb);
                FocusManager.SetFocusedElement(this, tb);
                tb.SelectAll();
                return;
            }

            _firstInputControl.Focus();
            Keyboard.Focus(_firstInputControl);
            FocusManager.SetFocusedElement(this, _firstInputControl);
        }
        catch { }
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
        _validators.Clear();

        _firstInputControl = null;
        _hasInitialFocusBeenSet = false;

        foreach (var token in tokens)
        {
            var fieldWrapper = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

            // Label
            var labelText = new TextBlock
            {
                Text = token.Label,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 5)
            };
            labelText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
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
                    _firstInputControl ??= combo;
                    break;

                case TokenType.PromptNumber:
                    if (token.MinNumber.HasValue && token.MaxNumber.HasValue)
                    {
                        labelText.Text = $"{token.Label} (Range: {token.MinNumber.Value} – {token.MaxNumber.Value})";
                    }
                    else if (token.MinNumber.HasValue)
                    {
                        labelText.Text = $"{token.Label} (Min: {token.MinNumber.Value})";
                    }
                    else if (token.MaxNumber.HasValue)
                    {
                        labelText.Text = $"{token.Label} (Max: {token.MaxNumber.Value})";
                    }

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
                        numBox.Text = token.MinNumber.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    numBox.PreviewTextInput += (s, e) =>
                    {
                        e.Handled = !double.TryParse(e.Text, out _) && e.Text != "-" && e.Text != ".";
                    };
                    _valueExtractors[token.RawTag] = () => numBox.Text.Trim();

                    _validators.Add(() =>
                    {
                        var valText = numBox.Text.Trim();
                        if (!string.IsNullOrEmpty(valText))
                        {
                            if (double.TryParse(valText, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var n) ||
                                double.TryParse(valText, out n))
                            {
                                if (token.MinNumber.HasValue && n < token.MinNumber.Value)
                                {
                                    numBox.Focus();
                                    numBox.SelectAll();
                                    return $"'{token.Label}' must be at least {token.MinNumber.Value}.";
                                }
                                if (token.MaxNumber.HasValue && n > token.MaxNumber.Value)
                                {
                                    numBox.Focus();
                                    numBox.SelectAll();
                                    return $"'{token.Label}' cannot exceed {token.MaxNumber.Value}.";
                                }
                            }
                            else
                            {
                                numBox.Focus();
                                numBox.SelectAll();
                                return $"'{token.Label}' must be a valid number.";
                            }
                        }
                        return null;
                    });

                    fieldWrapper.Children.Add(numBox);
                    _firstInputControl ??= numBox;
                    break;

                case TokenType.PromptMultiline:
                    var multiBox = new TextBox
                    {
                        Style = (Style)Application.Current.FindResource("ModernTextBoxStyle"),
                        AcceptsReturn = true,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalContentAlignment = VerticalAlignment.Top,
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
                    _firstInputControl ??= multiBox;
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
                        Style = (Style)Application.Current.FindResource("ModernDatePickerStyle"),
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
                    _firstInputControl ??= datePicker;
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
                    _firstInputControl ??= textBox;
                    break;
            }

            FieldsContainer.Children.Add(fieldWrapper);
        }

        Loaded += (s, e) =>
        {
            CenterOnActiveScreen();
            ForceForeground();
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(FocusFirstInput));
        };
    }

    private void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        Submit();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Results = null;
        try { DialogResult = false; } catch (InvalidOperationException) { }
        Close();
    }

    internal void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Results = null;
            try { DialogResult = false; } catch (InvalidOperationException) { }
            Close();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            bool isCtrl = (Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control;
            if (isCtrl)
            {
                CommitFocusedControl();
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

            // If DatePicker calendar popup is open, let Enter select the date
            if (Keyboard.FocusedElement is DependencyObject d)
            {
                var dp = FindVisualAncestor<DatePicker>(d);
                if (dp != null && dp.IsDropDownOpen)
                {
                    return;
                }
            }

            // If focused on multiline textbox, let normal Enter insert a new line
            if (Keyboard.FocusedElement is TextBox tb && tb.AcceptsReturn)
            {
                return;
            }

            CommitFocusedControl();
            Submit();
            e.Handled = true;
        }
    }

    private void CommitFocusedControl()
    {
        if (Keyboard.FocusedElement is DependencyObject d)
        {
            var dp = FindVisualAncestor<DatePicker>(d);
            if (dp != null)
            {
                dp.GetBindingExpression(DatePicker.SelectedDateProperty)?.UpdateSource();
            }
        }
    }

    private static T? FindVisualAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T typed) return typed;
            if (current is Visual or System.Windows.Media.Media3D.Visual3D)
            {
                current = VisualTreeHelper.GetParent(current);
            }
            else if (current is FrameworkContentElement fce)
            {
                current = fce.Parent;
            }
            else
            {
                break;
            }
        }
        return null;
    }

    private void Submit()
    {
        foreach (var validator in _validators)
        {
            var err = validator();
            if (err != null)
            {
                MessageBox.Show(this, err, "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        var dict = new Dictionary<string, string>();
        foreach (var (tag, extractor) in _valueExtractors)
        {
            dict[tag] = extractor();
        }
        Results = dict;
        try { DialogResult = true; } catch (InvalidOperationException) { }
        Close();
    }

    public async System.Threading.Tasks.Task<Dictionary<string, string>?> ShowPromptDialogAsync(
        IReadOnlyList<PromptToken> promptTokens,
        string? title = null,
        string? subtitle = null)
    {
        return await Dispatcher.InvokeAsync(() =>
        {
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
            var dlg = new InteractivePromptDialog(promptTokens, title, subtitle);
            var result = dlg.ShowDialog();
            return result == true ? dlg.Results : null;
        });
    }
}
