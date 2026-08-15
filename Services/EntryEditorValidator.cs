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
            var message = nameError switch
            {
                GxtNameValidationError.Empty => "Имя ключа не может быть пустым.",
                GxtNameValidationError.TooLong =>
                    $"Имя ключа может содержать не более " +
                    $"{GxtDomainRules.MaximumNameLength} символов.",
                _ => "Имя ключа должно содержать только ASCII-символы без NUL.",
            };
            return new EntryEditorValidationResult(null, message, EntryEditorField.Name);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return new EntryEditorValidationResult(
                null,
                "Текст ключа не может быть пустым.",
                EntryEditorField.Text);
        }

        if (request.Tables.Count > 0 && table is null)
        {
            return new EntryEditorValidationResult(
                null,
                "Выберите таблицу.",
                EntryEditorField.Table);
        }

        if (table is not null &&
            GxtDomainRules.GetNameValidationError(table.TrimEnd('\0')) is not GxtNameValidationError.None)
        {
            return new EntryEditorValidationResult(
                null,
                "Имя таблицы должно содержать от 1 до 8 ASCII-символов без NUL.",
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
