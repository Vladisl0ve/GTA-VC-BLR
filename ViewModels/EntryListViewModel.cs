using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using GTA_3_GXT_Editor.Utils;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;
using GTA_GXT_Editor.Utils;

namespace GTA_GXT_Editor.ViewModels;

public partial class EntryListViewModel : ObservableObject
{
    private readonly EditorSession _session;
    private readonly ILocalizationService _localization;

    public EntryListViewModel(EditorSession session, ILocalizationService? localization = null)
    {
        _session = session;
        _localization = localization ?? LocalizationProvider.Current;
        EntriesView = CollectionViewSource.GetDefaultView(Entries);
        EntriesView.Filter = FilterEntry;
        SearchColumns = CreateSearchColumns();
        selectedSearchColumn = SearchColumns[0];

        MetadataTypeOptions = CreateMetadataTypeOptions();
        selectedMetadataType = MetadataTypeOptions[0];
        MetadataBlockOptions.Add(CreateAllBlocksOption());
        selectedMetadataBlock = MetadataBlockOptions[0];

        SortOptions = CreateSortOptions();
        selectedSortOption = SortOptions[1];
        ReviewedFilterOptions = CreateReviewedFilterOptions();
        selectedReviewedFilter = ReviewedFilterOptions[0];
        _localization.LanguageChanged += OnLanguageChanged;
    }

    public ObservableCollection<GxtEntryRow> Entries { get; } = [];

    public ObservableCollection<GxtComparisonColumn> ComparisonColumns { get; } = [];

    public ObservableCollection<MetadataBlockFilterOption> MetadataBlockOptions { get; } = [];

    public ICollectionView EntriesView { get; }

    public IReadOnlyList<SearchColumnOption> SearchColumns { get; private set; }

    public IReadOnlyList<MetadataTypeFilterOption> MetadataTypeOptions { get; private set; }

    public IReadOnlyList<EntrySortOption> SortOptions { get; private set; }

    public IReadOnlyList<ReviewedFilterOption> ReviewedFilterOptions { get; private set; }

    public bool HasEncounterMetadata => Entries.Any(entry => entry.Occurrences.Count > 0);

    public int FilteredEntryCount => EntriesView.Cast<object>().Count();

    public string FilteredEntriesText => _localization.Format(
        "Status.FilteredEntries",
        FilteredEntryCount,
        Entries.Count);

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private SearchColumnOption selectedSearchColumn;

    [ObservableProperty]
    private MetadataTypeFilterOption selectedMetadataType;

    [ObservableProperty]
    private MetadataBlockFilterOption selectedMetadataBlock;

    [ObservableProperty]
    private EntrySortOption selectedSortOption;

    [ObservableProperty]
    private ReviewedFilterOption selectedReviewedFilter;

    [ObservableProperty]
    private string commentDraft = string.Empty;

    [ObservableProperty]
    private bool caseSensitive;

    [ObservableProperty]
    private bool liveSearch = true;

    [ObservableProperty]
    private GxtEntryRow? selectedEntry;

    partial void OnSearchTextChanged(string value)
    {
        if (LiveSearch)
        {
            ApplyFilter();
        }
    }

    partial void OnSelectedSearchColumnChanged(SearchColumnOption value)
    {
        if (value is not null && LiveSearch)
        {
            ApplyFilter();
        }
    }

    partial void OnCaseSensitiveChanged(bool value)
    {
        if (LiveSearch)
        {
            ApplyFilter();
        }
    }

    partial void OnLiveSearchChanged(bool value)
    {
        if (value)
        {
            ApplyFilter();
        }
    }

    partial void OnSelectedMetadataTypeChanged(MetadataTypeFilterOption value)
    {
        if (value is null)
        {
            return;
        }

        RebuildMetadataBlockOptions();
        ApplySort();
        ApplyFilter();
    }

    partial void OnSelectedMetadataBlockChanged(MetadataBlockFilterOption value)
    {
        if (value is null)
        {
            return;
        }

        ApplySort();
        ApplyFilter();
    }

    partial void OnSelectedSortOptionChanged(EntrySortOption value)
    {
        if (value is not null)
        {
            ApplySort();
        }
    }

    partial void OnSelectedReviewedFilterChanged(ReviewedFilterOption value)
    {
        if (value is not null)
        {
            ApplyFilter();
        }
    }

