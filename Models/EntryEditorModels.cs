namespace GTA_GXT_Editor.Models;

public sealed record TableOption(string RawName, string DisplayName);

public sealed record EntryEditorRequest(
    bool IsAdding,
    string Name,
    string Text,
    string? RawTableName,
    IReadOnlyList<TableOption> Tables);

public sealed record EntryEditorResult(string Name, string Text, string? RawTableName);
