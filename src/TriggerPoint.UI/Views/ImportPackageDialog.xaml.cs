using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TriggerPoint.Core.Models;

namespace TriggerPoint.UI.Views;

public enum ImportResolutionChoice
{
    KeepBoth,
    Overwrite,
    Skip
}

public sealed class ImportReviewItemViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public TriggerItem IncomingItem { get; }
    public TriggerItem? ConflictingExistingItem { get; set; }
    public string Name => IncomingItem.Name;
    public ActionType ActionType => IncomingItem.ActionType;
    public string ActionTypeText => IncomingItem.ActionType.ToString();

    public string IconSymbol => IncomingItem.ActionType switch
    {
        ActionType.Folder => "📁",
        ActionType.Snippet => "⚡",
        ActionType.Workflow => "⚡",
        ActionType.Macro => "🔴",
        ActionType.Service => "⚙",
        _ => "▶"
    };

    public Brush IconBrush => IncomingItem.ActionType switch
    {
        ActionType.Folder => Application.Current.TryFindResource("FolderBrush") as Brush ?? Brushes.Goldenrod,
        ActionType.Snippet => Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue,
        ActionType.Workflow => Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue,
        ActionType.Macro => Brushes.Crimson,
        ActionType.Service => Brushes.DarkOrange,
        _ => Application.Current.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray
    };

    public string TriggerText
    {
        get
        {
            if (IncomingItem.ActionType == ActionType.Snippet && !string.IsNullOrWhiteSpace(IncomingItem.Abbreviation))
            {
                return IncomingItem.Abbreviation;
            }
            if (IncomingItem.Hotkey != null && !IncomingItem.Hotkey.IsEmpty)
            {
                return IncomingItem.Hotkey.DisplayText;
            }
            return "-";
        }
    }

    public bool HasConflict { get; set; }
    public string StatusText { get; set; } = "✨ New";
    public string ConflictDescription { get; set; } = "No collision detected. Ready to import.";

    public Brush StatusForegroundBrush => HasConflict
        ? Application.Current.TryFindResource("WarningBrush") as Brush ?? Brushes.DarkOrange
        : Application.Current.TryFindResource("SuccessBrush") as Brush ?? Brushes.Green;

    public Brush StatusBorderBrush => HasConflict
        ? Application.Current.TryFindResource("WarningBrush") as Brush ?? Brushes.DarkOrange
        : Application.Current.TryFindResource("SuccessBrush") as Brush ?? Brushes.Green;

    public Brush StatusBackgroundBrush => HasConflict
        ? Application.Current.TryFindResource("WarningSubtleBrush") as Brush ?? Brushes.FloralWhite
        : Application.Current.TryFindResource("SuccessSubtleBrush") as Brush ?? Brushes.Honeydew;

    private int _selectedResolutionIndex = 0;
    public int SelectedResolutionIndex
    {
        get => _selectedResolutionIndex;
        set
        {
            if (_selectedResolutionIndex != value)
            {
                _selectedResolutionIndex = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedResolutionIndex)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ResolutionChoice)));
            }
        }
    }

    public ImportResolutionChoice ResolutionChoice => SelectedResolutionIndex switch
    {
        1 => ImportResolutionChoice.Overwrite,
        2 => ImportResolutionChoice.Skip,
        _ => ImportResolutionChoice.KeepBoth
    };

    public ImportReviewItemViewModel(TriggerItem item)
    {
        IncomingItem = item;
    }
}

public sealed class DestinationFolderOption
{
    public Guid? FolderId { get; set; }
    public string DisplayName { get; set; } = "📁 Root";

    public override string ToString() => DisplayName;
}

public partial class ImportPackageDialog : Window
{
    private readonly ConfigurationBackupPackage _package;
    private readonly List<TriggerItem> _existingItems;
    private readonly List<ImportReviewItemViewModel> _reviewItems = [];

