using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;

namespace TriggerPoint.UI.Views;

public partial class TokenConfigurationDialog : Window
{
    private string _initialToken = string.Empty;
    private string _tokenType = "date";
    private bool _isInitializing;

    public string ResultToken { get; private set; } = string.Empty;

    public TokenConfigurationDialog(string token)
    {
        InitializeComponent();
        _initialToken = token ?? string.Empty;
        ResultToken = _initialToken;
        InitializeFromToken(_initialToken);
    }

    private void InitializeFromToken(string token)
    {
        _isInitializing = true;
        try
        {
            var inner = token.Trim().TrimStart('{').TrimEnd('}').Trim();
            var colonIdx = inner.IndexOf(':');
            var prefix = colonIdx > 0 ? inner[..colonIdx].Trim().ToLowerInvariant() : inner.ToLowerInvariant();
            var arg = colonIdx > 0 ? inner[(colonIdx + 1)..].Trim() : string.Empty;

            // Consolidate date to unified datetime token
            if (prefix == "date") prefix = "datetime";

            _tokenType = prefix;

            switch (prefix)
            {
                case "datetime":
                case "tomorrow":
                case "yesterday":
                case "time":
                    DialogIconText.Text = "🗓️";
                    DialogTitleText.Text = "Configure Date & Time Format";
                    DateFormatPanel.Visibility = Visibility.Visible;
                    DateFormatBox.Text = string.IsNullOrEmpty(arg) ? "yyyy-MM-dd HH:mm" : arg;
                    SetupDatePresets();
                    UpdateDateLivePreview();
                    break;

                case "text":
                case "multiline":
                    DialogIconText.Text = prefix == "multiline" ? "📝" : "💬";
                    DialogTitleText.Text = prefix == "multiline" ? "Configure Multi-Line Text Input" : "Configure Text Input";
                    TextPromptPanel.Visibility = Visibility.Visible;
                    var textParts = arg.Split('|', 2);
                    TextPromptLabelBox.Text = textParts.Length > 0 ? textParts[0] : "Label";
                    TextPromptDefaultBox.Text = textParts.Length > 1 ? textParts[1] : string.Empty;
                    break;

                case "number":
                    DialogIconText.Text = "🔢";
                    DialogTitleText.Text = "Configure Number Input";
                    NumberPromptPanel.Visibility = Visibility.Visible;
                    var numParts = arg.Split('|', 3);
                    NumberPromptLabelBox.Text = numParts.Length > 0 ? numParts[0] : "Quantity";
                    if (numParts.Length > 1 && numParts[1].Contains(','))
                    {
                        var range = numParts[1].Split(',');
                        NumberMinBox.Text = range[0];
                        if (range.Length > 1) NumberMaxBox.Text = range[1];
                    }
                    NumberDefaultBox.Text = numParts.Length > 2 ? numParts[2] : (numParts.Length > 1 && !numParts[1].Contains(',') ? numParts[1] : string.Empty);
                    break;

                case "choice":
                    DialogIconText.Text = "🔽";
                    DialogTitleText.Text = "Configure Choice Dropdown";
                    ChoicePromptPanel.Visibility = Visibility.Visible;
                    var choiceParts = arg.Split('|', 2);
                    ChoicePromptLabelBox.Text = choiceParts.Length > 0 ? choiceParts[0] : "Option";
                    ChoiceOptionsBox.Text = choiceParts.Length > 1 ? choiceParts[1] : "Option 1, Option 2*, Option 3";
                    break;

                case "date_picker":
                    DialogIconText.Text = "📆";
                    DialogTitleText.Text = "Configure Date Picker Prompt";
                    DatePickerPromptPanel.Visibility = Visibility.Visible;
                    var dpParts = arg.Split('|', 3);
                    DatePickerLabelBox.Text = dpParts.Length > 0 ? dpParts[0] : "Due Date";
                    DatePickerFormatBox.Text = dpParts.Length > 1 ? dpParts[1] : "yyyy-MM-dd";
                    DatePickerOffsetBox.Text = dpParts.Length > 2 ? dpParts[2] : string.Empty;
                    break;

                default:
                    DialogIconText.Text = "⚙️";
                    DialogTitleText.Text = "Configure Token";
                    GenericTokenPanel.Visibility = Visibility.Visible;
                    GenericTokenBox.Text = inner;
                    break;
            }

            UpdateGeneratedToken();
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private void SetupDatePresets()
    {
        DatePresetsWrapPanel.Children.Clear();
        var presets = new (string Label, string Format)[]
        {
            ("Standard", "yyyy-MM-dd HH:mm"),
            ("US Date", "MM/dd/yyyy"),
            ("EU Date", "dd/MM/yyyy"),
            ("Full Date", "dddd, MMMM d, yyyy"),
            ("ISO 8601", "yyyy-MM-ddTHH:mm:ss"),
            ("Compact Timestamp", "yyyyMMdd_HHmmss"),
            ("Time Only", "HH:mm:ss"),
            ("Tomorrow", "+1d"),
            ("Next Week", "+7d")
        };

        foreach (var (lbl, fmt) in presets)
        {
            var btn = new Button
            {
                Content = lbl,
                Style = Application.Current.TryFindResource("TokenAssistantPillStyle") as Style,
                Margin = new Thickness(0, 0, 4, 4),
                ToolTip = fmt
            };
            btn.Click += (s, e) =>
            {
                DateFormatBox.Text = fmt;
            };
            DatePresetsWrapPanel.Children.Add(btn);
        }
    }

    private void DateFormatBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing) return;
        UpdateDateLivePreview();
        UpdateGeneratedToken();
    }

