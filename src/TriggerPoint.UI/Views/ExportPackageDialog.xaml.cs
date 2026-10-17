using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;

namespace TriggerPoint.UI.Views;

public sealed class ExportTreeItemViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public TriggerItem Item { get; }
    public string Name => Item.Name;
    public ActionType ActionType => Item.ActionType;
    public List<ExportTreeItemViewModel> Children { get; } = [];
    public ExportTreeItemViewModel? Parent { get; set; }

    private bool _isChecked = true;
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked != value)
            {
                _isChecked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }
    }

    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }
    }

    public string IconSymbol => Item.ActionType switch
    {
        ActionType.Folder => "📁",
        ActionType.Snippet => "⚡",
        ActionType.Workflow => "⚡",
        ActionType.Macro => "🔴",
        ActionType.Service => "⚙",
        _ => "▶"
    };

    public Brush IconBrush => Item.ActionType switch
    {
        ActionType.Folder => Application.Current.TryFindResource("FolderBrush") as Brush ?? Brushes.Goldenrod,
        ActionType.Snippet => Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue,
        ActionType.Workflow => Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue,
        ActionType.Macro => Brushes.Crimson,
        ActionType.Service => Brushes.DarkOrange,
        _ => Application.Current.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray
    };

    public string DetailsText => Item.ActionType switch
    {
        ActionType.Folder => string.Empty,
        ActionType.Snippet when !string.IsNullOrEmpty(Item.Abbreviation) => $"[{Item.Abbreviation}]",
        _ when Item.Hotkey != null && !Item.Hotkey.IsEmpty => $"({Item.Hotkey.DisplayText})",
        _ => string.Empty
    };

    public ExportTreeItemViewModel(TriggerItem item, ExportTreeItemViewModel? parent = null)
    {
        Item = item;
        Parent = parent;
    }
}

public partial class ExportPackageDialog : Window
{
    private readonly IConfigRepository _repository;
    private readonly List<ExportTreeItemViewModel> _rootNodes = [];
    private readonly List<ExportTreeItemViewModel> _allNodes = [];

    public string? ExportedFilePath { get; private set; }
    public int ExportedFolderCount { get; private set; }
    public int ExportedActionCount { get; private set; }

    public ExportPackageDialog(
        IConfigRepository repository,
        IEnumerable<TriggerItem> allItems,
        TriggerItem? scopeItem = null)
    {
        InitializeComponent();
        _repository = repository;

        BuildTree(allItems, scopeItem);
        ExportTreeView.ItemsSource = _rootNodes;

        string initialScope = scopeItem != null
            ? scopeItem.Name
            : "TriggerPoint Pack";
        PackageScopeBox.Text = initialScope;

        UpdateSelectionSummary();
    }

    private void BuildTree(IEnumerable<TriggerItem> allItems, TriggerItem? scopeItem)
    {
        var itemsList = allItems.ToList();
        var lookup = new Dictionary<Guid, ExportTreeItemViewModel>();

        // If a specific folder or action was chosen as the scope, limit or pre-select it
        List<TriggerItem> targetSet;
        if (scopeItem != null)
        {
            if (scopeItem.ActionType == ActionType.Folder)
            {
                var descendantIds = GetDescendantIds(scopeItem.Id, itemsList);
                descendantIds.Add(scopeItem.Id);
                targetSet = itemsList.Where(x => descendantIds.Contains(x.Id)).ToList();
            }
            else
            {
                targetSet = [scopeItem];
            }
        }
        else
        {
            targetSet = itemsList;
        }

        foreach (var item in targetSet)
        {
            var vm = new ExportTreeItemViewModel(item);
            lookup[item.Id] = vm;
            _allNodes.Add(vm);
        }

        foreach (var vm in _allNodes)
        {
            if (vm.Item.ParentId.HasValue && lookup.TryGetValue(vm.Item.ParentId.Value, out var parentVm))
            {
                vm.Parent = parentVm;
                parentVm.Children.Add(vm);
            }
            else
            {
                _rootNodes.Add(vm);
            }
        }
    }

