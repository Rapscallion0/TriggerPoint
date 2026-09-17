using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Microsoft.Win32;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;
using TriggerPoint.Infrastructure.Services;
using TriggerPoint.Infrastructure.Win32;
using TriggerPoint.UI.Theme;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;

namespace TriggerPoint.UI.Views;

public class WorkflowStepDragData
{
    public WorkflowStep Step { get; }
    public List<WorkflowStep> SourceList { get; }
    public WorkflowStep? ParentIfStep { get; }

    public WorkflowStepDragData(WorkflowStep step, List<WorkflowStep> sourceList, WorkflowStep? parentIfStep = null)
    {
        Step = step;
        SourceList = sourceList;
        ParentIfStep = parentIfStep;
    }
}

public partial class SettingsWindow
{
    public static readonly DependencyProperty IsWorkflowDropTargetProperty =
        DependencyProperty.RegisterAttached(
            "IsWorkflowDropTarget",
            typeof(bool),
            typeof(SettingsWindow),
            new PropertyMetadata(false));

    public static void SetIsWorkflowDropTarget(UIElement element, bool value) =>
        element.SetValue(IsWorkflowDropTargetProperty, value);

    public static bool GetIsWorkflowDropTarget(UIElement element) =>
        (bool)element.GetValue(IsWorkflowDropTargetProperty);

