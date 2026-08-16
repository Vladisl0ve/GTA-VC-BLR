using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public sealed class ByxProjectSerializer : IProjectSerializer
{
    private const int CurrentVersion = 3;
    private const string ManifestEntryName = "manifest.json";
    private const string GxtEntryName = "gxt/main.gxt";
    private const string TxdEntryName = "txd/fonts.txd";
    private const string CharacterMapEntryName = "mapping/characters.json";
    private const string MetadataEntryName = "metadata/entries.json";
    private const long MaximumArchiveSize = 512L * 1024 * 1024;
    private const long MaximumManifestSize = 1024 * 1024;
    private const long MaximumCharacterMapSize = 4L * 1024 * 1024;
    private const long MaximumMetadataSize = 64L * 1024 * 1024;
    private const int MaximumEntries = 64;

    private static readonly JsonSerializerOptions HeaderJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
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
            throw new FileNotFoundException(LocalizationProvider.Current.Get("Byx.NotFound"), path);
        }

        if (file.Length > MaximumArchiveSize)
        {
            throw new InvalidDataException(LocalizationProvider.Current.Get("Byx.TooLarge"));
        }

        using var stream = File.OpenRead(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        ValidateArchive(archive);

        var manifestData = ReadEntry(GetRequiredEntry(archive, ManifestEntryName), MaximumManifestSize);
        var header = Deserialize<ByxManifestHeader>(manifestData, HeaderJsonOptions);
        if (!string.Equals(header.Format, "BYX", StringComparison.Ordinal))
        {
            throw new InvalidDataException(LocalizationProvider.Current.Get("Byx.InvalidFormat"));
        }

        if (header.Version != CurrentVersion)
        {
            throw new InvalidDataException(LocalizationProvider.Current.Format(
                header.Version > CurrentVersion ? "Byx.VersionNew" : "Byx.VersionOld",
                header.Version));
        }

        var manifest = Deserialize<ByxManifest>(manifestData, JsonOptions);
        ValidateManifest(manifest);
        ValidateManifestEntries(archive, manifest);

        var characterMap = manifest.CharacterMap is null
            ? null
            : LoadCharacterMap(archive, manifest.CharacterMap);
        var gxtData = ReadValidatedEntry(archive, manifest.Gxt, MaximumArchiveSize);
        var txdData = manifest.Txd is null
            ? null
            : ReadValidatedEntry(archive, manifest.Txd, MaximumArchiveSize);
        var gameType = GxtDomainRules.ParseCanonicalGameName(manifest.Game, "BYX");
        var metadataData = ReadValidatedEntry(archive, manifest.Metadata, MaximumMetadataSize);
        var metadata = ProjectMetadataJsonSerializer.Deserialize(metadataData, gameType);
        var manager = _gxtManagerFactory.Open(
            gxtData,
            gameType,
            manifest.Gxt.OriginalFileName,
            GxtDomainRules.ParseLanguageCode(manifest.Language),
            characterMap);
        var attachment = manifest.Txd is null || txdData is null
            ? null
            : LoadTxd(manifest.Txd, txdData, gameType);

        return new EditorProject
        {
            ProjectPath = path,
            GxtSourceName = manifest.Gxt.OriginalFileName,
            GxtSourcePath = null,
            GameType = gameType,
            GxtManager = manager,
            UsesCustomDictionary = characterMap is not null,
            AttachedTxd = attachment,
            CharacterMap = characterMap,
            Metadata = metadata,
            IsDirty = false,
        };
    }

    public void Save(string path, EditorProject project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(project);
        ValidateProject(project);

        using var gxtStream = new MemoryStream();
        project.GxtManager.WriteGXT(gxtStream);
        var gxtData = gxtStream.ToArray();

        var profile = project.CharacterMap?.Clone();
        if (profile is null && project.UsesCustomDictionary)
        {
            profile = project.GxtManager.CharacterMap;
            profile.IsVerified = false;
        }

        var characterMapData = profile is null
            ? null
            : CharacterMapFileSerializer.Serialize(profile);
        var metadataData = ProjectMetadataJsonSerializer.Serialize(
            project.Metadata,
            project.GameType);
        var txdData = project.AttachedTxd?.Data;

        var manifest = new ByxManifest
        {
            Format = "BYX",
            Version = CurrentVersion,
            Game = GxtDomainRules.ToGameName(project.GameType),
            Language = GxtDomainRules.ToLanguageCode(project.GxtManager.Language),
            Gxt = new ByxGxtItem
            {
                OriginalFileName = SanitizeFileName(project.GxtSourceName, "main.gxt"),
                Entry = GxtEntryName,
                Sha256 = ComputeHash(gxtData),
            },
            Txd = project.AttachedTxd is null || txdData is null
                ? null
                : new ByxTxdItem
                {
                    OriginalFileName = SanitizeFileName(
                        project.AttachedTxd.OriginalFileName,
                        "fonts.txd"),
                    Entry = TxdEntryName,
                    Sha256 = ComputeHash(txdData),
                },
            CharacterMap = characterMapData is null
                ? null
                : new ByxCharacterMapItem
                {
                    Entry = CharacterMapEntryName,
                    Sha256 = ComputeHash(characterMapData),
                    IsVerified = profile!.IsVerified,
                },
            Metadata = new ByxArchiveItem
            {
                Entry = MetadataEntryName,
                Sha256 = ComputeHash(metadataData),
            },
        };
        var manifestData = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        ValidateSaveLimits(
            manifestData,
            gxtData,
            txdData,
            characterMapData,
            metadataData);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException(LocalizationProvider.Current.Get("Byx.DirectoryUnknown"));
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var fileStream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.ReadWrite,
                       FileShare.None))
            using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false))
            {
                WriteEntry(archive, GxtEntryName, gxtData, CompressionLevel.Optimal);
                if (txdData is not null)
                {
                    WriteEntry(archive, TxdEntryName, txdData, CompressionLevel.Optimal);
                }

                if (characterMapData is not null)
                {
                    WriteEntry(
                        archive,
                        CharacterMapEntryName,
                        characterMapData,
                        CompressionLevel.Optimal);
                }

                WriteEntry(archive, MetadataEntryName, metadataData, CompressionLevel.Optimal);
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

    private static void ValidateProject(EditorProject project)
    {
        if (project.GameType is not (GXTType.GtaIII or GXTType.GtaViceCity))
        {
            throw new InvalidOperationException(LocalizationProvider.Current.Get("Byx.UnsupportedGxt"));
        }

        if (project.GameType != GXTType.GtaViceCity && project.AttachedTxd is not null)
        {
            throw new InvalidOperationException(LocalizationProvider.Current.Get("Byx.TxdViceCityOnly"));
        }
    }

    private static void ValidateSaveLimits(
        byte[] manifestData,
        byte[] gxtData,
        byte[]? txdData,
        byte[]? characterMapData,
        byte[] metadataData)
    {
        var entryCount = 3 + (txdData is null ? 0 : 1) + (characterMapData is null ? 0 : 1);
        var totalLength = checked(
            manifestData.LongLength +
            gxtData.LongLength +
            (txdData?.LongLength ?? 0) +
            (characterMapData?.LongLength ?? 0) +
            metadataData.LongLength);
        if (entryCount > MaximumEntries ||
            manifestData.LongLength > MaximumManifestSize ||
            (characterMapData?.LongLength ?? 0) > MaximumCharacterMapSize ||
            metadataData.LongLength > MaximumMetadataSize ||
            totalLength > MaximumArchiveSize)
        {
            throw new InvalidOperationException(LocalizationProvider.Current.Get("Byx.Limits"));
        }
    }

    private static CharacterMapProfile LoadCharacterMap(
        ZipArchive archive,
        ByxCharacterMapItem item)
    {
        var data = ReadValidatedEntry(archive, item, MaximumCharacterMapSize);
        try
        {
            var profile = CharacterMapFileSerializer.Deserialize(data);
            profile.IsVerified = item.IsVerified;
            return profile;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            throw new InvalidDataException(LocalizationProvider.Current.Get("Byx.MappingCorrupt"), exception);
        }
    }

    private TxdAttachment LoadTxd(ByxTxdItem item, byte[] data, GXTType gameType)
    {
        if (gameType != GXTType.GtaViceCity)
        {
            throw new InvalidDataException(LocalizationProvider.Current.Get("Byx.TxdViceCityOnly"));
        }

        var hash = SHA256.HashData(data);
        return new TxdAttachment
        {
            Id = new Guid(hash.AsSpan(0, 16)),
            OriginalFileName = item.OriginalFileName,
            DisplayName = Path.GetFileNameWithoutExtension(item.OriginalFileName),
            SourcePath = null,
            Data = data,
            Document = _txdReader.Read(data, item.OriginalFileName),
        };
    }

    private static void ValidateManifest(ByxManifest manifest)
    {
        if (!string.Equals(manifest.Format, "BYX", StringComparison.Ordinal) ||
            manifest.Version != CurrentVersion || manifest.Gxt is null || manifest.Metadata is null)
        {
            throw new InvalidDataException(LocalizationProvider.Current.Get("Byx.ManifestIncomplete"));
        }

        _ = GxtDomainRules.ParseCanonicalGameName(manifest.Game, "BYX");
        _ = GxtDomainRules.ParseLanguageCode(manifest.Language);
        ValidateGxtItem(manifest.Gxt);
        ValidateArchiveItem(manifest.Metadata, MetadataEntryName, LocalizationProvider.Current.Get("Byx.AttachmentMetadata"));
        if (manifest.Txd is not null)
        {
            ValidateTxdItem(manifest.Txd);
        }

        if (manifest.CharacterMap is not null)
        {
            ValidateArchiveItem(manifest.CharacterMap, CharacterMapEntryName, LocalizationProvider.Current.Get("Byx.AttachmentMapping"));
        }
    }

    private static void ValidateGxtItem(ByxGxtItem item)
    {
        if (!IsFileNameOnly(item.OriginalFileName))
        {
            throw new InvalidDataException(LocalizationProvider.Current.Get("Byx.InvalidGxtName"));
        }

        ValidateArchiveItem(item.Entry, item.Sha256, GxtEntryName, "GXT");
    }

    private static void ValidateTxdItem(ByxTxdItem item)
    {
        if (!IsFileNameOnly(item.OriginalFileName))
        {
            throw new InvalidDataException(LocalizationProvider.Current.Get("Byx.InvalidTxdName"));
        }

        ValidateArchiveItem(item.Entry, item.Sha256, TxdEntryName, "TXD");
    }

    private static void ValidateArchiveItem(
        ByxArchiveItem item,
        string expectedEntry,
        string description) =>
        ValidateArchiveItem(item.Entry, item.Sha256, expectedEntry, description);

    private static void ValidateArchiveItem(
        string entry,
        string sha256,
        string expectedEntry,
        string description)
    {
        if (!string.Equals(entry, expectedEntry, StringComparison.Ordinal) ||
            !IsSha256(sha256))
        {
            throw new InvalidDataException(LocalizationProvider.Current.Format("Byx.InvalidAttachment", description));
        }
    }

    private static void ValidateManifestEntries(ZipArchive archive, ByxManifest manifest)
    {
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ManifestEntryName,
            manifest.Gxt.Entry,
            manifest.Metadata.Entry,
        };
        if (manifest.Txd is not null && !expected.Add(manifest.Txd.Entry) ||
            manifest.CharacterMap is not null && !expected.Add(manifest.CharacterMap.Entry))
        {
            throw new InvalidDataException(LocalizationProvider.Current.Get("Byx.DuplicatePaths"));
        }

        if (!expected.SetEquals(archive.Entries.Select(entry => entry.FullName)))
        {
            throw new InvalidDataException(LocalizationProvider.Current.Get("Byx.EntrySetMismatch"));
        }
    }

    private static void ValidateArchive(ZipArchive archive)
    {
        if (archive.Entries.Count > MaximumEntries)
        {
            throw new InvalidDataException(LocalizationProvider.Current.Get("Byx.TooManyEntries"));
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalLength = 0;
        foreach (var entry in archive.Entries)
        {
            ValidateEntryName(entry.FullName);
            if (!names.Add(entry.FullName))
            {
                throw new InvalidDataException(LocalizationProvider.Current.Format("Byx.DuplicateEntry", entry.FullName));
            }

            if (entry.Length > MaximumArchiveSize - totalLength)
            {
                throw new InvalidDataException(LocalizationProvider.Current.Get("Byx.UnpackedTooLarge"));
            }

            totalLength += entry.Length;
        }
    }

    private static byte[] ReadValidatedEntry(
        ZipArchive archive,
        ByxArchiveItem item,
        long maximumLength)
    {
        var data = ReadEntry(GetRequiredEntry(archive, item.Entry), maximumLength);
        ValidateHash(data, item.Sha256, item.Entry);
        return data;
    }

    private static byte[] ReadValidatedEntry(
        ZipArchive archive,
        ByxGxtItem item,
        long maximumLength)
    {
        var data = ReadEntry(GetRequiredEntry(archive, item.Entry), maximumLength);
        ValidateHash(data, item.Sha256, item.Entry);
        return data;
    }

    private static byte[] ReadValidatedEntry(
        ZipArchive archive,
        ByxTxdItem item,
        long maximumLength)
    {
        var data = ReadEntry(GetRequiredEntry(archive, item.Entry), maximumLength);
        ValidateHash(data, item.Sha256, item.Entry);
        return data;
    }

    private static T Deserialize<T>(byte[] data, JsonSerializerOptions options) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(data, options)
                ?? throw new InvalidDataException(LocalizationProvider.Current.Get("Byx.JsonCorrupt"));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(LocalizationProvider.Current.Get("Byx.JsonCorrupt"), exception);
        }
    }

    private static ZipArchiveEntry GetRequiredEntry(ZipArchive archive, string name)
    {
        ValidateEntryName(name);
        return archive.Entries.SingleOrDefault(
                   entry => string.Equals(entry.FullName, name, StringComparison.Ordinal))
               ?? throw new InvalidDataException(LocalizationProvider.Current.Format("Byx.EntryMissing", name));
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry, long maximumLength)
    {
        if (entry.Length > maximumLength || entry.Length > int.MaxValue)
        {
            throw new InvalidDataException(LocalizationProvider.Current.Format("Byx.EntryTooLarge", entry.FullName));
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
            throw new InvalidDataException(LocalizationProvider.Current.Format("Byx.UnsafeEntryName", name));
        }
    }

    private static void ValidateHash(byte[] data, string expected, string entryName)
    {
        if (!string.Equals(ComputeHash(data), expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(LocalizationProvider.Current.Format("Byx.ChecksumMismatch", entryName));
        }
    }

    private static string ComputeHash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool IsFileNameOnly(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        string.Equals(value, Path.GetFileName(value), StringComparison.Ordinal) &&
        value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static string SanitizeFileName(string value, string fallback)
    {
        var name = Path.GetFileName(value);
        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

}