    private static HashSet<Guid> GetDescendantIds(Guid parentId, List<TriggerItem> items)
    {
        var result = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(parentId);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var child in items.Where(x => x.ParentId == cur))
            {
                if (result.Add(child.Id))
                {
                    queue.Enqueue(child.Id);
                }
            }
        }
        return result;
    }

    private void UpdateSelectionSummary()
    {
        int folders = _allNodes.Count(x => x.IsChecked && x.ActionType == ActionType.Folder);
        int actions = _allNodes.Count(x => x.IsChecked && x.ActionType != ActionType.Folder);
        int total = folders + actions;

        SelectionCountText.Text = $"{total} items selected ({folders} folders, {actions} actions)";
        SummaryFooterText.Text = total > 0
            ? $"Ready to package {folders} folder(s) and {actions} action(s)."
            : "Please select at least one item to export.";

        ExportBtn.IsEnabled = total > 0;
    }

    private void ItemCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ExportTreeItemViewModel vm)
        {
            SetChildrenChecked(vm, vm.IsChecked);
            UpdateSelectionSummary();
        }
    }

    private static void SetChildrenChecked(ExportTreeItemViewModel parent, bool isChecked)
    {
        foreach (var child in parent.Children)
        {
            child.IsChecked = isChecked;
            SetChildrenChecked(child, isChecked);
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var node in _allNodes)
        {
            node.IsChecked = true;
        }
        UpdateSelectionSummary();
    }

    private void DeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var node in _allNodes)
        {
            node.IsChecked = false;
        }
        UpdateSelectionSummary();
    }

    private void ExpandAll_Click(object sender, RoutedEventArgs e)
    {
        SetExpandedRecursive(_rootNodes, true);
    }

    private void CollapseAll_Click(object sender, RoutedEventArgs e)
    {
        SetExpandedRecursive(_rootNodes, false);
    }

    private static void SetExpandedRecursive(IEnumerable<ExportTreeItemViewModel> nodes, bool isExpanded)
    {
        foreach (var node in nodes)
        {
            node.IsExpanded = isExpanded;
            SetExpandedRecursive(node.Children, isExpanded);
        }
    }

    private async void ExportBtn_Click(object sender, RoutedEventArgs e)
    {
        var selectedNodes = _allNodes.Where(x => x.IsChecked).ToList();
        if (selectedNodes.Count == 0) return;

        string scopeName = string.IsNullOrWhiteSpace(PackageScopeBox.Text)
            ? "TriggerPoint Pack"
            : PackageScopeBox.Text.Trim();

        string safeName = string.Join("_", scopeName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "package";

        var sfd = new SaveFileDialog
        {
            Title = "Save TriggerPoint Package",
            Filter = "TriggerPoint Package (*.tppack)|*.tppack|JSON Backup File (*.json)|*.json|All Files (*.*)|*.*",
            FileName = $"{safeName}.tppack",
            DefaultExt = ".tppack"
        };

        if (sfd.ShowDialog(this) != true) return;

        try
        {
            bool sanitize = SanitizeSecretsCheck.IsChecked == true;
            var exportedItems = new List<TriggerItem>();

            foreach (var node in selectedNodes)
            {
                var clone = node.Item.Clone();

                // If parent folder was not selected, detach parent so it imports at root
                if (clone.ParentId.HasValue && !selectedNodes.Any(x => x.Item.Id == clone.ParentId.Value))
                {
                    clone.ParentId = null;
                }

                if (sanitize)
                {
                    SanitizeItem(clone);
                }

                exportedItems.Add(clone);
            }

            var package = ConfigurationBackupPackage.CreateTreeItems(exportedItems, scopeName);
            package.AuthorNotes = string.IsNullOrWhiteSpace(AuthorNotesBox.Text) ? null : AuthorNotesBox.Text.Trim();
            package.IsSanitized = sanitize;

            await _repository.ExportPackageAsync(sfd.FileName, package);

            ExportedFilePath = sfd.FileName;
            ExportedFolderCount = package.FolderCount;
            ExportedActionCount = package.ActionCount;

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to export package: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void SanitizeItem(TriggerItem item)
    {
        if (item.Payload?.WorkflowVariables != null)
        {
            foreach (var v in item.Payload.WorkflowVariables)
            {
                if (v.IsSecret ||
                    v.Name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                    v.Name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                    v.Name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                    v.Name.Contains("key", StringComparison.OrdinalIgnoreCase) ||
                    v.Name.Contains("auth", StringComparison.OrdinalIgnoreCase))
                {
                    v.Value = string.Empty;
                }
            }
        }

        if (item.Payload?.WorkflowSteps != null)
        {
            foreach (var step in item.Payload.WorkflowSteps)
            {
                if (step.PromptFields != null)
                {
                    foreach (var field in step.PromptFields)
                    {
                        if (field.VariableName.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                            field.VariableName.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                            field.VariableName.Contains("token", StringComparison.OrdinalIgnoreCase))
                        {
                            field.DefaultValue = string.Empty;
                        }
                    }
                }
            }
        }
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
        }
    }
}
