using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;

namespace TriggerPoint.UI.Views;

public partial class WorkflowTemplateEditorDialog : Window
{
    private readonly IWorkflowTemplateService _templateService;
    private readonly List<WorkflowStep> _currentWorkflowSteps;
    private readonly string? _suggestedTitle;
    private WorkflowPreset? _editingTemplate;

    public WorkflowPreset? ResultTemplate { get; private set; }

    public WorkflowTemplateEditorDialog(
        IWorkflowTemplateService templateService,
        WorkflowPreset? existingTemplate = null,
        IReadOnlyList<WorkflowStep>? stepsFromActiveWorkflow = null,
        string? suggestedTitle = null)
    {
        InitializeComponent();
        _templateService = templateService;
        _editingTemplate = existingTemplate?.DeepClone();
        _currentWorkflowSteps = (stepsFromActiveWorkflow ?? []).Select(s => s.Clone()).ToList();
        _suggestedTitle = suggestedTitle;

        Loaded += WorkflowTemplateEditorDialog_Loaded;
    }

    private void WorkflowTemplateEditorDialog_Loaded(object sender, RoutedEventArgs e)
    {
        // Populate existing categories into ComboBox
        var categories = _templateService.GetCategories();
        CategoryBox.ItemsSource = categories;

        if (_editingTemplate != null)
        {
            DialogTitleText.Text = "Edit Workflow Template";
            DialogSubtitleText.Text = "Modify the template title, category, description, or steps.";
            TitleBox.Text = _editingTemplate.Title;
            CategoryBox.Text = _editingTemplate.Category;
            DescriptionBox.Text = _editingTemplate.Description;
            UpdateStepSummaryUI(_editingTemplate.Steps.Count);

            if (_currentWorkflowSteps.Count > 0)
            {
                UpdateStepsFromWorkflowBtn.Visibility = Visibility.Visible;
            }
        }
        else
        {
            DialogTitleText.Text = "Save as Workflow Template";
            DialogSubtitleText.Text = "Create a reusable template from this workflow.";
            TitleBox.Text = _suggestedTitle ?? "New Workflow Template";
            CategoryBox.Text = categories.FirstOrDefault() ?? WorkflowPresets.CategoryGeneral;
            UpdateStepSummaryUI(_currentWorkflowSteps.Count);
        }

        TitleBox.Focus();
        TitleBox.SelectAll();
        ValidateForm();
    }

    private void UpdateStepSummaryUI(int count)
    {
        StepSummaryText.Text = $"Pipeline: {count} {(count == 1 ? "step" : "steps")} included";
    }

    private void UpdateStepsFromWorkflowBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_editingTemplate != null && _currentWorkflowSteps.Count > 0)
        {
            _editingTemplate.Steps = _currentWorkflowSteps.Select(s => s.Clone()).ToList();
            UpdateStepSummaryUI(_editingTemplate.Steps.Count);
            UpdateStepsFromWorkflowBtn.Content = "✓ Steps Updated";
            UpdateStepsFromWorkflowBtn.IsEnabled = false;
        }
    }

    private void FormInput_Changed(object sender, RoutedEventArgs e)
    {
        ValidateForm();
    }

    private void ValidateForm()
    {
        if (SaveBtn == null) return;
        bool hasTitle = !string.IsNullOrWhiteSpace(TitleBox.Text);
        bool hasCategory = !string.IsNullOrWhiteSpace(CategoryBox.Text);
        SaveBtn.IsEnabled = hasTitle && hasCategory;
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        var title = TitleBox.Text.Trim();
        var category = CategoryBox.Text.Trim();
        var description = DescriptionBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(category))
        {
            return;
        }

        if (_editingTemplate != null)
        {
            _editingTemplate.Title = title;
            _editingTemplate.Category = category;
            _editingTemplate.Description = description;
            ResultTemplate = _editingTemplate;
        }
        else
        {
            ResultTemplate = new WorkflowPreset
            {
                Id = string.Empty, // Will be generated as slug in SaveTemplate
                Category = category,
                Title = title,
                Description = description,
                Steps = _currentWorkflowSteps.Select(s => s.Clone()).ToList()
            };
        }

        _templateService.SaveTemplate(ResultTemplate);
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
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && (Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control && SaveBtn.IsEnabled)
        {
            SaveBtn_Click(sender, e);
            e.Handled = true;
        }
    }
}