    partial void OnSelectedEntryChanged(GxtEntryRow? value)
    {
        CommentDraft = value?.Comment ?? string.Empty;
    }

    public void ApplyFilter()
    {
        EntriesView.Refresh();
        OnPropertyChanged(nameof(FilteredEntryCount));
        OnPropertyChanged(nameof(FilteredEntriesText));
    }

    public void ClearSearch()
    {
        SearchText = string.Empty;
        SelectedSearchColumn = SearchColumns[0];
        SelectedMetadataType = MetadataTypeOptions[0];
        SelectedMetadataBlock = MetadataBlockOptions[0];
        SelectedReviewedFilter = ReviewedFilterOptions[0];
        ApplyFilter();
    }

    public void RefreshEntries(string? selectedName = null, string? selectedTable = null)
    {
        Entries.Clear();
        var manager = _session.Manager;
        if (manager is null)
        {
            OnPropertyChanged(nameof(FilteredEntryCount));
            OnPropertyChanged(nameof(FilteredEntriesText));
            return;
        }

        var projectEntries = (_session.Project?.Metadata.Entries ?? [])
            .ToDictionary(
                entry => CreateMetadataIdentity(entry.Key, entry.Table),
                entry => entry);
        var projectBlocks = (_session.Project?.Metadata.Blocks ?? [])
            .Select((block, index) => new EncounterMetadataBlockIndex(block, index))
            .ToDictionary(block => block.Block.Id, StringComparer.Ordinal);

        var sourceIndex = 0;
        foreach (var entry in manager.GXTEntries)
        {
            var rawTable = (entry as GTAVC.GXTEntry)?.TableName;
            var identity = GetEntryIdentity(entry);
            var metadataIdentity = CreateMetadataIdentity(
                entry.DatName.GetClearName(),
                rawTable?.GetClearName());
            projectEntries.TryGetValue(metadataIdentity, out var projectEntry);
            var occurrences = BuildOccurrences(metadataIdentity, projectEntry, projectBlocks);
            Entries.Add(new GxtEntryRow(
                entry.DatName.GetClearName(),
                manager.ConvertBytesToText(entry.Value).GetClearName(),
                rawTable?.GetClearName() ?? string.Empty,
                rawTable)
            {
                SourceIndex = sourceIndex++,
                ComparisonTexts = _session.Comparisons
                    .Select(document => document.Texts.GetValueOrDefault(identity))
                    .ToArray(),
                Comment = projectEntry?.Comment,
                IsReviewed = projectEntry?.IsReviewed == true,
                Occurrences = occurrences,
                PrimaryOccurrence = occurrences.Length > 0 ? occurrences[0] : null,
            });
        }

        RebuildMetadataBlockOptions();
        ApplySort();
        ApplyFilter();
        SelectedEntry = Entries.FirstOrDefault(entry =>
            string.Equals(entry.Name, selectedName, StringComparison.Ordinal) &&
            string.Equals(entry.RawTableName, selectedTable, StringComparison.Ordinal));
        OnPropertyChanged(nameof(HasEncounterMetadata));
    }

    public void AddOrReplaceComparison(
        string path,
        Dictionary<GxtEntryIdentity, string> texts)
    {
        var existingIndex = _session.Comparisons.FindIndex(document =>
            string.Equals(document.Path, path, StringComparison.OrdinalIgnoreCase));
        var isEnglishSource = existingIndex < 0 && _session.Comparisons.Count == 0;
        var column = new GxtComparisonColumn(
            CreateComparisonColumnName(path, isEnglishSource),
            path);
        var document = new ComparisonDocument(path, texts);
        if (existingIndex >= 0)
        {
            _session.Comparisons[existingIndex] = document;
            ComparisonColumns[existingIndex] = column;
        }
        else
        {
            _session.Comparisons.Add(document);
            ComparisonColumns.Add(column);
        }
    }

    public void ClearComparisons()
    {
        _session.Comparisons.Clear();
        ComparisonColumns.Clear();
    }

