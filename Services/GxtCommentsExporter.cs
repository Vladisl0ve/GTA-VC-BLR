using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Contracts;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Utils;

namespace GTA_GXT_Editor.Services;

public static class GxtCommentsExporter
{
    public const string FormatName = "GXT_COMMENTS";
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Export(
        string targetPath,
        EditorProject project,
        bool includeText = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(project);
        File.WriteAllBytes(targetPath, Serialize(project, includeText));
    }

    public static byte[] Serialize(EditorProject project, bool includeText = true)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Serialize(
            project.GameType,
            project.GxtManager,
            project.Metadata,
            includeText);
    }

    public static byte[] Serialize(
        GXTType gameType,
        CommonGXTManager manager,
        ProjectMetadata metadata,
        bool includeText = true)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(metadata);
        ValidateGameType(gameType);
        ProjectMetadataJsonSerializer.Validate(metadata, gameType);

        var comments = metadata.Entries.ToDictionary(
            entry => CreateIdentity(gameType, entry.Key, entry.Table),
            entry => entry.Comment);
        var identities = new HashSet<GxtEntryIdentity>();
        var entries = new List<CommentExportEntry>(manager.GXTEntries.Count);

        foreach (var gxtEntry in manager.GXTEntries)
        {
            var table = GetTableName(gameType, gxtEntry);
            var key = gxtEntry.DatName.GetClearName();
            var identity = CreateIdentity(gameType, key, table);
            if (!identities.Add(identity))
            {
                throw new InvalidDataException(
                    $"GXT содержит повторяющуюся запись '{identity}'.");
            }

            comments.TryGetValue(identity, out var comment);
            entries.Add(new CommentExportEntry
            {
                Key = key,
                Table = table,
                Text = includeText
                    ? manager.ConvertBytesToText(gxtEntry.Value).GetClearName()
                    : null,
                Comment = comment,
            });
        }

        var document = new CommentsExportDocument
        {
            Format = FormatName,
            Version = CurrentVersion,
            Game = ToGameName(gameType),
            Entries = entries,
        };
        return JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
    }

    internal static GxtEntryIdentity CreateIdentity(
        GXTType gameType,
        string key,
        string? table)
    {
        ValidateGameType(gameType);
        return gameType == GXTType.GtaViceCity
            ? new GxtEntryIdentity(key, table)
            : new GxtEntryIdentity(key);
    }

    internal static string? GetTableName(GXTType gameType, GXTBase entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return (gameType, entry) switch
        {
            (GXTType.GtaIII, GTAIII.GXTEntry) => null,
            (GXTType.GtaViceCity, GTAVC.GXTEntry viceCityEntry) =>
                viceCityEntry.TableName.GetClearName(),
            _ => throw new InvalidDataException(
                $"Тип записи '{entry.GetType().Name}' не соответствует типу проекта " +
                $"{ToGameName(gameType)}."),
        };
    }

    internal static string ToGameName(GXTType gameType) => gameType switch
    {
        GXTType.GtaIII => "GTA III",
        GXTType.GtaViceCity => "GTA Vice City",
        _ => throw new ArgumentOutOfRangeException(nameof(gameType), gameType, "Неподдерживаемый тип GXT."),
    };

    internal static void ValidateGameType(GXTType gameType)
    {
        if (gameType is not (GXTType.GtaIII or GXTType.GtaViceCity))
        {
            throw new ArgumentOutOfRangeException(nameof(gameType), gameType, "Неподдерживаемый тип GXT.");
        }
    }

    private sealed class CommentsExportDocument
    {
        public required string Format { get; init; }

        public required int Version { get; init; }

        public required string Game { get; init; }

        public required List<CommentExportEntry> Entries { get; init; }
    }

    private sealed class CommentExportEntry
    {
        public required string Key { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Table { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Text { get; init; }

        public string? Comment { get; init; }
    }
}