    public List<TriggerItem> FinalItems { get; private set; } = [];
    public int ImportedCount { get; private set; }

    public ImportPackageDialog(
        ConfigurationBackupPackage package,
        IEnumerable<TriggerItem> currentItems,
        Guid? defaultDestinationFolderId = null)
    {
        InitializeComponent();
        _package = package;
        _existingItems = currentItems.Select(x => x.Clone()).ToList();

        PopulateHeader();
        PopulateDestinationFolders(defaultDestinationFolderId);
        AnalyzeConflicts();
        ReviewListView.ItemsSource = _reviewItems;
        UpdateSummaryFooter();
    }

    private void PopulateHeader()
    {
        PackageScopeText.Text = string.IsNullOrWhiteSpace(_package.ScopeName)
            ? "Package: TriggerPoint Archive"
            : $"Package: {_package.ScopeName}";

        SanitizedBadge.Visibility = _package.IsSanitized ? Visibility.Visible : Visibility.Collapsed;

        int folders = _package.Items.Count(x => x.ActionType == ActionType.Folder);
        int actions = _package.Items.Count(x => x.ActionType != ActionType.Folder);
        PackageMetaText.Text = $"Exported: {_package.ExportedAt.ToLocalTime():MMM dd, yyyy HH:mm} • {folders} folder(s), {actions} action(s) • App v{_package.AppVersion}";

        if (!string.IsNullOrWhiteSpace(_package.AuthorNotes))
        {
            PackageNotesText.Text = $"Notes: {_package.AuthorNotes}";
            PackageNotesText.Visibility = Visibility.Visible;
        }
    }

    private void PopulateDestinationFolders(Guid? defaultDestinationFolderId)
    {
        var options = new List<DestinationFolderOption>
        {
            new() { FolderId = null, DisplayName = "📁 Root (Top-Level)" }
        };

        var folders = _existingItems.Where(x => x.ActionType == ActionType.Folder).ToList();
        AddFolderOptionsRecursive(options, folders, null, 1);

        DestinationFolderCombo.ItemsSource = options;
        var selected = options.FirstOrDefault(x => x.FolderId == defaultDestinationFolderId) ?? options[0];
        DestinationFolderCombo.SelectedItem = selected;

        DestinationFolderCombo.SelectionChanged += (s, e) =>
        {
            AnalyzeConflicts();
            UpdateSummaryFooter();
        };
    }

    private static void AddFolderOptionsRecursive(
        List<DestinationFolderOption> options,
        List<TriggerItem> folders,
        Guid? parentId,
        int depth)
    {
        string indent = new(' ', depth * 4);
        var children = folders.Where(x => x.ParentId == parentId).OrderBy(x => x.OrderIndex);

        foreach (var f in children)
        {
            options.Add(new DestinationFolderOption
            {
                FolderId = f.Id,
                DisplayName = $"{indent}📁 {f.Name}"
            });
            AddFolderOptionsRecursive(options, folders, f.Id, depth + 1);
        }
    }

