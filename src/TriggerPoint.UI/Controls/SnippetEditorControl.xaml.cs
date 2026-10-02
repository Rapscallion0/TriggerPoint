using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;
using TriggerPoint.Infrastructure.Services;
using TriggerPoint.UI.Views;

namespace TriggerPoint.UI.Controls;

public partial class SnippetEditorControl : UserControl
{
    private SnippetContentType _currentContentType = SnippetContentType.PlainText;
    private int _lastCaretIndex = -1;
    private int _lastSelectionLength = 0;
    private bool _isUpdatingUi;
    private bool _isUpdatingRichText;
    private bool _isPaperCanvasActive = true;
    private bool _isColorPickerForHighlight;
    private DispatcherTimer? _previewDebounceTimer;
    private DispatcherTimer? _warningTimer;
    private List<string> _availableVariables = [];

    public event EventHandler? SnippetChanged;

    public SnippetContentType ContentType => _currentContentType;

    public string PlainTextTemplate
    {
        get => SnippetTemplateBox?.Text ?? string.Empty;
        set
        {
            if (SnippetTemplateBox != null)
            {
                SnippetTemplateBox.Text = value ?? string.Empty;
            }
        }
    }

    public string RtfContent
    {
        get
        {
            if (SnippetRichTextBox == null) return string.Empty;
            return RichTextService.SaveToRtf(SnippetRichTextBox.Document);
        }
        set
        {
            if (SnippetRichTextBox != null)
            {
                RichTextService.LoadFromRtf(SnippetRichTextBox.Document, value ?? string.Empty);
            }
        }
    }

    public IReadOnlyList<string> AvailableVariables => _availableVariables;

    public void InsertToken(string token) => InsertTokenIntoEditor(token);

    public SnippetEditorControl()
    {
        InitializeComponent();

        _previewDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _previewDebounceTimer.Tick += (s, e) =>
        {
            _previewDebounceTimer.Stop();
            _ = UpdateLivePreviewAsync();
        };

        SetFormatMode(SnippetContentType.PlainText, notify: false);
    }

    public void Initialize(
        SnippetContentType contentType, 
        string template, 
        string rtf, 
        IEnumerable<string>? availableVariables = null)
    {
        _isUpdatingUi = true;
        try
        {
            SetAvailableVariables(availableVariables);

            _currentContentType = contentType;
            SnippetTemplateBox.Text = template ?? string.Empty;
            SnippetTemplateBox.ScrollToHome();

            if (contentType == SnippetContentType.RichText)
            {
                if (!string.IsNullOrEmpty(rtf))
                {
                    RichTextService.LoadFromRtf(SnippetRichTextBox.Document, rtf);
                }
                else if (!string.IsNullOrEmpty(template))
                {
                    RichTextService.LoadFromPlainText(SnippetRichTextBox.Document, template);
                }
                else
                {
                    SnippetRichTextBox.Document.Blocks.Clear();
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(rtf))
                {
                    RichTextService.LoadFromRtf(SnippetRichTextBox.Document, rtf);
                }
                else
                {
                    SnippetRichTextBox.Document.Blocks.Clear();
                }
            }

            SetFormatMode(contentType, notify: false);
            UpdateContextualTokenAssistant();
            QueuePreviewUpdate();
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    public void SetAvailableVariables(IEnumerable<string>? variables)
    {
        _availableVariables = variables?.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];

        WorkflowVariablesChipsPanel.Children.Clear();
        if (_availableVariables.Count > 0)
        {
            WorkflowVariablesContainer.Visibility = Visibility.Visible;
            foreach (var variable in _availableVariables)
            {
                var cleanVar = variable.Trim().TrimStart('{').TrimEnd('}');
                var btn = new Button
                {
                    Content = $"+ {{{cleanVar}}}",
                    Style = Application.Current.TryFindResource("TokenChipPromptStyle") as Style,
                    Margin = new Thickness(0, 0, 5, 4),
                    ToolTip = $"Insert {{{cleanVar}}} into snippet",
                    Tag = $"{{{cleanVar}}}"
                };
                btn.Click += (s, e) =>
                {
                    InsertTokenIntoEditor($"{{{cleanVar}}}");
                };
                WorkflowVariablesChipsPanel.Children.Add(btn);
            }
        }
        else
        {
            WorkflowVariablesContainer.Visibility = Visibility.Collapsed;
        }
    }

    public void FocusEditor()
    {
        if (_currentContentType == SnippetContentType.RichText)
        {
            SnippetRichTextBox?.Focus();
        }
        else
        {
            SnippetTemplateBox?.Focus();
        }
    }

    public (string Template, string Rtf) GetSnippetPayload()
    {
        if (_currentContentType == SnippetContentType.RichText)
        {
            string plain = RichTextService.ExtractPlainText(SnippetRichTextBox.Document);
            string rtf = RichTextService.SaveToRtf(SnippetRichTextBox.Document);
            return (plain, rtf);
        }
        else
        {
            return (SnippetTemplateBox.Text, string.Empty);
        }
    }

    private void OnContentChanged()
    {
        if (_isUpdatingUi) return;
        QueuePreviewUpdate();
        SnippetChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetFormatMode(SnippetContentType format, bool notify = true)
    {
        _currentContentType = format;

        var accentBrush = Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;
        var textSecBrush = Application.Current.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;

        if (format == SnippetContentType.RichText)
        {
            SnippetFormatRichSegment.Background = accentBrush;
            SnippetFormatRichSegment.Foreground = Brushes.White;
            SnippetFormatPlainSegment.Background = Brushes.Transparent;
            SnippetFormatPlainSegment.Foreground = textSecBrush;

            SnippetPlainTextContainer.Visibility = Visibility.Collapsed;
            SnippetRichTextContainer.Visibility = Visibility.Visible;
            SnippetLivePreviewText.Visibility = Visibility.Collapsed;
            SnippetLivePreviewRichBox.Visibility = Visibility.Visible;

            _isPaperCanvasActive = true;
            ApplySnippetCanvasMode();
        }
        else
        {
            SnippetFormatPlainSegment.Background = accentBrush;
            SnippetFormatPlainSegment.Foreground = Brushes.White;
            SnippetFormatRichSegment.Background = Brushes.Transparent;
            SnippetFormatRichSegment.Foreground = textSecBrush;

            SnippetPlainTextContainer.Visibility = Visibility.Visible;
            SnippetRichTextContainer.Visibility = Visibility.Collapsed;
            SnippetLivePreviewText.Visibility = Visibility.Visible;
            SnippetLivePreviewRichBox.Visibility = Visibility.Collapsed;

            if (SnippetLivePreviewContainerBorder != null)
            {
                SnippetLivePreviewContainerBorder.Background = Application.Current.TryFindResource("BgInputBrush") as Brush ?? Brushes.Transparent;
                SnippetLivePreviewContainerBorder.BorderBrush = Application.Current.TryFindResource("BorderBrush") as Brush ?? Brushes.Gray;
            }
        }

        if (notify)
        {
            OnContentChanged();
        }
    }

    private void SnippetFormatPlainSegment_Click(object sender, RoutedEventArgs e)
    {
        if (_currentContentType == SnippetContentType.PlainText) return;

        // When switching from Rich to Plain, extract plain text from RichTextBox
        var plainText = RichTextService.ExtractPlainText(SnippetRichTextBox.Document);
        if (!string.IsNullOrEmpty(plainText) && string.IsNullOrEmpty(SnippetTemplateBox.Text))
        {
            SnippetTemplateBox.Text = plainText;
        }

        SetFormatMode(SnippetContentType.PlainText);
        SnippetTemplateBox.Focus();
    }

    private void SnippetFormatRichSegment_Click(object sender, RoutedEventArgs e)
    {
        if (_currentContentType == SnippetContentType.RichText) return;

        // When switching from Plain to Rich, load plain text into RichTextBox if currently empty
        var richPlain = RichTextService.ExtractPlainText(SnippetRichTextBox.Document);
        if (string.IsNullOrEmpty(richPlain) && !string.IsNullOrEmpty(SnippetTemplateBox.Text))
        {
            RichTextService.LoadFromPlainText(SnippetRichTextBox.Document, SnippetTemplateBox.Text);
        }

        SetFormatMode(SnippetContentType.RichText);
        SnippetRichTextBox.Focus();
    }

    #region Token Chips & Presets

    private void InsertTokenChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string token)
        {
            InsertTokenIntoEditor(token);
        }
    }

