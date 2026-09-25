using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;
using TriggerPoint.Infrastructure.Win32;
using ModifierKeys = System.Windows.Input.ModifierKeys;

namespace TriggerPoint.UI.Views;

public record HighlightSegment(string Text, bool IsMatched);
public record PrefixSuggestionItem(string Prefix, string Title, string ShortcutHint, string Icon, CommandPaletteFilterType FilterType);

public static class TextBlockInlines
{
    public static readonly DependencyProperty SegmentsProperty =
        DependencyProperty.RegisterAttached(
            "Segments",
            typeof(IReadOnlyList<HighlightSegment>),
            typeof(TextBlockInlines),
            new PropertyMetadata(null, OnSegmentsChanged));

    public static IReadOnlyList<HighlightSegment>? GetSegments(DependencyObject obj) =>
        (IReadOnlyList<HighlightSegment>?)obj.GetValue(SegmentsProperty);

    public static void SetSegments(DependencyObject obj, IReadOnlyList<HighlightSegment>? value) =>
        obj.SetValue(SegmentsProperty, value);

    private static void OnSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock tb)
        {
            tb.Inlines.Clear();
            if (e.NewValue is IReadOnlyList<HighlightSegment> segments)
            {
                var accentBrush = Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.CornflowerBlue;
                foreach (var seg in segments)
                {
                    var run = new Run(seg.Text);
                    if (seg.IsMatched)
                    {
                        run.FontWeight = FontWeights.Bold;
                        run.Foreground = accentBrush;
                    }
                    tb.Inlines.Add(run);
                }
            }
        }
    }
}

public class PaletteItemViewModel
{
    public TriggerItem Item { get; }
    public FuzzyMatchResult? MatchResult { get; }
    public string? ParentPath { get; }

    public bool IsSectionHeader { get; }
    public string SectionHeaderText { get; }
    public Thickness HeaderBorderThickness { get; }
    public bool IsSelectable => !IsSectionHeader;
    public Visibility HeaderVisibility => IsSectionHeader ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ActionVisibility => IsSelectable ? Visibility.Visible : Visibility.Collapsed;
    public bool IsSystemAction => Item != null && (Item.Id == App.OpenSettingsActionId || Item.Id == CommandPaletteView.AppSettingsVirtualId);
    public bool IsCalculatorResult { get; init; }
    public CalculatorResult? CalcResult { get; init; }

    public IReadOnlyList<AlternativeMeasurement> CommonAlternatives =>
        CalcResult?.Alternatives?.Where(a => a.IsCommon).Take(4).ToList() ?? (IReadOnlyList<AlternativeMeasurement>)Array.Empty<AlternativeMeasurement>();

    public string? HierarchicalBreakdown => CalcResult?.HierarchicalBreakdown;
    public string? CumulativeBreakdown => CalcResult?.CumulativeBreakdown;

    public bool HasAlternatives => CalcResult?.Alternatives != null && CalcResult.Alternatives.Count > 0;
    public Visibility AlternativesVisibility => (IsCalculatorResult && HasAlternatives) ? Visibility.Visible : Visibility.Collapsed;
    public Cursor SubtitleCursor => (IsCalculatorResult && HasAlternatives) ? Cursors.Hand : Cursors.Arrow;
    public string? SubtitleTooltip => (IsCalculatorResult && HasAlternatives) ? "Click to view and choose other measurement units" : null;

    public string Name => Item?.Name ?? string.Empty;
    public string Description => Item?.Description ?? string.Empty;

    public string SecondaryDetail
    {
        get
        {
            if (IsSectionHeader) return string.Empty;
            if (!string.IsNullOrWhiteSpace(Description)) return Description;

            if (!string.IsNullOrWhiteSpace(ParentPath))
                return $"📁 {ParentPath}";

            if (Item.ActionType == ActionType.Shell && !string.IsNullOrWhiteSpace(Item.Payload.Command))
                return Item.Payload.Command;

            if (Item.ActionType == ActionType.Snippet && !string.IsNullOrWhiteSpace(Item.Payload.SnippetTemplate))
            {
                var clean = Item.Payload.SnippetTemplate.Replace("\r", " ").Replace("\n", " ").Trim();
                return clean.Length > 60 ? clean.Substring(0, 57) + "..." : clean;
            }

            if (Item.ActionType == ActionType.Workflow)
            {
                int count = Item.Payload.WorkflowSteps?.Count ?? 0;
                return $"{count} automated {(count == 1 ? "step" : "steps")}";
            }

            return string.Empty;
        }
    }

    public Visibility HasSecondaryDetail => !string.IsNullOrWhiteSpace(SecondaryDetail) ? Visibility.Visible : Visibility.Collapsed;

    public string HotkeyText => Item?.Hotkey?.DisplayText ?? string.Empty;
    public Visibility HasHotkey => (!IsSectionHeader && !string.IsNullOrWhiteSpace(HotkeyText)) ? Visibility.Visible : Visibility.Collapsed;

    public string FolderBreadcrumb => (!IsSectionHeader && !string.IsNullOrWhiteSpace(ParentPath)) ? $"📁 {ParentPath}" : string.Empty;
    public Visibility HasFolderBreadcrumb => !string.IsNullOrWhiteSpace(FolderBreadcrumb) ? Visibility.Visible : Visibility.Collapsed;

    public string UsageRecencyText
    {
        get
        {
            if (IsSectionHeader || IsSystemAction || Item == null) return string.Empty;
            if (Item.UsageStats.LaunchCount <= 0 && !Item.UsageStats.LastExecutedUtc.HasValue) return string.Empty;

            long count = Item.UsageStats.LaunchCount;
            if (Item.UsageStats.LastExecutedUtc.HasValue)
            {
                string rel = FormatRelativeTime(Item.UsageStats.LastExecutedUtc.Value);
                return count > 0 ? $"⚡ {count} • {rel}" : rel;
            }
            return $"⚡ {count}";
        }
    }

    public Visibility HasUsageRecency => !string.IsNullOrWhiteSpace(UsageRecencyText) ? Visibility.Visible : Visibility.Collapsed;

    public string TypeText
    {
        get
        {
            if (IsSectionHeader) return string.Empty;
            if (IsCalculatorResult) return "CALC";
            if (IsSystemAction) return "SYSTEM";
            return Item?.ActionType switch
            {
                ActionType.Shell => "APP & COMMAND",
                ActionType.Snippet => "SNIPPET",
                ActionType.Workflow => "WORKFLOW",
                ActionType.Folder => "FOLDER",
                _ => "ACTION"
            };
        }
    }

    public Visibility AdminBadgeVisibility => (!IsSectionHeader && !IsSystemAction && Item?.Payload?.RunAsAdmin == true) ? Visibility.Visible : Visibility.Collapsed;

    public Brush TypeForegroundBrush
    {
        get
        {
            if (IsCalculatorResult || IsSystemAction) return Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.CornflowerBlue;
            return Item?.ActionType switch
            {
                ActionType.Folder => Application.Current.TryFindResource("FolderBrush") as Brush ?? Brushes.SteelBlue,
                ActionType.Snippet => Application.Current.TryFindResource("SnippetBrush") as Brush ?? Brushes.MediumSeaGreen,
                ActionType.Shell => Application.Current.TryFindResource("ShellBrush") as Brush ?? Brushes.Goldenrod,
                ActionType.Workflow => Application.Current.TryFindResource("WorkflowBrush") as Brush ?? Brushes.MediumPurple,
                _ => Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.Gray
            };
        }
    }

    public Brush TypeBackgroundBrush
    {
        get
        {
            if (IsCalculatorResult || IsSystemAction) return Application.Current.TryFindResource("AccentSubtleBrush") as Brush ?? new SolidColorBrush(Color.FromArgb(0x18, 0x00, 0x7A, 0xCC));
            return Item?.ActionType switch
            {
                ActionType.Folder => Application.Current.TryFindResource("FolderSubtleBrush") as Brush ?? new SolidColorBrush(Color.FromArgb(0x18, 0x3B, 0x82, 0xF6)),
                ActionType.Snippet => Application.Current.TryFindResource("SnippetSubtleBrush") as Brush ?? new SolidColorBrush(Color.FromArgb(0x18, 0x10, 0xB9, 0x81)),
                ActionType.Shell => Application.Current.TryFindResource("ShellSubtleBrush") as Brush ?? new SolidColorBrush(Color.FromArgb(0x18, 0xF5, 0x9E, 0x0B)),
                ActionType.Workflow => Application.Current.TryFindResource("WorkflowSubtleBrush") as Brush ?? new SolidColorBrush(Color.FromArgb(0x18, 0x8B, 0x5C, 0xF6)),
                _ => Brushes.Transparent
            };
        }
    }

    public Brush TypeBorderBrush
    {
        get
        {
            if (IsSystemAction) return Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.CornflowerBlue;
            return Item?.ActionType switch
            {
                ActionType.Folder => Application.Current.TryFindResource("FolderBrush") as Brush ?? Brushes.SteelBlue,
                ActionType.Snippet => Application.Current.TryFindResource("SnippetBrush") as Brush ?? Brushes.MediumSeaGreen,
                ActionType.Shell => Application.Current.TryFindResource("ShellBrush") as Brush ?? Brushes.Goldenrod,
                ActionType.Workflow => Application.Current.TryFindResource("WorkflowBrush") as Brush ?? Brushes.MediumPurple,
                _ => Application.Current.TryFindResource("BorderSubtleBrush") as Brush ?? Brushes.Gray
            };
        }
    }

    public string IconSymbol
    {
        get
        {
            if (IsSectionHeader) return string.Empty;
            if (IsCalculatorResult) return "🧮";
            if (IsSystemAction)
            {
                return Item.Id == App.OpenSettingsActionId ? "🎯" : "⚙️";
            }
            return Item?.ActionType switch
            {
                ActionType.Folder => "📁",
                ActionType.Snippet => "📝",
                ActionType.Shell => "⚡",
                ActionType.Workflow => "🔀",
                _ => "▶"
            };
        }
    }

    public Brush IconBrush
    {
        get
        {
            if (IsSystemAction) return Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.CornflowerBlue;
            string key = Item?.ActionType switch
            {
                ActionType.Folder => "FolderBrush",
                ActionType.Shell => "ShellBrush",
                ActionType.Snippet => "SnippetBrush",
                ActionType.Workflow => "WorkflowBrush",
                _ => "AccentBrush"
            };
            return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
        }
    }

    public Visibility IsFolder => (Item != null && Item.ActionType == ActionType.Folder && !IsSectionHeader) ? Visibility.Visible : Visibility.Collapsed;

    public bool CanRevealInExplorer
    {
        get
        {
            if (IsSectionHeader || IsSystemAction || Item == null) return false;
            return ActionShortcutRelevanceHelper.CanRevealInExplorer(Item);
        }
    }

