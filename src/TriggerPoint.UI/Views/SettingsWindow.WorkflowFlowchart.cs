using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TriggerPoint.Core.Models;
using TriggerPoint.UI.Theme;

namespace TriggerPoint.UI.Views;

public partial class SettingsWindow
{
    private bool _isFlowchartView;
    private double _flowchartZoom = 1.0;
    private Point? _flowchartPanStart;
    private Point _flowchartScrollStart;
    private bool _isPanningFlowchart;

    // View Mode Switcher Handlers
    private void WorkflowViewModeListBtn_Click(object sender, RoutedEventArgs e)
    {
        _isFlowchartView = false;
        UpdateWorkflowViewModeUI();
    }

    private void WorkflowViewModeFlowchartBtn_Click(object sender, RoutedEventArgs e)
    {
        _isFlowchartView = true;
        UpdateWorkflowViewModeUI();
        RebuildWorkflowFlowchart();
    }

    private void UpdateWorkflowViewModeUI()
    {
        if (WorkflowViewModeListBtn == null || WorkflowViewModeFlowchartBtn == null) return;

        var accentBrush = Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.RoyalBlue;
        var textSecBrush = Application.Current.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;

        if (_isFlowchartView)
        {
            WorkflowViewModeListBtn.Background = Brushes.Transparent;
            WorkflowViewModeListBtn.Foreground = textSecBrush;

            WorkflowViewModeFlowchartBtn.Background = accentBrush;
            WorkflowViewModeFlowchartBtn.Foreground = Brushes.White;

            if (WorkflowStepsHost != null) WorkflowStepsHost.Visibility = Visibility.Collapsed;
            if (WorkflowFlowchartContainer != null) WorkflowFlowchartContainer.Visibility = Visibility.Visible;
            if (WorkflowToggleAllExpandBtn != null) WorkflowToggleAllExpandBtn.Visibility = Visibility.Collapsed;
        }
        else
        {
            WorkflowViewModeListBtn.Background = accentBrush;
            WorkflowViewModeListBtn.Foreground = Brushes.White;

            WorkflowViewModeFlowchartBtn.Background = Brushes.Transparent;
            WorkflowViewModeFlowchartBtn.Foreground = textSecBrush;

            if (WorkflowStepsHost != null) WorkflowStepsHost.Visibility = Visibility.Visible;
            if (WorkflowFlowchartContainer != null) WorkflowFlowchartContainer.Visibility = Visibility.Collapsed;
            if (WorkflowToggleAllExpandBtn != null) WorkflowToggleAllExpandBtn.Visibility = Visibility.Visible;
        }
    }

    // Zoom & Pan Handlers
    private void WorkflowFlowchartZoomInBtn_Click(object sender, RoutedEventArgs e)
    {
        SetFlowchartZoom(_flowchartZoom + 0.15);
    }

    private void WorkflowFlowchartZoomOutBtn_Click(object sender, RoutedEventArgs e)
    {
        SetFlowchartZoom(_flowchartZoom - 0.15);
    }

    private void WorkflowFlowchartZoomReset_MouseDown(object sender, MouseButtonEventArgs e)
    {
        SetFlowchartZoom(1.0);
    }

    private void WorkflowFlowchartFitBtn_Click(object sender, RoutedEventArgs e)
    {
        SetFlowchartZoom(1.0);
        WorkflowFlowchartScrollViewer?.ScrollToHome();
    }

    private void SetFlowchartZoom(double zoom)
    {
        _flowchartZoom = Math.Clamp(zoom, 0.4, 2.5);
        if (WorkflowFlowchartZoomTransform != null)
        {
            WorkflowFlowchartZoomTransform.ScaleX = _flowchartZoom;
            WorkflowFlowchartZoomTransform.ScaleY = _flowchartZoom;
        }
        if (WorkflowFlowchartZoomText != null)
        {
            WorkflowFlowchartZoomText.Text = $"{(int)(_flowchartZoom * 100)}%";
        }
    }