    private static bool IsCursorPhysicallyOver(FrameworkElement element)
    {
        if (!element.IsLoaded || !element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return false;

        if (!NativeMethods.GetCursorPos(out var pt)) return false;

        try
        {
            Point local = element.PointFromScreen(new Point(pt.X, pt.Y));
            return local.X >= 0 && local.Y >= 0 && local.X < element.ActualWidth && local.Y < element.ActualHeight;
        }
        catch
        {
            return false;
        }
    }

    private Guid? _activeWorkflowStepId;

    // Step Clipboard State
    private static WorkflowStep? _stepClipboard;
    private static bool _stepClipboardIsCut;
    private static List<WorkflowStep>? _stepClipboardSourceList;
    private static WorkflowStep? _stepClipboardParentIfStep;

    // Drag and Drop tracking for Workflow Steps
    private Point? _workflowStepDragStartPoint;
    private WorkflowStepDragData? _draggedStepData;
    private System.Windows.Threading.DispatcherTimer? _workflowDragScrollTimer;
    private Point _lastWorkflowDragScreenPoint;

    private static readonly Geometry _arrowUpGeometry = Geometry.Parse("M 7 14 L 7 5 L 3.5 8.5 L 2 7 L 8 1 L 14 7 L 12.5 8.5 L 9 5 L 9 14 Z");
    private static readonly Geometry _arrowDownGeometry = Geometry.Parse("M 7 2 L 7 11 L 3.5 7.5 L 2 9 L 8 15 L 14 9 L 12.5 7.5 L 9 11 L 9 2 Z");
    private static readonly Geometry _arrowBranchGeometry = Geometry.Parse("M 3 3 L 3 10 A 3 3 0 0 0 6 13 L 11 13 L 9 15 L 10.5 16.5 L 15 12 L 10.5 7.5 L 9 9 L 11 11 L 6 11 A 1 1 0 0 1 5 10 L 5 3 Z");

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
        bool hasSteps = _selectedItem?.Payload.WorkflowSteps != null && _selectedItem.Payload.WorkflowSteps.Count > 0;
        if (SaveWorkflowAsTemplateBtn != null)
        {
            SaveWorkflowAsTemplateBtn.Visibility = hasSteps ? Visibility.Visible : Visibility.Collapsed;
        }
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
        RebuildWorkflowVariablesUI();
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

        // Pre-populate with workflow-level constants/variables
        if (_selectedItem?.Payload?.WorkflowVariables != null)
        {
            foreach (var v in _selectedItem.Payload.WorkflowVariables)
            {
                if (!string.IsNullOrWhiteSpace(v.Name))
                {
                    definedVariables.Add(v.Name.Trim());
                }
            }
        }

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

            // Drop indicators for visual feedback without card border jump
            var accentBrush = Application.Current.TryFindResource("WorkflowBrush") as Brush ?? Brushes.Purple;
            var topIndicator = CreateDropIndicatorLine(accentBrush);
            topIndicator.VerticalAlignment = VerticalAlignment.Top;
            topIndicator.Margin = new Thickness(0, 7, 0, 0);
            stepWrapperGrid.Children.Add(topIndicator);

            var bottomIndicator = CreateDropIndicatorLine(accentBrush);
            bottomIndicator.VerticalAlignment = VerticalAlignment.Bottom;
            bottomIndicator.Margin = new Thickness(0, 0, 0, 7);
            stepWrapperGrid.Children.Add(bottomIndicator);

            if (_selectedItem?.Payload.WorkflowSteps != null)
            {
                WireWorkflowStepDropTarget(card, step, _selectedItem.Payload.WorkflowSteps, null, topIndicator, bottomIndicator, accentBrush);
            }

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
            else if (step.StepType == WorkflowStepType.SetVariable)
            {
                if (!string.IsNullOrWhiteSpace(step.SetVariableName))
                {
                    definedVariables.Add(step.SetVariableName.Trim());
                }
            }
            else if (step.StepType == WorkflowStepType.EnsureDirectory)
            {
                var folderVar = string.IsNullOrWhiteSpace(step.VariableName) ? "folder" : step.VariableName.Trim();
                definedVariables.Add(folderVar);
            }
            else if (step.StepType == WorkflowStepType.Dialog)
            {
                var dlgVar = string.IsNullOrWhiteSpace(step.VariableName) ? "dialogResult" : step.VariableName.Trim();
                definedVariables.Add(dlgVar);
            }
            else if (step.StepType == WorkflowStepType.IfCondition)
            {
                if (step.ThenSteps != null)
                {
                    foreach (var sub in step.ThenSteps)
                    {
                        if (sub.StepType == WorkflowStepType.SetVariable && !string.IsNullOrWhiteSpace(sub.SetVariableName))
                        {
                            definedVariables.Add(sub.SetVariableName.Trim());
                        }
                    }
                }
                if (step.ElseSteps != null)
                {
                    foreach (var sub in step.ElseSteps)
                    {
                        if (sub.StepType == WorkflowStepType.SetVariable && !string.IsNullOrWhiteSpace(sub.SetVariableName))
                        {
                            definedVariables.Add(sub.SetVariableName.Trim());
                        }
                    }
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

        pill.AllowDrop = true;
        SetIsWorkflowDropTarget(pill, true);
        pill.PreviewDragEnter += (s, e) =>
        {
            if (e.Data.GetDataPresent("WorkflowStepDragData"))
            {
                UpdateWorkflowGhostPosition(e);
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
        };
        pill.PreviewDragOver += (s, e) =>
        {
            if (e.Data.GetDataPresent("WorkflowStepDragData"))
            {
                UpdateWorkflowGhostPosition(e);
                var accent = Application.Current.TryFindResource("WorkflowBrush") as Brush ?? Brushes.Purple;
                pill.BorderBrush = accent;
                pill.BorderThickness = new Thickness(2);
                text.Foreground = accent;
                SetWorkflowGhostAction($"Insert at position #{insertIndex + 1}", _arrowDownGeometry, accent);
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
        };

        pill.PreviewDragLeave += (s, e) =>
        {
            if (!IsCursorPhysicallyOver(pill))
            {
                pill.BorderThickness = new Thickness(1);
                bool active = _activeWorkflowStepId == stepId;
                pill.BorderBrush = active
                    ? (Application.Current.TryFindResource("WorkflowBrush") as Brush ?? Brushes.Purple)
                    : (Application.Current.TryFindResource("BorderSubtleBrush") as Brush ?? Brushes.Gray);
                text.Foreground = active
                    ? (Application.Current.TryFindResource("WorkflowBrush") as Brush ?? Brushes.Purple)
                    : (Application.Current.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray);
                ClearWorkflowGhostAction();
            }
        };

        pill.PreviewDrop += (s, e) =>
        {
            pill.BorderThickness = new Thickness(1);
            ClearWorkflowGhostAction();
            if (e.Data.GetDataPresent("WorkflowStepDragData"))
            {
                var dragData = e.Data.GetData("WorkflowStepDragData") as WorkflowStepDragData;
                if (dragData != null && _selectedItem?.Payload.WorkflowSteps != null)
                {
                    ExecuteStepDrop(dragData, _selectedItem.Payload.WorkflowSteps, insertIndex);
                    e.Handled = true;
                }
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

        if (_stepClipboard != null && _selectedItem?.Payload.WorkflowSteps != null)
        {
            var parts = SplitIconAndName(GetStepTypeIconAndName(_stepClipboard.StepType));
            var pasteItem = new MenuItem
            {
                Header = $"📋  Paste Step: \"{_stepClipboard.Name}\" ({parts.Icon})",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Application.Current.TryFindResource("WorkflowBrush") as Brush ?? Brushes.Purple
            };
            pasteItem.Click += (s, e) => PasteStepAt(_selectedItem.Payload.WorkflowSteps, insertIndex);
            menu.Items.Add(pasteItem);
            menu.Items.Add(new Separator());
        }

        var types = new (string Title, WorkflowStepType Type, string Icon)[]
        {
            ("Set Variable", WorkflowStepType.SetVariable, "🔤"),
            ("If / Condition Branch", WorkflowStepType.IfCondition, "🔀"),
            ("Prompt for User Input", WorkflowStepType.Prompt, "💬"),
            ("Show Dialog / Confirmation", WorkflowStepType.Dialog, "💬"),
            ("Open URL / Web Page", WorkflowStepType.OpenUrl, "🌐"),
            ("Launch Application / Command", WorkflowStepType.LaunchApp, "⚡"),
            ("Ensure Folder Exists", WorkflowStepType.EnsureDirectory, "📁"),
            ("Paste / Insert Text Snippet", WorkflowStepType.InjectSnippet, "📝"),
            ("Recorded Macro Sequence", WorkflowStepType.Macro, "🔴"),
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

        if (stepType == WorkflowStepType.SetVariable)
        {
            step.Name = "Set Variable";
            step.SetVariableName = "myVar";
            step.SetVariableValue = string.Empty;
        }
        else if (stepType == WorkflowStepType.IfCondition)
        {
            step.Name = "If Condition";
            step.ConditionLeft = string.Empty;
            step.ConditionOperator = ConditionOperator.Equals;
            step.ConditionRight = string.Empty;
            step.ConditionIgnoreCase = true;
            step.HasElseBranch = false;
            step.ThenSteps = [];
            step.ElseSteps = [];
        }
        else if (stepType == WorkflowStepType.Prompt)
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
        else if (stepType == WorkflowStepType.EnsureDirectory)
        {
            step.VariableName = "folder";
        }
        else if (stepType == WorkflowStepType.Dialog)
        {
            step.Name = "Show Confirmation Dialog";
            step.DialogTitle = "Confirmation";
            step.DialogMessage = "Do you want to proceed with this workflow?";
            step.DialogButtons = WorkflowDialogButtons.OkCancel;
            step.VariableName = "dialogResult";
        }
        else if (stepType == WorkflowStepType.Macro)
        {
            step.Name = "Recorded Macro";
            step.Macro = new MacroPayload();
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

        // Left Header: Drag grip + Index badge + Type icon + Summary badge
        var leftHeader = new StackPanel 
        { 
            Orientation = Orientation.Horizontal, 
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Double-click to " + (step.IsCollapsed ? "expand" : "collapse")
        };

        if (_selectedItem?.Payload.WorkflowSteps != null)
        {
            var dragGrip = new TextBlock
            {
                Text = "⠿",
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
                Foreground = Application.Current.TryFindResource("TextMutedBrush") as Brush ?? Brushes.Gray,
                Cursor = Cursors.SizeAll,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Drag to reorder or move into/out of conditions"
            };
            WireWorkflowStepDragSource(dragGrip, step, _selectedItem.Payload.WorkflowSteps, null);
            leftHeader.Children.Add(dragGrip);
        }

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

        if (step.StepType == WorkflowStepType.IfCondition)
        {
            var testCondItem = new MenuItem { Header = "▶  Test Condition Live", FontSize = 12 };
            testCondItem.Click += (s, e) => TestConditionLive(step);
            kebabMenu.Items.Add(testCondItem);
        }
        else
        {
            var testItem = new MenuItem { Header = "▶  Test Step", FontSize = 12 };
            testItem.Click += async (s, e) => await TestSingleWorkflowStepAsync(step, stepIndex);
            kebabMenu.Items.Add(testItem);
        }

        // Cut / Copy
        if (_selectedItem?.Payload.WorkflowSteps != null)
        {
            var cutItem = new MenuItem { Header = "✂  Cut Step", FontSize = 12 };
            cutItem.Click += (s, e) => CutStep(step, _selectedItem.Payload.WorkflowSteps, null);
            kebabMenu.Items.Add(cutItem);

            var copyItem = new MenuItem { Header = "⧉  Copy Step", FontSize = 12 };
            copyItem.Click += (s, e) => CopyStep(step);
            kebabMenu.Items.Add(copyItem);
        }

        var dupItem = new MenuItem { Header = "📄  Duplicate", FontSize = 12 };
        dupItem.Click += (s, e) => DuplicateStep(step);
        kebabMenu.Items.Add(dupItem);

        // Move into Condition ▾ (if any other condition steps exist)
        if (_selectedItem?.Payload.WorkflowSteps != null)
        {
            var ifSteps = _selectedItem.Payload.WorkflowSteps
                .Select((s, i) => (Step: s, Index: i + 1))
                .Where(x => x.Step.StepType == WorkflowStepType.IfCondition && x.Step.Id != step.Id)
                .ToList();

            if (ifSteps.Count > 0)
            {
                var moveCondSub = new MenuItem { Header = "↳  Move into Condition ▾", FontSize = 12 };
                foreach (var ifItem in ifSteps)
                {
                    var targetIf = ifItem.Step;
                    var condMenu = new MenuItem
                    {
                        Header = $"Step {ifItem.Index}: {GetStepLiveSummary(targetIf)}",
                        FontSize = 11.5
                    };

                    var intoThen = new MenuItem { Header = "↳ Into THEN Branch", FontSize = 11.5 };
                    intoThen.Click += (s, e) => MoveStepToConditionBranch(step, _selectedItem.Payload.WorkflowSteps, targetIf, isElse: false);
                    condMenu.Items.Add(intoThen);

                    var intoElse = new MenuItem { Header = "↳ Into ELSE Branch", FontSize = 11.5 };
                    intoElse.Click += (s, e) => MoveStepToConditionBranch(step, _selectedItem.Payload.WorkflowSteps, targetIf, isElse: true);
                    condMenu.Items.Add(intoElse);

                    moveCondSub.Items.Add(condMenu);
                }
                kebabMenu.Items.Add(moveCondSub);
            }
        }

        // Convert to Inline Script — hidden for RunScript steps (already a script) and IfCondition
        if (step.StepType != WorkflowStepType.RunScript && step.StepType != WorkflowStepType.IfCondition)
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

                // Export Variable Token Name row
                var varExportStack = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
                varExportStack.Children.Add(new TextBlock { Text = "Export Variable Token Name", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
                var varBox = new TextBox
                {
                    Text = string.IsNullOrWhiteSpace(step.VariableName) ? "folder" : step.VariableName,
                    Height = 32,
                    Padding = new Thickness(8, 3, 8, 3),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Style = Application.Current.TryFindResource("ModernTextBoxStyle") as Style,
                    ToolTip = "Name of the variable token created with this folder path, e.g. {folder} for subsequent steps"
                };
                varBox.TextChanged += (s, e) =>
                {
                    step.VariableName = varBox.Text.Trim();
                    OnFormEdited();
                };
                varExportStack.Children.Add(varBox);
                container.Children.Add(varExportStack);
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

            case WorkflowStepType.Dialog:
            {
                // Dialog Title
                var titleStack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
                titleStack.Children.Add(new TextBlock { Text = "Dialog Title", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
                var titleBox = new TextBox
                {
                    Text = step.DialogTitle,
                    Height = 32,
                    Padding = new Thickness(8, 3, 8, 3),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Style = Application.Current.TryFindResource("ModernTextBoxStyle") as Style
                };
                titleBox.TextChanged += (s, e) =>
                {
                    step.DialogTitle = titleBox.Text;
                    summaryText.Text = GetStepLiveSummary(step);
                    OnFormEdited();
                };
                titleStack.Children.Add(titleBox);
                container.Children.Add(titleStack);

                // Dialog Message
                var msgStack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
                msgStack.Children.Add(new TextBlock { Text = "Dialog Message", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
                var msgBox = new TextBox
                {
                    Text = step.DialogMessage,
                    Height = 56,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    Padding = new Thickness(8, 4, 8, 4),
                    Style = Application.Current.TryFindResource("ModernTextBoxStyle") as Style
                };
                msgBox.TextChanged += (s, e) =>
                {
                    step.DialogMessage = msgBox.Text;
                    summaryText.Text = GetStepLiveSummary(step);
                    OnFormEdited();
                };
                msgStack.Children.Add(msgBox);
                container.Children.Add(msgStack);

                RenderVariableChips(container, msgBox, availableVariables);

                // Buttons & Export Row
                var optionsGrid = new Grid { Margin = new Thickness(0, 4, 0, 8) };
                optionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                optionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12, GridUnitType.Pixel) });
                optionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var btnStack = new StackPanel();
                btnStack.Children.Add(new TextBlock { Text = "Dialog Buttons", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
                var btnCombo = new ComboBox { Height = 32, FontSize = 12 };
                btnCombo.Items.Add(new ComboBoxItem { Content = "OK (Alert / Notification)" });
                btnCombo.Items.Add(new ComboBoxItem { Content = "OK / Cancel" });
                btnCombo.Items.Add(new ComboBoxItem { Content = "Yes / No" });
                btnCombo.SelectedIndex = step.DialogButtons switch
                {
                    WorkflowDialogButtons.Ok => 0,
                    WorkflowDialogButtons.YesNo => 2,
                    _ => 1
                };
                btnCombo.SelectionChanged += (s, e) =>
                {
                    step.DialogButtons = btnCombo.SelectedIndex switch
                    {
                        0 => WorkflowDialogButtons.Ok,
                        2 => WorkflowDialogButtons.YesNo,
                        _ => WorkflowDialogButtons.OkCancel
                    };
                    summaryText.Text = GetStepLiveSummary(step);
                    OnFormEdited();
                };
                btnStack.Children.Add(btnCombo);
                Grid.SetColumn(btnStack, 0);
                optionsGrid.Children.Add(btnStack);

                var varStack = new StackPanel();
                varStack.Children.Add(new TextBlock { Text = "Result Variable Name", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
                var varBox = new TextBox
                {
                    Text = string.IsNullOrWhiteSpace(step.VariableName) ? "dialogResult" : step.VariableName,
                    Height = 32,
                    Padding = new Thickness(8, 3, 8, 3),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Style = Application.Current.TryFindResource("ModernTextBoxStyle") as Style,
                    ToolTip = "Variable set to 'ok' or 'cancel' based on user selection"
                };
                varBox.TextChanged += (s, e) =>
                {
                    step.VariableName = varBox.Text.Trim();
                    OnFormEdited();
                };
                varStack.Children.Add(varBox);
                Grid.SetColumn(varStack, 2);
                optionsGrid.Children.Add(varStack);

                container.Children.Add(optionsGrid);
                break;
            }

            case WorkflowStepType.Macro:
            {
                step.Macro ??= new MacroPayload();
                var macroEditor = new Controls.MacroEditorControl { Height = 340, Margin = new Thickness(0, 4, 0, 4) };
                macroEditor.Initialize(_macroService, step.Macro);
                macroEditor.MacroChanged += (s, e) =>
                {
                    step.Macro = macroEditor.CurrentMacro;
                    summaryText.Text = GetStepLiveSummary(step);
                    OnFormEdited();
                };
                container.Children.Add(macroEditor);
                break;
            }

            case WorkflowStepType.SetVariable:
            {
                var grid = new Grid { Margin = new Thickness(0, 4, 0, 8) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Variable Name
                var nameStack = new StackPanel();
                nameStack.Children.Add(new TextBlock 
                { 
                    Text = "Variable Name", 
                    FontSize = 11, 
                    FontWeight = FontWeights.SemiBold, 
                    Margin = new Thickness(0, 0, 0, 3) 
                });
                var nameBox = new TextBox
                {
                    Text = step.SetVariableName ?? string.Empty,
                    Height = 34,
                    Padding = new Thickness(8, 4, 8, 4),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Style = Application.Current.TryFindResource("ModernTextBoxStyle") as Style,
                    Tag = "e.g. buildDir, ticket"
                };
                nameBox.TextChanged += (s, e) =>
                {
                    step.SetVariableName = nameBox.Text.Trim();
                    summaryText.Text = GetStepLiveSummary(step);
                    OnFormEdited();
                };
                nameStack.Children.Add(nameBox);
                Grid.SetColumn(nameStack, 0);
                grid.Children.Add(nameStack);

                // Variable Value
                var valStack = new StackPanel();
                valStack.Children.Add(new TextBlock 
                { 
                    Text = "Value Expression (%ENV% and {tokens} supported)", 
                    FontSize = 11, 
                    FontWeight = FontWeights.SemiBold, 
                    Margin = new Thickness(0, 0, 0, 3) 
                });
                var valBox = new TextBox
                {
                    Text = step.SetVariableValue ?? string.Empty,
                    Height = 34,
                    Padding = new Thickness(8, 4, 8, 4),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Style = Application.Current.TryFindResource("ModernTextBoxStyle") as Style,
                    Tag = "e.g. %USERPROFILE%\\Projects or {clipboard:trim}"
                };
                valBox.TextChanged += (s, e) =>
                {
                    step.SetVariableValue = valBox.Text;
                    summaryText.Text = GetStepLiveSummary(step);
                    OnFormEdited();
                };
                valStack.Children.Add(valBox);
                RenderVariableChips(valStack, valBox, availableVariables);

                Grid.SetColumn(valStack, 2);
                grid.Children.Add(valStack);

                container.Children.Add(grid);
                break;
            }

            case WorkflowStepType.IfCondition:
            {
                RenderIfConditionControls(container, step, summaryText, availableVariables);
                break;
            }
        }
    }

    private void ShowVariablePickerDialog(TextBox targetBox, List<string>? availableVariables = null)
    {
        var dialog = new VariablePickerDialog(availableVariables)
        {
            Owner = Window.GetWindow(this) ?? Application.Current.MainWindow
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.SelectedToken))
        {
            InsertTokenIntoBox(targetBox, dialog.SelectedToken);
        }
    }

    private void RenderVariableChips(StackPanel container, TextBox targetBox, List<string> availableVariables)
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };

        // Unified Token & Environment Variable Dropdown Button
        var tokenBtn = new Button
        {
            Content = "{x} Insert Token / Env ▾",
            Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
            FontSize = 10.5,
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = "Insert Windows Environment Variables, Date/Time tokens, Clipboard, or Workflow variables (Click to search & filter, right-click for quick menu)"
        };
        tokenBtn.Click += (s, e) => ShowVariablePickerDialog(targetBox, availableVariables);
        tokenBtn.MouseRightButtonUp += (s, e) =>
        {
            ShowTokenAndEnvMenu(tokenBtn, targetBox, availableVariables);
            e.Handled = true;
        };
        wrap.Children.Add(tokenBtn);

        // One-click quick pills for available workflow variables (if any)
        if (availableVariables != null && availableVariables.Count > 0)
        {
            foreach (var varName in availableVariables.Take(5))
            {
                var pill = new Button
                {
                    Content = $"+{{{varName}}}",
                    Tag = $"{{{varName}}}",
                    Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
                    FontSize = 10.5,
                    Padding = new Thickness(5, 2, 5, 2),
                    Margin = new Thickness(2, 0, 2, 0),
                    ToolTip = $"Insert {{{varName}}} into this field"
                };
                pill.Click += (s, e) =>
                {
                    InsertTokenIntoBox(targetBox, $"{{{varName}}}");
                };
                wrap.Children.Add(pill);
            }
        }

        container.Children.Add(wrap);
    }

    private void ShowTokenAndEnvMenu(FrameworkElement target, TextBox targetBox, List<string>? availableVariables = null)
    {
        var contextMenu = new ContextMenu();

        var searchItem = new MenuItem
        {
            Header = "🔍 Search & Filter Variables / Tokens...",
            FontWeight = FontWeights.SemiBold
        };
        searchItem.Click += (s, e) => ShowVariablePickerDialog(targetBox, availableVariables);
        contextMenu.Items.Add(searchItem);
        contextMenu.Items.Add(new Separator());

        var envMenu = new MenuItem { Header = "Windows Environment Variables (%VAR%)" };

        // Submenu 1: User & Profile Paths
        var userPathsMenu = new MenuItem { Header = "User & Profile Paths" };
        var userPathVars = new (string Token, string Description)[]
        {
            ("%USERPROFILE%", "User home profile directory (C:\\Users\\<user>)"),
            ("%APPDATA%", "Roaming AppData directory (%APPDATA%)"),
            ("%LOCALAPPDATA%", "Local AppData directory (%LOCALAPPDATA%)"),
            ("%TEMP%", "Temporary files directory (%TEMP%)"),
            ("%TMP%", "Temporary files directory (%TMP%)"),
            ("%HOMEDRIVE%", "Host drive for user profile (e.g. C:)"),
            ("%HOMEPATH%", "Home path relative to drive (e.g. \\Users\\<user>)"),
            ("%PUBLIC%", "Public shared profile directory (C:\\Users\\Public)"),
            ("%USERNAME%", "Current logon username"),
            ("%USERDOMAIN%", "Domain or workgroup name")
        };
        foreach (var (tok, desc) in userPathVars)
        {
            string liveVal = Environment.ExpandEnvironmentVariables(tok);
            var mItem = new MenuItem { Header = $"{tok}  ({desc})" };
            if (!string.Equals(liveVal, tok, StringComparison.OrdinalIgnoreCase))
            {
                mItem.ToolTip = $"Current Value:\n{liveVal}";
            }
            mItem.Click += (s, e) => InsertTokenIntoBox(targetBox, tok);
            userPathsMenu.Items.Add(mItem);
        }
        envMenu.Items.Add(userPathsMenu);

        // Submenu 2: Windows & System Directories
        var sysPathsMenu = new MenuItem { Header = "Windows & System Directories" };
        var sysPathVars = new (string Token, string Description)[]
        {
            ("%WINDIR%", "Windows installation directory (C:\\Windows)"),
            ("%SYSTEMROOT%", "Windows root directory (C:\\Windows)"),
            ("%SYSTEMDRIVE%", "Windows operating system drive (C:)"),
            ("%PROGRAMFILES%", "64-bit Program Files (C:\\Program Files)"),
            ("%PROGRAMFILES(X86)%", "32-bit Program Files (C:\\Program Files (x86))"),
            ("%PROGRAMDATA%", "Shared Application Data directory (C:\\ProgramData)"),
            ("%ALLUSERSPROFILE%", "All Users Profile directory (C:\\ProgramData)"),
            ("%COMMONPROGRAMFILES%", "Common Files directory"),
            ("%COMSPEC%", "Executable path to Command Prompt (cmd.exe)"),
            ("%PATH%", "Search path for executable files")
        };
        foreach (var (tok, desc) in sysPathVars)
        {
            string liveVal = Environment.ExpandEnvironmentVariables(tok);
            var mItem = new MenuItem { Header = $"{tok}  ({desc})" };
            if (!string.Equals(liveVal, tok, StringComparison.OrdinalIgnoreCase))
            {
                mItem.ToolTip = $"Current Value:\n{liveVal}";
            }
            mItem.Click += (s, e) => InsertTokenIntoBox(targetBox, tok);
            sysPathsMenu.Items.Add(mItem);
        }
        envMenu.Items.Add(sysPathsMenu);

        // Submenu 3: Hardware & System Info
        var hardwareMenu = new MenuItem { Header = "Hardware & System Info" };
        var hwVars = new (string Token, string Description)[]
        {
            ("%COMPUTERNAME%", "Host computer network name"),
            ("%PROCESSOR_ARCHITECTURE%", "CPU architecture (AMD64, ARM64, x86)"),
            ("%NUMBER_OF_PROCESSORS%", "Logical processor count"),
            ("%OS%", "Operating system identification")
        };
        foreach (var (tok, desc) in hwVars)
        {
            string liveVal = Environment.ExpandEnvironmentVariables(tok);
            var mItem = new MenuItem { Header = $"{tok}  ({desc})" };
            if (!string.Equals(liveVal, tok, StringComparison.OrdinalIgnoreCase))
            {
                mItem.ToolTip = $"Current Value:\n{liveVal}";
            }
            mItem.Click += (s, e) => InsertTokenIntoBox(targetBox, tok);
            hardwareMenu.Items.Add(mItem);
        }
        envMenu.Items.Add(hardwareMenu);

        // Submenu 4: Dynamic Live System Environment Variables (alphabetized)
        try
        {
            var liveVars = Environment.GetEnvironmentVariables();
            var sortedKeys = liveVars.Keys.Cast<string>().OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            if (sortedKeys.Count > 0)
            {
                envMenu.Items.Add(new Separator());
                var allLiveMenu = new MenuItem { Header = $"All System Environment Variables ({sortedKeys.Count})" };
                foreach (var key in sortedKeys)
                {
                    string val = liveVars[key]?.ToString() ?? string.Empty;
                    string token = $"%{key}%";
                    string preview = val.Length > 40 ? val.Substring(0, 37) + "..." : val;
                    var mItem = new MenuItem { Header = $"{token}  ({preview})" };
                    mItem.ToolTip = $"Name: {key}\nValue: {val}";
                    mItem.Click += (s, e) => InsertTokenIntoBox(targetBox, token);
                    allLiveMenu.Items.Add(mItem);
                }
                envMenu.Items.Add(allLiveMenu);
            }
        }
        catch { }

        contextMenu.Items.Add(envMenu);

        var sysMenu = new MenuItem { Header = "System & Dynamic Tokens" };
        var sysTokens = new (string Token, string Description)[]
        {
            ("{date:yyyy-MM-dd}", "Current date (ISO format)"),
            ("{date:MM/dd/yyyy}", "Current date (US format)"),
            ("{time:HH:mm:ss}", "Current time (24h)"),
            ("{datetime:yyyyMMdd_HHmmss}", "Timestamp for files/backups"),
            ("{clipboard}", "Current clipboard content"),
            ("{clipboard:trim}", "Trimmed clipboard content"),
            ("{guid}", "Unique GUID"),
            ("{username}", "Windows username ({username})"),
            ("{machine}", "Computer name ({machine})")
        };
        foreach (var (tok, desc) in sysTokens)
        {
            var mItem = new MenuItem { Header = $"{tok}  ({desc})" };
            mItem.Click += (s, e) => InsertTokenIntoBox(targetBox, tok);
            sysMenu.Items.Add(mItem);
        }
        contextMenu.Items.Add(sysMenu);

        if (availableVariables != null && availableVariables.Count > 0)
        {
            contextMenu.Items.Add(new Separator());
            var varMenu = new MenuItem { Header = "Workflow Variables" };
            foreach (var v in availableVariables)
            {
                var mItem = new MenuItem { Header = $"{{{v}}}" };
                mItem.Click += (s, e) => InsertTokenIntoBox(targetBox, $"{{{v}}}");
                varMenu.Items.Add(mItem);
            }
            contextMenu.Items.Add(varMenu);
        }

        contextMenu.PlacementTarget = target;
        contextMenu.IsOpen = true;
    }

    private void InsertTokenIntoBox(TextBox box, string token)
    {
        if (box == null || string.IsNullOrEmpty(token)) return;

        string current = box.Text ?? string.Empty;
        int insertPos;

        if (box.IsFocused || (box.SelectionStart > 0 && box.SelectionStart <= current.Length))
        {
            insertPos = box.SelectionStart;
            int selLen = Math.Max(0, Math.Min(box.SelectionLength, current.Length - insertPos));
            if (selLen > 0)
            {
                current = current.Remove(insertPos, selLen);
            }
        }
        else
        {
            insertPos = current.Length;
        }

        box.Text = current.Insert(insertPos, token);
        box.Focus();
        box.CaretIndex = insertPos + token.Length;
        box.SelectionLength = 0;
        FlashHighlightBox(box);
        OnFormEdited();
    }

    private static void FlashHighlightBox(TextBox box)
    {
        if (box == null) return;
        try
        {
            var origBorder = box.BorderBrush;
            var highlightBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x8B, 0x5C, 0xF6));
            box.BorderBrush = highlightBrush;

            var anim = new ColorAnimation
            {
                From = Color.FromArgb(0xFF, 0x8B, 0x5C, 0xF6),
                To = (origBorder as SolidColorBrush)?.Color ?? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF),
                Duration = TimeSpan.FromMilliseconds(450),
                FillBehavior = FillBehavior.Stop
            };
            anim.Completed += (s, e) =>
            {
                box.BorderBrush = origBorder;
            };
            highlightBrush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        }
        catch
        {
            // Non-critical animation fallback
        }
    }

    private static string GetStepTypeIconAndName(WorkflowStepType stepType) => stepType switch
    {
        WorkflowStepType.SetVariable => "🔤 Set Variable",
        WorkflowStepType.IfCondition => "🔀 If / Branch",
        WorkflowStepType.Prompt => "💬 Prompt for Input",
        WorkflowStepType.Dialog => "💬 Show Confirmation Dialog",
        WorkflowStepType.OpenUrl => "🌐 Open Web URL",
        WorkflowStepType.EnsureDirectory => "📁 Ensure Folder Exists",
        WorkflowStepType.LaunchApp => "⚡ Launch Application",
        WorkflowStepType.InjectSnippet => "📝 Insert Snippet",
        WorkflowStepType.Macro => "🔴 Recorded Macro",
        WorkflowStepType.Delay => "⏱ Delay / Pause",
        WorkflowStepType.ExecuteAction => "⚡ Execute Action / Folder",
        WorkflowStepType.RunScript => "📜 Inline Script",
        _ => "Step"
    };

    private string GetStepLiveSummary(WorkflowStep step) => step.StepType switch
    {
        WorkflowStepType.IfCondition => string.IsNullOrWhiteSpace(step.ConditionLeft)
            ? "No condition specified"
            : $"If {step.ConditionLeft} {GetOperatorSymbol(step.ConditionOperator)} \"{step.ConditionRight}\" → {step.ThenSteps?.Count ?? 0} then{(step.HasElseBranch ? $", {step.ElseSteps?.Count ?? 0} else" : "")}",
        WorkflowStepType.SetVariable => string.IsNullOrWhiteSpace(step.SetVariableName)
            ? "No variable name"
            : $"{{{step.SetVariableName}}} = {step.SetVariableValue}",
        WorkflowStepType.Prompt => (step.PromptFields != null && step.PromptFields.Count > 0)
            ? (step.PromptFields.Count == 1 
                ? $"stores into ${{{step.PromptFields[0].VariableName}}}" 
                : $"{step.PromptFields.Count} fields: " + string.Join(", ", step.PromptFields.Select(f => $"${{{f.VariableName}}}")))
            : (string.IsNullOrWhiteSpace(step.VariableName) ? "No variable defined" : $"stores into ${{{step.VariableName}}}"),
        WorkflowStepType.Dialog => string.IsNullOrWhiteSpace(step.DialogMessage) 
            ? "No dialog message" 
            : $"{step.DialogButtons}: \"{step.DialogMessage}\"",
        WorkflowStepType.OpenUrl => string.IsNullOrWhiteSpace(step.Url) 
            ? "No URL specified" 
            : !string.IsNullOrWhiteSpace(step.BrowserTarget)
                ? (!string.IsNullOrWhiteSpace(step.BrowserProfile) ? $"{step.Url} [{step.BrowserTarget}:{step.BrowserProfile}]" : $"{step.Url} [{step.BrowserTarget}]")
                : step.Url,
        WorkflowStepType.EnsureDirectory => string.IsNullOrWhiteSpace(step.DirectoryPath) ? "No directory path" : step.DirectoryPath,
        WorkflowStepType.LaunchApp => string.IsNullOrWhiteSpace(step.Command) ? "No command specified" : step.Command,
        WorkflowStepType.InjectSnippet => string.IsNullOrWhiteSpace(step.SnippetTemplate) ? "Empty snippet" : step.SnippetTemplate,
        WorkflowStepType.Macro => step.Macro != null ? $"{step.Macro.Events.Count} event(s)" : "No recorded events",
        WorkflowStepType.Delay => $"{step.DelayMs}ms",
        WorkflowStepType.ExecuteAction => step.TargetItemId.HasValue
            ? (_items?.FirstOrDefault(i => i.Id == step.TargetItemId.Value) is { } target ? $"Run: {target.Name} ({(target.ActionType == ActionType.Folder ? "Folder Menu" : target.ActionType.ToString())})" : "Target item not found")
            : "No action or folder selected",
        WorkflowStepType.RunScript => string.IsNullOrWhiteSpace(step.InlineScript) ? "Empty script" : "Custom JavaScript",
        _ => string.Empty
    };

    private static string GetOperatorSymbol(ConditionOperator op) => op switch
    {
        ConditionOperator.Equals => "==",
        ConditionOperator.NotEquals => "!=",
        ConditionOperator.Contains => "contains",
        ConditionOperator.NotContains => "!contains",
        ConditionOperator.StartsWith => "startsWith",
        ConditionOperator.EndsWith => "endsWith",
        ConditionOperator.MatchesRegex => "matches",
        ConditionOperator.IsEmpty => "is empty",
        ConditionOperator.IsNotEmpty => "is not empty",
        ConditionOperator.GreaterThan => ">",
        ConditionOperator.LessThan => "<",
        ConditionOperator.GreaterOrEqual => ">=",
        ConditionOperator.LessOrEqual => "<=",
        ConditionOperator.FileExists => "file exists",
        ConditionOperator.DirectoryExists => "folder exists",
        ConditionOperator.ProcessIsRunning => "process running",
        _ => "=="
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

    private void AddFirstStepBtn_Click(object sender, RoutedEventArgs e)
    {
        if (AddFirstStepContextMenu != null && AddFirstStepBtn != null)
        {
            for (int i = AddFirstStepContextMenu.Items.Count - 1; i >= 0; i--)
            {
                if (AddFirstStepContextMenu.Items[i] is FrameworkElement fe && Equals(fe.Tag, "DynamicPasteItem"))
                {
                    AddFirstStepContextMenu.Items.RemoveAt(i);
                }
            }

            if (_stepClipboard != null && _selectedItem?.Payload.WorkflowSteps != null)
            {
                var parts = SplitIconAndName(GetStepTypeIconAndName(_stepClipboard.StepType));
                var pasteItem = new MenuItem
                {
                    Header = $"📋  Paste Step: \"{_stepClipboard.Name}\" ({parts.Icon})",
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Application.Current.TryFindResource("WorkflowBrush") as Brush ?? Brushes.Purple,
                    Tag = "DynamicPasteItem"
                };
                pasteItem.Click += (s, ev) => PasteStepAt(_selectedItem.Payload.WorkflowSteps, 0);
                AddFirstStepContextMenu.Items.Insert(0, pasteItem);
                AddFirstStepContextMenu.Items.Insert(1, new Separator { Tag = "DynamicPasteItem" });
            }

            AddFirstStepContextMenu.PlacementTarget = AddFirstStepBtn;
            AddFirstStepContextMenu.IsOpen = true;
        }
    }

    private void AddStepMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem == null) return;
        _selectedItem.Payload.WorkflowSteps ??= [];

        var tag = (sender as MenuItem)?.Tag?.ToString() ?? "Prompt";
        var stepType = tag switch
        {
            "SetVariable" => WorkflowStepType.SetVariable,
            "IfCondition" => WorkflowStepType.IfCondition,
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

    private void ChoosePresetTemplateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem == null) return;

        var dialog = new WorkflowTemplatePickerDialog(
            _workflowTemplateService,
            new ConfirmationDialog(),
            _selectedItem.Payload.WorkflowSteps)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.SelectedTemplate != null)
        {
            ApplyTemplate(dialog.SelectedTemplate);
        }
    }

    private void ApplyTemplate(WorkflowPreset preset)
    {
        if (_selectedItem == null) return;

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
        UpdateWorkflowPresetsVisibility();
        OnFormEdited();
    }

    private void SaveWorkflowAsTemplateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem?.Payload.WorkflowSteps == null || _selectedItem.Payload.WorkflowSteps.Count == 0)
        {
            MessageBox.Show("Add at least one visual step to save as a template.", "No Steps", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        CommitCurrentFormChanges();

        var dialog = new WorkflowTemplateEditorDialog(
            _workflowTemplateService,
            existingTemplate: null,
            stepsFromActiveWorkflow: _selectedItem.Payload.WorkflowSteps,
            suggestedTitle: _selectedItem.Name)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.ResultTemplate != null)
        {
            StatusText.Text = $"Workflow template '{dialog.ResultTemplate.Title}' saved.";
        }
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

    private void RebuildWorkflowVariablesUI()
    {
        if (WorkflowVariablesListHost == null || WorkflowVariablesCountText == null) return;
        WorkflowVariablesListHost.Children.Clear();

        if (_selectedItem?.Payload == null) return;
        _selectedItem.Payload.WorkflowVariables ??= [];

        var vars = _selectedItem.Payload.WorkflowVariables;
        WorkflowVariablesCountText.Text = $"Workflow Variables ({vars.Count})";

        for (int i = 0; i < vars.Count; i++)
        {
            var v = vars[i];
            var row = new Grid { Margin = new Thickness(0, 2, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameBox = new TextBox
            {
                Text = v.Name ?? string.Empty,
                Height = 30,
                Padding = new Thickness(6, 3, 6, 3),
                VerticalContentAlignment = VerticalAlignment.Center,
                Style = Application.Current.TryFindResource("ModernTextBoxStyle") as Style,
                Tag = "Variable name"
            };
            nameBox.TextChanged += (s, e) =>
            {
                v.Name = nameBox.Text.Trim();
                OnFormEdited();
            };
            Grid.SetColumn(nameBox, 0);
            row.Children.Add(nameBox);

            var valBox = new TextBox
            {
                Text = v.Value ?? string.Empty,
                Height = 30,
                Padding = new Thickness(6, 3, 6, 3),
                VerticalContentAlignment = VerticalAlignment.Center,
                Style = Application.Current.TryFindResource("ModernTextBoxStyle") as Style,
                Tag = "Value (e.g. %USERPROFILE%\\Docs or constant)"
            };
            valBox.TextChanged += (s, e) =>
            {
                v.Value = valBox.Text;
                OnFormEdited();
            };
            Grid.SetColumn(valBox, 2);
            row.Children.Add(valBox);

            // Token / Env helper button for this row
            var helperBtn = new Button
            {
                Content = "{x} ▾",
                Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
                Height = 30,
                Padding = new Thickness(6, 0, 6, 0),
                FontSize = 11,
                ToolTip = "Insert environment variable or token"
            };
            helperBtn.Click += (s, e) =>
            {
                ShowTokenAndEnvMenu(helperBtn, valBox);
            };
            Grid.SetColumn(helperBtn, 4);
            row.Children.Add(helperBtn);

            // Delete variable button
            var delBtn = new Button
            {
                Content = "✕",
                Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
                Height = 30,
                Width = 30,
                Padding = new Thickness(0),
                Foreground = Application.Current.TryFindResource("ErrorBrush") as Brush ?? Brushes.Red,
                ToolTip = "Delete variable"
            };
            var capturedVar = v;
            delBtn.Click += (s, e) =>
            {
                _selectedItem.Payload.WorkflowVariables.Remove(capturedVar);
                RebuildWorkflowVariablesUI();
                RebuildWorkflowStepCards();
                OnFormEdited();
            };
            Grid.SetColumn(delBtn, 6);
            row.Children.Add(delBtn);

            WorkflowVariablesListHost.Children.Add(row);
        }
    }

    private void AddWorkflowVariableBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem?.Payload == null) return;
        _selectedItem.Payload.WorkflowVariables ??= [];
        int count = _selectedItem.Payload.WorkflowVariables.Count + 1;
        _selectedItem.Payload.WorkflowVariables.Add(new WorkflowVariableDefinition
        {
            Name = $"var{count}",
            Value = string.Empty
        });
        if (WorkflowVariablesExpander != null)
        {
            WorkflowVariablesExpander.IsExpanded = true;
        }
        RebuildWorkflowVariablesUI();
        RebuildWorkflowStepCards();
        OnFormEdited();
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

    private void RenderIfConditionControls(StackPanel container, WorkflowStep step, TextBlock summaryText, List<string> availableVariables)
    {
        step.ThenSteps ??= [];
        step.ElseSteps ??= [];

        // 1. Condition Rule Expression Card
        var condCard = new Border
        {
            Background = Application.Current.TryFindResource("CardBgBrush") as Brush ?? Brushes.Transparent,
            BorderBrush = Application.Current.TryFindResource("CardBorderBrush") as Brush ?? Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var condStack = new StackPanel();
        condCard.Child = condStack;

        var condTitleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = false };
        var condTitle = new TextBlock
        {
            Text = "Condition Rule",
            FontWeight = FontWeights.SemiBold,
            FontSize = 11.5,
            Foreground = Application.Current.TryFindResource("WorkflowBrush") as Brush ?? Brushes.CornflowerBlue,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(condTitle, Dock.Left);
        condTitleRow.Children.Add(condTitle);

        var testBtn = new Button
        {
            Content = "▶ Test Condition Live",
            Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
            FontSize = 11,
            Padding = new Thickness(8, 2, 8, 2),
            ToolTip = "Evaluate this condition against current workflow variables and system state"
        };
        testBtn.Click += (s, e) => TestConditionLive(step);
        DockPanel.SetDock(testBtn, Dock.Right);
        condTitleRow.Children.Add(testBtn);

        condStack.Children.Add(condTitleRow);

        // Expression Grid: Left Operand | Operator | Right Operand
        var exprGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        exprGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        exprGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10, GridUnitType.Pixel) });
        exprGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180, GridUnitType.Pixel) });
        exprGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10, GridUnitType.Pixel) });
        var rightCol = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
        exprGrid.ColumnDefinitions.Add(rightCol);

        // Left Operand Stack
        var leftStack = new StackPanel();
        leftStack.Children.Add(new TextBlock { Text = "Value / Variable / Path", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var leftBox = new TextBox
        {
            Text = step.ConditionLeft ?? string.Empty,
            Height = 32,
            Padding = new Thickness(8, 4, 8, 4),
            VerticalContentAlignment = VerticalAlignment.Center,
            Style = Application.Current.TryFindResource("ModernTextBoxStyle") as Style,
            Tag = "e.g. {status}, %TEMP%\\app.lock, or myVar"
        };
        leftStack.Children.Add(leftBox);
        RenderVariableChips(leftStack, leftBox, availableVariables);
        Grid.SetColumn(leftStack, 0);
        exprGrid.Children.Add(leftStack);

        // Operator Stack
        var opStack = new StackPanel();
        opStack.Children.Add(new TextBlock { Text = "Operator", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var opCombo = new ComboBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            Style = Application.Current.TryFindResource("ModernComboBoxStyle") as Style,
            ItemContainerStyle = Application.Current.TryFindResource("ModernComboBoxItemStyle") as Style
        };

        var operators = new (ConditionOperator Op, string Label)[]
        {
            (ConditionOperator.Equals, "Equals (=)"),
            (ConditionOperator.NotEquals, "Does Not Equal (≠)"),
            (ConditionOperator.Contains, "Contains"),
            (ConditionOperator.NotContains, "Does Not Contain"),
            (ConditionOperator.StartsWith, "Starts With"),
            (ConditionOperator.EndsWith, "Ends With"),
            (ConditionOperator.MatchesRegex, "Matches Regex"),
            (ConditionOperator.IsEmpty, "Is Empty"),
            (ConditionOperator.IsNotEmpty, "Is Not Empty"),
            (ConditionOperator.GreaterThan, "Greater Than (>)"),
            (ConditionOperator.LessThan, "Less Than (<)"),
            (ConditionOperator.GreaterOrEqual, "Greater or Equal (≥)"),
            (ConditionOperator.LessOrEqual, "Less or Equal (≤)"),
            (ConditionOperator.FileExists, "File Exists"),
            (ConditionOperator.DirectoryExists, "Directory Exists"),
            (ConditionOperator.ProcessIsRunning, "Process is Running")
        };

        int selIdx = 0;
        for (int i = 0; i < operators.Length; i++)
        {
            opCombo.Items.Add(new ComboBoxItem 
            { 
                Content = operators[i].Label, 
                Tag = operators[i].Op,
                FontSize = 12.5,
                Padding = new Thickness(8, 4, 8, 4)
            });
            if (operators[i].Op == step.ConditionOperator)
            {
                selIdx = i;
            }
        }
        opCombo.SelectedIndex = selIdx;

        opStack.Children.Add(opCombo);
        Grid.SetColumn(opStack, 2);
        exprGrid.Children.Add(opStack);

        // Right Operand Stack
        var rightStack = new StackPanel();
        var rightLabel = new TextBlock { Text = "Compare Against", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) };
        rightStack.Children.Add(rightLabel);
        var rightBox = new TextBox
        {
            Text = step.ConditionRight ?? string.Empty,
            Height = 32,
            Padding = new Thickness(8, 4, 8, 4),
            VerticalContentAlignment = VerticalAlignment.Center,
            Style = Application.Current.TryFindResource("ModernTextBoxStyle") as Style,
            Tag = "e.g. completed, 100, or {target}"
        };
        rightStack.Children.Add(rightBox);
        RenderVariableChips(rightStack, rightBox, availableVariables);
        Grid.SetColumn(rightStack, 4);
        exprGrid.Children.Add(rightStack);

        // Helper to update right operand visibility for unary operators
        void UpdateRightOperandVisibility()
        {
            bool isUnary = ConditionEvaluator.IsUnary(step.ConditionOperator);
            rightStack.Visibility = isUnary ? Visibility.Collapsed : Visibility.Visible;
            rightCol.Width = isUnary ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        }
        UpdateRightOperandVisibility();

        leftBox.TextChanged += (s, e) =>
        {
            step.ConditionLeft = leftBox.Text;
            summaryText.Text = GetStepLiveSummary(step);
            OnFormEdited();
        };

        rightBox.TextChanged += (s, e) =>
        {
            step.ConditionRight = rightBox.Text;
            summaryText.Text = GetStepLiveSummary(step);
            OnFormEdited();
        };

        opCombo.SelectionChanged += (s, e) =>
        {
            if (opCombo.SelectedItem is ComboBoxItem item && item.Tag is ConditionOperator op)
            {
                step.ConditionOperator = op;
                UpdateRightOperandVisibility();
                summaryText.Text = GetStepLiveSummary(step);
                OnFormEdited();
            }
        };

        condStack.Children.Add(exprGrid);

        // Options row: Ignore case checkbox
        var optsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var ignoreCaseCheck = new CheckBox
        {
            Content = "Ignore Case (case-insensitive comparison)",
            IsChecked = step.ConditionIgnoreCase,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Treat uppercase and lowercase letters as equal during comparison"
        };
        ignoreCaseCheck.Checked += (s, e) => { step.ConditionIgnoreCase = true; OnFormEdited(); };
        ignoreCaseCheck.Unchecked += (s, e) => { step.ConditionIgnoreCase = false; OnFormEdited(); };
        optsRow.Children.Add(ignoreCaseCheck);
        condStack.Children.Add(optsRow);

        container.Children.Add(condCard);

        // 2. Render THEN Branch Container
        RenderBranchContainer(container, step, step.ThenSteps, "THEN", "When Condition is True", Color.FromRgb(0x10, 0xB9, 0x81), availableVariables, summaryText, isElse: false);

        // 3. Render ELSE Branch Container
        if (step.HasElseBranch)
        {
            RenderBranchContainer(container, step, step.ElseSteps, "ELSE", "When Condition is False", Color.FromRgb(0xF5, 0x9E, 0x0B), availableVariables, summaryText, isElse: true);
        }
        else
        {
            var addElseCard = new Border
            {
                BorderBrush = Application.Current.TryFindResource("CardBorderBrush") as Brush ?? Brushes.Gray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 4, 0, 4),
                Background = Brushes.Transparent,
                AllowDrop = true
            };
            SetIsWorkflowDropTarget(addElseCard, true);

            var defaultAddElseBorder = addElseCard.BorderBrush;
            addElseCard.PreviewDragEnter += (s, e) =>
            {
                if (e.Data.GetDataPresent("WorkflowStepDragData"))
                {
                    UpdateWorkflowGhostPosition(e);
                    e.Effects = DragDropEffects.Move;
                    e.Handled = true;
                }
            };
            addElseCard.PreviewDragOver += (s, e) =>
            {
                UpdateWorkflowGhostPosition(e);
                if (e.Data.GetDataPresent("WorkflowStepDragData"))
                {
                    var dragData = e.Data.GetData("WorkflowStepDragData") as WorkflowStepDragData;
                    if (dragData != null && dragData.Step.Id != step.Id)
                    {
                        var amber = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
                        addElseCard.BorderBrush = amber;
                        addElseCard.BorderThickness = new Thickness(2);
                        SetWorkflowGhostAction("Move into ELSE branch (Enable)", _arrowBranchGeometry, amber);
                        e.Effects = DragDropEffects.Move;
                        e.Handled = true;
                        return;
                    }
                }
                addElseCard.BorderBrush = defaultAddElseBorder;
                addElseCard.BorderThickness = new Thickness(1);
                ClearWorkflowGhostAction();
                e.Effects = DragDropEffects.None;
            };

            addElseCard.PreviewDragLeave += (s, e) =>
            {
                if (!IsCursorPhysicallyOver(addElseCard))
                {
                    addElseCard.BorderBrush = defaultAddElseBorder;
                    addElseCard.BorderThickness = new Thickness(1);
                    ClearWorkflowGhostAction();
                }
            };

            addElseCard.PreviewDrop += (s, e) =>
            {
                addElseCard.BorderBrush = defaultAddElseBorder;
                addElseCard.BorderThickness = new Thickness(1);
                ClearWorkflowGhostAction();
                if (e.Data.GetDataPresent("WorkflowStepDragData"))
                {
                    var dragData = e.Data.GetData("WorkflowStepDragData") as WorkflowStepDragData;
                    if (dragData != null && dragData.Step.Id != step.Id)
                    {
                        step.HasElseBranch = true;
                        step.ElseSteps ??= [];
                        ExecuteStepDrop(dragData, step.ElseSteps, 0, step);
                        e.Handled = true;
                    }
                }
            };

            var addElseStack = new StackPanel { Orientation = Orientation.Horizontal };
            var addElseBtn = new Button
            {
                Content = "+ Add Else Branch (Optional)",
                Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
                FontSize = 11.5,
                Padding = new Thickness(10, 4, 10, 4),
                ToolTip = "Add steps to execute when the condition evaluates to False"
            };
            addElseBtn.Click += (s, e) =>
            {
                step.HasElseBranch = true;
                summaryText.Text = GetStepLiveSummary(step);
                RebuildWorkflowStepCards();
                OnFormEdited();
            };
            addElseStack.Children.Add(addElseBtn);
            var noteText = new TextBlock
            {
                Text = "If condition is False and no Else branch is defined, the workflow continues to the next step.",
                FontSize = 11,
                Foreground = Application.Current.TryFindResource("TextMutedBrush") as Brush ?? Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            addElseStack.Children.Add(noteText);
            addElseCard.Child = addElseStack;
            container.Children.Add(addElseCard);
        }
    }

    private void RenderBranchContainer(
        StackPanel parentContainer,
        WorkflowStep parentStep,
        List<WorkflowStep> branchSteps,
        string branchTitle,
        string branchSubtitle,
        Color accentColor,
        List<string> availableVariables,
        TextBlock summaryText,
        bool isElse)
    {
        var accentBrush = new SolidColorBrush(accentColor);
        var subtleBg = new SolidColorBrush(Color.FromArgb(0x0C, accentColor.R, accentColor.G, accentColor.B));

        var branchCard = new Border
        {
            Background = subtleBg,
            BorderBrush = Application.Current.TryFindResource("CardBorderBrush") as Brush ?? Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 10),
            Margin = new Thickness(0, 4, 0, 6),
            AllowDrop = true
        };
        SetIsWorkflowDropTarget(branchCard, true);

        var defaultBranchBorder = branchCard.BorderBrush;
        branchCard.PreviewDragEnter += (s, e) =>
        {
            if (IsOverChildDropTarget(branchCard, e.OriginalSource))
            {
                return;
            }

            if (e.Data.GetDataPresent("WorkflowStepDragData"))
            {
                UpdateWorkflowGhostPosition(e);
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
        };
        branchCard.PreviewDragOver += (s, e) =>
        {
            UpdateWorkflowGhostPosition(e);

            if (e.Data.GetDataPresent("WorkflowStepDragData"))
            {
                var dragData = e.Data.GetData("WorkflowStepDragData") as WorkflowStepDragData;
                if (dragData != null && dragData.Step.Id != parentStep.Id)
                {
                    branchCard.BorderBrush = accentBrush;
                    branchCard.BorderThickness = new Thickness(2);

                    if (IsOverChildDropTarget(branchCard, e.OriginalSource))
                    {
                        // Over a nested child step card: let the child step handle the drop line indicator & ghost action
                        return;
                    }

                    SetWorkflowGhostAction($"Move into {branchTitle} branch", _arrowBranchGeometry, accentBrush);
                    e.Effects = DragDropEffects.Move;
                    e.Handled = true;
                    return;
                }
            }
            branchCard.BorderBrush = defaultBranchBorder;
            branchCard.BorderThickness = new Thickness(1);
            ClearWorkflowGhostAction();
            e.Effects = DragDropEffects.None;
        };

        branchCard.PreviewDragLeave += (s, e) =>
        {
            if (!IsCursorPhysicallyOver(branchCard))
            {
                branchCard.BorderBrush = defaultBranchBorder;
                branchCard.BorderThickness = new Thickness(1);
                ClearWorkflowGhostAction();
            }
        };

        branchCard.PreviewDrop += (s, e) =>
        {
            if (IsOverChildDropTarget(branchCard, e.OriginalSource))
            {
                branchCard.BorderBrush = defaultBranchBorder;
                branchCard.BorderThickness = new Thickness(1);
                return;
            }

            branchCard.BorderBrush = defaultBranchBorder;
            branchCard.BorderThickness = new Thickness(1);
            ClearWorkflowGhostAction();
            if (e.Data.GetDataPresent("WorkflowStepDragData"))
            {
                var dragData = e.Data.GetData("WorkflowStepDragData") as WorkflowStepDragData;
                if (dragData != null && dragData.Step.Id != parentStep.Id)
                {
                    ExecuteStepDrop(dragData, branchSteps, branchSteps.Count, parentStep);
                    e.Handled = true;
                }
            }
        };

        // Left accent rail indicator
        var railGrid = new Grid();
        railGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Pixel) });
        railGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Pixel) });
        railGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var rail = new Border
        {
            Background = accentBrush,
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetColumn(rail, 0);
        railGrid.Children.Add(rail);

        var contentStack = new StackPanel();
        Grid.SetColumn(contentStack, 2);
        railGrid.Children.Add(contentStack);
        branchCard.Child = railGrid;

        // Branch Header
        var headerDock = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = false };

        var leftTitleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x28, accentColor.R, accentColor.G, accentColor.B)),
            BorderBrush = accentBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 8, 0)
        };
        badge.Child = new TextBlock
        {
            Text = branchTitle,
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Foreground = accentBrush
        };
        leftTitleStack.Children.Add(badge);

        var descText = new TextBlock
        {
            Text = branchSubtitle,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Application.Current.TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White
        };
        leftTitleStack.Children.Add(descText);

        var countText = new TextBlock
        {
            Text = $"({branchSteps.Count} step{(branchSteps.Count == 1 ? "" : "s")})",
            FontSize = 11,
            Foreground = Application.Current.TryFindResource("TextMutedBrush") as Brush ?? Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        leftTitleStack.Children.Add(countText);

        DockPanel.SetDock(leftTitleStack, Dock.Left);
        headerDock.Children.Add(leftTitleStack);

        if (isElse)
        {
            var removeElseBtn = new Button
            {
                Content = "✕ Remove Else",
                Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
                FontSize = 10.5,
                Padding = new Thickness(6, 1, 6, 1),
                ToolTip = "Remove Else branch and all steps within it"
            };
            removeElseBtn.Click += (s, e) =>
            {
                if (branchSteps.Count > 0)
                {
                    bool confirmed = ModernMessageDialog.ShowConfirm(this,
                        "Remove Else Branch",
                        $"Are you sure you want to remove the Else branch? Its {branchSteps.Count} step(s) will be deleted.",
                        "Remove Branch",
                        "Cancel");
                    if (!confirmed) return;
                }
                parentStep.HasElseBranch = false;
                parentStep.ElseSteps.Clear();
                summaryText.Text = GetStepLiveSummary(parentStep);
                RebuildWorkflowStepCards();
                OnFormEdited();
            };
            DockPanel.SetDock(removeElseBtn, Dock.Right);
            headerDock.Children.Add(removeElseBtn);
        }

        contentStack.Children.Add(headerDock);

        // Branch Sub-Steps Host
        var stepsStack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        if (branchSteps.Count == 0)
        {
            var emptyPlaceholder = new Border
            {
                BorderBrush = Application.Current.TryFindResource("CardBorderBrush") as Brush ?? Brushes.Gray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 6),
                Background = Brushes.Transparent
            };
            var placeholderText = new TextBlock
            {
                Text = $"No steps in {branchTitle} branch yet. Click '+ Add Step to {branchTitle}' below.",
                FontSize = 11.5,
                Foreground = Application.Current.TryFindResource("TextMutedBrush") as Brush ?? Brushes.Gray,
                FontStyle = FontStyles.Italic,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            emptyPlaceholder.Child = placeholderText;
            stepsStack.Children.Add(emptyPlaceholder);
        }
        else
        {
            for (int i = 0; i < branchSteps.Count; i++)
            {
                var subStep = branchSteps[i];
                var subCard = CreateNestedStepCard(subStep, i, branchSteps, availableVariables, accentColor, parentStep, summaryText);
                stepsStack.Children.Add(subCard);
            }
        }

        contentStack.Children.Add(stepsStack);

        // Add Step to Branch button
        var addStepBtn = new Button
        {
            Content = $"+ Add Step to {branchTitle} ▾",
            Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
            FontSize = 11,
            Padding = new Thickness(10, 3, 10, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = $"Add a new workflow action inside the {branchTitle} branch"
        };
        addStepBtn.Click += (s, e) => ShowAddBranchStepContextMenu(addStepBtn, branchSteps, summaryText, parentStep);
        contentStack.Children.Add(addStepBtn);

        parentContainer.Children.Add(branchCard);
    }

    private FrameworkElement CreateNestedStepCard(
        WorkflowStep step,
        int subIndex,
        List<WorkflowStep> branchSteps,
        List<string> availableVariables,
        Color accentColor,
        WorkflowStep parentStep,
        TextBlock parentSummaryText)
    {
        bool isFirst = subIndex == 0;
        bool isLast = subIndex == branchSteps.Count - 1;

        var card = new Border
        {
            Background = Application.Current.TryFindResource("CardBgBrush") as Brush ?? Brushes.DarkSlateGray,
            BorderBrush = Application.Current.TryFindResource("CardBorderBrush") as Brush ?? Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 4, 0, 4)
        };

        var mainStack = new StackPanel();
        card.Child = mainStack;

        // Sub-Step Header
        var headerGrid = new Grid
        {
            Margin = new Thickness(0, 0, 0, step.IsCollapsed ? 0 : 8),
            Background = Brushes.Transparent
        };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Left Header: Drag grip + Sub-Index badge + Icon & Name + Live summary
        var leftHeader = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Double-click to " + (step.IsCollapsed ? "expand" : "collapse")
        };

        var dragGrip = new TextBlock
        {
            Text = "⠿",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = Application.Current.TryFindResource("TextMutedBrush") as Brush ?? Brushes.Gray,
            Cursor = Cursors.SizeAll,
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Drag to reorder or move into/out of conditions"
        };
        WireWorkflowStepDragSource(dragGrip, step, branchSteps, parentStep);
        leftHeader.Children.Add(dragGrip);

        var indexBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x20, accentColor.R, accentColor.G, accentColor.B)),
            BorderBrush = new SolidColorBrush(accentColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Width = 20,
            Height = 20,
            Margin = new Thickness(0, 0, 6, 0)
        };
        indexBadge.Child = new TextBlock
        {
            Text = (subIndex + 1).ToString(),
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(accentColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        leftHeader.Children.Add(indexBadge);

        var typeText = new TextBlock
        {
            Text = GetStepTypeIconAndName(step.StepType),
            FontWeight = FontWeights.SemiBold,
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Application.Current.TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White,
            Margin = new Thickness(0, 0, 8, 0)
        };
        leftHeader.Children.Add(typeText);

        var summaryBlock = new TextBlock
        {
            Text = GetStepLiveSummary(step),
            FontSize = 11,
            Foreground = Application.Current.TryFindResource("TextMutedBrush") as Brush ?? Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 250
        };
        leftHeader.Children.Add(summaryBlock);

        Grid.SetColumn(leftHeader, 0);
        headerGrid.Children.Add(leftHeader);

        // Right Header: Reorder up/down + Kebab menu
        var rightHeader = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var upBtn = new Button
        {
            Content = "▲",
            FontSize = 9.5,
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(0, 0, 2, 0),
            IsEnabled = !isFirst,
            Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
            ToolTip = "Move step up"
        };
        upBtn.Click += (s, e) => MoveBranchStep(branchSteps, step, -1);
        rightHeader.Children.Add(upBtn);

        var downBtn = new Button
        {
            Content = "▼",
            FontSize = 9.5,
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(0, 0, 4, 0),
            IsEnabled = !isLast,
            Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
            ToolTip = "Move step down"
        };
        downBtn.Click += (s, e) => MoveBranchStep(branchSteps, step, 1);
        rightHeader.Children.Add(downBtn);

        // Kebab menu
        var kebabMenu = new ContextMenu();
        if (step.StepType == WorkflowStepType.IfCondition)
        {
            var testCondItem = new MenuItem { Header = "▶  Test Condition Live", FontSize = 12 };
            testCondItem.Click += (s, e) => TestConditionLive(step);
            kebabMenu.Items.Add(testCondItem);
        }
        else
        {
            var testItem = new MenuItem { Header = "▶  Test Step", FontSize = 12 };
            testItem.Click += async (s, e) => await TestSingleWorkflowStepAsync(step, subIndex + 1);
            kebabMenu.Items.Add(testItem);
        }

        // Cut / Copy
        var cutItem = new MenuItem { Header = "✂  Cut Step", FontSize = 12 };
        cutItem.Click += (s, e) => CutStep(step, branchSteps, parentStep);
        kebabMenu.Items.Add(cutItem);

        var copyItem = new MenuItem { Header = "⧉  Copy Step", FontSize = 12 };
        copyItem.Click += (s, e) => CopyStep(step);
        kebabMenu.Items.Add(copyItem);

        var dupItem = new MenuItem { Header = "📄  Duplicate", FontSize = 12 };
        dupItem.Click += (s, e) => DuplicateBranchStep(branchSteps, step, parentStep, parentSummaryText);
        kebabMenu.Items.Add(dupItem);

        // Move Out options
        var moveOutBefore = new MenuItem { Header = "⬆  Move Out (Before Condition)", FontSize = 12 };
        moveOutBefore.Click += (s, e) => MoveStepOutOfBranch(step, branchSteps, parentStep, before: true);
        kebabMenu.Items.Add(moveOutBefore);

        var moveOutAfter = new MenuItem { Header = "⬇  Move Out (After Condition)", FontSize = 12 };
        moveOutAfter.Click += (s, e) => MoveStepOutOfBranch(step, branchSteps, parentStep, before: false);
        kebabMenu.Items.Add(moveOutAfter);

        // Move between branches of parent condition
        bool isInElse = branchSteps == parentStep.ElseSteps;
        if (isInElse)
        {
            var moveToThen = new MenuItem { Header = "⇄  Move to THEN Branch", FontSize = 12 };
            moveToThen.Click += (s, e) => MoveStepBetweenBranches(step, parentStep.ElseSteps, parentStep.ThenSteps, parentStep, targetIsElse: false);
            kebabMenu.Items.Add(moveToThen);
        }
        else
        {
            var moveToElse = new MenuItem { Header = "⇄  Move to ELSE Branch", FontSize = 12 };
            moveToElse.Click += (s, e) => MoveStepBetweenBranches(step, parentStep.ThenSteps, parentStep.ElseSteps, parentStep, targetIsElse: true);
            kebabMenu.Items.Add(moveToElse);
        }

        // Move into another condition (if other condition steps exist)
        if (_selectedItem?.Payload.WorkflowSteps != null)
        {
            var otherIfSteps = _selectedItem.Payload.WorkflowSteps
                .Select((s, i) => (Step: s, Index: i + 1))
                .Where(x => x.Step.StepType == WorkflowStepType.IfCondition && x.Step.Id != parentStep.Id && x.Step.Id != step.Id)
                .ToList();

            if (otherIfSteps.Count > 0)
            {
                var moveOtherCondSub = new MenuItem { Header = "↳  Move into Another Condition ▾", FontSize = 12 };
                foreach (var ifItem in otherIfSteps)
                {
                    var targetIf = ifItem.Step;
                    var condMenu = new MenuItem
                    {
                        Header = $"Step {ifItem.Index}: {GetStepLiveSummary(targetIf)}",
                        FontSize = 11.5
                    };

                    var intoThen = new MenuItem { Header = "↳ Into THEN Branch", FontSize = 11.5 };
                    intoThen.Click += (s, e) => MoveStepToConditionBranch(step, branchSteps, targetIf, isElse: false);
                    condMenu.Items.Add(intoThen);

                    var intoElse = new MenuItem { Header = "↳ Into ELSE Branch", FontSize = 11.5 };
                    intoElse.Click += (s, e) => MoveStepToConditionBranch(step, branchSteps, targetIf, isElse: true);
                    condMenu.Items.Add(intoElse);

                    moveOtherCondSub.Items.Add(condMenu);
                }
                kebabMenu.Items.Add(moveOtherCondSub);
            }
        }

        kebabMenu.Items.Add(new Separator());

        var deleteItem = new MenuItem
        {
            Header = "🗑  Delete",
            FontSize = 12,
            Foreground = Application.Current.TryFindResource("ErrorBrush") as Brush ?? Brushes.Red
        };
        deleteItem.Click += (s, e) => DeleteBranchStep(branchSteps, step, parentStep, parentSummaryText);
        kebabMenu.Items.Add(deleteItem);

        var kebabBtn = new Button
        {
            Content = "⋮",
            FontSize = 13,
            Padding = new Thickness(6, 1, 6, 1),
            Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
            ToolTip = "Sub-step actions",
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

        // Double click header to collapse/expand
        headerGrid.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ClickCount == 2)
            {
                if (e.OriginalSource is DependencyObject dep && rightHeader.IsAncestorOf(dep)) return;
                step.IsCollapsed = !step.IsCollapsed;
                RebuildWorkflowStepCards();
                OnFormEdited();
            }
        };

        mainStack.Children.Add(headerGrid);

        // Sub-Step Body (when expanded)
        if (!step.IsCollapsed)
        {
            var bodyContainer = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            PopulateStepTypeControls(bodyContainer, step, summaryBlock, availableVariables);
            mainStack.Children.Add(bodyContainer);
        }

        var nestedWrapperGrid = new Grid { Margin = new Thickness(0, 2, 0, 4) };
        nestedWrapperGrid.Children.Add(card);

        var accentBrush = new SolidColorBrush(accentColor);
        var topIndicator = CreateDropIndicatorLine(accentBrush);
        topIndicator.VerticalAlignment = VerticalAlignment.Top;
        topIndicator.Margin = new Thickness(0, 1, 0, 0);
        nestedWrapperGrid.Children.Add(topIndicator);

        var bottomIndicator = CreateDropIndicatorLine(accentBrush);
        bottomIndicator.VerticalAlignment = VerticalAlignment.Bottom;
        bottomIndicator.Margin = new Thickness(0, 0, 0, 1);
        nestedWrapperGrid.Children.Add(bottomIndicator);

        WireWorkflowStepDropTarget(card, step, branchSteps, parentStep, topIndicator, bottomIndicator, accentBrush);

        return nestedWrapperGrid;
    }

    private void MoveBranchStep(List<WorkflowStep> branchSteps, WorkflowStep step, int direction)
    {
        int idx = branchSteps.IndexOf(step);
        if (idx < 0) return;
        int newIdx = idx + direction;
        if (newIdx >= 0 && newIdx < branchSteps.Count)
        {
            branchSteps.RemoveAt(idx);
            branchSteps.Insert(newIdx, step);
            RebuildWorkflowStepCards();
            OnFormEdited();
        }
    }

    private void DuplicateBranchStep(List<WorkflowStep> branchSteps, WorkflowStep step, WorkflowStep parentStep, TextBlock parentSummaryText)
    {
        int idx = branchSteps.IndexOf(step);
        if (idx < 0) return;
        var cloned = step.Clone();
        branchSteps.Insert(idx + 1, cloned);
        if (parentSummaryText != null && parentStep != null)
        {
            parentSummaryText.Text = GetStepLiveSummary(parentStep);
        }
        RebuildWorkflowStepCards();
        OnFormEdited();
    }

    private void DeleteBranchStep(List<WorkflowStep> branchSteps, WorkflowStep step, WorkflowStep parentStep, TextBlock parentSummaryText)
    {
        branchSteps.Remove(step);
        if (parentSummaryText != null && parentStep != null)
        {
            parentSummaryText.Text = GetStepLiveSummary(parentStep);
        }
        RebuildWorkflowStepCards();
        OnFormEdited();
    }

    private void ShowAddBranchStepContextMenu(Button targetBtn, List<WorkflowStep> branchSteps, TextBlock summaryText, WorkflowStep parentStep)
    {
        var menu = new ContextMenu();

        if (_stepClipboard != null)
        {
            var parts = SplitIconAndName(GetStepTypeIconAndName(_stepClipboard.StepType));
            var pasteItem = new MenuItem
            {
                Header = $"📋  Paste Step: \"{_stepClipboard.Name}\" ({parts.Icon})",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Application.Current.TryFindResource("WorkflowBrush") as Brush ?? Brushes.Purple
            };
            pasteItem.Click += (s, e) => PasteStepAt(branchSteps, branchSteps.Count, parentStep);
            menu.Items.Add(pasteItem);
            menu.Items.Add(new Separator());
        }

        var stepTypes = new (string Title, WorkflowStepType Type, string Icon)[]
        {
            ("Set Variable", WorkflowStepType.SetVariable, "🔤"),
            ("If / Condition Branch", WorkflowStepType.IfCondition, "🔀"),
            ("Prompt for User Input", WorkflowStepType.Prompt, "💬"),
            ("Show Dialog / Confirmation", WorkflowStepType.Dialog, "💬"),
            ("Open URL / Web Page", WorkflowStepType.OpenUrl, "🌐"),
            ("Launch Application / Command", WorkflowStepType.LaunchApp, "⚡"),
            ("Ensure Folder Exists", WorkflowStepType.EnsureDirectory, "📁"),
            ("Paste / Insert Text Snippet", WorkflowStepType.InjectSnippet, "📝"),
            ("Recorded Macro Sequence", WorkflowStepType.Macro, "🔴"),
            ("Delay / Pause Execution", WorkflowStepType.Delay, "⏱"),
            ("Execute Action / Folder", WorkflowStepType.ExecuteAction, "⚡"),
            ("Inline JavaScript", WorkflowStepType.RunScript, "📜")
        };

        foreach (var (title, stepType, icon) in stepTypes)
        {
            var item = new MenuItem
            {
                Header = $"{icon}  {title}",
                FontSize = 12
            };
            item.Click += (s, e) =>
            {
                var newStep = CreateDefaultStep(stepType);
                branchSteps.Add(newStep);
                summaryText.Text = GetStepLiveSummary(parentStep);
                RebuildWorkflowStepCards();
                OnFormEdited();
            };
            menu.Items.Add(item);
        }

        menu.PlacementTarget = targetBtn;
        menu.IsOpen = true;
    }

    private void TestConditionLive(WorkflowStep step)
    {
        if (_selectedItem?.Payload == null) return;
        CommitCurrentFormChanges();

        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_selectedItem.Payload.WorkflowVariables != null)
        {
            foreach (var v in _selectedItem.Payload.WorkflowVariables)
            {
                if (!string.IsNullOrWhiteSpace(v.Name))
                {
                    vars[v.Name] = v.Value ?? string.Empty;
                }
            }
        }

        string leftResolved = WorkflowExecutor.ResolveVariables(step.ConditionLeft ?? string.Empty, vars);
        string rightResolved = WorkflowExecutor.ResolveVariables(step.ConditionRight ?? string.Empty, vars);

        bool result = ConditionEvaluator.Evaluate(
            leftResolved,
            step.ConditionOperator,
            rightResolved,
            step.ConditionIgnoreCase);

        string branch = result ? "THEN branch" : (step.HasElseBranch ? "ELSE branch" : "None (condition False, no ELSE branch)");
        int stepCount = result ? (step.ThenSteps?.Count ?? 0) : (step.HasElseBranch ? (step.ElseSteps?.Count ?? 0) : 0);

        string rightLine = ConditionEvaluator.IsUnary(step.ConditionOperator)
            ? string.Empty
            : $"• Right Operand: \"{step.ConditionRight}\" → \"{rightResolved}\"\n";

        string msg = $"Condition Evaluation Live Result:\n\n" +
                     $"• Left Operand: \"{step.ConditionLeft}\" → \"{leftResolved}\"\n" +
                     $"• Operator: {step.ConditionOperator} (Ignore Case: {step.ConditionIgnoreCase})\n" +
                     rightLine +
                     $"\n▶ Evaluated Result: {(result ? "✔ TRUE" : "✖ FALSE")}\n" +
                     $"▶ Branch Selected: {branch} ({stepCount} sub-step{(stepCount == 1 ? "" : "s")})";

        ModernMessageDialog.ShowAlert(this, "Live Condition Test", msg, ModernDialogType.Info);
    }

    private static (string Icon, string Name) SplitIconAndName(string full)
    {
        if (string.IsNullOrWhiteSpace(full)) return ("⚡", "Step");
        int space = full.IndexOf(' ');
        if (space > 0)
        {
            return (full.Substring(0, space).Trim(), full.Substring(space + 1).Trim());
        }
        return ("⚡", full);
    }

    private static FrameworkElement CreateDropIndicatorLine(Brush lineBrush)
    {
        var grid = new Grid
        {
            Height = 8,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        Panel.SetZIndex(grid, 20);

        var line = new Border
        {
            Height = 3,
            Background = lineBrush,
            CornerRadius = new CornerRadius(1.5),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };

        var bead = new System.Windows.Shapes.Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = lineBrush,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };

        grid.Children.Add(line);
        grid.Children.Add(bead);
        return grid;
    }

    private void SetWorkflowGhostAction(string actionText, Geometry iconGeo, Brush? iconBrush = null)
    {
        if (WorkflowStepDragActionBadge == null || WorkflowStepDragActionIcon == null || WorkflowStepDragActionText == null) return;
        WorkflowStepDragActionBadge.Visibility = Visibility.Visible;
        WorkflowStepDragActionIcon.Data = iconGeo;
        if (iconBrush != null)
        {
            WorkflowStepDragActionIcon.Fill = iconBrush;
        }
        WorkflowStepDragActionText.Text = actionText;
    }

    private void ClearWorkflowGhostAction()
    {
        if (WorkflowStepDragActionBadge != null)
        {
            WorkflowStepDragActionBadge.Visibility = Visibility.Collapsed;
        }
    }

    private void CheckWorkflowDragAutoScroll()
    {
        if (EditorScrollViewer == null) return;
        try
        {
            var svPos = EditorScrollViewer.PointFromScreen(_lastWorkflowDragScreenPoint);
            double height = EditorScrollViewer.ActualHeight;
            if (height <= 0) return;

            const double threshold = 50.0;
            if (svPos.Y >= 0 && svPos.Y < threshold)
            {
                double intensity = 1.0 - (svPos.Y / threshold);
                double scrollDelta = Math.Max(2, intensity * 20);
                EditorScrollViewer.ScrollToVerticalOffset(Math.Max(0, EditorScrollViewer.VerticalOffset - scrollDelta));
            }
            else if (svPos.Y > height - threshold && svPos.Y <= height)
            {
                double intensity = 1.0 - ((height - svPos.Y) / threshold);
                double scrollDelta = Math.Max(2, intensity * 20);
                EditorScrollViewer.ScrollToVerticalOffset(Math.Min(EditorScrollViewer.ScrollableHeight, EditorScrollViewer.VerticalOffset + scrollDelta));
            }
        }
        catch
        {
            // Ignore coordinate mapping errors if window state changed
        }
    }

    private void EditorScrollViewer_PreviewDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("WorkflowStepDragData"))
        {
            UpdateWorkflowGhostPosition(e);
            e.Effects = DragDropEffects.Move;
        }
    }

    private void EditorScrollViewer_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("WorkflowStepDragData"))
        {
            UpdateWorkflowGhostPosition(e);
            e.Effects = DragDropEffects.Move;
        }
    }

    private void UpdateWorkflowGhostPosition(DragEventArgs e)
    {
        var screenPt = PointToScreen(e.GetPosition(this));
        _lastWorkflowDragScreenPoint = screenPt;
        if (WorkflowStepDragGhostPopup != null && WorkflowStepDragGhostPopup.IsOpen)
        {
            WorkflowStepDragGhostPopup.HorizontalOffset = screenPt.X + 14;
            WorkflowStepDragGhostPopup.VerticalOffset = screenPt.Y + 14;
        }
    }

    private void ClearWorkflowGhost()
    {
        _workflowDragScrollTimer?.Stop();
        _workflowDragScrollTimer = null;
        if (WorkflowStepDragGhostPopup != null)
        {
            WorkflowStepDragGhostPopup.IsOpen = false;
        }
        ClearWorkflowGhostAction();
        _workflowStepDragStartPoint = null;
        _draggedStepData = null;
    }

    private void WireWorkflowStepDragSource(FrameworkElement dragElement, WorkflowStep step, List<WorkflowStep> sourceList, WorkflowStep? parentIfStep)
    {
        dragElement.PreviewMouseLeftButtonDown += (s, e) =>
        {
            _workflowStepDragStartPoint = e.GetPosition(this);
            _draggedStepData = new WorkflowStepDragData(step, sourceList, parentIfStep);
        };

        dragElement.PreviewMouseMove += (s, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || _workflowStepDragStartPoint == null || _draggedStepData == null)
            {
                return;
            }

            var currentPoint = e.GetPosition(this);
            var diff = _workflowStepDragStartPoint.Value - currentPoint;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (WorkflowStepDragGhostPopup != null)
                {
                    string fullType = GetStepTypeIconAndName(_draggedStepData.Step.StepType);
                    var parts = SplitIconAndName(fullType);
                    WorkflowStepDragGhostIcon.Text = parts.Icon;
                    WorkflowStepDragGhostText.Text = !string.IsNullOrWhiteSpace(_draggedStepData.Step.Name) ? _draggedStepData.Step.Name : parts.Name;

                    var screenPt = PointToScreen(e.GetPosition(this));
                    _lastWorkflowDragScreenPoint = screenPt;
                    WorkflowStepDragGhostPopup.HorizontalOffset = screenPt.X + 14;
                    WorkflowStepDragGhostPopup.VerticalOffset = screenPt.Y + 14;
                    WorkflowStepDragGhostPopup.IsOpen = true;
                }

                _workflowDragScrollTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(25)
                };
                _workflowDragScrollTimer.Tick += (timerSender, timerArgs) => CheckWorkflowDragAutoScroll();
                _workflowDragScrollTimer.Start();

                var data = new DataObject("WorkflowStepDragData", _draggedStepData);
                GiveFeedbackEventHandler giveFeedbackHandler = (s, e) =>
                {
                    if (_draggedStepData != null)
                    {
                        e.UseDefaultCursors = false;
                        Mouse.SetCursor(Cursors.Arrow);
                        e.Handled = true;
                    }
                };
                dragElement.GiveFeedback += giveFeedbackHandler;
                try
                {
                    DragDrop.DoDragDrop(dragElement, data, DragDropEffects.Move);
                }
                finally
                {
                    dragElement.GiveFeedback -= giveFeedbackHandler;
                    _workflowDragScrollTimer?.Stop();
                    _workflowDragScrollTimer = null;
                    ClearWorkflowGhost();
                }
            }
        };
    }

    internal static bool IsOverChildDropTarget(FrameworkElement currentTarget, object? hitSource)
    {
        DependencyObject? current = hitSource as DependencyObject;
        if (current is FrameworkContentElement fce)
        {
            current = fce.Parent;
        }
        while (current != null && current != currentTarget)
        {
            if (current is FrameworkElement fe && fe != currentTarget && GetIsWorkflowDropTarget(fe))
            {
                return true;
            }
            if (current is Visual || current is System.Windows.Media.Media3D.Visual3D)
            {
                current = VisualTreeHelper.GetParent(current);
            }
            else if (current is FrameworkContentElement contentElem)
            {
                current = contentElem.Parent;
            }
            else
            {
                break;
            }
        }
        return false;
    }

    private void WireWorkflowStepDropTarget(
        FrameworkElement hitElement,
        WorkflowStep targetStep,
        List<WorkflowStep> targetList,
        WorkflowStep? parentIfStep,
        FrameworkElement topIndicator,
        FrameworkElement bottomIndicator,
        Brush accentBrush)
    {
        hitElement.AllowDrop = true;
        SetIsWorkflowDropTarget(hitElement, true);

        hitElement.PreviewDragEnter += (s, e) =>
        {
            if (IsOverChildDropTarget(hitElement, e.OriginalSource))
            {
                return;
            }

            if (e.Data.GetDataPresent("WorkflowStepDragData"))
            {
                UpdateWorkflowGhostPosition(e);
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
        };

        hitElement.PreviewDragOver += (s, e) =>
        {
            UpdateWorkflowGhostPosition(e);

            if (IsOverChildDropTarget(hitElement, e.OriginalSource))
            {
                topIndicator.Visibility = Visibility.Collapsed;
                bottomIndicator.Visibility = Visibility.Collapsed;
                return;
            }

            if (!e.Data.GetDataPresent("WorkflowStepDragData"))
            {
                topIndicator.Visibility = Visibility.Collapsed;
                bottomIndicator.Visibility = Visibility.Collapsed;
                ClearWorkflowGhostAction();
                e.Effects = DragDropEffects.None;
                return;
            }

            var dragData = e.Data.GetData("WorkflowStepDragData") as WorkflowStepDragData;
            if (dragData == null || dragData.Step.Id == targetStep.Id)
            {
                topIndicator.Visibility = Visibility.Collapsed;
                bottomIndicator.Visibility = Visibility.Collapsed;
                ClearWorkflowGhostAction();
                e.Effects = DragDropEffects.None;
                return;
            }

            // Prevent dragging an IfCondition into its own sub-branches
            if (dragData.Step.StepType == WorkflowStepType.IfCondition && (targetList == dragData.Step.ThenSteps || targetList == dragData.Step.ElseSteps))
            {
                topIndicator.Visibility = Visibility.Collapsed;
                bottomIndicator.Visibility = Visibility.Collapsed;
                ClearWorkflowGhostAction();
                e.Effects = DragDropEffects.None;
                return;
            }

            Point p = e.GetPosition(hitElement);
            bool dropBefore = p.Y < (hitElement.ActualHeight / 2);

            topIndicator.Visibility = dropBefore ? Visibility.Visible : Visibility.Collapsed;
            bottomIndicator.Visibility = dropBefore ? Visibility.Collapsed : Visibility.Visible;

            string stepName = !string.IsNullOrWhiteSpace(targetStep.Name) ? targetStep.Name : GetStepTypeIconAndName(targetStep.StepType);
            string actionText = dropBefore ? $"Drop before \"{stepName}\"" : $"Drop after \"{stepName}\"";
            var arrowGeo = dropBefore ? _arrowUpGeometry : _arrowDownGeometry;
            SetWorkflowGhostAction(actionText, arrowGeo, accentBrush);

            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        };

        hitElement.PreviewDragLeave += (s, e) =>
        {
            if (!IsCursorPhysicallyOver(hitElement))
            {
                topIndicator.Visibility = Visibility.Collapsed;
                bottomIndicator.Visibility = Visibility.Collapsed;
                ClearWorkflowGhostAction();
            }
        };

        hitElement.PreviewDrop += (s, e) =>
        {
            if (IsOverChildDropTarget(hitElement, e.OriginalSource))
            {
                topIndicator.Visibility = Visibility.Collapsed;
                bottomIndicator.Visibility = Visibility.Collapsed;
                return;
            }

            topIndicator.Visibility = Visibility.Collapsed;
            bottomIndicator.Visibility = Visibility.Collapsed;
            ClearWorkflowGhostAction();

            if (e.Data.GetDataPresent("WorkflowStepDragData"))
            {
                var dragData = e.Data.GetData("WorkflowStepDragData") as WorkflowStepDragData;
                if (dragData != null && dragData.Step.Id != targetStep.Id)
                {
                    int targetIdx = targetList.IndexOf(targetStep);
                    Point p = e.GetPosition(hitElement);
                    bool dropBefore = p.Y < (hitElement.ActualHeight / 2);
                    if (!dropBefore) targetIdx++;
                    ExecuteStepDrop(dragData, targetList, targetIdx, parentIfStep);
                    e.Handled = true;
                }
            }
        };
    }

    private void ExecuteStepDrop(WorkflowStepDragData dragData, List<WorkflowStep> targetList, int targetIndex, WorkflowStep? targetIfStep = null)
    {
        if (dragData?.Step == null || targetList == null) return;
        var step = dragData.Step;

        // Prevent dragging an IfCondition into its own sub-branches
        if (step.StepType == WorkflowStepType.IfCondition && (targetList == step.ThenSteps || targetList == step.ElseSteps))
        {
            return;
        }

        // Remove from source list
        int sourceIdx = dragData.SourceList.IndexOf(step);
        if (sourceIdx >= 0)
        {
            if (dragData.SourceList == targetList && sourceIdx < targetIndex)
            {
                targetIndex--;
            }
            dragData.SourceList.RemoveAt(sourceIdx);
        }

        targetIndex = Math.Clamp(targetIndex, 0, targetList.Count);
        targetList.Insert(targetIndex, step);

        if (targetIfStep != null && targetList == targetIfStep.ElseSteps)
        {
            targetIfStep.HasElseBranch = true;
        }

        RebuildWorkflowStepCards(step.Id);
        OnFormEdited();
    }

    private void MoveStepToConditionBranch(WorkflowStep step, List<WorkflowStep> sourceList, WorkflowStep targetIfStep, bool isElse)
    {
        if (step.Id == targetIfStep.Id) return;
        var targetList = isElse ? targetIfStep.ElseSteps : targetIfStep.ThenSteps;
        targetList ??= [];
        if (isElse) targetIfStep.ElseSteps = targetList;
        else targetIfStep.ThenSteps = targetList;

        ExecuteStepDrop(new WorkflowStepDragData(step, sourceList), targetList, targetList.Count, targetIfStep);
    }

    private void MoveStepOutOfBranch(WorkflowStep step, List<WorkflowStep> branchSteps, WorkflowStep parentIfStep, bool before)
    {
        if (_selectedItem?.Payload.WorkflowSteps == null) return;
        var rootSteps = _selectedItem.Payload.WorkflowSteps;
        int parentIdx = rootSteps.IndexOf(parentIfStep);
        if (parentIdx < 0) return;

        int targetIdx = before ? Math.Max(0, parentIdx) : parentIdx + 1;
        ExecuteStepDrop(new WorkflowStepDragData(step, branchSteps, parentIfStep), rootSteps, targetIdx);
    }

    private void MoveStepBetweenBranches(WorkflowStep step, List<WorkflowStep> sourceBranch, List<WorkflowStep> targetBranch, WorkflowStep parentIfStep, bool targetIsElse)
    {
        if (targetIsElse)
        {
            parentIfStep.HasElseBranch = true;
            parentIfStep.ElseSteps ??= [];
            targetBranch = parentIfStep.ElseSteps;
        }
        else
        {
            parentIfStep.ThenSteps ??= [];
            targetBranch = parentIfStep.ThenSteps;
        }

        ExecuteStepDrop(new WorkflowStepDragData(step, sourceBranch, parentIfStep), targetBranch, targetBranch.Count, parentIfStep);
    }

    private void CutStep(WorkflowStep step, List<WorkflowStep> sourceList, WorkflowStep? parentIfStep)
    {
        _stepClipboard = step;
        _stepClipboardIsCut = true;
        _stepClipboardSourceList = sourceList;
        _stepClipboardParentIfStep = parentIfStep;
        StatusText.Text = $"Cut \"{step.Name}\" to clipboard. Paste it at any insertion point.";
    }

    private void CopyStep(WorkflowStep step)
    {
        _stepClipboard = step.Clone();
        _stepClipboardIsCut = false;
        _stepClipboardSourceList = null;
        _stepClipboardParentIfStep = null;
        StatusText.Text = $"Copied \"{step.Name}\" to clipboard.";
    }

    private void PasteStepAt(List<WorkflowStep> targetList, int targetIndex, WorkflowStep? targetIfStep = null)
    {
        if (_stepClipboard == null || targetList == null) return;

        WorkflowStep stepToInsert;
        if (_stepClipboardIsCut)
        {
            stepToInsert = _stepClipboard;
            _stepClipboardSourceList?.Remove(stepToInsert);
            _stepClipboard = null;
            _stepClipboardIsCut = false;
            _stepClipboardSourceList = null;
            _stepClipboardParentIfStep = null;
        }
        else
        {
            stepToInsert = _stepClipboard.Clone();
        }

        targetIndex = Math.Clamp(targetIndex, 0, targetList.Count);
        targetList.Insert(targetIndex, stepToInsert);

        if (targetIfStep != null && targetList == targetIfStep.ElseSteps)
        {
            targetIfStep.HasElseBranch = true;
        }

        RebuildWorkflowStepCards(stepToInsert.Id);
        OnFormEdited();
    }
}