    public string DetailPreviewText
    {
        get
        {
            if (IsSectionHeader) return string.Empty;
            if (IsSystemAction) return Item.Description;

            if (Item.ActionType == ActionType.Snippet && !string.IsNullOrWhiteSpace(Item.Payload.SnippetTemplate))
            {
                var clean = Item.Payload.SnippetTemplate.Trim();
                var lines = clean.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                return string.Join(" • ", lines.Take(2));
            }

            if (Item.ActionType == ActionType.Shell)
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(Item.Payload.Command)) parts.Add(Item.Payload.Command);
                if (!string.IsNullOrWhiteSpace(Item.Payload.Arguments)) parts.Add(Item.Payload.Arguments);
                if (!string.IsNullOrWhiteSpace(Item.Payload.TargetDisplay) && Item.Payload.TargetDisplay != "default")
                {
                    parts.Add($"[Display: {Item.Payload.TargetDisplay}]");
                }
                return string.Join(" ", parts);
            }

            if (Item.ActionType == ActionType.Workflow)
            {
                var count = Item.Payload.WorkflowSteps?.Count ?? 0;
                return $"Workflow: {count} sequential step{(count == 1 ? "" : "s")}";
            }

            if (Item.ActionType == ActionType.Folder)
            {
                return !string.IsNullOrWhiteSpace(ParentPath) ? $"Folder inside {ParentPath}" : "Root Folder";
            }

            return Item.Description;
        }
    }

    public IReadOnlyList<HighlightSegment> HighlightedNameSegments { get; }

    public PaletteItemViewModel(TriggerItem item, FuzzyMatchResult? matchResult, string? parentPath = null)
    {
        Item = item;
        MatchResult = matchResult;
        ParentPath = parentPath;
        IsSectionHeader = false;
        SectionHeaderText = string.Empty;
        HeaderBorderThickness = new Thickness(0);
        HighlightedNameSegments = BuildHighlightedSegments(item.Name, matchResult?.MatchedIndices);
    }

    private PaletteItemViewModel(string sectionHeader, bool hasTopDivider)
    {
        Item = null!;
        MatchResult = null;
        ParentPath = null;
        IsSectionHeader = true;
        SectionHeaderText = sectionHeader;
        HeaderBorderThickness = hasTopDivider ? new Thickness(0, 1, 0, 0) : new Thickness(0);
        HighlightedNameSegments = [];
    }

    public static PaletteItemViewModel CreateSectionHeader(string title, bool hasTopDivider) =>
        new(title, hasTopDivider);

    private static IReadOnlyList<HighlightSegment> BuildHighlightedSegments(string name, IReadOnlyList<int>? matchedIndices)
    {
        if (string.IsNullOrEmpty(name)) return [];
        if (matchedIndices == null || matchedIndices.Count == 0)
        {
            return new[] { new HighlightSegment(name, false) };
        }

        var segments = new List<HighlightSegment>();
        var matchSet = new HashSet<int>(matchedIndices);
        int start = 0;
        bool inMatch = matchSet.Contains(0);

        for (int i = 1; i < name.Length; i++)
        {
            bool isCurrentMatch = matchSet.Contains(i);
            if (isCurrentMatch != inMatch)
            {
                segments.Add(new HighlightSegment(name.Substring(start, i - start), inMatch));
                start = i;
                inMatch = isCurrentMatch;
            }
        }

        if (start < name.Length)
        {
            segments.Add(new HighlightSegment(name.Substring(start), inMatch));
        }

        return segments;
    }

    private static string FormatRelativeTime(DateTime utc)
    {
        var diff = DateTime.UtcNow - utc;
        if (diff.TotalSeconds < 60) return "Just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
        return utc.ToLocalTime().ToString("MMM d");
    }
}

public sealed class FolderActionPreviewItem
{
    public TriggerItem Item { get; init; } = null!;
    public string Name => Item.Name;
    public string PathBreadcrumb { get; init; } = string.Empty;
    public Visibility BreadcrumbVisibility => !string.IsNullOrEmpty(PathBreadcrumb) ? Visibility.Visible : Visibility.Collapsed;
    public string IconEmoji => Item.ActionType switch
    {
        ActionType.Shell => "⚙️",
        ActionType.Snippet => "📝",
        ActionType.Workflow => "⚡",
        _ => "▶"
    };
    public string TypeBadgeText => Item.ActionType switch
    {
        ActionType.Shell => "APP",
        ActionType.Snippet => "SNIPPET",
        ActionType.Workflow => "WORKFLOW",
        _ => "ACTION"
    };
    public Brush TypeBadgeBg => Item.ActionType switch
    {
        ActionType.Shell => (Brush)Application.Current.TryFindResource("ShellSubtleBrush") ?? Brushes.LightSkyBlue,
        ActionType.Snippet => (Brush)Application.Current.TryFindResource("SnippetSubtleBrush") ?? Brushes.LightGreen,
        ActionType.Workflow => (Brush)Application.Current.TryFindResource("WorkflowSubtleBrush") ?? Brushes.MediumPurple,
        _ => Brushes.Transparent
    };
    public Brush TypeBadgeFg => Item.ActionType switch
    {
        ActionType.Shell => (Brush)Application.Current.TryFindResource("ShellBrush") ?? Brushes.SkyBlue,
        ActionType.Snippet => (Brush)Application.Current.TryFindResource("SnippetBrush") ?? Brushes.Green,
        ActionType.Workflow => (Brush)Application.Current.TryFindResource("WorkflowBrush") ?? Brushes.Purple,
        _ => (Brush)Application.Current.TryFindResource("TextPrimaryBrush") ?? Brushes.White
    };
}

public partial class CommandPaletteView : Window
{
    public static readonly Guid AppSettingsVirtualId = Guid.Parse("00000000-0000-0000-0000-000000000003");

    private readonly List<TriggerItem> _allItems;
    private readonly IActionExecutor _executor;
    private readonly IConfigRepository? _repository;
    private readonly IntPtr _targetHwnd;

    private Guid? _currentScopeFolderId;
    private string? _currentScopeFolderName;
    private readonly Dictionary<Guid, string> _folderPaths;

    private CommandPaletteFilterType _activeFilter = CommandPaletteFilterType.All;
    internal CommandPaletteFilterType ActiveFilter => _activeFilter;
    private CommandPaletteSortMode _currentSortMode = CommandPaletteSortMode.Smart;
    private readonly DispatcherTimer _feedbackTimer;
    private bool _isLoaded;

    private TriggerItem? _folderConfirmTargetFolder;
    private List<TriggerItem> _folderConfirmResolvedActions = [];

    private readonly TriggerItem _virtualActionManagerItem;
    private readonly TriggerItem _virtualAppSettingsItem;

    private static readonly IReadOnlyList<PrefixSuggestionItem> AllPrefixSuggestions =
    [
        new("@all", "All Actions", "Ctrl+1", "🔍", CommandPaletteFilterType.All),
        new("@app", "Apps & Commands", "Ctrl+2", "⚡", CommandPaletteFilterType.App),
        new("@snip", "Snippets & Templates", "Ctrl+3", "📝", CommandPaletteFilterType.Snippet),
        new("@flow", "Workflows", "Ctrl+4", "🔀", CommandPaletteFilterType.Workflow),
        new("@folder", "Folders", "Ctrl+5", "📁", CommandPaletteFilterType.Folder),
        new("@calc", "Calculator & Math", "=", "🧮", CommandPaletteFilterType.All),
        new("@current", "Current App Context", "", "🎯", CommandPaletteFilterType.All),
    ];

    public CommandPaletteView(
        IEnumerable<TriggerItem> items,
        IActionExecutor executor,
        Guid? scopedFolderId = null,
        string? scopedFolderName = null,
        IntPtr targetHwnd = default)
        : this(items, executor, (Application.Current as App)?.Repository, scopedFolderId, scopedFolderName, targetHwnd)
    {
    }

    public CommandPaletteView(
        IEnumerable<TriggerItem> items,
        IActionExecutor executor,
        IConfigRepository? repository,
        Guid? scopedFolderId = null,
        string? scopedFolderName = null,
        IntPtr targetHwnd = default)
    {
        InitializeComponent();
        _executor = executor;
        _repository = repository;
        _targetHwnd = targetHwnd;
        _currentScopeFolderId = scopedFolderId;
        _currentScopeFolderName = scopedFolderName;

        var itemsList = items.ToList();
        _folderPaths = BuildFolderPaths(itemsList);
        _allItems = itemsList;

        // Virtual system actions
        _virtualActionManagerItem = new TriggerItem
        {
            Id = App.OpenSettingsActionId,
            Name = "Open Action Manager",
            Description = "Manage triggers, action tree, folders, and workflows",
            ActionType = ActionType.Shell,
            Payload = new ActionPayload { Command = "Action Manager" }
        };

        _virtualAppSettingsItem = new TriggerItem
        {
            Id = AppSettingsVirtualId,
            Name = "Open Application Settings",
            Description = "Configure global shortcuts, theme, startup, and logging",
            ActionType = ActionType.Shell,
            Payload = new ActionPayload { Command = "Application Settings" }
        };

        _feedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _feedbackTimer.Tick += (s, e) =>
        {
            _feedbackTimer.Stop();
            FeedbackBadge.Visibility = Visibility.Collapsed;
        };

        UpdateScopeUi();
        LoadSortModePreference();

        if (App.LatestAvailableUpdate?.IsUpdateAvailable == true && App.LatestAvailableUpdate.LatestUpdate != null)
        {
            AppSettingsUpdateBadge.Visibility = Visibility.Visible;
            AppSettingsBtn.ToolTip = $"Application Settings (Update v{App.LatestAvailableUpdate.LatestUpdate.Version} available)";
        }

        Loaded += (s, e) =>
        {
            _isLoaded = true;
            try
            {
                var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                NativeMethods.SetForegroundWindow(handle);
            }
            catch { }

            SelectCalcGuideTab("Popular");
            SearchTextBox.Focus();
            Keyboard.Focus(SearchTextBox);
            FilterResults();
        };
    }

