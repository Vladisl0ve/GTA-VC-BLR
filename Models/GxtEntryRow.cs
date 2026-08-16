using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Models;

public sealed record GxtEntryRow(
    string Name,
    string Text,
    string Table,
    string? RawTableName)
{
    public int SourceIndex { get; init; }

    public IReadOnlyList<string?> ComparisonTexts { get; init; } = [];

    public string? SourceText => ComparisonTexts.Count > 0 ? ComparisonTexts[0] : null;

    public string? Comment { get; init; }

    public IReadOnlyList<GxtEntryOccurrenceView> Occurrences { get; init; } = [];

    public GxtEntryOccurrenceView? PrimaryOccurrence { get; init; }

    public string EncounterSummary => PrimaryOccurrence is null
        ? string.Empty
        : Occurrences.Count > 1
            ? $"{PrimaryOccurrence.Summary} (+{Occurrences.Count - 1})"
            : PrimaryOccurrence.Summary;

    public string EncounterToolTip => Occurrences.Count == 0
        ? LocalizationProvider.Current.Get("Metadata.None")
        : string.Join("\n\n", Occurrences.Select(occurrence => occurrence.ToolTip));

    public string CommentPreview => string.IsNullOrWhiteSpace(Comment) ? string.Empty : Comment;
}
