using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Contracts;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Utils;

namespace GTA_GXT_Editor.Services;

public static class GxtCommentsImporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static GxtCommentsImportResult Import(string sourcePath, EditorProject project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(project);
        var result = Import(
            File.ReadAllBytes(sourcePath),
            project.GameType,
            project.GxtManager,
            project.Metadata);
        if (result.ChangedEntryCount > 0)
        {
            project.IsDirty = true;
        }

        return result;
    }

    public static GxtCommentsImportResult Import(
        byte[] data,
        GXTType gameType,
        CommonGXTManager manager,
        ProjectMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(metadata);
        GxtCommentsExporter.ValidateGameType(gameType);
        ProjectMetadataJsonSerializer.Validate(metadata, gameType);

        var document = Deserialize(data);
        var documentGameType = ParseGame(document.Game);
        if (documentGameType != gameType)
        {
            throw new InvalidDataException(
                $"Файл комментариев предназначен для {document.Game}, а открыт проект " +
                $"{GxtCommentsExporter.ToGameName(gameType)}.");
        }

        var imports = ValidateEntries(document.Entries, gameType);
        var currentEntries = BuildCurrentEntries(gameType, manager);
        var metadataEntries = metadata.Entries.ToDictionary(
            entry => GxtCommentsExporter.CreateIdentity(gameType, entry.Key, entry.Table));
        var missing = new List<GxtEntryIdentity>();
        var mismatches = new List<GxtCommentTextMismatch>();
        var updatedCount = 0;
        var clearedCount = 0;
        var unchangedCount = 0;

        foreach (var import in imports)
        {
            if (!currentEntries.TryGetValue(import.Identity, out var currentText))
            {
                missing.Add(import.Identity);
                continue;
            }

            if (import.Text is not null &&
                !string.Equals(import.Text, currentText, StringComparison.Ordinal))
            {
                mismatches.Add(new GxtCommentTextMismatch(
                    import.Identity,
                    import.Text,
                    currentText));
            }

            metadataEntries.TryGetValue(import.Identity, out var metadataEntry);
            var currentComment = metadataEntry?.Comment;
            if (string.Equals(currentComment, import.Comment, StringComparison.Ordinal))
            {
                unchangedCount++;
                continue;
            }

            if (metadataEntry is null)
            {
                metadataEntry = new ProjectEntryMetadata
                {
                    Key = import.Identity.Key,
                    Table = import.Identity.Table,
                };
                metadata.Entries.Add(metadataEntry);
                metadataEntries.Add(import.Identity, metadataEntry);
            }

            metadataEntry.Comment = import.Comment;
            if (import.Comment is null)
            {
                clearedCount++;
            }
            else
            {
                updatedCount++;
            }
        }

        return new GxtCommentsImportResult(
            imports.Count,
            updatedCount,
            clearedCount,
            unchangedCount,
            missing,
            mismatches);
    }

    private static CommentsImportDocument Deserialize(byte[] data)
    {
        try
        {
            var document = JsonSerializer.Deserialize<CommentsImportDocument>(data, JsonOptions)
                ?? throw new InvalidDataException("JSON комментариев не содержит документа.");
            if (!string.Equals(
                    document.Format,
                    GxtCommentsExporter.FormatName,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Некорректный формат комментариев: ожидается " +
                    $"'{GxtCommentsExporter.FormatName}'.");
            }

            if (document.Version != GxtCommentsExporter.CurrentVersion)
            {
                throw new InvalidDataException(
                    $"Версия файла комментариев {document.Version} не поддерживается.");
            }

            if (document.Entries is null)
            {
                throw new InvalidDataException(
                    "В JSON комментариев отсутствует обязательный массив 'entries'.");
            }

            return document;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Некорректный JSON комментариев: {exception.Message}",
                exception);
        }
    }

    private static List<ValidatedCommentEntry> ValidateEntries(
        List<CommentImportEntry?> entries,
        GXTType gameType)
    {
        var result = new List<ValidatedCommentEntry>(entries.Count);
        var identities = new HashSet<GxtEntryIdentity>();

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var displayIndex = index + 1;
            if (entry is null || string.IsNullOrWhiteSpace(entry.Key))
            {
                throw new InvalidDataException(
                    $"У записи комментариев {displayIndex} отсутствует непустое поле 'key'.");
            }

            ValidateAsciiName(entry.Key, "Ключ", displayIndex);
            string? table = null;
            if (gameType == GXTType.GtaViceCity)
            {
                if (string.IsNullOrWhiteSpace(entry.Table))
                {
                    throw new InvalidDataException(
                        $"У записи комментариев {displayIndex} отсутствует непустое поле 'table'.");
                }

                ValidateAsciiName(entry.Table, "Таблица", displayIndex);
                table = entry.Table;
            }

            var identity = GxtCommentsExporter.CreateIdentity(gameType, entry.Key, table);
            if (!identities.Add(identity))
            {
                throw new InvalidDataException(
                    $"Запись комментариев {displayIndex} дублирует '{identity}'.");
            }

            result.Add(new ValidatedCommentEntry(
                identity,
                entry.Text,
                string.IsNullOrWhiteSpace(entry.Comment) ? null : entry.Comment));
        }

        return result;
    }

    private static Dictionary<GxtEntryIdentity, string> BuildCurrentEntries(
        GXTType gameType,
        CommonGXTManager manager)
    {
        var result = new Dictionary<GxtEntryIdentity, string>();
        foreach (var entry in manager.GXTEntries)
        {
            var table = GxtCommentsExporter.GetTableName(gameType, entry);
            var identity = GxtCommentsExporter.CreateIdentity(
                gameType,
                entry.DatName.GetClearName(),
                table);
            if (!result.TryAdd(
                    identity,
                    manager.ConvertBytesToText(entry.Value).GetClearName()))
            {
                throw new InvalidDataException(
                    $"GXT содержит повторяющуюся запись '{identity}'.");
            }
        }

        return result;
    }

    private static GXTType ParseGame(string game) => game switch
    {
        "GTA III" => GXTType.GtaIII,
        "GTA Vice City" => GXTType.GtaViceCity,
        _ => throw new InvalidDataException(
            $"Игра '{game}' в файле комментариев не поддерживается."),
    };

    private static void ValidateAsciiName(string name, string fieldName, int entryIndex)
    {
        if (name.Length > 8)
        {
            throw new InvalidDataException(
                $"{fieldName} записи комментариев {entryIndex} может содержать не более 8 символов.");
        }

        if (name.Any(character => character is '\0' or > '\u007f'))
        {
            throw new InvalidDataException(
                $"{fieldName} записи комментариев {entryIndex} должен содержать только ASCII-символы без NUL.");
        }
    }

    private sealed class CommentsImportDocument
    {
        public required string Format { get; init; }

        public required int Version { get; init; }

        public required string Game { get; init; }

        public required List<CommentImportEntry?> Entries { get; init; }
    }

    private sealed class CommentImportEntry
    {
        public required string Key { get; init; }

        public string? Table { get; init; }

        public string? Text { get; init; }

        public required string? Comment { get; init; }
    }

    private sealed record ValidatedCommentEntry(
        GxtEntryIdentity Identity,
        string? Text,
        string? Comment);
}
