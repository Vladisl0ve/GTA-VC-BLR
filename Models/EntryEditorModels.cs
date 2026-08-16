namespace GTA_GXT_Editor.Models;

public sealed record TableOption(string RawName, string DisplayName);

public sealed record EntryEditorRequest(
    bool IsAdding,
    string Name,
    string Text,
    string? RawTableName,
    IReadOnlyList<TableOption> Tables)
{
    public string? SourceText { get; init; }

    public string? Comment { get; init; }

    public IReadOnlyList<GxtEntryOccurrenceView> Occurrences { get; init; } = [];
}

public sealed record EntryEditorResult(string Name, string Text, string? RawTableName)
{
    public string? Comment { get; init; }
}
