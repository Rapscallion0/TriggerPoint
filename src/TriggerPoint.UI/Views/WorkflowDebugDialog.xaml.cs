using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;

namespace TriggerPoint.UI.Views;

public class WorkflowStepDebugViewModel
{
    public WorkflowStep Step { get; }
    public int Index { get; }
    public string StepTitle { get; }
    public string StepSubtitle { get; }
    public string StatusIcon { get; set; } = "⏳";
    public string DurationText { get; set; } = string.Empty;
    public Brush BackgroundBrush { get; set; } = Brushes.Transparent;
    public Brush BorderBrush { get; set; } = Brushes.Transparent;

    public WorkflowStepDebugViewModel(WorkflowStep step, int index)
    {
        Step = step;
        Index = index;
        StepTitle = $"{index + 1}. {(!string.IsNullOrWhiteSpace(step.Name) ? step.Name : step.StepType.ToString())}";
        StepSubtitle = step.StepType switch
        {
            WorkflowStepType.LaunchApp => $"Launch: {step.Command}",
            WorkflowStepType.OpenUrl => $"URL: {step.Url}",
            WorkflowStepType.InjectSnippet => $"Snippet: {step.SnippetTemplate}",
            WorkflowStepType.Prompt => $"Prompt: {step.PromptTitle}",
            WorkflowStepType.Delay => $"Delay: {step.DelayMs}ms",
            WorkflowStepType.SetVariable => $"Set {step.SetVariableName} = {step.SetVariableValue}",
            WorkflowStepType.IfCondition => $"If {step.ConditionLeft} {step.ConditionOperator} {step.ConditionRight}",
            _ => step.StepType.ToString()
        };
    }
}

public partial class WorkflowDebugDialog : Window
{
    private readonly TriggerItem _item;
    private readonly IWorkflowExecutor _workflowExecutor;
    private readonly List<WorkflowStepDebugViewModel> _stepViewModels = [];
    private readonly Dictionary<string, string> _runtimeVariables = new(StringComparer.OrdinalIgnoreCase);
    private int _currentStepIndex = 0;
    private bool _isExecuting = false;
    private CancellationTokenSource? _cts;
    private long _totalElapsedMs = 0;

    public WorkflowDebugDialog(TriggerItem workflowItem, IWorkflowExecutor workflowExecutor)
    {
        InitializeComponent();
        _item = workflowItem;
        _workflowExecutor = workflowExecutor;

        WorkflowTitleText.Text = $"Debug Workflow: {_item.Name}";
        InitializeDebugger();
    }

    private void InitializeDebugger()
    {
        _stepViewModels.Clear();
        _runtimeVariables.Clear();
        _currentStepIndex = 0;
        _totalElapsedMs = 0;

        // Initialize default workflow variables
        if (_item.Payload.WorkflowVariables != null)
        {
            foreach (var v in _item.Payload.WorkflowVariables)
            {
                _runtimeVariables[v.Name] = v.Value;
            }
        }

        // Initialize steps
        var steps = _item.Payload.WorkflowSteps ?? [];
        for (int i = 0; i < steps.Count; i++)
        {
            _stepViewModels.Add(new WorkflowStepDebugViewModel(steps[i], i));
        }

        if (_stepViewModels.Count > 0)
        {
            HighlightStep(0);
        }

        StepsListBox.ItemsSource = null;
        StepsListBox.ItemsSource = _stepViewModels;

        RefreshVariablesUI();
        TraceLogBox.Text = $"[{DateTime.Now:HH:mm:ss}] Debugger initialized with {_stepViewModels.Count} steps.\n";
        StatusBadgeText.Text = "READY";
        StatusBadge.BorderBrush = Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.CornflowerBlue;
        UpdateControls();
    }

    private void HighlightStep(int index)
    {
        var accentBrush = Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.CornflowerBlue;
        var accentSubtle = Application.Current.TryFindResource("AccentSubtleBrush") as Brush ?? new SolidColorBrush(Color.FromArgb(0x20, 0x00, 0x7A, 0xCC));

        for (int i = 0; i < _stepViewModels.Count; i++)
        {
            var vm = _stepViewModels[i];
            if (i == index)
            {
                vm.BackgroundBrush = accentSubtle;
                vm.BorderBrush = accentBrush;
                if (vm.StatusIcon == "⏳") vm.StatusIcon = "▶";
            }
            else if (i > index)
            {
                vm.BackgroundBrush = Brushes.Transparent;
                vm.BorderBrush = Brushes.Transparent;
            }
        }
        StepsListBox.Items.Refresh();
    }

    private void RefreshVariablesUI()
    {
        VariablesListBox.ItemsSource = null;
        VariablesListBox.ItemsSource = _runtimeVariables.Select(kvp => new KeyValuePair<string, string>(kvp.Key, kvp.Value)).ToList();
        VariableCountText.Text = $"{_runtimeVariables.Count} variables";
    }

