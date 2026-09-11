using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;
using TriggerPoint.UI.Theme;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;

namespace TriggerPoint.UI.Views;

public partial class SettingsWindow
{
    private Guid? _activeWorkflowStepId;

    private void SwitchToVisualMode()
    {
        if (WorkflowVisualContainer == null || WorkflowScriptContainer == null) return;
        WorkflowVisualContainer.Visibility = Visibility.Visible;
        WorkflowScriptContainer.Visibility = Visibility.Collapsed;
        if (_selectedItem != null)
        {
            _selectedItem.Payload.WorkflowMode = WorkflowMode.Visual;
            UpdateWorkflowPresetsVisibility();
        }
    }

    private void SwitchToScriptMode()
    {
        if (WorkflowVisualContainer == null || WorkflowScriptContainer == null) return;
        WorkflowVisualContainer.Visibility = Visibility.Collapsed;
        WorkflowScriptContainer.Visibility = Visibility.Visible;
        if (_selectedItem != null)
        {
            _selectedItem.Payload.WorkflowMode = WorkflowMode.Script;
            UpdateWorkflowPresetsVisibility();
        }
    }

    private void ReturnToVisualBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem == null) return;

        // Guard: nothing to return to if no visual steps exist
        if (_selectedItem.Payload.WorkflowSteps == null || _selectedItem.Payload.WorkflowSteps.Count == 0)
        {
            ModernMessageDialog.ShowAlert(this,
                "No Visual Steps",
                "There are no visual steps to return to. Add steps in Visual mode first, or start a fresh workflow.",
                ModernDialogType.Info);
            return;
        }

        // Warn if the script exists (may be out of sync with visual steps)
        if (!string.IsNullOrWhiteSpace(_selectedItem.Payload.ScriptSource))
        {
            bool confirmed = ModernMessageDialog.ShowConfirm(this,
                "Return to Visual Steps",
                "Your JavaScript script will be preserved but will no longer execute — only the visual steps will run.\n\nIf you've modified the script since the last conversion, those edits won't be reflected in the visual steps.\n\nContinue?",
                "← Return to Visual Steps",
                "Stay in Script Mode");
            if (!confirmed) return;
        }

        SwitchToVisualMode();
        OnFormEdited();

        // Update ReturnToVisualBtn visibility based on step count
        UpdateReturnToVisualBtnVisibility();
    }

    private void UpdateReturnToVisualBtnVisibility()
    {
        if (ReturnToVisualBtn == null) return;
        bool hasSteps = _selectedItem?.Payload.WorkflowSteps?.Count > 0;
        ReturnToVisualBtn.Visibility = hasSteps ? Visibility.Visible : Visibility.Collapsed;
    }

    private void WorkflowScriptEditor_TextChanged(object? sender, EventArgs e)
    {
        if (!_isUpdatingForm && _selectedItem != null && WorkflowScriptEditor != null)
        {
            _selectedItem.Payload.ScriptSource = WorkflowScriptEditor.Text;
            UpdateWorkflowPresetsVisibility();
            OnFormEdited();
        }
    }

    private bool _isScriptEditorMaximized;

    private void MaximizeScriptEditorBtn_Click(object sender, RoutedEventArgs e)
    {
        _isScriptEditorMaximized = !_isScriptEditorMaximized;
        ApplyScriptEditorMaximizedState();
    }

    private void ApplyScriptEditorMaximizedState()
    {
        if (WorkflowScriptEditor == null || MaximizeScriptEditorBtn == null) return;

        if (_isScriptEditorMaximized)
        {
            if (ConflictBanner != null) ConflictBanner.Visibility = Visibility.Collapsed;
            if (ItemNameDescRow != null) ItemNameDescRow.Visibility = Visibility.Collapsed;
            if (ItemTypePresentationRow != null) ItemTypePresentationRow.Visibility = Visibility.Collapsed;
            if (ItemHotkeyRow != null) ItemHotkeyRow.Visibility = Visibility.Collapsed;
            if (ContextProcessFilterGroup != null) ContextProcessFilterGroup.Visibility = Visibility.Collapsed;

            double targetHeight = Math.Max(480, (EditorScrollViewer?.ActualHeight ?? 500) - 110);
            WorkflowScriptEditor.Height = targetHeight;
            WorkflowScriptEditor.MinHeight = 400;

            MaximizeScriptEditorBtn.Content = "❐ Restore";
            MaximizeScriptEditorBtn.ToolTip = "Restore standard form view";
        }
        else
        {
            if (ItemNameDescRow != null) ItemNameDescRow.Visibility = Visibility.Visible;
            if (ItemTypePresentationRow != null) ItemTypePresentationRow.Visibility = Visibility.Visible;
            if (ItemHotkeyRow != null) ItemHotkeyRow.Visibility = Visibility.Visible;
            if (ContextProcessFilterGroup != null) ContextProcessFilterGroup.Visibility = Visibility.Visible;

            WorkflowScriptEditor.Height = 280;
            WorkflowScriptEditor.MinHeight = 200;

            MaximizeScriptEditorBtn.Content = "⛶ Maximize";
            MaximizeScriptEditorBtn.ToolTip = "Toggle expanded full-view code editor";
        }
    }

    private void UpdateWorkflowPresetsVisibility()
    {
        if (WorkflowPresetsBtn == null) return;
        bool hasSteps = _selectedItem?.Payload.WorkflowSteps != null && _selectedItem.Payload.WorkflowSteps.Count > 0;
        bool hasScript = !string.IsNullOrWhiteSpace(_selectedItem?.Payload.ScriptSource);
        bool hasContent = hasSteps || hasScript;
        WorkflowPresetsBtn.Visibility = hasContent ? Visibility.Collapsed : Visibility.Visible;
    }

    private void WorkflowToggleAllExpandBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem?.Payload.WorkflowSteps == null || _selectedItem.Payload.WorkflowSteps.Count == 0) return;

        bool anyExpanded = _selectedItem.Payload.WorkflowSteps.Any(s => !s.IsCollapsed);
        bool newCollapsedState = anyExpanded; // If any expanded, collapse them; otherwise expand them

        foreach (var s in _selectedItem.Payload.WorkflowSteps)
        {
            s.IsCollapsed = newCollapsedState;
        }

        UpdateToggleAllExpandButtonUi();
        RebuildWorkflowStepCards();
    }

    private void UpdateToggleAllExpandButtonUi()
    {
        if (WorkflowToggleAllIcon == null || WorkflowToggleAllText == null || WorkflowToggleAllExpandBtn == null) return;
        bool anyExpanded = _selectedItem?.Payload.WorkflowSteps?.Any(s => !s.IsCollapsed) == true;
        WorkflowToggleAllIcon.Data = anyExpanded ? CollapseAllGeometry : ExpandAllGeometry;
        WorkflowToggleAllText.Text = anyExpanded ? "Collapse All" : "Expand All";
        WorkflowToggleAllExpandBtn.ToolTip = anyExpanded ? "Collapse all step cards" : "Expand all step cards";
    }

    private void SetActiveWorkflowStep(Guid stepId, bool focusFirstInput = false)
    {
        _activeWorkflowStepId = stepId;
        if (WorkflowStepsHost == null) return;

        foreach (UIElement child in WorkflowStepsHost.Children)
        {
            Border? card = null;
            var pills = new List<Border>();

            if (child is Grid g)
            {
                foreach (var b in g.Children.OfType<Border>())
                {
                    if (b.Tag is WorkflowStep) card = b;
                    else if (b.Tag is Guid) pills.Add(b);
                }
            }
            else if (child is StackPanel sp)
            {
                foreach (var b in sp.Children.OfType<Border>())
                {
                    if (b.Tag is WorkflowStep) card = b;
                    else if (b.Tag is Guid) pills.Add(b);
                }
            }
            else if (child is Border b && b.Tag is WorkflowStep)
            {
                card = b;
            }

            if (card == null || card.Tag is not WorkflowStep step) continue;

            bool isActive = step.Id == _activeWorkflowStepId;
            card.SnapsToDevicePixels = true;
            card.UseLayoutRounding = true;
            card.Effect = null;

            if (isActive)
            {
                card.BorderBrush = Application.Current.TryFindResource("WorkflowBrush") as Brush ?? Brushes.Purple;
                card.BorderThickness = new Thickness(1.5);
                card.Background = new SolidColorBrush(Color.FromArgb(0x18, 0xA8, 0x55, 0xF7));

                if (focusFirstInput)
                {
                    FocusFirstInputInCard(card);
                }
            }
            else
            {
                card.BorderBrush = Application.Current.TryFindResource("CardBorderBrush") as Brush ?? Brushes.Gray;
                card.BorderThickness = new Thickness(1);
                card.Background = Application.Current.TryFindResource("CardBgBrush") as Brush ?? Brushes.DarkSlateGray;
            }

            // Update insertion pills on this step wrapper
            foreach (var pill in pills)
            {
                if (isActive)
                {
                    pill.BorderBrush = Application.Current.TryFindResource("WorkflowBrush") as Brush ?? Brushes.Purple;
                    if (pill.Child is TextBlock tb)
                    {
                        tb.Foreground = Application.Current.TryFindResource("WorkflowBrush") as Brush ?? Brushes.Purple;
                    }
                }
                else
                {
                    pill.BorderBrush = Application.Current.TryFindResource("BorderSubtleBrush") as Brush ?? Brushes.Gray;
                    if (pill.Child is TextBlock tb)
                    {
                        tb.Foreground = Application.Current.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;
                    }
                }
            }
        }
    }

    private static void FocusFirstInputInCard(Border card)
    {
        if (card.Child is not Panel mainPanel) return;
        var textBox = FindVisualChild<TextBox>(mainPanel);
        if (textBox != null)
        {
            textBox.Focus();
            textBox.SelectAll();
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild) return typedChild;
            var descendant = FindVisualChild<T>(child);
            if (descendant != null) return descendant;
        }
        return null;
    }

    private void RebuildWorkflowStepCards(Guid? stepIdToFocus = null)
    {
        UpdateWorkflowPresetsVisibility();
        if (WorkflowStepsHost == null) return;
        WorkflowStepsHost.Children.Clear();

        if (_selectedItem == null || _selectedItem.Payload.WorkflowSteps == null || _selectedItem.Payload.WorkflowSteps.Count == 0)
        {
            if (WorkflowEmptyState != null) WorkflowEmptyState.Visibility = Visibility.Visible;
            UpdateToggleAllExpandButtonUi();
            return;
        }

        if (WorkflowEmptyState != null) WorkflowEmptyState.Visibility = Visibility.Collapsed;

        var steps = _selectedItem.Payload.WorkflowSteps;
        var definedVariables = new List<string>();
        FrameworkElement? elementToFocus = null;

        if (stepIdToFocus.HasValue)
        {
            _activeWorkflowStepId = stepIdToFocus.Value;
        }
        else if (!_activeWorkflowStepId.HasValue || !steps.Any(s => s.Id == _activeWorkflowStepId.Value))
        {
            _activeWorkflowStepId = steps[0].Id;
        }

        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            int stepIndex = i + 1;
            bool isFirst = i == 0;
            bool isLast = i == steps.Count - 1;

            var stepWrapperGrid = new Grid { Margin = new Thickness(0, 4, 0, 8) };

            // 1. Step Card Border
            var card = CreateStepCard(step, stepIndex, isFirst, isLast, [.. definedVariables]);
            card.Tag = step;
            card.Margin = new Thickness(0, 10, 0, 10);
            stepWrapperGrid.Children.Add(card);

            // 2. Top insertion pill: "+ Add step before" (intersects top border line)
            var topPill = CreateStepInsertionPill("Add step before", i, step.Id);
            topPill.VerticalAlignment = VerticalAlignment.Top;
            topPill.HorizontalAlignment = HorizontalAlignment.Center;
            Panel.SetZIndex(topPill, 10);
            stepWrapperGrid.Children.Add(topPill);

            // 3. Bottom insertion pill: "+ Add step after" (intersects bottom border line)
            var bottomPill = CreateStepInsertionPill("Add step after", i + 1, step.Id);
            bottomPill.VerticalAlignment = VerticalAlignment.Bottom;
            bottomPill.HorizontalAlignment = HorizontalAlignment.Center;
            Panel.SetZIndex(bottomPill, 10);
            stepWrapperGrid.Children.Add(bottomPill);

            WorkflowStepsHost.Children.Add(stepWrapperGrid);

            if (step.Id == _activeWorkflowStepId)
            {
                elementToFocus = card;
            }

            // Record variable names if this step produces them
            if (step.StepType == WorkflowStepType.Prompt)
            {
                if (step.PromptFields != null && step.PromptFields.Count > 0)
                {
                    foreach (var f in step.PromptFields)
                    {
                        if (!string.IsNullOrWhiteSpace(f.VariableName))
                        {
                            definedVariables.Add(f.VariableName.Trim());
                        }
                    }
                }
                else if (!string.IsNullOrWhiteSpace(step.VariableName))
                {
                    definedVariables.Add(step.VariableName.Trim());
                }
            }
        }

        UpdateToggleAllExpandButtonUi();

        if (_activeWorkflowStepId.HasValue)
        {
            SetActiveWorkflowStep(_activeWorkflowStepId.Value, focusFirstInput: stepIdToFocus.HasValue);
        }

        if (elementToFocus != null && stepIdToFocus.HasValue)
        {
            Dispatcher.InvokeAsync(() =>
            {
                elementToFocus.BringIntoView();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private FrameworkElement CreateStepInsertionPill(string label, int insertIndex, Guid stepId)
    {
        bool isActive = _activeWorkflowStepId == stepId;

        var pill = new Border
        {
            Background = Application.Current.TryFindResource("BgPrimaryBrush") as Brush ?? Brushes.Black,
            BorderBrush = isActive 
                ? (Application.Current.TryFindResource("WorkflowBrush") as Brush ?? Brushes.Purple)
                : (Application.Current.TryFindResource("BorderSubtleBrush") as Brush ?? Brushes.Gray),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Height = 20,
            Padding = new Thickness(12, 0, 12, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Center,
            Tag = stepId
        };

        var text = new TextBlock
        {
            Text = $"+ {label}",
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = isActive
                ? (Application.Current.TryFindResource("WorkflowBrush") as Brush ?? Brushes.Purple)
                : (Application.Current.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        pill.Child = text;

        pill.MouseEnter += (s, e) =>
        {
            pill.Background = Application.Current.TryFindResource("BgSecondaryBrush") as Brush ?? Brushes.DarkSlateGray;
            pill.BorderBrush = Application.Current.TryFindResource("WorkflowBrush") as Brush ?? Brushes.Purple;
            pill.BorderThickness = new Thickness(1.5);
            text.Foreground = Application.Current.TryFindResource("WorkflowBrush") as Brush ?? Brushes.Purple;
        };

        pill.MouseLeave += (s, e) =>
        {
            pill.Background = Application.Current.TryFindResource("BgPrimaryBrush") as Brush ?? Brushes.Black;
            pill.BorderThickness = new Thickness(1);
            bool active = _activeWorkflowStepId == stepId;
            if (active)
            {
                pill.BorderBrush = Application.Current.TryFindResource("WorkflowBrush") as Brush ?? Brushes.Purple;
                text.Foreground = Application.Current.TryFindResource("WorkflowBrush") as Brush ?? Brushes.Purple;
            }
            else
            {
                pill.BorderBrush = Application.Current.TryFindResource("BorderSubtleBrush") as Brush ?? Brushes.Gray;
                text.Foreground = Application.Current.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;
            }
        };

        pill.MouseLeftButtonDown += (s, e) =>
        {
            e.Handled = true;
            ShowAddStepContextMenu(pill, insertIndex);
        };

        return pill;
    }

    private void ShowAddStepContextMenu(FrameworkElement target, int insertIndex)
    {
        var menu = new ContextMenu();
        var types = new (string Title, WorkflowStepType Type, string Icon)[]
        {
            ("Prompt for User Input", WorkflowStepType.Prompt, "💬"),
            ("Open URL / Web Page", WorkflowStepType.OpenUrl, "🌐"),
            ("Launch Application / Command", WorkflowStepType.LaunchApp, "⚡"),
            ("Ensure Folder Exists", WorkflowStepType.EnsureDirectory, "📁"),
            ("Paste / Insert Text Snippet", WorkflowStepType.InjectSnippet, "📝"),
            ("Delay / Pause Execution", WorkflowStepType.Delay, "⏱"),
            ("Execute Action / Folder", WorkflowStepType.ExecuteAction, "⚡"),
            ("Inline JavaScript", WorkflowStepType.RunScript, "📜")
        };

        foreach (var (title, stepType, icon) in types)
        {
            var item = new MenuItem
            {
                Header = $"{icon}  {title}",
                FontSize = 12
            };
            item.Click += (s, e) =>
            {
                InsertStepAtIndex(stepType, insertIndex);
            };
            menu.Items.Add(item);
        }

        menu.PlacementTarget = target;
        menu.IsOpen = true;
    }

    private void InsertStepAtIndex(WorkflowStepType stepType, int insertIndex)
    {
        if (_selectedItem == null) return;
        _selectedItem.Payload.WorkflowSteps ??= [];

        var step = CreateDefaultStep(stepType);
        insertIndex = Math.Clamp(insertIndex, 0, _selectedItem.Payload.WorkflowSteps.Count);
        _selectedItem.Payload.WorkflowSteps.Insert(insertIndex, step);

        RebuildWorkflowStepCards(step.Id);
        OnFormEdited();
    }

    private static WorkflowStep CreateDefaultStep(WorkflowStepType stepType)
    {
        var step = new WorkflowStep
        {
            StepType = stepType,
            Name = GetStepTypeIconAndName(stepType),
            IsCollapsed = false
        };

        if (stepType == WorkflowStepType.Prompt)
        {
            step.PromptTitle = "User Input Required";
            step.PromptSubtitle = "Provide the values below to proceed:";
            step.PromptFields = [
                new WorkflowPromptField
                {
                    VariableName = "input",
                    Label = "Enter value",
                    Type = TokenType.PromptText
                }
            ];
            step.VariableName = "input";
            step.PromptLabel = "Enter value";
        }
        else if (stepType == WorkflowStepType.OpenUrl)
        {
            step.Url = "https://";
        }
        else if (stepType == WorkflowStepType.Delay)
        {
            step.DelayMs = 1000;
        }
        else if (stepType == WorkflowStepType.ExecuteAction)
        {
            step.Name = "Execute Action";
        }

        return step;
    }

    private Border CreateStepCard(WorkflowStep step, int stepIndex, bool isFirst, bool isLast, List<string> availableVariables)
    {
        var card = new Border
        {
            Background = Application.Current.TryFindResource("CardBgBrush") as Brush ?? Brushes.DarkSlateGray,
            BorderBrush = Application.Current.TryFindResource("CardBorderBrush") as Brush ?? Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 2, 0, 2)
        };

        if (ThemeManager.CurrentTheme == AppTheme.Light)
        {
            card.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 8,
                ShadowDepth = 1.5,
                Opacity = 0.08,
                Direction = 270,
                Color = Colors.Black
            };
        }

        card.PreviewMouseDown += (s, e) => SetActiveWorkflowStep(step.Id);
        card.GotFocus += (s, e) => SetActiveWorkflowStep(step.Id);

        var mainStack = new StackPanel();
        card.Child = mainStack;

        // Card Header
        var headerGrid = new Grid 
        { 
            Margin = new Thickness(0, 0, 0, step.IsCollapsed ? 0 : 8),
            Background = Brushes.Transparent
        };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Left Header: Index badge + Type icon + Summary badge
        var leftHeader = new StackPanel 
        { 
            Orientation = Orientation.Horizontal, 
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Double-click to " + (step.IsCollapsed ? "expand" : "collapse")
        };

        // Index badge: Prominent purple pill sequence indicator
        var indexBadge = new Border
        {
            Background = Application.Current.TryFindResource("WorkflowSubtleBrush") as Brush ?? new SolidColorBrush(Color.FromArgb(0x28, 0xA8, 0x55, 0xF7)),
            BorderBrush = Application.Current.TryFindResource("WorkflowBrush") as Brush ?? Brushes.Purple,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(13),
            MinWidth = 26,
            Height = 26,
            Padding = new Thickness(6, 0, 6, 0),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        indexBadge.Child = new TextBlock
        {
            Text = $"{stepIndex}",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Application.Current.TryFindResource("WorkflowBrush") as Brush ?? Brushes.Purple
        };
        leftHeader.Children.Add(indexBadge);

        // Step Name / Type Title
        var typeName = new TextBlock
        {
            Text = GetStepTypeIconAndName(step.StepType),
            FontWeight = FontWeights.SemiBold,
            FontSize = 12.5,
            Foreground = Application.Current.TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        leftHeader.Children.Add(typeName);

        // Live Summary Tag
        var summaryText = new TextBlock
        {
            Text = GetStepLiveSummary(step),
            FontSize = 11.5,
            Foreground = Application.Current.TryFindResource("TextMutedBrush") as Brush ?? Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 340
        };
        leftHeader.Children.Add(summaryText);

        Grid.SetColumn(leftHeader, 0);
        headerGrid.Children.Add(leftHeader);

        // Right Header: ▲/▼ reorder + ⋮ kebab menu
        var rightHeader = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        // Move Up
        var upBtn = new Button
        {
            Content = "▲",
            FontSize = 10,
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 3, 0),
            IsEnabled = !isFirst,
            Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
            ToolTip = "Move step up"
        };
        upBtn.Click += (s, e) => MoveStep(step, -1);
        rightHeader.Children.Add(upBtn);

        // Move Down
        var downBtn = new Button
        {
            Content = "▼",
            FontSize = 10,
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 6, 0),
            IsEnabled = !isLast,
            Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
            ToolTip = "Move step down"
        };
        downBtn.Click += (s, e) => MoveStep(step, 1);
        rightHeader.Children.Add(downBtn);

        // ⋮ Kebab overflow menu
        var kebabMenu = new ContextMenu();

        var testItem = new MenuItem { Header = "▶  Test Step", FontSize = 12 };
        testItem.Click += async (s, e) => await TestSingleWorkflowStepAsync(step, stepIndex);
        kebabMenu.Items.Add(testItem);

        var dupItem = new MenuItem { Header = "⧉  Duplicate", FontSize = 12 };
        dupItem.Click += (s, e) => DuplicateStep(step);
        kebabMenu.Items.Add(dupItem);

        // Convert to Inline Script — hidden for RunScript steps (already a script)
        if (step.StepType != WorkflowStepType.RunScript)
        {
            var convertItem = new MenuItem { Header = "📜  Convert to Inline Script", FontSize = 12 };
            convertItem.Click += (s, e) => ConvertStepToScript(step);
            kebabMenu.Items.Add(convertItem);
        }

        kebabMenu.Items.Add(new Separator());

        var deleteItem = new MenuItem
        {
            Header = "🗑  Delete",
            FontSize = 12,
            Foreground = Application.Current.TryFindResource("ErrorBrush") as Brush ?? Brushes.Red
        };
        deleteItem.Click += (s, e) => DeleteStep(step);
        kebabMenu.Items.Add(deleteItem);

        var kebabBtn = new Button
        {
            Content = "⋮",
            FontSize = 14,
            Padding = new Thickness(7, 1, 7, 1),
            Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
            ToolTip = "Step actions",
            ContextMenu = kebabMenu
        };
        kebabBtn.Click += (s, e) =>
        {
            kebabMenu.PlacementTarget = kebabBtn;
            kebabMenu.IsOpen = true;
        };
        rightHeader.Children.Add(kebabBtn);

        Grid.SetColumn(rightHeader, 1);
        headerGrid.Children.Add(rightHeader);

        headerGrid.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ClickCount == 2)
            {
                if (e.OriginalSource is DependencyObject dep && rightHeader.IsAncestorOf(dep))
                {
                    return;
                }
                e.Handled = true;
                step.IsCollapsed = !step.IsCollapsed;
                RebuildWorkflowStepCards();
            }
        };

        mainStack.Children.Add(headerGrid);

        // If collapsed, only show the header
        if (step.IsCollapsed)
        {
            return card;
        }

        // Expanded Card Body
        var bodyBorder = new Border
        {
            Background = Application.Current.TryFindResource("BgPrimaryBrush") as Brush ?? Brushes.Black,
            BorderBrush = Application.Current.TryFindResource("BorderSubtleBrush") as Brush ?? Brushes.DarkGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 4)
        };
        var bodyStack = new StackPanel();
        bodyBorder.Child = bodyStack;

        // Step Type Specific Controls
        PopulateStepTypeControls(bodyStack, step, summaryText, availableVariables);

        mainStack.Children.Add(bodyBorder);

        // Error handling row: Both label and dropdown right-aligned together (omitted on the last step)
        if (!isLast)
        {
            var errorRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 2)
            };

            var errorLabel = new TextBlock
            {
                Text = "If this step fails or is cancelled:",
                FontSize = 11.5,
                Foreground = Application.Current.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            errorRow.Children.Add(errorLabel);

            var errorCombo = new ComboBox
            {
                Height = 28,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            errorCombo.Items.Add(new ComboBoxItem { Content = "Stop Workflow (Default)" });
            errorCombo.Items.Add(new ComboBoxItem { Content = "Continue to Next Step" });
            errorCombo.SelectedIndex = step.OnError == StepErrorPolicy.Continue ? 1 : 0;
            errorCombo.SelectionChanged += (s, e) =>
            {
                step.OnError = errorCombo.SelectedIndex == 1 ? StepErrorPolicy.Continue : StepErrorPolicy.StopWorkflow;
                OnFormEdited();
            };
            errorRow.Children.Add(errorCombo);
            mainStack.Children.Add(errorRow);
        }

        return card;
    }

    private void PopulateStepTypeControls(StackPanel container, WorkflowStep step, TextBlock summaryText, List<string> availableVariables)
    {
        switch (step.StepType)
        {
            case WorkflowStepType.Prompt:
            {
                // Ensure PromptFields is initialized
                if (step.PromptFields == null || step.PromptFields.Count == 0)
                {
                    step.PromptFields = [
                        new WorkflowPromptField
                        {
                            VariableName = string.IsNullOrWhiteSpace(step.VariableName) ? "input" : step.VariableName,
                            Label = string.IsNullOrWhiteSpace(step.PromptLabel) ? "Enter value" : step.PromptLabel,
                            DefaultValue = step.PromptDefaultValue,
                            Type = step.PromptType,
                            Choices = step.PromptChoices
                        }
                    ];
                }

                // Dialog Header Configuration (Title & Subtitle)
                var headerCard = new Border
                {
                    Background = Application.Current.TryFindResource("CardHeaderBgBrush") as Brush ?? Brushes.Transparent,
                    BorderBrush = Application.Current.TryFindResource("CardBorderBrush") as Brush ?? Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 0, 0, 10)
                };

                var headerGrid = new Grid();
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12, GridUnitType.Pixel) });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Dialog Title
                var titleStack = new StackPanel();
                titleStack.Children.Add(new TextBlock { Text = "Dialog Title (Optional)", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
                var titleBox = new TextBox
                {
                    Text = step.PromptTitle,
                    Height = 32,
                    Padding = new Thickness(8, 3, 8, 3),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Style = Application.Current.TryFindResource("ModernTextBoxStyle") as Style,
                    ToolTip = "Custom window title for the prompt dialog (defaults to 'Parameters Required' if left blank)"
                };
                titleBox.TextChanged += (s, e) =>
                {
                    step.PromptTitle = titleBox.Text;
                    OnFormEdited();
                };
                titleStack.Children.Add(titleBox);
                Grid.SetColumn(titleStack, 0);
                headerGrid.Children.Add(titleStack);

                // Dialog Subtitle
                var subStack = new StackPanel();
                subStack.Children.Add(new TextBlock { Text = "Instructions / Subtitle (Optional)", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
                var subBox = new TextBox
                {
                    Text = step.PromptSubtitle,
                    Height = 32,
                    Padding = new Thickness(8, 3, 8, 3),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Style = Application.Current.TryFindResource("ModernTextBoxStyle") as Style,
                    ToolTip = "Explanatory instructions displayed below the dialog title"
                };
                subBox.TextChanged += (s, e) =>
                {
                    step.PromptSubtitle = subBox.Text;
                    OnFormEdited();
                };
                subStack.Children.Add(subBox);
                Grid.SetColumn(subStack, 2);
                headerGrid.Children.Add(subStack);

                headerCard.Child = headerGrid;
                container.Children.Add(headerCard);

                // Fields List Section
                var fieldsHeaderGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                fieldsHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                fieldsHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var fieldsLabel = new TextBlock
                {
                    Text = $"Prompt Input Fields ({step.PromptFields.Count})",
                    FontWeight = FontWeights.Bold,
                    FontSize = 11.5,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(fieldsLabel, 0);
                fieldsHeaderGrid.Children.Add(fieldsLabel);

                // Merge into existing prompt button (if other prompt steps exist in this workflow)
                var otherPromptSteps = _selectedItem?.Payload?.WorkflowSteps?
                    .Where(s => s.Id != step.Id && s.StepType == WorkflowStepType.Prompt)
                    .ToList();

                if (otherPromptSteps != null && otherPromptSteps.Count > 0)
                {
                    var mergeBtn = new Button
                    {
                        Content = "⤿ Merge into another prompt...",
                        Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
                        FontSize = 11,
                        Padding = new Thickness(8, 3, 8, 3),
                        ToolTip = "Consolidate this prompt's fields into another prompt step and remove this card"
                    };

                    mergeBtn.Click += (s, e) =>
                    {
                        var menu = new ContextMenu();
                        for (int idx = 0; idx < otherPromptSteps.Count; idx++)
                        {
                            var target = otherPromptSteps[idx];
                            int targetStepIndex = (_selectedItem?.Payload?.WorkflowSteps?.IndexOf(target) ?? -1) + 1;
                            string targetDesc = !string.IsNullOrWhiteSpace(target.PromptTitle) 
                                ? target.PromptTitle 
                                : (!string.IsNullOrWhiteSpace(target.VariableName) ? target.VariableName : $"Step {targetStepIndex}");

                            var mi = new MenuItem
                            {
                                Header = $"Step #{targetStepIndex}: {targetDesc} ({target.PromptFields.Count} field{(target.PromptFields.Count == 1 ? "" : "s")})"
                            };
                            mi.Click += (ms, me) =>
                            {
                                // Transfer all fields into target
                                target.PromptFields.AddRange(step.PromptFields);
                                _selectedItem?.Payload.WorkflowSteps.Remove(step);
                                RebuildWorkflowStepCards(target.Id);
                                OnFormEdited();
                            };
                            menu.Items.Add(mi);
                        }
                        menu.PlacementTarget = mergeBtn;
                        menu.IsOpen = true;
                    };

                    Grid.SetColumn(mergeBtn, 1);
                    fieldsHeaderGrid.Children.Add(mergeBtn);
                }

                container.Children.Add(fieldsHeaderGrid);

                // Render each field
                for (int fIndex = 0; fIndex < step.PromptFields.Count; fIndex++)
                {
                    var field = step.PromptFields[fIndex];
                    int fieldDisplayNum = fIndex + 1;

                    var fieldCard = new Border
                    {
                        Background = Application.Current.TryFindResource("BgInputBrush") as Brush ?? Brushes.Transparent,
                        BorderBrush = Application.Current.TryFindResource("BorderSubtleBrush") as Brush ?? Brushes.Gray,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(10, 8, 10, 8),
                        Margin = new Thickness(0, 0, 0, 8)
                    };

                    var fieldStack = new StackPanel();

                    // Field Header (Field #, Reorder buttons, Delete button)
                    var fieldTopGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                    fieldTopGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    fieldTopGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var fieldNumText = new TextBlock
                    {
                        Text = $"Field #{fieldDisplayNum}: {field.VariableName}",
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Application.Current.TryFindResource("WorkflowBrush") as Brush ?? Brushes.Purple,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(fieldNumText, 0);
                    fieldTopGrid.Children.Add(fieldNumText);

                    var fieldActionsPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

                    if (step.PromptFields.Count > 1)
                    {
                        int currentIdx = fIndex;
                        var fieldUpBtn = new Button
                        {
                            Content = "▲",
                            FontSize = 10,
                            Padding = new Thickness(6, 2, 6, 2),
                            Margin = new Thickness(0, 0, 4, 0),
                            IsEnabled = currentIdx > 0,
                            Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
                            ToolTip = "Move field up"
                        };
                        fieldUpBtn.Click += (s, e) =>
                        {
                            if (currentIdx > 0)
                            {
                                (step.PromptFields[currentIdx], step.PromptFields[currentIdx - 1]) = 
                                    (step.PromptFields[currentIdx - 1], step.PromptFields[currentIdx]);
                                SyncPrimaryPromptField(step);
                                RebuildWorkflowStepCards(step.Id);
                                OnFormEdited();
                            }
                        };
                        fieldActionsPanel.Children.Add(fieldUpBtn);

                        var fieldDownBtn = new Button
                        {
                            Content = "▼",
                            FontSize = 10,
                            Padding = new Thickness(6, 2, 6, 2),
                            Margin = new Thickness(0, 0, 6, 0),
                            IsEnabled = currentIdx < step.PromptFields.Count - 1,
                            Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
                            ToolTip = "Move field down"
                        };
                        fieldDownBtn.Click += (s, e) =>
                        {
                            if (currentIdx < step.PromptFields.Count - 1)
                            {
                                (step.PromptFields[currentIdx], step.PromptFields[currentIdx + 1]) = 
                                    (step.PromptFields[currentIdx + 1], step.PromptFields[currentIdx]);
                                SyncPrimaryPromptField(step);
                                RebuildWorkflowStepCards(step.Id);
                                OnFormEdited();
                            }
                        };
                        fieldActionsPanel.Children.Add(fieldDownBtn);

                        var delFieldBtn = new Button
                        {
                            Content = "✕ Delete Field",
                            FontSize = 10,
                            Padding = new Thickness(6, 2, 6, 2),
                            Foreground = Application.Current.TryFindResource("ErrorBrush") as Brush ?? Brushes.Red,
                            Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
                            ToolTip = "Remove this input field from the prompt"
                        };
                        delFieldBtn.Click += (s, e) =>
                        {
                            step.PromptFields.Remove(field);
                            SyncPrimaryPromptField(step);
                            RebuildWorkflowStepCards(step.Id);
                            OnFormEdited();
                        };
                        fieldActionsPanel.Children.Add(delFieldBtn);
                    }

                    Grid.SetColumn(fieldActionsPanel, 1);
                    fieldTopGrid.Children.Add(fieldActionsPanel);

                    fieldStack.Children.Add(fieldTopGrid);

                    // Row 1: Variable Name & Input Type
                    var r1Grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                    r1Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    r1Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12, GridUnitType.Pixel) });
                    r1Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    var fVarStack = new StackPanel();
                    fVarStack.Children.Add(new TextBlock { Text = "Variable Name", FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
                    var fVarBox = new TextBox
                    {
                        Text = field.VariableName,
                        Height = 30,
                        Padding = new Thickness(6, 2, 6, 2),
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Style = Application.Current.TryFindResource("ModernTextBoxStyle") as Style
                    };
                    fVarBox.TextChanged += (s, e) =>
                    {
                        field.VariableName = fVarBox.Text.Trim();
                        fieldNumText.Text = $"Field #{fieldDisplayNum}: {field.VariableName}";
                        SyncPrimaryPromptField(step);
                        summaryText.Text = GetStepLiveSummary(step);
                        OnFormEdited();
                    };
                    fVarStack.Children.Add(fVarBox);
                    Grid.SetColumn(fVarStack, 0);
                    r1Grid.Children.Add(fVarStack);

                    var fTypeStack = new StackPanel();
                    fTypeStack.Children.Add(new TextBlock { Text = "Field Type", FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
                    var fTypeCombo = new ComboBox { Height = 30, FontSize = 11.5 };
                    fTypeCombo.Items.Add(new ComboBoxItem { Content = "Single-Line Text", Tag = TokenType.PromptText });
                    fTypeCombo.Items.Add(new ComboBoxItem { Content = "Multi-Line Text", Tag = TokenType.PromptMultiline });
                    fTypeCombo.Items.Add(new ComboBoxItem { Content = "Dropdown Choice List", Tag = TokenType.PromptChoice });
                    fTypeCombo.Items.Add(new ComboBoxItem { Content = "Numeric Input", Tag = TokenType.PromptNumber });
                    fTypeCombo.Items.Add(new ComboBoxItem { Content = "Date Picker", Tag = TokenType.PromptDatePicker });
                    fTypeCombo.SelectedIndex = field.Type switch
                    {
                        TokenType.PromptMultiline => 1,
                        TokenType.PromptChoice => 2,
                        TokenType.PromptNumber => 3,
                        TokenType.PromptDatePicker => 4,
                        _ => 0
                    };
                    fTypeCombo.SelectionChanged += (s, e) =>
                    {
                        field.Type = fTypeCombo.SelectedIndex switch
                        {
                            1 => TokenType.PromptMultiline,
                            2 => TokenType.PromptChoice,
                            3 => TokenType.PromptNumber,
                            4 => TokenType.PromptDatePicker,
                            _ => TokenType.PromptText
                        };
                        SyncPrimaryPromptField(step);
                        RebuildWorkflowStepCards(step.Id);
                        OnFormEdited();
                    };
                    fTypeStack.Children.Add(fTypeCombo);
                    Grid.SetColumn(fTypeStack, 2);
                    r1Grid.Children.Add(fTypeStack);
                    fieldStack.Children.Add(r1Grid);

                    // Row 2: Field Prompt Label
                    var fLabelStack = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
                    fLabelStack.Children.Add(new TextBlock { Text = "Prompt Label / Question", FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
                    var fLabelBox = new TextBox
                    {
                        Text = field.Label,
                        Height = 30,
                        Padding = new Thickness(6, 2, 6, 2),
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Style = Application.Current.TryFindResource("ModernTextBoxStyle") as Style
                    };
                    fLabelBox.TextChanged += (s, e) =>
                    {
                        field.Label = fLabelBox.Text;
                        SyncPrimaryPromptField(step);
                        OnFormEdited();
                    };
                    fLabelStack.Children.Add(fLabelBox);
                    fieldStack.Children.Add(fLabelStack);

                    // Row 3: Default Value
                    var fDefStack = new StackPanel { Margin = new Thickness(0, 0, 0, (field.Type == TokenType.PromptChoice || field.Type == TokenType.PromptNumber || field.Type == TokenType.PromptDatePicker) ? 6 : 0) };
                    fDefStack.Children.Add(new TextBlock { Text = "Default Value (Pre-populated, optional)", FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
                    var fDefBox = new TextBox
                    {
                        Text = field.DefaultValue,
                        Height = 30,
                        Padding = new Thickness(6, 2, 6, 2),
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Style = Application.Current.TryFindResource("ModernTextBoxStyle") as Style
                    };
                    fDefBox.TextChanged += (s, e) =>
                    {
                        field.DefaultValue = fDefBox.Text;
                        SyncPrimaryPromptField(step);
                        OnFormEdited();
                    };
                    fDefStack.Children.Add(fDefBox);
                    fieldStack.Children.Add(fDefStack);

                    // Row 4 (if Choice): Comma separated choices
                    if (field.Type == TokenType.PromptChoice)
                    {
                        var fChoiceStack = new StackPanel();
                        fChoiceStack.Children.Add(new TextBlock { Text = "Comma-Separated Choices (e.g. WPF, Web, CLI)", FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
                        var fChoiceBox = new TextBox
                        {
                            Text = field.Choices,
                            Height = 30,
                            Padding = new Thickness(6, 2, 6, 2),
                            VerticalContentAlignment = VerticalAlignment.Center,
                            Style = Application.Current.TryFindResource("ModernTextBoxStyle") as Style
                        };
                        fChoiceBox.TextChanged += (s, e) =>
                        {
                            field.Choices = fChoiceBox.Text;
                            SyncPrimaryPromptField(step);
                            OnFormEdited();
                        };
                        fChoiceStack.Children.Add(fChoiceBox);
                        fieldStack.Children.Add(fChoiceStack);
                    }

                    // Row 5 (if Number): Optional Min and Max bounds
                    if (field.Type == TokenType.PromptNumber)
                    {
                        var minMaxGrid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
                        minMaxGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        minMaxGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12, GridUnitType.Pixel) });
                        minMaxGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                        var minStack = new StackPanel();
                        minStack.Children.Add(new TextBlock { Text = "Minimum Value (optional)", FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
                        var minBox = new TextBox
                        {
                            Text = field.MinNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                            Height = 30,
                            Padding = new Thickness(6, 2, 6, 2),
                            VerticalContentAlignment = VerticalAlignment.Center,
                            Style = Application.Current.TryFindResource("ModernTextBoxStyle") as Style
                        };
                        minBox.TextChanged += (s, e) =>
                        {
                            field.MinNumber = double.TryParse(minBox.Text.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedMin)
                                ? parsedMin : null;
                            SyncPrimaryPromptField(step);
                            OnFormEdited();
                        };
                        minStack.Children.Add(minBox);
                        Grid.SetColumn(minStack, 0);
                        minMaxGrid.Children.Add(minStack);

                        var maxStack = new StackPanel();
                        maxStack.Children.Add(new TextBlock { Text = "Maximum Value (optional)", FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
                        var maxBox = new TextBox
                        {
                            Text = field.MaxNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                            Height = 30,
                            Padding = new Thickness(6, 2, 6, 2),
                            VerticalContentAlignment = VerticalAlignment.Center,
                            Style = Application.Current.TryFindResource("ModernTextBoxStyle") as Style
                        };
                        maxBox.TextChanged += (s, e) =>
                        {
                            field.MaxNumber = double.TryParse(maxBox.Text.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedMax)
                                ? parsedMax : null;
                            SyncPrimaryPromptField(step);
                            OnFormEdited();
                        };
                        maxStack.Children.Add(maxBox);
                        Grid.SetColumn(maxStack, 2);
                        minMaxGrid.Children.Add(maxStack);

                        fieldStack.Children.Add(minMaxGrid);
                    }

                    // Row 6 (if DatePicker): Date format pattern
                    if (field.Type == TokenType.PromptDatePicker)
                    {
                        var dateFmtStack = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };
                        dateFmtStack.Children.Add(new TextBlock { Text = "Date Format (e.g. yyyy-MM-dd, MM/dd/yyyy, dddd, MMMM d)", FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
                        var dateFmtBox = new TextBox
                        {
                            Text = string.IsNullOrWhiteSpace(field.DateFormat) ? "yyyy-MM-dd" : field.DateFormat,
                            Height = 30,
                            Padding = new Thickness(6, 2, 6, 2),
                            VerticalContentAlignment = VerticalAlignment.Center,
                            Style = Application.Current.TryFindResource("ModernTextBoxStyle") as Style
                        };
                        dateFmtBox.TextChanged += (s, e) =>
                        {
                            field.DateFormat = string.IsNullOrWhiteSpace(dateFmtBox.Text) ? "yyyy-MM-dd" : dateFmtBox.Text.Trim();
                            SyncPrimaryPromptField(step);
                            OnFormEdited();
                        };
                        dateFmtStack.Children.Add(dateFmtBox);
                        fieldStack.Children.Add(dateFmtStack);
                    }

                    fieldCard.Child = fieldStack;
                    container.Children.Add(fieldCard);
                }

                // Add Field Button
                var addFieldBtn = new Button
                {
                    Content = "＋ Add Field to this Prompt",
                    Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(0, 2, 0, 4),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                addFieldBtn.Click += (s, e) =>
                {
                    step.PromptFields.Add(new WorkflowPromptField
                    {
                        VariableName = $"input{step.PromptFields.Count + 1}",
                        Label = "Enter value",
                        Type = TokenType.PromptText
                    });
                    RebuildWorkflowStepCards(step.Id);
                    OnFormEdited();
                };
                container.Children.Add(addFieldBtn);

                break;
            }

            case WorkflowStepType.OpenUrl:
            {
                var urlStack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
                urlStack.Children.Add(new TextBlock { Text = "Target Web URL", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
                var urlBox = new TextBox
                {
                    Text = step.Url,
                    Height = 34,
                    Padding = new Thickness(8, 4, 8, 4),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Style = Application.Current.TryFindResource("ModernTextBoxStyle") as Style
                };
                urlBox.TextChanged += (s, e) =>
                {
                    step.Url = urlBox.Text;
                    summaryText.Text = GetStepLiveSummary(step);
                    OnFormEdited();
                };
                urlStack.Children.Add(urlBox);
                container.Children.Add(urlStack);

                // Variable insertion chips
                RenderVariableChips(container, urlBox, availableVariables);

                // Browser & Profile Target Row
                var browserGrid = new Grid { Margin = new Thickness(0, 6, 0, 8) };
                browserGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                browserGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16, GridUnitType.Pixel) });
                browserGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Browser selector
                var browserStack = new StackPanel();
                browserStack.Children.Add(new TextBlock { Text = "Target Browser (Optional)", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
                var browserCombo = new ComboBox { Height = 34, FontSize = 12 };
                browserCombo.Items.Add(new ComboBoxItem { Content = "🌐 (System Default Browser)", Tag = "default" });

                var installedBrowsers = _browserDetectionService?.GetInstalledBrowsers() ?? [];
                foreach (var b in installedBrowsers)
                {
                    string icon = b.Kind switch
                    {
                        BrowserKind.Firefox => "🦊",
                        BrowserKind.Chromium => b.Id == "edge" ? "🌊" : b.Id == "brave" ? "🦁" : "🌐",
                        _ => "🌐"
                    };
                    browserCombo.Items.Add(new ComboBoxItem { Content = $"{icon} {b.Name}", Tag = b.Id });
                }

                // Profile selector
                var profileStack = new StackPanel();
                profileStack.Children.Add(new TextBlock { Text = "Target Profile (Optional)", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
                var profileCombo = new ComboBox { Height = 34, FontSize = 12 };

                void PopulateProfiles(string browserId)
                {
                    profileCombo.Items.Clear();
                    profileCombo.Items.Add(new ComboBoxItem { Content = "👤 (Default Profile)", Tag = "" });
                    if (!string.IsNullOrWhiteSpace(browserId) && !browserId.Equals("default", StringComparison.OrdinalIgnoreCase))
                    {
                        var profiles = _browserDetectionService?.GetProfiles(browserId) ?? [];
                        foreach (var p in profiles)
                        {
                            profileCombo.Items.Add(new ComboBoxItem { Content = $"👤 {p.DisplayName}", Tag = p.Id });
                        }
                    }

                    int profIdx = 0;
                    if (!string.IsNullOrWhiteSpace(step.BrowserProfile))
                    {
                        for (int i = 0; i < profileCombo.Items.Count; i++)
                        {
                            if (profileCombo.Items[i] is ComboBoxItem cbi && string.Equals(cbi.Tag?.ToString(), step.BrowserProfile, StringComparison.OrdinalIgnoreCase))
                            {
                                profIdx = i;
                                break;
                            }
                        }
                    }
                    profileCombo.SelectedIndex = profIdx;
                }

                int currentBrowserIdx = 0;
                if (!string.IsNullOrWhiteSpace(step.BrowserTarget))
                {
                    for (int i = 0; i < browserCombo.Items.Count; i++)
                    {
                        if (browserCombo.Items[i] is ComboBoxItem cbi && string.Equals(cbi.Tag?.ToString(), step.BrowserTarget, StringComparison.OrdinalIgnoreCase))
                        {
                            currentBrowserIdx = i;
                            break;
                        }
                    }
                }
                browserCombo.SelectedIndex = currentBrowserIdx;
                var currentBrowserTag = (browserCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "default";
                PopulateProfiles(currentBrowserTag);

                bool isDefaultBrowser = string.IsNullOrWhiteSpace(currentBrowserTag) || currentBrowserTag.Equals("default", StringComparison.OrdinalIgnoreCase);
                profileStack.Visibility = isDefaultBrowser ? Visibility.Collapsed : Visibility.Visible;

                browserCombo.SelectionChanged += (s, e) =>
                {
                    var selectedTag = (browserCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "default";
                    step.BrowserTarget = selectedTag.Equals("default", StringComparison.OrdinalIgnoreCase) ? null : selectedTag;
                    bool isDef = string.IsNullOrWhiteSpace(step.BrowserTarget);
                    profileStack.Visibility = isDef ? Visibility.Collapsed : Visibility.Visible;
                    PopulateProfiles(selectedTag);
                    step.BrowserProfile = (profileCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
                    summaryText.Text = GetStepLiveSummary(step);
                    OnFormEdited();
                };

                profileCombo.SelectionChanged += (s, e) =>
                {
                    var profTag = (profileCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
                    step.BrowserProfile = string.IsNullOrWhiteSpace(profTag) ? null : profTag;
                    summaryText.Text = GetStepLiveSummary(step);
                    OnFormEdited();
                };

                browserStack.Children.Add(browserCombo);
                profileStack.Children.Add(profileCombo);

                Grid.SetColumn(browserStack, 0);
                Grid.SetColumn(profileStack, 2);
                browserGrid.Children.Add(browserStack);
                browserGrid.Children.Add(profileStack);
                container.Children.Add(browserGrid);

                var newWindowCheck = new CheckBox
                {
                    Content = "Open in new window (instead of new tab)",
                    IsChecked = step.OpenInNewWindow,
                    FontSize = 12,
                    Margin = new Thickness(0, 2, 0, 4)
                };
                newWindowCheck.Checked += (s, e) => { step.OpenInNewWindow = true; OnFormEdited(); };
                newWindowCheck.Unchecked += (s, e) => { step.OpenInNewWindow = false; OnFormEdited(); };
                container.Children.Add(newWindowCheck);
                break;
            }

            case WorkflowStepType.EnsureDirectory:
            {
                var dirStack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
                dirStack.Children.Add(new TextBlock { Text = "Folder Directory Path", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var pathBox = new TextBox
                {
                    Text = step.DirectoryPath,
                    Height = 34,
                    Padding = new Thickness(8, 4, 8, 4),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Style = Application.Current.TryFindResource("ModernTextBoxStyle") as Style
                };
                pathBox.TextChanged += (s, e) =>
                {
                    step.DirectoryPath = pathBox.Text;
                    summaryText.Text = GetStepLiveSummary(step);
                    OnFormEdited();
                };
                Grid.SetColumn(pathBox, 0);
                grid.Children.Add(pathBox);

                var browseBtn = new Button
                {
                    Content = "Browse...",
                    Height = 34,
                    Padding = new Thickness(10, 4, 10, 4),
                    Margin = new Thickness(6, 0, 0, 0),
                    Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style
                };
                browseBtn.Click += (s, e) =>
                {
                    var dlg = new OpenFolderDialog { Title = "Select Directory" };
                    if (dlg.ShowDialog() == true)
                    {
                        pathBox.Text = dlg.FolderName;
                    }
                };
                Grid.SetColumn(browseBtn, 1);
                grid.Children.Add(browseBtn);
                dirStack.Children.Add(grid);
                container.Children.Add(dirStack);

                // Variable insertion chips
                RenderVariableChips(container, pathBox, availableVariables);

                // Creation Policy Row
                var policyGrid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                policyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                policyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16, GridUnitType.Pixel) });
                policyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var policyStack = new StackPanel();
                policyStack.Children.Add(new TextBlock { Text = "If Directory Does Not Exist", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
                var policyCombo = new ComboBox { Height = 34, FontSize = 12 };
                policyCombo.Items.Add(new ComboBoxItem { Content = "Ask user to create it (Confirmation Dialog)" });
                policyCombo.Items.Add(new ComboBoxItem { Content = "Create directory automatically (Silently)" });
                policyCombo.Items.Add(new ComboBoxItem { Content = "Fail / Abort workflow" });
                policyCombo.SelectedIndex = step.DirectoryMissingPolicy switch
                {
                    DirectoryMissingPolicy.CreateSilently => 1,
                    DirectoryMissingPolicy.Fail => 2,
                    _ => 0
                };
                policyCombo.SelectionChanged += (s, e) =>
                {
                    step.DirectoryMissingPolicy = policyCombo.SelectedIndex switch
                    {
                        1 => DirectoryMissingPolicy.CreateSilently,
                        2 => DirectoryMissingPolicy.Fail,
                        _ => DirectoryMissingPolicy.PromptToCreate
                    };
                    OnFormEdited();
                };
                policyStack.Children.Add(policyCombo);
                Grid.SetColumn(policyStack, 0);
                policyGrid.Children.Add(policyStack);

                // Open in Explorer checkbox
                var openStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
                var openCheck = new CheckBox
                {
                    Content = "Open folder in Windows Explorer after check",
                    IsChecked = step.OpenInExplorer,
                    FontSize = 12
                };
                openCheck.Checked += (s, e) => { step.OpenInExplorer = true; OnFormEdited(); };
                openCheck.Unchecked += (s, e) => { step.OpenInExplorer = false; OnFormEdited(); };
                openStack.Children.Add(openCheck);
                Grid.SetColumn(openStack, 2);
                policyGrid.Children.Add(openStack);

                container.Children.Add(policyGrid);
                break;
            }

            case WorkflowStepType.LaunchApp:
            {
                var cmdStack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
                cmdStack.Children.Add(new TextBlock { Text = "Application, File or Script Path", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var cmdBox = new TextBox
                {
                    Text = step.Command,
                    Height = 34,
                    Padding = new Thickness(8, 4, 8, 4),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Style = Application.Current.TryFindResource("ModernTextBoxStyle") as Style
                };
                cmdBox.TextChanged += (s, e) =>
                {
                    step.Command = cmdBox.Text;
                    summaryText.Text = GetStepLiveSummary(step);
                    OnFormEdited();
                };
                Grid.SetColumn(cmdBox, 0);
                grid.Children.Add(cmdBox);

                var browseBtn = new Button
                {
                    Content = "Browse...",
                    Height = 34,
                    Padding = new Thickness(10, 4, 10, 4),
                    Margin = new Thickness(6, 0, 0, 0),
                    Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style
                };
                browseBtn.Click += (s, e) =>
                {
                    var dlg = new OpenFileDialog
                    {
                        Title = "Select Application or Script",
                        Filter = "Applications & Scripts (*.exe;*.lnk;*.bat;*.cmd;*.ps1)|*.exe;*.lnk;*.bat;*.cmd;*.ps1|All Files (*.*)|*.*",
                        InitialDirectory = GetInitialExecutableDirectory()
                    };
                    if (dlg.ShowDialog() == true)
                    {
                        cmdBox.Text = dlg.FileName;
                    }
                };
                Grid.SetColumn(browseBtn, 1);
                grid.Children.Add(browseBtn);
                cmdStack.Children.Add(grid);
                container.Children.Add(cmdStack);

                RenderVariableChips(container, cmdBox, availableVariables);

                // Arguments Box
                var argsStack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
                argsStack.Children.Add(new TextBlock { Text = "Command Arguments (Supports variables)", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
                var argsBox = new TextBox
                {
                    Text = step.Arguments,
                    Height = 34,
                    Padding = new Thickness(8, 4, 8, 4),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Style = Application.Current.TryFindResource("ModernTextBoxStyle") as Style
                };
                argsBox.TextChanged += (s, e) =>
                {
                    step.Arguments = argsBox.Text;
                    OnFormEdited();
                };
                argsStack.Children.Add(argsBox);
                container.Children.Add(argsStack);

                RenderVariableChips(container, argsBox, availableVariables);

                // Working Directory Box
                var workStack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
                workStack.Children.Add(new TextBlock { Text = "Working Directory (Optional)", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
                var workBox = new TextBox
                {
                    Text = step.WorkingDirectory,
                    Height = 34,
                    Padding = new Thickness(8, 4, 8, 4),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Style = Application.Current.TryFindResource("ModernTextBoxStyle") as Style
                };
                workBox.TextChanged += (s, e) =>
                {
                    step.WorkingDirectory = workBox.Text;
                    OnFormEdited();
                };
                workStack.Children.Add(workBox);
                container.Children.Add(workStack);

                RenderVariableChips(container, workBox, availableVariables);

                // Run as Admin Checkbox
                var adminCheck = new CheckBox
                {
                    Content = "Run with Administrator Elevation",
                    IsChecked = step.RunAsAdmin,
                    FontSize = 12,
                    Margin = new Thickness(0, 4, 0, 6)
                };
                adminCheck.Checked += (s, e) => { step.RunAsAdmin = true; OnFormEdited(); };
                adminCheck.Unchecked += (s, e) => { step.RunAsAdmin = false; OnFormEdited(); };
                container.Children.Add(adminCheck);

                // Display Target Row
                var displayStack = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
                displayStack.Children.Add(new TextBlock { Text = "Display Target", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
                var displayCombo = CreateDisplayTargetComboBox(step.TargetDisplay, (val) =>
                {
                    step.TargetDisplay = val;
                    OnFormEdited();
                });
                displayStack.Children.Add(displayCombo);
                container.Children.Add(displayStack);
                break;
            }

            case WorkflowStepType.InjectSnippet:
            {
                var snipStack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
                snipStack.Children.Add(new TextBlock { Text = "Snippet Template (Expanded & Typed into target window)", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
                var snipBox = new TextBox
                {
                    Text = step.SnippetTemplate,
                    Height = 60,
                    Padding = new Thickness(8, 4, 8, 4),
                    TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = true,
                    VerticalContentAlignment = VerticalAlignment.Top,
                    Style = Application.Current.TryFindResource("ModernTextBoxStyle") as Style
                };
                snipBox.TextChanged += (s, e) =>
                {
                    step.SnippetTemplate = snipBox.Text;
                    summaryText.Text = GetStepLiveSummary(step);
                    OnFormEdited();
                };
                snipStack.Children.Add(snipBox);
                container.Children.Add(snipStack);

                RenderVariableChips(container, snipBox, availableVariables);
                break;
            }

            case WorkflowStepType.Delay:
            {
                var delayStack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
                delayStack.Children.Add(new TextBlock { Text = "Delay Duration (Milliseconds)", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
                var delayBox = new TextBox
                {
                    Text = step.DelayMs.ToString(),
                    Height = 34,
                    Width = 140,
                    Padding = new Thickness(8, 4, 8, 4),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Style = Application.Current.TryFindResource("ModernTextBoxStyle") as Style
                };
                delayBox.TextChanged += (s, e) =>
                {
                    if (int.TryParse(delayBox.Text, out int ms))
                    {
                        step.DelayMs = Math.Max(10, ms);
                        summaryText.Text = GetStepLiveSummary(step);
                        OnFormEdited();
                    }
                };
                delayStack.Children.Add(delayBox);
                container.Children.Add(delayStack);
                break;
            }

            case WorkflowStepType.RunScript:
            {
                var scriptStack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
                scriptStack.Children.Add(new TextBlock { Text = "Inline JavaScript Code", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });

                var editorHost = new Border
                {
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(4)
                };
                editorHost.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                editorHost.SetResourceReference(Border.BackgroundProperty, "BgInputBrush");

                var scriptEditor = new TextEditor
                {
                    SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("JavaScript"),
                    ShowLineNumbers = true,
                    FontSize = 12.5,
                    Height = 140,
                    Background = Brushes.Transparent,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };
                if (Application.Current.TryFindResource("CodeFont") is FontFamily codeFont)
                {
                    scriptEditor.FontFamily = codeFont;
                }
                scriptEditor.SetResourceReference(TextEditor.ForegroundProperty, "TextPrimaryBrush");
                scriptEditor.Text = step.InlineScript ?? string.Empty;

                scriptEditor.TextChanged += (s, e) =>
                {
                    step.InlineScript = scriptEditor.Text;
                    summaryText.Text = GetStepLiveSummary(step);
                    OnFormEdited();
                };

                editorHost.Child = scriptEditor;
                scriptStack.Children.Add(editorHost);
                container.Children.Add(scriptStack);
                break;
            }

            case WorkflowStepType.ExecuteAction:
            {
                var execStack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
                execStack.Children.Add(new TextBlock
                {
                    Text = "Target Action or Folder to Execute",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 3)
                });

                // Target Action / Folder Picker Row
                var pickerBorder = new Border
                {
                    Background = Application.Current.TryFindResource("BgPrimaryBrush") as Brush ?? Brushes.Black,
                    BorderBrush = Application.Current.TryFindResource("BorderBrush") as Brush ?? Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 6, 8, 6),
                    Height = 42
                };

                var pickerGrid = new Grid();
                pickerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                pickerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                pickerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var targetInfoStack = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var targetIconText = new TextBlock
                {
                    FontSize = 14,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var targetNameText = new TextBlock
                {
                    FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Application.Current.TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                var targetTypeBadge = new Border
                {
                    Background = Application.Current.TryFindResource("AccentSubtleBrush") as Brush ?? Brushes.DarkSlateGray,
                    BorderBrush = Application.Current.TryFindResource("BorderSubtleBrush") as Brush ?? Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var targetTypeText = new TextBlock
                {
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.Purple,
                    VerticalAlignment = VerticalAlignment.Center
                };
                targetTypeBadge.Child = targetTypeText;

                targetInfoStack.Children.Add(targetIconText);
                targetInfoStack.Children.Add(targetNameText);
                targetInfoStack.Children.Add(targetTypeBadge);
                pickerGrid.Children.Add(targetInfoStack);
                Grid.SetColumn(targetInfoStack, 0);

                void UpdateTargetDisplay(Guid? targetId)
                {
                    TriggerItem? targetItem = targetId.HasValue
                        ? _items.FirstOrDefault(i => i.Id == targetId.Value)
                        : null;

                    if (targetItem != null)
                    {
                        targetIconText.Text = targetItem.ActionType switch
                        {
                            ActionType.Folder => "📁",
                            ActionType.Workflow => "🧱",
                            ActionType.Shell => "⚡",
                            ActionType.Snippet => "📝",
                            _ => "🔹"
                        };
                        targetNameText.Text = targetItem.Name;
                        targetTypeText.Text = targetItem.ActionType == ActionType.Folder
                            ? (targetItem.PresentationMode == PresentationMode.Direct ? "Folder" : "Folder (Popup)")
                            : targetItem.ActionType.ToString();
                        targetTypeBadge.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        targetIconText.Text = "🔍";
                        targetNameText.Text = "-- None selected (Click Choose) --";
                        targetTypeBadge.Visibility = Visibility.Collapsed;
                    }
                }

                UpdateTargetDisplay(step.TargetItemId);

                var chooseBtn = new Button
                {
                    Content = "Choose...",
                    Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
                    Padding = new Thickness(10, 4, 10, 4),
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                chooseBtn.Click += (s, e) =>
                {
                    var dlg = new ActionPickerDialog(_items, _selectedItem?.Id, step.TargetItemId)
                    {
                        Owner = this
                    };
                    if (dlg.ShowDialog() == true && dlg.SelectedItem != null)
                    {
                        step.TargetItemId = dlg.SelectedItem.Id;
                        UpdateTargetDisplay(step.TargetItemId);
                        summaryText.Text = GetStepLiveSummary(step);
                        OnFormEdited();
                    }
                };
                pickerGrid.Children.Add(chooseBtn);
                Grid.SetColumn(chooseBtn, 1);

                var clearBtn = new Button
                {
                    Content = "✕",
                    Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
                    Padding = new Thickness(8, 4, 8, 4),
                    FontSize = 11.5,
                    ToolTip = "Clear selected target",
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                clearBtn.Click += (s, e) =>
                {
                    if (step.TargetItemId.HasValue)
                    {
                        step.TargetItemId = null;
                        UpdateTargetDisplay(null);
                        summaryText.Text = GetStepLiveSummary(step);
                        OnFormEdited();
                    }
                };
                pickerGrid.Children.Add(clearBtn);
                Grid.SetColumn(clearBtn, 2);

                pickerBorder.Child = pickerGrid;
                execStack.Children.Add(pickerBorder);

                var noteText = new TextBlock
                {
                    Text = "Executing a Folder will open its popup cursor menu or command palette. TriggerPoint automatically halts circular calls if an action calls itself.",
                    FontSize = 10.5,
                    Foreground = Application.Current.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                execStack.Children.Add(noteText);

                container.Children.Add(execStack);
                break;
            }
        }
    }

    private void RenderVariableChips(StackPanel container, TextBox targetBox, List<string> availableVariables)
    {
        if (availableVariables == null || availableVariables.Count == 0) return;

        var wrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };
        wrap.Children.Add(new TextBlock
        {
            Text = "Insert variable: ",
            FontSize = 10.5,
            Foreground = Application.Current.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        });

        foreach (var varName in availableVariables)
        {
            var pill = new Button
            {
                Content = $"+{{{varName}}}",
                Tag = $"{{{varName}}}",
                Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
                FontSize = 10.5,
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(2, 0, 2, 0),
                ToolTip = $"Insert {{{varName}}} into this field"
            };
            pill.Click += (s, e) =>
            {
                InsertTokenIntoBox(targetBox, $"{{{varName}}}");
            };
            wrap.Children.Add(pill);
        }

        container.Children.Add(wrap);
    }

    private static void InsertTokenIntoBox(TextBox box, string token)
    {
        int caret = box.SelectionStart;
        box.Text = box.Text.Insert(caret, token);
        box.SelectionStart = caret + token.Length;
        box.Focus();
    }

    private static string GetStepTypeIconAndName(WorkflowStepType stepType) => stepType switch
    {
        WorkflowStepType.Prompt => "💬 Prompt for Input",
        WorkflowStepType.OpenUrl => "🌐 Open Web URL",
        WorkflowStepType.EnsureDirectory => "📁 Ensure Folder Exists",
        WorkflowStepType.LaunchApp => "⚡ Launch Application",
        WorkflowStepType.InjectSnippet => "📝 Insert Snippet",
        WorkflowStepType.Delay => "⏱ Delay / Pause",
        WorkflowStepType.ExecuteAction => "⚡ Execute Action / Folder",
        WorkflowStepType.RunScript => "📜 Inline Script",
        _ => "Step"
    };

    private string GetStepLiveSummary(WorkflowStep step) => step.StepType switch
    {
        WorkflowStepType.Prompt => (step.PromptFields != null && step.PromptFields.Count > 0)
            ? (step.PromptFields.Count == 1 
                ? $"stores into ${{{step.PromptFields[0].VariableName}}}" 
                : $"{step.PromptFields.Count} fields: " + string.Join(", ", step.PromptFields.Select(f => $"${{{f.VariableName}}}")))
            : (string.IsNullOrWhiteSpace(step.VariableName) ? "No variable defined" : $"stores into ${{{step.VariableName}}}"),
        WorkflowStepType.OpenUrl => string.IsNullOrWhiteSpace(step.Url) 
            ? "No URL specified" 
            : !string.IsNullOrWhiteSpace(step.BrowserTarget)
                ? (!string.IsNullOrWhiteSpace(step.BrowserProfile) ? $"{step.Url} [{step.BrowserTarget}:{step.BrowserProfile}]" : $"{step.Url} [{step.BrowserTarget}]")
                : step.Url,
        WorkflowStepType.EnsureDirectory => string.IsNullOrWhiteSpace(step.DirectoryPath) ? "No directory path" : step.DirectoryPath,
        WorkflowStepType.LaunchApp => string.IsNullOrWhiteSpace(step.Command) ? "No command specified" : step.Command,
        WorkflowStepType.InjectSnippet => string.IsNullOrWhiteSpace(step.SnippetTemplate) ? "Empty snippet" : step.SnippetTemplate,
        WorkflowStepType.Delay => $"{step.DelayMs}ms",
        WorkflowStepType.ExecuteAction => step.TargetItemId.HasValue
            ? (_items?.FirstOrDefault(i => i.Id == step.TargetItemId.Value) is { } target ? $"Run: {target.Name} ({(target.ActionType == ActionType.Folder ? "Folder Menu" : target.ActionType.ToString())})" : "Target item not found")
            : "No action or folder selected",
        WorkflowStepType.RunScript => string.IsNullOrWhiteSpace(step.InlineScript) ? "Empty script" : "Custom JavaScript",
        _ => string.Empty
    };

    private void MoveStep(WorkflowStep step, int direction)
    {
        if (_selectedItem?.Payload.WorkflowSteps == null) return;
        var steps = _selectedItem.Payload.WorkflowSteps;
        int idx = steps.IndexOf(step);
        if (idx < 0) return;

        int newIdx = idx + direction;
        if (newIdx >= 0 && newIdx < steps.Count)
        {
            steps.RemoveAt(idx);
            steps.Insert(newIdx, step);
            RebuildWorkflowStepCards(step.Id);
            OnFormEdited();
        }
    }

    private void DuplicateStep(WorkflowStep step)
    {
        if (_selectedItem?.Payload.WorkflowSteps == null) return;
        var steps = _selectedItem.Payload.WorkflowSteps;
        int idx = steps.IndexOf(step);
        if (idx < 0) return;

        var cloned = step.Clone();
        steps.Insert(idx + 1, cloned);
        RebuildWorkflowStepCards(cloned.Id);
        OnFormEdited();
    }

    private void DeleteStep(WorkflowStep step)
    {
        if (_selectedItem?.Payload.WorkflowSteps == null) return;
        _selectedItem.Payload.WorkflowSteps.Remove(step);
        RebuildWorkflowStepCards();
        UpdateReturnToVisualBtnVisibility();
        OnFormEdited();
    }

    private void ConvertStepToScript(WorkflowStep step)
    {
        if (_selectedItem == null) return;

        bool confirmed = ModernMessageDialog.ShowConfirm(this,
            "Convert Step to Script",
            $"This step \"{step.Name}\" will be converted into an Inline Script.\n\nOnce saved, this conversion cannot be automatically reversed back into visual fields.\n\nDo you wish to proceed?",
            "📜 Convert to Script",
            "Cancel");
        if (!confirmed) return;

        var js = WorkflowStepCompiler.CompileSingleStep(step);

        step.StepType = WorkflowStepType.RunScript;
        step.InlineScript = js;

        RebuildWorkflowStepCards(step.Id);
        OnFormEdited();
    }

    private void AddStepBtn_Click(object sender, RoutedEventArgs e)
    {
        if (AddStepContextMenu != null)
        {
            AddStepContextMenu.PlacementTarget = AddStepBtn;
            AddStepContextMenu.IsOpen = true;
        }
    }

    private void AddStepMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem == null) return;
        _selectedItem.Payload.WorkflowSteps ??= [];

        var tag = (sender as MenuItem)?.Tag?.ToString() ?? "Prompt";
        var stepType = tag switch
        {
            "OpenUrl" => WorkflowStepType.OpenUrl,
            "EnsureDirectory" => WorkflowStepType.EnsureDirectory,
            "LaunchApp" => WorkflowStepType.LaunchApp,
            "InjectSnippet" => WorkflowStepType.InjectSnippet,
            "Delay" => WorkflowStepType.Delay,
            "ExecuteAction" => WorkflowStepType.ExecuteAction,
            "RunScript" => WorkflowStepType.RunScript,
            _ => WorkflowStepType.Prompt
        };

        var newStep = CreateDefaultStep(stepType);
        _selectedItem.Payload.WorkflowSteps.Add(newStep);
        RebuildWorkflowStepCards(newStep.Id);
        UpdateReturnToVisualBtnVisibility();
        OnFormEdited();
    }

    private async Task TestSingleWorkflowStepAsync(WorkflowStep step, int stepIndex)
    {
        if (_selectedItem == null) return;
        CommitCurrentFormChanges();

        StatusText.Text = $"Testing Step {stepIndex} ({step.StepType})...";

        try
        {
            bool success = false;
            if (_workflowExecutor != null)
            {
                success = await _workflowExecutor.ExecuteSingleStepAsync(step, _selectedItem);
            }
            else
            {
                var tempItem = _selectedItem.Clone();
                tempItem.Payload.WorkflowSteps = [step.Clone()];
                await _executor.ExecuteAsync(tempItem);
                success = true;
            }

            if (success)
            {
                StatusText.Text = $"Step {stepIndex} ({step.StepType}) test completed successfully.";
            }
            else
            {
                StatusText.Text = $"Step {stepIndex} ({step.StepType}) test stopped or cancelled.";
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to test step {StepIndex}", stepIndex);
            StatusText.Text = $"Step {stepIndex} test failed: {ex.Message}";
        }
    }

    private void ConvertToJsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem?.Payload.WorkflowSteps == null || _selectedItem.Payload.WorkflowSteps.Count == 0)
        {
            ModernMessageDialog.ShowAlert(this,
                "Convert to JavaScript",
                "There are no visual steps to convert. Add steps first.",
                ModernDialogType.Info);
            return;
        }

        if (!string.IsNullOrWhiteSpace(_selectedItem.Payload.ScriptSource))
        {
            // Existing script — show 3-option dialog
            var choice = ModernMessageDialog.ShowConvertJsDialog(this);
            switch (choice)
            {
                case ConvertJsChoice.Recompile:
                    // Replace the script with freshly compiled output
                    var js = WorkflowStepCompiler.CompileToJavaScript(_selectedItem.Payload.WorkflowSteps);
                    if (WorkflowScriptEditor != null) WorkflowScriptEditor.Text = js;
                    _selectedItem.Payload.ScriptSource = js;
                    SwitchToScriptMode();
                    OnFormEdited();
                    break;

                case ConvertJsChoice.KeepScript:
                    // Just switch to script view — no recompilation
                    SwitchToScriptMode();
                    OnFormEdited();
                    break;

                case ConvertJsChoice.Cancel:
                default:
                    return;
            }
        }
        else
        {
            // No existing script — compile immediately, no dialog needed
            var js = WorkflowStepCompiler.CompileToJavaScript(_selectedItem.Payload.WorkflowSteps);
            if (WorkflowScriptEditor != null) WorkflowScriptEditor.Text = js;
            _selectedItem.Payload.ScriptSource = js;
            SwitchToScriptMode();
            OnFormEdited();
        }

        UpdateReturnToVisualBtnVisibility();
    }

    private void WorkflowPresetsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (WorkflowPresetsMenu == null) return;
        WorkflowPresetsMenu.Items.Clear();

        var presets = WorkflowPresets.GetAll();

        // 1. General & Everyday Productivity Category
        var genHeader = new MenuItem
        {
            Header = "📁 General & Everyday Productivity",
            FontWeight = FontWeights.Bold,
            IsEnabled = false
        };
        WorkflowPresetsMenu.Items.Add(genHeader);

        foreach (var preset in presets.Where(p => p.Category == WorkflowPresets.CategoryGeneral))
        {
            var item = new MenuItem
            {
                Header = preset.Title,
                ToolTip = preset.Description,
                Tag = preset
            };
            item.Click += PresetMenuItem_Click;
            WorkflowPresetsMenu.Items.Add(item);
        }

        WorkflowPresetsMenu.Items.Add(new Separator());

        // 2. Developer & Advanced Category
        var devHeader = new MenuItem
        {
            Header = "⚡ Developer & Advanced Workspaces",
            FontWeight = FontWeights.Bold,
            IsEnabled = false
        };
        WorkflowPresetsMenu.Items.Add(devHeader);

        foreach (var preset in presets.Where(p => p.Category == WorkflowPresets.CategoryDeveloper))
        {
            var item = new MenuItem
            {
                Header = preset.Title,
                ToolTip = preset.Description,
                Tag = preset
            };
            item.Click += PresetMenuItem_Click;
            WorkflowPresetsMenu.Items.Add(item);
        }

        WorkflowPresetsMenu.PlacementTarget = WorkflowPresetsBtn;
        WorkflowPresetsMenu.IsOpen = true;
    }

    private void PresetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem == null || (sender as MenuItem)?.Tag is not WorkflowPreset preset) return;

        _selectedItem.Payload.WorkflowSteps = preset.Steps.ConvertAll(s => s.Clone());
        _selectedItem.Payload.WorkflowMode = WorkflowMode.Visual;
        SwitchToVisualMode();

        if (string.IsNullOrWhiteSpace(ItemNameBox.Text) || ItemNameBox.Text.StartsWith("New Action") || ItemNameBox.Text.StartsWith("New Workflow"))
        {
            ItemNameBox.Text = preset.Title;
            _selectedItem.Name = preset.Title;
        }

        RebuildWorkflowStepCards();
        UpdateReturnToVisualBtnVisibility();
        OnFormEdited();
    }

    private void ToggleApiDrawerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (WorkflowApiDrawer == null || WorkflowApiDrawerHost == null) return;

        bool isOpening = WorkflowApiDrawer.Visibility != Visibility.Visible;
        WorkflowApiDrawer.Visibility = isOpening ? Visibility.Visible : Visibility.Collapsed;

        if (isOpening)
        {
            PopulateApiReferenceDrawer();
        }
    }

    private void PopulateApiReferenceDrawer()
    {
        if (WorkflowApiDrawerHost == null) return;
        WorkflowApiDrawerHost.Children.Clear();

        var apis = new (string Category, string Name, string Desc, string Example)[]
        {
            ("🟢 General API", "tp.prompt(label, options)", "Displays an interactive popup asking the user for input. Returns the text entered, or null if cancelled.", "const ticket = await tp.prompt(\"Ticket Number\", { default: \"\" });\nif (!ticket) return;"),
            ("🟢 General API", "tp.confirm(message, title)", "Displays a Yes/No confirmation dialog returning true or false.", "const create = await tp.confirm(\"Create directory?\", \"Confirm\");"),
            ("🟢 General API", "tp.alert(message, title)", "Displays an informational alert notification toast.", "tp.alert(\"Task finished.\");"),
            ("🟢 General API", "tp.openUrl(url, browser?, profile?)", "Opens any URL in the default or targeted web browser (supports %ENV% variables).", "tp.openUrl(`https://locsoftware.zendesk.com/agent/tickets/${tp.vars.ticket}`, \"chrome\", \"Work\");"),
            ("🟢 General API", "tp.fs.openInExplorer(path)", "Reveals a folder or file in Windows File Explorer.", "tp.fs.openInExplorer(`d:\\\\Tickets\\\\${tp.vars.ticket}`);"),
            ("🟢 General API", "tp.clipboard.get() / set(text)", "Safely reads or writes text to the Windows clipboard.", "const clip = tp.clipboard.get();\ntp.clipboard.set(\"Copied text\");"),
            ("🟢 General API", "tp.notify(title, message)", "Emits a desktop toast notification.", "tp.notify(\"TriggerPoint\", \"Workflow executed successfully.\");"),

            ("🟣 Advanced & Automation API", "tp.launch(cmd, args, dir, admin)", "Launches an executable, script, or document with arguments and optional elevation.", "tp.launch(\"subl.exe\", `\"d:\\\\Tickets\\\\${tp.vars.ticket}\"`);"),
            ("🟣 Advanced & Automation API", "tp.fs.exists(path)", "Checks whether a file or directory exists on disk (returns boolean).", "if (!tp.fs.exists(folderPath)) {\n    tp.fs.createDirectory(folderPath);\n}"),
            ("🟣 Advanced & Automation API", "tp.fs.createDirectory(path)", "Creates a directory tree on disk.", "tp.fs.createDirectory(`d:\\\\Tickets\\\\${tp.vars.ticket}`);"),
            ("🟣 Advanced & Automation API", "tp.injectSnippet(template)", "Injects text expansion snippets into the active foreground window.", "await tp.injectSnippet(\"Working on #{ticket}\");"),
            ("🟣 Advanced & Automation API", "tp.executeAction(idOrName)", "Executes another TriggerPoint action or triggers a folder menu/palette (with loop prevention).", "await tp.executeAction(\"Open Work Ticket\");"),
            ("🟣 Advanced & Automation API", "tp.delay(ms)", "Pauses script execution for the specified milliseconds.", "await tp.delay(1000);"),
            ("🟣 Advanced & Automation API", "tp.vars", "Key-value dictionary holding shared variables across steps.", "tp.vars.cleanTicket = tp.vars.ticket.trim();")
        };

        string lastCat = string.Empty;
        foreach (var (cat, name, desc, ex) in apis)
        {
            if (cat != lastCat)
            {
                WorkflowApiDrawerHost.Children.Add(new TextBlock
                {
                    Text = cat,
                    FontWeight = FontWeights.Bold,
                    FontSize = 12,
                    Foreground = Application.Current.TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White,
                    Margin = new Thickness(0, lastCat == string.Empty ? 0 : 10, 0, 4)
                });
                lastCat = cat;
            }

            var card = new Border
            {
                Background = Application.Current.TryFindResource("BgSecondaryBrush") as Brush ?? Brushes.DarkSlateGray,
                BorderBrush = Application.Current.TryFindResource("BorderSubtleBrush") as Brush ?? Brushes.Gray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 5)
            };

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel();
            info.Children.Add(new TextBlock
            {
                Text = name,
                FontFamily = Application.Current.TryFindResource("CodeFont") as FontFamily,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11.5,
                Foreground = Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.CornflowerBlue
            });
            info.Children.Add(new TextBlock
            {
                Text = desc,
                FontSize = 11,
                Foreground = Application.Current.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray,
                Margin = new Thickness(0, 2, 0, 0)
            });

            Grid.SetColumn(info, 0);
            row.Children.Add(info);

            var insertBtn = new Button
            {
                Content = "+ Insert",
                Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
                FontSize = 10.5,
                Padding = new Thickness(7, 3, 7, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            insertBtn.Click += (s, e) =>
            {
                if (WorkflowScriptEditor.Document == null) return;
                int caret = Math.Clamp(WorkflowScriptEditor.CaretOffset, 0, WorkflowScriptEditor.Document.TextLength);
                WorkflowScriptEditor.Document.Insert(caret, ex + "\n");
                WorkflowScriptEditor.CaretOffset = caret + ex.Length + 1;
                WorkflowScriptEditor.Focus();
            };

            Grid.SetColumn(insertBtn, 1);
            row.Children.Add(insertBtn);

            card.Child = row;
            WorkflowApiDrawerHost.Children.Add(card);
        }
    }

    private void TestWorkflowBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem == null) return;
        CommitCurrentFormChanges();

        _ = Task.Run(async () =>
        {
            try
            {
                await _executor.ExecuteAsync(_selectedItem);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to test workflow '{Name}'", _selectedItem.Name);
            }
        });
    }

    private static void SyncPrimaryPromptField(WorkflowStep step)
    {
        if (step.PromptFields != null && step.PromptFields.Count > 0)
        {
            var first = step.PromptFields[0];
            step.VariableName = first.VariableName;
            step.PromptLabel = first.Label;
            step.PromptDefaultValue = first.DefaultValue;
            step.PromptType = first.Type;
            step.PromptChoices = first.Choices;
            step.PromptMinNumber = first.MinNumber;
            step.PromptMaxNumber = first.MaxNumber;
            step.PromptDateFormat = first.DateFormat;
        }
    }
}
