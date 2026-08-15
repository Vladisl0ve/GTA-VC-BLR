namespace GTA_GXT_Editor.Models;

public sealed record GxtEntryIdentity(string Key, string? Table = null)
{
    public override string ToString() =>
        Table is null ? Key : $"{Table}/{Key}";
}

public sealed record GxtCommentTextMismatch(
    GxtEntryIdentity Identity,
    string ImportedText,
    string CurrentText);

public sealed record GxtCommentsImportResult(
    int ListedEntryCount,
    int UpdatedEntryCount,
    int ClearedEntryCount,
    int UnchangedEntryCount,
    IReadOnlyList<GxtEntryIdentity> MissingEntries,
    IReadOnlyList<GxtCommentTextMismatch> TextMismatches)
{
    public int ChangedEntryCount => UpdatedEntryCount + ClearedEntryCount;
}
