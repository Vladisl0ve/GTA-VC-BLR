using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Models;

public enum EncounterMetadataSource
{
    Canonical,
    Project,
}

public enum EntrySortMode
{
    EncounterOrder,
    GxtOrder,
}

public enum ReviewedFilterMode
{
    All,
    ReviewedOnly,
    UnreviewedOnly,
}

public sealed record EntrySortOption(EntrySortMode Mode, string Name);

public sealed record ReviewedFilterOption(ReviewedFilterMode Mode, string Name);

public sealed record MetadataTypeFilterOption(string? Type, string Name);

public sealed record MetadataBlockFilterOption(string? Id, string Name, string? Type);

public sealed record GxtEntryOccurrenceView(
    string BlockId,
    string BlockType,
    string BlockName,
    string? BlockDescription,
    int BlockOrder,
    int BlockSequence,
    int OccurrenceOrder,
    string? Context,
    EncounterMetadataSource Source)
{
    public string SourceName => Source == EncounterMetadataSource.Canonical
        ? LocalizationProvider.Current.Get("Common.Source.Canonical")
        : LocalizationProvider.Current.Get("Common.Source.Project");

    public string OrderText => $"{BlockOrder} / {OccurrenceOrder}";

    public string Summary => string.IsNullOrWhiteSpace(Context)
        ? BlockName
        : $"{BlockName} — {Context}";

    public string ToolTip => LocalizationProvider.Current.Format(
        "Metadata.ToolTip",
        BlockId,
        BlockType,
        OrderText,
        SourceName);
}
