using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Contracts;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Utils;

namespace GTA_GXT_Editor.Services;

public static class GxtJsonImporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static GxtJsonImportResult Import(
        string sourcePath,
        string targetPath,
        string? dictionaryPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var payload = Load(sourcePath);
        var manager = CreateManager(payload, dictionaryPath);
        SaveAtomically(manager.SaveGXTChanges, targetPath);
        return ToResult(payload, manager);
    }

    public static GxtJsonImportResult ImportInto(
        string sourcePath,
        EditorProject project,
        string? dictionaryPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(project);

        var payload = Load(sourcePath);
        if (payload.Type != project.GameType)
        {
            throw new InvalidDataException(LocalizationProvider.Current.Get("Message.JsonGameTypeMismatch"));
        }

        var existingManager = project.GxtManager;
        var manager = CreateManager(
            payload,
            dictionaryPath,
            project.GxtSourceName,
            dictionaryPath is null ? existingManager.CharacterMap : null,
            dictionaryPath is null ? existingManager.Language : payload.Language);
        if (dictionaryPath is null)
        {
            manager.CharacterMapPath = existingManager.CharacterMapPath;
        }

        project.GxtManager = manager;
        if (dictionaryPath is not null)
        {
            project.UsesCustomDictionary = true;
            project.CharacterMap = manager.CharacterMap.Clone();
        }

        PruneMissingMetadata(project, payload.Type, manager);
        project.IsDirty = true;
        return ToResult(payload, manager);
    }

    private static JsonGxtPayload Load(string sourcePath)
    {
        var document = ReadDocument(sourcePath);
        var type = GxtDomainRules.ParseJsonGameName(document.Game);
        var language = GxtDomainRules.ParseFlexibleLanguageCode(document.Language);
        var entries = ValidateEntries(document.Entries, type);
        return new JsonGxtPayload(type, language, document.Source, entries);
    }

    private static CommonGXTManager CreateManager(
        JsonGxtPayload payload,
        string? dictionaryPath,
        string? sourceName = null,
        CharacterMapProfile? characterMap = null,
        GxtLanguage? language = null)
    {
        var manager = GxtManagerFactory.Create(
            payload.Type,
            dictionaryPath,
            sourceName ?? payload.Source,
            payload.Entries.Select(entry => entry.Text),
            language ?? payload.Language);

        if (characterMap is not null)
        {
            manager.CharacterMap = characterMap;
        }

        foreach (var entry in payload.Entries)
        {
            try
            {
                manager.AddGXTEntry(entry.Key, entry.Text, entry.Table);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
            {
                throw new InvalidDataException(LocalizationProvider.Current.Format(
                    "Json.ConvertEntryFailed",
                    entry.Index,
                    entry.Key,
                    exception.Message), exception);
            }
        }

        return manager;
    }

    private static GxtJsonImportResult ToResult(JsonGxtPayload payload, CommonGXTManager manager) =>
        new(payload.Type, payload.Entries.Count)
        {
            Language = manager.Language,
        };

    private static void PruneMissingMetadata(
        EditorProject project,
        GXTType type,
        CommonGXTManager manager)
    {
        var remaining = new HashSet<GxtEntryIdentity>();
        foreach (var entry in manager.GXTEntries)
        {
            remaining.Add(GxtDomainRules.CreateIdentity(
                type,
                entry.DatName.GetClearName(),
                GxtDomainRules.GetTableName(type, entry)));
        }

        project.Metadata.Entries.RemoveAll(entry =>
            !remaining.Contains(GxtDomainRules.CreateIdentity(type, entry.Key, entry.Table)));
    }

    private static JsonGxtDocument ReadDocument(string sourcePath)
    {
        try
        {
            using var stream = File.OpenRead(sourcePath);
            return JsonSerializer.Deserialize<JsonGxtDocument>(stream, SerializerOptions) ??
                   throw new InvalidDataException(LocalizationProvider.Current.Get("Json.NoGxtDocument"));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                LocalizationProvider.Current.Format("Json.Invalid", exception.Message),
                exception);
        }
    }

    private static List<ValidatedEntry> ValidateEntries(
        List<JsonGxtEntry?>? sourceEntries,
        GXTType type)
    {
        if (sourceEntries is null)
        {
            throw new InvalidDataException(LocalizationProvider.Current.Get("Json.EntriesMissing"));
        }

        var result = new List<ValidatedEntry>(sourceEntries.Count);
        var identities = new HashSet<GxtEntryIdentity>();

        for (var index = 0; index < sourceEntries.Count; index++)
        {
            var sourceEntry = sourceEntries[index];
            var displayIndex = index + 1;
            if (sourceEntry is null)
            {
                throw new InvalidDataException(LocalizationProvider.Current.Format("Json.EntryNull", displayIndex));
            }

            var key = sourceEntry.Key;
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidDataException(LocalizationProvider.Current.Format("Json.KeyMissing", displayIndex));
            }

            ValidateAsciiName(key, LocalizationProvider.Current.Get("Json.FieldKey"), displayIndex);
            if (sourceEntry.Text is null)
            {
                throw new InvalidDataException(LocalizationProvider.Current.Format("Json.TextMissing", displayIndex));
            }

            string? table = null;
            if (type == GXTType.GtaViceCity)
            {
                table = string.IsNullOrWhiteSpace(sourceEntry.Table) ? "MAIN" : sourceEntry.Table;
                ValidateAsciiName(table, LocalizationProvider.Current.Get("Json.FieldTable"), displayIndex);
            }

            var identity = GxtDomainRules.CreateIdentity(type, key, table);
            if (!identities.Add(identity))
            {
                throw new InvalidDataException(table is null
                    ? LocalizationProvider.Current.Format("Json.DuplicateKey", displayIndex, key)
                    : LocalizationProvider.Current.Format("Json.DuplicateTableKey", displayIndex, key, table));
            }

            result.Add(new ValidatedEntry(displayIndex, key, sourceEntry.Text, table));
        }

        return result;
    }

    private static void ValidateAsciiName(string name, string fieldName, int entryIndex)
    {
        if (GxtDomainRules.GetNameValidationError(name) == GxtNameValidationError.TooLong)
        {
            throw new InvalidDataException(LocalizationProvider.Current.Format(
                "Json.NameTooLong",
                fieldName,
                entryIndex,
                GxtDomainRules.MaximumNameLength));
        }

        if (GxtDomainRules.GetNameValidationError(name) == GxtNameValidationError.NonAscii)
        {
            throw new InvalidDataException(LocalizationProvider.Current.Format(
                "Json.NameNonAscii",
                fieldName,
                entryIndex));
        }
    }

    private static void SaveAtomically(Action<string> save, string targetPath)
    {
        var fullTargetPath = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullTargetPath) ?? Environment.CurrentDirectory;
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullTargetPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            save(temporaryPath);
            File.Move(temporaryPath, fullTargetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed class JsonGxtDocument
    {
        [JsonPropertyName("game")]
        public string? Game { get; init; }

        [JsonPropertyName("source")]
        public string? Source { get; init; }

        [JsonPropertyName("language")]
        public string? Language { get; init; }

        [JsonPropertyName("entries")]
        public List<JsonGxtEntry?>? Entries { get; init; }
    }

    private sealed class JsonGxtEntry
    {
        [JsonPropertyName("key")]
        public string? Key { get; init; }

        [JsonPropertyName("text")]
        public string? Text { get; init; }

        [JsonPropertyName("table")]
        public string? Table { get; init; }
    }

    private sealed record JsonGxtPayload(
        GXTType Type,
        GxtLanguage Language,
        string? Source,
        List<ValidatedEntry> Entries);

    private sealed record ValidatedEntry(
        int Index,
        string Key,
        string Text,
        string? Table);
}

public sealed record GxtJsonImportResult(GXTType Type, int EntryCount)
{
    public GxtLanguage Language { get; init; }
}
