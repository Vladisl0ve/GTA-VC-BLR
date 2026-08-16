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
        var documentGameType = GxtDomainRules.ParseCanonicalGameName(
            document.Game,
            LocalizationProvider.Current.Get("Comments.Context"));
        if (documentGameType != gameType)
        {
            throw new InvalidDataException(LocalizationProvider.Current.Format(
                "Comments.GameMismatch",
                document.Game,
                GxtCommentsExporter.ToGameName(gameType)));
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
                ?? throw new InvalidDataException(LocalizationProvider.Current.Get("Comments.NoDocument"));
            if (!string.Equals(
                    document.Format,
                    GxtCommentsExporter.FormatName,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(LocalizationProvider.Current.Format(
                    "Comments.InvalidFormat",
                    GxtCommentsExporter.FormatName));
            }

            if (document.Version != GxtCommentsExporter.CurrentVersion)
            {
                throw new InvalidDataException(LocalizationProvider.Current.Format(
                    "Comments.UnsupportedVersion",
                    document.Version));
            }

            if (document.Entries is null)
            {
                throw new InvalidDataException(LocalizationProvider.Current.Get("Comments.EntriesMissing"));
            }

            return document;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                LocalizationProvider.Current.Format("Comments.InvalidJson", exception.Message),
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
                throw new InvalidDataException(LocalizationProvider.Current.Format(
                    "Comments.KeyMissing",
                    displayIndex));
            }

            ValidateAsciiName(entry.Key, LocalizationProvider.Current.Get("Json.FieldKey"), displayIndex);
            string? table = null;
            if (gameType == GXTType.GtaViceCity)
            {
                if (string.IsNullOrWhiteSpace(entry.Table))
                {
                    throw new InvalidDataException(LocalizationProvider.Current.Format(
                        "Comments.TableMissing",
                        displayIndex));
                }

                ValidateAsciiName(entry.Table, LocalizationProvider.Current.Get("Json.FieldTable"), displayIndex);
                table = entry.Table;
            }

            var identity = GxtCommentsExporter.CreateIdentity(gameType, entry.Key, table);
            if (!identities.Add(identity))
            {
                throw new InvalidDataException(LocalizationProvider.Current.Format(
                    "Comments.Duplicate",
                    displayIndex,
                    identity));
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
                throw new InvalidDataException(LocalizationProvider.Current.Format(
                    "Document.DuplicateEntry",
                    identity));
            }
        }

        return result;
    }

    private static void ValidateAsciiName(string name, string fieldName, int entryIndex)
    {
        if (GxtDomainRules.GetNameValidationError(name) == GxtNameValidationError.TooLong)
        {
            throw new InvalidDataException(LocalizationProvider.Current.Format(
                "Comments.NameTooLong",
                fieldName,
                entryIndex,
                GxtDomainRules.MaximumNameLength));
        }

        if (GxtDomainRules.GetNameValidationError(name) == GxtNameValidationError.NonAscii)
        {
            throw new InvalidDataException(LocalizationProvider.Current.Format(
                "Comments.NameNonAscii",
                fieldName,
                entryIndex));
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
