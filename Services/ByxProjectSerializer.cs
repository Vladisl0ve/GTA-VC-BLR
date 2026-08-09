using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Contracts;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public sealed class ByxProjectSerializer : IProjectSerializer
{
    private const int CurrentVersion = 2;
    private const string ManifestEntryName = "manifest.json";
    private const string GxtEntryName = "gxt/main.gxt";
    private const string DictionaryEntryName = "dictionary/characters.json";
    private const string CharacterMapEntryName = "mapping/characters.json";
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

        var manifestData = ReadEntry(manifestEntry, MaximumManifestSize);
        var header = Deserialize<ByxManifestHeader>(manifestData);
        if (!string.Equals(header.Format, "BYX", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Некорректный формат проекта BYX.");
        }

        return header.Version switch
        {
            1 => LoadVersion1(path, archive, Deserialize<ByxManifestV1>(manifestData)),
            CurrentVersion => LoadVersion2(path, archive, Deserialize<ByxManifest>(manifestData)),
            > CurrentVersion => throw new InvalidDataException(
                $"Версия BYX {header.Version} пока не поддерживается."),
            _ => throw new InvalidDataException("Некорректная версия проекта BYX."),
        };
    }

    public void Save(string path, EditorProject project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(project);
        if (project.GameType is not (GXTType.GtaIII or GXTType.GtaViceCity))
        {
            throw new InvalidOperationException("Проект содержит неподдерживаемый тип GXT.");
        }

        if (project.GameType != GXTType.GtaViceCity && project.AttachedTxd is not null)
        {
            throw new InvalidOperationException("TXD можно подключать только к проекту GTA Vice City.");
        }

        if (project.AttachedTxd?.Id == Guid.Empty)
        {
            throw new InvalidOperationException("Проект содержит пустой идентификатор TXD.");
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

            var profile = project.CharacterMap?.Clone();
            byte[]? characterMapData = null;
            byte[]? dictionaryData = null;
            ByxDictionaryItem? dictionaryItem = null;
            ByxTxdItem? txdItem = null;
            if (project.AttachedTxd is { } attachment)
            {
                profile ??= CharacterMapProfile.FromDictionary(
                    project.GxtManager.CyrillicCharsDictionary,
                    isVerified: false);
                characterMapData = CharacterMapFileSerializer.Serialize(profile);
                txdItem = new ByxTxdItem
                {
                    Id = attachment.Id,
                    OriginalFileName = SanitizeFileName(attachment.OriginalFileName, "fonts.txd"),
                    DisplayName = attachment.DisplayName,
                    Entry = $"txd/{attachment.Id:N}.txd",
                    Sha256 = ComputeHash(attachment.Data),
                    CharacterMap = new ByxDictionaryItem
                    {
                        Entry = CharacterMapEntryName,
                        Sha256 = ComputeHash(characterMapData),
                    },
                    CharacterMapVerified = profile.IsVerified,
                };
            }
            else if (project.UsesCustomDictionary)
            {
                profile ??= CharacterMapProfile.FromDictionary(
                    project.GxtManager.CyrillicCharsDictionary,
                    isVerified: false);
                dictionaryData = CharacterMapFileSerializer.Serialize(profile);
                dictionaryItem = new ByxDictionaryItem
                {
                    Entry = DictionaryEntryName,
                    Sha256 = ComputeHash(dictionaryData),
                };
            }

            var manifest = new ByxManifest
            {
                Format = "BYX",
                Version = CurrentVersion,
                Game = project.GameType == GXTType.GtaIII ? "GTA III" : "GTA Vice City",
                Language = ToLanguageCode(project.GxtManager.Language),
                Gxt = new ByxGxtItem
                {
                    OriginalFileName = SanitizeFileName(project.GxtSourceName, "main.gxt"),
                    Entry = GxtEntryName,
                    Sha256 = ComputeHash(gxtData),
                },
                Dictionary = dictionaryItem,
                Txd = txdItem,
            };
            var manifestData = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
            var entryCount = 2 + (txdItem is null ? 0 : 2) + (dictionaryItem is null ? 0 : 1);
            var totalLength = checked(
                gxtData.LongLength +
                manifestData.LongLength +
                (project.AttachedTxd?.Data.LongLength ?? 0) +
                (characterMapData?.LongLength ?? 0) +
                (dictionaryData?.LongLength ?? 0));
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
                if (project.AttachedTxd is { } attached && txdItem is not null &&
                    characterMapData is not null)
                {
                    WriteEntry(archive, txdItem.Entry, attached.Data, CompressionLevel.Optimal);
                    WriteEntry(
                        archive,
                        CharacterMapEntryName,
                        characterMapData,
                        CompressionLevel.Optimal);
                }

                if (dictionaryData is not null)
                {
                    WriteEntry(
                        archive,
                        DictionaryEntryName,
                        dictionaryData,
                        CompressionLevel.Optimal);
                }

                WriteEntry(archive, ManifestEntryName, manifestData, CompressionLevel.Optimal);
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

    private EditorProject LoadVersion1(
        string path,
        ZipArchive archive,
        ByxManifestV1 manifest)
    {
        ValidateManifestVersion1(manifest);
        ValidateManifestEntriesVersion1(archive, manifest);
        if (manifest.Txd.Count > 1)
        {
            throw new InvalidDataException(
                "BYX v1 содержит несколько TXD. Эта версия приложения поддерживает только один TXD на GXT.");
        }

        var dictionary = LoadLegacyDictionary(archive, manifest.Dictionary);
        var manager = LoadManager(archive, manifest.Game, manifest.Language, manifest.Gxt, dictionary);
        var gameType = ParseGame(manifest.Game);
        var attachment = manifest.Txd.SingleOrDefault() is { } item
            ? LoadTxd(archive, item, gameType)
            : null;
        var profile = attachment is null
            ? null
            : CharacterMapProfile.FromDictionary(
                dictionary ?? manager.CyrillicCharsDictionary,
                isVerified: false);

        return new EditorProject
        {
            ProjectPath = path,
            GxtSourceName = manifest.Gxt.OriginalFileName,
            GxtSourcePath = null,
            GameType = gameType,
            GxtManager = manager,
            UsesCustomDictionary = dictionary is not null || profile is not null,
            AttachedTxd = attachment,
            CharacterMap = profile,
            IsDirty = false,
        };
    }

    private EditorProject LoadVersion2(
        string path,
        ZipArchive archive,
        ByxManifest manifest)
    {
        ValidateManifestVersion2(manifest);
        ValidateManifestEntriesVersion2(archive, manifest);

        CharacterMapProfile? profile = null;
        Dictionary<int[], char>? dictionary = null;
        if (manifest.Txd?.CharacterMap is { } mapItem)
        {
            profile = LoadProfile(archive, mapItem);
            profile.IsVerified = manifest.Txd.CharacterMapVerified;
            dictionary = profile.ToCharacterDictionary();
        }
        else if (manifest.Dictionary is not null)
        {
            profile = LoadProfile(archive, manifest.Dictionary);
            profile.IsVerified = true;
            dictionary = profile.ToCharacterDictionary();
        }

        var manager = LoadManager(archive, manifest.Game, manifest.Language, manifest.Gxt, dictionary);
        var gameType = ParseGame(manifest.Game);
        var attachment = manifest.Txd is null ? null : LoadTxd(archive, manifest.Txd, gameType);
        return new EditorProject
        {
            ProjectPath = path,
            GxtSourceName = manifest.Gxt.OriginalFileName,
            GxtSourcePath = null,
            GameType = gameType,
            GxtManager = manager,
            UsesCustomDictionary = profile is not null,
            AttachedTxd = attachment,
            CharacterMap = profile,
            IsDirty = false,
        };
    }

    private CommonGXTManager LoadManager(
        ZipArchive archive,
        string game,
        string languageText,
        ByxGxtItem item,
        Dictionary<int[], char>? dictionary)
    {
        var gameType = ParseGame(game);
        var language = ParseLanguage(languageText);
        var entry = GetRequiredEntry(archive, item.Entry);
        var data = ReadEntry(entry, MaximumArchiveSize);
        ValidateHash(data, item.Sha256, item.Entry);
        return _gxtManagerFactory.Open(data, gameType, item.OriginalFileName, language, dictionary);
    }

    private TxdAttachment LoadTxd(ZipArchive archive, ByxTxdItem item, GXTType gameType)
    {
        if (gameType != GXTType.GtaViceCity)
        {
            throw new InvalidDataException("TXD можно подключать только к проекту GTA Vice City.");
        }

        var entry = GetRequiredEntry(archive, item.Entry);
        var data = ReadEntry(entry, MaximumArchiveSize);
        ValidateHash(data, item.Sha256, item.Entry);
        return new TxdAttachment
        {
            Id = item.Id,
            OriginalFileName = item.OriginalFileName,
            DisplayName = string.IsNullOrWhiteSpace(item.DisplayName)
                ? Path.GetFileNameWithoutExtension(item.OriginalFileName)
                : item.DisplayName,
            SourcePath = null,
            Data = data,
            Document = _txdReader.Read(data, item.OriginalFileName),
        };
    }

    private static CharacterMapProfile LoadProfile(
        ZipArchive archive,
        ByxDictionaryItem item)
    {
        var entry = GetRequiredEntry(archive, item.Entry);
        var data = ReadEntry(entry, MaximumManifestSize);
        ValidateHash(data, item.Sha256, item.Entry);
        return CharacterMapFileSerializer.Deserialize(data);
    }

    private static Dictionary<int[], char>? LoadLegacyDictionary(
        ZipArchive archive,
        ByxDictionaryItem? item)
    {
        if (item is null)
        {
            return null;
        }

        var entry = GetRequiredEntry(archive, item.Entry);
        var data = ReadEntry(entry, MaximumManifestSize);
        ValidateHash(data, item.Sha256, item.Entry);
        return ParseLegacyDictionary(Deserialize<List<ByxCharacterMapping>>(data));
    }

    private static void ValidateManifestVersion1(ByxManifestV1 manifest)
    {
        if (!string.Equals(manifest.Format, "BYX", StringComparison.Ordinal) || manifest.Version != 1 ||
            manifest.Gxt is null || manifest.Txd is null)
        {
            throw new InvalidDataException("Manifest проекта BYX v1 заполнен не полностью.");
        }

        ValidateGxtItem(manifest.Gxt);
        if (manifest.Dictionary is not null &&
            !string.Equals(manifest.Dictionary.Entry, DictionaryEntryName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Manifest BYX содержит некорректный путь словаря.");
        }

        var ids = new HashSet<Guid>();
        var paths = CreateBaseManifestPaths(manifest.Gxt, manifest.Dictionary);
        foreach (var txd in manifest.Txd)
        {
            ValidateTxdItem(txd, ids, paths, requireCharacterMap: false);
        }
    }

    private static void ValidateManifestVersion2(ByxManifest manifest)
    {
        if (!string.Equals(manifest.Format, "BYX", StringComparison.Ordinal) ||
            manifest.Version != CurrentVersion || manifest.Gxt is null)
        {
            throw new InvalidDataException("Manifest проекта BYX v2 заполнен не полностью.");
        }

        ValidateGxtItem(manifest.Gxt);
        if (manifest.Dictionary is not null && manifest.Txd is not null)
        {
            throw new InvalidDataException("BYX v2 не может одновременно хранить отдельный словарь и TXD-профиль.");
        }

        var paths = CreateBaseManifestPaths(manifest.Gxt, manifest.Dictionary);
        if (manifest.Dictionary is not null &&
            !string.Equals(manifest.Dictionary.Entry, DictionaryEntryName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Manifest BYX содержит некорректный путь словаря.");
        }

        if (manifest.Txd is not null)
        {
            ValidateTxdItem(manifest.Txd, [], paths, requireCharacterMap: true);
        }
    }

    private static void ValidateGxtItem(ByxGxtItem item)
    {
        if (string.IsNullOrWhiteSpace(item.OriginalFileName) ||
            !string.Equals(item.Entry, GxtEntryName, StringComparison.Ordinal) ||
            !IsFileNameOnly(item.OriginalFileName))
        {
            throw new InvalidDataException("Manifest BYX содержит некорректное вложение GXT.");
        }
    }

    private static HashSet<string> CreateBaseManifestPaths(
        ByxGxtItem gxt,
        ByxDictionaryItem? dictionary)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ManifestEntryName,
            gxt.Entry,
        };
        if (dictionary is not null && !paths.Add(dictionary.Entry))
        {
            throw new InvalidDataException("Manifest BYX содержит повторяющиеся пути вложений.");
        }

        return paths;
    }

    private static void ValidateTxdItem(
        ByxTxdItem txd,
        HashSet<Guid> ids,
        HashSet<string> paths,
        bool requireCharacterMap)
    {
        if (txd.Id == Guid.Empty || !ids.Add(txd.Id) || !IsFileNameOnly(txd.OriginalFileName) ||
            !string.Equals(txd.Entry, $"txd/{txd.Id:N}.txd", StringComparison.Ordinal) ||
            !paths.Add(txd.Entry))
        {
            throw new InvalidDataException("Manifest BYX содержит некорректный TXD.");
        }

        if (requireCharacterMap && (txd.CharacterMap is null ||
            !string.Equals(txd.CharacterMap.Entry, CharacterMapEntryName, StringComparison.Ordinal) ||
            !paths.Add(txd.CharacterMap.Entry)))
        {
            throw new InvalidDataException("Manifest BYX не содержит корректный маппинг пары GXT + TXD.");
        }
    }

    private static void ValidateManifestEntriesVersion1(
        ZipArchive archive,
        ByxManifestV1 manifest)
    {
        var expected = CreateBaseManifestPaths(manifest.Gxt, manifest.Dictionary);
        foreach (var txd in manifest.Txd)
        {
            expected.Add(txd.Entry);
        }

        ValidateExpectedEntries(archive, expected);
    }

    private static void ValidateManifestEntriesVersion2(
        ZipArchive archive,
        ByxManifest manifest)
    {
        var expected = CreateBaseManifestPaths(manifest.Gxt, manifest.Dictionary);
        if (manifest.Txd is not null)
        {
            expected.Add(manifest.Txd.Entry);
            expected.Add(manifest.Txd.CharacterMap!.Entry);
        }

        ValidateExpectedEntries(archive, expected);
    }

    private static void ValidateExpectedEntries(ZipArchive archive, HashSet<string> expected)
    {
        if (!expected.SetEquals(archive.Entries.Select(entry => entry.FullName)))
        {
            throw new InvalidDataException("Набор записей BYX не соответствует manifest.json.");
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

    private static Dictionary<int[], char> ParseLegacyDictionary(List<ByxCharacterMapping> items)
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

    private static GXTType ParseGame(string game) => game switch
    {
        "GTA III" => GXTType.GtaIII,
        "GTA Vice City" => GXTType.GtaViceCity,
        _ => throw new InvalidDataException($"Игра '{game}' в BYX не поддерживается."),
    };

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
            name.StartsWith('/') || name.Split('/').Any(part => part is "" or "." or ".."))
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

    private static string ComputeHash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    private static bool IsFileNameOnly(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        string.Equals(value, Path.GetFileName(value), StringComparison.Ordinal) &&
        value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

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
