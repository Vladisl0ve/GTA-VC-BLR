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
    private const int CurrentVersion = 5;
    private const int OldestSupportedVersion = 3;
    private const int InstallerVersion = 4;
    private const string ManifestEntryName = "manifest.json";
    private const string GxtEntryName = "gxt/main.gxt";
    private const string TxdEntryName = "txd/fonts.txd";
    private const string CharacterMapEntryName = "mapping/characters.json";
    private const string FontMetricsEntryName = "font/metrics.json";
    private const string MetadataEntryName = "metadata/entries.json";
    private const string InstallerProfileEntryName = "installer/profile.json";
    private const long MaximumArchiveSize = 1024L * 1024 * 1024;
    private const long MaximumManifestSize = 1024 * 1024;
    private const long MaximumCharacterMapSize = 4L * 1024 * 1024;
    private const long MaximumFontMetricsSize = 1024 * 1024;
    private const long MaximumMetadataSize = 64L * 1024 * 1024;
    private const int MaximumEntries = 520;

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

        try
        {
            return LoadCore(path);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is EndOfStreamException or OverflowException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            throw new InvalidDataException(LocalizationProvider.Current.Get("Byx.InvalidFormat"), exception);
        }
    }

    private EditorProject LoadCore(string path)
    {
        using var stream = File.OpenRead(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        ValidateArchive(archive);

        var manifestData = ReadEntry(GetRequiredEntry(archive, ManifestEntryName), MaximumManifestSize);
        var header = Deserialize<ByxManifestHeader>(manifestData, HeaderJsonOptions);
        if (!string.Equals(header.Format, "BYX", StringComparison.Ordinal))
        {
            throw new InvalidDataException(LocalizationProvider.Current.Get("Byx.InvalidFormat"));
        }

        if (header.Version is < OldestSupportedVersion or > CurrentVersion)
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
        var gameType = GxtDomainRules.ParseCanonicalGameName(manifest.Game, "BYX");
        var fontMetrics = manifest.FontMetrics is null
            ? GetLegacyFontMetrics(manifest.Version, gameType, characterMap)
            : LoadFontMetrics(archive, manifest.FontMetrics);
        var gxtData = ReadValidatedEntry(archive, manifest.Gxt, MaximumArchiveSize);
        var txdData = manifest.Txd is null
            ? null
            : ReadValidatedEntry(archive, manifest.Txd, MaximumArchiveSize);
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
        var installerProfile = manifest.Installer is null
            ? null
            : LoadInstallerProfile(archive, manifest.Installer);

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
            FontMetrics = fontMetrics,
            Metadata = metadata,
            InstallerProfile = installerProfile,
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
        var fontMetricsData = project.FontMetrics is null
            ? null
            : FontMetricsFileSerializer.Serialize(project.FontMetrics);
        var metadataData = ProjectMetadataJsonSerializer.Serialize(
            project.Metadata,
            project.GameType);
        var txdData = project.AttachedTxd?.Data;
        var installerProfile = project.InstallerProfile?.Clone();
        byte[]? installerProfileData = null;
        if (installerProfile is not null)
        {
            MigrateLegacyAdditionalAssets(installerProfile);
            InstallerProfileValidator.ValidateForStorage(installerProfile);
            installerProfileData = JsonSerializer.SerializeToUtf8Bytes(
                CreateInstallerProfileDocument(installerProfile),
                JsonOptions);
        }

        var installerBinaries = installerProfile is null
            ? new List<InstallerStoredBinary>()
            : GetInstallerStoredBinaries(installerProfile);

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
            FontMetrics = fontMetricsData is null
                ? null
                : new ByxArchiveItem
                {
                    Entry = FontMetricsEntryName,
                    Sha256 = ComputeHash(fontMetricsData),
                },
            Metadata = new ByxArchiveItem
            {
                Entry = MetadataEntryName,
                Sha256 = ComputeHash(metadataData),
            },
            Installer = installerProfile is null || installerProfileData is null
                ? null
                : new ByxInstallerItem
                {
                    Entry = InstallerProfileEntryName,
                    Sha256 = ComputeHash(installerProfileData),
                    Assets = installerBinaries.Select(binary => new ByxInstallerAssetItem
                    {
                        Id = binary.Id,
                        OriginalFileName = SanitizeFileName(binary.OriginalFileName, $"{binary.Id:N}.bin"),
                        Entry = GetInstallerAssetEntryName(binary.Id),
                        Sha256 = ComputeHash(binary.Data),
                    }).ToList(),
                },
        };
        var manifestData = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        ValidateSaveLimits(
            manifestData,
            gxtData,
            txdData,
            characterMapData,
            fontMetricsData,
            metadataData,
            installerProfileData,
            installerBinaries);

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

                if (fontMetricsData is not null)
                {
                    WriteEntry(
                        archive,
                        FontMetricsEntryName,
                        fontMetricsData,
                        CompressionLevel.Optimal);
                }

                WriteEntry(archive, MetadataEntryName, metadataData, CompressionLevel.Optimal);
                if (installerProfileData is not null && installerProfile is not null)
                {
                    WriteEntry(
                        archive,
                        InstallerProfileEntryName,
                        installerProfileData,
                        CompressionLevel.Optimal);
                    foreach (var binary in installerBinaries)
                    {
                        WriteEntry(
                            archive,
                            GetInstallerAssetEntryName(binary.Id),
                            binary.Data,
                            CompressionLevel.Optimal);
                    }
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

        if (project.GameType != GXTType.GtaViceCity && project.InstallerProfile is not null)
        {
            throw new InvalidOperationException(LocalizationProvider.Current.Get("Byx.InstallerViceCityOnly"));
        }
    }

    private static void ValidateSaveLimits(
        byte[] manifestData,
        byte[] gxtData,
        byte[]? txdData,
        byte[]? characterMapData,
        byte[]? fontMetricsData,
        byte[] metadataData,
        byte[]? installerProfileData,
        IReadOnlyList<InstallerStoredBinary> installerBinaries)
    {
        var entryCount = 3 +
                         (txdData is null ? 0 : 1) +
                         (characterMapData is null ? 0 : 1) +
                         (fontMetricsData is null ? 0 : 1) +
                         (installerProfileData is null ? 0 : 1) +
                         installerBinaries.Count;
        var installerAssetsLength = installerBinaries.Sum(binary => binary.Data.LongLength);
        var totalLength = checked(
            manifestData.LongLength +
            gxtData.LongLength +
            (txdData?.LongLength ?? 0) +
            (characterMapData?.LongLength ?? 0) +
            (fontMetricsData?.LongLength ?? 0) +
            metadataData.LongLength +
            (installerProfileData?.LongLength ?? 0) +
            installerAssetsLength);
        if (entryCount > MaximumEntries ||
            manifestData.LongLength > MaximumManifestSize ||
            (characterMapData?.LongLength ?? 0) > MaximumCharacterMapSize ||
            (fontMetricsData?.LongLength ?? 0) > MaximumFontMetricsSize ||
            metadataData.LongLength > MaximumMetadataSize ||
            (installerProfileData?.LongLength ?? 0) > MaximumManifestSize ||
            totalLength > MaximumArchiveSize)
        {
            throw new InvalidOperationException(LocalizationProvider.Current.Get("Byx.Limits"));
        }
    }

    private static ByxInstallerProfileDocument CreateInstallerProfileDocument(
        InstallerProfile profile) => new()
    {
        ProductId = profile.ProductId,
        Name = profile.Name,
        InstallerVersion = profile.Version,
        Publisher = profile.Publisher,
        OutputFileName = profile.OutputFileName,
        Assets = profile.Assets.Select(asset => new ByxInstallerProfileAsset
        {
            Id = asset.Id,
            Role = asset.Role,
            DestinationPath = asset.DestinationPath,
        }).ToList(),
        Mods = profile.Mods.Select(mod => new ByxInstallerProfileMod
        {
            Id = mod.Id,
            Name = mod.Name,
            IsRequired = mod.IsRequired,
            Files = mod.Files.Select(file => new ByxInstallerProfileModFile
            {
                Id = file.Id,
                DestinationPath = file.DestinationPath,
            }).ToList(),
        }).ToList(),
        ReleaseReadMeEnglish = CreateReleaseDocument(profile.ReleaseReadMeEnglish),
        ReleaseReadMeBelarusian = CreateReleaseDocument(profile.ReleaseReadMeBelarusian),
    };

    private static ByxInstallerReleaseDocument? CreateReleaseDocument(
        InstallerReleaseDocument? document) =>
        document is null
            ? null
            : new ByxInstallerReleaseDocument
            {
                Id = document.Id,
                FileName = document.OriginalFileName,
            };

    private static List<InstallerStoredBinary> GetInstallerStoredBinaries(
        InstallerProfile profile)
    {
        var binaries = new List<InstallerStoredBinary>(
            profile.PayloadFileCount + InstallerProfileValidator.MaximumReleaseDocuments);
        binaries.AddRange(profile.Assets.Select(asset =>
            new InstallerStoredBinary(asset.Id, asset.OriginalFileName, asset.Data)));
        binaries.AddRange(profile.Mods.SelectMany(mod => mod.Files).Select(file =>
            new InstallerStoredBinary(file.Id, file.OriginalFileName, file.Data)));
        binaries.AddRange(profile.EnumerateReleaseDocuments().Select(document =>
            new InstallerStoredBinary(document.Id, document.OriginalFileName, document.Data)));
        return binaries;
    }

    private static InstallerProfile LoadInstallerProfile(
        ZipArchive archive,
        ByxInstallerItem item)
    {
        var profileData = ReadValidatedEntry(archive, item, MaximumManifestSize);
        var document = Deserialize<ByxInstallerProfileDocument>(profileData, JsonOptions);
        if (!string.Equals(document.Format, "BYX_INSTALLER_PROFILE", StringComparison.Ordinal) ||
            document.Version is not (
                ByxInstallerProfileDocument.LegacyVersion or
                ByxInstallerProfileDocument.CurrentVersion))
        {
            throw new InvalidDataException(LocalizationProvider.Current.Get("Byx.InstallerProfileInvalid"));
        }

        var manifestAssets = item.Assets.ToDictionary(asset => asset.Id);
        var referencedIds = document.Assets.Select(asset => asset.Id)
            .Concat(document.Mods.SelectMany(mod => mod.Files).Select(file => file.Id))
            .ToArray();
        if (manifestAssets.Count != item.Assets.Count ||
            referencedIds.Distinct().Count() != referencedIds.Length)
        {
            throw new InvalidDataException(LocalizationProvider.Current.Get("Byx.InstallerAssetsMismatch"));
        }

        var assets = new List<InstallerAsset>(document.Assets.Count);
        foreach (var profileAsset in document.Assets)
        {
            if (!manifestAssets.Remove(profileAsset.Id, out var manifestAsset))
            {
                throw new InvalidDataException(LocalizationProvider.Current.Get("Byx.InstallerAssetsMismatch"));
            }

            assets.Add(new InstallerAsset
            {
                Id = profileAsset.Id,
                Role = profileAsset.Role,
                OriginalFileName = manifestAsset.OriginalFileName,
                DestinationPath = profileAsset.DestinationPath,
                Data = ReadValidatedEntry(archive, manifestAsset, MaximumArchiveSize),
            });
        }

        var mods = new List<InstallerMod>(document.Mods.Count);
        foreach (var profileMod in document.Mods)
        {
            var files = new List<InstallerModFile>(profileMod.Files.Count);
            foreach (var profileFile in profileMod.Files)
            {
                if (!manifestAssets.Remove(profileFile.Id, out var manifestAsset))
                {
                    throw new InvalidDataException(LocalizationProvider.Current.Get("Byx.InstallerAssetsMismatch"));
                }

                files.Add(new InstallerModFile
                {
                    Id = profileFile.Id,
                    OriginalFileName = manifestAsset.OriginalFileName,
                    DestinationPath = profileFile.DestinationPath,
                    Data = ReadValidatedEntry(archive, manifestAsset, MaximumArchiveSize),
                });
            }

            mods.Add(new InstallerMod
            {
                Id = profileMod.Id,
                Name = profileMod.Name,
                IsRequired = profileMod.IsRequired,
                Files = files,
            });
        }

        var profile = new InstallerProfile
        {
            ProductId = document.ProductId,
            Name = document.Name,
            Version = document.InstallerVersion,
            Publisher = document.Publisher,
            OutputFileName = document.OutputFileName,
            Assets = assets,
            Mods = mods,
            ReleaseReadMeEnglish = LoadReleaseDocument(
                archive,
                document.ReleaseReadMeEnglish,
                manifestAssets),
            ReleaseReadMeBelarusian = LoadReleaseDocument(
                archive,
                document.ReleaseReadMeBelarusian,
                manifestAssets),
        };
        if (document.Version == ByxInstallerProfileDocument.LegacyVersion)
        {
            MigrateLegacyAdditionalAssets(profile);
        }
        if (manifestAssets.Count != 0)
        {
            throw new InvalidDataException(LocalizationProvider.Current.Get("Byx.InstallerAssetsMismatch"));
        }

        InstallerProfileValidator.ValidateForStorage(profile);
        return profile;
    }

    private static void MigrateLegacyAdditionalAssets(InstallerProfile profile)
    {
        var legacyAssets = profile.Assets
            .Where(asset => asset.Role == InstallerAssetRole.Additional)
            .ToList();
        if (legacyAssets.Count == 0)
        {
            return;
        }

        var source = new byte[16 + legacyAssets.Count * 16];
        profile.ProductId.TryWriteBytes(source);
        var offset = 16;
        foreach (var asset in legacyAssets.OrderBy(asset => asset.Id))
        {
            asset.Id.TryWriteBytes(source.AsSpan(offset, 16));
            offset += 16;
        }

        var hash = SHA256.HashData(source);
        var modId = new Guid(hash.AsSpan(0, 16));
        var modName = "Legacy additional files";
        var suffix = 2;
        while (profile.Mods.Any(mod =>
                   mod.Id == modId ||
                   mod.Name.Equals(modName, StringComparison.OrdinalIgnoreCase)))
        {
            hash = SHA256.HashData(hash);
            modId = new Guid(hash.AsSpan(0, 16));
            modName = $"Legacy additional files ({suffix++})";
        }

        profile.Assets.RemoveAll(asset => asset.Role == InstallerAssetRole.Additional);
        profile.Mods.Add(new InstallerMod
        {
            Id = modId,
            Name = modName,
            IsRequired = true,
            Files = legacyAssets.Select(asset => new InstallerModFile
            {
                Id = asset.Id,
                OriginalFileName = asset.OriginalFileName,
                DestinationPath = asset.DestinationPath,
                Data = [.. asset.Data],
            }).ToList(),
        });
    }

    private static InstallerReleaseDocument? LoadReleaseDocument(
        ZipArchive archive,
        ByxInstallerReleaseDocument? document,
        Dictionary<Guid, ByxInstallerAssetItem> remainingManifestAssets)
    {
        if (document is null)
        {
            return null;
        }

        if (!remainingManifestAssets.Remove(document.Id, out var manifestAsset) ||
            !string.Equals(document.FileName, manifestAsset.OriginalFileName, StringComparison.Ordinal))
        {
            throw new InvalidDataException(LocalizationProvider.Current.Get("Byx.InstallerAssetsMismatch"));
        }

        return new InstallerReleaseDocument
        {
            Id = document.Id,
            OriginalFileName = manifestAsset.OriginalFileName,
            Data = ReadValidatedEntry(archive, manifestAsset, MaximumArchiveSize),
        };
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

    private static FontMetricsProfile LoadFontMetrics(
        ZipArchive archive,
        ByxArchiveItem item)
    {
        var data = ReadValidatedEntry(archive, item, MaximumFontMetricsSize);
        try
        {
            return FontMetricsFileSerializer.Deserialize(data);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            throw new InvalidDataException(LocalizationProvider.Current.Get("Byx.InvalidFormat"), exception);
        }
    }

    private static FontMetricsProfile? GetLegacyFontMetrics(
        int version,
        GXTType gameType,
        CharacterMapProfile? characterMap)
    {
        if (version >= CurrentVersion ||
            gameType != GXTType.GtaViceCity ||
            characterMap is null)
        {
            return null;
        }

        var preset = CharacterMapPresets.Belarusian;
        return CharacterMapsMatch(characterMap, preset)
            ? FontMetricsPresets.BelarusianViceCity
            : null;
    }

    private static bool CharacterMapsMatch(
        CharacterMapProfile first,
        CharacterMapProfile second)
    {
        var firstDecode = first.ToDecodeMap();
        var secondDecode = second.ToDecodeMap();
        if (firstDecode.Count != secondDecode.Count ||
            firstDecode.Any(pair =>
                !secondDecode.TryGetValue(pair.Key, out var character) || character != pair.Value))
        {
            return false;
        }

        var firstEncode = first.ToEncodeMap();
        var secondEncode = second.ToEncodeMap();
        return firstEncode.Count == secondEncode.Count &&
               firstEncode.All(pair =>
                   secondEncode.TryGetValue(pair.Key, out var code) && code == pair.Value);
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
            manifest.Version is < OldestSupportedVersion or > CurrentVersion ||
            manifest.Gxt is null || manifest.Metadata is null ||
            manifest.Version < InstallerVersion && manifest.Installer is not null ||
            manifest.Version < CurrentVersion && manifest.FontMetrics is not null)
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

        if (manifest.FontMetrics is not null)
        {
            ValidateArchiveItem(manifest.FontMetrics, FontMetricsEntryName, "font metrics");
        }

        if (manifest.Installer is not null)
        {
            ValidateInstallerItem(manifest.Installer);
        }
    }

    private static void ValidateInstallerItem(ByxInstallerItem item)
    {
        ValidateArchiveItem(
            item,
            InstallerProfileEntryName,
            LocalizationProvider.Current.Get("Byx.AttachmentInstaller"));
        if (item.Assets.Count > InstallerProfileValidator.MaximumAssets +
            InstallerProfileValidator.MaximumReleaseDocuments)
        {
            throw new InvalidDataException(LocalizationProvider.Current.Get("Byx.TooManyEntries"));
        }

        var ids = new HashSet<Guid>();
        foreach (var asset in item.Assets)
        {
            if (asset.Id == Guid.Empty || !ids.Add(asset.Id) || !IsFileNameOnly(asset.OriginalFileName))
            {
                throw new InvalidDataException(LocalizationProvider.Current.Get("Byx.InstallerAssetInvalid"));
            }

            ValidateArchiveItem(
                asset,
                GetInstallerAssetEntryName(asset.Id),
                LocalizationProvider.Current.Get("Byx.AttachmentInstallerAsset"));
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
            manifest.CharacterMap is not null && !expected.Add(manifest.CharacterMap.Entry) ||
            manifest.FontMetrics is not null && !expected.Add(manifest.FontMetrics.Entry) ||
            manifest.Installer is not null && !expected.Add(manifest.Installer.Entry) ||
            manifest.Installer is not null &&
            manifest.Installer.Assets.Any(asset => !expected.Add(asset.Entry)))
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

    private static string GetInstallerAssetEntryName(Guid id) =>
        $"installer/assets/{id:N}.bin";

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

    private readonly record struct InstallerStoredBinary(Guid Id, string OriginalFileName, byte[] Data);
}