    private async void LoadSortModePreference()
    {
        if (_repository != null)
        {
            try
            {
                var settings = await _repository.LoadSettingsAsync();
                _currentSortMode = settings.CommandPaletteSortMode;
                UpdateSortButtonText();
                UpdateSortMenuCheckmarks();

                if (settings.EnableUiAnimations)
                {
                    var anim = new System.Windows.Media.Animation.DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(120));
                    BeginAnimation(OpacityProperty, anim);
                }

                FilterResults();
            }
            catch { }
        }
    }

    private static Dictionary<Guid, string> BuildFolderPaths(IEnumerable<TriggerItem> allItems)
    {
        var itemsList = allItems.ToList();
        var folderDict = itemsList.Where(x => x.ActionType == ActionType.Folder).ToDictionary(x => x.Id);
        var result = new Dictionary<Guid, string>();

        foreach (var folder in folderDict.Values)
        {
            var segments = new List<string>();
            var curr = folder;
            while (curr != null)
            {
                segments.Insert(0, curr.Name);
                curr = curr.ParentId.HasValue && folderDict.TryGetValue(curr.ParentId.Value, out var parent) ? parent : null;
            }
            result[folder.Id] = string.Join(" › ", segments);
        }

        return result;
    }

    private void UpdateScopeUi()
    {
        if (_currentScopeFolderId.HasValue)
        {
            ScopeBadge.Visibility = Visibility.Visible;
            ScopeText.Text = _currentScopeFolderName ?? "Scoped";
            BackButton.Visibility = Visibility.Visible;
        }
        else
        {
            ScopeBadge.Visibility = Visibility.Collapsed;
            BackButton.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateSortButtonText()
    {
        SortModeText.Text = _currentSortMode switch
        {
            CommandPaletteSortMode.Alphabetical => "A–Z",
            CommandPaletteSortMode.MostFrequent => "Frequent",
            CommandPaletteSortMode.Recent => "Recent",
            CommandPaletteSortMode.ActionTree => "Tree",
            _ => "Smart"
        };
    }

    public static List<TriggerItem> OrderByActionTree(IEnumerable<TriggerItem> items, Guid? rootScopeId = null)
    {
        var list = items.ToList();
        var foldersByParent = list.Where(x => x.ActionType == ActionType.Folder)
                                  .ToLookup(x => x.ParentId);

        var actionsByParent = list.Where(x => x.ActionType != ActionType.Folder)
                                  .ToLookup(x => x.ParentId);

        var result = new List<TriggerItem>();

        void Traverse(Guid? parentId)
        {
            // 1. Child folders ordered by OrderIndex
            foreach (var folder in foldersByParent[parentId].OrderBy(x => x.OrderIndex))
            {
                result.Add(folder);
                Traverse(folder.Id);
            }

            // 2. Child actions ordered by OrderIndex
            foreach (var action in actionsByParent[parentId].OrderBy(x => x.OrderIndex))
            {
                result.Add(action);
            }
        }

        Traverse(rootScopeId);

        var addedIds = new HashSet<Guid>(result.Select(x => x.Id));
        foreach (var item in list)
        {
            if (addedIds.Add(item.Id))
            {
                result.Add(item);
            }
        }

        return result;
    }

    public static List<PrefixSuggestionItem> GetPrefixSuggestions(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith("@") && !trimmed.Contains(' '))
        {
            return AllPrefixSuggestions
                .Where(s => s.Prefix.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase) ||
                            s.Title.Contains(trimmed.TrimStart('@'), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        return [];
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchWatermark.Visibility = string.IsNullOrEmpty(SearchTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;

        var matches = GetPrefixSuggestions(SearchTextBox.Text);
        if (matches.Count > 0)
        {
            PrefixSuggestionsList.ItemsSource = matches;
            PrefixSuggestionsList.SelectedIndex = 0;
            PrefixAutoCompletePopup.IsOpen = true;
        }
        else
        {
            PrefixAutoCompletePopup.IsOpen = false;
        }

        FilterResults();
    }

    private void ApplyPrefixSuggestion(PrefixSuggestionItem suggestion)
    {
        PrefixAutoCompletePopup.IsOpen = false;
        if (suggestion.Prefix.Equals("@calc", StringComparison.OrdinalIgnoreCase))
        {
            SearchTextBox.Text = "= ";
            SearchTextBox.CaretIndex = 2;
            SearchTextBox.Focus();
            return;
        }

        if (suggestion.Prefix.Equals("@current", StringComparison.OrdinalIgnoreCase))
        {
            SearchTextBox.Text = "@current ";
            SearchTextBox.CaretIndex = 9;
            SearchTextBox.Focus();
            return;
        }

        _activeFilter = suggestion.FilterType;
        UpdateFilterPillsUi();
        SearchTextBox.Text = string.Empty;
        SearchTextBox.Focus();
        FilterResults();
    }

    private void PrefixSuggestionsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var dep = e.OriginalSource as DependencyObject;
        while (dep != null && dep is not ListBoxItem && dep != PrefixSuggestionsList)
        {
            dep = VisualTreeHelper.GetParent(dep);
        }

        if (dep is ListBoxItem lbi && lbi.DataContext is PrefixSuggestionItem item)
        {
            ApplyPrefixSuggestion(item);
            e.Handled = true;
            return;
        }

        if (PrefixSuggestionsList.SelectedItem is PrefixSuggestionItem sel)
        {
            ApplyPrefixSuggestion(sel);
            e.Handled = true;
        }
    }

    private void FilterResults()
    {
        var rawQuery = SearchTextBox.Text.Trim();
        var effectiveQuery = rawQuery;

        bool filterCurrentContext = false;
        if (rawQuery.StartsWith("@current", StringComparison.OrdinalIgnoreCase))
        {
            filterCurrentContext = true;
            int spaceIdx = rawQuery.IndexOf(' ');
            effectiveQuery = spaceIdx >= 0 ? rawQuery.Substring(spaceIdx).Trim() : string.Empty;
        }

        // Auto-detect and trim prefixes
        if (rawQuery.StartsWith("@app", StringComparison.OrdinalIgnoreCase) || rawQuery.StartsWith("@cmd", StringComparison.OrdinalIgnoreCase))
        {
            _activeFilter = CommandPaletteFilterType.App;
            int spaceIdx = rawQuery.IndexOf(' ');
            effectiveQuery = spaceIdx >= 0 ? rawQuery.Substring(spaceIdx).Trim() : string.Empty;
        }
        else if (rawQuery.StartsWith("@snip", StringComparison.OrdinalIgnoreCase) || rawQuery.StartsWith("@text", StringComparison.OrdinalIgnoreCase))
        {
            _activeFilter = CommandPaletteFilterType.Snippet;
            int spaceIdx = rawQuery.IndexOf(' ');
            effectiveQuery = spaceIdx >= 0 ? rawQuery.Substring(spaceIdx).Trim() : string.Empty;
        }
        else if (rawQuery.StartsWith("@flow", StringComparison.OrdinalIgnoreCase) || rawQuery.StartsWith("@wf", StringComparison.OrdinalIgnoreCase))
        {
            _activeFilter = CommandPaletteFilterType.Workflow;
            int spaceIdx = rawQuery.IndexOf(' ');
            effectiveQuery = spaceIdx >= 0 ? rawQuery.Substring(spaceIdx).Trim() : string.Empty;
        }
        else if (rawQuery.StartsWith("@folder", StringComparison.OrdinalIgnoreCase) || rawQuery.StartsWith("@dir", StringComparison.OrdinalIgnoreCase))
        {
            _activeFilter = CommandPaletteFilterType.Folder;
            int spaceIdx = rawQuery.IndexOf(' ');
            effectiveQuery = spaceIdx >= 0 ? rawQuery.Substring(spaceIdx).Trim() : string.Empty;
        }
        else if (rawQuery.StartsWith("@all", StringComparison.OrdinalIgnoreCase))
        {
            _activeFilter = CommandPaletteFilterType.All;
            int spaceIdx = rawQuery.IndexOf(' ');
            effectiveQuery = spaceIdx >= 0 ? rawQuery.Substring(spaceIdx).Trim() : string.Empty;
        }

        UpdateFilterPillsUi();

        // 1. Resolve candidates based on current folder scope
        IEnumerable<TriggerItem> scopeCandidates;
        if (_currentScopeFolderId.HasValue)
        {
            var descendantFolderIds = new HashSet<Guid> { _currentScopeFolderId.Value };
            bool added;
            do
            {
                added = false;
                foreach (var item in _allItems.Where(x => x.ActionType == ActionType.Folder && x.ParentId.HasValue))
                {
                    if (descendantFolderIds.Contains(item.ParentId!.Value) && descendantFolderIds.Add(item.Id))
                    {
                        added = true;
                    }
                }
            } while (added);

            scopeCandidates = _allItems.Where(x => x.ParentId.HasValue && descendantFolderIds.Contains(x.ParentId.Value) && x.IsEnabled).ToList();
        }
        else
        {
            // At root: include all enabled items and virtual system actions
            var list = new List<TriggerItem>(_allItems.Where(x => x.IsEnabled));
            list.Add(_virtualActionManagerItem);
            list.Add(_virtualAppSettingsItem);
            scopeCandidates = list;
        }

        // 2. Update pill count badges
        UpdatePillCounts(scopeCandidates);

        // 3. Apply active category filter and @current context filter
        string? currentProcName = null;
        if (filterCurrentContext && _targetHwnd != IntPtr.Zero)
        {
            currentProcName = NativeMethods.GetProcessNameForWindow(_targetHwnd);
        }

        var filteredCandidates = scopeCandidates.Where(x =>
        {
            if (filterCurrentContext && !string.IsNullOrWhiteSpace(currentProcName))
            {
                if (x.ContextFilter != null && !x.ContextFilter.IsActiveForProcess(currentProcName))
                {
                    return false;
                }
            }

            return _activeFilter switch
            {
                CommandPaletteFilterType.App => x.ActionType == ActionType.Shell && x.Id != App.OpenSettingsActionId && x.Id != AppSettingsVirtualId,
                CommandPaletteFilterType.Snippet => x.ActionType == ActionType.Snippet,
                CommandPaletteFilterType.Workflow => x.ActionType == ActionType.Workflow,
                CommandPaletteFilterType.Folder => x.ActionType == ActionType.Folder,
                _ => true
            };
        }).ToList();

        // 4. Build results list
        var vms = new List<PaletteItemViewModel>();

        if (_currentSortMode == CommandPaletteSortMode.ActionTree)
        {
            var treeOrdered = OrderByActionTree(filteredCandidates, _currentScopeFolderId);
            if (string.IsNullOrWhiteSpace(effectiveQuery))
            {
                vms.AddRange(treeOrdered.Select(item => new PaletteItemViewModel(
                    item,
                    null,
                    item.ParentId.HasValue && _folderPaths.TryGetValue(item.ParentId.Value, out var path) ? path : null)));
            }
            else
            {
                var ranked = FuzzyMatcher.FilterAndRank(treeOrdered, effectiveQuery, _currentSortMode);
                vms.AddRange(ranked.Select(r => new PaletteItemViewModel(
                    r.Item,
                    r,
                    r.Item.ParentId.HasValue && _folderPaths.TryGetValue(r.Item.ParentId.Value, out var path) ? path : null)));
            }
        }
        else if (string.IsNullOrWhiteSpace(effectiveQuery) && _activeFilter == CommandPaletteFilterType.All && _currentSortMode == CommandPaletteSortMode.Smart)
        {
            // Partition into RECENT and ALL ACTIONS
            var (recent, alphabetical) = FuzzyMatcher.PartitionEmptySearch(filteredCandidates, maxRecent: 4);

            if (recent.Count > 0)
            {
                vms.Add(PaletteItemViewModel.CreateSectionHeader("RECENT", hasTopDivider: false));
                vms.AddRange(recent.Select(r => new PaletteItemViewModel(
                    r.Item,
                    r,
                    r.Item.ParentId.HasValue && _folderPaths.TryGetValue(r.Item.ParentId.Value, out var path) ? path : null)));

                vms.Add(PaletteItemViewModel.CreateSectionHeader("ALL ACTIONS", hasTopDivider: true));
                vms.AddRange(alphabetical.Select(r => new PaletteItemViewModel(
                    r.Item,
                    r,
                    r.Item.ParentId.HasValue && _folderPaths.TryGetValue(r.Item.ParentId.Value, out var path) ? path : null)));
            }
            else
            {
                vms.AddRange(alphabetical.Select(r => new PaletteItemViewModel(
                    r.Item,
                    r,
                    r.Item.ParentId.HasValue && _folderPaths.TryGetValue(r.Item.ParentId.Value, out var path) ? path : null)));
            }
        }
        else
        {
            var ranked = FuzzyMatcher.FilterAndRank(filteredCandidates, effectiveQuery, _currentSortMode);
            vms.AddRange(ranked.Select(r => new PaletteItemViewModel(
                r.Item,
                r,
                r.Item.ParentId.HasValue && _folderPaths.TryGetValue(r.Item.ParentId.Value, out var path) ? path : null)));
        }

        bool isCalcMode = rawQuery.StartsWith("=") || rawQuery.StartsWith("@calc", StringComparison.OrdinalIgnoreCase);

        // Evaluate quick math or unit conversion utility
        var calcResult = QuickCalculatorService.TryEvaluate(rawQuery);

        if (isCalcMode)
        {
            FilterPillsBar.Visibility = Visibility.Collapsed;
            ResultsListBox.Visibility = Visibility.Collapsed;
            EmptyStateCard.Visibility = Visibility.Collapsed;
            CalculatorGuideCard.Visibility = Visibility.Visible;
            AnimateWindowHeight(Math.Min(640, SystemParameters.WorkArea.Height - 60));

            if (calcResult != null)
            {
                _activeCalcResult = calcResult;
                var calcItem = new TriggerItem
                {
                    Id = Guid.NewGuid(),
                    Name = $"{calcResult.FormattedResult}  ({calcResult.Expression})",
                    Description = calcResult.Description,
                    ActionType = ActionType.Snippet,
                    Payload = new ActionPayload
                    {
                        SnippetTemplate = calcResult.FormattedResult
                    },
                    IsEnabled = true
                };
                var calcVm = new PaletteItemViewModel(calcItem, null)
                {
                    IsCalculatorResult = true,
                    CalcResult = calcResult
                };
                _activeCalcVm = calcVm;
                vms.Clear();
                vms.Add(calcVm);
                ResultsListBox.ItemsSource = vms;
                ResultsListBox.SelectedItem = calcVm;

                CalcResultBanner.Visibility = Visibility.Visible;
                CalcDraftBanner.Visibility = Visibility.Collapsed;
                CalcResultValueText.Text = calcResult.FormattedResult;
                CalcResultValueText.ToolTip = $"Calculation: {calcResult.Expression.TrimStart('=', ' ').Trim()} = {calcResult.FormattedResult} (Enter to paste)";
                CalcResultDescText.Text = !string.IsNullOrWhiteSpace(calcResult.Description) ? calcResult.Description : "Calculation result";
                CalcResultMoreUnitsBtn.Visibility = (calcResult.Alternatives != null && calcResult.Alternatives.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                _activeCalcResult = null;
                _activeCalcVm = null;
                vms.Clear();
                ResultsListBox.ItemsSource = vms;
                ResultsListBox.SelectedItem = null;

                CalcResultBanner.Visibility = Visibility.Collapsed;
                CalcDraftBanner.Visibility = Visibility.Visible;
                string cleanQuery = rawQuery;
                if (cleanQuery.StartsWith("@calc", StringComparison.OrdinalIgnoreCase))
                {
                    cleanQuery = cleanQuery.Substring(5).TrimStart(':', ' ');
                }
                else if (cleanQuery.StartsWith("="))
                {
                    cleanQuery = cleanQuery.Substring(1).Trim();
                }

                CalcDraftTitleText.Text = !string.IsNullOrWhiteSpace(cleanQuery) ? $"🧮 {cleanQuery} ..." : "🧮 Calculator Mode";
                CalcDraftSubtitleText.Text = "Enter math expressions, dates (next friday, now + 3d), or unit conversions";
            }
        }
        else
        {
            FilterPillsBar.Visibility = Visibility.Visible;
            ResultsListBox.Visibility = Visibility.Visible;
            CalculatorGuideCard.Visibility = Visibility.Collapsed;
            CalcResultBanner.Visibility = Visibility.Collapsed;
            CalcDraftBanner.Visibility = Visibility.Collapsed;
            AnimateWindowHeight(520);

            if (calcResult != null)
            {
                _activeCalcResult = calcResult;
                var calcItem = new TriggerItem
                {
                    Id = Guid.NewGuid(),
                    Name = $"{calcResult.FormattedResult}  ({calcResult.Expression})",
                    Description = calcResult.Description,
                    ActionType = ActionType.Snippet,
                    Payload = new ActionPayload
                    {
                        SnippetTemplate = calcResult.FormattedResult
                    },
                    IsEnabled = true
                };
                var calcVm = new PaletteItemViewModel(calcItem, null)
                {
                    IsCalculatorResult = true,
                    CalcResult = calcResult
                };
                _activeCalcVm = calcVm;
                vms.Insert(0, calcVm);
            }
            else
            {
                _activeCalcResult = null;
                _activeCalcVm = null;
            }

            ResultsListBox.ItemsSource = vms;

            bool hasSelectable = vms.Any(x => x.IsSelectable);
            if (!hasSelectable)
            {
                EmptyStateCard.Visibility = Visibility.Visible;
                if (_currentScopeFolderId.HasValue)
                {
                    if (string.IsNullOrWhiteSpace(effectiveQuery))
                    {
                        EmptyStateIconText.Text = "📁";
                        EmptyStateTitleText.Text = $"\"{_currentScopeFolderName}\" is empty";
                        EmptyStatePromptText.Text = "There are no actions in this folder yet. Press Backspace or Esc to return.";
                        CreateActionEmptyBtnText.Text = $"➕ Create Action in {_currentScopeFolderName}";
                    }
                    else
                    {
                        EmptyStateIconText.Text = "🔍";
                        EmptyStateTitleText.Text = $"No actions matching \"{effectiveQuery}\" in {_currentScopeFolderName}";
                        EmptyStatePromptText.Text = $"Press Enter to create a new action for \"{effectiveQuery}\" in this folder";
                        CreateActionEmptyBtnText.Text = $"➕ Create Action in {_currentScopeFolderName}";
                    }
                }
                else
                {
                    EmptyStateIconText.Text = "🔍";
                    EmptyStateTitleText.Text = "No matching actions found.";
                    EmptyStatePromptText.Text = !string.IsNullOrWhiteSpace(effectiveQuery)
                        ? $"Press Enter to create a new action for \"{effectiveQuery}\""
                        : "No actions found in this category.";
                    CreateActionEmptyBtnText.Text = "➕ Create Action";
                }
                ResultsListBox.SelectedItem = null;
            }
            else
            {
                EmptyStateCard.Visibility = Visibility.Collapsed;
                var firstSelectable = vms.FirstOrDefault(x => x.IsSelectable);
                if (firstSelectable != null)
                {
                    ResultsListBox.SelectedItem = firstSelectable;
                }
            }
        }

        UpdatePreviewAndHints();
    }

    private double _currentAnimatedTargetHeight = 520;

    private void AnimateWindowHeight(double targetHeight)
    {
        if (Math.Abs(_currentAnimatedTargetHeight - targetHeight) < 1) return;
        _currentAnimatedTargetHeight = targetHeight;

        var anim = new DoubleAnimation
        {
            To = targetHeight,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(HeightProperty, anim);
    }

    private void CalcGuideTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            SelectCalcGuideTab(tag);
        }
    }

    private void SelectCalcGuideTab(string tag)
    {
        if (CalcPanelPopular == null || CalcPanelDate == null || CalcPanelScientific == null ||
            CalcPanelProgrammer == null || CalcPanelUnits == null) return;

        CalcPanelPopular.Visibility = tag == "Popular" ? Visibility.Visible : Visibility.Collapsed;
        CalcPanelDate.Visibility = tag == "Date" ? Visibility.Visible : Visibility.Collapsed;
        CalcPanelScientific.Visibility = tag == "Scientific" ? Visibility.Visible : Visibility.Collapsed;
        CalcPanelProgrammer.Visibility = tag == "Programmer" ? Visibility.Visible : Visibility.Collapsed;
        CalcPanelUnits.Visibility = tag == "Units" ? Visibility.Visible : Visibility.Collapsed;

        UpdateTabButtonStyle(CalcTabPopular, tag == "Popular");
        UpdateTabButtonStyle(CalcTabDate, tag == "Date");
        UpdateTabButtonStyle(CalcTabScientific, tag == "Scientific");
        UpdateTabButtonStyle(CalcTabProgrammer, tag == "Programmer");
        UpdateTabButtonStyle(CalcTabUnits, tag == "Units");
    }

    private void UpdateTabButtonStyle(Button? btn, bool isSelected)
    {
        if (btn == null) return;

        if (isSelected)
        {
            btn.Background = Application.Current?.TryFindResource("AccentBrush") as Brush ?? Brushes.CornflowerBlue;
            btn.Foreground = Brushes.White;
            btn.BorderBrush = Application.Current?.TryFindResource("AccentBrush") as Brush ?? Brushes.CornflowerBlue;
            btn.FontWeight = FontWeights.Bold;
        }
        else
        {
            btn.Background = Brushes.Transparent;
            btn.Foreground = Application.Current?.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.LightGray;
            btn.BorderBrush = Application.Current?.TryFindResource("BorderSubtleBrush") as Brush ?? Brushes.DarkGray;
            btn.FontWeight = FontWeights.SemiBold;
        }
    }

    private void CalcOperatorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string op)
        {
            string current = SearchTextBox.Text;
            if (!current.StartsWith("=") && !current.StartsWith("@calc", StringComparison.OrdinalIgnoreCase))
            {
                current = "= " + current.TrimStart();
            }

            int selStart = SearchTextBox.SelectionStart;
            if (selStart < 0 || selStart > current.Length)
            {
                selStart = current.Length;
            }

            if (op.EndsWith("()"))
            {
                string funcName = op[..^1]; // e.g. "sin("
                string newText = current.Insert(selStart, op);
                SearchTextBox.Text = newText;
                SearchTextBox.CaretIndex = selStart + funcName.Length;
            }
            else
            {
                string toInsert = op.StartsWith("to ") || op.StartsWith("in ") ? " " + op + " " : (op.EndsWith(" ") ? op : op + " ");
                string newText = current.Insert(selStart, toInsert);
                SearchTextBox.Text = newText;
                SearchTextBox.CaretIndex = selStart + toInsert.Length;
            }
            SearchTextBox.Focus();
        }
    }

    private void CalcExamplePill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string query)
        {
            SearchTextBox.Text = query;
            SearchTextBox.CaretIndex = SearchTextBox.Text.Length;
            SearchTextBox.Focus();
        }
    }

    private void PasteActiveCalcResult()
    {
        if (_activeCalcResult == null) return;
        var calcItem = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = $"{_activeCalcResult.FormattedResult}  ({_activeCalcResult.Expression})",
            Description = _activeCalcResult.Description,
            ActionType = ActionType.Snippet,
            Payload = new ActionPayload
            {
                SnippetTemplate = _activeCalcResult.FormattedResult
            },
            IsEnabled = true
        };
        SafeClose();
        _ = _executor.ExecuteAsync(calcItem, ExecutionOverride.Standard, _targetHwnd);
    }

    private void CalcResultPasteBtn_Click(object sender, RoutedEventArgs e)
    {
        PasteActiveCalcResult();
    }

    private void CalcResultCopyValueBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_activeCalcResult != null)
        {
            Clipboard.SetText(_activeCalcResult.FormattedResult);
            ShowInlineFeedback($"Copied answer ({_activeCalcResult.FormattedResult})! 📋");
        }
    }

    private void CalcResultCopyFullBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_activeCalcResult != null)
        {
            string expr = _activeCalcResult.Expression.TrimStart('=', ' ').Trim();
            string full = $"{expr} = {_activeCalcResult.FormattedResult}";
            Clipboard.SetText(full);
            ShowInlineFeedback($"Copied: {full} 📋");
        }
    }

    private void CalcResultChainBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_activeCalcResult != null && SearchTextBox != null)
        {
            SearchTextBox.Text = $"= {_activeCalcResult.FormattedResult} ";
            SearchTextBox.CaretIndex = SearchTextBox.Text.Length;
            SearchTextBox.Focus();
        }
    }

    private void CalcResultMoreUnitsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_activeCalcVm != null)
        {
            OpenCalculatorUnitsOverlay(_activeCalcVm);
        }
        else if (_activeCalcResult != null)
        {
            var calcItem = new TriggerItem
            {
                Id = Guid.NewGuid(),
                Name = $"{_activeCalcResult.FormattedResult}  ({_activeCalcResult.Expression})",
                Description = _activeCalcResult.Description,
                ActionType = ActionType.Snippet,
                Payload = new ActionPayload { SnippetTemplate = _activeCalcResult.FormattedResult },
                IsEnabled = true
            };
            var vm = new PaletteItemViewModel(calcItem, null)
            {
                IsCalculatorResult = true,
                CalcResult = _activeCalcResult
            };
            OpenCalculatorUnitsOverlay(vm);
        }
    }

    private void UpdatePillCounts(IEnumerable<TriggerItem> scopeItems)
    {
        var list = scopeItems.ToList();
        int allCount = list.Count(x => x.Id != App.OpenSettingsActionId && x.Id != AppSettingsVirtualId);
        int appCount = list.Count(x => x.ActionType == ActionType.Shell && x.Id != App.OpenSettingsActionId && x.Id != AppSettingsVirtualId);
        int snipCount = list.Count(x => x.ActionType == ActionType.Snippet);
        int wfCount = list.Count(x => x.ActionType == ActionType.Workflow);
        int folderCount = list.Count(x => x.ActionType == ActionType.Folder);

        FilterCountAll.Text = $"All ({allCount})";
        FilterCountApp.Text = $"Apps & Commands ({appCount})";
        FilterCountSnippet.Text = $"Snippets ({snipCount})";
        FilterCountWorkflow.Text = $"Workflows ({wfCount})";
        FilterCountFolder.Text = $"Folders ({folderCount})";
    }

    private void UpdateFilterPillsUi()
    {
        FilterPillAll.Tag = _activeFilter == CommandPaletteFilterType.All ? "Selected" : "";
        FilterPillApp.Tag = _activeFilter == CommandPaletteFilterType.App ? "Selected" : "";
        FilterPillSnippet.Tag = _activeFilter == CommandPaletteFilterType.Snippet ? "Selected" : "";
        FilterPillWorkflow.Tag = _activeFilter == CommandPaletteFilterType.Workflow ? "Selected" : "";
        FilterPillFolder.Tag = _activeFilter == CommandPaletteFilterType.Folder ? "Selected" : "";
    }

    private void FilterPill_Click(object sender, RoutedEventArgs e)
    {
        if (sender == FilterPillApp)
        {
            _activeFilter = _activeFilter == CommandPaletteFilterType.App ? CommandPaletteFilterType.All : CommandPaletteFilterType.App;
        }
        else if (sender == FilterPillSnippet)
        {
            _activeFilter = _activeFilter == CommandPaletteFilterType.Snippet ? CommandPaletteFilterType.All : CommandPaletteFilterType.Snippet;
        }
        else if (sender == FilterPillWorkflow)
        {
            _activeFilter = _activeFilter == CommandPaletteFilterType.Workflow ? CommandPaletteFilterType.All : CommandPaletteFilterType.Workflow;
        }
        else if (sender == FilterPillFolder)
        {
            _activeFilter = _activeFilter == CommandPaletteFilterType.Folder ? CommandPaletteFilterType.All : CommandPaletteFilterType.Folder;
        }
        else
        {
            _activeFilter = CommandPaletteFilterType.All;
        }

        SearchTextBox.Focus();
        FilterResults();
    }

    private void ResultsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdatePreviewAndHints();
    }

    private void UpdatePreviewAndHints()
    {
        string rawQuery = SearchTextBox?.Text?.Trim() ?? string.Empty;
        bool isCalcMode = rawQuery.StartsWith("=") || rawQuery.StartsWith("@calc", StringComparison.OrdinalIgnoreCase);

        if (isCalcMode)
        {
            if (_activeCalcResult != null)
            {
                PreviewDetailText.Text = $"Calculator • {_activeCalcResult.Expression} = {_activeCalcResult.FormattedResult}";
                FooterHintsText.Text = $"Enter Paste Answer   •   Ctrl+C Copy Answer ({_activeCalcResult.FormattedResult})   •   Ctrl+Shift+C Copy Question & Answer   •   Tab Chain";
            }
            else
            {
                PreviewDetailText.Text = "Calculator Mode • Active";
                FooterHintsText.Text = "Click keypad to insert operators   •   Esc Exit Calculator";
            }
            return;
        }

        if (ResultsListBox.SelectedItem is PaletteItemViewModel vm && vm.IsSelectable)
        {
            PreviewDetailText.Text = vm.DetailPreviewText;

            // Contextual footer hints
            if (vm.IsCalculatorResult)
            {
                string ans = vm.CalcResult?.FormattedResult ?? string.Empty;
                FooterHintsText.Text = string.IsNullOrEmpty(ans)
                    ? "Enter Paste Answer   •   Ctrl+C Copy Answer   •   Ctrl+Shift+C Copy Question & Answer   •   Tab Chain"
                    : $"Enter Paste Answer   •   Ctrl+C Copy Answer ({ans})   •   Ctrl+Shift+C Copy Question & Answer   •   Tab Chain";
            }
            else if (vm.Item.ActionType == ActionType.Snippet)
            {
                FooterHintsText.Text = "Enter Paste   •   Ctrl+C Copy Snippet   •   Alt+Enter Edit in Action Manager";
            }
            else if (vm.Item.ActionType == ActionType.Shell)
            {
                if (vm.IsSystemAction)
                {
                    FooterHintsText.Text = "Enter Open   •   Ctrl+M Action Manager   •   Ctrl+, Settings";
                }
                else
                {
                    var shortcuts = ActionShortcutRelevanceHelper.GetContextualShortcuts(vm.Item);
                    var hints = new List<string> { $"Enter {shortcuts.PrimaryActionVerb}" };

                    if (shortcuts.CanRunAsAdmin)
                    {
                        hints.Add("Ctrl+Enter Run as Admin");
                    }

                    if (shortcuts.CanRevealInExplorer)
                    {
                        hints.Add("Shift+Enter Reveal in Explorer");
                    }

                    if (!string.IsNullOrEmpty(shortcuts.CopyLabel))
                    {
                        hints.Add($"Ctrl+C {shortcuts.CopyLabel}");
                    }

                    hints.Add("Alt+Enter Edit in Action Manager");
                    FooterHintsText.Text = string.Join("   •   ", hints);
                }
            }
            else if (vm.Item.ActionType == ActionType.Workflow)
            {
                var shortcuts = ActionShortcutRelevanceHelper.GetContextualShortcuts(vm.Item);
                var hints = new List<string> { $"Enter {shortcuts.PrimaryActionVerb}" };

                if (shortcuts.CanRunAsAdmin)
                {
                    hints.Add("Ctrl+Enter Run as Admin");
                }

                if (!string.IsNullOrEmpty(shortcuts.CopyLabel))
                {
                    hints.Add($"Ctrl+C {shortcuts.CopyLabel}");
                }

                hints.Add("Alt+Enter Edit in Action Manager");
                FooterHintsText.Text = string.Join("   •   ", hints);
            }
            else if (vm.Item.ActionType == ActionType.Folder)
            {
                int count = ResolveFolderActions(vm.Item, _allItems, includeSubfolders: true).Count;
                FooterHintsText.Text = count > 0
                    ? $"Enter Drill into folder   •   Ctrl+Enter Run all ({count})   •   Shift+Enter Cursor menu   •   Alt+Enter Action Manager"
                    : "Enter Drill into folder   •   Shift+Enter Cursor menu   •   Alt+Enter Action Manager";
            }
        }
        else
        {
            PreviewDetailText.Text = "Select an action to view details";
            FooterHintsText.Text = "Enter Run   •   Ctrl+Enter Run as Admin   •   Alt+Enter Edit in Action Manager";
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Overlay Escape
        if (CheatSheetOverlay.Visibility == Visibility.Visible)
        {
            if (key == Key.Escape || key == Key.F1 || (key == Key.OemQuestion && (Keyboard.Modifiers & ModifierKeys.Shift) == 0))
            {
                CheatSheetOverlay.Visibility = Visibility.Collapsed;
                e.Handled = true;
                return;
            }
        }

        // Alternative Units Overlay Escape
        if (CalculatorUnitsOverlay.Visibility == Visibility.Visible)
        {
            if (key == Key.Escape)
            {
                CloseCalculatorUnitsOverlay();
                e.Handled = true;
                return;
            }
        }

        // Folder Execution Confirmation Overlay Escape / Enter
        if (FolderExecutionConfirmOverlay.Visibility == Visibility.Visible)
        {
            if (key == Key.Escape)
            {
                CloseFolderExecutionConfirmOverlay();
                e.Handled = true;
                return;
            }

            if (key == Key.Enter)
            {
                FolderConfirmRunBtn_Click(sender, e);
                e.Handled = true;
                return;
            }
        }

        // Toggle Help Overlay
        if (key == Key.F1 || (key == Key.OemQuestion && (Keyboard.Modifiers & ModifierKeys.Shift) != 0 && SearchTextBox.SelectionLength == 0 && string.IsNullOrWhiteSpace(SearchTextBox.Text)))
        {
            ToggleHelpOverlay();
            e.Handled = true;
            return;
        }

        // Autocomplete Popup Navigation
        if (PrefixAutoCompletePopup.IsOpen)
        {
            if (key == Key.Down)
            {
                int count = PrefixSuggestionsList.Items.Count;
                if (count > 0)
                {
                    int next = (PrefixSuggestionsList.SelectedIndex + 1) % count;
                    PrefixSuggestionsList.SelectedIndex = next;
                    PrefixSuggestionsList.ScrollIntoView(PrefixSuggestionsList.SelectedItem);
                }
                e.Handled = true;
                return;
            }

            if (key == Key.Up)
            {
                int count = PrefixSuggestionsList.Items.Count;
                if (count > 0)
                {
                    int prev = (PrefixSuggestionsList.SelectedIndex - 1 + count) % count;
                    PrefixSuggestionsList.SelectedIndex = prev;
                    PrefixSuggestionsList.ScrollIntoView(PrefixSuggestionsList.SelectedItem);
                }
                e.Handled = true;
                return;
            }

            if (key == Key.Enter || key == Key.Tab)
            {
                if (PrefixSuggestionsList.SelectedItem is PrefixSuggestionItem selectedSuggestion)
                {
                    ApplyPrefixSuggestion(selectedSuggestion);
                }
                else if (PrefixSuggestionsList.Items.Count > 0 && PrefixSuggestionsList.Items[0] is PrefixSuggestionItem firstItem)
                {
                    ApplyPrefixSuggestion(firstItem);
                }
                else
                {
                    PrefixAutoCompletePopup.IsOpen = false;
                }
                e.Handled = true;
                return;
            }

            if (key == Key.Escape)
            {
                PrefixAutoCompletePopup.IsOpen = false;
                e.Handled = true;
                return;
            }
        }

        // Global Navigation Hotkeys
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (key == Key.M)
            {
                SafeClose();
                (Application.Current as App)?.ShowSettingsWindow();
                e.Handled = true;
                return;
            }

            if (key == Key.OemComma)
            {
                SafeClose();
                (Application.Current as App)?.ShowApplicationSettingsWindow();
                e.Handled = true;
                return;
            }

            if (key == Key.S)
            {
                OpenSortMenu();
                e.Handled = true;
                return;
            }

            if (key == Key.C)
            {
                bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                if (isShift)
                {
                    if (_activeCalcResult != null)
                    {
                        string expr = _activeCalcResult.Expression.TrimStart('=', ' ').Trim();
                        string full = $"{expr} = {_activeCalcResult.FormattedResult}";
                        Clipboard.SetText(full);
                        ShowInlineFeedback($"Copied: {full} 📋");
                        e.Handled = true;
                        return;
                    }
                    if (ResultsListBox.SelectedItem is PaletteItemViewModel calcVm && calcVm.IsCalculatorResult && calcVm.CalcResult != null)
                    {
                        string expr = calcVm.CalcResult.Expression.TrimStart('=', ' ').Trim();
                        string full = $"{expr} = {calcVm.CalcResult.FormattedResult}";
                        Clipboard.SetText(full);
                        ShowInlineFeedback($"Copied: {full} 📋");
                        e.Handled = true;
                        return;
                    }
                }
                else
                {
                    if (SearchTextBox.SelectedText.Length == 0 && _activeCalcResult != null)
                    {
                        Clipboard.SetText(_activeCalcResult.FormattedResult);
                        ShowInlineFeedback($"Copied answer ({_activeCalcResult.FormattedResult})! 📋");
                        e.Handled = true;
                        return;
                    }
                    else if (ResultsListBox.SelectedItem is PaletteItemViewModel selVm && SearchTextBox.SelectedText.Length == 0)
                    {
                        CopyItemToClipboard(selVm);
                        e.Handled = true;
                        return;
                    }
                }
            }

            if (key >= Key.D1 && key <= Key.D5)
            {
                _activeFilter = key switch
                {
                    Key.D2 => CommandPaletteFilterType.App,
                    Key.D3 => CommandPaletteFilterType.Snippet,
                    Key.D4 => CommandPaletteFilterType.Workflow,
                    Key.D5 => CommandPaletteFilterType.Folder,
                    _ => CommandPaletteFilterType.All
                };
                FilterResults();
                e.Handled = true;
                return;
            }
        }

        if (key == Key.Escape)
        {
            if (_currentScopeFolderId.HasValue)
            {
                ExitFolderScope();
                e.Handled = true;
                return;
            }

            SafeClose();
            e.Handled = true;
            return;
        }

        if (key == Key.Back && string.IsNullOrEmpty(SearchTextBox.Text) && _currentScopeFolderId.HasValue)
        {
            ExitFolderScope();
            e.Handled = true;
            return;
        }

        if (key == Key.Tab)
        {
            if (SearchTextBox == null) return;
            string rawQuery = SearchTextBox.Text?.Trim() ?? string.Empty;
            bool isCalcMode = rawQuery.StartsWith("=") || rawQuery.StartsWith("@calc", StringComparison.OrdinalIgnoreCase);

            if (_activeCalcResult != null)
            {
                var ans = _activeCalcResult.FormattedResult;
                if (!string.IsNullOrWhiteSpace(ans))
                {
                    SearchTextBox.Text = $"= {ans} ";
                    SearchTextBox.CaretIndex = SearchTextBox.Text.Length;
                    e.Handled = true;
                    return;
                }
            }
            else if (ResultsListBox.SelectedItem is PaletteItemViewModel calcVm && calcVm.IsCalculatorResult)
            {
                var ans = calcVm.Item?.Payload?.SnippetTemplate ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(ans))
                {
                    SearchTextBox.Text = $"= {ans} ";
                    SearchTextBox.CaretIndex = SearchTextBox.Text.Length;
                    e.Handled = true;
                    return;
                }
            }
            else if (isCalcMode)
            {
                // Incomplete calculation in calculator mode: suppress tab focus loss
                e.Handled = true;
                return;
            }

            if (string.IsNullOrEmpty(SearchTextBox.Text))
            {
                // Cycle filter pills
                int next = ((int)_activeFilter + 1) % 5;
                _activeFilter = (CommandPaletteFilterType)next;
                FilterResults();
                e.Handled = true;
                return;
            }
        }

        if (key == Key.Down)
        {
            NavigateSelection(1);
            e.Handled = true;
            return;
        }

        if (key == Key.Up)
        {
            NavigateSelection(-1);
            e.Handled = true;
            return;
        }

        if (key == Key.Enter)
        {
            string rawQuery = SearchTextBox?.Text?.Trim() ?? string.Empty;
            bool isCalcMode = rawQuery.StartsWith("=") || rawQuery.StartsWith("@calc", StringComparison.OrdinalIgnoreCase);
            if (isCalcMode)
            {
                if (_activeCalcResult != null)
                {
                    PasteActiveCalcResult();
                    e.Handled = true;
                    return;
                }
                // Incomplete input: do nothing on Enter
                e.Handled = true;
                return;
            }

            if (EmptyStateCard.Visibility == Visibility.Visible)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && _currentScopeFolderId.HasValue)
                {
                    var scopedFolder = _allItems.FirstOrDefault(x => x.Id == _currentScopeFolderId.Value && x.ActionType == ActionType.Folder);
                    if (scopedFolder != null)
                    {
                        OpenFolderExecutionConfirmOverlay(scopedFolder);
                        e.Handled = true;
                        return;
                    }
                }

                CreateActionEmptyBtn_Click(sender, e);
                e.Handled = true;
                return;
            }

            ExecuteCurrentSelection(DetermineOverride());
            e.Handled = true;
        }
    }

    private void NavigateSelection(int direction)
    {
        var items = ResultsListBox.ItemsSource as IList<PaletteItemViewModel>;
        if (items == null || items.Count == 0) return;

        int count = items.Count;
        int currentIdx = ResultsListBox.SelectedIndex;
        if (currentIdx < 0) currentIdx = 0;

        for (int step = 1; step <= count; step++)
        {
            int nextIdx = (currentIdx + (direction * step) + count) % count;
            if (items[nextIdx].IsSelectable)
            {
                ResultsListBox.SelectedIndex = nextIdx;
                ResultsListBox.ScrollIntoView(items[nextIdx]);
                break;
            }
        }
    }

    private void CopyItemToClipboard(PaletteItemViewModel vm)
    {
        if (vm.Item == null) return;

        try
        {
            string copyText = string.Empty;
            string feedbackMsg = "Copied to clipboard! 📋";

            if (vm.IsCalculatorResult && vm.CalcResult != null)
            {
                copyText = vm.CalcResult.FormattedResult;
                feedbackMsg = $"Copied answer ({vm.CalcResult.FormattedResult})! 📋";
            }
            else if (vm.Item.ActionType == ActionType.Snippet)
            {
                string raw = vm.Item.Payload.SnippetTemplate ?? string.Empty;
                copyText = raw.Replace("{{Date}}", DateTime.Now.ToString("yyyy-MM-dd"))
                              .Replace("{{Time}}", DateTime.Now.ToString("HH:mm:ss"))
                              .Replace("{{DateTime}}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                feedbackMsg = "Copied snippet to clipboard! 📋";
            }
            else if (vm.Item.ActionType == ActionType.Shell)
            {
                var cmd = vm.Item.Payload.Command ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(vm.Item.Payload.Arguments))
                {
                    cmd += " " + vm.Item.Payload.Arguments;
                }
                copyText = cmd;
                feedbackMsg = "Copied command to clipboard! 📋";
            }
            else if (vm.Item.ActionType == ActionType.Workflow)
            {
                copyText = $"Workflow: {vm.Item.Name} ({vm.Item.Payload.WorkflowSteps?.Count ?? 0} steps)";
                feedbackMsg = "Copied workflow summary! 📋";
            }
            else if (vm.Item.ActionType == ActionType.Folder)
            {
                copyText = vm.Item.Name;
                feedbackMsg = "Copied folder name! 📋";
            }

            if (!string.IsNullOrEmpty(copyText))
            {
                Clipboard.SetText(copyText);
                ShowInlineFeedback(feedbackMsg);
            }
        }
        catch { }
    }

    private void ShowInlineFeedback(string message)
    {
        FeedbackText.Text = message;
        FeedbackBadge.Visibility = Visibility.Visible;
        _feedbackTimer.Stop();
        _feedbackTimer.Start();
    }

    private void OpenSortMenu()
    {
        UpdateSortMenuCheckmarks();
        SortContextMenu.PlacementTarget = SortModeBtn;
        SortContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        SortContextMenu.IsOpen = true;
    }

    private void SortModeBtn_Click(object sender, RoutedEventArgs e)
    {
        OpenSortMenu();
    }

    private void SortMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string tagStr && Enum.TryParse<CommandPaletteSortMode>(tagStr, out var mode))
        {
            ApplySortMode(mode);
        }
    }

    private void ApplySortMode(CommandPaletteSortMode mode)
    {
        _currentSortMode = mode;
        UpdateSortButtonText();
        UpdateSortMenuCheckmarks();
        FilterResults();

        if (_repository != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var settings = await _repository.LoadSettingsAsync();
                    settings.CommandPaletteSortMode = _currentSortMode;
                    await _repository.SaveSettingsAsync(settings);
                }
                catch { }
            });
        }
    }

    private void UpdateSortMenuCheckmarks()
    {
        SetMenuChecked(SortItemSmart, _currentSortMode == CommandPaletteSortMode.Smart);
        SetMenuChecked(SortItemAlpha, _currentSortMode == CommandPaletteSortMode.Alphabetical);
        SetMenuChecked(SortItemFreq, _currentSortMode == CommandPaletteSortMode.MostFrequent);
        SetMenuChecked(SortItemRecent, _currentSortMode == CommandPaletteSortMode.Recent);
        SetMenuChecked(SortItemTree, _currentSortMode == CommandPaletteSortMode.ActionTree);
    }

    private static void SetMenuChecked(MenuItem? item, bool isChecked)
    {
        if (item == null) return;
        if (isChecked)
        {
            item.Icon = new TextBlock
            {
                Text = "✓",
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Foreground = Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.CornflowerBlue,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
        }
        else
        {
            item.Icon = null;
        }
    }

    private void ActionManagerBtn_Click(object sender, RoutedEventArgs e)
    {
        SafeClose();
        (Application.Current as App)?.ShowSettingsWindow();
    }

    private void AppSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        SafeClose();
        var initialCategory = (App.LatestAvailableUpdate?.IsUpdateAvailable == true && App.LatestAvailableUpdate.LatestUpdate != null)
            ? ApplicationSettingsWindow.SettingsCategory.Updates
            : ApplicationSettingsWindow.SettingsCategory.Appearance;
        (Application.Current as App)?.ShowApplicationSettingsWindow(initialCategory);
    }

    private void HelpOverlayBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleHelpOverlay();
    }

    private void CloseHelpBtn_Click(object sender, RoutedEventArgs e)
    {
        CheatSheetOverlay.Visibility = Visibility.Collapsed;
    }

    private void ToggleHelpOverlay()
    {
        CheatSheetOverlay.Visibility = CheatSheetOverlay.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        ExitFolderScope();
    }

    private void ScopeDismissBtn_Click(object sender, RoutedEventArgs e)
    {
        ExitFolderScope();
    }

    private void ExitFolderScope()
    {
        if (!_currentScopeFolderId.HasValue) return;

        // Try to step up to parent folder if one exists
        var currentFolder = _allItems.FirstOrDefault(x => x.Id == _currentScopeFolderId.Value && x.ActionType == ActionType.Folder);
        if (currentFolder?.ParentId.HasValue == true)
        {
            var parentFolder = _allItems.FirstOrDefault(x => x.Id == currentFolder.ParentId.Value && x.ActionType == ActionType.Folder);
            if (parentFolder != null)
            {
                _currentScopeFolderId = parentFolder.Id;
                _currentScopeFolderName = parentFolder.Name;
            }
            else
            {
                _currentScopeFolderId = null;
                _currentScopeFolderName = null;
            }
        }
        else
        {
            _currentScopeFolderId = null;
            _currentScopeFolderName = null;
        }

        _activeFilter = CommandPaletteFilterType.All;
        UpdateFilterPillsUi();
        UpdateScopeUi();
        SearchTextBox.Text = string.Empty;
        FilterResults();
    }

    private void DrillIntoFolder(TriggerItem folder)
    {
        _currentScopeFolderId = folder.Id;
        _currentScopeFolderName = folder.Name;
        _activeFilter = CommandPaletteFilterType.All;
        UpdateFilterPillsUi();
        UpdateScopeUi();
        SearchTextBox.Text = string.Empty;
        FilterResults();
    }

    private void FolderChevron_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is PaletteItemViewModel vm && vm.Item.ActionType == ActionType.Folder)
        {
            DrillIntoFolder(vm.Item);
        }
    }

    private void CreateActionEmptyBtn_Click(object sender, RoutedEventArgs e)
    {
        var rawQuery = SearchTextBox.Text.Trim();
        SafeClose();
        (Application.Current as App)?.ShowSettingsWindowAndCreate(rawQuery, _currentScopeFolderId);
    }

    private static ExecutionOverride DetermineOverride()
    {
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            return ExecutionOverride.RunAsAdmin;
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            return ExecutionOverride.RevealInExplorer;
        if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
            return ExecutionOverride.OpenSettings;
        return ExecutionOverride.Standard;
    }

    private bool _isClosing;

    private void SafeClose()
    {
        if (_isClosing) return;
        _isClosing = true;
        try
        {
            if (PrefixAutoCompletePopup != null)
            {
                PrefixAutoCompletePopup.IsOpen = false;
            }
            if (SortContextMenu != null)
            {
                SortContextMenu.IsOpen = false;
            }
            if (CalculatorUnitsOverlay != null)
            {
                CalculatorUnitsOverlay.Visibility = Visibility.Collapsed;
            }
            if (FolderExecutionConfirmOverlay != null)
            {
                FolderExecutionConfirmOverlay.Visibility = Visibility.Collapsed;
            }
            Close();
        }
        catch { }
    }

    private void ResultsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ExecuteCurrentSelection(DetermineOverride());
    }

    private void ExecuteCurrentSelection(ExecutionOverride executionOverride)
    {
        var vm = ResultsListBox.SelectedItem as PaletteItemViewModel ?? _activeCalcVm;
        if (vm != null && vm.IsSelectable)
        {
            // Calculator result
            if (vm.IsCalculatorResult)
            {
                if (executionOverride == ExecutionOverride.OpenSettings)
                {
                    // Suppress opening Action Manager for ephemeral calculator results; copy answer instead
                    CopyItemToClipboard(vm);
                    return;
                }

                SafeClose();
                _ = _executor.ExecuteAsync(vm.Item, executionOverride, _targetHwnd);
                return;
            }

            // Virtual: Action Manager
            if (vm.Item.Id == App.OpenSettingsActionId)
            {
                SafeClose();
                (Application.Current as App)?.ShowSettingsWindow();
                return;
            }

            // Virtual: Application Settings
            if (vm.Item.Id == AppSettingsVirtualId)
            {
                SafeClose();
                (Application.Current as App)?.ShowApplicationSettingsWindow();
                return;
            }

            // Folder
            if (vm.Item.ActionType == ActionType.Folder)
            {
                if (executionOverride == ExecutionOverride.RunAsAdmin)
                {
                    OpenFolderExecutionConfirmOverlay(vm.Item);
                    return;
                }

                if (executionOverride == ExecutionOverride.RevealInExplorer)
                {
                    SafeClose();
                    (Application.Current as App)?.OpenCursorMenu(vm.Item, _targetHwnd);
                    return;
                }

                if (executionOverride == ExecutionOverride.OpenSettings)
                {
                    SafeClose();
                    (Application.Current as App)?.ShowSettingsWindow(vm.Item);
                    return;
                }

                DrillIntoFolder(vm.Item);
                return;
            }

            SafeClose();
            _ = _executor.ExecuteAsync(vm.Item, executionOverride, _targetHwnd);
        }
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (!_isLoaded) return;
        SafeClose();
    }

    private CalculatorResult? _activeCalcResult;
    private PaletteItemViewModel? _activeCalcVm;
    private IReadOnlyList<AlternativeMeasurement>? _currentAlternatives;
    private string _selectedCategory = "All";

    private void UpdateCategoryChipsUi()
    {
        var chips = new[] { ChipAll, ChipDateFormats, ChipCommon, ChipPhysics, ChipSpace, ChipPopCulture, ChipNature, ChipHistorical };
        foreach (var chip in chips)
        {
            if (chip == null) continue;
            string tag = chip.Tag as string ?? "All";
            bool isSelected = string.Equals(tag, _selectedCategory, StringComparison.OrdinalIgnoreCase);
            if (isSelected)
            {
                chip.Background = (Brush)FindResource("AccentSubtleBrush");
                chip.BorderBrush = (Brush)FindResource("AccentBrush");
                chip.Foreground = (Brush)FindResource("AccentBrush");
            }
            else
            {
                chip.Background = (Brush)FindResource("BgTertiaryBrush");
                chip.BorderBrush = (Brush)FindResource("BorderSubtleBrush");
                chip.Foreground = (Brush)FindResource("TextSecondaryBrush");
            }
        }
    }

    private double _previousWindowHeight = 520;
    private double _previousWindowTop = 0;

    private void OpenCalculatorUnitsOverlay(PaletteItemViewModel vm)
    {
        _activeCalcVm = vm;
        _currentAlternatives = vm.CalcResult?.Alternatives ?? Array.Empty<AlternativeMeasurement>();
        _selectedCategory = "All";

        bool hasDateFormats = _currentAlternatives.Any(a => a.Category == "Date Formats");
        if (ChipDateFormats != null)
        {
            ChipDateFormats.Visibility = hasDateFormats ? Visibility.Visible : Visibility.Collapsed;
        }

        UpdateCategoryChipsUi();

        if (!string.IsNullOrWhiteSpace(vm.CalcResult?.HierarchicalBreakdown))
        {
            HeroBreakdownCard.Visibility = Visibility.Visible;
            HeroHierarchicalValueText.Text = vm.CalcResult.HierarchicalBreakdown;
            HeroCumulativeValueText.Text = vm.CalcResult.CumulativeBreakdown ?? vm.CalcResult.HierarchicalBreakdown;
        }
        else
        {
            HeroBreakdownCard.Visibility = Visibility.Collapsed;
        }

        UnitsSearchTextBox.Text = string.Empty;
        ApplyAlternativesFilter();

        _previousWindowHeight = this.Height;
        _previousWindowTop = this.Top;

        double targetHeight = Math.Min(680, SystemParameters.WorkArea.Height - 60);
        if (targetHeight > this.Height)
        {
            double diff = targetHeight - this.Height;
            double newTop = this.Top - (diff / 2.0);
            if (newTop < SystemParameters.WorkArea.Top + 20)
            {
                newTop = SystemParameters.WorkArea.Top + 20;
            }
            this.Top = newTop;
            this.Height = targetHeight;
        }

        CalculatorUnitsOverlay.Visibility = Visibility.Visible;
        UnitsSearchTextBox.Focus();
    }

    private void CloseCalculatorUnitsOverlay()
    {
        CalculatorUnitsOverlay.Visibility = Visibility.Collapsed;
        if (Math.Abs(this.Height - _previousWindowHeight) > 1.0)
        {
            this.Height = _previousWindowHeight;
            this.Top = _previousWindowTop;
        }
        SearchTextBox.Focus();
    }

    private void CloseUnitsOverlayBtn_Click(object sender, RoutedEventArgs e)
    {
        CloseCalculatorUnitsOverlay();
    }

    private void CategoryChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string cat)
        {
            _selectedCategory = cat;
            UpdateCategoryChipsUi();
            ApplyAlternativesFilter();
        }
    }

    private void UnitsSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyAlternativesFilter();
    }

    private void ApplyAlternativesFilter()
    {
        if (_currentAlternatives == null) return;
        var query = UnitsSearchTextBox.Text?.Trim() ?? string.Empty;
        var filtered = _currentAlternatives.Where(a =>
        {
            bool catMatch = _selectedCategory == "All" || string.Equals(a.Category, _selectedCategory, StringComparison.OrdinalIgnoreCase);
            if (!catMatch) return false;

            if (string.IsNullOrWhiteSpace(query)) return true;

            return a.FormattedValue.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   a.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   (a.ConceptTitle != null && a.ConceptTitle.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                   (a.ConversationalSentence != null && a.ConversationalSentence.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                   a.Description.Contains(query, StringComparison.OrdinalIgnoreCase);
        }).ToList();

        UnitsItemsControl.ItemsSource = filtered;
    }

    private void SecondaryDetail_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBlock tb && tb.DataContext is PaletteItemViewModel vm && vm.IsCalculatorResult && vm.HasAlternatives)
        {
            OpenCalculatorUnitsOverlay(vm);
            e.Handled = true;
        }
    }

    private void CalcUnitPill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is AlternativeMeasurement alt)
        {
            try
            {
                Clipboard.SetText(alt.FormattedValue);
                ShowInlineFeedback($"Copied {alt.FormattedValue} to clipboard! 📋");
            }
            catch { }
            e.Handled = true;
        }
    }

    private void CalcMoreUnitsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is PaletteItemViewModel vm)
        {
            OpenCalculatorUnitsOverlay(vm);
            e.Handled = true;
        }
    }

    private void HeroHierarchicalPaste_Click(object sender, RoutedEventArgs e)
    {
        if (_activeCalcVm != null && !string.IsNullOrWhiteSpace(_activeCalcVm.CalcResult?.HierarchicalBreakdown))
        {
            _activeCalcVm.Item.Payload.SnippetTemplate = _activeCalcVm.CalcResult.HierarchicalBreakdown;
            _activeCalcVm.Item.Name = $"{_activeCalcVm.CalcResult.HierarchicalBreakdown}  ({_activeCalcVm.CalcResult.Expression})";
            CloseCalculatorUnitsOverlay();
            ExecuteCurrentSelection(DetermineOverride());
        }
    }

    private void HeroHierarchicalCopyValue_Click(object sender, RoutedEventArgs e)
    {
        if (_activeCalcVm != null && !string.IsNullOrWhiteSpace(_activeCalcVm.CalcResult?.HierarchicalBreakdown))
        {
            try
            {
                Clipboard.SetText(_activeCalcVm.CalcResult.HierarchicalBreakdown);
                ShowInlineFeedback($"Copied {_activeCalcVm.CalcResult.HierarchicalBreakdown} to clipboard! 📋");
                CloseCalculatorUnitsOverlay();
            }
            catch { }
        }
    }

    private void HeroHierarchicalCopySentence_Click(object sender, RoutedEventArgs e)
    {
        if (_activeCalcVm != null)
        {
            string sentence = _activeCalcVm.CalcResult?.Alternatives?.FirstOrDefault(a => a.Label == "Time Breakdown")?.ConversationalSentence
                              ?? _activeCalcVm.CalcResult?.Description
                              ?? _activeCalcVm.CalcResult?.HierarchicalBreakdown
                              ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(sentence))
            {
                try
                {
                    Clipboard.SetText(sentence);
                    ShowInlineFeedback("Copied sentence to clipboard! 💬");
                    CloseCalculatorUnitsOverlay();
                }
                catch { }
            }
        }
    }

    private void HeroCumulativePaste_Click(object sender, RoutedEventArgs e)
    {
        if (_activeCalcVm != null)
        {
            string val = _activeCalcVm.CalcResult?.CumulativeBreakdown ?? _activeCalcVm.CalcResult?.HierarchicalBreakdown ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(val))
            {
                _activeCalcVm.Item.Payload.SnippetTemplate = val;
                _activeCalcVm.Item.Name = $"{val}  ({_activeCalcVm.CalcResult?.Expression})";
                CloseCalculatorUnitsOverlay();
                ExecuteCurrentSelection(DetermineOverride());
            }
        }
    }

    private void HeroCumulativeCopyValue_Click(object sender, RoutedEventArgs e)
    {
        if (_activeCalcVm != null)
        {
            string val = _activeCalcVm.CalcResult?.CumulativeBreakdown ?? _activeCalcVm.CalcResult?.HierarchicalBreakdown ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(val))
            {
                try
                {
                    Clipboard.SetText(val);
                    ShowInlineFeedback($"Copied {val} to clipboard! 📋");
                    CloseCalculatorUnitsOverlay();
                }
                catch { }
            }
        }
    }

    private void HeroCumulativeCopySentence_Click(object sender, RoutedEventArgs e)
    {
        if (_activeCalcVm != null)
        {
            string sentence = _activeCalcVm.CalcResult?.Alternatives?.FirstOrDefault(a => a.Label == "Days & Time")?.ConversationalSentence
                              ?? _activeCalcVm.CalcResult?.CumulativeBreakdown
                              ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(sentence))
            {
                try
                {
                    Clipboard.SetText(sentence);
                    ShowInlineFeedback("Copied sentence to clipboard! 💬");
                    CloseCalculatorUnitsOverlay();
                }
                catch { }
            }
        }
    }

    private void FlyoutUseUnitBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is AlternativeMeasurement alt && _activeCalcVm != null)
        {
            _activeCalcVm.Item.Payload.SnippetTemplate = alt.FormattedValue;
            _activeCalcVm.Item.Name = $"{alt.FormattedValue}  ({_activeCalcVm.CalcResult?.Expression ?? alt.Label})";
            CloseCalculatorUnitsOverlay();
            ExecuteCurrentSelection(DetermineOverride());
        }
    }

    private void FlyoutCopyValueBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is AlternativeMeasurement alt)
        {
            try
            {
                Clipboard.SetText(alt.FormattedValue);
                ShowInlineFeedback($"Copied {alt.FormattedValue} to clipboard! 📋");
                CloseCalculatorUnitsOverlay();
            }
            catch { }
        }
    }

    private void FlyoutCopySentenceBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is AlternativeMeasurement alt)
        {
            try
            {
                string sentence = !string.IsNullOrWhiteSpace(alt.ConversationalSentence)
                    ? alt.ConversationalSentence
                    : $"{_activeCalcVm?.CalcResult?.Expression ?? "Result"} is {alt.FormattedValue}.";
                Clipboard.SetText(sentence);
                ShowInlineFeedback("Copied sentence to clipboard! 💬");
                CloseCalculatorUnitsOverlay();
            }
            catch { }
        }
    }

    public static List<TriggerItem> ResolveFolderActions(TriggerItem folder, IEnumerable<TriggerItem> allItems, bool includeSubfolders)
    {
        if (folder == null) return [];
        var itemsList = allItems.ToList();
        var result = new List<TriggerItem>();

        if (!includeSubfolders)
        {
            return itemsList
                .Where(x => x.ParentId == folder.Id && x.ActionType != ActionType.Folder && x.IsEnabled)
                .OrderBy(x => x.OrderIndex)
                .ThenBy(x => x.Name)
                .ToList();
        }

        var folderQueue = new Queue<TriggerItem>();
        folderQueue.Enqueue(folder);

        while (folderQueue.Count > 0)
        {
            var currFolder = folderQueue.Dequeue();
            var children = itemsList.Where(x => x.ParentId == currFolder.Id && x.IsEnabled).ToList();

            var actions = children.Where(x => x.ActionType != ActionType.Folder)
                                  .OrderBy(x => x.OrderIndex)
                                  .ThenBy(x => x.Name);
            result.AddRange(actions);

            var subfolders = children.Where(x => x.ActionType == ActionType.Folder)
                                     .OrderBy(x => x.OrderIndex)
                                     .ThenBy(x => x.Name);
            foreach (var sf in subfolders)
            {
                folderQueue.Enqueue(sf);
            }
        }

        return result;
    }

    public static bool FolderHasSubfolders(TriggerItem folder, IEnumerable<TriggerItem> allItems)
    {
        if (folder == null) return false;
        return allItems.Any(x => x.ParentId == folder.Id && x.ActionType == ActionType.Folder);
    }

    private void OpenFolderExecutionConfirmOverlay(TriggerItem folder)
    {
        _folderConfirmTargetFolder = folder;
        bool hasSubfolders = FolderHasSubfolders(folder, _allItems);
        FolderConfirmIncludeSubfoldersCheck.Visibility = hasSubfolders ? Visibility.Visible : Visibility.Collapsed;
        FolderConfirmIncludeSubfoldersCheck.IsChecked = true;

        UpdateFolderConfirmUi();
        FolderExecutionConfirmOverlay.Visibility = Visibility.Visible;
        FolderConfirmRunBtn.Focus();
    }

    private void UpdateFolderConfirmUi()
    {
        if (_folderConfirmTargetFolder == null) return;

        bool includeSubfolders = FolderConfirmIncludeSubfoldersCheck.IsChecked == true;
        _folderConfirmResolvedActions = ResolveFolderActions(_folderConfirmTargetFolder, _allItems, includeSubfolders);

        FolderConfirmTitleText.Text = $"Execute Folder: {_folderConfirmTargetFolder.Name}";
        int count = _folderConfirmResolvedActions.Count;
        FolderConfirmSubtitleText.Text = count == 1
            ? "The following 1 action will be executed:"
            : $"The following {count} actions will be executed sequentially:";
        FolderConfirmRunBtnText.Text = count > 0 ? $"Run All ({count})" : "Run All";
        FolderConfirmRunBtn.IsEnabled = count > 0;

        var previewItems = _folderConfirmResolvedActions.Select(action =>
        {
            string breadcrumb = string.Empty;
            if (action.ParentId.HasValue && action.ParentId.Value != _folderConfirmTargetFolder.Id && _folderPaths.TryGetValue(action.ParentId.Value, out var path))
            {
                breadcrumb = path;
            }
            return new FolderActionPreviewItem
            {
                Item = action,
                PathBreadcrumb = breadcrumb
            };
        }).ToList();

        FolderConfirmActionsList.ItemsSource = previewItems;
    }

    private void CloseFolderExecutionConfirmOverlay()
    {
        FolderExecutionConfirmOverlay.Visibility = Visibility.Collapsed;
        _folderConfirmTargetFolder = null;
        _folderConfirmResolvedActions.Clear();
        SearchTextBox.Focus();
    }

    private void CloseFolderConfirmBtn_Click(object sender, RoutedEventArgs e)
    {
        CloseFolderExecutionConfirmOverlay();
    }

    private void FolderConfirmCancelBtn_Click(object sender, RoutedEventArgs e)
    {
        CloseFolderExecutionConfirmOverlay();
    }

    private void FolderConfirmIncludeSubfoldersCheck_Click(object sender, RoutedEventArgs e)
    {
        UpdateFolderConfirmUi();
    }

    private void FolderConfirmRunBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_folderConfirmResolvedActions.Count == 0)
        {
            CloseFolderExecutionConfirmOverlay();
            return;
        }

        var actionsToRun = _folderConfirmResolvedActions.ToList();
        SafeClose();

        _ = Task.Run(async () =>
        {
            foreach (var action in actionsToRun)
            {
                await _executor.ExecuteAsync(action, ExecutionOverride.Standard, _targetHwnd);
                await Task.Delay(100);
            }
        });
    }
}