    private void AnalyzeConflicts()
    {
        _reviewItems.Clear();
        var selectedFolder = DestinationFolderCombo.SelectedItem as DestinationFolderOption;
        Guid? targetFolderId = selectedFolder?.FolderId;

        int conflictCount = 0;

        foreach (var incoming in _package.Items)
        {
            var vm = new ImportReviewItemViewModel(incoming);

            // 1. Same ID conflict
            var sameId = _existingItems.FirstOrDefault(x => x.Id == incoming.Id);
            if (sameId != null)
            {
                vm.HasConflict = true;
                vm.ConflictingExistingItem = sameId;
                vm.StatusText = "🔄 Same ID Collision";
                vm.ConflictDescription = $"An item with ID '{incoming.Id}' already exists: '{sameId.Name}'.";
                conflictCount++;
                _reviewItems.Add(vm);
                continue;
            }

            // 2. Hotkey conflict
            if (incoming.Hotkey != null && !incoming.Hotkey.IsEmpty)
            {
                var hotkeyMatch = _existingItems.FirstOrDefault(x => x.Hotkey != null && x.Hotkey == incoming.Hotkey);
                if (hotkeyMatch != null)
                {
                    vm.HasConflict = true;
                    vm.ConflictingExistingItem = hotkeyMatch;
                    vm.StatusText = "⚠ Hotkey Collision";
                    vm.ConflictDescription = $"Hotkey '{incoming.Hotkey.DisplayText}' is already bound to '{hotkeyMatch.Name}'.";
                    conflictCount++;
                    _reviewItems.Add(vm);
                    continue;
                }
            }

            // 3. Abbreviation conflict
            if (incoming.ActionType == ActionType.Snippet && !string.IsNullOrWhiteSpace(incoming.Abbreviation))
            {
                var abbrMatch = _existingItems.FirstOrDefault(x =>
                    x.ActionType == ActionType.Snippet &&
                    !string.IsNullOrWhiteSpace(x.Abbreviation) &&
                    string.Equals(x.Abbreviation.Trim(), incoming.Abbreviation.Trim(), StringComparison.OrdinalIgnoreCase));

                if (abbrMatch != null)
                {
                    vm.HasConflict = true;
                    vm.ConflictingExistingItem = abbrMatch;
                    vm.StatusText = "⚡ Abbr Collision";
                    vm.ConflictDescription = $"Abbreviation '{incoming.Abbreviation}' is already in use by snippet '{abbrMatch.Name}'.";
                    conflictCount++;
                    _reviewItems.Add(vm);
                    continue;
                }
            }

            // 4. Same name in destination folder
            var sameName = _existingItems.FirstOrDefault(x =>
                x.ParentId == targetFolderId &&
                string.Equals(x.Name.Trim(), incoming.Name.Trim(), StringComparison.OrdinalIgnoreCase));

            if (sameName != null)
            {
                vm.HasConflict = true;
                vm.ConflictingExistingItem = sameName;
                vm.StatusText = "🔄 Name Conflict";
                vm.ConflictDescription = $"An action named '{incoming.Name}' already exists in target destination.";
                conflictCount++;
                _reviewItems.Add(vm);
                continue;
            }

            // No conflict
            vm.HasConflict = false;
            vm.StatusText = "✨ New";
            vm.ConflictDescription = "No collision detected. Ready to import.";
            _reviewItems.Add(vm);
        }

        ReviewListView.Items.Refresh();

        if (conflictCount > 0)
        {
            ConflictSummaryBadge.Text = $"⚠ {conflictCount} collision(s) detected";
            ConflictSummaryBadge.Foreground = Application.Current.TryFindResource("WarningBrush") as Brush ?? Brushes.DarkOrange;
        }
        else
        {
            ConflictSummaryBadge.Text = "✓ No collisions detected";
            ConflictSummaryBadge.Foreground = Application.Current.TryFindResource("SuccessBrush") as Brush ?? Brushes.Green;
        }
    }

    private void UpdateSummaryFooter()
    {
        int keepCount = _reviewItems.Count(x => x.ResolutionChoice == ImportResolutionChoice.KeepBoth);
        int overwriteCount = _reviewItems.Count(x => x.ResolutionChoice == ImportResolutionChoice.Overwrite);
        int skipCount = _reviewItems.Count(x => x.ResolutionChoice == ImportResolutionChoice.Skip);

        int totalImporting = keepCount + overwriteCount;
        ImportSummaryFooterText.Text = $"Will import {totalImporting} item(s) ({keepCount} new/cloned, {overwriteCount} overwrite, {skipCount} skipped).";
        ConfirmImportBtn.IsEnabled = totalImporting > 0;
    }

