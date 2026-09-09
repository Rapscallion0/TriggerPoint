using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;

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
                    if (token.MinNumber.HasValue)
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
                    _valueExtractors[token.RawTag] = () => multiBox.Text;
                    fieldWrapper.Children.Add(multiBox);
                    firstInputControl ??= multiBox;
                    break;

                case TokenType.PromptDatePicker:
                    var datePicker = new DatePicker
                    {
                        SelectedDate = DateTime.Today,
                        Height = 32,
                        FontSize = 13
                    };
                    _valueExtractors[token.RawTag] = () => datePicker.SelectedDate?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd");
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
                    _valueExtractors[token.RawTag] = () => textBox.Text.Trim();
                    fieldWrapper.Children.Add(textBox);
                    firstInputControl ??= textBox;
                    break;
            }

            FieldsContainer.Children.Add(fieldWrapper);
        }

        Loaded += (s, e) => firstInputControl?.Focus();
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
        else if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
        {
            // If focused on multiline textbox, let Enter create a new line
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
            var dlg = new InteractivePromptDialog(promptTokens)
            {
                Owner = Application.Current.MainWindow
            };
            var result = dlg.ShowDialog();
            return result == true ? dlg.Results : null;
        });
    }
}