    private void InsertTokenPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is string token)
        {
            InsertTokenIntoEditor(token);
        }
    }

    private void TokenDropdownArrow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            DependencyObject current = fe;
            while (current != null && current is not Border)
            {
                current = VisualTreeHelper.GetParent(current);
            }

            if (current is Border border && border.ContextMenu != null)
            {
                border.ContextMenu.PlacementTarget = border;
                border.ContextMenu.Placement = PlacementMode.Bottom;
                border.ContextMenu.IsOpen = true;
            }
        }
    }

    public void InsertTokenIntoEditor(string token)
    {
        if (string.IsNullOrEmpty(token)) return;

        // Enforce single {cursor} rule
        if (token.Equals("{cursor}", StringComparison.OrdinalIgnoreCase))
        {
            string existing = _currentContentType == SnippetContentType.RichText && SnippetRichTextBox != null
                ? RichTextService.ExtractPlainText(SnippetRichTextBox.Document)
                : (SnippetTemplateBox?.Text ?? string.Empty);

            if (existing.Contains("{cursor}", StringComparison.OrdinalIgnoreCase) || existing.Contains("{{cursor}}", StringComparison.OrdinalIgnoreCase))
            {
                ShowSnippetWarning("Only one {cursor} token is allowed per snippet. Existing {cursor} has been highlighted.");
                HighlightExistingCursorToken();
                return;
            }
        }

        // Disambiguate prompt token labels
        string currentContent = _currentContentType == SnippetContentType.RichText && SnippetRichTextBox != null
            ? RichTextService.ExtractPlainText(SnippetRichTextBox.Document)
            : (SnippetTemplateBox?.Text ?? string.Empty);

        token = PlaceholderParser.GetDisambiguatedPromptToken(token, currentContent);

        if (_currentContentType == SnippetContentType.RichText && SnippetRichTextBox != null)
        {
            SnippetRichTextBox.Selection.Text = token;
            SnippetRichTextBox.CaretPosition = SnippetRichTextBox.Selection.End;
            SnippetRichTextBox.Focus();
            OnContentChanged();
            return;
        }

        if (SnippetTemplateBox == null) return;

        string current = SnippetTemplateBox.Text ?? string.Empty;
        int insertPos;

        if (_lastCaretIndex >= 0 && _lastCaretIndex <= current.Length)
        {
            insertPos = _lastCaretIndex;
            int selLen = Math.Max(0, Math.Min(_lastSelectionLength, current.Length - insertPos));
            if (selLen > 0)
            {
                current = current.Remove(insertPos, selLen);
            }
        }
        else
        {
            insertPos = current.Length;
        }

        SnippetTemplateBox.Text = current.Insert(insertPos, token);
        SnippetTemplateBox.Focus();
        SnippetTemplateBox.CaretIndex = insertPos + token.Length;
        SnippetTemplateBox.SelectionLength = 0;
        _lastCaretIndex = insertPos + token.Length;
        _lastSelectionLength = 0;

        ShowFloatingTokenPillOverlay(SnippetFloatingOverlayCanvas, SnippetTemplateBox, token, insertPos);
        UpdateContextualTokenAssistant();
        OnContentChanged();
    }

    private void HighlightExistingCursorToken()
    {
        if (_currentContentType == SnippetContentType.RichText && SnippetRichTextBox != null)
        {
            var range = FindTextInRange(SnippetRichTextBox.Document.ContentStart, SnippetRichTextBox.Document.ContentEnd, "{cursor}")
                     ?? FindTextInRange(SnippetRichTextBox.Document.ContentStart, SnippetRichTextBox.Document.ContentEnd, "{{cursor}}");
            if (range != null)
            {
                SnippetRichTextBox.Focus();
                SnippetRichTextBox.Selection.Select(range.Start, range.End);
            }
        }
        else if (SnippetTemplateBox != null)
        {
            string text = SnippetTemplateBox.Text ?? string.Empty;
            int idx = text.IndexOf("{cursor}", StringComparison.OrdinalIgnoreCase);
            int len = 8;
            if (idx < 0)
            {
                idx = text.IndexOf("{{cursor}}", StringComparison.OrdinalIgnoreCase);
                len = 10;
            }

            if (idx >= 0)
            {
                SnippetTemplateBox.Focus();
                SnippetTemplateBox.Select(idx, len);
                _lastCaretIndex = idx;
                _lastSelectionLength = len;
            }
        }
    }

    private static TextRange? FindTextInRange(TextPointer start, TextPointer end, string text)
    {
        while (start != null && start.CompareTo(end) < 0)
        {
            if (start.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                string textRun = start.GetTextInRun(LogicalDirection.Forward);
                int matchIndex = textRun.IndexOf(text, StringComparison.OrdinalIgnoreCase);
                if (matchIndex >= 0)
                {
                    TextPointer matchStart = start.GetPositionAtOffset(matchIndex);
                    TextPointer matchEnd = matchStart.GetPositionAtOffset(text.Length);
                    return new TextRange(matchStart, matchEnd);
                }
            }
            start = start.GetNextContextPosition(LogicalDirection.Forward);
        }
        return null;
    }

    public void ShowSnippetWarning(string message)
    {
        SnippetInlineWarningText.Text = message;
        SnippetInlineWarningBanner.Visibility = Visibility.Visible;
        _warningTimer?.Stop();
        _warningTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _warningTimer.Tick += (s, e) =>
        {
            _warningTimer?.Stop();
            SnippetInlineWarningBanner.Visibility = Visibility.Collapsed;
        };
        _warningTimer.Start();
    }

    private void DismissSnippetWarningBtn_Click(object sender, RoutedEventArgs e)
    {
        _warningTimer?.Stop();
        SnippetInlineWarningBanner.Visibility = Visibility.Collapsed;
    }

    public static void ShowFloatingTokenPillOverlay(Canvas? canvas, TextBox textBox, string token, int insertPos)
    {
        if (canvas == null || textBox == null) return;

        try
        {
            int charIndex = Math.Max(0, Math.Min(insertPos, textBox.Text.Length - 1));
            var charRect = textBox.Text.Length > 0 ? textBox.GetRectFromCharacterIndex(charIndex, true) : new Rect(8, 8, 10, 16);
            double left = charRect.Left;
            double top = charRect.Top - 24;

            if (double.IsInfinity(left) || double.IsNaN(left) || left < 0) left = 12;
            if (double.IsInfinity(top) || double.IsNaN(top) || top < 0) top = 6;

            if (canvas.ActualWidth > 80 && left > canvas.ActualWidth - 75)
            {
                left = canvas.ActualWidth - 75;
            }

            var pill = new Border
            {
                Background = Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue,
                BorderBrush = Application.Current.TryFindResource("AccentHoverBrush") as Brush ?? Brushes.DeepSkyBlue,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2, 8, 2),
                IsHitTestVisible = false,
                Opacity = 1.0,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = (Application.Current.TryFindResource("AccentColorBrush") as SolidColorBrush)?.Color ?? Colors.DodgerBlue,
                    BlurRadius = 14,
                    ShadowDepth = 2,
                    Opacity = 0.85
                }
            };

            var textBlock = new TextBlock
            {
                Text = $"✨ {token}",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            pill.Child = textBlock;

            var transformGroup = new TransformGroup();
            var scaleTransform = new ScaleTransform(1.0, 1.0);
            var translateTransform = new TranslateTransform(0, 0);
            transformGroup.Children.Add(scaleTransform);
            transformGroup.Children.Add(translateTransform);
            pill.RenderTransform = transformGroup;
            pill.RenderTransformOrigin = new Point(0.5, 0.5);

            Canvas.SetLeft(pill, left);
            Canvas.SetTop(pill, top);
            canvas.Children.Add(pill);

            var storyboard = new Storyboard();

            var scaleXAnim = new DoubleAnimation(0.5, 1.0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } };
            var scaleYAnim = new DoubleAnimation(0.5, 1.0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } };
            var transYAnim = new DoubleAnimation(0, -18, TimeSpan.FromMilliseconds(900)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            var fadeAnim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(400)) { BeginTime = TimeSpan.FromMilliseconds(500) };

            Storyboard.SetTarget(scaleXAnim, pill);
            Storyboard.SetTargetProperty(scaleXAnim, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
            Storyboard.SetTarget(scaleYAnim, pill);
            Storyboard.SetTargetProperty(scaleYAnim, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"));
            Storyboard.SetTarget(transYAnim, pill);
            Storyboard.SetTargetProperty(transYAnim, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.Y)"));
            Storyboard.SetTarget(fadeAnim, pill);
            Storyboard.SetTargetProperty(fadeAnim, new PropertyPath(UIElement.OpacityProperty));

            storyboard.Children.Add(scaleXAnim);
            storyboard.Children.Add(scaleYAnim);
            storyboard.Children.Add(transYAnim);
            storyboard.Children.Add(fadeAnim);

            storyboard.Completed += (s, e) =>
            {
                canvas.Children.Remove(pill);
            };
            storyboard.Begin();
        }
        catch
        {
            // Floating overlay failure must not disrupt typing
        }
    }

    #endregion

    #region Contextual Token Assistant

    private void SnippetTemplateBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        _lastCaretIndex = SnippetTemplateBox.CaretIndex;
        _lastSelectionLength = SnippetTemplateBox.SelectionLength;
        UpdateContextualTokenAssistant();
    }

    private void SnippetTemplateBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingUi) return;
        UpdateContextualTokenAssistant();
        OnContentChanged();
    }

    private void UpdateContextualTokenAssistant()
    {
        if (TokenAssistantBorder == null || AssistantOptionsPillsPanel == null) return;

        var tokenInfo = GetTokenAtCaret();
        if (tokenInfo == null)
        {
            AssistantBadgeBorder.Visibility = Visibility.Collapsed;
            AssistantDescriptionText.Text = "Type '{' or move caret inside a token to see parameters and quick format options.";
            AssistantOptionsPillsPanel.Children.Clear();
            AssistantOptionsPillsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var (start, end, fullToken, prefix, arg) = tokenInfo.Value;
        AssistantOptionsPillsPanel.Children.Clear();

        switch (prefix)
        {
            case "date":
                AssistantBadgeBorder.Visibility = Visibility.Visible;
                AssistantTokenBadge.Text = "{date:format}";
                AssistantDescriptionText.Text = "Current date. Options: custom format (.NET specifiers) and relative offsets (+/- days, weeks, months).";
                AddAssistantPill("MM/dd/yyyy (US)", "MM/dd/yyyy", start, end, prefix);
                AddAssistantPill("dd/MM/yyyy (EU)", "dd/MM/yyyy", start, end, prefix);
                AddAssistantPill("dddd, MMMM d, yyyy (Full)", "dddd, MMMM d, yyyy", start, end, prefix);
                AddAssistantPill("yyyyMMdd (Compact)", "yyyyMMdd", start, end, prefix);
                AddAssistantPill("+1d (Tomorrow)", "+1d", start, end, prefix);
                AddAssistantPill("-1d (Yesterday)", "-1d", start, end, prefix);
                AddAssistantPill("+7d (Next Week)", "+7d", start, end, prefix);
                AddAssistantPill("+1m (Next Month)", "+1m", start, end, prefix);
                AddAssistantPill("+1d:MM/dd/yyyy", "+1d:MM/dd/yyyy", start, end, prefix);
                break;

            case "tomorrow":
                AssistantBadgeBorder.Visibility = Visibility.Visible;
                AssistantTokenBadge.Text = "{tomorrow:format}";
                AssistantDescriptionText.Text = "Tomorrow's date with optional format specifier.";
                AddAssistantPill("MM/dd/yyyy", "MM/dd/yyyy", start, end, prefix);
                AddAssistantPill("dddd, MMMM d", "dddd, MMMM d", start, end, prefix);
                AddAssistantPill("yyyyMMdd", "yyyyMMdd", start, end, prefix);
                break;

            case "yesterday":
                AssistantBadgeBorder.Visibility = Visibility.Visible;
                AssistantTokenBadge.Text = "{yesterday:format}";
                AssistantDescriptionText.Text = "Yesterday's date with optional format specifier.";
                AddAssistantPill("MM/dd/yyyy", "MM/dd/yyyy", start, end, prefix);
                AddAssistantPill("dddd, MMMM d", "dddd, MMMM d", start, end, prefix);
                AddAssistantPill("yyyyMMdd", "yyyyMMdd", start, end, prefix);
                break;

            case "time":
                AssistantBadgeBorder.Visibility = Visibility.Visible;
                AssistantTokenBadge.Text = "{time:format}";
                AssistantDescriptionText.Text = "Current time. Options: 12/24-hour specifiers and relative offsets (+/- hours, minutes).";
                AddAssistantPill("HH:mm:ss (24-Hour)", "HH:mm:ss", start, end, prefix);
                AddAssistantPill("hh:mm tt (12-Hour AM/PM)", "hh:mm tt", start, end, prefix);
                AddAssistantPill("HH:mm (Short 24-Hour)", "HH:mm", start, end, prefix);
                AddAssistantPill("+1h (One Hour Later)", "+1h:HH:mm", start, end, prefix);
                AddAssistantPill("-30m (30 Mins Ago)", "-30m:HH:mm", start, end, prefix);
                break;

            case "datetime":
                AssistantBadgeBorder.Visibility = Visibility.Visible;
                AssistantTokenBadge.Text = "{datetime:format}";
                AssistantDescriptionText.Text = "Current date and time combined.";
                AddAssistantPill("yyyy-MM-ddTHH:mm:ss (ISO)", "yyyy-MM-ddTHH:mm:ss", start, end, prefix);
                AddAssistantPill("MM/dd/yyyy hh:mm tt", "MM/dd/yyyy hh:mm tt", start, end, prefix);
                AddAssistantPill("yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm", start, end, prefix);
                break;

            case "guid":
            case "uuid":
                AssistantBadgeBorder.Visibility = Visibility.Visible;
                AssistantTokenBadge.Text = "{guid:modifier}";
                AssistantDescriptionText.Text = "Generates a unique identifier with optional casing and format.";
                AddAssistantPill("upper (Uppercase)", "upper", start, end, prefix);
                AddAssistantPill("N (32 Digits No Hyphens)", "N", start, end, prefix);
                AddAssistantPill("B (Braced)", "B", start, end, prefix);
                AddAssistantPill("P (Parentheses)", "P", start, end, prefix);
                AddAssistantPill("N:upper (Compact Uppercase)", "N:upper", start, end, prefix);
                break;

            case "clipboard":
                AssistantBadgeBorder.Visibility = Visibility.Visible;
                AssistantTokenBadge.Text = "{clipboard:modifier}";
                AssistantDescriptionText.Text = "Inserts current clipboard text with optional transformation modifier.";
                AddAssistantPill("trim (Strip Whitespace)", "trim", start, end, prefix);
                AddAssistantPill("upper (UPPERCASE)", "upper", start, end, prefix);
                AddAssistantPill("lower (lowercase)", "lower", start, end, prefix);
                AddAssistantPill("urlencode (URL-Safe)", "urlencode", start, end, prefix);
                AddAssistantPill("urldecode (Decoded)", "urldecode", start, end, prefix);
                break;

            case "env":
                AssistantBadgeBorder.Visibility = Visibility.Visible;
                AssistantTokenBadge.Text = "{env:VARIABLE_NAME}";
                AssistantDescriptionText.Text = "Resolves a Windows environment variable.";
                AddAssistantPill("USERPROFILE", "USERPROFILE", start, end, prefix);
                AddAssistantPill("TEMP", "TEMP", start, end, prefix);
                AddAssistantPill("APPDATA", "APPDATA", start, end, prefix);
                AddAssistantPill("COMPUTERNAME", "COMPUTERNAME", start, end, prefix);
                AddAssistantPill("PATH", "PATH", start, end, prefix);
                break;

            case "cursor":
                AssistantBadgeBorder.Visibility = Visibility.Visible;
                AssistantTokenBadge.Text = "{cursor}";
                AssistantDescriptionText.Text = "Positions the text caret at this position after pasting snippet text.";
                break;

            default:
                AssistantBadgeBorder.Visibility = Visibility.Visible;
                AssistantTokenBadge.Text = $"{{{prefix}}}";
                AssistantDescriptionText.Text = "Dynamic snippet token.";
                break;
        }

        AssistantOptionsPillsPanel.Visibility = AssistantOptionsPillsPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddAssistantPill(string displayLabel, string parameterValue, int tokenStart, int tokenEnd, string prefix)
    {
        var btn = new Button
        {
            Content = displayLabel,
            Style = Application.Current.TryFindResource("TokenAssistantPillStyle") as Style,
            ToolTip = $"Apply '{parameterValue}' to {{{prefix}}}"
        };

        btn.Click += (s, e) =>
        {
            string replacement = string.IsNullOrEmpty(parameterValue) ? $"{{{prefix}}}" : $"{{{prefix}:{parameterValue}}}";
            var text = SnippetTemplateBox.Text;
            if (tokenStart >= 0 && tokenEnd <= text.Length && tokenStart <= tokenEnd)
            {
                SnippetTemplateBox.Text = text.Remove(tokenStart, tokenEnd - tokenStart).Insert(tokenStart, replacement);
                SnippetTemplateBox.CaretIndex = tokenStart + replacement.Length;
                SnippetTemplateBox.Focus();
                UpdateContextualTokenAssistant();
                OnContentChanged();
            }
        };

        AssistantOptionsPillsPanel.Children.Add(btn);
    }

    private (int Start, int End, string FullToken, string Prefix, string Arg)? GetTokenAtCaret()
    {
        var text = SnippetTemplateBox.Text;
        if (string.IsNullOrEmpty(text)) return null;
        var caret = Math.Clamp(SnippetTemplateBox.CaretIndex, 0, text.Length);

        int start = -1;
        for (int i = caret - 1; i >= 0; i--)
        {
            if (text[i] == '}') break;
            if (text[i] == '{')
            {
                start = i;
                break;
            }
        }
        if (start == -1)
        {
            if (caret < text.Length && text[caret] == '{')
            {
                start = caret;
            }
            else
            {
                return null;
            }
        }

        int end = -1;
        for (int i = start + 1; i < text.Length; i++)
        {
            if (text[i] == '{') break;
            if (text[i] == '}')
            {
                end = i + 1;
                break;
            }
        }
        if (end == -1)
        {
            end = text.Length;
        }

        var fullToken = text[start..end];
        var inner = fullToken.TrimStart('{').TrimEnd('}').Trim();
        var colonIdx = inner.IndexOf(':');
        var prefix = colonIdx > 0 ? inner[..colonIdx].Trim().ToLowerInvariant() : inner.ToLowerInvariant();
        var arg = colonIdx > 0 ? inner[(colonIdx + 1)..].Trim() : string.Empty;

        return (start, end, fullToken, prefix, arg);
    }

    #endregion

    #region Rich Text Ribbon & Editor

    private void RichFontSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingRichText || SnippetRichTextBox == null) return;
        if (RichFontSizeCombo.SelectedItem is ComboBoxItem item && double.TryParse(item.Tag?.ToString(), out var size))
        {
            SnippetRichTextBox.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
            OnRichFormattingApplied();
            SnippetRichTextBox.Focus();
        }
    }

    private void RichBoldBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SnippetRichTextBox == null) return;
        EditingCommands.ToggleBold.Execute(null, SnippetRichTextBox);
        OnRichFormattingApplied();
        SnippetRichTextBox.Focus();
    }

    private void RichItalicBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SnippetRichTextBox == null) return;
        EditingCommands.ToggleItalic.Execute(null, SnippetRichTextBox);
        OnRichFormattingApplied();
        SnippetRichTextBox.Focus();
    }

    private void RichUnderlineBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SnippetRichTextBox == null) return;
        EditingCommands.ToggleUnderline.Execute(null, SnippetRichTextBox);
        OnRichFormattingApplied();
        SnippetRichTextBox.Focus();
    }

    private void RichStrikethroughBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SnippetRichTextBox == null) return;
        var curDecs = SnippetRichTextBox.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
        if (curDecs is TextDecorationCollection coll && coll.Contains(TextDecorations.Strikethrough[0]))
        {
            SnippetRichTextBox.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, null);
        }
        else
        {
            SnippetRichTextBox.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, TextDecorations.Strikethrough);
        }
        OnRichFormattingApplied();
        SnippetRichTextBox.Focus();
    }

    private void RichTextColorBtn_Click(object sender, RoutedEventArgs e)
    {
        _isColorPickerForHighlight = false;
        RichColorPickerFlyout.Show(RichTextColorBtn, isHighlightMode: false);
    }

    private void RichHighlightBtn_Click(object sender, RoutedEventArgs e)
    {
        _isColorPickerForHighlight = true;
        RichColorPickerFlyout.Show(RichHighlightBtn, isHighlightMode: true);
    }

    private void RichColorPickerFlyout_ColorSelected(object? sender, Color? color)
    {
        if (SnippetRichTextBox == null) return;

        if (_isColorPickerForHighlight)
        {
            if (color.HasValue)
            {
                var brush = new SolidColorBrush(color.Value);
                SnippetRichTextBox.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, brush);
                if (RichHighlightColorIndicator != null) RichHighlightColorIndicator.Background = brush;
            }
            else
            {
                SnippetRichTextBox.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
                if (RichHighlightColorIndicator != null) RichHighlightColorIndicator.Background = Brushes.Transparent;
            }
        }
        else
        {
            if (color.HasValue)
            {
                var brush = new SolidColorBrush(color.Value);
                SnippetRichTextBox.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
                if (RichTextColorIndicator != null) RichTextColorIndicator.Background = brush;
            }
            else
            {
                SnippetRichTextBox.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, DependencyProperty.UnsetValue);
                if (RichTextColorIndicator != null) RichTextColorIndicator.Background = Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;
            }
        }

        OnRichFormattingApplied();
        SnippetRichTextBox.Focus();
    }

    private void RichBulletListBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SnippetRichTextBox == null) return;
        EditingCommands.ToggleBullets.Execute(null, SnippetRichTextBox);
        OnRichFormattingApplied();
        SnippetRichTextBox.Focus();
    }

    private void RichNumberedListBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SnippetRichTextBox == null) return;
        EditingCommands.ToggleNumbering.Execute(null, SnippetRichTextBox);
        OnRichFormattingApplied();
        SnippetRichTextBox.Focus();
    }

    private void RichAlignLeftBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SnippetRichTextBox == null) return;
        EditingCommands.AlignLeft.Execute(null, SnippetRichTextBox);
        OnRichFormattingApplied();
        SnippetRichTextBox.Focus();
    }

    private void RichAlignCenterBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SnippetRichTextBox == null) return;
        EditingCommands.AlignCenter.Execute(null, SnippetRichTextBox);
        OnRichFormattingApplied();
        SnippetRichTextBox.Focus();
    }

    private void RichAlignRightBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SnippetRichTextBox == null) return;
        EditingCommands.AlignRight.Execute(null, SnippetRichTextBox);
        OnRichFormattingApplied();
        SnippetRichTextBox.Focus();
    }

    private void RichClearFormatBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SnippetRichTextBox == null) return;

        bool isFullDocument = SnippetRichTextBox.Selection.IsEmpty;
        TextRange targetRange = isFullDocument
            ? new TextRange(SnippetRichTextBox.Document.ContentStart, SnippetRichTextBox.Document.ContentEnd)
            : SnippetRichTextBox.Selection;

        if (string.IsNullOrEmpty(targetRange.Text)) return;

        ConvertListsToParagraphs(SnippetRichTextBox.Document.Blocks, targetRange);

        targetRange = isFullDocument
            ? new TextRange(SnippetRichTextBox.Document.ContentStart, SnippetRichTextBox.Document.ContentEnd)
            : SnippetRichTextBox.Selection;

        targetRange.ClearAllProperties();
        targetRange.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
        targetRange.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Normal);
        targetRange.ApplyPropertyValue(Inline.TextDecorationsProperty, null);
        targetRange.ApplyPropertyValue(TextElement.FontSizeProperty, 14.66);
        targetRange.ApplyPropertyValue(Block.TextAlignmentProperty, TextAlignment.Left);

        if (RichFontSizeCombo != null)
        {
            _isUpdatingRichText = true;
            try
            {
                foreach (ComboBoxItem item in RichFontSizeCombo.Items)
                {
                    if (item.Tag?.ToString() == "14.66")
                    {
                        RichFontSizeCombo.SelectedItem = item;
                        break;
                    }
                }
            }
            finally
            {
                _isUpdatingRichText = false;
            }
        }
        if (RichTextColorIndicator != null)
            RichTextColorIndicator.Background = Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;
        if (RichHighlightColorIndicator != null)
            RichHighlightColorIndicator.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF59D"));

        OnRichFormattingApplied();
        SnippetRichTextBox.Focus();
    }

    private static void ConvertListsToParagraphs(BlockCollection blocks, TextRange targetRange)
    {
        var listBlocks = blocks.OfType<List>().ToList();
        foreach (var list in listBlocks)
        {
            if (targetRange.Start.CompareTo(list.ContentEnd) <= 0 && targetRange.End.CompareTo(list.ContentStart) >= 0)
            {
                var extracted = new List<Block>();
                foreach (var item in list.ListItems.ToList())
                {
                    while (item.Blocks.Count > 0)
                    {
                        var childBlock = item.Blocks.FirstBlock;
                        item.Blocks.Remove(childBlock);
                        extracted.Add(childBlock);
                    }
                }

                foreach (var b in extracted)
                {
                    if (b is Paragraph p)
                    {
                        p.Margin = new Thickness(0, 0, 0, 4);
                        p.TextAlignment = TextAlignment.Left;
                    }
                    blocks.InsertBefore(list, b);
                }
                blocks.Remove(list);
            }
        }
    }

    private void SnippetCanvasToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        _isPaperCanvasActive = !_isPaperCanvasActive;
        ApplySnippetCanvasMode();
    }

    private void ApplySnippetCanvasMode()
    {
        try
        {
            if (_isPaperCanvasActive)
            {
                if (SnippetRichTextEditorBorder != null)
                {
                    SnippetRichTextEditorBorder.Background = Brushes.White;
                    SnippetRichTextEditorBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1"));
                }
                if (SnippetRichTextBox != null)
                {
                    SnippetRichTextBox.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
                }
                if (SnippetLivePreviewContainerBorder != null)
                {
                    SnippetLivePreviewContainerBorder.Background = Brushes.White;
                    SnippetLivePreviewContainerBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1"));
                }
                if (SnippetLivePreviewRichBox != null)
                {
                    SnippetLivePreviewRichBox.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
                }
                if (SnippetLivePreviewText != null)
                {
                    SnippetLivePreviewText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
                }

                if (SnippetCanvasIconText != null) SnippetCanvasIconText.Text = "🌙";
                if (SnippetCanvasModeText != null) SnippetCanvasModeText.Text = "Theme";
                if (SnippetCanvasToggleBtn != null) SnippetCanvasToggleBtn.ToolTip = "Switch to theme editor canvas";
            }
            else
            {
                if (SnippetRichTextEditorBorder != null)
                {
                    SnippetRichTextEditorBorder.Background = Application.Current.TryFindResource("BgInputBrush") as Brush ?? Brushes.Transparent;
                    SnippetRichTextEditorBorder.BorderBrush = Application.Current.TryFindResource("BorderBrush") as Brush ?? Brushes.Gray;
                }
                if (SnippetRichTextBox != null)
                {
                    SnippetRichTextBox.Foreground = Application.Current.TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White;
                }
                if (SnippetLivePreviewContainerBorder != null)
                {
                    SnippetLivePreviewContainerBorder.Background = Application.Current.TryFindResource("BgInputBrush") as Brush ?? Brushes.Transparent;
                    SnippetLivePreviewContainerBorder.BorderBrush = Application.Current.TryFindResource("BorderBrush") as Brush ?? Brushes.Gray;
                }
                if (SnippetLivePreviewRichBox != null)
                {
                    SnippetLivePreviewRichBox.Foreground = Application.Current.TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White;
                }
                if (SnippetLivePreviewText != null)
                {
                    SnippetLivePreviewText.Foreground = Application.Current.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;
                }

                if (SnippetCanvasIconText != null) SnippetCanvasIconText.Text = "📄";
                if (SnippetCanvasModeText != null) SnippetCanvasModeText.Text = "Paper";
                if (SnippetCanvasToggleBtn != null) SnippetCanvasToggleBtn.ToolTip = "Switch to paper canvas (standard paper background)";
            }

            _ = UpdateLivePreviewAsync();
        }
        catch
        {
        }
    }

    private void SnippetRichTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingRichText) return;
        OnContentChanged();
    }

    private void SnippetRichTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        UpdateRichToolbarSelectionState();
    }

    private void UpdateRichToolbarSelectionState()
    {
        if (_isUpdatingRichText || SnippetRichTextBox == null) return;

        var isBold = SnippetRichTextBox.Selection.GetPropertyValue(TextElement.FontWeightProperty);
        if (RichBoldBtn != null)
            RichBoldBtn.Opacity = (isBold is FontWeight fw && fw >= FontWeights.Bold) ? 1.0 : 0.7;

        var isItalic = SnippetRichTextBox.Selection.GetPropertyValue(TextElement.FontStyleProperty);
        if (RichItalicBtn != null)
            RichItalicBtn.Opacity = (isItalic is FontStyle fs && fs == FontStyles.Italic) ? 1.0 : 0.7;

        var textDecs = SnippetRichTextBox.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
        if (textDecs is TextDecorationCollection coll && coll.Count > 0)
        {
            if (RichUnderlineBtn != null)
                RichUnderlineBtn.Opacity = coll.Contains(TextDecorations.Underline[0]) ? 1.0 : 0.7;
            if (RichStrikethroughBtn != null)
                RichStrikethroughBtn.Opacity = coll.Contains(TextDecorations.Strikethrough[0]) ? 1.0 : 0.7;
        }
        else
        {
            if (RichUnderlineBtn != null) RichUnderlineBtn.Opacity = 0.7;
            if (RichStrikethroughBtn != null) RichStrikethroughBtn.Opacity = 0.7;
        }

        var fontSize = SnippetRichTextBox.Selection.GetPropertyValue(TextElement.FontSizeProperty);
        if (fontSize is double sz && RichFontSizeCombo != null)
        {
            _isUpdatingRichText = true;
            try
            {
                foreach (ComboBoxItem item in RichFontSizeCombo.Items)
                {
                    if (double.TryParse(item.Tag?.ToString(), out var itemSz) && Math.Abs(itemSz - sz) < 0.5)
                    {
                        RichFontSizeCombo.SelectedItem = item;
                        break;
                    }
                }
            }
            finally
            {
                _isUpdatingRichText = false;
            }
        }

        var fg = SnippetRichTextBox.Selection.GetPropertyValue(TextElement.ForegroundProperty);
        var bg = SnippetRichTextBox.Selection.GetPropertyValue(TextElement.BackgroundProperty);

        bool isAutoFg = fg == DependencyProperty.UnsetValue || fg == null;
        if (RichTextColorIndicator != null)
        {
            if (!isAutoFg && fg is SolidColorBrush scbFg)
                RichTextColorIndicator.Background = scbFg;
            else
                RichTextColorIndicator.Background = Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;
        }
        if (RichHighlightColorIndicator != null)
        {
            if (bg is SolidColorBrush scbBg && scbBg.Color.A > 0)
                RichHighlightColorIndicator.Background = scbBg;
            else
                RichHighlightColorIndicator.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF59D"));
        }
    }

    private void OnRichFormattingApplied()
    {
        OnContentChanged();
        UpdateRichToolbarSelectionState();
    }

    private void SnippetRichTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
        {
            if (e.Key == Key.B || e.Key == Key.I || e.Key == Key.U || e.Key == Key.Z || e.Key == Key.Y)
            {
                Dispatcher.InvokeAsync(() => OnRichFormattingApplied(), DispatcherPriority.Background);
            }
        }
    }

    #endregion

    #region Live Expansion Preview

    private void QueuePreviewUpdate()
    {
        _previewDebounceTimer?.Stop();
        _previewDebounceTimer?.Start();
    }

    private void RefreshSnippetPreviewBtn_Click(object sender, RoutedEventArgs e)
    {
        _ = UpdateLivePreviewAsync();
    }

    private async Task UpdateLivePreviewAsync()
    {
        if (SnippetLivePreviewText == null || SnippetPreviewStatsText == null) return;

        if (_currentContentType == SnippetContentType.RichText)
        {
            if (SnippetRichTextBox == null || SnippetLivePreviewRichBox == null) return;

            try
            {
                var rtf = RichTextService.SaveToRtf(SnippetRichTextBox.Document);
                var plain = RichTextService.ExtractPlainText(SnippetRichTextBox.Document);

                if (string.IsNullOrWhiteSpace(plain))
                {
                    SnippetLivePreviewRichBox.Document.Blocks.Clear();
                    SnippetPreviewStatsText.Text = "0 chars • 0 tokens";
                    return;
                }

                string previewClip = string.Empty;
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        previewClip = Clipboard.GetText();
                        if (previewClip.Length > 40) previewClip = previewClip[..37] + "...";
                    }
                }
                catch
                {
                }

                if (string.IsNullOrEmpty(previewClip)) previewClip = "[Clipboard text]";

                var previewPrompts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var tokens = PlaceholderParser.ExtractPromptTokens(plain);
                foreach (var t in tokens)
                {
                    previewPrompts[t.Label] = !string.IsNullOrEmpty(t.DefaultValue) ? t.DefaultValue : $"[{t.Label}]";
                }

                // Inject mock values for available workflow variables if in workflow context
                foreach (var v in _availableVariables)
                {
                    if (!previewPrompts.ContainsKey(v))
                    {
                        previewPrompts[v] = $"[{v}]";
                    }
                }

                var evaluatedRtf = await PlaceholderParser.EvaluateAsync(
                    rtf,
                    clipboardProvider: () => Task.FromResult(previewClip),
                    promptResponses: previewPrompts,
                    activeWindowTitle: "Active Application",
                    activeProcessName: "notepad.exe");

                // Also evaluate workflow variables in RTF
                foreach (var v in _availableVariables)
                {
                    evaluatedRtf = evaluatedRtf.Replace($"{{{v}}}", $"[{v}]");
                }

                RichTextService.LoadFromRtf(SnippetLivePreviewRichBox.Document, evaluatedRtf);

                var evaluatedPlain = RichTextService.ExtractPlainText(SnippetLivePreviewRichBox.Document);
                int tokenCount = tokens.Count + (plain.Contains("{date") ? 1 : 0) + (plain.Contains("{time") ? 1 : 0) + (plain.Contains("{guid") ? 1 : 0) + (plain.Contains("{clipboard") ? 1 : 0) + _availableVariables.Count(v => plain.Contains($"{{{v}}}"));
                SnippetPreviewStatsText.Text = $"{evaluatedPlain.Length} chars • {tokenCount} tokens";
            }
            catch
            {
                SnippetPreviewStatsText.Text = "Preview error";
            }
        }
        else
        {
            var template = SnippetTemplateBox.Text;
            if (string.IsNullOrEmpty(template))
            {
                SnippetLivePreviewText.Text = "Type a snippet template above to see a real-time expansion preview...";
                SnippetPreviewStatsText.Text = "0 chars • 0 tokens";
                return;
            }

            try
            {
                string previewClip = string.Empty;
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        previewClip = Clipboard.GetText();
                        if (previewClip.Length > 40) previewClip = previewClip[..37] + "...";
                    }
                }
                catch
                {
                }

                if (string.IsNullOrEmpty(previewClip)) previewClip = "[Clipboard text]";

                var previewPrompts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var tokens = PlaceholderParser.ExtractPromptTokens(template);
                foreach (var t in tokens)
                {
                    previewPrompts[t.Label] = !string.IsNullOrEmpty(t.DefaultValue) ? t.DefaultValue : $"[{t.Label}]";
                }

                // Inject mock values for available workflow variables if in workflow context
                foreach (var v in _availableVariables)
                {
                    if (!previewPrompts.ContainsKey(v))
                    {
                        previewPrompts[v] = $"[{v}]";
                    }
                }

                var evaluated = await PlaceholderParser.EvaluateAsync(
                    template,
                    clipboardProvider: () => Task.FromResult(previewClip),
                    promptResponses: previewPrompts,
                    activeWindowTitle: "Active Application",
                    activeProcessName: "notepad.exe");

                // Also evaluate workflow variables in plain text
                foreach (var v in _availableVariables)
                {
                    evaluated = evaluated.Replace($"{{{v}}}", $"[{v}]");
                }

                var (cleanPreview, _) = PlaceholderParser.ProcessCursorPosition(evaluated);
                SnippetLivePreviewText.Text = cleanPreview;

                int tokenCount = tokens.Count + (template.Contains("{date") ? 1 : 0) + (template.Contains("{time") ? 1 : 0) + (template.Contains("{guid") ? 1 : 0) + (template.Contains("{clipboard") ? 1 : 0) + _availableVariables.Count(v => template.Contains($"{{{v}}}"));
                SnippetPreviewStatsText.Text = $"{cleanPreview.Length} chars • {tokenCount} tokens";
            }
            catch (Exception ex)
            {
                SnippetLivePreviewText.Text = $"Preview evaluation error: {ex.Message}";
                SnippetPreviewStatsText.Text = "Error";
            }
        }
    }

    private void SnippetPreviewScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer scroller)
        {
            bool atTop = scroller.VerticalOffset == 0 && e.Delta > 0;
            bool atBottom = scroller.VerticalOffset >= scroller.ScrollableHeight && e.Delta < 0;

            if (atTop || atBottom || scroller.ScrollableHeight == 0)
            {
                e.Handled = true;
                var parent = VisualTreeHelper.GetParent(scroller);
                while (parent != null && parent is not ScrollViewer)
                {
                    parent = VisualTreeHelper.GetParent(parent);
                }
                if (parent is ScrollViewer parentScroller)
                {
                    parentScroller.ScrollToVerticalOffset(parentScroller.VerticalOffset - (e.Delta / 3.0));
                }
            }
        }
    }

    #endregion

    #region Token Validation

    public bool ValidateTokens(Window? parentWindow = null)
    {
        string template = _currentContentType == SnippetContentType.RichText
            ? (SnippetRichTextBox != null ? RichTextService.ExtractPlainText(SnippetRichTextBox.Document) : string.Empty)
            : (SnippetTemplateBox?.Text ?? string.Empty);

        if (string.IsNullOrEmpty(template)) return true;

        // 1. Enforce single {cursor} token
        int cursorCount = PlaceholderParser.CountCursorTokens(template);
        if (cursorCount > 1)
        {
            if (parentWindow != null)
            {
                ModernMessageDialog.ShowAlert(
                    parentWindow,
                    "Validation Error",
                    $"A snippet can only contain a single {{cursor}} token, but {cursorCount} occurrences were found.\n\nPlease remove the extra {{cursor}} tokens before saving.",
                    ModernDialogType.Warning);
            }

            FocusEditor();
            return false;
        }

        // 2. Validate Prompt Tokens
        var allPrompts = PlaceholderParser.ExtractAllPromptTokenOccurrences(template);
        if (allPrompts.Count > 1)
        {
            var groups = allPrompts.GroupBy(p => p.Label, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var group in groups)
            {
                if (group.Count() > 1)
                {
                    var first = group.First();
                    bool hasConflict = group.Any(p => p.Type != first.Type || p.DateFormat != first.DateFormat || p.Choices.Count != first.Choices.Count);
                    if (hasConflict)
                    {
                        if (parentWindow != null)
                        {
                            ModernMessageDialog.ShowAlert(
                                parentWindow,
                                "Validation Error",
                                $"The prompt label '{group.Key}' is used multiple times with conflicting types or options.\n\nEach distinct prompt field must have a unique label.",
                                ModernDialogType.Warning);
                        }

                        FocusEditor();
                        return false;
                    }
                }
            }

            var duplicateGroups = groups.Where(g => g.Count() > 1).ToList();
            if (duplicateGroups.Count > 0 && parentWindow != null)
            {
                string labelsText = string.Join(", ", duplicateGroups.Select(g => $"'{g.Key}'"));
                var dialog = new ConfirmationDialog(
                    $"The prompt label {(duplicateGroups.Count == 1 ? duplicateGroups[0].Key : labelsText)} appears multiple times in this snippet.\n\n" +
                    "Because they have identical labels, TriggerPoint will only show a single prompt dialog at runtime and insert that same value into all occurrences.\n\n" +
                    "Would you like to auto-rename them to unique labels (e.g. 'Label 2') so each renders a separate input field, or keep them shared?",
                    "Duplicate Prompt Labels",
                    "Auto-Rename Unique",
                    "Keep Shared")
                {
                    Owner = parentWindow
                };

                if (dialog.ShowDialog() == true)
                {
                    if (dialog.Confirmed)
                    {
                        string disambiguated = PlaceholderParser.DisambiguateDuplicatePromptTokens(template);
                        if (_currentContentType == SnippetContentType.RichText)
                        {
                            if (SnippetRichTextBox != null)
                            {
                                RichTextService.LoadFromPlainText(SnippetRichTextBox.Document, disambiguated);
                            }
                        }
                        else
                        {
                            if (SnippetTemplateBox != null)
                            {
                                SnippetTemplateBox.Text = disambiguated;
                            }
                        }
                        OnContentChanged();
                    }
                }
                else
                {
                    return false;
                }
            }
        }

        return true;
    }

    #endregion
}
