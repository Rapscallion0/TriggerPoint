using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;

namespace TriggerPoint.UI.Views;

public partial class WorkflowTemplatePickerDialog : Window
{
    private readonly IWorkflowTemplateService _templateService;
    private readonly IConfirmationDialogService _confirmationService;
    private readonly IReadOnlyList<WorkflowStep>? _currentWorkflowSteps;
    private List<WorkflowPreset> _allTemplates = [];

    public WorkflowPreset? SelectedTemplate { get; private set; }

    public WorkflowTemplatePickerDialog(
        IWorkflowTemplateService templateService,
        IConfirmationDialogService confirmationService,
        IReadOnlyList<WorkflowStep>? currentWorkflowSteps = null)
    {
        InitializeComponent();
        _templateService = templateService;
        _confirmationService = confirmationService;
        _currentWorkflowSteps = currentWorkflowSteps;

        Loaded += WorkflowTemplatePickerDialog_Loaded;
    }

    private void WorkflowTemplatePickerDialog_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshCategories();
        RefreshTemplateList();
        SearchBox.Focus();
    }

    private void RefreshCategories()
    {
        var currentSelection = CategoryFilterBox.SelectedItem as string;
        var categories = new List<string> { "All Categories" };
        categories.AddRange(_templateService.GetCategories());

        CategoryFilterBox.ItemsSource = categories;
        if (!string.IsNullOrEmpty(currentSelection) && categories.Contains(currentSelection))
        {
            CategoryFilterBox.SelectedItem = currentSelection;
        }
        else
        {
            CategoryFilterBox.SelectedIndex = 0;
        }
    }

    private void RefreshTemplateList()
    {
        _allTemplates = _templateService.GetAllTemplates().ToList();
        RenderFilteredTemplates();
    }

    private void RenderFilteredTemplates()
    {
        if (TemplatesHost == null) return;
        TemplatesHost.Children.Clear();

        var query = SearchBox.Text?.Trim() ?? string.Empty;
        var selectedCategory = CategoryFilterBox.SelectedItem as string;
        bool filterByCategory = !string.IsNullOrEmpty(selectedCategory) && selectedCategory != "All Categories";

        var filtered = _allTemplates.Where(t =>
        {
            if (filterByCategory && !string.Equals(t.Category, selectedCategory, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(query)) return true;

            return (t.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                   (t.Category?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                   (t.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
        }).ToList();

        if (filtered.Count == 0)
        {
            var emptyNotice = new Border
            {
                Padding = new Thickness(24),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var noticeStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            noticeStack.Children.Add(new TextBlock
            {
                Text = "🔍",
                FontSize = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            });
            noticeStack.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(query) ? "No templates available in this category." : $"No templates match '{query}'.",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Application.Current.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            emptyNotice.Child = noticeStack;
            TemplatesHost.Children.Add(emptyNotice);
            StatusCountText.Text = "0 templates found";
            return;
        }

        StatusCountText.Text = $"Showing {filtered.Count} {(filtered.Count == 1 ? "template" : "templates")}";

        // Group by category
        var groups = filtered.GroupBy(t => t.Category, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            // Category Header
            var headerBorder = new Border
            {
                Margin = new Thickness(4, 10, 4, 4),
                Padding = new Thickness(6, 4, 6, 4)
            };
            var headerText = new TextBlock
            {
                Text = group.Key,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue
            };
            headerBorder.Child = headerText;
            TemplatesHost.Children.Add(headerBorder);

            foreach (var template in group)
            {
                var card = CreateTemplateCard(template);
                TemplatesHost.Children.Add(card);
            }
        }
    }

    private FrameworkElement CreateTemplateCard(WorkflowPreset template)
    {
        var card = new Border
        {
            Background = Application.Current.TryFindResource("CardBgBrush") as Brush ?? Brushes.Transparent,
            BorderBrush = Application.Current.TryFindResource("CardBorderBrush") as Brush ?? Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(4, 0, 4, 8)
        };

        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Row 0: Title and Badges
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var titleText = new TextBlock
        {
            Text = template.Title,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Foreground = Application.Current.TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        titleStack.Children.Add(titleText);

        headerGrid.Children.Add(titleStack);

        var badgesStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        // Step count badge
        var stepBadge = new Border
        {
            Background = Application.Current.TryFindResource("BgTertiaryBrush") as Brush ?? Brushes.DimGray,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(6, 0, 0, 0)
        };
        stepBadge.Child = new TextBlock
        {
            Text = $"{template.Steps.Count} {(template.Steps.Count == 1 ? "step" : "steps")}",
            FontSize = 10,
            Foreground = Application.Current.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.LightGray
        };
        badgesStack.Children.Add(stepBadge);

        Grid.SetColumn(badgesStack, 1);
        headerGrid.Children.Add(badgesStack);
        mainGrid.Children.Add(headerGrid);

        // Row 1: Description
        if (!string.IsNullOrWhiteSpace(template.Description))
        {
            var descText = new TextBlock
            {
                Text = template.Description,
                FontSize = 11.5,
                Foreground = Application.Current.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.LightGray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 8)
            };
            Grid.SetRow(descText, 1);
            mainGrid.Children.Add(descText);
        }

        // Row 2: Action Buttons
        var actionsGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        actionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var useBtn = new Button
        {
            Content = "Use Template",
            Style = Application.Current.TryFindResource("AccentButtonStyle") as Style,
            Padding = new Thickness(12, 4, 12, 4),
            FontSize = 11,
            Height = 26,
            Tag = template
        };
        useBtn.Click += UseBtn_Click;
        actionsGrid.Children.Add(useBtn);

        var rightStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

        var editBtn = new Button
        {
            Content = "✏ Edit",
            Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
            Padding = new Thickness(8, 4, 8, 4),
            FontSize = 11,
            Height = 26,
            Margin = new Thickness(0, 0, 6, 0),
            Tag = template
        };
        editBtn.Click += EditBtn_Click;
        rightStack.Children.Add(editBtn);

        var deleteBtn = new Button
        {
            Content = "🗑 Delete",
            Style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style,
            Padding = new Thickness(8, 4, 8, 4),
            FontSize = 11,
            Height = 26,
            Tag = template
        };
        deleteBtn.Click += DeleteBtn_Click;
        rightStack.Children.Add(deleteBtn);

        Grid.SetColumn(rightStack, 2);
        actionsGrid.Children.Add(rightStack);

        Grid.SetRow(actionsGrid, 2);
        mainGrid.Children.Add(actionsGrid);

        card.Child = mainGrid;
        return card;
    }

    private void UseBtn_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is WorkflowPreset preset)
        {
            SelectedTemplate = preset;
            DialogResult = true;
            Close();
        }
    }

    private void EditBtn_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is WorkflowPreset preset)
        {
            var editor = new WorkflowTemplateEditorDialog(
                _templateService,
                existingTemplate: preset,
                stepsFromActiveWorkflow: _currentWorkflowSteps)
            {
                Owner = this
            };

            if (editor.ShowDialog() == true)
            {
                RefreshCategories();
                RefreshTemplateList();
            }
        }
    }

    private async void DeleteBtn_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is WorkflowPreset preset)
        {
            bool confirmed = await _confirmationService.ShowConfirmationAsync(
                $"Are you sure you want to delete the template '{preset.Title}'?\n\nThis will remove the JSON file from disk.",
                "Delete Workflow Template",
                "Delete",
                "Cancel");

            if (confirmed)
            {
                _templateService.DeleteTemplate(preset.Id);
                RefreshCategories();
                RefreshTemplateList();
            }
        }
    }

    private void NewTemplateBtn_Click(object sender, RoutedEventArgs e)
    {
        var editor = new WorkflowTemplateEditorDialog(
            _templateService,
            existingTemplate: null,
            stepsFromActiveWorkflow: _currentWorkflowSteps)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
        {
            RefreshCategories();
            RefreshTemplateList();
        }
    }

    private void OpenFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!System.IO.Directory.Exists(_templateService.TemplatesDirectory))
            {
                _templateService.EnsureDefaultTemplates();
            }

            Process.Start(new ProcessStartInfo("explorer.exe", _templateService.TemplatesDirectory)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open templates directory:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RenderFilteredTemplates();
    }

    private void CategoryFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RenderFilteredTemplates();
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
    }
}
