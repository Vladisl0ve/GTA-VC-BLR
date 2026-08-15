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

public sealed record EntrySortOption(EntrySortMode Mode, string Name);

public sealed record MetadataTypeFilterOption(string? Type, string Name)
{
    public static MetadataTypeFilterOption All { get; } = new(null, "Все типы");
}

public sealed record MetadataBlockFilterOption(string? Id, string Name, string? Type)
{
    public static MetadataBlockFilterOption All { get; } = new(null, "Все блоки", null);
}

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
        ? "Canonical"
        : "Project";

    public string OrderText => $"{BlockOrder} / {OccurrenceOrder}";

    public string Summary => string.IsNullOrWhiteSpace(Context)
        ? BlockName
        : $"{BlockName} — {Context}";

    public string ToolTip =>
        $"{BlockId}\nТип: {BlockType}\nПорядок: {OrderText}\nИсточник: {SourceName}";
}
