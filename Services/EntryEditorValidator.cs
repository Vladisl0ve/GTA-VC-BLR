using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public enum EntryEditorField
{
    Name,
    Text,
    Table,
}

public sealed record EntryEditorValidationResult(
    EntryEditorResult? Result,
    string? ErrorMessage,
    EntryEditorField? ErrorField)
{
    public bool IsValid => Result is not null;
}

public static class EntryEditorValidator
{
    public static EntryEditorValidationResult Validate(
        EntryEditorRequest request,
        string name,
        string text,
        string? table,
        string? comment)
    {
        ArgumentNullException.ThrowIfNull(request);
        name = name.Trim();
        var nameError = GxtDomainRules.GetNameValidationError(name);
        if (nameError != GxtNameValidationError.None)
        {
            var localization = LocalizationProvider.Current;
            var message = nameError switch
            {
                GxtNameValidationError.Empty => localization.Get("Validation.Name.Empty"),
                GxtNameValidationError.TooLong => localization.Format(
                    "Validation.Name.TooLong",
                    GxtDomainRules.MaximumNameLength),
                _ => localization.Get("Validation.Name.Invalid"),
            };
            return new EntryEditorValidationResult(null, message, EntryEditorField.Name);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return new EntryEditorValidationResult(
                null,
                LocalizationProvider.Current.Get("Validation.Text.Empty"),
                EntryEditorField.Text);
        }

        if (request.Tables.Count > 0 && table is null)
        {
            return new EntryEditorValidationResult(
                null,
                LocalizationProvider.Current.Get("Validation.Table.Required"),
                EntryEditorField.Table);
        }

        if (table is not null &&
            GxtDomainRules.GetNameValidationError(table.TrimEnd('\0')) is not GxtNameValidationError.None)
        {
            return new EntryEditorValidationResult(
                null,
                LocalizationProvider.Current.Get("Validation.Table.Invalid"),
                EntryEditorField.Table);
        }

        return new EntryEditorValidationResult(
            new EntryEditorResult(name, text, table)
            {
                Comment = string.IsNullOrWhiteSpace(comment) ? null : comment,
            },
            null,
            null);
    }
}
