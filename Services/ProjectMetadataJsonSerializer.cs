using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public static class ProjectMetadataJsonSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Save(string path, ProjectMetadata metadata) =>
        Save(path, metadata, gameType: null);

    public static void Save(string path, ProjectMetadata metadata, GXTType gameType) =>
        Save(path, metadata, (GXTType?)gameType);

    public static ProjectMetadata Load(string path) => Load(path, gameType: null);

    public static ProjectMetadata Load(string path, GXTType gameType) =>
        Load(path, (GXTType?)gameType);

    public static byte[] Serialize(ProjectMetadata metadata) =>
        Serialize(metadata, gameType: null);

    public static byte[] Serialize(ProjectMetadata metadata, GXTType gameType) =>
        Serialize(metadata, (GXTType?)gameType);

    public static ProjectMetadata Deserialize(byte[] data) =>
        Deserialize(data, gameType: null);

    public static ProjectMetadata Deserialize(byte[] data, GXTType gameType) =>
        Deserialize(data, (GXTType?)gameType);

    public static void Validate(ProjectMetadata metadata) =>
        Validate(metadata, gameType: null);

    public static void Validate(ProjectMetadata metadata, GXTType gameType) =>
        Validate(metadata, (GXTType?)gameType);

    private static void Save(string path, ProjectMetadata metadata, GXTType? gameType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllBytes(path, Serialize(metadata, gameType));
    }

    private static ProjectMetadata Load(string path, GXTType? gameType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Deserialize(File.ReadAllBytes(path), gameType);
    }

    private static byte[] Serialize(ProjectMetadata metadata, GXTType? gameType)
    {
        Validate(metadata, gameType);
        return JsonSerializer.SerializeToUtf8Bytes(metadata, JsonOptions);
    }

    private static ProjectMetadata Deserialize(byte[] data, GXTType? gameType)
    {
        ArgumentNullException.ThrowIfNull(data);
        try
        {
            var metadata = JsonSerializer.Deserialize<ProjectMetadata>(data, JsonOptions)
                ?? throw new InvalidDataException(LocalizationProvider.Current.Get("Metadata.NoDocument"));
            Validate(metadata, gameType);
            return metadata;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                LocalizationProvider.Current.Format("Metadata.InvalidJson", exception.Message),
                exception);
        }
    }

    private static void Validate(ProjectMetadata metadata, GXTType? gameType)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (gameType is not null and not (GXTType.GtaIII or GXTType.GtaViceCity))
        {
            throw new ArgumentOutOfRangeException(
                nameof(gameType), gameType,
                LocalizationProvider.Current.Get("Gxt.UnsupportedType"));
        }

        if (!string.Equals(metadata.Format, "GXT_ENTRY_METADATA", StringComparison.Ordinal) ||
            metadata.Version != ProjectMetadata.CurrentVersion ||
            metadata.Blocks is null || metadata.Entries is null)
        {
            throw new InvalidDataException(LocalizationProvider.Current.Get("Metadata.InvalidFile"));
        }

        var blockIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var block in metadata.Blocks)
        {
            if (block is null || string.IsNullOrWhiteSpace(block.Id) ||
                string.IsNullOrWhiteSpace(block.Type) || string.IsNullOrWhiteSpace(block.Name) ||
                !blockIds.Add(block.Id))
            {
                throw new InvalidDataException(LocalizationProvider.Current.Get("Metadata.BlocksCorrupt"));
            }
        }

        var identities = new HashSet<MetadataIdentity>();
        foreach (var entry in metadata.Entries)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Key) ||
                entry.Table is not null && string.IsNullOrWhiteSpace(entry.Table) ||
                entry.Occurrences is null)
            {
                throw new InvalidDataException(LocalizationProvider.Current.Get("Metadata.EntriesCorrupt"));
            }

            ValidateTable(entry, gameType);
            var identity = gameType == GXTType.GtaIII
                ? new MetadataIdentity(null, entry.Key)
                : new MetadataIdentity(entry.Table, entry.Key);
            if (!identities.Add(identity))
            {
                throw new InvalidDataException(LocalizationProvider.Current.Format(
                    "Metadata.Duplicate",
                    FormatIdentity(entry.Table, entry.Key)));
            }

            foreach (var occurrence in entry.Occurrences)
            {
                if (occurrence is null || string.IsNullOrWhiteSpace(occurrence.BlockId) ||
                    !blockIds.Contains(occurrence.BlockId))
                {
                    throw new InvalidDataException(LocalizationProvider.Current.Format(
                        "Metadata.UnknownBlock",
                        FormatIdentity(entry.Table, entry.Key)));
                }
            }
        }
    }

    private static void ValidateTable(ProjectEntryMetadata entry, GXTType? gameType)
    {
        if (gameType == GXTType.GtaViceCity && string.IsNullOrWhiteSpace(entry.Table))
        {
            throw new InvalidDataException(LocalizationProvider.Current.Format(
                "Metadata.ViceCityTableMissing",
                entry.Key));
        }

        if (gameType == GXTType.GtaIII && entry.Table is not null)
        {
            throw new InvalidDataException(LocalizationProvider.Current.Format(
                "Metadata.GtaThirdTableInvalid",
                entry.Key));
        }
    }

    private static string FormatIdentity(string? table, string key) =>
        table is null ? key : $"{table}/{key}";

    private readonly record struct MetadataIdentity(string? Table, string Key);
}