    private void ResolutionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSummaryFooter();
    }

    private void ResolveKeepBothAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _reviewItems)
        {
            item.SelectedResolutionIndex = 0; // KeepBoth
        }
        UpdateSummaryFooter();
    }

    private void ResolveOverwriteAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _reviewItems)
        {
            if (item.HasConflict)
            {
                item.SelectedResolutionIndex = 1; // Overwrite
            }
        }
        UpdateSummaryFooter();
    }

    private void ResolveSkipAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _reviewItems)
        {
            if (item.HasConflict)
            {
                item.SelectedResolutionIndex = 2; // Skip
            }
        }
        UpdateSummaryFooter();
    }

    private void ConfirmImportBtn_Click(object sender, RoutedEventArgs e)
    {
        var selectedFolder = DestinationFolderCombo.SelectedItem as DestinationFolderOption;
        Guid? targetDestinationFolderId = selectedFolder?.FolderId;

        var workingItems = _existingItems.Select(x => x.Clone()).ToList();
        var incomingItemsToKeep = new List<TriggerItem>();
        var idRemap = new Dictionary<Guid, Guid>();

        // Pre-generate new IDs for incoming items being kept as duplicate
        foreach (var vm in _reviewItems)
        {
            if (vm.ResolutionChoice == ImportResolutionChoice.KeepBoth)
            {
                idRemap[vm.IncomingItem.Id] = Guid.NewGuid();
            }
        }

        int count = 0;
        foreach (var vm in _reviewItems)
        {
            if (vm.ResolutionChoice == ImportResolutionChoice.Skip)
            {
                continue;
            }

            if (vm.ResolutionChoice == ImportResolutionChoice.Overwrite && vm.ConflictingExistingItem != null)
            {
                // Replace existing item payload & details in place, keeping existing item's ID & ParentId
                var target = workingItems.FirstOrDefault(x => x.Id == vm.ConflictingExistingItem.Id);
                if (target != null)
                {
                    target.Name = vm.IncomingItem.Name;
                    target.Description = vm.IncomingItem.Description;
                    target.ActionType = vm.IncomingItem.ActionType;
                    target.Payload = vm.IncomingItem.Payload.Clone();
                    target.Hotkey = vm.IncomingItem.Hotkey != null
                        ? new ShortcutBinding(vm.IncomingItem.Hotkey.Modifiers, vm.IncomingItem.Hotkey.VirtualKey, vm.IncomingItem.Hotkey.KeyName)
                        : null;
                    target.Abbreviation = vm.IncomingItem.Abbreviation;
                    target.AbbreviationMode = vm.IncomingItem.AbbreviationMode;
                    target.AbbreviationCaseSensitive = vm.IncomingItem.AbbreviationCaseSensitive;
                    target.ContextFilter = vm.IncomingItem.ContextFilter;
                    count++;
                }
                continue;
            }

            // Keep Both
            var clone = vm.IncomingItem.Clone();
            clone.Id = idRemap[vm.IncomingItem.Id];

            // Resolve parent hierarchy
            if (clone.ParentId.HasValue && idRemap.TryGetValue(clone.ParentId.Value, out var newParentGuid))
            {
                clone.ParentId = newParentGuid;
            }
            else
            {
                clone.ParentId = targetDestinationFolderId;
            }

            // If name or shortcut collided, make clone safe
            if (vm.HasConflict)
            {
                if (vm.StatusText.Contains("Name") || vm.StatusText.Contains("Same ID"))
                {
                    clone.Name = $"{clone.Name} (Imported)";
                }

                // Per AGENTS.md rule 2: Clear conflicting hotkey on cloned/duplicated items to prevent immediate collisions
                if (vm.StatusText.Contains("Hotkey"))
                {
                    clone.Hotkey = null;
                }

                // Clear abbreviation if it directly collides
                if (vm.StatusText.Contains("Abbr"))
                {
                    clone.Abbreviation = null;
                }
            }

            clone.OrderIndex = workingItems.Count;
            workingItems.Add(clone);
            count++;
        }

        FinalItems = workingItems;
        ImportedCount = count;

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
        }
    }
}
