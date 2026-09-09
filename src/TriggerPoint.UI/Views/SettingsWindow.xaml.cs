using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Serilog;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;
using TriggerPoint.Infrastructure.Services;
using TriggerPoint.Infrastructure.Win32;

namespace TriggerPoint.UI.Views;

public class TriggerTreeItemViewModel : INotifyPropertyChanged
{
    public TriggerItem Item { get; }
    public ObservableCollection<TriggerTreeItemViewModel> Children { get; } = [];

    public string Name => Item.Name;
    public string IconSymbol => Item.ActionType switch
    {
        ActionType.Folder => "📁",
        ActionType.Snippet => "📝",
        ActionType.Shell => "⚡",
        _ => "▶"
    };

    public string HotkeyDisplay => Item.Hotkey?.DisplayText ?? string.Empty;
    public Visibility HasHotkey => !string.IsNullOrWhiteSpace(HotkeyDisplay) ? Visibility.Visible : Visibility.Collapsed;

    // Only show conflict if item actually has a non-empty hotkey AND a conflict status
    public Visibility HasConflictVisibility => 
        (Item.Hotkey != null && !Item.Hotkey.IsEmpty && Item.ConflictStatus.HasConflict) 
        ? Visibility.Visible 
        : Visibility.Collapsed;

    public string ConflictTooltip => Item.ConflictStatus.SystemMessage ?? "Hotkey Conflict";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
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

    public event PropertyChangedEventHandler? PropertyChanged;

    public TriggerTreeItemViewModel(TriggerItem item)
    {
        Item = item;
    }

    public void NotifyUpdated()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconSymbol)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HotkeyDisplay)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasHotkey)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasConflictVisibility)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ConflictTooltip)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
    }
}

public partial class SettingsWindow : Window
{
    private readonly ILogger _logger = Log.ForContext<SettingsWindow>();
    private readonly IConfigRepository _repository;
    private readonly IShortcutListener _shortcutListener;
    private readonly IActionExecutor _executor;

    private List<TriggerItem> _items = [];
    private ObservableCollection<TriggerTreeItemViewModel> _treeRoots = [];
    private TriggerItem? _selectedItem;
    private bool _isUpdatingForm;

    // TreeView drag & drop re-sorting
    private Point? _treeDragStartPoint;
    private TriggerTreeItemViewModel? _draggedTreeVm;

    // Window Target Crosshair Tool (Spy++ style)
    private bool _isCapturingWindow;
    private string _windowTargetMode = "Shell";
    private readonly IContextFilterService? _contextFilterService;
    private readonly ILogManagerService? _logManagerService;

    public bool IsExiting { get; set; }

    public SettingsWindow(
        IConfigRepository repository,
        IShortcutListener shortcutListener,
        IActionExecutor executor,
        IContextFilterService? contextFilterService = null,
        ILogManagerService? logManagerService = null)
    {
        InitializeComponent();
        _repository = repository;
        _shortcutListener = shortcutListener;
        _executor = executor;
        _contextFilterService = contextFilterService;
        _logManagerService = logManagerService;

        HotkeyRecorder.BindingRecorded += HotkeyRecorder_BindingRecorded;
        _shortcutListener.ConflictsUpdated += (s, e) => Dispatcher.Invoke(RefreshTreeConflictStates);

        AllowedProcessesTagInput.TagsChanged += (s, e) => CommitCurrentFormChanges();
        ExcludedProcessesTagInput.TagsChanged += (s, e) => CommitCurrentFormChanges();
        AllowedUrlsTagInput.TagsChanged += (s, e) => CommitCurrentFormChanges();
        ExcludedUrlsTagInput.TagsChanged += (s, e) => CommitCurrentFormChanges();

        StateChanged += (s, e) =>
        {
            if (MaximizeBtn != null)
            {
                MaximizeBtn.Content = WindowState == WindowState.Maximized ? "❐" : "▢";
            }
        };

        Loaded += async (s, e) => await LoadDataAsync();
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        _items = (await _repository.LoadAsync()).ToList();
        _shortcutListener.RegisterAll(_items);
        RebuildTree();
        UpdateSnoozeButtonUi();
    }

    private void RebuildTree()
    {
        _treeRoots.Clear();
        var folderMap = new Dictionary<Guid, TriggerTreeItemViewModel>();

        // First pass: Folders
        var folders = _items.Where(x => x.ActionType == ActionType.Folder).OrderBy(x => x.OrderIndex).ToList();
        foreach (var item in folders)
        {
            folderMap[item.Id] = new TriggerTreeItemViewModel(item);
        }

        foreach (var item in folders)
        {
            var vm = folderMap[item.Id];
            if (item.ParentId.HasValue && folderMap.TryGetValue(item.ParentId.Value, out var parentVm))
            {
                parentVm.Children.Add(vm);
            }
            else
            {
                _treeRoots.Add(vm);
            }
        }

        // Second pass: Actions
        foreach (var item in _items.Where(x => x.ActionType != ActionType.Folder).OrderBy(x => x.OrderIndex))
        {
            var vm = new TriggerTreeItemViewModel(item);
            if (item.ParentId.HasValue && folderMap.TryGetValue(item.ParentId.Value, out var parentVm))
            {
                parentVm.Children.Add(vm);
            }
            else
            {
                _treeRoots.Add(vm);
            }
        }

        ItemsTreeView.ItemsSource = _treeRoots;

        // Restore selection or select first item if available
        if (_treeRoots.Count > 0)
        {
            if (_selectedItem != null && _items.Any(x => x.Id == _selectedItem.Id))
            {
                SelectTreeItem(_selectedItem);
            }
            else
            {
                var first = _treeRoots[0].Children.Count > 0 ? _treeRoots[0].Children[0] : _treeRoots[0];
                SelectTreeItem(first.Item);
            }
        }
        else
        {
            _selectedItem = null;
            ClearForm();
        }
    }