    private void WorkflowFlowchart_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
        {
            double delta = e.Delta > 0 ? 0.1 : -0.1;
            SetFlowchartZoom(_flowchartZoom + delta);
            e.Handled = true;
        }
    }

    private void WorkflowFlowchart_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle || (e.ChangedButton == MouseButton.Left && e.OriginalSource is Canvas or ScrollViewer))
        {
            if (WorkflowFlowchartScrollViewer != null)
            {
                _flowchartPanStart = e.GetPosition(WorkflowFlowchartScrollViewer);
                _flowchartScrollStart = new Point(WorkflowFlowchartScrollViewer.HorizontalOffset, WorkflowFlowchartScrollViewer.VerticalOffset);
                _isPanningFlowchart = true;
                WorkflowFlowchartScrollViewer.Cursor = Cursors.SizeAll;
                WorkflowFlowchartScrollViewer.CaptureMouse();
            }
        }
    }

    private void WorkflowFlowchart_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanningFlowchart && _flowchartPanStart.HasValue && WorkflowFlowchartScrollViewer != null)
        {
            var cur = e.GetPosition(WorkflowFlowchartScrollViewer);
            var dx = cur.X - _flowchartPanStart.Value.X;
            var dy = cur.Y - _flowchartPanStart.Value.Y;
            WorkflowFlowchartScrollViewer.ScrollToHorizontalOffset(_flowchartScrollStart.X - dx);
            WorkflowFlowchartScrollViewer.ScrollToVerticalOffset(_flowchartScrollStart.Y - dy);
        }
    }

    private void WorkflowFlowchart_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanningFlowchart && WorkflowFlowchartScrollViewer != null)
        {
            _isPanningFlowchart = false;
            _flowchartPanStart = null;
            WorkflowFlowchartScrollViewer.Cursor = Cursors.Arrow;
            WorkflowFlowchartScrollViewer.ReleaseMouseCapture();
        }
    }

    // Graph Layout and Rendering Engine
    private class FlowchartNode
    {
        public WorkflowStep? Step { get; set; }
        public int StepNumber { get; set; }
        public Rect Bounds { get; set; }
        public Point InputAnchor => new(Bounds.X + Bounds.Width / 2.0, Bounds.Y);
        public Point OutputAnchor => new(Bounds.X + Bounds.Width / 2.0, Bounds.Y + Bounds.Height);
        public Point TrueBranchAnchor => new(Bounds.X + Bounds.Width * 0.25, Bounds.Y + Bounds.Height);
        public Point FalseBranchAnchor => new(Bounds.X + Bounds.Width * 0.75, Bounds.Y + Bounds.Height);
        public List<FlowchartNode> ThenChildren { get; } = [];
        public List<FlowchartNode> ElseChildren { get; } = [];
    }

    private const double FlowNodeWidth = 240.0;
    private const double FlowNodeHeight = 54.0;
    private const double FlowVerticalGap = 42.0;
    private const double FlowHorizontalGap = 40.0;

    internal static double MeasureSubtreeWidth(WorkflowStep step)
    {
        if (step.StepType == WorkflowStepType.IfCondition)
        {
            double thenW = MeasureSequenceWidth(step.ThenSteps);
            double elseW = step.HasElseBranch ? MeasureSequenceWidth(step.ElseSteps) : FlowNodeWidth;
            return Math.Max(FlowNodeWidth, thenW + FlowHorizontalGap + elseW);
        }
        return FlowNodeWidth;
    }

    internal static double MeasureSequenceWidth(List<WorkflowStep>? steps)
    {
        if (steps == null || steps.Count == 0) return FlowNodeWidth;
        double maxW = FlowNodeWidth;
        foreach (var s in steps)
        {
            maxW = Math.Max(maxW, MeasureSubtreeWidth(s));
        }
        return maxW;
    }

    private void LayoutStepSequence(
        List<WorkflowStep> steps,
        ref int stepNumberCounter,
        double left,
        double top,
        double availableWidth,
        List<FlowchartNode> outNodes,
        out double bottomY)
    {
        double currentY = top;
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            stepNumberCounter++;
            int curStepNum = stepNumberCounter;

            double nodeX = left + (availableWidth - FlowNodeWidth) / 2.0;
            var node = new FlowchartNode
            {
                Step = step,
                StepNumber = curStepNum,
                Bounds = new Rect(nodeX, currentY, FlowNodeWidth, FlowNodeHeight)
            };
            outNodes.Add(node);

            if (step.StepType == WorkflowStepType.IfCondition)
            {
                double thenW = MeasureSequenceWidth(step.ThenSteps);
                double elseW = step.HasElseBranch ? MeasureSequenceWidth(step.ElseSteps) : FlowNodeWidth;
                double totalBranchesW = thenW + FlowHorizontalGap + elseW;
                double branchStartX = left + (availableWidth - totalBranchesW) / 2.0;

                double branchTop = currentY + FlowNodeHeight + FlowVerticalGap;
                double thenBottom = branchTop;
                double elseBottom = branchTop;

                if (step.ThenSteps != null && step.ThenSteps.Count > 0)
                {
                    LayoutStepSequence(step.ThenSteps, ref stepNumberCounter, branchStartX, branchTop, thenW, node.ThenChildren, out thenBottom);
                }

                if (step.HasElseBranch && step.ElseSteps != null && step.ElseSteps.Count > 0)
                {
                    LayoutStepSequence(step.ElseSteps, ref stepNumberCounter, branchStartX + thenW + FlowHorizontalGap, branchTop, elseW, node.ElseChildren, out elseBottom);
                }

                currentY = Math.Max(thenBottom, elseBottom) + FlowVerticalGap;
            }
            else
            {
                currentY += FlowNodeHeight + FlowVerticalGap;
            }
        }
        bottomY = currentY - FlowVerticalGap + FlowNodeHeight;
    }

    public void RebuildWorkflowFlowchart()
    {
        if (WorkflowFlowchartCanvas == null) return;
        WorkflowFlowchartCanvas.Children.Clear();

        var steps = _selectedItem?.Payload?.WorkflowSteps;
        if (steps == null || steps.Count == 0)
        {
            if (WorkflowFlowchartEmptyOverlay != null) WorkflowFlowchartEmptyOverlay.Visibility = Visibility.Visible;
            WorkflowFlowchartCanvas.Width = 600;
            WorkflowFlowchartCanvas.Height = 400;
            return;
        }

        if (WorkflowFlowchartEmptyOverlay != null) WorkflowFlowchartEmptyOverlay.Visibility = Visibility.Collapsed;

        int stepCounter = 0;
        double totalSequenceWidth = MeasureSequenceWidth(steps);
        double marginX = 80.0;
        double marginY = 30.0;
        double availableW = Math.Max(totalSequenceWidth, 300.0);

        // 1. Layout Sequence
        var rootNodes = new List<FlowchartNode>();
        double sequenceStartY = marginY + 34.0 + FlowVerticalGap;
        LayoutStepSequence(steps, ref stepCounter, marginX, sequenceStartY, availableW, rootNodes, out double sequenceBottomY);

        // Start Node
        double startX = marginX + (availableW - 120.0) / 2.0;
        var startPoint = new Point(startX + 60.0, marginY + 30.0);
        var startNode = CreateStartNode(startX, marginY);

        // End Node
        double endY = sequenceBottomY + FlowVerticalGap;
        double endX = marginX + (availableW - 120.0) / 2.0;
        var endPoint = new Point(endX + 60.0, endY);
        var endNode = CreateEndNode(endX, endY);

        // 2. Render Splines First (so cards sit on top)
        var workflowBrush = Application.Current.TryFindResource("WorkflowBrush") as Brush ?? Brushes.Purple;
        var successBrush = Application.Current.TryFindResource("SuccessBrush") as Brush ?? Brushes.ForestGreen;
        var warningBrush = Application.Current.TryFindResource("WarningBrush") as Brush ?? Brushes.DarkOrange;

        // Connect Start to First Node
        if (rootNodes.Count > 0)
        {
            DrawSpline(startPoint, rootNodes[0].InputAnchor, workflowBrush, 2.0);
        }

        // Connect Nodes recursively
        ConnectNodes(rootNodes, endPoint, workflowBrush, successBrush, warningBrush);

        // 3. Render Node Cards
        WorkflowFlowchartCanvas.Children.Add(startNode);
        RenderNodeCards(rootNodes);
        WorkflowFlowchartCanvas.Children.Add(endNode);

        // Set total canvas dimensions
        double canvasW = Math.Max(availableW + marginX * 2.0, 700.0);
        double canvasH = Math.Max(endY + 80.0, 480.0);
        WorkflowFlowchartCanvas.Width = canvasW;
        WorkflowFlowchartCanvas.Height = canvasH;
    }

    private void ConnectNodes(
        List<FlowchartNode> nodes,
        Point exitTargetPoint,
        Brush defaultBrush,
        Brush trueBrush,
        Brush falseBrush)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            bool hasNextInSequence = i < nodes.Count - 1;
            Point nextTargetPoint = hasNextInSequence ? nodes[i + 1].InputAnchor : exitTargetPoint;

            if (node.Step?.StepType == WorkflowStepType.IfCondition)
            {
                // Connect True branch
                if (node.ThenChildren.Count > 0)
                {
                    DrawSpline(node.TrueBranchAnchor, node.ThenChildren[0].InputAnchor, trueBrush, 2.0, "✓ True", trueBrush);
                    ConnectNodes(node.ThenChildren, nextTargetPoint, defaultBrush, trueBrush, falseBrush);
                }
                else
                {
                    DrawSpline(node.TrueBranchAnchor, nextTargetPoint, trueBrush, 1.5, "✓ True", trueBrush);
                }

                // Connect False branch
                if (node.ElseChildren.Count > 0)
                {
                    DrawSpline(node.FalseBranchAnchor, node.ElseChildren[0].InputAnchor, falseBrush, 2.0, "✕ False", falseBrush);
                    ConnectNodes(node.ElseChildren, nextTargetPoint, defaultBrush, trueBrush, falseBrush);
                }
                else if (node.Step?.HasElseBranch == true)
                {
                    DrawSpline(node.FalseBranchAnchor, nextTargetPoint, falseBrush, 1.5, "✕ False", falseBrush);
                }
                else
                {
                    DrawSpline(node.FalseBranchAnchor, nextTargetPoint, defaultBrush, 1.5);
                }
            }
            else
            {
                DrawSpline(node.OutputAnchor, nextTargetPoint, defaultBrush, 2.0);
            }
        }
    }

    private void RenderNodeCards(List<FlowchartNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Step != null)
            {
                var card = CreateFlowchartNodeCard(node.Step, node.StepNumber, node.Bounds);
                WorkflowFlowchartCanvas.Children.Add(card);
            }

            if (node.ThenChildren.Count > 0)
            {
                RenderNodeCards(node.ThenChildren);
            }
            if (node.ElseChildren.Count > 0)
            {
                RenderNodeCards(node.ElseChildren);
            }
        }
    }

    private void DrawSpline(Point pStart, Point pEnd, Brush brush, double thickness, string? badgeText = null, Brush? badgeBrush = null)
    {
        var path = new Path
        {
            Stroke = brush,
            StrokeThickness = thickness,
            SnapsToDevicePixels = true
        };

        var geom = new PathGeometry();
        var fig = new PathFigure { StartPoint = pStart, IsFilled = false };

        double dy = Math.Max(18.0, (pEnd.Y - pStart.Y) * 0.45);
        var c1 = new Point(pStart.X, pStart.Y + dy);
        var c2 = new Point(pEnd.X, pEnd.Y - dy);
        fig.Segments.Add(new BezierSegment(c1, c2, pEnd, true));
        geom.Figures.Add(fig);

        // Arrowhead at pEnd
        var arrowFig = new PathFigure { StartPoint = pEnd, IsFilled = true, IsClosed = true };
        arrowFig.Segments.Add(new LineSegment(new Point(pEnd.X - 4.5, pEnd.Y - 7), true));
        arrowFig.Segments.Add(new LineSegment(new Point(pEnd.X + 4.5, pEnd.Y - 7), true));
        geom.Figures.Add(arrowFig);

        path.Data = geom;
        WorkflowFlowchartCanvas.Children.Add(path);

        if (!string.IsNullOrEmpty(badgeText))
        {
            var badge = new Border
            {
                Background = badgeBrush ?? brush,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                SnapsToDevicePixels = true,
                Child = new TextBlock
                {
                    Text = badgeText,
                    FontSize = 9.0,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White
                }
            };
            Canvas.SetLeft(badge, (pStart.X + c1.X) / 2.0 - 16);
            Canvas.SetTop(badge, pStart.Y + 6);
            WorkflowFlowchartCanvas.Children.Add(badge);
        }
    }

    private Border CreateStartNode(double x, double y)
    {
        var border = new Border
        {
            Width = 120,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Background = Application.Current.TryFindResource("AccentSubtleBrush") as Brush ?? Brushes.SlateBlue,
            BorderBrush = Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.RoyalBlue,
            BorderThickness = new Thickness(1.5),
            SnapsToDevicePixels = true,
            Child = new TextBlock
            {
                Text = "▶ Start",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.RoyalBlue,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Canvas.SetLeft(border, x);
        Canvas.SetTop(border, y);
        return border;
    }

    private Border CreateEndNode(double x, double y)
    {
        var border = new Border
        {
            Width = 120,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Background = Application.Current.TryFindResource("BgSecondaryBrush") as Brush ?? Brushes.DarkSlateGray,
            BorderBrush = Application.Current.TryFindResource("BorderSubtleBrush") as Brush ?? Brushes.Gray,
            BorderThickness = new Thickness(1),
            SnapsToDevicePixels = true,
            Child = new TextBlock
            {
                Text = "⏹ Complete",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Application.Current.TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Canvas.SetLeft(border, x);
        Canvas.SetTop(border, y);
        return border;
    }

    private Border CreateFlowchartNodeCard(WorkflowStep step, int stepNumber, Rect bounds)
    {
        bool isActive = step.Id == _activeWorkflowStepId;
        var card = new Border
        {
            Width = bounds.Width,
            Height = bounds.Height,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 6, 8, 6),
            Background = Application.Current.TryFindResource("CardBgBrush") as Brush ?? Brushes.Black,
            BorderBrush = isActive 
                ? (Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.RoyalBlue)
                : (Application.Current.TryFindResource("BorderBrush") as Brush ?? Brushes.DimGray),
            BorderThickness = new Thickness(isActive ? 2 : 1),
            Cursor = Cursors.Hand,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
            Tag = step
        };

        if (!step.IsEnabled)
        {
            card.Opacity = 0.55;
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Number pill
        var numBorder = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = Application.Current.TryFindResource("BgSecondaryBrush") as Brush ?? Brushes.DarkSlateGray,
            BorderBrush = Application.Current.TryFindResource("BorderSubtleBrush") as Brush ?? Brushes.Gray,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = $"#{stepNumber}",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Application.Current.TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(numBorder, 0);
        grid.Children.Add(numBorder);

        // Details stack
        var detailsStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal };

        // Type badge
        var typeBadge = new Border
        {
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1, 4, 1),
            Margin = new Thickness(0, 0, 6, 0),
            Background = GetStepBadgeBackground(step.StepType),
            Child = new TextBlock
            {
                Text = GetStepBadgeShortName(step.StepType),
                FontSize = 8.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            }
        };
        headerRow.Children.Add(typeBadge);

        // Title
        string titleText = string.IsNullOrWhiteSpace(step.Name) ? GetStepTypeIconAndName(step.StepType) : step.Name;
        var titleBlock = new TextBlock
        {
            Text = titleText,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Application.Current.TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 145
        };
        headerRow.Children.Add(titleBlock);
        detailsStack.Children.Add(headerRow);

        // Summary
        string summary = GetStepLiveSummary(step);
        var summaryBlock = new TextBlock
        {
            Text = summary,
            FontSize = 9.5,
            Foreground = Application.Current.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0)
        };
        detailsStack.Children.Add(summaryBlock);

        Grid.SetColumn(detailsStack, 1);
        grid.Children.Add(detailsStack);

        card.Child = grid;

        // Hover & Click events
        card.MouseEnter += (s, e) =>
        {
            if (step.Id != _activeWorkflowStepId)
            {
                card.BorderBrush = Application.Current.TryFindResource("WorkflowBrush") as Brush ?? Brushes.Purple;
            }
        };
        card.MouseLeave += (s, e) =>
        {
            if (step.Id != _activeWorkflowStepId)
            {
                card.BorderBrush = Application.Current.TryFindResource("BorderBrush") as Brush ?? Brushes.DimGray;
            }
        };
        card.MouseLeftButtonDown += (s, e) =>
        {
            _activeWorkflowStepId = step.Id;
            HighlightActiveFlowchartCard();

            if (e.ClickCount == 2)
            {
                WorkflowViewModeListBtn_Click(this, new RoutedEventArgs());
                Dispatcher.InvokeAsync(() =>
                {
                    SetActiveWorkflowStep(step.Id, focusFirstInput: true);
                    ScrollToWorkflowStepCard(step.Id);
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
        };

        Canvas.SetLeft(card, bounds.X);
        Canvas.SetTop(card, bounds.Y);
        return card;
    }

    private void HighlightActiveFlowchartCard()
    {
        if (WorkflowFlowchartCanvas == null) return;
        var accentBrush = Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.RoyalBlue;
        var borderBrush = Application.Current.TryFindResource("BorderBrush") as Brush ?? Brushes.DimGray;

        foreach (var card in WorkflowFlowchartCanvas.Children.OfType<Border>())
        {
            if (card.Tag is WorkflowStep step)
            {
                bool isActive = step.Id == _activeWorkflowStepId;
                card.BorderBrush = isActive ? accentBrush : borderBrush;
                card.BorderThickness = new Thickness(isActive ? 2 : 1);
            }
        }
    }

    private void ScrollToWorkflowStepCard(Guid stepId)
    {
        if (WorkflowStepsHost == null) return;
        foreach (UIElement child in WorkflowStepsHost.Children)
        {
            if (child is Grid g)
            {
                foreach (var b in g.Children.OfType<Border>().Where(b => b.Tag is WorkflowStep s && s.Id == stepId))
                {
                    b.BringIntoView();
                    return;
                }
            }
            else if (child is StackPanel sp)
            {
                foreach (var b in sp.Children.OfType<Border>().Where(b => b.Tag is WorkflowStep s && s.Id == stepId))
                {
                    b.BringIntoView();
                    return;
                }
            }
            else if (child is Border b && b.Tag is WorkflowStep s && s.Id == stepId)
            {
                b.BringIntoView();
                return;
            }
        }
    }

    internal static string GetStepBadgeShortName(WorkflowStepType stepType) => stepType switch
    {
        WorkflowStepType.IfCondition => "IF",
        WorkflowStepType.Prompt => "PROMPT",
        WorkflowStepType.OpenUrl => "URL",
        WorkflowStepType.LaunchApp => "APP",
        WorkflowStepType.EnsureDirectory => "DIR",
        WorkflowStepType.InjectSnippet => "SNIP",
        WorkflowStepType.Delay => "DELAY",
        WorkflowStepType.RunScript => "SCRIPT",
        WorkflowStepType.ExecuteAction => "ACTION",
        WorkflowStepType.Dialog => "DIALOG",
        WorkflowStepType.Macro => "MACRO",
        WorkflowStepType.SetVariable => "SET",
        WorkflowStepType.Service => "SRV",
        _ => "STEP"
    };

    private Brush GetStepBadgeBackground(WorkflowStepType stepType)
    {
        string resourceKey = stepType switch
        {
            WorkflowStepType.IfCondition => "WorkflowBrush",
            WorkflowStepType.Prompt => "AccentBrush",
            WorkflowStepType.LaunchApp => "ShellBrush",
            WorkflowStepType.InjectSnippet => "SnippetBrush",
            WorkflowStepType.EnsureDirectory => "FolderBrush",
            WorkflowStepType.Delay => "WarningBrush",
            WorkflowStepType.Dialog => "InfoBrush",
            WorkflowStepType.RunScript => "ErrorBrush",
            WorkflowStepType.Service => "AccentBrush",
            _ => "WorkflowBrush"
        };
        return Application.Current.TryFindResource(resourceKey) as Brush 
            ?? Application.Current.TryFindResource("AccentBrush") as Brush 
            ?? Brushes.MediumPurple;
    }
}