    private void AnyField_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing) return;
        UpdateGeneratedToken();
    }

    private void UpdateDateLivePreview()
    {
        string fmt = DateFormatBox.Text.Trim();
        try
        {
            DateTime now = DateTime.Now;
            if (fmt.StartsWith("+") || fmt.StartsWith("-"))
            {
                var colon = fmt.IndexOf(':');
                if (colon > 0)
                {
                    var offset = fmt[..colon];
                    var subFmt = fmt[(colon + 1)..];
                    now = ApplyRelativeOffset(now, offset);
                    DateLivePreviewText.Text = now.ToString(subFmt, CultureInfo.CurrentCulture);
                    return;
                }
                else
                {
                    now = ApplyRelativeOffset(now, fmt);
                    DateLivePreviewText.Text = now.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture);
                    return;
                }
            }

            DateLivePreviewText.Text = now.ToString(fmt, CultureInfo.CurrentCulture);
        }
        catch
        {
            DateLivePreviewText.Text = "(Invalid format)";
        }
    }

    private static DateTime ApplyRelativeOffset(DateTime target, string offset)
    {
        if (offset.StartsWith("+") || offset.StartsWith("-"))
        {
            bool isAdd = offset.StartsWith("+");
            string numStr = offset[1..^1];
            char unit = char.ToLowerInvariant(offset[^1]);

            if (int.TryParse(numStr, out int val))
            {
                int multiplier = isAdd ? 1 : -1;
                return unit switch
                {
                    'd' => target.AddDays(val * multiplier),
                    'w' => target.AddDays(val * 7 * multiplier),
                    'm' => target.AddMonths(val * multiplier),
                    'y' => target.AddYears(val * multiplier),
                    'h' => target.AddHours(val * multiplier),
                    _ => target
                };
            }
        }
        return target;
    }

    private void UpdateGeneratedToken()
    {
        string token;
        switch (_tokenType)
        {
            case "datetime":
            case "date":
            case "tomorrow":
            case "yesterday":
            case "time":
                string fmt = DateFormatBox.Text.Trim();
                token = string.IsNullOrEmpty(fmt) ? $"{{{_tokenType}}}" : $"{{{_tokenType}:{fmt}}}";
                break;

            case "text":
            case "multiline":
                string label = TextPromptLabelBox.Text.Trim();
                if (string.IsNullOrEmpty(label)) label = "Label";
                string def = TextPromptDefaultBox.Text.Trim();
                token = string.IsNullOrEmpty(def) ? $"{{{_tokenType}:{label}}}" : $"{{{_tokenType}:{label}|{def}}}";
                break;

            case "number":
                string numLabel = NumberPromptLabelBox.Text.Trim();
                if (string.IsNullOrEmpty(numLabel)) numLabel = "Quantity";
                string min = NumberMinBox.Text.Trim();
                string max = NumberMaxBox.Text.Trim();
                string numDef = NumberDefaultBox.Text.Trim();

                string rangePart = string.Empty;
                if (!string.IsNullOrEmpty(min) || !string.IsNullOrEmpty(max))
                {
                    rangePart = $"{min},{max}".Trim(',');
                }

                if (!string.IsNullOrEmpty(rangePart) && !string.IsNullOrEmpty(numDef))
                {
                    token = $"{{number:{numLabel}|{rangePart}|{numDef}}}";
                }
                else if (!string.IsNullOrEmpty(rangePart))
                {
                    token = $"{{number:{numLabel}|{rangePart}}}";
                }
                else if (!string.IsNullOrEmpty(numDef))
                {
                    token = $"{{number:{numLabel}|{numDef}}}";
                }
                else
                {
                    token = $"{{number:{numLabel}}}";
                }
                break;

            case "choice":
                string choiceLabel = ChoicePromptLabelBox.Text.Trim();
                if (string.IsNullOrEmpty(choiceLabel)) choiceLabel = "Option";
                string choices = ChoiceOptionsBox.Text.Trim();
                token = string.IsNullOrEmpty(choices) ? $"{{choice:{choiceLabel}}}" : $"{{choice:{choiceLabel}|{choices}}}";
                break;

            case "date_picker":
                string dpLabel = DatePickerLabelBox.Text.Trim();
                if (string.IsNullOrEmpty(dpLabel)) dpLabel = "Due Date";
                string dpFmt = DatePickerFormatBox.Text.Trim();
                string dpOffset = DatePickerOffsetBox.Text.Trim();

                if (!string.IsNullOrEmpty(dpOffset))
                {
                    token = $"{{date_picker:{dpLabel}|{(string.IsNullOrEmpty(dpFmt) ? "yyyy-MM-dd" : dpFmt)}|{dpOffset}}}";
                }
                else if (!string.IsNullOrEmpty(dpFmt))
                {
                    token = $"{{date_picker:{dpLabel}|{dpFmt}}}";
                }
                else
                {
                    token = $"{{date_picker:{dpLabel}}}";
                }
                break;

            default:
                string generic = GenericTokenBox.Text.Trim();
                token = generic.StartsWith('{') && generic.EndsWith('}') ? generic : $"{{{generic}}}";
                break;
        }

        ResultToken = token;
        ResultingTokenBadge.Text = token;
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        UpdateGeneratedToken();
        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SaveBtn_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            CancelBtn_Click(sender, e);
        }
    }
}