    public void SelectTreeItem(TriggerItem item)
    {
        _selectedItem = item;
        PopulateForm(item);
        SetViewModelSelected(_treeRoots, item.Id);
    }

    private bool SetViewModelSelected(IEnumerable<TriggerTreeItemViewModel> vms, Guid targetId)
    {
        bool found = false;
        foreach (var vm in vms)
        {
            if (vm.Item.Id == targetId)
            {
                vm.IsSelected = true;
                found = true;
            }
            else
            {
                if (SetViewModelSelected(vm.Children, targetId))
                {
                    vm.IsExpanded = true;
                    found = true;
                }
                else
                {
                    vm.IsSelected = false;
                }
            }
        }
        return found;
    }

    private void ItemsTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TriggerTreeItemViewModel vm)
        {
            // Save changes from current form before switching
            CommitCurrentFormChanges();
            _selectedItem = vm.Item;
            PopulateForm(vm.Item);
        }
    }

    private void PopulateForm(TriggerItem item)
    {
        _isUpdatingForm = true;
        try
        {
            EditorHeaderTitle.Text = $"Edit: {item.Name}";
            ItemNameBox.Text = item.Name;
            ItemDescBox.Text = item.Description;
            ActionTypeCombo.SelectedIndex = (int)item.ActionType;
            PresentationModeCombo.SelectedIndex = (int)item.PresentationMode;
            HotkeyRecorder.Binding = item.Hotkey;
            AcceleratorBox.Text = item.AcceleratorKey ?? string.Empty;

            // Shell payload
            ShellCommandBox.Text = item.Payload.Command;
            ShellArgsBox.Text = item.Payload.Arguments;
            ShellWorkDirBox.Text = item.Payload.WorkingDirectory;
            ShellRunAsAdminCheck.IsChecked = item.Payload.RunAsAdmin;

            // Snippet payload
            SnippetTemplateBox.Text = item.Payload.SnippetTemplate;

            // Context filter
            AllowedProcessesTagInput.SetTags(item.ContextFilter.AllowedProcesses);
            ExcludedProcessesTagInput.SetTags(item.ContextFilter.ExcludedProcesses);
            AllowedUrlsTagInput.SetTags(item.ContextFilter.AllowedUrls);
            ExcludedUrlsTagInput.SetTags(item.ContextFilter.ExcludedUrls);

            UpdateFormVisibility(item.ActionType);
            UpdateConflictBanner(item);
        }
        finally
        {
            _isUpdatingForm = false;
        }
    }

    private void UpdateFormVisibility(ActionType actionType)
    {
        if (actionType == ActionType.Shell)
        {
            ShellSettingsGroup.Visibility = Visibility.Visible;
            SnippetSettingsGroup.Visibility = Visibility.Collapsed;
        }
        else if (actionType == ActionType.Snippet)
        {
            ShellSettingsGroup.Visibility = Visibility.Collapsed;
            SnippetSettingsGroup.Visibility = Visibility.Visible;
        }
        else // Folder
        {
            ShellSettingsGroup.Visibility = Visibility.Collapsed;
            SnippetSettingsGroup.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateConflictBanner(TriggerItem item)
    {
        if (item.Hotkey != null && !item.Hotkey.IsEmpty && item.ConflictStatus.HasConflict)
        {
            ConflictBanner.Visibility = Visibility.Visible;
            ConflictBannerTitle.Text = item.ConflictStatus.ConflictType == HotkeyConflictType.Internal
                ? "⚠ Internal Conflict Detected"
                : "⚠ System Hotkey Conflict";
            ConflictBannerMessage.Text = item.ConflictStatus.SystemMessage ?? "Conflict detected.";
        }
        else
        {
            ConflictBanner.Visibility = Visibility.Collapsed;
        }
    }

    private void CommitCurrentFormChanges()
    {
        if (_selectedItem == null || _isUpdatingForm) return;

        _selectedItem.Name = ItemNameBox.Text.Trim();
        _selectedItem.Description = ItemDescBox.Text.Trim();
        _selectedItem.ActionType = (ActionType)ActionTypeCombo.SelectedIndex;
        _selectedItem.PresentationMode = (PresentationMode)PresentationModeCombo.SelectedIndex;
        _selectedItem.Hotkey = HotkeyRecorder.Binding;
        _selectedItem.AcceleratorKey = AcceleratorBox.Text.Trim();

        _selectedItem.Payload.Command = ShellCommandBox.Text.Trim();
        _selectedItem.Payload.Arguments = ShellArgsBox.Text.Trim();
        _selectedItem.Payload.WorkingDirectory = ShellWorkDirBox.Text.Trim();
        _selectedItem.Payload.RunAsAdmin = ShellRunAsAdminCheck.IsChecked == true;

        _selectedItem.Payload.SnippetTemplate = SnippetTemplateBox.Text;

        _selectedItem.ContextFilter.AllowedProcesses = AllowedProcessesTagInput.GetTags();
        _selectedItem.ContextFilter.ExcludedProcesses = ExcludedProcessesTagInput.GetTags();
        _selectedItem.ContextFilter.AllowedUrls = AllowedUrlsTagInput.GetTags();
        _selectedItem.ContextFilter.ExcludedUrls = ExcludedUrlsTagInput.GetTags();

        // If hotkey was cleared, reset conflict status
        if (_selectedItem.Hotkey == null || _selectedItem.Hotkey.IsEmpty)
        {
            _selectedItem.ConflictStatus = HotkeyConflictStatus.None;
        }

        // Refresh tree view item representation
        FindViewModel(_selectedItem)?.NotifyUpdated();
    }

    private void ActionTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUpdatingForm && ActionTypeCombo.SelectedIndex >= 0)
        {
            UpdateFormVisibility((ActionType)ActionTypeCombo.SelectedIndex);
        }
    }

    private void HotkeyRecorder_BindingRecorded(object? sender, ShortcutBinding? newBinding)
    {
        if (_selectedItem == null) return;

        if (newBinding == null || newBinding.IsEmpty)
        {
            _selectedItem.ConflictStatus = HotkeyConflictStatus.None;
        }
        else
        {
            // Check for immediate potential conflict
            var conflict = HotkeyRegistryValidator.CheckPotentialConflict(_selectedItem, newBinding, _items);
            _selectedItem.ConflictStatus = conflict ?? HotkeyConflictStatus.None;
        }

        UpdateConflictBanner(_selectedItem);
        FindViewModel(_selectedItem)?.NotifyUpdated();
    }

    private void InsertTokenChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string token })
        {
            var caret = SnippetTemplateBox.CaretIndex;
            SnippetTemplateBox.Text = SnippetTemplateBox.Text.Insert(caret, token);
            SnippetTemplateBox.CaretIndex = caret + token.Length;
            SnippetTemplateBox.Focus();
        }
    }

    private void BrowseFileBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select Application, Script or File",
            Filter = "All Executable & Documents|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            ShellCommandBox.Text = dlg.FileName;
            if (string.IsNullOrWhiteSpace(ItemNameBox.Text))
            {
                ItemNameBox.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
            }
        }
    }

    private void BrowseDirBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Select Working Directory"
        };
        if (dlg.ShowDialog() == true)
        {
            ShellWorkDirBox.Text = dlg.FolderName;
        }
    }

    private async void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        CommitCurrentFormChanges();

        try
        {
            await _repository.SaveAsync(_items);
            _shortcutListener.RegisterAll(_items);
            RefreshTreeConflictStates();
            StatusText.Text = $"Saved and registered {_items.Count} items at {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save configuration.");
            ModernMessageDialog.ShowAlert(this, "TriggerPoint Error", $"Failed to save configuration: {ex.Message}", ModernDialogType.Error);
        }
    }

    private void AppSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_logManagerService == null) return;
        var dlg = new ApplicationSettingsWindow(_repository, _logManagerService)
        {
            Owner = this
        };
        dlg.ShowDialog();
    }

    private TriggerTreeItemViewModel? _rightClickedTreeVm;

    private void AddActionBtn_Click(object sender, RoutedEventArgs e)
    {
        CommitCurrentFormChanges();

        Guid? parentId = null;
        if (sender == ContextAddActionItem && _rightClickedTreeVm == null)
        {
            // Right-clicked in blank space -> add at root
            parentId = null;
        }
        else if (_rightClickedTreeVm != null)
        {
            parentId = _rightClickedTreeVm.Item.ActionType == ActionType.Folder ? _rightClickedTreeVm.Item.Id : _rightClickedTreeVm.Item.ParentId;
        }
        else if (_selectedItem != null)
        {
            parentId = _selectedItem.ActionType == ActionType.Folder ? _selectedItem.Id : _selectedItem.ParentId;
        }

        var newItem = new TriggerItem
        {
            Id = Guid.NewGuid(),
            ParentId = parentId,
            Name = "New Action",
            ActionType = ActionType.Shell,
            PresentationMode = PresentationMode.Direct,
            OrderIndex = _items.Count
        };

        _items.Add(newItem);
        RebuildTree();
        SelectTreeItem(newItem);
        _ = RestoreTreeFocus(newItem);
    }

    private void AddMenuBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private void ItemsTreeView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? source = e.OriginalSource as DependencyObject;
        while (source != null && source is not TreeViewItem)
        {
            source = VisualTreeHelper.GetParent(source);
        }

        if (source is TreeViewItem tvi && tvi.DataContext is TriggerTreeItemViewModel vm)
        {
            _rightClickedTreeVm = vm;
            tvi.Focus();
            tvi.IsSelected = true;
            CommitCurrentFormChanges();
            _selectedItem = vm.Item;
            PopulateForm(vm.Item);
        }
        else
        {
            _rightClickedTreeVm = null;
        }
    }

    private void TreeContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (_rightClickedTreeVm != null)
        {
            ContextDeleteItem.Visibility = Visibility.Visible;
            if (ContextDeleteSeparator != null) ContextDeleteSeparator.Visibility = Visibility.Visible;
            ContextDeleteItem.IsEnabled = true;
            ContextDeleteItem.Header = $"Delete '{_rightClickedTreeVm.Name}'";

            ContextAddActionItem.Header = "New Action";
            ContextAddFolderItem.Header = "New Folder";
        }
        else
        {
            ContextDeleteItem.Visibility = Visibility.Collapsed;
            if (ContextDeleteSeparator != null) ContextDeleteSeparator.Visibility = Visibility.Collapsed;

            ContextAddActionItem.Header = "New Action at Root";
            ContextAddFolderItem.Header = "New Folder at Root";
        }
    }

    private void AddFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        CommitCurrentFormChanges();

        Guid? parentId = null;
        if (sender == ContextAddFolderItem && _rightClickedTreeVm == null)
        {
            // Right-clicked in blank space -> add at root
            parentId = null;
        }
        else if (_rightClickedTreeVm != null)
        {
            parentId = _rightClickedTreeVm.Item.ActionType == ActionType.Folder ? _rightClickedTreeVm.Item.Id : _rightClickedTreeVm.Item.ParentId;
        }
        else if (_selectedItem != null)
        {
            parentId = _selectedItem.ActionType == ActionType.Folder ? _selectedItem.Id : _selectedItem.ParentId;
        }

        var newFolder = new TriggerItem
        {
            Id = Guid.NewGuid(),
            ParentId = parentId,
            Name = "New Folder",
            ActionType = ActionType.Folder,
            PresentationMode = PresentationMode.CursorMenu,
            OrderIndex = _items.Count
        };

        _items.Add(newFolder);
        RebuildTree();
        SelectTreeItem(newFolder);
        _ = RestoreTreeFocus(newFolder);
    }

    private TriggerItem? FindPostDeleteFocusCandidate(TriggerItem itemToDelete)
    {
        // Get all siblings belonging to the same parent, in order
        var siblings = _items
            .Where(x => x.ParentId == itemToDelete.ParentId)
            .OrderBy(x => x.OrderIndex)
            .ToList();

        int currentIndex = siblings.FindIndex(x => x.Id == itemToDelete.Id);

        // 1. Next sibling
        if (currentIndex >= 0 && currentIndex < siblings.Count - 1)
        {
            return siblings[currentIndex + 1];
        }

        // 2. Previous sibling
        if (currentIndex > 0)
        {
            return siblings[currentIndex - 1];
        }

        // 3. Parent folder (if inside a folder)
        if (itemToDelete.ParentId.HasValue)
        {
            var parent = _items.FirstOrDefault(x => x.Id == itemToDelete.ParentId.Value);
            if (parent != null) return parent;
        }

        // 4. Any remaining root item
        var remainingRoot = _items
            .Where(x => !x.ParentId.HasValue && x.Id != itemToDelete.Id)
            .OrderBy(x => x.OrderIndex)
            .FirstOrDefault();
        if (remainingRoot != null) return remainingRoot;

        // 5. Any remaining item at all
        return _items.FirstOrDefault(x => x.Id != itemToDelete.Id);
    }

    private async System.Threading.Tasks.Task RestoreTreeFocus(TriggerItem? targetItem)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            ItemsTreeView.Focus();
            if (targetItem != null)
            {
                var tvi = FindTreeViewItemForId(ItemsTreeView, targetItem.Id);
                if (tvi != null)
                {
                    tvi.Focus();
                    Keyboard.Focus(tvi);
                }
            }
        }, DispatcherPriority.Input);
    }

    private TreeViewItem? FindTreeViewItemForId(ItemsControl parent, Guid id)
    {
        for (int i = 0; i < parent.Items.Count; i++)
        {
            if (parent.ItemContainerGenerator.ContainerFromIndex(i) is TreeViewItem tvi)
            {
                if (tvi.DataContext is TriggerTreeItemViewModel vm && vm.Item.Id == id)
                {
                    return tvi;
                }

                var childTvi = FindTreeViewItemForId(tvi, id);
                if (childTvi != null)
                {
                    return childTvi;
                }
            }
        }
        return null;
    }

    private void ClearForm()
    {
        _isUpdatingForm = true;
        try
        {
            EditorHeaderTitle.Text = "No Selection";
            ItemNameBox.Text = string.Empty;
            ItemDescBox.Text = string.Empty;
            ActionTypeCombo.SelectedIndex = 0;
            PresentationModeCombo.SelectedIndex = 0;
            HotkeyRecorder.Binding = null;
            AcceleratorBox.Text = string.Empty;
            ShellCommandBox.Text = string.Empty;
            ShellArgsBox.Text = string.Empty;
            ShellWorkDirBox.Text = string.Empty;
            ShellRunAsAdminCheck.IsChecked = false;
            SnippetTemplateBox.Text = string.Empty;
            AllowedProcessesTagInput.SetTags(new List<string>());
            ExcludedProcessesTagInput.SetTags(new List<string>());
        }
        finally
        {
            _isUpdatingForm = false;
        }
    }

    private async void DeleteItemBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem == null) return;

        var itemToDelete = _selectedItem;
        var focusCandidate = FindPostDeleteFocusCandidate(itemToDelete);

        if (itemToDelete.ActionType == ActionType.Folder)
        {
            var directChildren = _items.Where(x => x.ParentId == itemToDelete.Id).ToList();
            var descendants = new List<TriggerItem>();
            void CollectDescendants(Guid folderId)
            {
                var children = _items.Where(x => x.ParentId == folderId).ToList();
                descendants.AddRange(children);
                foreach (var child in children.Where(c => c.ActionType == ActionType.Folder))
                {
                    CollectDescendants(child.Id);
                }
            }
            CollectDescendants(itemToDelete.Id);

            if (descendants.Count > 0)
            {
                var choice = ModernMessageDialog.ShowFolderDeleteConfirm(
                    this,
                    itemToDelete.Name,
                    descendants.Count);

                if (choice == FolderDeleteChoice.Cancel)
                {
                    await RestoreTreeFocus(itemToDelete);
                    return;
                }

                if (choice == FolderDeleteChoice.MoveToRoot)
                {
                    // Move direct children to root
                    foreach (var child in directChildren)
                    {
                        child.ParentId = null;
                    }

                    _items.Remove(itemToDelete);
                    _selectedItem = null;
                    RebuildTree();

                    if (focusCandidate != null && _items.Any(x => x.Id == focusCandidate.Id))
                    {
                        SelectTreeItem(focusCandidate);
                    }
                    else if (directChildren.Count > 0)
                    {
                        SelectTreeItem(directChildren[0]);
                        focusCandidate = directChildren[0];
                    }

                    StatusText.Text = $"Deleted folder '{itemToDelete.Name}' and moved {descendants.Count} item(s) to root.";
                    await RestoreTreeFocus(focusCandidate);
                    return;
                }

                if (choice == FolderDeleteChoice.DeleteAll)
                {
                    var toDelete = new List<TriggerItem> { itemToDelete };
                    toDelete.AddRange(descendants);

                    foreach (var item in toDelete)
                    {
                        _items.Remove(item);
                    }

                    _selectedItem = null;
                    RebuildTree();

                    if (focusCandidate != null && _items.Any(x => x.Id == focusCandidate.Id))
                    {
                        SelectTreeItem(focusCandidate);
                    }

                    StatusText.Text = $"Deleted folder '{itemToDelete.Name}' and {descendants.Count} contained item(s).";
                    await RestoreTreeFocus(focusCandidate);
                    return;
                }
            }
            else
            {
                // Empty folder
                bool confirmed = ModernMessageDialog.ShowConfirm(
                    this,
                    "Confirm Delete",
                    $"Are you sure you want to delete folder '{itemToDelete.Name}'?",
                    "Delete",
                    "Cancel",
                    isDestructive: true);

                if (!confirmed)
                {
                    await RestoreTreeFocus(itemToDelete);
                    return;
                }

                _items.Remove(itemToDelete);
                _selectedItem = null;
                RebuildTree();

                if (focusCandidate != null && _items.Any(x => x.Id == focusCandidate.Id))
                {
                    SelectTreeItem(focusCandidate);
                }

                StatusText.Text = $"Deleted folder '{itemToDelete.Name}'.";
                await RestoreTreeFocus(focusCandidate);
                return;
            }
        }
        else
        {
            // Action item
            bool confirmed = ModernMessageDialog.ShowConfirm(
                this,
                "Confirm Delete",
                $"Are you sure you want to delete '{itemToDelete.Name}'?",
                "Delete",
                "Cancel",
                isDestructive: true);

            if (!confirmed)
            {
                await RestoreTreeFocus(itemToDelete);
                return;
            }

            _items.Remove(itemToDelete);
            _selectedItem = null;
            RebuildTree();

            if (focusCandidate != null && _items.Any(x => x.Id == focusCandidate.Id))
            {
                SelectTreeItem(focusCandidate);
            }

            StatusText.Text = $"Deleted item '{itemToDelete.Name}'.";
            await RestoreTreeFocus(focusCandidate);
        }
    }

    private async void TestActionBtn_Click(object sender, RoutedEventArgs e)
    {
        CommitCurrentFormChanges();
        if (_selectedItem == null) return;

        StatusText.Text = $"Testing action '{_selectedItem.Name}'...";
        await _executor.ExecuteAsync(_selectedItem);
        StatusText.Text = $"Action '{_selectedItem.Name}' execution triggered.";
    }

    private void SnoozeToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        _shortcutListener.IsSnoozed = !_shortcutListener.IsSnoozed;
        UpdateSnoozeButtonUi();
    }

    private void UpdateSnoozeButtonUi()
    {
        if (_shortcutListener.IsSnoozed)
        {
            SnoozeToggleBtn.Content = "🔕 Snoozed";
            SnoozeToggleBtn.Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("WarningBrush");
        }
        else
        {
            SnoozeToggleBtn.Content = "🔔 Active";
            SnoozeToggleBtn.Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextPrimaryBrush");
        }
    }

    private void RefreshTreeConflictStates()
    {
        foreach (var root in _treeRoots)
        {
            root.NotifyUpdated();
            foreach (var child in root.Children)
            {
                child.NotifyUpdated();
            }
        }

        if (_selectedItem != null)
        {
            UpdateConflictBanner(_selectedItem);
        }
    }

    private TriggerTreeItemViewModel? FindViewModel(TriggerItem item)
    {
        foreach (var root in _treeRoots)
        {
            if (root.Item.Id == item.Id) return root;
            foreach (var child in root.Children)
            {
                if (child.Item.Id == item.Id) return child;
            }
        }
        return null;
    }

    private void TreeSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = TreeSearchBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            RebuildTree();
            return;
        }

        // Filter tree roots
        var filteredRoots = new ObservableCollection<TriggerTreeItemViewModel>();
        foreach (var item in _items)
        {
            if (item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                filteredRoots.Add(new TriggerTreeItemViewModel(item));
            }
        }
        ItemsTreeView.ItemsSource = filteredRoots;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                ApplyDroppedFile(files[0]);
                e.Handled = true;
            }
        }
    }

    private void DropTargetZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            DropTargetZone.BorderBrush = (System.Windows.Media.Brush)Application.Current.FindResource("AccentBrush");
            e.Handled = true;
        }
    }

    private void DropTargetZone_DragLeave(object sender, DragEventArgs e)
    {
        DropTargetZone.BorderBrush = (System.Windows.Media.Brush)Application.Current.FindResource("BorderBrush");
    }

    private void DropTargetZone_Drop(object sender, DragEventArgs e)
    {
        DropTargetZone.BorderBrush = (System.Windows.Media.Brush)Application.Current.FindResource("BorderBrush");
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                ApplyDroppedFile(files[0]);
                e.Handled = true;
            }
        }
    }

    private void ApplyDroppedFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;

        // If no item is selected or selected item is a Folder, create a new Action first
        if (_selectedItem == null || _selectedItem.ActionType == ActionType.Folder)
        {
            AddActionBtn_Click(this, new RoutedEventArgs());
        }

        string target = filePath;
        string args = string.Empty;
        string workDir = string.Empty;
        string desc = string.Empty;
        string friendlyName = Path.GetFileNameWithoutExtension(filePath);

        // If it's a .lnk file, resolve target executable and properties
        if (filePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var resolved = ShellLinkScanner.ResolveShortcut(filePath);
            if (resolved.HasValue)
            {
                target = resolved.Value.TargetPath;
                args = resolved.Value.Arguments;
                workDir = resolved.Value.WorkingDirectory;
                desc = resolved.Value.Description;
            }
        }

        if (string.IsNullOrWhiteSpace(workDir) && File.Exists(target))
        {
            workDir = Path.GetDirectoryName(target) ?? string.Empty;
        }

        // Switch to Shell action type
        ActionTypeCombo.SelectedIndex = (int)ActionType.Shell;
        ShellCommandBox.Text = target;
        ShellArgsBox.Text = args;
        ShellWorkDirBox.Text = workDir;

        if (string.IsNullOrWhiteSpace(ItemNameBox.Text) || ItemNameBox.Text.StartsWith("New Action", StringComparison.OrdinalIgnoreCase))
        {
            ItemNameBox.Text = friendlyName;
        }

        if (string.IsNullOrWhiteSpace(ItemDescBox.Text) && !string.IsNullOrWhiteSpace(desc))
        {
            ItemDescBox.Text = desc;
        }

        CommitCurrentFormChanges();
        StatusText.Text = $"Target set to '{friendlyName}': {target}";
    }

    // Window Title Bar Controls
    private void WindowMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void WindowMaximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void WindowClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // Keyboard navigation & delete key
    private void ItemsTreeView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && _selectedItem != null)
        {
            DeleteItemBtn_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    // TreeView Drag & Drop Re-sorting
    private void ItemsTreeView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _treeDragStartPoint = e.GetPosition(null);
        _draggedTreeVm = FindTreeItemViewModelUnderMouse(e.OriginalSource as DependencyObject);
    }

    private void ItemsTreeView_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && _treeDragStartPoint.HasValue && _draggedTreeVm != null)
        {
            var currentPoint = e.GetPosition(null);
            var diff = _treeDragStartPoint.Value - currentPoint;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                var data = new DataObject("TriggerTreeItemViewModel", _draggedTreeVm);
                DragDrop.DoDragDrop(ItemsTreeView, data, DragDropEffects.Move);
                _treeDragStartPoint = null;
                _draggedTreeVm = null;
            }
        }
    }

    private void ItemsTreeView_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("TriggerTreeItemViewModel"))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private async void ItemsTreeView_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("TriggerTreeItemViewModel")) return;

        var draggedVm = e.Data.GetData("TriggerTreeItemViewModel") as TriggerTreeItemViewModel;
        if (draggedVm == null) return;

        var targetVm = FindTreeItemViewModelUnderMouse(e.OriginalSource as DependencyObject);

        // Cannot drop on itself
        if (targetVm != null && targetVm.Item.Id == draggedVm.Item.Id) return;

        // If dragged item is a folder, cannot drop into itself or its descendants
        if (draggedVm.Item.ActionType == ActionType.Folder && targetVm != null)
        {
            if (IsDescendant(targetVm.Item, draggedVm.Item.Id))
            {
                StatusText.Text = "Cannot move a folder into its own sub-folder.";
                return;
            }
        }

        CommitCurrentFormChanges();

        if (targetVm == null)
        {
            // Dropped on empty area of TreeView -> move to root at the end
            draggedVm.Item.ParentId = null;
            draggedVm.Item.OrderIndex = _items.Count > 0 ? _items.Max(x => x.OrderIndex) + 1 : 0;
        }
        else if (targetVm.Item.ActionType == ActionType.Folder)
        {
            // Dropped onto a folder -> move inside the folder
            draggedVm.Item.ParentId = targetVm.Item.Id;
            var siblings = _items.Where(x => x.ParentId == targetVm.Item.Id && x.Id != draggedVm.Item.Id).ToList();
            draggedVm.Item.OrderIndex = siblings.Count > 0 ? siblings.Max(x => x.OrderIndex) + 1 : 0;
        }
        else
        {
            // Dropped onto an action -> place alongside target item (same parent) at target's position
            draggedVm.Item.ParentId = targetVm.Item.ParentId;
            var siblings = _items.Where(x => x.ParentId == targetVm.Item.ParentId && x.Id != draggedVm.Item.Id)
                                 .OrderBy(x => x.OrderIndex)
                                 .ToList();

            int targetIdx = siblings.IndexOf(targetVm.Item);
            if (targetIdx >= 0)
            {
                siblings.Insert(targetIdx, draggedVm.Item);
            }
            else
            {
                siblings.Add(draggedVm.Item);
            }

            for (int i = 0; i < siblings.Count; i++)
            {
                siblings[i].OrderIndex = i;
            }
        }

        RebuildTree();
        SelectTreeItem(draggedVm.Item);

        try
        {
            await _repository.SaveAsync(_items);
            StatusText.Text = $"Reordered '{draggedVm.Name}'.";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save reordered items");
        }

        e.Handled = true;
    }

    private bool IsDescendant(TriggerItem item, Guid potentialAncestorId)
    {
        var currentParent = item.ParentId;
        while (currentParent.HasValue)
        {
            if (currentParent.Value == potentialAncestorId) return true;
            var parentItem = _items.FirstOrDefault(x => x.Id == currentParent.Value);
            currentParent = parentItem?.ParentId;
        }
        return false;
    }

    private TriggerTreeItemViewModel? FindTreeItemViewModelUnderMouse(DependencyObject? source)
    {
        while (source != null && source is not TreeViewItem)
        {
            source = VisualTreeHelper.GetParent(source);
        }

        if (source is TreeViewItem tvi && tvi.DataContext is TriggerTreeItemViewModel vm)
        {
            return vm;
        }

        return null;
    }

    // Window Target Crosshair Tool (Spy++ style)
    private void CrosshairTarget_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        StartWindowCapture("Shell");
        e.Handled = true;
    }

    private void ProcessCrosshair_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var tag = (sender as FrameworkElement)?.Tag?.ToString() ?? "Allowed";
        string mode = tag switch
        {
            "Excluded" => "ExcludedProcess",
            "AllowedUrl" => "AllowedUrl",
            _ => "AllowedProcess"
        };
        StartWindowCapture(mode);
        e.Handled = true;
    }

    private void BrowseRunningProcess_Click(object sender, RoutedEventArgs e)
    {
        string? proc = ProcessPickerDialog.PickProcess(this);
        if (!string.IsNullOrWhiteSpace(proc))
        {
            var tag = (sender as FrameworkElement)?.Tag?.ToString() ?? "Allowed";
            if (tag == "Excluded")
            {
                ExcludedProcessesTagInput.AddTag(proc);
            }
            else
            {
                AllowedProcessesTagInput.AddTag(proc);
            }
            CommitCurrentFormChanges();
            StatusText.Text = $"Added '{proc}' to {tag} processes.";
        }
    }

    private void BrowseFileProcess_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select Application Executable",
            Filter = "Applications (*.exe)|*.exe|All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            string procName = Path.GetFileName(dlg.FileName);
            var tag = (sender as FrameworkElement)?.Tag?.ToString() ?? "Allowed";
            if (tag == "Excluded")
            {
                ExcludedProcessesTagInput.AddTag(procName);
            }
            else
            {
                AllowedProcessesTagInput.AddTag(procName);
            }
            CommitCurrentFormChanges();
            StatusText.Text = $"Added '{procName}' to {tag} processes.";
        }
    }

    private void StartWindowCapture(string mode)
    {
        _windowTargetMode = mode;
        _isCapturingWindow = true;
        CaptureMouse();
        Mouse.OverrideCursor = Cursors.Cross;
        StatusText.Text = "🎯 Crosshair active: Drag and release over any application window to capture it...";
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (_isCapturingWindow)
        {
            _isCapturingWindow = false;
            ReleaseMouseCapture();
            Mouse.OverrideCursor = null;
            CompleteWindowCapture();
            e.Handled = true;
        }
    }

    private void CompleteWindowCapture()
    {
        try
        {
            if (!NativeMethods.GetCursorPos(out var pt))
            {
                StatusText.Text = "Failed to retrieve cursor position.";
                return;
            }

            IntPtr hwnd = NativeMethods.WindowFromPoint(pt);
            if (hwnd == IntPtr.Zero)
            {
                StatusText.Text = "No window found under crosshair.";
                return;
            }

            IntPtr rootHwnd = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            if (rootHwnd == IntPtr.Zero) rootHwnd = hwnd;

            NativeMethods.GetWindowThreadProcessId(rootHwnd, out uint pid);
            if (pid == 0)
            {
                StatusText.Text = "Could not identify window process ID.";
                return;
            }

            if (pid == (uint)Environment.ProcessId)
            {
                StatusText.Text = "Targeted TriggerPoint window. Please drag crosshair to an external application window.";
                return;
            }

            string? exePath = GetProcessPath(pid);
            if (string.IsNullOrWhiteSpace(exePath))
            {
                StatusText.Text = $"Could not query executable path for process ID {pid}.";
                return;
            }

            string exeName = Path.GetFileName(exePath);

            if (_windowTargetMode == "Shell")
            {
                if (_selectedItem == null || _selectedItem.ActionType == ActionType.Folder)
                {
                    AddActionBtn_Click(this, new RoutedEventArgs());
                }

                ActionTypeCombo.SelectedIndex = (int)ActionType.Shell;
                ShellCommandBox.Text = exePath;
                ShellWorkDirBox.Text = Path.GetDirectoryName(exePath) ?? string.Empty;

                if (string.IsNullOrWhiteSpace(ItemNameBox.Text) || ItemNameBox.Text.StartsWith("New Action", StringComparison.OrdinalIgnoreCase))
                {
                    ItemNameBox.Text = Path.GetFileNameWithoutExtension(exePath);
                }

                CommitCurrentFormChanges();
                StatusText.Text = $"🎯 Target captured: {exeName} ({exePath})";
            }
            else if (_windowTargetMode == "AllowedProcess")
            {
                AllowedProcessesTagInput.AddTag(exeName);
                if (ContextFilter.IsKnownBrowser(exeName) && _contextFilterService != null)
                {
                    var activeUrl = _contextFilterService.GetActiveBrowserUrl(rootHwnd, exeName);
                    if (!string.IsNullOrWhiteSpace(activeUrl))
                    {
                        bool captureTab = ModernMessageDialog.ShowConfirm(
                            this,
                            "Browser Tab Detected",
                            $"Targeted browser '{exeName}'.\n\nActive Tab URL / Page:\n{activeUrl}\n\nWould you like to scope this action to this specific tab/URL as well?",
                            "Include Tab URL",
                            "Process Only");

                        if (captureTab)
                        {
                            AllowedUrlsTagInput.AddTag(activeUrl);
                            CommitCurrentFormChanges();
                            StatusText.Text = $"🎯 Added '{exeName}' and tab URL '{activeUrl}'.";
                            return;
                        }
                    }
                }
                CommitCurrentFormChanges();
                StatusText.Text = $"🎯 Added '{exeName}' to Allowed Processes.";
            }
            else if (_windowTargetMode == "ExcludedProcess")
            {
                ExcludedProcessesTagInput.AddTag(exeName);
                CommitCurrentFormChanges();
                StatusText.Text = $"🎯 Added '{exeName}' to Excluded Processes.";
            }
            else if (_windowTargetMode == "AllowedUrl")
            {
                string? url = null;
                if (_contextFilterService != null)
                {
                    url = _contextFilterService.GetActiveBrowserUrl(rootHwnd, exeName);
                }

                if (!string.IsNullOrWhiteSpace(url))
                {
                    AllowedUrlsTagInput.AddTag(url);
                    CommitCurrentFormChanges();
                    StatusText.Text = $"🎯 Captured browser tab URL: {url}";
                }
                else
                {
                    int len = NativeMethods.GetWindowTextLength(rootHwnd);
                    if (len > 0)
                    {
                        var sbTitle = new StringBuilder(len + 1);
                        if (NativeMethods.GetWindowText(rootHwnd, sbTitle, sbTitle.Capacity) > 0)
                        {
                            string t = sbTitle.ToString().Trim();
                            AllowedUrlsTagInput.AddTag("*" + t + "*");
                            CommitCurrentFormChanges();
                            StatusText.Text = $"🎯 Captured window title pattern: *{t}*";
                        }
                    }
                    else
                    {
                        StatusText.Text = "Could not detect active URL or window title from target.";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error completing window capture");
            StatusText.Text = $"Error capturing window: {ex.Message}";
        }
    }

    private string? GetProcessPath(uint pid)
    {
        IntPtr hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProcess != IntPtr.Zero)
        {
            try
            {
                var sb = new StringBuilder(1024);
                uint size = (uint)sb.Capacity;
                if (NativeMethods.QueryFullProcessImageName(hProcess, 0, sb, ref size))
                {
                    return sb.ToString();
                }
            }
            finally
            {
                NativeMethods.CloseHandle(hProcess);
            }
        }

        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
            return proc.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    // Process Context Rules Drag & Drop
    private void ProcessFilterGroup_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void ProcessFilterGroup_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                foreach (var file in files)
                {
                    string target = file;
                    if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        var res = ShellLinkScanner.ResolveShortcut(file);
                        if (res.HasValue && !string.IsNullOrWhiteSpace(res.Value.TargetPath))
                        {
                            target = res.Value.TargetPath;
                        }
                    }

                    string exeName = Path.GetFileName(target);
                    if (!string.IsNullOrWhiteSpace(exeName))
                    {
                        AllowedProcessesTagInput.AddTag(exeName);
                    }
                }
                CommitCurrentFormChanges();
                StatusText.Text = "Added process rule(s) from dropped file(s).";
                e.Handled = true;
            }
        }
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (!IsExiting)
        {
            // Minimize/Hide to tray instead of closing
            e.Cancel = true;
            Hide();
        }
    }
}
