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

public enum TreeDropPosition
{
    None,
    Above,
    Inside,
    Below
}

public class TriggerTreeItemViewModel : INotifyPropertyChanged
{
    public TriggerItem Item { get; }
    public ObservableCollection<TriggerTreeItemViewModel> Children { get; } = [];

    public bool IsRecycleBinRoot { get; set; }
    public bool IsRecycledItem { get; set; }
    public RecycleBinItem? RecycledInfo { get; set; }

    // Inline Rename support
    private bool _isEditingName;
    public bool IsEditingName
    {
        get => _isEditingName;
        set
        {
            if (_isEditingName != value)
            {
                _isEditingName = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEditingName)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameDisplayVisibility)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameEditVisibility)));
            }
        }
    }

    private string _editingNameText = string.Empty;
    public string EditingNameText
    {
        get => _editingNameText;
        set
        {
            if (_editingNameText != value)
            {
                _editingNameText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditingNameText)));
            }
        }
    }

    public Visibility NameDisplayVisibility => IsEditingName ? Visibility.Collapsed : Visibility.Visible;
    public Visibility NameEditVisibility => IsEditingName ? Visibility.Visible : Visibility.Collapsed;

    public void StartEdit()
    {
        if (IsRecycleBinRoot || IsRecycledItem) return;
        EditingNameText = Item.Name;
        IsEditingName = true;
    }

    public void CancelEdit()
    {
        EditingNameText = Item.Name;
        IsEditingName = false;
    }

    // Drag-and-drop Visual Feedback
    private TreeDropPosition _dropPosition = TreeDropPosition.None;
    public TreeDropPosition DropPosition
    {
        get => _dropPosition;
        set
        {
            if (_dropPosition != value)
            {
                _dropPosition = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DropPosition)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DropAboveVisibility)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DropInsideVisibility)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DropBelowVisibility)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DropInsideBorderBrush)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DropInsideBackgroundBrush)));
            }
        }
    }

    public Visibility DropAboveVisibility => DropPosition == TreeDropPosition.Above ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DropInsideVisibility => DropPosition == TreeDropPosition.Inside ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DropBelowVisibility => DropPosition == TreeDropPosition.Below ? Visibility.Visible : Visibility.Collapsed;

    public Brush DropInsideBorderBrush => DropPosition == TreeDropPosition.Inside
        ? (Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue)
        : Brushes.Transparent;

    public Brush DropInsideBackgroundBrush => DropPosition == TreeDropPosition.Inside
        ? (Application.Current.TryFindResource("AccentSubtleBrush") as Brush ?? new SolidColorBrush(Color.FromArgb(0x25, 0x00, 0x7A, 0xCC)))
        : Brushes.Transparent;

    public string Name => Item.Name;
    public string IconSymbol => IsRecycleBinRoot ? "🗑" : Item.ActionType switch
    {
        ActionType.Folder => "📁",
        ActionType.Snippet => "📝",
        ActionType.Shell => "⚡",
        _ => "▶"
    };

    public string HotkeyDisplay => (IsRecycleBinRoot || IsRecycledItem) ? string.Empty : (Item.Hotkey?.DisplayText ?? string.Empty);
    public Visibility HasHotkey => !string.IsNullOrWhiteSpace(HotkeyDisplay) ? Visibility.Visible : Visibility.Collapsed;

    // Only show conflict if item actually has a non-empty hotkey AND a conflict status
    public Visibility HasConflictVisibility => 
        (!IsRecycledItem && !IsRecycleBinRoot && Item.Hotkey != null && !Item.Hotkey.IsEmpty && Item.ConflictStatus.HasConflict) 
        ? Visibility.Visible 
        : Visibility.Collapsed;

    public bool IsBrokenTarget { get; set; }
    public string? BrokenTargetMessage { get; set; }

    public Visibility BrokenTargetVisibility => (!IsRecycledItem && !IsRecycleBinRoot && IsBrokenTarget)
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string BrokenTargetTooltip => BrokenTargetMessage ?? "Target file or path could not be found";

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

    public Brush IconBrush
    {
        get
        {
            if (IsRecycleBinRoot)
            {
                return Application.Current.TryFindResource("WarningBrush") as Brush ?? Brushes.Orange;
            }
            if (IsRecycledItem)
            {
                return Application.Current.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;
            }
            string key = Item.ActionType switch
            {
                ActionType.Folder => "FolderBrush",
                ActionType.Shell => "ShellBrush",
                ActionType.Snippet => "SnippetBrush",
                _ => "TextSecondaryBrush"
            };
            return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
        }
    }

    public string TypeBadgeText => IsRecycleBinRoot ? "BIN" : IsRecycledItem ? "DELETED" : Item.ActionType switch
    {
        ActionType.Folder => "FOLDER",
        ActionType.Shell => "SHELL",
        ActionType.Snippet => "SNIPPET",
        _ => "ACTION"
    };

    public Brush TypeBadgeBrush
    {
        get
        {
            if (IsRecycleBinRoot || IsRecycledItem)
            {
                return Application.Current.TryFindResource("WarningBrush") as Brush ?? Brushes.Orange;
            }
            string key = Item.ActionType switch
            {
                ActionType.Folder => "FolderBrush",
                ActionType.Shell => "ShellBrush",
                ActionType.Snippet => "SnippetBrush",
                _ => "TextSecondaryBrush"
            };
            return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
        }
    }

    public Brush TypeBadgeSubtleBrush
    {
        get
        {
            if (IsRecycleBinRoot || IsRecycledItem)
            {
                return Application.Current.TryFindResource("WarningSubtleBrush") as Brush ?? Brushes.Transparent;
            }
            string key = Item.ActionType switch
            {
                ActionType.Folder => "FolderSubtleBrush",
                ActionType.Shell => "ShellSubtleBrush",
                ActionType.Snippet => "SnippetSubtleBrush",
                _ => "BgTertiaryBrush"
            };
            return Application.Current.TryFindResource(key) as Brush ?? Brushes.Transparent;
        }
    }

    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => IsRecycleBinRoot ? _isExpanded : Item.IsExpanded;
        set
        {
            if (IsRecycleBinRoot)
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
                }
            }
            else
            {
                if (Item.IsExpanded != value)
                {
                    Item.IsExpanded = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
                }
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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconBrush)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TypeBadgeText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TypeBadgeBrush)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TypeBadgeSubtleBrush)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HotkeyDisplay)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasHotkey)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasConflictVisibility)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ConflictTooltip)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BrokenTargetVisibility)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BrokenTargetTooltip)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameDisplayVisibility)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameEditVisibility)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DropAboveVisibility)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DropInsideVisibility)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DropBelowVisibility)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DropInsideBorderBrush)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DropInsideBackgroundBrush)));
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
    private List<RecycleBinItem> _recycledItems = [];
    private ObservableCollection<TriggerTreeItemViewModel> _treeRoots = [];
    private TriggerItem? _selectedItem;
    private TriggerItem? _originalItemSnapshot;
    private bool _isUpdatingForm;
    private bool _isItemDirty;
    private bool _isRevertingTreeSelection;
    private readonly DispatcherTimer _snippetPreviewDebounceTimer;

    // TreeView drag & drop re-sorting
    private Point? _treeDragStartPoint;
    private TriggerTreeItemViewModel? _draggedTreeVm;

    // Executable browsing path memory
    private static string? _lastBrowsedExecutableDirectory;

    private static string GetInitialExecutableDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_lastBrowsedExecutableDirectory) && Directory.Exists(_lastBrowsedExecutableDirectory))
        {
            return _lastBrowsedExecutableDirectory;
        }

        string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (Directory.Exists(progFiles)) return progFiles;

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string userPrograms = Path.Combine(localAppData, "Programs");
        if (Directory.Exists(userPrograms)) return userPrograms;

        return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    }

    // Window Target Crosshair Tool
    private AppSettings? _appSettings;
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
        _shortcutListener.SnoozeChanged += (s, isSnoozed) => Dispatcher.Invoke(UpdateSnoozeButtonUi);

        AllowedProcessesTagInput.TagsChanged += (s, e) =>
        {
            OnFormEdited();
            if (!_isUpdatingForm)
            {
                CheckAutoExpandBrowserUrlSection();
            }
        };
        ExcludedProcessesTagInput.TagsChanged += (s, e) => OnFormEdited();
        AllowedUrlsTagInput.TagsChanged += (s, e) =>
        {
            OnFormEdited();
            if (!_isUpdatingForm)
            {
                UpdateUrlRulesBadge();
            }
        };
        ExcludedUrlsTagInput.TagsChanged += (s, e) =>
        {
            OnFormEdited();
            if (!_isUpdatingForm)
            {
                UpdateUrlRulesBadge();
            }
        };

        ItemNameBox.TextChanged += (s, e) =>
        {
            if (!_isUpdatingForm)
            {
                EditorHeaderTitle.Text = string.IsNullOrWhiteSpace(ItemNameBox.Text) ? "Untitled Item" : ItemNameBox.Text;
                OnFormEdited();
            }
        };
        ItemDescBox.TextChanged += (s, e) => OnFormEdited();
        ShellCommandBox.TextChanged += (s, e) =>
        {
            OnFormEdited();
            UpdateCommandValidationStatus(ShellCommandBox.Text);
        };
        ShellArgsBox.TextChanged += (s, e) => OnFormEdited();
        ShellWorkDirBox.TextChanged += (s, e) => OnFormEdited();
        ShellRunAsAdminCheck.Click += (s, e) => OnFormEdited();
        
        _snippetPreviewDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _snippetPreviewDebounceTimer.Tick += async (s, e) =>
        {
            _snippetPreviewDebounceTimer.Stop();
            await UpdateSnippetLivePreviewAsync();
        };

        SnippetTemplateBox.TextChanged += (s, e) =>
        {
            OnFormEdited();
            UpdateContextualTokenAssistant();
            QueueSnippetLivePreviewUpdate();
        };
        AcceleratorBox.TextChanged += (s, e) => OnFormEdited();
        PresentationModeCombo.SelectionChanged += (s, e) => OnFormEdited();

        StateChanged += (s, e) =>
        {
            if (MaximizeBtn != null)
            {
                MaximizeBtn.Content = WindowState == WindowState.Maximized ? "❐" : "▢";
            }
        };

        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        if (AppVersionText != null && version != null)
        {
            AppVersionText.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
        }

        Loaded += async (s, e) => await LoadDataAsync();
    }

    private void SetDirty(bool isDirty = true)
    {
        _isItemDirty = isDirty;
        if (SaveBtn != null)
        {
            SaveBtn.IsEnabled = isDirty;
        }
        if (RevertItemBtn != null)
        {
            RevertItemBtn.IsEnabled = isDirty;
        }
    }

    private void OnFormEdited()
    {
        if (_isUpdatingForm || _selectedItem == null) return;
        CommitCurrentFormChanges();
        SetDirty(true);
    }

    private static void RestoreItemFromSnapshot(TriggerItem target, TriggerItem snapshot)
    {
        target.Name = snapshot.Name;
        target.Description = snapshot.Description;
        target.IconPath = snapshot.IconPath;
        target.AcceleratorKey = snapshot.AcceleratorKey;
        target.Hotkey = snapshot.Hotkey != null 
            ? new ShortcutBinding(snapshot.Hotkey.Modifiers, snapshot.Hotkey.VirtualKey, snapshot.Hotkey.KeyName) 
            : null;
        target.PresentationMode = snapshot.PresentationMode;
        target.ActionType = snapshot.ActionType;
        target.Payload.Command = snapshot.Payload.Command;
        target.Payload.Arguments = snapshot.Payload.Arguments;
        target.Payload.WorkingDirectory = snapshot.Payload.WorkingDirectory;
        target.Payload.RunAsAdmin = snapshot.Payload.RunAsAdmin;
        target.Payload.SnippetTemplate = snapshot.Payload.SnippetTemplate;
        target.ContextFilter.AllowedProcesses = [.. snapshot.ContextFilter.AllowedProcesses];
        target.ContextFilter.ExcludedProcesses = [.. snapshot.ContextFilter.ExcludedProcesses];
        target.ContextFilter.AllowedUrls = [.. snapshot.ContextFilter.AllowedUrls];
        target.ContextFilter.ExcludedUrls = [.. snapshot.ContextFilter.ExcludedUrls];
        target.ConflictStatus = snapshot.ConflictStatus;
    }

    private bool PromptSaveIfDirty()
    {
        if (!_isItemDirty || _selectedItem == null) return true;

        var choice = ModernMessageDialog.ShowUnsavedChangesDialog(this, _selectedItem.Name);
        if (choice == SavePromptChoice.Cancel)
        {
            return false;
        }
        else if (choice == SavePromptChoice.Discard)
        {
            if (_originalItemSnapshot != null)
            {
                RestoreItemFromSnapshot(_selectedItem, _originalItemSnapshot);
                FindViewModel(_selectedItem)?.NotifyUpdated();
            }
            SetDirty(false);
            return true;
        }
        else if (choice == SavePromptChoice.Save)
        {
            CommitCurrentFormChanges();
            _ = SaveConfigurationCoreAsync();
            SetDirty(false);
            return true;
        }
        return true;
    }

    private async Task LoadDataAsync()
    {
        _items = (await _repository.LoadAsync()).ToList();
        try
        {
            _recycledItems = (await _repository.LoadRecycleBinAsync()).ToList();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load recycle bin items on startup.");
            _recycledItems = [];
        }

        try
        {
            _appSettings = await _repository.LoadSettingsAsync();
        }
        catch
        {
            _appSettings = new AppSettings();
        }
        RegisterShortcuts();
        RebuildTree();
        UpdateSnoozeButtonUi();
    }

    private void RegisterShortcuts()
    {
        var allItems = new List<TriggerItem>(_items);
        if (_appSettings != null)
        {
            allItems.AddRange(App.CreateVirtualApplicationItems(_appSettings));
        }
        _shortcutListener.RegisterAll(allItems);
    }

    private async Task RebuildTreeAsync()
    {
        try
        {
            _recycledItems = (await _repository.LoadRecycleBinAsync()).ToList();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to refresh recycle bin items.");
            _recycledItems = [];
        }
        RebuildTree();
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
            if (item.ActionType == ActionType.Shell)
            {
                var validation = ShortcutValidator.Validate(item.Payload.Command);
                if (validation.Status != ShortcutValidationStatus.Valid)
                {
                    vm.IsBrokenTarget = true;
                    vm.BrokenTargetMessage = validation.Message;
                }
            }

            if (item.ParentId.HasValue && folderMap.TryGetValue(item.ParentId.Value, out var parentVm))
            {
                parentVm.Children.Add(vm);
            }
            else
            {
                _treeRoots.Add(vm);
            }
        }

        // Third pass: Recycle Bin (pinned at bottom of tree if non-empty)
        if (_recycledItems.Count > 0)
        {
            var recycleBinRootItem = new TriggerItem
            {
                Id = Guid.Empty,
                Name = $"Recycle Bin ({_recycledItems.Count})",
                ActionType = ActionType.Folder,
                Description = "Contains deleted actions and folders"
            };
            var recycleBinVm = new TriggerTreeItemViewModel(recycleBinRootItem)
            {
                IsRecycleBinRoot = true,
                IsExpanded = true
            };
            foreach (var rbi in _recycledItems)
            {
                var rVm = new TriggerTreeItemViewModel(rbi.Item)
                {
                    IsRecycledItem = true,
                    RecycledInfo = rbi
                };
                recycleBinVm.Children.Add(rVm);
            }
            _treeRoots.Add(recycleBinVm);
        }

        ItemsTreeView.ItemsSource = _treeRoots;
        UpdateBrokenFilterChipCount();
        UpdateExpandAllButtonGlyph();

        // Restore selection or select first item if available
        if (_treeRoots.Count > 0)
        {
            if (_selectedItem != null && (_selectedItem.Id == Guid.Empty || _items.Any(x => x.Id == _selectedItem.Id) || _recycledItems.Any(x => x.Item.Id == _selectedItem.Id)))
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
        _originalItemSnapshot = item.Clone();
        var vm = FindViewModel(item);
        PopulateForm(item, vm);
        SetViewModelSelected(_treeRoots, item.Id);
        SetDirty(false);
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
        if (_isRevertingTreeSelection) return;

        var currentlyEditing = FindCurrentlyEditingViewModel();
        if (currentlyEditing != null && (e.NewValue is not TriggerTreeItemViewModel newVm || newVm.Item.Id != currentlyEditing.Item.Id))
        {
            CommitActiveInlineRenameSync(currentlyEditing);
        }

        if (e.NewValue is TriggerTreeItemViewModel vm)
        {
            if (_isItemDirty && _selectedItem != null && _selectedItem.Id != vm.Item.Id)
            {
                if (!PromptSaveIfDirty())
                {
                    _isRevertingTreeSelection = true;
                    try
                    {
                        SetViewModelSelected(_treeRoots, _selectedItem.Id);
                    }
                    finally
                    {
                        _isRevertingTreeSelection = false;
                    }
                    return;
                }
            }

            _selectedItem = vm.Item;
            _originalItemSnapshot = vm.Item.Clone();
            PopulateForm(vm.Item, vm);
            SetDirty(false);
        }
    }

    private void PopulateForm(TriggerItem item, TriggerTreeItemViewModel? vm = null)
    {
        vm ??= FindViewModel(item);
        _isUpdatingForm = true;
        try
        {
            if (vm?.IsRecycleBinRoot == true)
            {
                if (EditorScrollViewer != null) EditorScrollViewer.Visibility = Visibility.Collapsed;
                if (RecycleBinOverviewPanel != null) RecycleBinOverviewPanel.Visibility = Visibility.Visible;
                if (RecycledItemBanner != null) RecycledItemBanner.Visibility = Visibility.Collapsed;
                EditorHeaderTitle.Text = "Recycle Bin";
                if (EditorTypeBadge != null) EditorTypeBadge.Visibility = Visibility.Collapsed;
                if (DeleteItemBtn != null) DeleteItemBtn.Visibility = Visibility.Collapsed;
                if (TestActionBtn != null) TestActionBtn.Visibility = Visibility.Collapsed;
                if (SaveBtn != null) SaveBtn.IsEnabled = false;
                if (RevertItemBtn != null) RevertItemBtn.Visibility = Visibility.Collapsed;

                if (RecycleBinCountDetailText != null)
                {
                    RecycleBinCountDetailText.Text = $"{_recycledItems.Count} item{(_recycledItems.Count == 1 ? "" : "s")} in Recycle Bin";
                }
                if (RecycleBinRetentionSummaryText != null)
                {
                    RecycleBinRetentionSummaryText.Text = _appSettings?.RecycleBinRetentionDays == 0
                        ? "Retention policy: Items are kept indefinitely (never auto-deleted)."
                        : $"Retention policy: Items older than {_appSettings?.RecycleBinRetentionDays ?? 30} days are automatically deleted on startup.";
                }
                if (EmptyRecycleBinFromSettingsBtn != null)
                {
                    EmptyRecycleBinFromSettingsBtn.IsEnabled = _recycledItems.Count > 0;
                }
                return;
            }

            if (EditorScrollViewer != null) EditorScrollViewer.Visibility = Visibility.Visible;
            if (RecycleBinOverviewPanel != null) RecycleBinOverviewPanel.Visibility = Visibility.Collapsed;

            if (vm?.IsRecycledItem == true)
            {
                if (RecycledItemBanner != null) RecycledItemBanner.Visibility = Visibility.Visible;
                if (EditorPanel != null)
                {
                    EditorPanel.IsEnabled = false;
                    EditorPanel.Opacity = 0.6;
                }
                if (DeleteItemBtn != null) DeleteItemBtn.Visibility = Visibility.Collapsed;
                if (TestActionBtn != null) TestActionBtn.Visibility = Visibility.Collapsed;
                if (SaveBtn != null) SaveBtn.IsEnabled = false;
                if (RevertItemBtn != null) RevertItemBtn.Visibility = Visibility.Collapsed;
                if (RestoreRecycledItemBtn != null) RestoreRecycledItemBtn.IsEnabled = true;
                if (PermanentlyDeleteRecycledItemBtn != null) PermanentlyDeleteRecycledItemBtn.IsEnabled = true;

                string origLoc = string.IsNullOrWhiteSpace(vm.RecycledInfo?.OriginalPath) ? "Root" : vm.RecycledInfo.OriginalPath;
                if (RecycledItemInfoText != null)
                {
                    RecycledItemInfoText.Text = $"Deleted on {vm.RecycledInfo?.DeletedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm} • Original folder: {origLoc}";
                }
            }
            else
            {
                if (RecycledItemBanner != null) RecycledItemBanner.Visibility = Visibility.Collapsed;
                if (EditorPanel != null)
                {
                    EditorPanel.IsEnabled = true;
                    EditorPanel.Opacity = 1.0;
                }
                if (DeleteItemBtn != null) DeleteItemBtn.Visibility = Visibility.Visible;
                if (RevertItemBtn != null)
                {
                    RevertItemBtn.Visibility = Visibility.Visible;
                    RevertItemBtn.IsEnabled = _isItemDirty;
                }
                if (TestActionBtn != null)
                {
                    TestActionBtn.Visibility = item.ActionType == ActionType.Folder ? Visibility.Collapsed : Visibility.Visible;
                }
            }

            EditorHeaderTitle.Text = string.IsNullOrWhiteSpace(item.Name) ? "Untitled Item" : item.Name;
            if (EditorPanel != null) EditorPanel.Visibility = Visibility.Visible;

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
            UpdateCommandValidationStatus(item.Payload.Command);

            // Snippet payload
            SnippetTemplateBox.Text = item.Payload.SnippetTemplate;

            // Context filter
            AllowedProcessesTagInput.SetTags(item.ContextFilter.AllowedProcesses);
            ExcludedProcessesTagInput.SetTags(item.ContextFilter.ExcludedProcesses);
            AllowedUrlsTagInput.SetTags(item.ContextFilter.AllowedUrls);
            ExcludedUrlsTagInput.SetTags(item.ContextFilter.ExcludedUrls);

            bool hasUrlRules = item.ContextFilter.AllowedUrls.Count > 0 || item.ContextFilter.ExcludedUrls.Count > 0;
            bool hasBrowserInAllowed = item.ContextFilter.AllowedProcesses.Any(ContextFilter.IsKnownBrowser);
            SetBrowserUrlSectionExpanded(hasUrlRules || hasBrowserInAllowed);

            UpdateFormVisibility(item.ActionType);
            UpdateEditorTypeBadge(item.ActionType);
            UpdateConflictBanner(item);
        }
        finally
        {
            _isUpdatingForm = false;
            UpdateContextualTokenAssistant();
            QueueSnippetLivePreviewUpdate();
        }
    }

    private void UpdateEditorTypeBadge(ActionType actionType)
    {
        if (EditorTypeBadge == null || EditorTypeBadgeText == null) return;
        EditorTypeBadge.Visibility = Visibility.Visible;
        EditorTypeBadgeText.Text = actionType switch
        {
            ActionType.Folder => "📁 FOLDER",
            ActionType.Shell => "⚡ SHELL",
            ActionType.Snippet => "📝 SNIPPET",
            _ => actionType.ToString().ToUpperInvariant()
        };
        string textKey = actionType switch
        {
            ActionType.Folder => "FolderBrush",
            ActionType.Shell => "ShellBrush",
            ActionType.Snippet => "SnippetBrush",
            _ => "TextPrimaryBrush"
        };
        string bgKey = actionType switch
        {
            ActionType.Folder => "FolderSubtleBrush",
            ActionType.Shell => "ShellSubtleBrush",
            ActionType.Snippet => "SnippetSubtleBrush",
            _ => "BgTertiaryBrush"
        };
        EditorTypeBadgeText.Foreground = Application.Current.TryFindResource(textKey) as Brush ?? Brushes.Gray;
        EditorTypeBadge.Background = Application.Current.TryFindResource(bgKey) as Brush ?? Brushes.Transparent;
    }

    private void UpdateFormVisibility(ActionType actionType)
    {
        if (TestActionBtn != null)
        {
            TestActionBtn.Visibility = actionType == ActionType.Folder ? Visibility.Collapsed : Visibility.Visible;
        }

        if (PresentationDirectItem != null)
        {
            PresentationDirectItem.Content = actionType == ActionType.Folder
                ? "None (Organizational Only / No Popup)"
                : "Direct (Run immediately, no UI)";
        }

        if (AcceleratorGroup != null)
        {
            // Allow quick-key on actions, subfolders (which can be drilled into), or menu containers
            bool allowQuickKey = actionType != ActionType.Folder || (_selectedItem != null && (_selectedItem.ParentId.HasValue || _selectedItem.PresentationMode == PresentationMode.CursorMenu));
            AcceleratorGroup.Visibility = allowQuickKey
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

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
        var targetVm = FindViewModel(_selectedItem);
        if (targetVm != null)
        {
            if (_selectedItem.ActionType == ActionType.Shell)
            {
                var validation = ShortcutValidator.Validate(_selectedItem.Payload.Command);
                targetVm.IsBrokenTarget = validation.Status != ShortcutValidationStatus.Valid;
                targetVm.BrokenTargetMessage = validation.Message;
            }
            else
            {
                targetVm.IsBrokenTarget = false;
                targetVm.BrokenTargetMessage = null;
            }
            targetVm.NotifyUpdated();
        }
        UpdateBrokenFilterChipCount();
    }

    private void ActionTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUpdatingForm && ActionTypeCombo.SelectedIndex >= 0)
        {
            var newType = (ActionType)ActionTypeCombo.SelectedIndex;
            UpdateFormVisibility(newType);
            if (_selectedItem != null)
            {
                _selectedItem.ActionType = newType;
                UpdateEditorTypeBadge(newType);
                FindViewModel(_selectedItem)?.NotifyUpdated();
            }
            UpdateCommandValidationStatus(ShellCommandBox.Text);
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
        OnFormEdited();
    }

    private void InsertTokenChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string token })
        {
            var caret = SnippetTemplateBox.CaretIndex;
            SnippetTemplateBox.Text = SnippetTemplateBox.Text.Insert(caret, token);
            SnippetTemplateBox.CaretIndex = caret + token.Length;
            SnippetTemplateBox.Focus();
            UpdateContextualTokenAssistant();
            QueueSnippetLivePreviewUpdate();
        }
    }

    private void InsertTokenPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string token })
        {
            var caret = SnippetTemplateBox.CaretIndex;
            SnippetTemplateBox.Text = SnippetTemplateBox.Text.Insert(caret, token);
            SnippetTemplateBox.CaretIndex = caret + token.Length;
            SnippetTemplateBox.Focus();
            UpdateContextualTokenAssistant();
            QueueSnippetLivePreviewUpdate();
        }
    }

    private void SnippetTemplateBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        UpdateContextualTokenAssistant();
    }

    private void RefreshSnippetPreviewBtn_Click(object sender, RoutedEventArgs e)
    {
        _ = UpdateSnippetLivePreviewAsync();
    }

    private void QueueSnippetLivePreviewUpdate()
    {
        _snippetPreviewDebounceTimer?.Stop();
        _snippetPreviewDebounceTimer?.Start();
    }

    private async Task UpdateSnippetLivePreviewAsync()
    {
        if (SnippetLivePreviewText == null || SnippetPreviewStatsText == null) return;

        var template = SnippetTemplateBox.Text;
        if (string.IsNullOrWhiteSpace(template))
        {
            SnippetLivePreviewText.Text = "Type a snippet template above to see a real-time expansion preview...";
            SnippetLivePreviewText.Foreground = Application.Current.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;
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
            catch { }

            if (string.IsNullOrEmpty(previewClip)) previewClip = "[Clipboard text]";

            var evaluated = await PlaceholderParser.EvaluateAsync(
                template,
                clipboardProvider: () => Task.FromResult(previewClip),
                referenceTime: DateTime.Now,
                activeWindowTitle: "Example Window Title",
                activeProcessName: "notepad.exe").ConfigureAwait(true);

            var (clean, _) = PlaceholderParser.ProcessCursorPosition(evaluated);

            SnippetLivePreviewText.Text = clean;
            SnippetLivePreviewText.Foreground = Application.Current.TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White;

            var tokenMatches = System.Text.RegularExpressions.Regex.Matches(template, @"\{[^{}]+\}");
            SnippetPreviewStatsText.Text = $"{clean.Length} chars • {tokenMatches.Count} tokens";
        }
        catch (Exception ex)
        {
            SnippetLivePreviewText.Text = $"Preview error: {ex.Message}";
            SnippetLivePreviewText.Foreground = Application.Current.TryFindResource("ErrorBrush") as Brush ?? Brushes.Red;
        }
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

            case "random":
                AssistantBadgeBorder.Visibility = Visibility.Visible;
                AssistantTokenBadge.Text = "{random:min,max}";
                AssistantDescriptionText.Text = "Generates a random integer or picks from comma-separated options.";
                AddAssistantPill("1000,9999 (4-Digit PIN)", "1000,9999", start, end, prefix);
                AddAssistantPill("1,100", "1,100", start, end, prefix);
                AddAssistantPill("yes,no,maybe", "yes,no,maybe", start, end, prefix);
                break;

            case "text":
                AssistantBadgeBorder.Visibility = Visibility.Visible;
                AssistantTokenBadge.Text = "{text:Label|DefaultValue}";
                AssistantDescriptionText.Text = "Single-line interactive input prompt. Add '|Default' to pre-populate.";
                AddAssistantPill("Label|Default", "Label|Default", start, end, prefix);
                AddAssistantPill("Client Name|Acme Corp", "Client Name|Acme Corp", start, end, prefix);
                break;

            case "multiline":
                AssistantBadgeBorder.Visibility = Visibility.Visible;
                AssistantTokenBadge.Text = "{multiline:Label|DefaultText}";
                AssistantDescriptionText.Text = "Multi-line text prompt dialog. Add '|Default' to pre-populate boilerplate text.";
                AddAssistantPill("Notes|Template notes here", "Notes|Template notes here", start, end, prefix);
                break;

            case "choice":
                AssistantBadgeBorder.Visibility = Visibility.Visible;
                AssistantTokenBadge.Text = "{choice:Label|Opt1=Val1,Opt2*=Val2}";
                AssistantDescriptionText.Text = "Dropdown selection prompt. Append '*' to an option (e.g. Staging*=stg) to mark as default.";
                AddAssistantPill("Env|Prod=prod,Staging*=stg,Dev=dev", "Env|Prod=prod,Staging*=stg,Dev=dev", start, end, prefix);
                AddAssistantPill("Status|Open,In Progress*,Resolved", "Status|Open,In Progress*,Resolved", start, end, prefix);
                break;

            case "number":
                AssistantBadgeBorder.Visibility = Visibility.Visible;
                AssistantTokenBadge.Text = "{number:Label|min,max|default}";
                AssistantDescriptionText.Text = "Numeric input prompt with optional min, max range and default number.";
                AddAssistantPill("Retries|1,10|3", "Retries|1,10|3", start, end, prefix);
                AddAssistantPill("Age|0,120|25", "Age|0,120|25", start, end, prefix);
                AddAssistantPill("Quantity|1,100", "Quantity|1,100", start, end, prefix);
                break;

            case "date_picker":
                AssistantBadgeBorder.Visibility = Visibility.Visible;
                AssistantTokenBadge.Text = "{date_picker:Label|Format}";
                AssistantDescriptionText.Text = "Calendar date picker prompt. Options: custom format (.NET specifiers) and relative offsets (+/- days, weeks).";
                AddAssistantPill("Due Date|MM/dd/yyyy (US)", "DueDate|MM/dd/yyyy", start, end, prefix);
                AddAssistantPill("Due Date|dd/MM/yyyy (EU)", "DueDate|dd/MM/yyyy", start, end, prefix);
                AddAssistantPill("Due Date|yyyy-MM-dd (ISO)", "DueDate|yyyy-MM-dd", start, end, prefix);
                AddAssistantPill("Due Date|yyyyMMdd (Compact)", "DueDate|yyyyMMdd", start, end, prefix);
                AddAssistantPill("Due Date|dddd, MMMM d, yyyy (Full)", "DueDate|dddd, MMMM d, yyyy", start, end, prefix);
                AddAssistantPill("Due Date|MM/dd/yyyy|+1d (Tomorrow)", "DueDate|MM/dd/yyyy|+1d", start, end, prefix);
                AddAssistantPill("Due Date|MM/dd/yyyy|+7d (Next Week)", "DueDate|MM/dd/yyyy|+7d", start, end, prefix);
                break;

            case "username":
            case "user":
                AssistantBadgeBorder.Visibility = Visibility.Visible;
                AssistantTokenBadge.Text = "{username}";
                AssistantDescriptionText.Text = $"Inserts the current Windows username ({Environment.UserName}).";
                break;

            case "machine":
            case "computer":
                AssistantBadgeBorder.Visibility = Visibility.Visible;
                AssistantTokenBadge.Text = "{machine}";
                AssistantDescriptionText.Text = $"Inserts the computer hostname ({Environment.MachineName}).";
                break;

            case "active_window":
                AssistantBadgeBorder.Visibility = Visibility.Visible;
                AssistantTokenBadge.Text = "{active_window}";
                AssistantDescriptionText.Text = "Inserts the window title of the target application window that triggered the shortcut.";
                break;

            case "active_process":
                AssistantBadgeBorder.Visibility = Visibility.Visible;
                AssistantTokenBadge.Text = "{active_process}";
                AssistantDescriptionText.Text = "Inserts the executable filename of the target application (e.g. Code.exe, chrome.exe).";
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
                QueueSnippetLivePreviewUpdate();
            }
        };

        AssistantOptionsPillsPanel.Children.Add(btn);
    }

    private (int Start, int End, string FullToken, string Prefix, string Arg)? GetTokenAtCaret()
    {
        var text = SnippetTemplateBox.Text;
        if (string.IsNullOrEmpty(text)) return null;
        var caret = Math.Clamp(SnippetTemplateBox.CaretIndex, 0, text.Length);

        // Search backwards for '{'
        int start = -1;
        for (int i = caret - 1; i >= 0; i--)
        {
            if (text[i] == '}') break; // already closed before caret
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

        // Search forwards for '}'
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

    private void BrowseFileBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select Application, Script or File",
            Filter = "Applications & Shortcuts (*.exe;*.lnk;*.bat;*.cmd;*.ps1)|*.exe;*.lnk;*.bat;*.cmd;*.ps1|All Files (*.*)|*.*",
            InitialDirectory = GetInitialExecutableDirectory()
        };
        if (dlg.ShowDialog(this) == true)
        {
            _lastBrowsedExecutableDirectory = Path.GetDirectoryName(dlg.FileName);

            string targetPath = dlg.FileName;
            string? args = null;
            string? workDir = null;

            if (dlg.FileName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var resolved = ShellLinkScanner.ResolveShortcut(dlg.FileName);
                if (resolved.HasValue && !string.IsNullOrWhiteSpace(resolved.Value.TargetPath))
                {
                    targetPath = resolved.Value.TargetPath;
                    args = resolved.Value.Arguments;
                    workDir = resolved.Value.WorkingDirectory;
                }
            }

            ShellCommandBox.Text = targetPath;
            if (!string.IsNullOrWhiteSpace(args) && string.IsNullOrWhiteSpace(ShellArgsBox.Text))
            {
                ShellArgsBox.Text = args;
            }
            if (!string.IsNullOrWhiteSpace(workDir) && string.IsNullOrWhiteSpace(ShellWorkDirBox.Text))
            {
                ShellWorkDirBox.Text = workDir;
            }

            if (string.IsNullOrWhiteSpace(ItemNameBox.Text))
            {
                ItemNameBox.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
            }
            OnFormEdited();
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

    private async Task<bool> SaveConfigurationCoreAsync()
    {
        CommitCurrentFormChanges();

        try
        {
            await _repository.SaveAsync(_items);
            RegisterShortcuts();
            RefreshTreeConflictStates();
            StatusText.Text = $"Saved and registered {_items.Count} items at {DateTime.Now:HH:mm:ss}";
            if (_selectedItem != null)
            {
                _originalItemSnapshot = _selectedItem.Clone();
            }
            SetDirty(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save configuration.");
            ModernMessageDialog.ShowAlert(this, "TriggerPoint Error", $"Failed to save configuration: {ex.Message}", ModernDialogType.Error);
            return false;
        }
    }

    private async void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        await SaveConfigurationCoreAsync();
    }

    private void RevertItemBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem == null || _originalItemSnapshot == null) return;

        RestoreItemFromSnapshot(_selectedItem, _originalItemSnapshot);
        PopulateForm(_selectedItem);
        FindViewModel(_selectedItem)?.NotifyUpdated();
        SetDirty(false);
        StatusText.Text = $"Reverted unsaved changes to '{_selectedItem.Name}'.";
    }

    private async void AppSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_logManagerService == null) return;
        if (!PromptSaveIfDirty()) return;

        var dlg = new ApplicationSettingsWindow(_repository, _logManagerService)
        {
            Owner = this
        };
        dlg.ShowDialog();
        try
        {
            _appSettings = await _repository.LoadSettingsAsync();
            RegisterShortcuts();
            if (Application.Current is App app)
            {
                _ = app.ReloadApplicationSettingsAndHotkeysAsync();
            }
        }
        catch { }
        if (dlg.TreeDataChanged)
        {
            _items = (await _repository.LoadAsync()).ToList();
            RegisterShortcuts();
            RebuildTree();
            RefreshTreeConflictStates();
            StatusText.Text = $"Reloaded {_items.Count} items following data import.";
        }
    }

    private TriggerTreeItemViewModel? _rightClickedTreeVm;

    private void AddActionBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!PromptSaveIfDirty()) return;
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
        SetDirty(true);
    }

    private async void DuplicateItemBtn_Click(object sender, RoutedEventArgs e)
    {
        CommitCurrentFormChanges();

        var itemToDuplicate = (sender == ContextDuplicateItem && _rightClickedTreeVm != null)
            ? _rightClickedTreeVm.Item
            : _selectedItem;

        if (itemToDuplicate == null) return;

        var clonedItems = ItemDuplicationHelper.DuplicateItemOrFolder(itemToDuplicate, _items);
        if (clonedItems.Count == 0) return;

        var primaryDuplicate = clonedItems[0];

        await _repository.SaveAsync(_items);
        RegisterShortcuts();
        RebuildTree();
        SelectTreeItem(primaryDuplicate);
        await RestoreTreeFocus(primaryDuplicate);

        if (itemToDuplicate.ActionType == ActionType.Folder)
        {
            int childCount = clonedItems.Count - 1;
            StatusText.Text = childCount > 0
                ? $"Duplicated folder '{itemToDuplicate.Name}' as '{primaryDuplicate.Name}' ({childCount} item{(childCount == 1 ? "" : "s")})."
                : $"Duplicated folder '{itemToDuplicate.Name}' as '{primaryDuplicate.Name}'.";
        }
        else
        {
            StatusText.Text = $"Duplicated action '{itemToDuplicate.Name}' as '{primaryDuplicate.Name}'.";
        }
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
        }
        else
        {
            _rightClickedTreeVm = null;
        }
    }

    private List<TriggerItem> GetAllDescendants(Guid folderId)
    {
        var descendants = new List<TriggerItem>();
        void Collect(Guid id)
        {
            var children = _items.Where(x => x.ParentId == id).ToList();
            descendants.AddRange(children);
            foreach (var child in children.Where(c => c.ActionType == ActionType.Folder))
            {
                Collect(child.Id);
            }
        }
        Collect(folderId);
        return descendants;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = string.Concat(name.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "item" : clean;
    }

    private void TreeContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (_rightClickedTreeVm != null)
        {
            if (_rightClickedTreeVm.IsRecycleBinRoot)
            {
                ContextAddActionItem.Visibility = Visibility.Collapsed;
                ContextAddFolderItem.Visibility = Visibility.Collapsed;
                ContextDuplicateItem.Visibility = Visibility.Collapsed;
                ContextRenameItem.Visibility = Visibility.Collapsed;
                ContextDeleteItem.Visibility = Visibility.Collapsed;
                if (ContextDeleteSeparator != null) ContextDeleteSeparator.Visibility = Visibility.Collapsed;
                ContextExportItem.Visibility = Visibility.Collapsed;
                ContextImportItem.Visibility = Visibility.Collapsed;
                if (ContextExportSeparator != null) ContextExportSeparator.Visibility = Visibility.Collapsed;
                ContextRestoreItem.Visibility = Visibility.Collapsed;
                ContextPermanentDeleteItem.Visibility = Visibility.Collapsed;
                ContextEmptyRecycleBinItem.Visibility = Visibility.Visible;
                ContextEmptyRecycleBinItem.IsEnabled = _recycledItems.Count > 0;
                return;
            }

            if (_rightClickedTreeVm.IsRecycledItem)
            {
                ContextAddActionItem.Visibility = Visibility.Collapsed;
                ContextAddFolderItem.Visibility = Visibility.Collapsed;
                ContextDuplicateItem.Visibility = Visibility.Collapsed;
                ContextRenameItem.Visibility = Visibility.Collapsed;
                ContextDeleteItem.Visibility = Visibility.Collapsed;
                if (ContextDeleteSeparator != null) ContextDeleteSeparator.Visibility = Visibility.Collapsed;
                ContextExportItem.Visibility = Visibility.Collapsed;
                ContextImportItem.Visibility = Visibility.Collapsed;
                if (ContextExportSeparator != null) ContextExportSeparator.Visibility = Visibility.Collapsed;
                ContextEmptyRecycleBinItem.Visibility = Visibility.Collapsed;
                ContextRestoreItem.Visibility = Visibility.Visible;
                ContextRestoreItem.Header = $"Restore '{_rightClickedTreeVm.Name}'";
                ContextPermanentDeleteItem.Visibility = Visibility.Visible;
                ContextPermanentDeleteItem.Header = $"Delete '{_rightClickedTreeVm.Name}' Permanently";
                return;
            }

            // Normal tree item
            ContextAddActionItem.Visibility = Visibility.Visible;
            ContextAddFolderItem.Visibility = Visibility.Visible;
            ContextExportItem.Visibility = Visibility.Visible;
            ContextImportItem.Visibility = Visibility.Visible;
            if (ContextExportSeparator != null) ContextExportSeparator.Visibility = Visibility.Visible;
            ContextRestoreItem.Visibility = Visibility.Collapsed;
            ContextPermanentDeleteItem.Visibility = Visibility.Collapsed;
            ContextEmptyRecycleBinItem.Visibility = Visibility.Collapsed;

            ContextDeleteItem.Visibility = Visibility.Visible;
            if (ContextDeleteSeparator != null) ContextDeleteSeparator.Visibility = Visibility.Visible;
            ContextDeleteItem.IsEnabled = true;
            ContextDeleteItem.Header = $"Delete '{_rightClickedTreeVm.Name}'";

            if (_rightClickedTreeVm.Item.ActionType == ActionType.Folder)
            {
                ContextAddActionItem.Header = $"New Action in '{_rightClickedTreeVm.Name}'";
                ContextAddFolderItem.Header = $"New Subfolder in '{_rightClickedTreeVm.Name}'";
            }
            else
            {
                var parentFolder = _rightClickedTreeVm.Item.ParentId.HasValue 
                    ? _items.FirstOrDefault(x => x.Id == _rightClickedTreeVm.Item.ParentId.Value) 
                    : null;
                if (parentFolder != null)
                {
                    ContextAddActionItem.Header = $"New Action in '{parentFolder.Name}'";
                    ContextAddFolderItem.Header = $"New Subfolder in '{parentFolder.Name}'";
                }
                else
                {
                    ContextAddActionItem.Header = "New Action at Root";
                    ContextAddFolderItem.Header = "New Folder at Root";
                }
            }

            ContextDuplicateItem.Visibility = Visibility.Visible;
            ContextDuplicateItem.IsEnabled = true;
            if (_rightClickedTreeVm.Item.ActionType == ActionType.Folder)
            {
                var descendants = GetAllDescendants(_rightClickedTreeVm.Item.Id);
                int totalDescendants = descendants.Count;
                ContextDuplicateItem.Header = totalDescendants > 0
                    ? $"Duplicate Folder '{_rightClickedTreeVm.Name}' ({totalDescendants} items)"
                    : $"Duplicate Folder '{_rightClickedTreeVm.Name}'";
            }
            else
            {
                ContextDuplicateItem.Header = $"Duplicate Action '{_rightClickedTreeVm.Name}'";
            }

            ContextRenameItem.Visibility = Visibility.Visible;
            ContextRenameItem.IsEnabled = true;
            ContextRenameItem.Header = $"Rename '{_rightClickedTreeVm.Name}'";

            if (_rightClickedTreeVm.Item.ActionType == ActionType.Folder)
            {
                var descendants = GetAllDescendants(_rightClickedTreeVm.Item.Id);
                int childActions = descendants.Count(x => x.ActionType != ActionType.Folder);
                int childFolders = descendants.Count(x => x.ActionType == ActionType.Folder);
                string countStr = childFolders > 0 ? $"{childFolders} subfolder(s), {childActions} action(s)" : $"{childActions} action(s)";
                ContextExportItem.Header = $"Export Folder '{_rightClickedTreeVm.Name}' ({countStr})...";
                ContextImportItem.Header = $"Import Actions into '{_rightClickedTreeVm.Name}'...";
            }
            else
            {
                ContextExportItem.Header = $"Export Action '{_rightClickedTreeVm.Name}' (1 action)...";
                ContextImportItem.Header = "Import Actions Here...";
            }
        }
        else
        {
            ContextAddActionItem.Visibility = Visibility.Visible;
            ContextAddFolderItem.Visibility = Visibility.Visible;
            ContextExportItem.Visibility = Visibility.Visible;
            ContextImportItem.Visibility = Visibility.Visible;
            if (ContextExportSeparator != null) ContextExportSeparator.Visibility = Visibility.Visible;
            ContextRestoreItem.Visibility = Visibility.Collapsed;
            ContextPermanentDeleteItem.Visibility = Visibility.Collapsed;
            ContextEmptyRecycleBinItem.Visibility = Visibility.Collapsed;

            ContextDeleteItem.Visibility = Visibility.Collapsed;
            if (ContextDeleteSeparator != null) ContextDeleteSeparator.Visibility = Visibility.Collapsed;
            ContextDuplicateItem.Visibility = Visibility.Collapsed;
            ContextRenameItem.Visibility = Visibility.Collapsed;

            ContextAddActionItem.Header = "New Action at Root";
            ContextAddFolderItem.Header = "New Folder at Root";

            int totalFolders = _items.Count(x => x.ActionType == ActionType.Folder);
            int totalActions = _items.Count(x => x.ActionType != ActionType.Folder);
            ContextExportItem.Header = $"Export All ({totalFolders} folder(s), {totalActions} action(s))...";
            ContextImportItem.Header = "Import Actions & Folders at Root...";
        }
    }

    private void ContextRenameItem_Click(object sender, RoutedEventArgs e)
    {
        var vm = _rightClickedTreeVm ?? (_selectedItem != null ? FindViewModel(_selectedItem) : null);
        if (vm != null && !vm.IsRecycleBinRoot && !vm.IsRecycledItem)
        {
            SelectTreeItem(vm.Item);
            vm.StartEdit();
        }
    }

    private async void ContextExportItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CommitCurrentFormChanges();

            List<TriggerItem> itemsToExport;
            string defaultFileName;
            string scopeName;

            if (_rightClickedTreeVm != null)
            {
                if (_rightClickedTreeVm.Item.ActionType == ActionType.Folder)
                {
                    var descendants = GetAllDescendants(_rightClickedTreeVm.Item.Id);
                    var rootFolderClone = _rightClickedTreeVm.Item.Clone();
                    rootFolderClone.ParentId = null;

                    itemsToExport = [rootFolderClone];
                    foreach (var d in descendants)
                    {
                        itemsToExport.Add(d.Clone());
                    }

                    scopeName = _rightClickedTreeVm.Name;
                    defaultFileName = $"triggerpoint-folder-{SanitizeFileName(_rightClickedTreeVm.Name)}.json";
                }
                else
                {
                    var actionClone = _rightClickedTreeVm.Item.Clone();
                    actionClone.ParentId = null;
                    itemsToExport = [actionClone];

                    scopeName = _rightClickedTreeVm.Name;
                    defaultFileName = $"triggerpoint-action-{SanitizeFileName(_rightClickedTreeVm.Name)}.json";
                }
            }
            else
            {
                itemsToExport = _items.Select(x => x.Clone()).ToList();
                scopeName = "All Items";
                defaultFileName = "triggerpoint-actions-export.json";
            }

            var package = ConfigurationBackupPackage.CreateTreeItems(itemsToExport, scopeName);

            var dlg = new SaveFileDialog
            {
                Title = $"Export {scopeName}",
                Filter = "JSON Backup File (*.json)|*.json|All Files (*.*)|*.*",
                FileName = defaultFileName
            };

            if (dlg.ShowDialog(this) == true)
            {
                await _repository.ExportPackageAsync(dlg.FileName, package);
                StatusText.Text = $"📤 Exported {package.FolderCount} folder(s) and {package.ActionCount} action(s) to '{Path.GetFileName(dlg.FileName)}'.";
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to export configuration items");
            ModernMessageDialog.ShowAlert(this, "Export Error", $"Failed to export items: {ex.Message}", ModernDialogType.Error);
        }
    }

    private async void ContextImportItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new OpenFileDialog
            {
                Title = "Import Actions or Folders",
                Filter = "JSON Backup File (*.json)|*.json|All Files (*.*)|*.*"
            };

            if (dlg.ShowDialog(this) != true) return;

            var package = await _repository.ReadPackageAsync(dlg.FileName);
            if (package == null)
            {
                ModernMessageDialog.ShowAlert(this, "Import Error", "Failed to read backup package from the selected file.", ModernDialogType.Error);
                return;
            }

            if (package.Items == null || package.Items.Count == 0)
            {
                if (package.ContentType == BackupContentType.AppSettings)
                {
                    ModernMessageDialog.ShowAlert(this, "Settings Backup Detected",
                        "The selected file contains Application Settings, not actions or folders.\n\nTo import or restore application settings, please open the Application Settings window (gear icon in header).",
                        ModernDialogType.Info);
                }
                else
                {
                    ModernMessageDialog.ShowAlert(this, "Import", "The selected backup package contains no actions or folders.", ModernDialogType.Warning);
                }
                return;
            }

            Guid? targetParentId = null;
            string targetScopeName = "Root";

            if (_rightClickedTreeVm != null)
            {
                if (_rightClickedTreeVm.Item.ActionType == ActionType.Folder)
                {
                    targetParentId = _rightClickedTreeVm.Item.Id;
                    targetScopeName = $"'{_rightClickedTreeVm.Name}'";
                }
                else
                {
                    targetParentId = _rightClickedTreeVm.Item.ParentId;
                    var parentFolder = targetParentId.HasValue ? _items.FirstOrDefault(x => x.Id == targetParentId.Value) : null;
                    targetScopeName = parentFolder != null ? $"'{parentFolder.Name}'" : "Root";
                }
            }

            bool canReplace = _rightClickedTreeVm == null;
            var choice = ModernMessageDialog.ShowImportChoiceDialog(
                this,
                targetScopeName,
                package.FolderCount,
                package.ActionCount,
                canReplace);

            if (choice == ImportChoice.Cancel) return;

            CommitCurrentFormChanges();

            TriggerItem? itemToSelect = null;

            if (choice == ImportChoice.Replace)
            {
                _items.Clear();
                _items.AddRange(package.Items.Select(x => x.Clone()));
                itemToSelect = _items.FirstOrDefault();
            }
            else // ImportChoice.Merge
            {
                var idMap = new Dictionary<Guid, Guid>();
                foreach (var item in package.Items)
                {
                    idMap[item.Id] = Guid.NewGuid();
                }

                var importedItems = new List<TriggerItem>();
                foreach (var origItem in package.Items)
                {
                    var clone = origItem.Clone();
                    clone.Id = idMap[origItem.Id];

                    if (origItem.ParentId.HasValue && idMap.TryGetValue(origItem.ParentId.Value, out var newParentGuid))
                    {
                        clone.ParentId = newParentGuid;
                    }
                    else
                    {
                        clone.ParentId = targetParentId;
                    }

                    importedItems.Add(clone);
                }

                _items.AddRange(importedItems);
                itemToSelect = importedItems.FirstOrDefault();
            }

            await _repository.SaveAsync(_items);
            RegisterShortcuts();
            RebuildTree();
            RefreshTreeConflictStates();

            if (itemToSelect != null)
            {
                SelectTreeItem(itemToSelect);
                await RestoreTreeFocus(itemToSelect);
            }

            StatusText.Text = $"📥 Successfully imported {package.FolderCount} folder(s) and {package.ActionCount} action(s).";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to import configuration package");
            ModernMessageDialog.ShowAlert(this, "Import Error", $"Failed to import items: {ex.Message}", ModernDialogType.Error);
        }
    }

    private void AddFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!PromptSaveIfDirty()) return;
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
            PresentationMode = PresentationMode.Direct,
            OrderIndex = _items.Count
        };

        _items.Add(newFolder);
        RebuildTree();
        SelectTreeItem(newFolder);
        _ = RestoreTreeFocus(newFolder);
        SetDirty(true);
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
            if (EditorTypeBadge != null) EditorTypeBadge.Visibility = Visibility.Collapsed;
            if (EditorPanel != null) EditorPanel.Visibility = Visibility.Collapsed;
            if (SaveBtn != null) SaveBtn.IsEnabled = false;
            if (TestActionBtn != null) TestActionBtn.Visibility = Visibility.Collapsed;
            if (DeleteItemBtn != null) DeleteItemBtn.Visibility = Visibility.Collapsed;

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
            AllowedUrlsTagInput.SetTags(new List<string>());
            ExcludedUrlsTagInput.SetTags(new List<string>());
            SetBrowserUrlSectionExpanded(false);
        }
        finally
        {
            _isUpdatingForm = false;
        }
    }

    private string GetItemPath(TriggerItem item)
    {
        var segments = new List<string>();
        var folderMap = _items.Where(x => x.ActionType == ActionType.Folder).ToDictionary(x => x.Id);

        var currentParentId = item.ParentId;
        while (currentParentId.HasValue && folderMap.TryGetValue(currentParentId.Value, out var parentFolder))
        {
            segments.Insert(0, parentFolder.Name);
            currentParentId = parentFolder.ParentId;
        }

        return segments.Count > 0 ? string.Join(" / ", segments) : "Root";
    }

    private async void DeleteItemBtn_Click(object sender, RoutedEventArgs e)
    {
        var itemToDelete = (sender == ContextDeleteItem && _rightClickedTreeVm != null)
            ? _rightClickedTreeVm.Item
            : _selectedItem;

        if (itemToDelete == null) return;

        var focusCandidate = FindPostDeleteFocusCandidate(itemToDelete);

        if (itemToDelete.ActionType == ActionType.Folder)
        {
            var directChildren = _items.Where(x => x.ParentId == itemToDelete.Id).ToList();
            var descendants = GetAllDescendants(itemToDelete.Id);

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
                    await _repository.MoveToRecycleBinAsync(itemToDelete, _items);
                    await _repository.SaveAsync(_items);
                    RegisterShortcuts();
                    _selectedItem = null;
                    await RebuildTreeAsync();

                    if (focusCandidate != null && _items.Any(x => x.Id == focusCandidate.Id))
                    {
                        SelectTreeItem(focusCandidate);
                    }
                    else if (directChildren.Count > 0)
                    {
                        SelectTreeItem(directChildren[0]);
                        focusCandidate = directChildren[0];
                    }

                    StatusText.Text = $"Moved folder '{itemToDelete.Name}' to Recycle Bin and moved {descendants.Count} item(s) to root.";
                    SetDirty(false);
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

                    await _repository.MoveToRecycleBinAsync(toDelete, _items);
                    await _repository.SaveAsync(_items);
                    RegisterShortcuts();
                    _selectedItem = null;
                    await RebuildTreeAsync();

                    if (focusCandidate != null && _items.Any(x => x.Id == focusCandidate.Id))
                    {
                        SelectTreeItem(focusCandidate);
                    }

                    StatusText.Text = $"Moved folder '{itemToDelete.Name}' and {descendants.Count} contained item(s) to Recycle Bin.";
                    SetDirty(false);
                    await RestoreTreeFocus(focusCandidate);
                    return;
                }
            }
            else
            {
                // Empty folder
                bool confirmed = ModernMessageDialog.ShowConfirm(
                    this,
                    "Move to Recycle Bin",
                    $"Move folder '{itemToDelete.Name}' to the Recycle Bin?",
                    "Move to Bin",
                    "Cancel",
                    isDestructive: false);

                if (!confirmed)
                {
                    await RestoreTreeFocus(itemToDelete);
                    return;
                }

                _items.Remove(itemToDelete);
                await _repository.MoveToRecycleBinAsync(itemToDelete, _items);
                await _repository.SaveAsync(_items);
                RegisterShortcuts();
                _selectedItem = null;
                await RebuildTreeAsync();

                if (focusCandidate != null && _items.Any(x => x.Id == focusCandidate.Id))
                {
                    SelectTreeItem(focusCandidate);
                }

                StatusText.Text = $"Moved folder '{itemToDelete.Name}' to Recycle Bin.";
                SetDirty(false);
                await RestoreTreeFocus(focusCandidate);
                return;
            }
        }
        else
        {
            // Action item
            bool confirmed = ModernMessageDialog.ShowConfirm(
                this,
                "Move to Recycle Bin",
                $"Move '{itemToDelete.Name}' to the Recycle Bin?",
                "Move to Bin",
                "Cancel",
                isDestructive: false);

            if (!confirmed)
            {
                await RestoreTreeFocus(itemToDelete);
                return;
            }

            _items.Remove(itemToDelete);
            await _repository.MoveToRecycleBinAsync(itemToDelete, _items);
            await _repository.SaveAsync(_items);
            RegisterShortcuts();
            _selectedItem = null;
            await RebuildTreeAsync();

            if (focusCandidate != null && _items.Any(x => x.Id == focusCandidate.Id))
            {
                SelectTreeItem(focusCandidate);
            }

            StatusText.Text = $"Moved item '{itemToDelete.Name}' to Recycle Bin.";
            SetDirty(false);
            await RestoreTreeFocus(focusCandidate);
        }
    }

    private async void RestoreRecycledItemBtn_Click(object sender, RoutedEventArgs e)
    {
        var targetItem = (sender == ContextRestoreItem && _rightClickedTreeVm != null)
            ? _rightClickedTreeVm.Item
            : _selectedItem;

        if (targetItem == null) return;
        var vm = FindViewModel(targetItem);
        if (vm?.RecycledInfo == null) return;

        try
        {
            var restoredItem = await _repository.RestoreFromRecycleBinAsync(vm.RecycledInfo.Id);
            if (restoredItem != null)
            {
                if (restoredItem.ParentId.HasValue && !_items.Any(x => x.Id == restoredItem.ParentId.Value))
                {
                    restoredItem.ParentId = null;
                }
                restoredItem.OrderIndex = _items.Count;
                _items.Add(restoredItem);
                await _repository.SaveAsync(_items);
                RegisterShortcuts();
                await RebuildTreeAsync();
                SelectTreeItem(restoredItem);
                await RestoreTreeFocus(restoredItem);
                StatusText.Text = $"Restored '{restoredItem.Name}' from Recycle Bin.";
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to restore item from recycle bin.");
            ModernMessageDialog.ShowAlert(this, "Restore Error", $"Failed to restore item: {ex.Message}", ModernDialogType.Error);
        }
    }

    private async void PermanentlyDeleteRecycledItemBtn_Click(object sender, RoutedEventArgs e)
    {
        var targetItem = (sender == ContextPermanentDeleteItem && _rightClickedTreeVm != null)
            ? _rightClickedTreeVm.Item
            : _selectedItem;

        if (targetItem == null) return;
        var vm = FindViewModel(targetItem);
        if (vm?.RecycledInfo == null) return;

        bool confirm = ModernMessageDialog.ShowConfirm(this,
            "Permanent Deletion",
            $"Are you sure you want to permanently delete '{vm.Name}'? This action cannot be undone.",
            "Delete Permanently", "Cancel", isDestructive: true);

        if (!confirm) return;

        try
        {
            await _repository.PermanentlyDeleteFromRecycleBinAsync(vm.RecycledInfo.Id);
            _selectedItem = null;
            await RebuildTreeAsync();
            StatusText.Text = $"Permanently deleted '{vm.Name}'.";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to permanently delete item.");
            ModernMessageDialog.ShowAlert(this, "Delete Error", $"Failed to permanently delete item: {ex.Message}", ModernDialogType.Error);
        }
    }

    private async void EmptyRecycleBinFromSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        bool confirm = ModernMessageDialog.ShowConfirm(this,
            "Empty Recycle Bin",
            $"Are you sure you want to permanently delete all {_recycledItems.Count} item(s) from the Recycle Bin? This action cannot be undone.",
            "Empty Bin", "Cancel", isDestructive: true);

        if (!confirm) return;

        try
        {
            await _repository.EmptyRecycleBinAsync();
            _selectedItem = null;
            await RebuildTreeAsync();
            StatusText.Text = "Recycle Bin emptied.";
            ModernMessageDialog.ShowAlert(this, "Recycle Bin Emptied", "All items in the Recycle Bin have been permanently deleted.", ModernDialogType.Info);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to empty recycle bin.");
            ModernMessageDialog.ShowAlert(this, "Error", $"Failed to empty recycle bin: {ex.Message}", ModernDialogType.Error);
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
            SnoozeToggleBtn.Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("SuccessBrush");
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
        return FindViewModelRecursive(_treeRoots, item.Id);
    }

    private static TriggerTreeItemViewModel? FindViewModelRecursive(IEnumerable<TriggerTreeItemViewModel> vms, Guid id)
    {
        foreach (var vm in vms)
        {
            if (vm.Item.Id == id) return vm;
            var found = FindViewModelRecursive(vm.Children, id);
            if (found != null) return found;
        }
        return null;
    }

    private void UpdateCommandValidationStatus(string? command)
    {
        if (CommandValidationStatusText == null) return;

        if (ActionTypeCombo == null || ActionTypeCombo.SelectedIndex != 0) // Not Shell
        {
            CommandValidationStatusText.Visibility = Visibility.Collapsed;
            return;
        }

        if (string.IsNullOrWhiteSpace(command))
        {
            CommandValidationStatusText.Text = "⚠ Command / target path is empty";
            CommandValidationStatusText.Foreground = Application.Current.TryFindResource("WarningBrush") as Brush ?? Brushes.Orange;
            CommandValidationStatusText.Visibility = Visibility.Visible;
            return;
        }

        var result = ShortcutValidator.Validate(command);
        if (result.Status == ShortcutValidationStatus.Valid)
        {
            CommandValidationStatusText.Text = string.IsNullOrWhiteSpace(result.ResolvedPath) || result.ResolvedPath.Equals(command, StringComparison.OrdinalIgnoreCase)
                ? "✓ Valid target path"
                : $"✓ Valid ({result.ResolvedPath})";
            CommandValidationStatusText.Foreground = Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.Green;
            CommandValidationStatusText.Visibility = Visibility.Visible;
        }
        else
        {
            CommandValidationStatusText.Text = $"✕ {result.Message}";
            CommandValidationStatusText.Foreground = Application.Current.TryFindResource("ErrorBrush") as Brush ?? Brushes.Red;
            CommandValidationStatusText.Visibility = Visibility.Visible;
        }
    }

    private void UpdateBrokenFilterChipCount()
    {
        if (BrokenFilterChip == null || BrokenFilterChipText == null) return;
        int brokenCount = CountBrokenItemsRecursive(_treeRoots);
        if (brokenCount > 0)
        {
            BrokenFilterChip.Visibility = Visibility.Visible;
            BrokenFilterChipText.Text = $"{brokenCount} Broken";
        }
        else
        {
            BrokenFilterChip.Visibility = Visibility.Collapsed;
            _filterBrokenOnly = false;
            BrokenFilterChip.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xEF, 0x44, 0x44));
        }
    }

    private static int CountBrokenItemsRecursive(IEnumerable<TriggerTreeItemViewModel> items)
    {
        int count = 0;
        foreach (var vm in items)
        {
            if (vm.IsRecycleBinRoot) continue;
            if (vm.IsBrokenTarget) count++;
            count += CountBrokenItemsRecursive(vm.Children);
        }
        return count;
    }

    private bool _filterBrokenOnly;

    private void UpdateTreeFilter()
    {
        var query = TreeSearchBox?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query) && !_filterBrokenOnly)
        {
            RebuildTree();
            return;
        }

        // Filter tree roots
        var filteredRoots = new ObservableCollection<TriggerTreeItemViewModel>();
        foreach (var item in _items)
        {
            bool isBroken = false;
            string? brokenMsg = null;
            if (item.ActionType == ActionType.Shell)
            {
                var validation = ShortcutValidator.Validate(item.Payload.Command);
                if (validation.Status != ShortcutValidationStatus.Valid)
                {
                    isBroken = true;
                    brokenMsg = validation.Message;
                }
            }

            if (_filterBrokenOnly && !isBroken)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                if (!item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                    !item.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            var vm = new TriggerTreeItemViewModel(item)
            {
                IsBrokenTarget = isBroken,
                BrokenTargetMessage = brokenMsg
            };
            filteredRoots.Add(vm);
        }
        ItemsTreeView.ItemsSource = filteredRoots;
    }

    private void TreeSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateTreeFilter();
    }

    private void BrokenFilterChip_Click(object sender, RoutedEventArgs e)
    {
        _filterBrokenOnly = !_filterBrokenOnly;
        if (_filterBrokenOnly)
        {
            BrokenFilterChip.Background = new SolidColorBrush(Color.FromArgb(0x44, 0xEF, 0x44, 0x44));
        }
        else
        {
            BrokenFilterChip.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xEF, 0x44, 0x44));
        }
        UpdateTreeFilter();
    }

    private void ToggleExpandAllBtn_Click(object sender, RoutedEventArgs e)
    {
        var folders = _items.Where(x => x.ActionType == ActionType.Folder).ToList();
        if (folders.Count == 0) return;

        bool anyExpanded = folders.Any(f => f.IsExpanded);
        bool newState = !anyExpanded;

        foreach (var f in folders)
        {
            f.IsExpanded = newState;
        }

        SetAllFoldersExpanded(_treeRoots, newState);
        UpdateExpandAllButtonGlyph();
        _ = _repository.SaveAsync(_items);
    }

    private static void SetAllFoldersExpanded(IEnumerable<TriggerTreeItemViewModel> vms, bool expanded)
    {
        foreach (var vm in vms)
        {
            if (vm.Item.ActionType == ActionType.Folder && !vm.IsRecycleBinRoot)
            {
                vm.IsExpanded = expanded;
            }
            SetAllFoldersExpanded(vm.Children, expanded);
        }
    }

    private void UpdateExpandAllButtonGlyph()
    {
        if (ToggleExpandAllBtn == null) return;
        var folders = _items.Where(x => x.ActionType == ActionType.Folder).ToList();
        bool anyExpanded = folders.Any(f => f.IsExpanded);
        ToggleExpandAllBtn.Content = anyExpanded ? "⊟" : "⊞";
        ToggleExpandAllBtn.ToolTip = anyExpanded ? "Collapse All Folders" : "Expand All Folders";
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
        // If editing an item name inline or if focus is inside any TextBox, do NOT intercept keys (Delete, Ctrl+D, F2)
        if (IsEventFromTextBox(e.OriginalSource) || (_selectedItem != null && FindViewModel(_selectedItem)?.IsEditingName == true))
        {
            return;
        }

        if (e.Key == Key.Delete && _selectedItem != null)
        {
            var vm = FindViewModel(_selectedItem);
            if (vm?.IsRecycledItem == true)
            {
                PermanentlyDeleteRecycledItemBtn_Click(this, new RoutedEventArgs());
            }
            else if (vm?.IsRecycleBinRoot == true)
            {
                EmptyRecycleBinFromSettingsBtn_Click(this, new RoutedEventArgs());
            }
            else
            {
                DeleteItemBtn_Click(this, new RoutedEventArgs());
            }
            e.Handled = true;
        }
        else if (e.Key == Key.D && (Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control && _selectedItem != null)
        {
            var vm = FindViewModel(_selectedItem);
            if (vm?.IsRecycledItem != true && vm?.IsRecycleBinRoot != true)
            {
                DuplicateItemBtn_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
        else if (e.Key == Key.F2 && _selectedItem != null)
        {
            var vm = FindViewModel(_selectedItem);
            if (vm != null && !vm.IsRecycleBinRoot && !vm.IsRecycledItem)
            {
                vm.StartEdit();
                e.Handled = true;
            }
        }
    }

    private static bool IsEventFromTextBox(object? source)
    {
        if (source is TextBox) return true;
        if (source is DependencyObject dep)
        {
            DependencyObject? current = dep;
            while (current != null)
            {
                if (current is TextBox) return true;
                current = VisualTreeHelper.GetParent(current);
            }
        }
        return false;
    }

    // Inline Tree Item Renaming Handlers
    private void InlineRenameBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.IsVisible)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                tb.Focus();
                tb.SelectAll();
            }));
        }
    }

    private async void InlineRenameBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb || tb.DataContext is not TriggerTreeItemViewModel vm) return;

        if (e.Key == Key.Enter || e.Key == Key.Return)
        {
            e.Handled = true;
            await CommitInlineRenameAsync(vm, tb.Text);
            ItemsTreeView.Focus();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            vm.CancelEdit();
            ItemsTreeView.Focus();
        }
    }

    private async void InlineRenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb || tb.DataContext is not TriggerTreeItemViewModel vm) return;

        if (e.Key == Key.Enter || e.Key == Key.Return)
        {
            e.Handled = true;
            await CommitInlineRenameAsync(vm, tb.Text);
            ItemsTreeView.Focus();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            vm.CancelEdit();
            ItemsTreeView.Focus();
        }
    }

    private async void InlineRenameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is TriggerTreeItemViewModel vm && vm.IsEditingName)
        {
            await CommitInlineRenameAsync(vm, tb.Text);
        }
    }

    private async Task CommitInlineRenameAsync(TriggerTreeItemViewModel vm, string? newName)
    {
        if (!vm.IsEditingName) return;

        var trimmed = newName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            vm.CancelEdit();
            return;
        }

        string oldName = vm.Item.Name;
        vm.Item.Name = trimmed;
        vm.EditingNameText = trimmed;
        vm.IsEditingName = false;
        vm.NotifyUpdated();

        if (_selectedItem?.Id == vm.Item.Id)
        {
            _isUpdatingForm = true;
            try
            {
                ItemNameBox.Text = trimmed;
                EditorHeaderTitle.Text = trimmed;
            }
            finally
            {
                _isUpdatingForm = false;
            }

            if (_originalItemSnapshot != null)
            {
                _originalItemSnapshot.Name = trimmed;
            }
        }

        try
        {
            await _repository.SaveAsync(_items);
            SetDirty(false);
            StatusText.Text = $"Renamed '{oldName}' to '{trimmed}'.";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save renamed item");
            StatusText.Text = $"Failed to save renamed item: {ex.Message}";
        }
    }

    private TriggerTreeItemViewModel? FindCurrentlyEditingViewModel()
    {
        return FindCurrentlyEditingViewModelRecursive(_treeRoots);
    }

    private static TriggerTreeItemViewModel? FindCurrentlyEditingViewModelRecursive(IEnumerable<TriggerTreeItemViewModel> vms)
    {
        foreach (var vm in vms)
        {
            if (vm.IsEditingName) return vm;
            var found = FindCurrentlyEditingViewModelRecursive(vm.Children);
            if (found != null) return found;
        }
        return null;
    }

    private void CommitActiveInlineRenameSync(TriggerTreeItemViewModel vm)
    {
        if (!vm.IsEditingName) return;
        _ = CommitInlineRenameAsync(vm, vm.EditingNameText);
    }

    // TreeView Drag & Drop Re-sorting with Visual Indicators and Ghost Preview
    private TriggerTreeItemViewModel? _currentDropTargetVm;
    private TreeDropPosition _currentDropPosition = TreeDropPosition.None;

    private DispatcherTimer? _springLoadFolderTimer;
    private TriggerTreeItemViewModel? _springLoadCandidateVm;

    private void StartOrContinueSpringLoadTimer(TriggerTreeItemViewModel folderVm)
    {
        if (folderVm.IsExpanded || folderVm.IsRecycleBinRoot)
        {
            ResetSpringLoadTimer();
            return;
        }

        if (_springLoadCandidateVm == folderVm && _springLoadFolderTimer?.IsEnabled == true)
        {
            return;
        }

        ResetSpringLoadTimer();
        _springLoadCandidateVm = folderVm;
        _springLoadFolderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(650)
        };
        _springLoadFolderTimer.Tick += (s, args) =>
        {
            var target = _springLoadCandidateVm;
            ResetSpringLoadTimer();
            if (target != null && !target.IsExpanded)
            {
                target.IsExpanded = true;
                target.NotifyUpdated();
                UpdateExpandAllButtonGlyph();
            }
        };
        _springLoadFolderTimer.Start();
    }

    private void ResetSpringLoadTimer()
    {
        if (_springLoadFolderTimer != null)
        {
            _springLoadFolderTimer.Stop();
            _springLoadFolderTimer = null;
        }
        _springLoadCandidateVm = null;
    }

    private void ClearDropIndicators()
    {
        ResetSpringLoadTimer();
        if (_currentDropTargetVm != null)
        {
            _currentDropTargetVm.DropPosition = TreeDropPosition.None;
            _currentDropTargetVm = null;
        }
        _currentDropPosition = TreeDropPosition.None;
    }

    private void UpdateGhostPosition(DragEventArgs e)
    {
        if (TreeDragGhostPopup != null && TreeDragGhostPopup.IsOpen)
        {
            var screenPt = PointToScreen(e.GetPosition(this));
            TreeDragGhostPopup.HorizontalOffset = screenPt.X + 14;
            TreeDragGhostPopup.VerticalOffset = screenPt.Y + 14;
        }
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var currentlyEditing = FindCurrentlyEditingViewModel();
        if (currentlyEditing != null && !IsEventFromTextBox(e.OriginalSource))
        {
            CommitActiveInlineRenameSync(currentlyEditing);
        }

        base.OnPreviewMouseLeftButtonDown(e);
    }

    protected override void OnPreviewMouseRightButtonDown(MouseButtonEventArgs e)
    {
        var currentlyEditing = FindCurrentlyEditingViewModel();
        if (currentlyEditing != null && !IsEventFromTextBox(e.OriginalSource))
        {
            CommitActiveInlineRenameSync(currentlyEditing);
        }

        base.OnPreviewMouseRightButtonDown(e);
    }

    private void ItemsTreeView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var currentlyEditing = FindCurrentlyEditingViewModel();
        if (currentlyEditing != null && !IsEventFromTextBox(e.OriginalSource))
        {
            CommitActiveInlineRenameSync(currentlyEditing);
        }

        _treeDragStartPoint = e.GetPosition(null);
        _draggedTreeVm = FindTreeItemViewModelUnderMouse(e.OriginalSource as DependencyObject);
    }

    private void ItemsTreeView_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && _treeDragStartPoint.HasValue && _draggedTreeVm != null)
        {
            if (_draggedTreeVm.IsRecycleBinRoot || _draggedTreeVm.IsRecycledItem || _draggedTreeVm.IsEditingName)
            {
                _draggedTreeVm = null;
                return;
            }

            var currentPoint = e.GetPosition(null);
            var diff = _treeDragStartPoint.Value - currentPoint;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (TreeDragGhostPopup != null && _draggedTreeVm != null)
                {
                    TreeDragGhostIcon.Text = _draggedTreeVm.IconSymbol;
                    TreeDragGhostIcon.Foreground = _draggedTreeVm.IconBrush;
                    TreeDragGhostText.Text = _draggedTreeVm.Name;
                    var screenPt = PointToScreen(e.GetPosition(this));
                    TreeDragGhostPopup.HorizontalOffset = screenPt.X + 14;
                    TreeDragGhostPopup.VerticalOffset = screenPt.Y + 14;
                    TreeDragGhostPopup.IsOpen = true;
                }

                var data = new DataObject("TriggerTreeItemViewModel", _draggedTreeVm);
                try
                {
                    DragDrop.DoDragDrop(ItemsTreeView, data, DragDropEffects.Move);
                }
                finally
                {
                    if (TreeDragGhostPopup != null) TreeDragGhostPopup.IsOpen = false;
                    ClearDropIndicators();
                    _treeDragStartPoint = null;
                    _draggedTreeVm = null;
                }
            }
        }
    }

    private void ItemsTreeView_DragOver(object sender, DragEventArgs e)
    {
        UpdateGhostPosition(e);

        if (!e.Data.GetDataPresent("TriggerTreeItemViewModel"))
        {
            ClearDropIndicators();
            return;
        }

        var draggedVm = e.Data.GetData("TriggerTreeItemViewModel") as TriggerTreeItemViewModel;
        if (draggedVm == null || draggedVm.IsRecycleBinRoot || draggedVm.IsRecycledItem)
        {
            ClearDropIndicators();
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var tvi = FindTreeViewItemUnderMouse(e.OriginalSource as DependencyObject);
        var targetVm = tvi?.DataContext as TriggerTreeItemViewModel;

        if (tvi == null || targetVm == null)
        {
            ClearDropIndicators();
            e.Effects = DragDropEffects.Move;
            StatusText.Text = $"Move '{draggedVm.Name}' to root (at end)";
            e.Handled = true;
            return;
        }

        if (targetVm.IsRecycleBinRoot || targetVm.IsRecycledItem || targetVm.Item.Id == draggedVm.Item.Id)
        {
            ClearDropIndicators();
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (draggedVm.Item.ActionType == ActionType.Folder && IsDescendant(targetVm.Item, draggedVm.Item.Id))
        {
            ClearDropIndicators();
            e.Effects = DragDropEffects.None;
            StatusText.Text = "Cannot move a folder into its own sub-folder.";
            e.Handled = true;
            return;
        }

        // Calculate precision drop zone
        Point relPos = e.GetPosition(tvi);
        double height = tvi.ActualHeight;
        TreeDropPosition pos;

        if (targetVm.Item.ActionType == ActionType.Folder)
        {
            if (!targetVm.IsExpanded)
            {
                StartOrContinueSpringLoadTimer(targetVm);
            }
            else
            {
                ResetSpringLoadTimer();
            }

            if (relPos.Y < height * 0.25)
            {
                pos = TreeDropPosition.Above;
            }
            else if (relPos.Y > height * 0.75)
            {
                pos = TreeDropPosition.Below;
            }
            else
            {
                pos = TreeDropPosition.Inside;
            }
        }
        else
        {
            ResetSpringLoadTimer();
            pos = relPos.Y < height * 0.5 ? TreeDropPosition.Above : TreeDropPosition.Below;
        }

        if (_currentDropTargetVm != targetVm || _currentDropPosition != pos)
        {
            if (_currentDropTargetVm != null && _currentDropTargetVm != targetVm)
            {
                _currentDropTargetVm.DropPosition = TreeDropPosition.None;
            }
            _currentDropTargetVm = targetVm;
            _currentDropPosition = pos;
            targetVm.DropPosition = pos;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        StatusText.Text = pos switch
        {
            TreeDropPosition.Above => $"↑ Insert '{draggedVm.Name}' before '{targetVm.Name}'",
            TreeDropPosition.Below => $"↓ Insert '{draggedVm.Name}' after '{targetVm.Name}'",
            TreeDropPosition.Inside => $"➜ Move '{draggedVm.Name}' into folder '{targetVm.Name}'",
            _ => $"Move '{draggedVm.Name}'"
        };
    }

    private void ItemsTreeView_DragLeave(object sender, DragEventArgs e)
    {
        Point pt = e.GetPosition(ItemsTreeView);
        if (pt.X < 0 || pt.Y < 0 || pt.X > ItemsTreeView.ActualWidth || pt.Y > ItemsTreeView.ActualHeight)
        {
            ClearDropIndicators();
        }
    }

    private async void ItemsTreeView_Drop(object sender, DragEventArgs e)
    {
        if (TreeDragGhostPopup != null) TreeDragGhostPopup.IsOpen = false;

        if (!e.Data.GetDataPresent("TriggerTreeItemViewModel"))
        {
            ClearDropIndicators();
            return;
        }

        var draggedVm = e.Data.GetData("TriggerTreeItemViewModel") as TriggerTreeItemViewModel;
        if (draggedVm == null || draggedVm.IsRecycleBinRoot || draggedVm.IsRecycledItem)
        {
            ClearDropIndicators();
            return;
        }

        var targetVm = _currentDropTargetVm;
        var dropPos = _currentDropPosition;
        ClearDropIndicators();

        if (targetVm != null && targetVm.Item.Id == draggedVm.Item.Id) return;

        if (draggedVm.Item.ActionType == ActionType.Folder && targetVm != null)
        {
            if (IsDescendant(targetVm.Item, draggedVm.Item.Id))
            {
                StatusText.Text = "Cannot move a folder into its own sub-folder.";
                return;
            }
        }

        CommitCurrentFormChanges();

        if (targetVm == null || dropPos == TreeDropPosition.None)
        {
            // Dropped on empty area of TreeView -> move to root at the end
            draggedVm.Item.ParentId = null;
            var rootSiblings = _items.Where(x => x.ParentId == null && x.Id != draggedVm.Item.Id).ToList();
            draggedVm.Item.OrderIndex = rootSiblings.Count > 0 ? rootSiblings.Max(x => x.OrderIndex) + 1 : 0;
        }
        else if (dropPos == TreeDropPosition.Inside && targetVm.Item.ActionType == ActionType.Folder)
        {
            // Move inside folder as child at the end
            draggedVm.Item.ParentId = targetVm.Item.Id;
            var children = _items.Where(x => x.ParentId == targetVm.Item.Id && x.Id != draggedVm.Item.Id).ToList();
            draggedVm.Item.OrderIndex = children.Count > 0 ? children.Max(x => x.OrderIndex) + 1 : 0;
            targetVm.IsExpanded = true;
        }
        else if (dropPos == TreeDropPosition.Above)
        {
            // Insert before targetVm
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
        else if (dropPos == TreeDropPosition.Below)
        {
            // Insert after targetVm
            draggedVm.Item.ParentId = targetVm.Item.ParentId;
            var siblings = _items.Where(x => x.ParentId == targetVm.Item.ParentId && x.Id != draggedVm.Item.Id)
                                 .OrderBy(x => x.OrderIndex)
                                 .ToList();
            int targetIdx = siblings.IndexOf(targetVm.Item);
            if (targetIdx >= 0)
            {
                siblings.Insert(targetIdx + 1, draggedVm.Item);
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
            StatusText.Text = $"Moved '{draggedVm.Name}'.";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save reordered items");
        }

        e.Handled = true;
    }

    private static TreeViewItem? FindTreeViewItemUnderMouse(DependencyObject? source)
    {
        while (source != null && source is not TreeViewItem)
        {
            source = VisualTreeHelper.GetParent(source);
        }
        return source as TreeViewItem;
    }

    private TriggerTreeItemViewModel? FindTreeItemViewModelUnderMouse(DependencyObject? source)
    {
        var tvi = FindTreeViewItemUnderMouse(source);
        return tvi?.DataContext as TriggerTreeItemViewModel;
    }

    private bool IsDescendant(TriggerItem item, Guid potentialAncestorId)
    {
        var currentParentId = item.ParentId;
        while (currentParentId != null)
        {
            if (currentParentId == potentialAncestorId) return true;
            var parent = _items.FirstOrDefault(x => x.Id == currentParentId);
            currentParentId = parent?.ParentId;
        }
        return false;
    }

    // Window Target Crosshair Tool (Spy++ style)
    private void CrosshairTarget_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        StartWindowCapture("Shell");
        e.Handled = true;
    }

    private void AddProcessMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var tag = btn.Tag?.ToString() ?? "Allowed";

        var cm = new ContextMenu();

        var targetItem = new MenuItem
        {
            Header = "🎯 Target Window with Crosshair",
            ToolTip = "Click or drag over any open application window to add its process name"
        };
        targetItem.Click += (s, args) =>
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                StartWindowCapture(tag == "Excluded" ? "ExcludedProcess" : "AllowedProcess");
            }));
        };

        var runningItem = new MenuItem
        {
            Header = "📋 Pick from Running Applications...",
            ToolTip = "Select from currently running desktop processes"
        };
        runningItem.Click += (s, args) => PickRunningProcessForTag(tag);

        var fileItem = new MenuItem
        {
            Header = "📁 Browse Executable File...",
            ToolTip = "Select an executable file (.exe) from disk"
        };
        fileItem.Click += (s, args) => BrowseExecutableForTag(tag);

        cm.Items.Add(targetItem);
        cm.Items.Add(runningItem);
        cm.Items.Add(fileItem);

        cm.PlacementTarget = btn;
        cm.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        cm.IsOpen = true;
    }

    private void PickRunningProcessForTag(string tag)
    {
        string? proc = ProcessPickerDialog.PickProcess(this);
        if (!string.IsNullOrWhiteSpace(proc))
        {
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

    private void BrowseExecutableForTag(string tag)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select Application Executable or Shortcut",
            Filter = "Applications & Shortcuts (*.exe;*.lnk)|*.exe;*.lnk|Applications (*.exe)|*.exe|Shortcuts (*.lnk)|*.lnk|All Files (*.*)|*.*",
            InitialDirectory = GetInitialExecutableDirectory()
        };
        if (dlg.ShowDialog(this) == true)
        {
            _lastBrowsedExecutableDirectory = Path.GetDirectoryName(dlg.FileName);

            string targetPath = dlg.FileName;
            if (dlg.FileName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var resolved = ShellLinkScanner.ResolveShortcut(dlg.FileName);
                if (resolved.HasValue && !string.IsNullOrWhiteSpace(resolved.Value.TargetPath))
                {
                    targetPath = resolved.Value.TargetPath;
                }
            }

            string procName = Path.GetFileName(targetPath);
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

    private void ProcessCrosshair_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var tag = (sender as FrameworkElement)?.Tag?.ToString() ?? "Allowed";
        string mode = tag switch
        {
            "Excluded" => "ExcludedProcess",
            "AllowedUrl" => "AllowedUrl",
            "ExcludedUrl" => "ExcludedUrl",
            _ => "AllowedProcess"
        };
        StartWindowCapture(mode);
        e.Handled = true;
    }

    private void BrowseRunningProcess_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as FrameworkElement)?.Tag?.ToString() ?? "Allowed";
        PickRunningProcessForTag(tag);
    }

    private void BrowseFileProcess_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as FrameworkElement)?.Tag?.ToString() ?? "Allowed";
        BrowseExecutableForTag(tag);
    }

    private void StartWindowCapture(string mode)
    {
        _windowTargetMode = mode;
        string prompt = mode switch
        {
            "AllowedUrl" => "Click browser tab or window to capture into Allowed URLs (Esc to cancel)",
            "ExcludedUrl" => "Click browser tab or window to capture into Excluded URLs (Esc to cancel)",
            "Shell" => "Click application window to capture executable path (Esc to cancel)",
            "ExcludedProcess" => "Click application window to exclude (Esc to cancel)",
            _ => "Click application window to select (Esc to cancel)"
        };

        bool hideWindow = _appSettings?.HideOnTargetWindow ?? true;

        if (WindowTargetingOverlay.TryTargetWindow(this, prompt, hideWindow, out IntPtr targetHwnd, out var pt))
        {
            CompleteWindowCaptureFromTarget(targetHwnd, pt, mode);
        }
        else
        {
            StatusText.Text = "Target window capture cancelled.";
        }
    }

    private void CompleteWindowCaptureFromTarget(IntPtr targetHwnd, NativeMethods.POINT pt, string mode)
    {
        try
        {
            if (targetHwnd == IntPtr.Zero)
            {
                StatusText.Text = "No window found under crosshair.";
                return;
            }

            IntPtr rootHwnd = NativeMethods.GetAncestor(targetHwnd, NativeMethods.GA_ROOT);
            if (rootHwnd == IntPtr.Zero) rootHwnd = targetHwnd;

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
            else if (_windowTargetMode == "AllowedUrl" || _windowTargetMode == "ExcludedUrl")
            {
                var targetTagInput = _windowTargetMode == "ExcludedUrl" 
                    ? ExcludedUrlsTagInput 
                    : AllowedUrlsTagInput;
                var listName = _windowTargetMode == "ExcludedUrl" ? "Excluded" : "Allowed";

                string? url = null;
                if (_contextFilterService != null)
                {
                    url = _contextFilterService.GetActiveBrowserUrl(rootHwnd, exeName);
                }

                if (!string.IsNullOrWhiteSpace(url))
                {
                    targetTagInput.AddTag(url);
                    CommitCurrentFormChanges();
                    StatusText.Text = $"🎯 Captured browser tab URL into {listName} URLs: {url}";
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
                            targetTagInput.AddTag("*" + t + "*");
                            CommitCurrentFormChanges();
                            StatusText.Text = $"🎯 Captured window title pattern into {listName} URLs: *{t}*";
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

    #region Browser URL Section Expander & Auto-Expand Logic

    private void BrowserUrlSectionHeader_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ToggleBrowserUrlSection();
    }

    private void ToggleBrowserUrlSection()
    {
        bool isCurrentlyExpanded = BrowserUrlSectionContent?.Visibility == Visibility.Visible;
        SetBrowserUrlSectionExpanded(!isCurrentlyExpanded);
    }

    private void SetBrowserUrlSectionExpanded(bool expanded)
    {
        if (BrowserUrlSectionContent == null || BrowserUrlSectionChevron == null) return;

        BrowserUrlSectionContent.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        BrowserUrlSectionChevron.Text = expanded ? "▼" : "▶";
        UpdateUrlRulesBadge();
    }

    private void UpdateUrlRulesBadge()
    {
        if (UrlRulesActiveBadge == null || UrlRulesActiveBadgeText == null) return;

        bool isExpanded = BrowserUrlSectionContent?.Visibility == Visibility.Visible;
        int allowedCount = AllowedUrlsTagInput?.GetTags().Count ?? 0;
        int excludedCount = ExcludedUrlsTagInput?.GetTags().Count ?? 0;
        int totalRules = allowedCount + excludedCount;

        if (!isExpanded && totalRules > 0)
        {
            UrlRulesActiveBadgeText.Text = totalRules == 1 ? "1 rule" : $"{totalRules} rules";
            UrlRulesActiveBadge.Visibility = Visibility.Visible;
        }
        else
        {
            UrlRulesActiveBadge.Visibility = Visibility.Collapsed;
        }
    }

    private void CheckAutoExpandBrowserUrlSection()
    {
        // Auto-expand if URL rules exist or if a browser application is in AllowedProcesses
        bool hasUrlRules = (AllowedUrlsTagInput?.GetTags().Count ?? 0) > 0 || 
                           (ExcludedUrlsTagInput?.GetTags().Count ?? 0) > 0;
        bool hasBrowser = AllowedProcessesTagInput?.GetTags().Any(ContextFilter.IsKnownBrowser) ?? false;

        if (hasUrlRules || hasBrowser)
        {
            SetBrowserUrlSectionExpanded(true);
        }
        else
        {
            UpdateUrlRulesBadge();
        }
    }

    #endregion

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (!PromptSaveIfDirty())
        {
            e.Cancel = true;
            return;
        }

        // Persist folder expansion states across launches
        _ = _repository.SaveAsync(_items);

        if (!IsExiting)
        {
            // Minimize/Hide to tray instead of closing
            e.Cancel = true;
            Hide();
        }
    }
}