    public List<TableOption> GetTableOptions()
    {
        if (_session.LoadedType != GXTType.GtaViceCity || _session.Manager is null)
        {
            return [];
        }

        return _session.Manager.GXTEntries
            .OfType<GTAVC.GXTEntry>()
            .Select(entry => entry.TableName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(table => table, new ASCIIStringComparer())
            .Select(table => new TableOption(table, table.GetClearName()))
            .ToList();
    }

    public bool EntryExists(string name, string? rawTableName) => Entries.Any(entry =>
        string.Equals(entry.Name, name, StringComparison.Ordinal) &&
        string.Equals(entry.RawTableName, rawTableName, StringComparison.Ordinal));

    public bool SetProjectComment(string key, string? rawTableName, string? comment)
    {
        if (_session.Project is not { } project)
        {
            return false;
        }

        var normalizedComment = string.IsNullOrWhiteSpace(comment) ? null : comment;
        var table = _session.LoadedType == GXTType.GtaViceCity
            ? rawTableName?.GetClearName()
            : null;
        var entry = project.Metadata.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, key, StringComparison.Ordinal) &&
            string.Equals(candidate.Table, table, StringComparison.Ordinal));
        if (string.Equals(entry?.Comment, normalizedComment, StringComparison.Ordinal))
        {
            return false;
        }

        if (entry is null)
        {
            if (normalizedComment is null)
            {
                return false;
            }

            entry = new ProjectEntryMetadata { Key = key, Table = table };
            project.Metadata.Entries.Add(entry);
        }

        entry.Comment = normalizedComment;
        if (entry.Comment is null && !entry.IsReviewed && entry.Occurrences.Count == 0)
        {
            project.Metadata.Entries.Remove(entry);
        }