    private async Task<bool> ExecuteStepAsync(int index)
    {
        if (index < 0 || index >= _stepViewModels.Count) return false;

        var vm = _stepViewModels[index];
        var step = vm.Step;

        vm.StatusIcon = "⏳";
        HighlightStep(index);

        var sw = Stopwatch.StartNew();
        bool isDryRun = DryRunCheck.IsChecked == true;
        bool success = true;

        AppendLog($"Executing Step {index + 1}: '{vm.StepTitle}' (Type: {step.StepType}){(isDryRun ? " [DRY-RUN]" : "")}...");

        try
        {
            if (isDryRun)
            {
                // Simulate step safely
                await Task.Delay(40); // small visual simulation tick

                switch (step.StepType)
                {
                    case WorkflowStepType.SetVariable:
                        string resolvedVal = PlaceholderParser.EvaluateAsync(step.SetVariableValue, null, _runtimeVariables).GetAwaiter().GetResult();
                        _runtimeVariables[step.SetVariableName] = resolvedVal;
                        AppendLog($"  -> SetVariable: '{step.SetVariableName}' = '{resolvedVal}'");
                        break;

                    case WorkflowStepType.IfCondition:
                        string left = PlaceholderParser.EvaluateAsync(step.ConditionLeft, null, _runtimeVariables).GetAwaiter().GetResult();
                        string right = PlaceholderParser.EvaluateAsync(step.ConditionRight, null, _runtimeVariables).GetAwaiter().GetResult();
                        bool conditionMet = ConditionEvaluator.Evaluate(left, step.ConditionOperator, right, step.ConditionIgnoreCase);
                        AppendLog($"  -> Condition evaluated: '{left}' {step.ConditionOperator} '{right}' => {conditionMet}");
                        break;

                    case WorkflowStepType.LaunchApp:
                        AppendLog($"  -> Mock Launch: {step.Command} {step.Arguments}");
                        break;

                    case WorkflowStepType.OpenUrl:
                        AppendLog($"  -> Mock Open URL: {step.Url}");
                        break;

                    case WorkflowStepType.InjectSnippet:
                        AppendLog($"  -> Mock Type Snippet: {step.SnippetTemplate}");
                        break;

                    default:
                        AppendLog($"  -> Mock completed step: {step.StepType}");
                        break;
                }
            }
            else
            {
                // Real execution
                success = await _workflowExecutor.ExecuteSingleStepAsync(step, _item, null, _cts?.Token ?? CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            success = false;
            AppendLog($"  ERROR: {ex.Message}");
        }
        finally
        {
            sw.Stop();
            long ms = sw.ElapsedMilliseconds;
            _totalElapsedMs += ms;
            vm.DurationText = $"{ms}ms";
            vm.StatusIcon = success ? "🟢" : "🔴";

            var greenSubtle = new SolidColorBrush(Color.FromArgb(0x20, 0x10, 0xB9, 0x81));
            var redSubtle = new SolidColorBrush(Color.FromArgb(0x20, 0xEF, 0x44, 0x44));
            vm.BackgroundBrush = success ? greenSubtle : redSubtle;
            vm.BorderBrush = success ? new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)) : new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));

            FooterTimingText.Text = $"Total execution time: {_totalElapsedMs}ms";
            StepsListBox.Items.Refresh();
            RefreshVariablesUI();
        }

        return success;
    }

    private void AppendLog(string message)
    {
        TraceLogBox.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        TraceLogBox.ScrollToEnd();
    }

    private async void StepOverBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStepIndex >= _stepViewModels.Count)
        {
            AppendLog("Workflow reached the end.");
            return;
        }

        _isExecuting = true;
        UpdateControls();

        bool ok = await ExecuteStepAsync(_currentStepIndex);
        _currentStepIndex++;

        if (_currentStepIndex < _stepViewModels.Count)
        {
            HighlightStep(_currentStepIndex);
        }
        else
        {
            StatusBadgeText.Text = "COMPLETED";
            StatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
        }

        _isExecuting = false;
        UpdateControls();
    }

    private async void ContinueBtn_Click(object sender, RoutedEventArgs e)
    {
        _isExecuting = true;
        _cts = new CancellationTokenSource();
        UpdateControls();
        StatusBadgeText.Text = "RUNNING";

        while (_currentStepIndex < _stepViewModels.Count && !_cts.IsCancellationRequested)
        {
            bool ok = await ExecuteStepAsync(_currentStepIndex);
            _currentStepIndex++;

            if (!ok && _stepViewModels[_currentStepIndex - 1].Step.OnError == StepErrorPolicy.StopWorkflow)
            {
                AppendLog("Execution halted due to error policy.");
                break;
            }
        }

        StatusBadgeText.Text = _currentStepIndex >= _stepViewModels.Count ? "COMPLETED" : "PAUSED";
        _isExecuting = false;
        UpdateControls();
    }

    private void ResetBtn_Click(object sender, RoutedEventArgs e)
    {
        InitializeDebugger();
    }

    private void StopBtn_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _isExecuting = false;
        StatusBadgeText.Text = "STOPPED";
        UpdateControls();
    }

    private void UpdateControls()
    {
        StepOverBtn.IsEnabled = !_isExecuting && _currentStepIndex < _stepViewModels.Count;
        ContinueBtn.IsEnabled = !_isExecuting && _currentStepIndex < _stepViewModels.Count;
        ResetBtn.IsEnabled = !_isExecuting;
        StopBtn.IsEnabled = _isExecuting;
    }

    private void ClearLogBtn_Click(object sender, RoutedEventArgs e)
    {
        TraceLogBox.Text = string.Empty;
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F10 && StepOverBtn.IsEnabled)
        {
            StepOverBtn_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.F5 && ContinueBtn.IsEnabled)
        {
            ContinueBtn_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && !_isExecuting)
        {
            Close();
            e.Handled = true;
        }
    }
}
