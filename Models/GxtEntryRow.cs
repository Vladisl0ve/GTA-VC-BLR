namespace GTA_GXT_Editor.Models;

public sealed record GxtEntryRow(
    string Name,
    string Text,
    string Table,
    string? RawTableName)
{
    public IReadOnlyList<string?> ComparisonTexts { get; init; } = [];
}