        return true;
    }

    public bool SetProjectReviewed(string key, string? rawTableName, bool isReviewed)
    {
        if (_session.Project is not { } project)
        {
            return false;
        }

        var table = _session.LoadedType == GXTType.GtaViceCity
            ? rawTableName?.GetClearName()
            : null;
        var entry = project.Metadata.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, key, StringComparison.Ordinal) &&
            string.Equals(candidate.Table, table, StringComparison.Ordinal));
        if ((entry?.IsReviewed ?? false) == isReviewed)
        {
            return false;
        }

        if (entry is null)
        {
            entry = new ProjectEntryMetadata { Key = key, Table = table };
            project.Metadata.Entries.Add(entry);
        }

        entry.IsReviewed = isReviewed;
        if (!entry.IsReviewed && entry.Comment is null && entry.Occurrences.Count == 0)
        {
            project.Metadata.Entries.Remove(entry);
        }

        return true;
    }

    public void MoveProjectEntryMetadata(
        string key,
        string? oldRawTableName,
        string? newRawTableName)
    {
        if (_session.Project is not { } project || _session.LoadedType != GXTType.GtaViceCity)
        {
            return;
        }

        var oldTable = oldRawTableName?.GetClearName();
        var newTable = newRawTableName?.GetClearName();
        if (string.Equals(oldTable, newTable, StringComparison.Ordinal))
        {
            return;
        }

        var entry = project.Metadata.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, key, StringComparison.Ordinal) &&
            string.Equals(candidate.Table, oldTable, StringComparison.Ordinal));
        if (entry is not null)
        {
            entry.Table = newTable;
        }
    }

    public void RemoveEmptyProjectMetadataEntries()
    {
        _session.Project?.Metadata.Entries.RemoveAll(entry =>
            !entry.IsReviewed &&
            string.IsNullOrWhiteSpace(entry.Comment) &&
            entry.Occurrences.Count == 0);
    }

    public bool HasPersistableProjectMetadata() =>
        _session.Project?.Metadata.Blocks.Count > 0 ||
        _session.Project?.Metadata.Entries.Any(entry =>
            entry.IsReviewed ||
            !string.IsNullOrWhiteSpace(entry.Comment) ||
            entry.Occurrences.Count > 0) == true;

    public bool CanSaveComment() =>
        SelectedEntry is not null &&
        !string.Equals(
            string.IsNullOrWhiteSpace(CommentDraft) ? null : CommentDraft,
            SelectedEntry.Comment,
            StringComparison.Ordinal);

    public bool CanClearComment() =>
        SelectedEntry is not null &&
        (!string.IsNullOrWhiteSpace(SelectedEntry.Comment) ||
         !string.IsNullOrWhiteSpace(CommentDraft));

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        var searchColumn = SelectedSearchColumn?.Column ?? SearchColumn.All;
        var metadataType = SelectedMetadataType?.Type;
        var sortMode = SelectedSortOption?.Mode ?? EntrySortMode.GxtOrder;
        var reviewedFilter = SelectedReviewedFilter?.Mode ?? ReviewedFilterMode.All;
        var selectedName = SelectedEntry?.Name;
        var selectedTable = SelectedEntry?.RawTableName;

        SearchColumns = CreateSearchColumns();
        MetadataTypeOptions = CreateMetadataTypeOptions();
        SortOptions = CreateSortOptions();
        ReviewedFilterOptions = CreateReviewedFilterOptions();
        OnPropertyChanged(nameof(SearchColumns));
        OnPropertyChanged(nameof(MetadataTypeOptions));
        OnPropertyChanged(nameof(SortOptions));
        OnPropertyChanged(nameof(ReviewedFilterOptions));

        SelectedSearchColumn = SearchColumns.First(option => option.Column == searchColumn);
        SelectedMetadataType = MetadataTypeOptions.First(option => option.Type == metadataType);
        SelectedSortOption = SortOptions.First(option => option.Mode == sortMode);
        SelectedReviewedFilter = ReviewedFilterOptions.First(option => option.Mode == reviewedFilter);
        RebuildComparisonColumns();

        if (_session.Manager is null)
        {
            RebuildMetadataBlockOptions();
            OnPropertyChanged(nameof(FilteredEntriesText));
            return;
        }

        RefreshEntries(selectedName, selectedTable);
    }

    private SearchColumnOption[] CreateSearchColumns() =>
    [
        new(SearchColumn.All, _localization.Get("Options.Search.All")),
        new(SearchColumn.Name, _localization.Get("Options.Search.Name")),
        new(SearchColumn.Text, _localization.Get("Options.Search.Text")),
        new(SearchColumn.Source, _localization.Get("Options.Search.Source")),
        new(SearchColumn.Comparison, _localization.Get("Options.Search.Comparison")),
        new(SearchColumn.Table, _localization.Get("Options.Search.Table")),
        new(SearchColumn.Metadata, _localization.Get("Options.Search.Metadata")),
        new(SearchColumn.Comment, _localization.Get("Options.Search.Comment")),
    ];

    private MetadataTypeFilterOption[] CreateMetadataTypeOptions() =>
    [
        new(null, _localization.Get("Options.Metadata.AllTypes")),
        new("story", _localization.Get("Options.Metadata.Story")),
        new("mission", _localization.Get("Options.Metadata.Missions")),
        new("asset", _localization.Get("Options.Metadata.Assets")),
        new("phone", _localization.Get("Options.Metadata.Phone")),
        new("interface", _localization.Get("Options.Metadata.Interface")),
        new("world", _localization.Get("Options.Metadata.World")),
        new("credits", _localization.Get("Options.Metadata.Credits")),
        new("misc", _localization.Get("Options.Metadata.Misc")),
    ];

    private EntrySortOption[] CreateSortOptions() =>
    [
        new(EntrySortMode.EncounterOrder, _localization.Get("Options.Sort.Encounter")),
        new(EntrySortMode.GxtOrder, _localization.Get("Options.Sort.Gxt")),
    ];

    private ReviewedFilterOption[] CreateReviewedFilterOptions() =>
    [
        new(ReviewedFilterMode.All, _localization.Get("Options.Reviewed.All")),
        new(ReviewedFilterMode.ReviewedOnly, _localization.Get("Options.Reviewed.Reviewed")),
        new(ReviewedFilterMode.UnreviewedOnly, _localization.Get("Options.Reviewed.Unreviewed")),
    ];

    private MetadataBlockFilterOption CreateAllBlocksOption() =>
        new(null, _localization.Get("Options.Metadata.AllBlocks"), null);

    private string GetMetadataTypeName(string type) =>
        MetadataTypeOptions.FirstOrDefault(option => option.Type == type)?.Name ?? type;

    private void RebuildComparisonColumns()
    {
        ComparisonColumns.Clear();
        foreach (var document in _session.Comparisons)
        {
            ComparisonColumns.Add(new GxtComparisonColumn(
                CreateComparisonColumnName(document.Path, ComparisonColumns.Count == 0),
                document.Path));
        }
    }

    private GxtEntryOccurrenceView[] BuildOccurrences(
        EncounterMetadataIdentity identity,
        ProjectEntryMetadata? projectEntry,
        IReadOnlyDictionary<string, EncounterMetadataBlockIndex> projectBlocks)
    {
        ProjectEntryMetadata? occurrenceEntry;
        IReadOnlyDictionary<string, EncounterMetadataBlockIndex> blocks;
        EncounterMetadataSource source;
        if (projectEntry is { Occurrences.Count: > 0 })
        {
            occurrenceEntry = projectEntry;
            blocks = projectBlocks;
            source = EncounterMetadataSource.Project;
        }
        else if (_session.CanonicalMetadata?.Entries.TryGetValue(identity, out var canonicalEntry) == true)
        {
            occurrenceEntry = canonicalEntry;
            blocks = _session.CanonicalMetadata.Blocks;
            source = EncounterMetadataSource.Canonical;
        }
        else
        {
            return [];
        }

        var result = new List<GxtEntryOccurrenceView>(occurrenceEntry.Occurrences.Count);
        foreach (var occurrence in occurrenceEntry.Occurrences)
        {
            if (!blocks.TryGetValue(occurrence.BlockId, out var block))
            {
                continue;
            }

            result.Add(new GxtEntryOccurrenceView(
                block.Block.Id,
                block.Block.Type,
                block.Block.Name,
                block.Block.Description,
                block.Block.Order,
                block.Sequence,
                occurrence.Order,
                occurrence.Context,
                source));
        }

        return result
            .OrderBy(occurrence => occurrence.BlockOrder)
            .ThenBy(occurrence => occurrence.BlockSequence)
            .ThenBy(occurrence => occurrence.OccurrenceOrder)
            .ThenBy(occurrence => occurrence.BlockId, StringComparer.Ordinal)
            .ToArray();
    }

    private EncounterMetadataIdentity CreateMetadataIdentity(string key, string? table) =>
        new(_session.LoadedType == GXTType.GtaViceCity ? table : null, key);

    private void RebuildMetadataBlockOptions()
    {
        var selectedId = SelectedMetadataBlock?.Id;
        var selectedType = SelectedMetadataType?.Type;
        var blocks = Entries
            .SelectMany(entry => entry.Occurrences)
            .Where(occurrence => selectedType is null ||
                string.Equals(occurrence.BlockType, selectedType, StringComparison.Ordinal))
            .GroupBy(occurrence => occurrence.BlockId, StringComparer.Ordinal)
            .Select(group => group
                .OrderBy(occurrence => occurrence.Source == EncounterMetadataSource.Project ? 0 : 1)
                .ThenBy(occurrence => occurrence.BlockSequence)
                .First())
            .OrderBy(occurrence => occurrence.BlockOrder)
            .ThenBy(occurrence => occurrence.BlockSequence)
            .ThenBy(occurrence => occurrence.BlockName, StringComparer.Ordinal)
            .ToArray();

        MetadataBlockOptions.Clear();
        MetadataBlockOptions.Add(CreateAllBlocksOption());
        foreach (var block in blocks)
        {
            MetadataBlockOptions.Add(new MetadataBlockFilterOption(
                block.BlockId,
                $"{block.BlockName} · {GetMetadataTypeName(block.BlockType)}",
                block.BlockType));
        }

        SelectedMetadataBlock = MetadataBlockOptions.FirstOrDefault(option =>
            string.Equals(option.Id, selectedId, StringComparison.Ordinal)) ??
            MetadataBlockOptions[0];
    }

    private void ApplySort()
    {
        if (EntriesView is ListCollectionView view &&
            SelectedSortOption is { } sortOption &&
            SelectedMetadataBlock is { } metadataBlock &&
            SelectedMetadataType is { } metadataType)
        {
            view.CustomSort = new GxtEntryRowComparer(
                sortOption.Mode,
                metadataBlock.Id,
                metadataType.Type);
        }
    }

    private bool FilterEntry(object item)
    {
        if (item is not GxtEntryRow entry)
        {
            return false;
        }

        if (SelectedMetadataType?.Type is { } selectedType &&
            !entry.Occurrences.Any(occurrence =>
                string.Equals(occurrence.BlockType, selectedType, StringComparison.Ordinal)))
        {
            return false;
        }

        if (SelectedMetadataBlock?.Id is { } selectedBlockId &&
            !entry.Occurrences.Any(occurrence =>
                string.Equals(occurrence.BlockId, selectedBlockId, StringComparison.Ordinal)))
        {
            return false;
        }

        if (SelectedReviewedFilter?.Mode == ReviewedFilterMode.ReviewedOnly && !entry.IsReviewed)
        {
            return false;
        }

        if (SelectedReviewedFilter?.Mode == ReviewedFilterMode.UnreviewedOnly && entry.IsReviewed)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var comparison = CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        bool Contains(string? value) => value?.Contains(SearchText, comparison) == true;
        bool ContainsMetadata() => entry.Occurrences.Any(occurrence =>
            Contains(occurrence.BlockId) ||
            Contains(occurrence.BlockType) ||
            Contains(occurrence.BlockName) ||
            Contains(occurrence.BlockDescription) ||
            Contains(occurrence.Context));

        return (SelectedSearchColumn?.Column ?? SearchColumn.All) switch
        {
            SearchColumn.Name => Contains(entry.Name),
            SearchColumn.Text => Contains(entry.Text),
            SearchColumn.Source => Contains(entry.SourceText),
            SearchColumn.Comparison => entry.ComparisonTexts.Any(Contains),
            SearchColumn.Table => Contains(entry.Table),
            SearchColumn.Metadata => ContainsMetadata(),
            SearchColumn.Comment => Contains(entry.Comment),
            _ => Contains(entry.Name) ||
                 Contains(entry.Text) ||
                 entry.ComparisonTexts.Any(Contains) ||
                 Contains(entry.Table) ||
                 ContainsMetadata() ||
                 Contains(entry.Comment),
        };
    }

    private string CreateComparisonColumnName(string path, bool isEnglishSource)
    {
        var normalizedName = Path.GetFileNameWithoutExtension(path).Trim();
        if (string.IsNullOrEmpty(normalizedName))
        {
            normalizedName = "GXT";
        }

        var sourcePrefix = _localization.Get("Comparison.EnglishPrefix");
        var usedNames = ComparisonColumns
            .Select(column => column.Name.StartsWith(sourcePrefix, StringComparison.Ordinal)
                ? column.Name[sourcePrefix.Length..]
                : column.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!usedNames.Contains(normalizedName))
        {
            return isEnglishSource ? sourcePrefix + normalizedName : normalizedName;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{normalizedName} ({suffix})";
            if (!usedNames.Contains(candidate))
            {
                return isEnglishSource ? sourcePrefix + candidate : candidate;
            }
        }
    }

    private GxtEntryIdentity GetEntryIdentity(GXTBase entry) =>
        GxtDomainRules.CreateIdentity(
            _session.LoadedType,
            entry.DatName.GetClearName(),
            GxtDomainRules.GetTableName(_session.LoadedType, entry));

    private sealed class GxtEntryRowComparer(
        EntrySortMode mode,
        string? selectedBlockId,
        string? selectedBlockType) : IComparer
    {
        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is not GxtEntryRow left)
            {
                return -1;
            }

            if (y is not GxtEntryRow right)
            {
                return 1;
            }

            if (mode == EntrySortMode.GxtOrder)
            {
                return left.SourceIndex.CompareTo(right.SourceIndex);
            }

            var leftOccurrence = SelectOccurrence(left);
            var rightOccurrence = SelectOccurrence(right);
            if (leftOccurrence is null || rightOccurrence is null)
            {
                if (leftOccurrence is not null)
                {
                    return -1;
                }

                if (rightOccurrence is not null)
                {
                    return 1;
                }

                return left.SourceIndex.CompareTo(right.SourceIndex);
            }

            var result = leftOccurrence.BlockOrder.CompareTo(rightOccurrence.BlockOrder);
            if (result == 0)
            {
                result = leftOccurrence.BlockSequence.CompareTo(rightOccurrence.BlockSequence);
            }

            if (result == 0)
            {
                result = leftOccurrence.OccurrenceOrder.CompareTo(rightOccurrence.OccurrenceOrder);
            }

            return result != 0 ? result : left.SourceIndex.CompareTo(right.SourceIndex);
        }

        private GxtEntryOccurrenceView? SelectOccurrence(GxtEntryRow entry) =>
            selectedBlockId is not null
                ? entry.Occurrences.FirstOrDefault(occurrence =>
                    string.Equals(occurrence.BlockId, selectedBlockId, StringComparison.Ordinal))
                : selectedBlockType is not null
                    ? entry.Occurrences.FirstOrDefault(occurrence =>
                        string.Equals(
                            occurrence.BlockType,
                            selectedBlockType,
                            StringComparison.Ordinal))
                    : entry.PrimaryOccurrence;
    }
}
