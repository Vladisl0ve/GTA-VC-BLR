using System.IO.Compression;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public sealed class ByxProjectSerializer : IProjectSerializer
{
    private const string ManifestEntryName = "manifest.json";
    private const string GxtEntryName = "gxt/main.gxt";
    private const string DictionaryEntryName = "dictionary/characters.json";
    private const long MaximumArchiveSize = 512L * 1024 * 1024;
    private const long MaximumManifestSize = 1024 * 1024;
    private const int MaximumEntries = 64;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly GxtManagerFactory _gxtManagerFactory;
    private readonly ITxdReader _txdReader;

    public ByxProjectSerializer(GxtManagerFactory gxtManagerFactory, ITxdReader txdReader)
    {
        _gxtManagerFactory = gxtManagerFactory;
        _txdReader = txdReader;
    }

    public EditorProject Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException("Файл проекта не найден.", path);
        }

        if (file.Length > MaximumArchiveSize)
        {
            throw new InvalidDataException("Проект BYX превышает допустимый размер 512 МБ.");
        }

        using var stream = File.OpenRead(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        ValidateArchive(archive);

        var manifestEntry = GetRequiredEntry(archive, ManifestEntryName);
        if (manifestEntry.Length > MaximumManifestSize)
        {
            throw new InvalidDataException("Manifest проекта BYX слишком велик.");
        }

        var manifest = Deserialize<ByxManifest>(ReadEntry(manifestEntry, MaximumManifestSize));
        ValidateManifest(manifest);
        ValidateManifestEntries(archive, manifest);

        var gameType = manifest.Game switch
        {
            "GTA III" => GXTType.GtaIII,
            "GTA Vice City" => GXTType.GtaViceCity,
            _ => throw new InvalidDataException($"Игра '{manifest.Game}' в BYX не поддерживается."),
        };
        var language = ParseLanguage(manifest.Language);
        var gxtEntry = GetRequiredEntry(archive, manifest.Gxt.Entry);
        var gxtData = ReadEntry(gxtEntry, MaximumArchiveSize);
        ValidateHash(gxtData, manifest.Gxt.Sha256, manifest.Gxt.Entry);

        Dictionary<int[], char>? characterDictionary = null;
        if (manifest.Dictionary is not null)
        {
            var dictionaryEntry = GetRequiredEntry(archive, manifest.Dictionary.Entry);
            var dictionaryData = ReadEntry(dictionaryEntry, MaximumManifestSize);
            ValidateHash(dictionaryData, manifest.Dictionary.Sha256, manifest.Dictionary.Entry);
            var dictionaryItems = Deserialize<List<ByxCharacterMapping>>(dictionaryData);
            characterDictionary = ParseDictionary(dictionaryItems);
        }

        var manager = _gxtManagerFactory.Open(
            gxtData,
            gameType,
            manifest.Gxt.OriginalFileName,
            language,
            characterDictionary);
        var project = new EditorProject
        {
            ProjectPath = path,
            GxtSourceName = manifest.Gxt.OriginalFileName,
            GxtSourcePath = null,
            GameType = gameType,
            GxtManager = manager,
            UsesCustomDictionary = characterDictionary is not null,
            IsDirty = false,
        };

        foreach (var item in manifest.Txd)
        {
            if (gameType != GXTType.GtaViceCity)
            {
                throw new InvalidDataException("TXD можно подключать только к проекту GTA Vice City.");
            }

            var txdEntry = GetRequiredEntry(archive, item.Entry);
            var data = ReadEntry(txdEntry, MaximumArchiveSize);
            ValidateHash(data, item.Sha256, item.Entry);
            var document = _txdReader.Read(data, item.OriginalFileName);
            project.TxdAttachments.Add(new TxdAttachment
            {
                Id = item.Id,
                OriginalFileName = item.OriginalFileName,
                DisplayName = string.IsNullOrWhiteSpace(item.DisplayName)
                    ? Path.GetFileNameWithoutExtension(item.OriginalFileName)
                    : item.DisplayName,
                SourcePath = null,
                Data = data,
                Document = document,
            });
        }

        return project;
    }

    public void Save(string path, EditorProject project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(project);
        if (project.GameType is not (GXTType.GtaIII or GXTType.GtaViceCity))
        {
            throw new InvalidOperationException("Проект содержит неподдерживаемый тип GXT.");
        }

        if (project.GameType != GXTType.GtaViceCity && project.TxdAttachments.Count > 0)
        {
            throw new InvalidOperationException("TXD можно подключать только к проекту GTA Vice City.");
        }

        if (project.TxdAttachments.Any(attachment => attachment.Id == Guid.Empty) ||
            project.TxdAttachments.Select(attachment => attachment.Id).Distinct().Count() !=
            project.TxdAttachments.Count)
        {
            throw new InvalidOperationException("Проект содержит пустые или повторяющиеся идентификаторы TXD.");
        }

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Не удалось определить каталог проекта.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using var gxtStream = new MemoryStream();
            project.GxtManager.WriteGXT(gxtStream);
            var gxtData = gxtStream.ToArray();
            byte[]? dictionaryData = null;
            if (project.UsesCustomDictionary)
            {
                var mappings = project.GxtManager.CyrillicCharsDictionary
                    .Select(pair => new ByxCharacterMapping
                    {
                        Codes = pair.Key,
                        Character = pair.Value.ToString(),
                    })
                    .OrderBy(mapping => mapping.Codes.Min())
                    .ToList();
                dictionaryData = JsonSerializer.SerializeToUtf8Bytes(mappings, JsonOptions);
            }

            var txdManifestItems = project.TxdAttachments.Select(attachment => new ByxTxdItem
            {
                Id = attachment.Id,
                OriginalFileName = SanitizeFileName(attachment.OriginalFileName, "fonts.txd"),
                DisplayName = attachment.DisplayName,
                Entry = $"txd/{attachment.Id:N}.txd",
                Sha256 = ComputeHash(attachment.Data),
            }).ToList();
            var manifest = new ByxManifest
            {
                Format = "BYX",
                Version = 1,
                Game = project.GameType == GXTType.GtaIII ? "GTA III" : "GTA Vice City",
                Language = ToLanguageCode(project.GxtManager.Language),
                Gxt = new ByxGxtItem
                {
                    OriginalFileName = SanitizeFileName(project.GxtSourceName, "main.gxt"),
                    Entry = GxtEntryName,
                    Sha256 = ComputeHash(gxtData),
                },
                Dictionary = project.UsesCustomDictionary
                    ? new ByxDictionaryItem
                    {
                        Entry = DictionaryEntryName,
                        Sha256 = ComputeHash(dictionaryData!),
                    }
                    : null,
                Txd = txdManifestItems,
            };
            var manifestData = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
            var entryCount = 2 + project.TxdAttachments.Count + (dictionaryData is null ? 0 : 1);
            var totalLength = checked(
                gxtData.LongLength +
                manifestData.LongLength +
                (dictionaryData?.LongLength ?? 0) +
                project.TxdAttachments.Sum(attachment => (long)attachment.Data.Length));
            if (entryCount > MaximumEntries || manifestData.LongLength > MaximumManifestSize ||
                totalLength > MaximumArchiveSize)
            {
                throw new InvalidOperationException("Проект превышает допустимые лимиты формата BYX.");
            }

            using (var fileStream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.ReadWrite,
                       FileShare.None))
            using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false))
            {
                WriteEntry(archive, GxtEntryName, gxtData, CompressionLevel.Optimal);
                for (var index = 0; index < project.TxdAttachments.Count; index++)
                {
                    WriteEntry(
                        archive,
                        txdManifestItems[index].Entry,
                        project.TxdAttachments[index].Data,
                        CompressionLevel.Optimal);
                }

                if (manifest.Dictionary is not null && dictionaryData is not null)
                {
                    WriteEntry(
                        archive,
                        DictionaryEntryName,
                        dictionaryData,
                        CompressionLevel.Optimal);
                }

                WriteEntry(
                    archive,
                    ManifestEntryName,
                    manifestData,
                    CompressionLevel.Optimal);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void ValidateArchive(ZipArchive archive)
    {
        if (archive.Entries.Count > MaximumEntries)
        {
            throw new InvalidDataException("В проекте BYX слишком много записей.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalLength = 0;
        foreach (var entry in archive.Entries)
        {
            ValidateEntryName(entry.FullName);
            if (!names.Add(entry.FullName))
            {
                throw new InvalidDataException($"Запись '{entry.FullName}' повторяется в BYX.");
            }

            totalLength = checked(totalLength + entry.Length);
            if (totalLength > MaximumArchiveSize)
            {
                throw new InvalidDataException("Распакованные данные BYX превышают 512 МБ.");
            }
        }
    }

    private static void ValidateManifest(ByxManifest manifest)
    {
        if (!string.Equals(manifest.Format, "BYX", StringComparison.Ordinal) || manifest.Version != 1)
        {
            throw new InvalidDataException(
                manifest.Version > 1
                    ? $"Версия BYX {manifest.Version} пока не поддерживается."
                    : "Некорректная версия проекта BYX.");
        }

        if (manifest.Gxt is null || string.IsNullOrWhiteSpace(manifest.Gxt.OriginalFileName) ||
            manifest.Txd is null)
        {
            throw new InvalidDataException("Manifest проекта BYX заполнен не полностью.");
        }

        if (!string.Equals(manifest.Gxt.Entry, GxtEntryName, StringComparison.Ordinal) ||
            !IsFileNameOnly(manifest.Gxt.OriginalFileName))
        {
            throw new InvalidDataException("Manifest BYX содержит некорректное вложение GXT.");
        }

        if (manifest.Dictionary is not null &&
            !string.Equals(manifest.Dictionary.Entry, DictionaryEntryName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Manifest BYX содержит некорректный путь словаря.");
        }

        var ids = new HashSet<Guid>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ManifestEntryName,
            manifest.Gxt.Entry,
        };
        if (manifest.Dictionary is not null && !paths.Add(manifest.Dictionary.Entry))
        {
            throw new InvalidDataException("Manifest BYX содержит повторяющиеся пути вложений.");
        }

        foreach (var txd in manifest.Txd)
        {
            if (txd.Id == Guid.Empty || !ids.Add(txd.Id) ||
                !IsFileNameOnly(txd.OriginalFileName) ||
                !string.Equals(txd.Entry, $"txd/{txd.Id:N}.txd", StringComparison.Ordinal) ||
                !paths.Add(txd.Entry))
            {
                throw new InvalidDataException("Manifest BYX содержит некорректные или повторяющиеся TXD.");
            }
        }
    }

    private static void ValidateManifestEntries(ZipArchive archive, ByxManifest manifest)
    {
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ManifestEntryName,
            manifest.Gxt.Entry,
        };
        if (manifest.Dictionary is not null)
        {
            expected.Add(manifest.Dictionary.Entry);
        }

        foreach (var txd in manifest.Txd)
        {
            expected.Add(txd.Entry);
        }

        if (!expected.SetEquals(archive.Entries.Select(entry => entry.FullName)))
        {
            throw new InvalidDataException("Набор записей BYX не соответствует manifest.json.");
        }
    }

    private static bool IsFileNameOnly(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        string.Equals(value, Path.GetFileName(value), StringComparison.Ordinal) &&
        value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static Dictionary<int[], char> ParseDictionary(List<ByxCharacterMapping> items)
    {
        var result = new Dictionary<int[], char>();
        var usedCodes = new HashSet<int>();
        foreach (var item in items)
        {
            if (item.Codes is null || item.Codes.Length == 0 ||
                item.Codes.Any(code => code is < byte.MinValue or > byte.MaxValue) ||
                item.Codes.Any(code => !usedCodes.Add(code)) ||
                string.IsNullOrEmpty(item.Character) || item.Character.Length != 1)
            {
                throw new InvalidDataException("Встроенный словарь BYX повреждён.");
            }

            result.Add(item.Codes.ToArray(), item.Character[0]);
        }

        return result;
    }

    private static T Deserialize<T>(byte[] data) where T : class =>
        JsonSerializer.Deserialize<T>(data, JsonOptions)
        ?? throw new InvalidDataException("JSON внутри BYX повреждён.");

    private static ZipArchiveEntry GetRequiredEntry(ZipArchive archive, string name)
    {
        ValidateEntryName(name);
        return archive.Entries.SingleOrDefault(
                   entry => string.Equals(entry.FullName, name, StringComparison.Ordinal))
               ?? throw new InvalidDataException($"В BYX отсутствует запись '{name}'.");
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry, long maximumLength)
    {
        if (entry.Length > maximumLength || entry.Length > int.MaxValue)
        {
            throw new InvalidDataException($"Запись '{entry.FullName}' слишком велика.");
        }

        using var stream = entry.Open();
        var result = new byte[(int)entry.Length];
        stream.ReadExactly(result);
        return result;
    }

    private static void WriteEntry(
        ZipArchive archive,
        string name,
        byte[] data,
        CompressionLevel compressionLevel)
    {
        var entry = archive.CreateEntry(name, compressionLevel);
        using var stream = entry.Open();
        stream.Write(data);
    }

    private static void ValidateEntryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\\') || name.Contains(':') ||
            name.StartsWith('/') ||
            name.Split('/').Any(part => part is "" or "." or ".."))
        {
            throw new InvalidDataException($"Небезопасное имя записи BYX: '{name}'.");
        }
    }

    private static void ValidateHash(byte[] data, string expected, string entryName)
    {
        if (string.IsNullOrWhiteSpace(expected) ||
            !string.Equals(ComputeHash(data), expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Контрольная сумма записи '{entryName}' не совпадает.");
        }
    }

    private static string ComputeHash(byte[] data) =>
        Convert.ToHexStringLower(SHA256.HashData(data));

    private static string SanitizeFileName(string value, string fallback)
    {
        var name = Path.GetFileName(value);
        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

    private static string ToLanguageCode(GxtLanguage language) => language switch
    {
        GxtLanguage.Belarusian => "be",
        GxtLanguage.Russian => "ru",
        GxtLanguage.Ukrainian => "uk",
        _ => "en",
    };

    private static GxtLanguage ParseLanguage(string? language) => language?.ToLowerInvariant() switch
    {
        "be" => GxtLanguage.Belarusian,
        "ru" => GxtLanguage.Russian,
        "uk" => GxtLanguage.Ukrainian,
        "en" => GxtLanguage.English,
        _ => throw new InvalidDataException($"Язык '{language}' в BYX не поддерживается."),
    };

}
