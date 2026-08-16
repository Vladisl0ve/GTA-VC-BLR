namespace GTA_GXT_Editor.Models;

public enum SearchColumn
{
    All,
    Name,
    Text,
    Source,
    Comparison,
    Table,
    Metadata,
    Comment,
}

public sealed record SearchColumnOption(SearchColumn Column, string Title)
;
