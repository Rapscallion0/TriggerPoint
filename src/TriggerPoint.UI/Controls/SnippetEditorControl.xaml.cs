using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
    private bool _isUpdatingUi;
    private bool _isUpdatingRichText;
    private bool _isPaperCanvasActive = true;
    private bool _isColorPickerForHighlight;
    private DispatcherTimer? _previewDebounceTimer;
    private DispatcherTimer? _warningTimer;
    private List<string> _availableVariables = [];
    private string _initialRtf = string.Empty;
    private bool _isRichTextModified;

    public event EventHandler? SnippetChanged;

    public SnippetContentType ContentType => _currentContentType;

    public string PlainTextTemplate
    {
        get => SnippetPlainTextBox != null ? RichTextService.ExtractPlainText(SnippetPlainTextBox.Document) : string.Empty;
        set
        {
            if (SnippetPlainTextBox != null)
            {
                LoadPlainTextIntoDocument(SnippetPlainTextBox.Document, value ?? string.Empty);
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
                LoadRtfIntoDocumentWithPills(SnippetRichTextBox.Document, value ?? string.Empty);
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
            _initialRtf = rtf ?? string.Empty;
            _isRichTextModified = false;

            if (contentType == SnippetContentType.RichText)
            {
                if (!string.IsNullOrEmpty(rtf))
                {
                    LoadRtfIntoDocumentWithPills(SnippetRichTextBox.Document, rtf);
                    PlainTextTemplate = RichTextService.ExtractPlainText(SnippetRichTextBox.Document);
                }
                else
                {
                    PlainTextTemplate = template ?? string.Empty;
                    LoadPlainTextIntoDocument(SnippetRichTextBox.Document, PlainTextTemplate);
                }
            }
            else
            {
                PlainTextTemplate = template ?? string.Empty;
                LoadPlainTextIntoDocument(SnippetRichTextBox.Document, PlainTextTemplate);
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
            SnippetPlainTextBox?.Focus();
        }
    }

    public (string Template, string Rtf) GetSnippetPayload()
    {
        if (_currentContentType == SnippetContentType.RichText)
        {
            string plain = RichTextService.ExtractPlainText(SnippetRichTextBox.Document);
            string rtf = (!_isRichTextModified && !string.IsNullOrEmpty(_initialRtf))
                ? _initialRtf
                : RichTextService.SaveToRtf(SnippetRichTextBox.Document);
            return (plain, rtf);
        }
        else
        {
            return (PlainTextTemplate, string.Empty);
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
                SnippetLivePreviewContainerBorder.SetResourceReference(Border.BackgroundProperty, "BgInputBrush");
                SnippetLivePreviewContainerBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            }
            if (SnippetLivePreviewText != null)
            {
                SnippetLivePreviewText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            }
            if (SnippetLivePreviewRichBox != null)
            {
                SnippetLivePreviewRichBox.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            }
            if (SnippetPlainEditorBorder != null)
            {
                SnippetPlainEditorBorder.SetResourceReference(Border.BackgroundProperty, "BgInputBrush");
                SnippetPlainEditorBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            }
            if (SnippetPlainTextBox != null)
            {
                SnippetPlainTextBox.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            }
        }

        UpdateContextualTokenAssistant();

        if (notify)
        {
            OnContentChanged();
        }
    }

    private void SnippetFormatPlainSegment_Click(object sender, RoutedEventArgs e)
    {
        if (_currentContentType == SnippetContentType.PlainText) return;

        // When switching from Rich to Plain, extract plain text from RichTextBox and sync
        var plainText = RichTextService.ExtractPlainText(SnippetRichTextBox.Document);
        if (!string.Equals(PlainTextTemplate, plainText, StringComparison.Ordinal))
        {
            PlainTextTemplate = plainText;
        }

        SetFormatMode(SnippetContentType.PlainText);
        SnippetPlainTextBox.Focus();
    }

    private void SnippetFormatRichSegment_Click(object sender, RoutedEventArgs e)
    {
        if (_currentContentType == SnippetContentType.RichText) return;

        // When switching from Plain to Rich, sync plain text into RichTextBox if text changed
        var currentRichPlain = RichTextService.ExtractPlainText(SnippetRichTextBox.Document);
        if (!string.Equals(currentRichPlain, PlainTextTemplate, StringComparison.Ordinal))
        {
            _isUpdatingRichText = true;
            try
            {
                LoadPlainTextIntoDocument(SnippetRichTextBox.Document, PlainTextTemplate);
            }
            finally
            {
                _isUpdatingRichText = false;
            }
            _isRichTextModified = true;
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

    private void TokenLibraryToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (StaticTokenLibraryPanel == null) return;

        bool isCurrentlyVisible = StaticTokenLibraryPanel.Visibility == Visibility.Visible;
        StaticTokenLibraryPanel.Visibility = isCurrentlyVisible ? Visibility.Collapsed : Visibility.Visible;

        if (TokenLibraryChevron != null)
        {
            TokenLibraryChevron.Text = isCurrentlyVisible ? "▸" : "▾";
        }
    }

    public void OpenTokenConfigurationDialog(InlineUIContainer uic)
    {
        string currentToken = uic.Tag as string ?? "{datetime:yyyy-MM-dd HH:mm}";
        var parentWindow = Window.GetWindow(this);
        var dlg = new TriggerPoint.UI.Views.TokenConfigurationDialog(currentToken)
        {
            Owner = parentWindow
        };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ResultToken))
        {
            UpdateTokenPillValue(uic, dlg.ResultToken);
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

    private RichTextBox? GetActiveEditor() =>
        _currentContentType == SnippetContentType.RichText ? SnippetRichTextBox : SnippetPlainTextBox;

    public void InsertTokenIntoEditor(string token)
    {
        if (string.IsNullOrEmpty(token)) return;

        var activeBox = GetActiveEditor();
        if (activeBox == null) return;

        string currentContent = RichTextService.ExtractPlainText(activeBox.Document);

        // Enforce single {cursor} rule
        if (token.Equals("{cursor}", StringComparison.OrdinalIgnoreCase))
        {
            if (currentContent.Contains("{cursor}", StringComparison.OrdinalIgnoreCase) || 
                currentContent.Contains("{{cursor}}", StringComparison.OrdinalIgnoreCase))
            {
                ShowSnippetWarning("Only one {cursor} token is allowed per snippet. Existing {cursor} has been highlighted.");
                HighlightExistingCursorToken();
                return;
            }
        }

        // Disambiguate prompt token labels
        token = PlaceholderParser.GetDisambiguatedPromptToken(token, currentContent);

        var uic = CreateInlineTokenPill(token);

        // If there's an active text selection, clear it
        if (!activeBox.Selection.IsEmpty)
        {
            activeBox.Selection.Text = string.Empty;
        }

        var caret = activeBox.CaretPosition;
        if (caret.Paragraph == null)
        {
            var p = new Paragraph { Margin = new Thickness(0) };
            activeBox.Document.Blocks.Add(p);
            caret = p.ContentStart;
        }

        var insertionPoint = caret.GetInsertionPosition(LogicalDirection.Forward);
        if (insertionPoint.Paragraph != null)
        {
            var run = insertionPoint.Parent as Run;
            if (run != null)
            {
                int offset = run.ContentStart.GetOffsetToPosition(insertionPoint);
                string text = run.Text;
                var parentInlines = run.SiblingInlines;

                if (offset <= 0)
                {
                    parentInlines.InsertBefore(run, uic);
                }
                else if (offset >= text.Length)
                {
                    parentInlines.InsertAfter(run, uic);
                }
                else
                {
                    var prefixRun = CloneRun(run, text[..offset]);
                    var suffixRun = CloneRun(run, text[offset..]);
                    parentInlines.InsertBefore(run, prefixRun);
                    parentInlines.InsertBefore(run, uic);
                    parentInlines.InsertBefore(run, suffixRun);
                    parentInlines.Remove(run);
                }
            }
            else
            {
                insertionPoint.Paragraph.Inlines.Add(uic);
            }
        }
        else
        {
            var p = new Paragraph { Margin = new Thickness(0) };
            p.Inlines.Add(uic);
            activeBox.Document.Blocks.Add(p);
        }

        if (uic.ElementEnd != null)
        {
            activeBox.CaretPosition = uic.ElementEnd;
        }

        activeBox.Focus();
        UpdateContextualTokenAssistant();
        OnContentChanged();
    }

    #region Inline Token Pill Creation & Management

    private static string GetTokenCategory(string token)
    {
        var inner = token.Trim().TrimStart('{').TrimEnd('}');
        var colon = inner.IndexOf(':');
        var prefix = colon > 0 ? inner[..colon].Trim().ToLowerInvariant() : inner.Trim().ToLowerInvariant();

        return prefix switch
        {
            "date" or "time" or "datetime" or "tomorrow" or "yesterday" => "Date",
            "clipboard" or "clip" => "Clip",
            _ => "Prompt"
        };
    }

    private static string GetTokenIcon(string token)
    {
        var inner = token.Trim().TrimStart('{').TrimEnd('}');
        var colon = inner.IndexOf(':');
        var prefix = colon > 0 ? inner[..colon].Trim().ToLowerInvariant() : inner.Trim().ToLowerInvariant();

        return prefix switch
        {
            "date" or "tomorrow" or "yesterday" => "📅",
            "time" => "⏰",
            "datetime" => "🗓️",
            "clipboard" or "clip" => "📋",
            "guid" or "uuid" => "🔑",
            "cursor" => "📍",
            "env" => "💻",
            "choice" => "🔽",
            "number" => "🔢",
            "multiline" => "📝",
            "date_picker" => "📆",
            _ => "💬"
        };
    }

    private static bool IsFormatCustomizableToken(string token)
    {
        var inner = token.Trim().TrimStart('{').TrimEnd('}');
        var colon = inner.IndexOf(':');
        var prefix = colon > 0 ? inner[..colon].Trim().ToLowerInvariant() : inner.Trim().ToLowerInvariant();

        return prefix switch
        {
            "date" or "tomorrow" or "yesterday" or "time" or "datetime" or
            "clipboard" or "clip" or "guid" or "uuid" or "env" or "number" or "date_picker" => true,
            _ => false
        };
    }

    public InlineUIContainer CreateInlineTokenPill(string fullToken)
    {
        var uic = new InlineUIContainer
        {
            BaselineAlignment = BaselineAlignment.Center,
            Focusable = false
        };
        PopulatePillVisuals(uic, fullToken);
        return uic;
    }

    public void PopulatePillVisuals(InlineUIContainer uic, string fullToken)
    {
        uic.Tag = fullToken;
        var category = GetTokenCategory(fullToken);
        var icon = GetTokenIcon(fullToken);
        bool isCustomizable = IsFormatCustomizableToken(fullToken);

        var border = new Border
        {
            Style = Application.Current.TryFindResource($"InlineTokenPill{category}Style") as Style,
            Cursor = Cursors.Arrow,
            Focusable = false
        };

        var sp = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        var mainBtn = new Button
        {
            Style = Application.Current.TryFindResource($"InlineTokenPill{category}MainBtnStyle") as Style,
            Content = $"{icon} {fullToken}",
            ToolTip = $"Token: {fullToken}\nDouble-click to configure",
            Focusable = false
        };

        Button? arrowBtn = null;
        if (isCustomizable)
        {
            arrowBtn = new Button
            {
                Style = Application.Current.TryFindResource($"InlineTokenPill{category}ArrowBtnStyle") as Style,
                Content = "▾",
                ToolTip = "Change format / presets",
                Focusable = false
            };
        }

        var closeBtn = new Button
        {
            Style = Application.Current.TryFindResource($"InlineTokenPill{category}CloseBtnStyle") as Style,
            Content = "✕",
            ToolTip = "Remove token",
            Focusable = false
        };

        sp.Children.Add(mainBtn);
        if (arrowBtn != null)
        {
            sp.Children.Add(arrowBtn);
        }
        sp.Children.Add(closeBtn);
        border.Child = sp;

        mainBtn.Click += (s, e) =>
        {
            e.Handled = true;
            var activeBox = GetActiveEditor();
            if (activeBox != null && uic.ElementEnd != null)
            {
                activeBox.CaretPosition = uic.ElementEnd;
                activeBox.Focus();
                UpdateContextualTokenAssistant();
            }
        };

        border.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ClickCount == 2)
            {
                e.Handled = true;
                OpenTokenConfigurationDialog(uic);
            }
        };

        if (arrowBtn != null)
        {
            arrowBtn.Click += (s, e) =>
            {
                e.Handled = true;
                ShowTokenPresetMenu(uic, arrowBtn, uic.Tag as string ?? fullToken);
            };
        }

        closeBtn.Click += (s, e) =>
        {
            e.Handled = true;
            RemoveInlineTokenPill(uic);
        };

        border.ContextMenu = CreateTokenContextMenu(uic, fullToken);
        uic.Child = border;
    }

    private ContextMenu? CreateTokenContextMenu(InlineUIContainer uic, string token)
    {
        var menu = new ContextMenu();
        var inner = token.Trim().TrimStart('{').TrimEnd('}');
        var colon = inner.IndexOf(':');
        var prefix = colon > 0 ? inner[..colon].Trim().ToLowerInvariant() : inner.Trim().ToLowerInvariant();

        var configMi = new MenuItem
        {
            Header = prefix is "datetime" or "date" or "time" or "tomorrow" or "yesterday"
                ? "✏️ Custom Format..."
                : "⚙️ Configure Parameters...",
            FontWeight = FontWeights.SemiBold
        };
        configMi.Click += (s, e) => OpenTokenConfigurationDialog(uic);
        menu.Items.Add(configMi);
        menu.Items.Add(new Separator());

        void AddItem(string header, string newToken)
        {
            bool isActive = string.Equals(newToken, token, StringComparison.OrdinalIgnoreCase);
            var mi = new MenuItem
            {
                Header = isActive ? $"✓ {header}" : $"   {header}",
                Tag = newToken,
                IsChecked = isActive,
                FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal
            };
            mi.Click += (s, e) =>
            {
                UpdateTokenPillValue(uic, newToken);
            };
            menu.Items.Add(mi);
        }

        void AddCategoryHeader(string header)
        {
            if (menu.Items.Count > 0)
            {
                menu.Items.Add(new Separator());
            }
            var mi = new MenuItem
            {
                Header = header,
                IsEnabled = false,
                FontWeight = FontWeights.Bold
            };
            menu.Items.Add(mi);
        }

        switch (prefix)
        {
            case "datetime":
                // 1. Date & Time (Combined)
                AddCategoryHeader("🗓️ Date & Time (Combined)");
                AddItem("Standard (yyyy-MM-dd HH:mm)", "{datetime:yyyy-MM-dd HH:mm}");
                AddItem("With Seconds (yyyy-MM-dd HH:mm:ss)", "{datetime:yyyy-MM-dd HH:mm:ss}");
                AddItem("US Full (MM/dd/yyyy hh:mm tt)", "{datetime:MM/dd/yyyy hh:mm tt}");
                AddItem("ISO 8601 (yyyy-MM-ddTHH:mm:ss)", "{datetime:s}");
                AddItem("Compact Timestamp (yyyyMMdd_HHmmss)", "{datetime:yyyyMMdd_HHmmss}");

                // 2. Date Only
                AddCategoryHeader("📅 Date Only");
                AddItem("Standard ISO (yyyy-MM-dd)", "{datetime:yyyy-MM-dd}");
                AddItem("US Date (MM/dd/yyyy)", "{datetime:MM/dd/yyyy}");
                AddItem("EU Date (dd/MM/yyyy)", "{datetime:dd/MM/yyyy}");
                AddItem("Full Date (dddd, MMMM d, yyyy)", "{datetime:dddd, MMMM d, yyyy}");
                AddItem("Compact Date (yyyyMMdd)", "{datetime:yyyyMMdd}");

                // 3. Time Only
                AddCategoryHeader("⏰ Time Only");
                AddItem("24-Hour (HH:mm:ss)", "{datetime:HH:mm:ss}");
                AddItem("12-Hour AM/PM (hh:mm tt)", "{datetime:hh:mm tt}");
                AddItem("Short 24-Hour (HH:mm)", "{datetime:HH:mm}");

                // 4. Relative Offsets (Calculations)
                AddCategoryHeader("⚡ Calculations (Offsets)");
                AddItem("Tomorrow (+1d)", "{datetime:+1d}");
                AddItem("Yesterday (-1d)", "{datetime:-1d}");
                AddItem("Next Week (+7d)", "{datetime:+7d}");
                AddItem("Next Month (+1m)", "{datetime:+1m}");
                AddItem("One Hour Later (+1h)", "{datetime:+1h}");
                break;

            case "date":
                // Deprecated token - offer 1-click conversion at top
                var convertMi = new MenuItem
                {
                    Header = "⚡ Convert to {datetime}",
                    FontWeight = FontWeights.Bold
                };
                convertMi.SetResourceReference(MenuItem.ForegroundProperty, "AccentBrush");
                convertMi.Click += (s, e) =>
                {
                    string converted = token.StartsWith("{date:", StringComparison.OrdinalIgnoreCase)
                        ? "{datetime:" + token[6..]
                        : "{datetime:yyyy-MM-dd}";
                    UpdateTokenPillValue(uic, converted);
                };
                menu.Items.Add(convertMi);

                AddCategoryHeader("🗓️ Date & Time (Combined)");
                AddItem("Standard (yyyy-MM-dd HH:mm)", "{date:yyyy-MM-dd HH:mm}");
                AddItem("With Seconds (yyyy-MM-dd HH:mm:ss)", "{date:yyyy-MM-dd HH:mm:ss}");
                AddItem("US Full (MM/dd/yyyy hh:mm tt)", "{date:MM/dd/yyyy hh:mm tt}");
                AddItem("Compact (yyyyMMdd_HHmmss)", "{date:yyyyMMdd_HHmmss}");

                AddCategoryHeader("📅 Date Only");
                AddItem("Standard (yyyy-MM-dd)", "{date}");
                AddItem("US Date (MM/dd/yyyy)", "{date:MM/dd/yyyy}");
                AddItem("EU Date (dd/MM/yyyy)", "{date:dd/MM/yyyy}");
                AddItem("Full Date (dddd, MMMM d, yyyy)", "{date:dddd, MMMM d, yyyy}");
                AddItem("Compact Date (yyyyMMdd)", "{date:yyyyMMdd}");

                AddCategoryHeader("⏰ Time Only");
                AddItem("24-Hour (HH:mm:ss)", "{date:HH:mm:ss}");
                AddItem("12-Hour AM/PM (hh:mm tt)", "{date:hh:mm tt}");

                AddCategoryHeader("⚡ Calculations (Offsets)");
                AddItem("Tomorrow (+1d)", "{date:+1d}");
                AddItem("Yesterday (-1d)", "{date:-1d}");
                AddItem("Next Week (+7d)", "{date:+7d}");
                AddItem("Next Month (+1m)", "{date:+1m}");
                break;

            case "tomorrow":
                AddItem("US Date (MM/dd/yyyy)", "{tomorrow:MM/dd/yyyy}");
                AddItem("Full Date (dddd, MMMM d)", "{tomorrow:dddd, MMMM d}");
                AddItem("Compact (yyyyMMdd)", "{tomorrow:yyyyMMdd}");
                break;

            case "yesterday":
                AddItem("US Date (MM/dd/yyyy)", "{yesterday:MM/dd/yyyy}");
                AddItem("Full Date (dddd, MMMM d)", "{yesterday:dddd, MMMM d}");
                AddItem("Compact (yyyyMMdd)", "{yesterday:yyyyMMdd}");
                break;

            case "time":
                AddItem("24-Hour (HH:mm:ss)", "{time:HH:mm:ss}");
                AddItem("12-Hour AM/PM (hh:mm tt)", "{time:hh:mm tt}");
                AddItem("Short 24-Hour (HH:mm)", "{time:HH:mm}");
                AddItem("One Hour Later (+1h)", "{time:+1h:HH:mm}");
                AddItem("30 Mins Ago (-30m)", "{time:-30m:HH:mm}");
                break;

            case "guid" or "uuid":
                AddItem("Default (Hyphenated)", "{guid}");
                AddItem("Uppercase (upper)", "{guid:upper}");
                AddItem("Compact 32-digit (N)", "{guid:N}");
                AddItem("Braced ({B})", "{guid:B}");
                AddItem("Parentheses ((P))", "{guid:P}");
                AddItem("Compact Uppercase (N:upper)", "{guid:N:upper}");
                break;

            case "clipboard" or "clip":
                AddItem("Trim Whitespace (trim)", "{clipboard:trim}");
                AddItem("Uppercase (upper)", "{clipboard:upper}");
                AddItem("Lowercase (lower)", "{clipboard:lower}");
                AddItem("URL Encoded (urlencode)", "{clipboard:urlencode}");
                AddItem("URL Decoded (urldecode)", "{clipboard:urldecode}");
                break;

            case "number":
                AddItem("Quantity: {number:Quantity}", "{number:Quantity}");
                AddItem("Count (1-100): {number:Count|1,100|10}", "{number:Count|1,100|10}");
                AddItem("Priority (1-10): {number:Priority|1,10|1}", "{number:Priority|1,10|1}");
                AddItem("Age (0-120): {number:Age|0,120|25}", "{number:Age|0,120|25}");
                break;

            case "date_picker":
                AddItem("Standard (yyyy-MM-dd)", "{date_picker:DueDate|yyyy-MM-dd}");
                AddItem("US Format (MM/dd/yyyy)", "{date_picker:DueDate|MM/dd/yyyy}");
                AddItem("EU Format (dd/MM/yyyy)", "{date_picker:DueDate|dd/MM/yyyy}");
                AddItem("Full Date (dddd, MMMM d, yyyy)", "{date_picker:DueDate|dddd, MMMM d, yyyy}");
                AddItem("Next Week (+7d)", "{date_picker:DueDate|MM/dd/yyyy|+7d}");
                break;
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        var removeMi = new MenuItem { Header = "Remove Token", Foreground = Brushes.IndianRed };
        removeMi.Click += (s, e) => RemoveInlineTokenPill(uic);
        menu.Items.Add(removeMi);

        return menu;
    }

    private void ShowTokenPresetMenu(InlineUIContainer uic, FrameworkElement target, string currentToken)
    {
        var menu = CreateTokenContextMenu(uic, currentToken);
        if (menu != null)
        {
            menu.PlacementTarget = target;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    public void UpdateTokenPillValue(InlineUIContainer uic, string newToken)
    {
        PopulatePillVisuals(uic, newToken);
        OnContentChanged();
        UpdateContextualTokenAssistant();
    }

    public void RemoveInlineTokenPill(InlineUIContainer uic)
    {
        DependencyObject parent = uic.Parent;
        if (parent is Paragraph p)
        {
            p.Inlines.Remove(uic);
        }
        else if (parent is Span s)
        {
            s.Inlines.Remove(uic);
        }
        OnContentChanged();
        UpdateContextualTokenAssistant();
        GetActiveEditor()?.Focus();
    }

    private static readonly Regex TokenMatchRegex = new(@"\{[^{}\r\n]+\}", RegexOptions.Compiled);

    public void LoadPlainTextIntoDocument(FlowDocument doc, string text)
    {
        doc.Blocks.Clear();
        if (string.IsNullOrEmpty(text))
        {
            doc.Blocks.Add(new Paragraph { Margin = new Thickness(0) });
            return;
        }

        var lines = text.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        foreach (var line in lines)
        {
            var p = new Paragraph { Margin = new Thickness(0) };
            if (string.IsNullOrEmpty(line))
            {
                doc.Blocks.Add(p);
                continue;
            }

            PopulateInlinesWithPills(p.Inlines, line);
            doc.Blocks.Add(p);
        }

        if (doc.Blocks.Count == 0)
        {
            doc.Blocks.Add(new Paragraph { Margin = new Thickness(0) });
        }
    }

    private void PopulateInlinesWithPills(InlineCollection inlines, string text)
    {
        int lastIdx = 0;
        var matches = TokenMatchRegex.Matches(text);
        foreach (Match match in matches)
        {
            if (match.Index > lastIdx)
            {
                inlines.Add(new Run(text[lastIdx..match.Index]));
            }

            var pill = CreateInlineTokenPill(match.Value);
            inlines.Add(pill);

            lastIdx = match.Index + match.Length;
        }

        if (lastIdx < text.Length)
        {
            inlines.Add(new Run(text[lastIdx..]));
        }
    }

    public void LoadRtfIntoDocumentWithPills(FlowDocument doc, string rtf)
    {
        RichTextService.LoadFromRtf(doc, rtf);
        ConvertDocumentRunsToPills(doc);
    }

    private void ConvertDocumentRunsToPills(FlowDocument doc)
    {
        foreach (var block in doc.Blocks.ToList())
        {
            ConvertBlockRunsToPills(block);
        }
    }

    private void ConvertBlockRunsToPills(Block block)
    {
        switch (block)
        {
            case Paragraph p:
                ConvertInlinesRunsToPills(p.Inlines);
                break;
            case List list:
                foreach (var item in list.ListItems)
                {
                    foreach (var child in item.Blocks.ToList())
                    {
                        ConvertBlockRunsToPills(child);
                    }
                }
                break;
            case Section sec:
                foreach (var child in sec.Blocks.ToList())
                {
                    ConvertBlockRunsToPills(child);
                }
                break;
        }
    }

    private void ConvertInlinesRunsToPills(InlineCollection inlines)
    {
        var inlineList = inlines.ToList();
        foreach (var inline in inlineList)
        {
            if (inline is Run run)
            {
                var text = run.Text;
                if (TokenMatchRegex.IsMatch(text))
                {
                    int lastIdx = 0;
                    var matches = TokenMatchRegex.Matches(text);
                    foreach (Match match in matches)
                    {
                        if (match.Index > lastIdx)
                        {
                            var prefixRun = CloneRun(run, text[lastIdx..match.Index]);
                            inlines.InsertBefore(run, prefixRun);
                        }

                        var pill = CreateInlineTokenPill(match.Value);
                        inlines.InsertBefore(run, pill);

                        lastIdx = match.Index + match.Length;
                    }

                    if (lastIdx < text.Length)
                    {
                        var suffixRun = CloneRun(run, text[lastIdx..]);
                        inlines.InsertBefore(run, suffixRun);
                    }

                    inlines.Remove(run);
                }
            }
            else if (inline is Span span)
            {
                ConvertInlinesRunsToPills(span.Inlines);
            }
        }
    }

    private static Run CloneRun(Run source, string newText)
    {
        var clone = new Run(newText)
        {
            FontWeight = source.FontWeight,
            FontStyle = source.FontStyle,
            FontSize = source.FontSize,
            FontFamily = source.FontFamily,
            Foreground = source.Foreground,
            Background = source.Background
        };
        if (source.TextDecorations != null && source.TextDecorations.Count > 0)
        {
            clone.TextDecorations = source.TextDecorations.Clone();
        }
        return clone;
    }

    #endregion

    private void HighlightExistingCursorToken()
    {
        var activeBox = GetActiveEditor();
        if (activeBox == null) return;

        foreach (var block in activeBox.Document.Blocks)
        {
            if (block is Paragraph p)
            {
                foreach (var inline in p.Inlines)
                {
                    if (inline is InlineUIContainer uic && (uic.Tag as string)?.Equals("{cursor}", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        activeBox.Focus();
                        activeBox.Selection.Select(uic.ElementStart, uic.ElementEnd);
                        return;
                    }
                    else if (inline is Run r && r.Text.Contains("{cursor}", StringComparison.OrdinalIgnoreCase))
                    {
                        int idx = r.Text.IndexOf("{cursor}", StringComparison.OrdinalIgnoreCase);
                        var start = r.ContentStart.GetPositionAtOffset(idx);
                        var end = start?.GetPositionAtOffset(8);
                        if (start != null && end != null)
                        {
                            activeBox.Focus();
                            activeBox.Selection.Select(start, end);
                            return;
                        }
                    }
                }
            }
        }
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

    #endregion

    #region Contextual Token Assistant

    private void SnippetPlainTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        CheckInlineAutocomplete(SnippetPlainTextBox);
        UpdateContextualTokenAssistant();
    }

    private void SnippetPlainTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingUi) return;
        AutoConvertTypedTokens(SnippetPlainTextBox);
        CheckInlineAutocomplete(SnippetPlainTextBox);
        UpdateContextualTokenAssistant();
        OnContentChanged();
    }

    private void SnippetPlainTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        HandleEditorPreviewKeyDown(SnippetPlainTextBox, e);
    }

    private void HandleEditorPreviewKeyDown(RichTextBox box, KeyEventArgs e)
    {
        if (TokenAutocompletePopup != null && TokenAutocompletePopup.IsOpen)
        {
            if (e.Key == Key.Escape)
            {
                TokenAutocompletePopup.IsOpen = false;
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Down)
            {
                int next = TokenAutocompleteListBox.SelectedIndex + 1;
                if (next < TokenAutocompleteListBox.Items.Count)
                {
                    TokenAutocompleteListBox.SelectedIndex = next;
                    TokenAutocompleteListBox.ScrollIntoView(TokenAutocompleteListBox.SelectedItem);
                }
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Up)
            {
                int prev = TokenAutocompleteListBox.SelectedIndex - 1;
                if (prev >= 0)
                {
                    TokenAutocompleteListBox.SelectedIndex = prev;
                    TokenAutocompleteListBox.ScrollIntoView(TokenAutocompleteListBox.SelectedItem);
                }
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                if (TokenAutocompleteListBox.SelectedItem is AutocompleteTokenItem selectedItem)
                {
                    CommitAutocomplete(box, selectedItem);
                    e.Handled = true;
                    return;
                }
            }
        }

        if (e.Key == Key.Back)
        {
            var caret = box.CaretPosition;
            var prev = caret.GetAdjacentElement(LogicalDirection.Backward);
            if (prev is InlineUIContainer uic)
            {
                RemoveInlineTokenPill(uic);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Delete)
        {
            var caret = box.CaretPosition;
            var next = caret.GetAdjacentElement(LogicalDirection.Forward);
            if (next is InlineUIContainer uic)
            {
                RemoveInlineTokenPill(uic);
                e.Handled = true;
            }
        }
    }

    private void AutoConvertTypedTokens(RichTextBox box)
    {
        if (_isUpdatingUi) return;

        var caret = box.CaretPosition;
        if (caret.Parent is Run run)
        {
            var text = run.Text;
            if (text.Contains('{') && text.Contains('}'))
            {
                var match = TokenMatchRegex.Match(text);
                if (match.Success)
                {
                    _isUpdatingUi = true;
                    try
                    {
                        var parentInlines = run.SiblingInlines;
                        int lastIdx = 0;
                        var matches = TokenMatchRegex.Matches(text);
                        InlineUIContainer? lastPill = null;

                        foreach (Match m in matches)
                        {
                            if (m.Index > lastIdx)
                            {
                                var pre = CloneRun(run, text[lastIdx..m.Index]);
                                parentInlines.InsertBefore(run, pre);
                            }

                            var pill = CreateInlineTokenPill(m.Value);
                            parentInlines.InsertBefore(run, pill);
                            lastPill = pill;

                            lastIdx = m.Index + m.Length;
                        }

                        if (lastIdx < text.Length)
                        {
                            var post = CloneRun(run, text[lastIdx..]);
                            parentInlines.InsertBefore(run, post);
                        }

                        parentInlines.Remove(run);

                        if (lastPill?.ElementEnd != null)
                        {
                            box.CaretPosition = lastPill.ElementEnd;
                        }
                    }
                    finally
                    {
                        _isUpdatingUi = false;
                    }
                }
            }
        }
    }

    #region Deprecated Token Upgrade Banner

    public string GetCurrentSnippetText()
    {
        if (_currentContentType == SnippetContentType.RichText)
        {
            return SnippetRichTextBox != null ? RichTextService.ExtractPlainText(SnippetRichTextBox.Document) : string.Empty;
        }
        return PlainTextTemplate;
    }

    public bool HasDeprecatedDateTokens()
    {
        string text = GetCurrentSnippetText();
        return Regex.IsMatch(text, @"\{date(?::[^{}]*)?\}", RegexOptions.IgnoreCase);
    }

    public void CheckForDeprecatedTokens()
    {
        if (SnippetTokenUpgradeBanner == null) return;
        bool hasDeprecated = HasDeprecatedDateTokens();
        SnippetTokenUpgradeBanner.Visibility = hasDeprecated ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ConvertDeprecatedDateTokens()
    {
        string currentText = GetCurrentSnippetText();
        if (string.IsNullOrEmpty(currentText)) return;

        // Convert {date} -> {datetime:yyyy-MM-dd} (preserving exact same default date output)
        // Convert {date:FMT} -> {datetime:FMT}
        string updatedText = Regex.Replace(currentText, @"\{date(?::([^{}]+))?\}", m =>
        {
            if (m.Groups[1].Success && !string.IsNullOrWhiteSpace(m.Groups[1].Value))
            {
                return $"{{datetime:{m.Groups[1].Value}}}";
            }
            return "{datetime:yyyy-MM-dd}";
        }, RegexOptions.IgnoreCase);

        if (_currentContentType == SnippetContentType.RichText)
        {
            if (SnippetRichTextBox != null)
            {
                LoadPlainTextIntoDocument(SnippetRichTextBox.Document, updatedText);
            }
        }
        else
        {
            PlainTextTemplate = updatedText;
        }

        OnContentChanged();
        CheckForDeprecatedTokens();
    }

    private void ConvertDateTokensBtn_Click(object sender, RoutedEventArgs e)
    {
        ConvertDeprecatedDateTokens();
    }

    private void DismissDateUpgradeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SnippetTokenUpgradeBanner != null)
        {
            SnippetTokenUpgradeBanner.Visibility = Visibility.Collapsed;
        }
    }

    public void UpdateContextualTokenAssistant()
    {
        CheckForDeprecatedTokens();
    }

    #endregion

    private (InlineUIContainer? ActivePill, string FullToken, string Prefix, string Arg)? GetActiveTokenAtCaret()
    {
        var box = GetActiveEditor();
        if (box == null) return null;

        var caret = box.CaretPosition;

        // 1. Check if caret is adjacent to an InlineUIContainer
        var backwardPill = caret.GetAdjacentElement(LogicalDirection.Backward) as InlineUIContainer;
        var forwardPill = caret.GetAdjacentElement(LogicalDirection.Forward) as InlineUIContainer;
        var pill = backwardPill ?? forwardPill;

        if (pill != null && pill.Tag is string pillToken)
        {
            var inner = pillToken.TrimStart('{').TrimEnd('}').Trim();
            var colonIdx = inner.IndexOf(':');
            var prefix = colonIdx > 0 ? inner[..colonIdx].Trim().ToLowerInvariant() : inner.ToLowerInvariant();
            var arg = colonIdx > 0 ? inner[(colonIdx + 1)..].Trim() : string.Empty;
            return (pill, pillToken, prefix, arg);
        }

        // 2. Check if caret is inside a Run with {token}
        if (caret.Parent is Run run)
        {
            var text = run.Text;
            int offset = run.ContentStart.GetOffsetToPosition(caret);
            offset = Math.Clamp(offset, 0, text.Length);

            int start = -1;
            for (int i = offset - 1; i >= 0; i--)
            {
                if (text[i] == '}') break;
                if (text[i] == '{')
                {
                    start = i;
                    break;
                }
            }
            if (start == -1 && offset < text.Length && text[offset] == '{')
            {
                start = offset;
            }

            if (start >= 0)
            {
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
                if (end == -1) end = text.Length;

                var rawToken = text[start..end];
                var inner = rawToken.TrimStart('{').TrimEnd('}').Trim();
                var colonIdx = inner.IndexOf(':');
                var prefix = colonIdx > 0 ? inner[..colonIdx].Trim().ToLowerInvariant() : inner.ToLowerInvariant();
                var arg = colonIdx > 0 ? inner[(colonIdx + 1)..].Trim() : string.Empty;
                return (null, rawToken, prefix, arg);
            }
        }

        return null;
    }

    #endregion

    #region Inline Token Autocomplete (@ Trigger)

    public class AutocompleteTokenItem
    {
        public string DisplayText { get; set; } = string.Empty;
        public string TokenValue { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
    }

    private List<AutocompleteTokenItem> BuildTokenCatalog()
    {
        var list = new List<AutocompleteTokenItem>
        {
            new() { Icon = "🗓️", DisplayText = "{datetime}", TokenValue = "{datetime:yyyy-MM-dd HH:mm}", Description = "Current date & time (combined, date-only, time-only, offsets)" },
            new() { Icon = "⏰", DisplayText = "{time}", TokenValue = "{time}", Description = "Current time (12/24-hour specifiers & offsets)" },
            new() { Icon = "🔑", DisplayText = "{guid}", TokenValue = "{guid}", Description = "Unique identifier (hyphenated, upper, compact)" },
            new() { Icon = "📋", DisplayText = "{clipboard}", TokenValue = "{clipboard}", Description = "Current clipboard contents (trim, casing)" },
            new() { Icon = "💻", DisplayText = "{username}", TokenValue = "{username}", Description = "Current Windows login user name" },
            new() { Icon = "💻", DisplayText = "{active_window}", TokenValue = "{active_window}", Description = "Active foreground window title" },
            new() { Icon = "📍", DisplayText = "{cursor}", TokenValue = "{cursor}", Description = "Caret landing position after pasting snippet" },
            new() { Icon = "💬", DisplayText = "{text:Label}", TokenValue = "{text:Label}", Description = "Interactive single-line text input prompt" },
            new() { Icon = "📝", DisplayText = "{multiline:Label}", TokenValue = "{multiline:Label}", Description = "Interactive multi-line text input prompt" },
            new() { Icon = "🔢", DisplayText = "{number:Quantity}", TokenValue = "{number:Quantity}", Description = "Numeric input prompt with optional min/max/step" },
            new() { Icon = "🔽", DisplayText = "{choice:Label|Opt1,Opt2}", TokenValue = "{choice:Label|Opt1=Val1,Opt2}", Description = "Dropdown selection choice prompt" },
            new() { Icon = "📆", DisplayText = "{date_picker:DueDate}", TokenValue = "{date_picker:DueDate}", Description = "Calendar date picker prompt with format & offset" }
        };

        if (_availableVariables != null)
        {
            foreach (var variable in _availableVariables)
            {
                if (!string.IsNullOrWhiteSpace(variable))
                {
                    list.Add(new AutocompleteTokenItem
                    {
                        Icon = "⚡",
                        DisplayText = $"{{{variable}}}",
                        TokenValue = $"{{{variable}}}",
                        Description = "Workflow variable from preceding action"
                    });
                }
            }
        }

        return list;
    }

    private void CheckInlineAutocomplete(RichTextBox box)
    {
        if (_isUpdatingUi || _isUpdatingRichText || TokenAutocompletePopup == null) return;

        var caret = box.CaretPosition;
        if (caret.Parent is Run run)
        {
            int offset = run.ContentStart.GetOffsetToPosition(caret);
            string text = run.Text;
            if (offset > 0 && offset <= text.Length)
            {
                int atIdx = text.LastIndexOf('@', offset - 1);
                if (atIdx >= 0)
                {
                    string query = text.Substring(atIdx + 1, offset - atIdx - 1);
                    if (!query.Any(char.IsWhiteSpace) && !query.Contains('{') && !query.Contains('}'))
                    {
                        OpenTokenAutocomplete(box, query);
                        return;
                    }
                }
            }
        }

        if (TokenAutocompletePopup.IsOpen)
        {
            TokenAutocompletePopup.IsOpen = false;
        }
    }

    private void OpenTokenAutocomplete(RichTextBox box, string query)
    {
        var catalog = BuildTokenCatalog();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? catalog
            : catalog.Where(t =>
                t.DisplayText.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.TokenValue.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Description.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        if (filtered.Count == 0)
        {
            TokenAutocompletePopup.IsOpen = false;
            return;
        }

        TokenAutocompleteListBox.ItemsSource = filtered;
        TokenAutocompleteListBox.SelectedIndex = 0;

        TokenAutocompletePopup.PlacementTarget = box;
        TokenAutocompletePopup.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
        {
            var rect = box.CaretPosition.GetCharacterRect(LogicalDirection.Forward);
            var point = new Point(Math.Max(0, rect.Left), rect.Bottom + 4);
            return new[] { new CustomPopupPlacement(point, PopupPrimaryAxis.Horizontal) };
        };

        if (!TokenAutocompletePopup.IsOpen)
        {
            TokenAutocompletePopup.IsOpen = true;
        }
    }

    private void CommitAutocomplete(RichTextBox box, AutocompleteTokenItem item)
    {
        TokenAutocompletePopup.IsOpen = false;
        var caret = box.CaretPosition;
        if (caret.Parent is Run run)
        {
            int offset = run.ContentStart.GetOffsetToPosition(caret);
            string text = run.Text;
            int atIdx = text.LastIndexOf('@', Math.Max(0, offset - 1));
            if (atIdx >= 0)
            {
                var atPos = run.ContentStart.GetPositionAtOffset(atIdx);
                var endPos = run.ContentStart.GetPositionAtOffset(offset);
                if (atPos != null && endPos != null)
                {
                    var range = new TextRange(atPos, endPos);
                    range.Text = string.Empty;
                    box.CaretPosition = range.Start;
                }
            }
        }

        InsertTokenIntoEditor(item.TokenValue);
    }

    private void TokenAutocompleteListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Tab)
        {
            var box = GetActiveEditor();
            if (box != null && TokenAutocompleteListBox.SelectedItem is AutocompleteTokenItem item)
            {
                CommitAutocomplete(box, item);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Escape)
        {
            TokenAutocompletePopup.IsOpen = false;
            e.Handled = true;
            GetActiveEditor()?.Focus();
        }
    }

    private void TokenAutocompleteListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var box = GetActiveEditor();
        if (box != null && TokenAutocompleteListBox.SelectedItem is AutocompleteTokenItem item)
        {
            CommitAutocomplete(box, item);
            e.Handled = true;
        }
    }

    private void TokenAutocompleteListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var box = GetActiveEditor();
        if (box != null && TokenAutocompleteListBox.SelectedItem is AutocompleteTokenItem item)
        {
            CommitAutocomplete(box, item);
            e.Handled = true;
        }
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

                if (SnippetCanvasIconText != null) SnippetCanvasIconText.Text = "🌙";
                if (SnippetCanvasModeText != null) SnippetCanvasModeText.Text = "Theme";
                if (SnippetCanvasToggleBtn != null) SnippetCanvasToggleBtn.ToolTip = "Switch to theme editor canvas";
            }
            else
            {
                if (SnippetRichTextEditorBorder != null)
                {
                    SnippetRichTextEditorBorder.SetResourceReference(Border.BackgroundProperty, "BgInputBrush");
                    SnippetRichTextEditorBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                }
                if (SnippetRichTextBox != null)
                {
                    SnippetRichTextBox.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
                }
                if (SnippetLivePreviewContainerBorder != null)
                {
                    SnippetLivePreviewContainerBorder.SetResourceReference(Border.BackgroundProperty, "BgInputBrush");
                    SnippetLivePreviewContainerBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                }
                if (SnippetLivePreviewRichBox != null)
                {
                    SnippetLivePreviewRichBox.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
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
        if (_isUpdatingRichText || _isUpdatingUi) return;
        _isRichTextModified = true;
        AutoConvertTypedTokens(SnippetRichTextBox);
        CheckInlineAutocomplete(SnippetRichTextBox);
        UpdateContextualTokenAssistant();
        OnContentChanged();
    }

    private void SnippetRichTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        UpdateRichToolbarSelectionState();
        CheckInlineAutocomplete(SnippetRichTextBox);
        UpdateContextualTokenAssistant();
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
        HandleEditorPreviewKeyDown(SnippetRichTextBox, e);

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

    public async Task UpdateLivePreviewAsync()
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

                foreach (var v in _availableVariables)
                {
                    if (!previewPrompts.ContainsKey(v))
                    {
                        previewPrompts[v] = $"[{v}]";
                    }
                }

                var (evaluatedRtf, _, _, _) = RichTextService.EvaluateFlowDocument(
                    rtf,
                    plain,
                    previewPrompts,
                    () => Task.FromResult(previewClip),
                    DateTime.Now,
                    "Active Application",
                    "notepad.exe");

                foreach (var v in _availableVariables)
                {
                    evaluatedRtf = evaluatedRtf.Replace($"{{{v}}}", $"[{v}]");
                }

                RichTextService.LoadFromRtf(SnippetLivePreviewRichBox.Document, evaluatedRtf);

                var evaluatedPlain = RichTextService.ExtractPlainText(SnippetLivePreviewRichBox.Document);
                int tokenCount = tokens.Count + (plain.Contains("{datetime") || plain.Contains("{date") ? 1 : 0) + (plain.Contains("{time") ? 1 : 0) + (plain.Contains("{guid") ? 1 : 0) + (plain.Contains("{clipboard") ? 1 : 0) + _availableVariables.Count(v => plain.Contains($"{{{v}}}"));
                SnippetPreviewStatsText.Text = $"{evaluatedPlain.Length} chars • {tokenCount} tokens";
            }
            catch
            {
                SnippetPreviewStatsText.Text = "Preview error";
            }
        }
        else
        {
            var template = PlainTextTemplate;
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

                foreach (var v in _availableVariables)
                {
                    evaluated = evaluated.Replace($"{{{v}}}", $"[{v}]");
                }

                var (cleanPreview, _) = PlaceholderParser.ProcessCursorPosition(evaluated);
                SnippetLivePreviewText.Text = cleanPreview;

                int tokenCount = tokens.Count + (template.Contains("{datetime") || template.Contains("{date") ? 1 : 0) + (template.Contains("{time") ? 1 : 0) + (template.Contains("{guid") ? 1 : 0) + (template.Contains("{clipboard") ? 1 : 0) + _availableVariables.Count(v => template.Contains($"{{{v}}}"));
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
            : PlainTextTemplate;

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
                                LoadPlainTextIntoDocument(SnippetRichTextBox.Document, disambiguated);
                            }
                        }
                        else
                        {
                            PlainTextTemplate = disambiguated;
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
