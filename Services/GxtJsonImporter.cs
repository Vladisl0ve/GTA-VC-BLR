using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GTA_GXT_Editor.Common;

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

        var document = ReadDocument(sourcePath);
        var type = ParseGame(document.Game);
        var entries = ValidateEntries(document.Entries, type);
        var manager = GxtManagerFactory.Create(
            type,
            dictionaryPath,
            document.Source,
            entries.Select(entry => entry.Text));

        foreach (var entry in entries)
        {
            try
            {
                manager.AddGXTEntry(entry.Key, entry.Text, entry.Table);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
            {
                throw new InvalidDataException(
                    $"Не удалось преобразовать запись {entry.Index} с ключом '{entry.Key}': {exception.Message}",
                    exception);
            }
        }

        SaveAtomically(manager.SaveGXTChanges, targetPath);
        return new GxtJsonImportResult(type, entries.Count);
    }

    private static JsonGxtDocument ReadDocument(string sourcePath)
    {
        try
        {
            using var stream = File.OpenRead(sourcePath);
            return JsonSerializer.Deserialize<JsonGxtDocument>(stream, SerializerOptions) ??
                   throw new InvalidDataException("JSON-файл не содержит документа GXT.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Некорректный JSON: {exception.Message}",
                exception);
        }
    }

    private static GXTType ParseGame(string? game)
    {
        return game?.Trim().ToUpperInvariant() switch
        {
            "GTA III" or "GTA 3" or "GTA3" => GXTType.GtaIII,
            "GTA VICE CITY" or "VICE CITY" or "GTAVC" => GXTType.GtaViceCity,
            null or "" => throw new InvalidDataException("В JSON отсутствует обязательное поле 'game'."),
            _ => throw new InvalidDataException(
                $"Игра '{game}' не поддерживается. Ожидается 'GTA III' или 'GTA Vice City'."),
        };
    }

    private static List<ValidatedEntry> ValidateEntries(
        List<JsonGxtEntry?>? sourceEntries,
        GXTType type)
    {
        if (sourceEntries is null)
        {
            throw new InvalidDataException("В JSON отсутствует обязательный массив 'entries'.");
        }

        var result = new List<ValidatedEntry>(sourceEntries.Count);
        var identities = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < sourceEntries.Count; index++)
        {
            var sourceEntry = sourceEntries[index];
            var displayIndex = index + 1;
            if (sourceEntry is null)
            {
                throw new InvalidDataException($"Запись {displayIndex} не может быть null.");
            }

            var key = sourceEntry.Key;
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidDataException($"У записи {displayIndex} отсутствует непустое поле 'key'.");
            }

            ValidateAsciiName(key, "Ключ", displayIndex);
            if (sourceEntry.Text is null)
            {
                throw new InvalidDataException($"У записи {displayIndex} отсутствует поле 'text'.");
            }

            string? table = null;
            if (type == GXTType.GtaViceCity)
            {
                table = string.IsNullOrWhiteSpace(sourceEntry.Table) ? "MAIN" : sourceEntry.Table;
                ValidateAsciiName(table, "Таблица", displayIndex);
            }

            var identity = type == GXTType.GtaViceCity
                ? $"{table}\u001f{key}"
                : key;
            if (!identities.Add(identity))
            {
                throw new InvalidDataException(
                    $"Запись {displayIndex} дублирует ключ '{key}'" +
                    (table is null ? "." : $" в таблице '{table}'."));
            }

            result.Add(new ValidatedEntry(displayIndex, key, sourceEntry.Text, table));
        }

        return result;
    }

    private static void ValidateAsciiName(string name, string fieldName, int entryIndex)
    {
        if (name.Length > 8)
        {
            throw new InvalidDataException(
                $"{fieldName} записи {entryIndex} может содержать не более 8 символов.");
        }

        if (name.Any(character => character is '\0' or > '\u007f'))
        {
            throw new InvalidDataException(
                $"{fieldName} записи {entryIndex} должен содержать только ASCII-символы без NUL.");
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

    private sealed record ValidatedEntry(
        int Index,
        string Key,
        string Text,
        string? Table);
}

public sealed record GxtJsonImportResult(GXTType Type, int EntryCount);
