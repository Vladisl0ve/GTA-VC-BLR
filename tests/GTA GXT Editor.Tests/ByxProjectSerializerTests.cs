using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Tests;

[TestClass]
public sealed class ByxProjectSerializerTests
{
    private string _testDirectory = null!;
    private TxdReader _txdReader = null!;
    private ByxProjectSerializer _serializer = null!;

    [TestInitialize]
    public void Initialize()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _testDirectory = Path.Combine(Path.GetTempPath(), $"gta-gxt-byx-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
        _txdReader = new TxdReader();
        _serializer = new ByxProjectSerializer(new GxtManagerFactory(), _txdReader);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void SaveAndLoad_FullV5Project_PreservesEveryProjectPart()
    {
        var project = CreateProject();
        var path = Path.Combine(_testDirectory, "translation.byx");

        _serializer.Save(path, project);
        var loaded = _serializer.Load(path);

        Assert.AreEqual(path, loaded.ProjectPath);
        Assert.AreEqual("american.gxt", loaded.GxtSourceName);
        Assert.AreEqual(GXTType.GtaViceCity, loaded.GameType);
        Assert.AreEqual(GxtLanguage.Belarusian, loaded.GxtManager.Language);
        Assert.IsTrue(loaded.UsesCustomDictionary);
        Assert.IsFalse(loaded.IsDirty);
        Assert.AreEqual('Ж', loaded.GxtManager.DecodeCharacterMap[200]);
        Assert.AreEqual(
            "ЖЖ",
            loaded.GxtManager.ConvertBytesToText(loaded.GxtManager.GXTEntries.Single().Value));
        Assert.IsNotNull(loaded.AttachedTxd);
        CollectionAssert.AreEqual(project.AttachedTxd!.Data, loaded.AttachedTxd.Data);
        Assert.AreEqual("fonts.txd", loaded.AttachedTxd.OriginalFileName);
        Assert.AreEqual("fonts", loaded.AttachedTxd.DisplayName);
        Assert.IsNull(loaded.AttachedTxd.SourcePath);
        Assert.IsNotNull(loaded.CharacterMap);
        Assert.IsTrue(loaded.CharacterMap.IsVerified);
        Assert.IsNotNull(loaded.FontMetrics);
        AssertFontMetricsAreEqual(project.FontMetrics!, loaded.FontMetrics);

        Assert.AreEqual("GXT_ENTRY_METADATA", loaded.Metadata.Format);
        Assert.AreEqual(1, loaded.Metadata.Version);
        Assert.AreEqual(2, loaded.Metadata.Blocks.Count);
        Assert.AreEqual("mission.the-party", loaded.Metadata.Blocks[0].Id);
        Assert.AreEqual("Opening cutscene", loaded.Metadata.Blocks[0].Description);
        Assert.AreEqual(2, loaded.Metadata.Entries.Count);
        Assert.AreEqual("MAIN", loaded.Metadata.Entries[0].Table);
        Assert.AreEqual("HELLO", loaded.Metadata.Entries[0].Key);
        Assert.AreEqual("Праверыць голас Кена.", loaded.Metadata.Entries[0].Comment);
        Assert.AreEqual(2, loaded.Metadata.Entries[0].Occurrences.Count);
        Assert.AreEqual("On the yacht", loaded.Metadata.Entries[0].Occurrences[1].Context);

        using var archive = ZipFile.OpenRead(path);
        CollectionAssert.AreEquivalent(
            new[]
            {
                "manifest.json",
                "gxt/main.gxt",
                "txd/fonts.txd",
                "mapping/characters.json",
                "font/metrics.json",
                "metadata/entries.json",
            },
            archive.Entries.Select(entry => entry.FullName).ToArray());

        var manifest = ReadJsonObject(archive, "manifest.json");
        Assert.AreEqual(5, manifest["version"]!.GetValue<int>());
        Assert.IsNull(manifest["installer"]);
        Assert.AreEqual("txd/fonts.txd", manifest["txd"]!["entry"]!.GetValue<string>());
        Assert.AreEqual(
            "mapping/characters.json",
            manifest["characterMap"]!["entry"]!.GetValue<string>());
        Assert.AreEqual(
            "metadata/entries.json",
            manifest["metadata"]!["entry"]!.GetValue<string>());
        Assert.AreEqual(
            "font/metrics.json",
            manifest["fontMetrics"]!["entry"]!.GetValue<string>());
        AssertManifestHashMatches(archive, manifest, "gxt");
        AssertManifestHashMatches(archive, manifest, "txd");
        AssertManifestHashMatches(archive, manifest, "characterMap");
        AssertManifestHashMatches(archive, manifest, "fontMetrics");
        AssertManifestHashMatches(archive, manifest, "metadata");
    }

    [TestMethod]
    public void SaveAndLoad_MinimalProject_StoresEmptyMetadataAndNoOptionalFiles()
    {
        var project = CreatePlainProject();
        var path = Path.Combine(_testDirectory, "minimal.byx");

        _serializer.Save(path, project);
        var loaded = _serializer.Load(path);

        Assert.IsNull(loaded.AttachedTxd);
        Assert.IsNull(loaded.CharacterMap);
        Assert.IsNull(loaded.FontMetrics);
        Assert.IsFalse(loaded.UsesCustomDictionary);
        Assert.IsEmpty(loaded.Metadata.Blocks);
        Assert.IsEmpty(loaded.Metadata.Entries);
        using var archive = ZipFile.OpenRead(path);
        CollectionAssert.AreEquivalent(
            new[] { "manifest.json", "gxt/main.gxt", "metadata/entries.json" },
            archive.Entries.Select(entry => entry.FullName).ToArray());
        var manifest = ReadJsonObject(archive, "manifest.json");
        Assert.IsNull(manifest["txd"]);
        Assert.IsNull(manifest["characterMap"]);
        Assert.IsNull(manifest["fontMetrics"]);
    }

    [TestMethod]
    public void SaveAndLoad_CharacterMapWithoutTxd_IsSupported()
    {
        var project = CreateProject();
        project.AttachedTxd = null;
        var path = Path.Combine(_testDirectory, "mapping-only.byx");

        _serializer.Save(path, project);
        var loaded = _serializer.Load(path);

        Assert.IsNull(loaded.AttachedTxd);
        Assert.IsNotNull(loaded.CharacterMap);
        Assert.AreEqual('Ж', loaded.CharacterMap.ToDecodeMap()[200]);
        using var archive = ZipFile.OpenRead(path);
        Assert.IsNotNull(archive.GetEntry("mapping/characters.json"));
        Assert.IsNull(archive.GetEntry("txd/fonts.txd"));
    }

    [TestMethod]
    public void SaveAndLoad_TxdWithoutCharacterMap_IsSupported()
    {
        var project = CreatePlainProject();
        project.AttachedTxd = CreateAttachment();
        var path = Path.Combine(_testDirectory, "txd-only.byx");

        _serializer.Save(path, project);
        var loaded = _serializer.Load(path);

        Assert.IsNotNull(loaded.AttachedTxd);
        Assert.IsNull(loaded.CharacterMap);
        Assert.IsFalse(loaded.UsesCustomDictionary);
        using var archive = ZipFile.OpenRead(path);
        Assert.IsNotNull(archive.GetEntry("txd/fonts.txd"));
        Assert.IsNull(archive.GetEntry("mapping/characters.json"));
    }

    [TestMethod]
    public void Save_CustomDictionaryWithoutProfile_EmbedsUnverifiedCharacterMap()
    {
        var project = CreateProject();
        project.AttachedTxd = null;
        project.CharacterMap = null;
        project.UsesCustomDictionary = true;
        var path = Path.Combine(_testDirectory, "dictionary.byx");

        _serializer.Save(path, project);
        var loaded = _serializer.Load(path);

        Assert.IsNotNull(loaded.CharacterMap);
        Assert.IsFalse(loaded.CharacterMap.IsVerified);
        Assert.AreEqual('Ж', loaded.CharacterMap.ToDecodeMap()[200]);
    }

    [TestMethod]
    public void SaveAndLoad_PreservesUnverifiedCharacterMapDraft()
    {
        var project = CreateProject();
        project.CharacterMap!.IsVerified = false;
        var path = Path.Combine(_testDirectory, "unverified.byx");

        _serializer.Save(path, project);
        var loaded = _serializer.Load(path);

        Assert.IsNotNull(loaded.CharacterMap);
        Assert.IsFalse(loaded.CharacterMap.IsVerified);
    }

    [TestMethod]
    public void LoadedCharacterMap_CanStillBeExportedSeparately()
    {
        var path = SaveProject();
        var exportPath = Path.Combine(_testDirectory, "export.gxtmap.json");
        var loaded = _serializer.Load(path);

        CharacterMapFileSerializer.Save(exportPath, loaded.CharacterMap!);
        var exported = CharacterMapFileSerializer.Load(exportPath);

        Assert.AreEqual('Ж', exported.ToDecodeMap()[200]);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void Load_OlderManifestVersion_IsRejected(int version)
    {
        var path = SaveProject();
        MutateManifest(path, manifest => manifest["version"] = version);

        var exception = Assert.Throws<InvalidDataException>(() => _serializer.Load(path));

        StringAssert.Contains(exception.Message, "no longer supported");
    }

    [TestMethod]
    public void Load_NewerManifestVersion_IsRejected()
    {
        var path = SaveProject();
        MutateManifest(path, manifest => manifest["version"] = 6);

        var exception = Assert.Throws<InvalidDataException>(() => _serializer.Load(path));

        StringAssert.Contains(exception.Message, "not supported yet");
    }

    [TestMethod]
    public void Load_V3Project_MigratesWithEmptyInstallerProfileOnNextSave()
    {
        var path = SaveProject();
        ConvertToLegacyVersion(path, 3);

        var loaded = _serializer.Load(path);

        Assert.IsNull(loaded.InstallerProfile);
        var migratedPath = Path.Combine(_testDirectory, "migrated.byx");
        _serializer.Save(migratedPath, loaded);
        using var archive = ZipFile.OpenRead(migratedPath);
        var manifest = ReadJsonObject(archive, "manifest.json");
        Assert.AreEqual(5, manifest["version"]!.GetValue<int>());
        Assert.IsNull(manifest["installer"]);
    }

    [TestMethod]
    public void Load_V4Project_PreservesPreviouslySupportedInstallerProfile()
    {
        var project = CreateProject();
        project.InstallerProfile = CreateInstallerProfile();
        var path = Path.Combine(_testDirectory, "legacy-installer-v4.byx");
        _serializer.Save(path, project);
        ConvertToLegacyVersion(path, 4);

        var loaded = _serializer.Load(path);

        Assert.IsNotNull(loaded.InstallerProfile);
        Assert.AreEqual(project.InstallerProfile.ProductId, loaded.InstallerProfile.ProductId);
        Assert.IsNull(loaded.FontMetrics);
        Assert.IsFalse(loaded.IsDirty);
    }

    [TestMethod]
    [DataRow(3)]
    [DataRow(4)]
    public void Load_LegacyViceCityProjectWithBundledCharacterMap_UsesBundledFontMetrics(
        int version)
    {
        var project = CreateProjectWithBelarusianPreset();
        var path = Path.Combine(_testDirectory, $"legacy-preset-v{version}.byx");
        _serializer.Save(path, project);
        ConvertToLegacyVersion(path, version);

        var loaded = _serializer.Load(path);

        Assert.IsNotNull(loaded.FontMetrics);
        AssertFontMetricsAreEqual(FontMetricsPresets.BelarusianViceCity, loaded.FontMetrics);
        Assert.IsFalse(loaded.IsDirty);
    }

    [TestMethod]
    [DataRow(3)]
    [DataRow(4)]
    public void Load_LegacyViceCityProjectWithDifferentCharacterMap_LeavesFontMetricsUnset(
        int version)
    {
        var path = SaveProject();
        ConvertToLegacyVersion(path, version);

        var loaded = _serializer.Load(path);

        Assert.IsNull(loaded.FontMetrics);
        Assert.IsFalse(loaded.IsDirty);
    }

    [TestMethod]
    public void Load_V5ProjectWithoutFontMetrics_DoesNotApplyLegacyFallback()
    {
        var project = CreateProjectWithBelarusianPreset();
        var path = Path.Combine(_testDirectory, "v5-without-metrics.byx");
        _serializer.Save(path, project);
        RemoveFontMetrics(path);

        var loaded = _serializer.Load(path);

        Assert.IsNull(loaded.FontMetrics);
        Assert.IsFalse(loaded.IsDirty);
    }

    [TestMethod]
    public void Load_FontMetricsWithWrongHash_IsRejected()
    {
        var path = SaveProject();
        ReplaceEntry(path, "font/metrics.json", "{}"u8.ToArray());

        var exception = Assert.Throws<InvalidDataException>(() => _serializer.Load(path));

        StringAssert.Contains(exception.Message, "font/metrics.json");
    }

    [TestMethod]
    public void Load_CaseInsensitiveDuplicateFontMetricsEntry_IsRejected()
    {
        var path = SaveProject();
        AddEntry(path, "FONT/METRICS.JSON", [1]);

        Assert.Throws<InvalidDataException>(() => _serializer.Load(path));
    }

    [TestMethod]
    public void Load_MalformedFontMetricsJsonWithValidHash_IsRejected()
    {
        var path = SaveProject();
        ReplaceFontMetricsAndUpdateHash(path, "{"u8.ToArray());

        Assert.Throws<InvalidDataException>(() => _serializer.Load(path));
    }

    [TestMethod]
    public void Load_FontMetricsLargerThanOneMiB_IsRejected()
    {
        var path = SaveProject();
        ReplaceFontMetricsAndUpdateHash(path, new byte[1024 * 1024 + 1]);

        Assert.Throws<InvalidDataException>(() => _serializer.Load(path));
    }

    [TestMethod]
    public void SaveAndLoad_InstallerProfile_PreservesMetadataAssetsAndHashes()
    {
        var project = CreateProject();
        project.InstallerProfile = CreateInstallerProfile();
        var path = Path.Combine(_testDirectory, "installer-profile.byx");

        _serializer.Save(path, project);
        var loaded = _serializer.Load(path);

        var profile = loaded.InstallerProfile;
        Assert.IsNotNull(profile);
        Assert.AreEqual(project.InstallerProfile.ProductId, profile.ProductId);
        Assert.AreEqual("1.2.3", profile.Version);
        Assert.AreEqual("Belarusian Games", profile.Publisher);
        Assert.HasCount(3, profile.Assets);
        for (var index = 0; index < profile.Assets.Count; index++)
        {
            Assert.AreEqual(project.InstallerProfile.Assets[index].Id, profile.Assets[index].Id);
            Assert.AreEqual(project.InstallerProfile.Assets[index].Role, profile.Assets[index].Role);
            Assert.AreEqual(project.InstallerProfile.Assets[index].DestinationPath, profile.Assets[index].DestinationPath);
            CollectionAssert.AreEqual(project.InstallerProfile.Assets[index].Data, profile.Assets[index].Data);
        }
        Assert.HasCount(1, profile.Mods);
        Assert.AreEqual(project.InstallerProfile.Mods[0].Id, profile.Mods[0].Id);
        Assert.AreEqual("Optional configuration", profile.Mods[0].Name);
        Assert.IsFalse(profile.Mods[0].IsRequired);
        Assert.HasCount(1, profile.Mods[0].Files);
        Assert.AreEqual(project.InstallerProfile.Mods[0].Files[0].Id, profile.Mods[0].Files[0].Id);
        CollectionAssert.AreEqual(
            project.InstallerProfile.Mods[0].Files[0].Data,
            profile.Mods[0].Files[0].Data);

        using var archive = ZipFile.OpenRead(path);
        var manifest = ReadJsonObject(archive, "manifest.json");
        Assert.IsNotNull(manifest["installer"]);
        AssertManifestHashMatches(archive, manifest, "installer");
        foreach (var asset in project.InstallerProfile.Assets)
        {
            var entryName = $"installer/assets/{asset.Id:N}.bin";
            Assert.IsNotNull(archive.GetEntry(entryName));
            var manifestAsset = manifest["installer"]!["assets"]!.AsArray()
                .Single(item => item!["id"]!.GetValue<Guid>() == asset.Id)!;
            Assert.AreEqual(entryName, manifestAsset["entry"]!.GetValue<string>());
            AssertManifestHashMatches(archive, manifestAsset.AsObject());
        }
        var modFile = project.InstallerProfile.Mods[0].Files[0];
        var modEntryName = $"installer/assets/{modFile.Id:N}.bin";
        Assert.IsNotNull(archive.GetEntry(modEntryName));
        AssertManifestHashMatches(
            archive,
            manifest["installer"]!["assets"]!.AsArray()
                .Single(item => item!["id"]!.GetValue<Guid>() == modFile.Id)!
                .AsObject());
    }

    [TestMethod]
    public void SaveAndLoad_InstallerProfile_PreservesReleaseReadMes()
    {
        var project = CreateProject();
        project.InstallerProfile = CreateInstallerProfile();
        project.InstallerProfile.ReleaseReadMeEnglish = CreateReleaseReadMe(
            20,
            "notes-en.txt",
            "English notes");
        project.InstallerProfile.ReleaseReadMeBelarusian = CreateReleaseReadMe(
            21,
            "ПрачытайМяне.txt",
            "Беларускія нататкі");
        var path = Path.Combine(_testDirectory, "installer-readmes.byx");

        _serializer.Save(path, project);
        var loaded = _serializer.Load(path);

        Assert.IsNotNull(loaded.InstallerProfile);
        Assert.IsNotNull(loaded.InstallerProfile.ReleaseReadMeEnglish);
        Assert.IsNotNull(loaded.InstallerProfile.ReleaseReadMeBelarusian);
        Assert.AreEqual(
            project.InstallerProfile.ReleaseReadMeEnglish.Id,
            loaded.InstallerProfile.ReleaseReadMeEnglish.Id);
        Assert.AreEqual("notes-en.txt", loaded.InstallerProfile.ReleaseReadMeEnglish.OriginalFileName);
        CollectionAssert.AreEqual(
            project.InstallerProfile.ReleaseReadMeEnglish.Data,
            loaded.InstallerProfile.ReleaseReadMeEnglish.Data);
        Assert.AreEqual(
            "ПрачытайМяне.txt",
            loaded.InstallerProfile.ReleaseReadMeBelarusian.OriginalFileName);
        CollectionAssert.AreEqual(
            project.InstallerProfile.ReleaseReadMeBelarusian.Data,
            loaded.InstallerProfile.ReleaseReadMeBelarusian.Data);

        using var archive = ZipFile.OpenRead(path);
        var profile = ReadJsonObject(archive, "installer/profile.json");
        Assert.AreEqual(3, profile["version"]!.GetValue<int>());
        Assert.AreEqual(
            project.InstallerProfile.ReleaseReadMeEnglish.Id,
            profile["releaseReadMeEnglish"]!["id"]!.GetValue<Guid>());
        Assert.AreEqual(
            "notes-en.txt",
            profile["releaseReadMeEnglish"]!["fileName"]!.GetValue<string>());
        var manifest = ReadJsonObject(archive, "manifest.json");
        Assert.HasCount(6, manifest["installer"]!["assets"]!.AsArray());
        AssertManifestHashMatches(
            archive,
            manifest["installer"]!["assets"]!.AsArray()
                .Single(item => item!["id"]!.GetValue<Guid>() ==
                    project.InstallerProfile.ReleaseReadMeEnglish.Id)!
                .AsObject());
    }

    [TestMethod]
    public void Save_InstallerProfile_WritesVersion3WithoutReadMes()
    {
        var project = CreateProject();
        project.InstallerProfile = CreateInstallerProfile();
        var path = Path.Combine(_testDirectory, "installer-v2.byx");

        _serializer.Save(path, project);

        using var archive = ZipFile.OpenRead(path);
        var profile = ReadJsonObject(archive, "installer/profile.json");
        Assert.AreEqual(3, profile["version"]!.GetValue<int>());
        Assert.IsFalse(profile.ContainsKey("releaseReadMeEnglish"));
        Assert.IsFalse(profile.ContainsKey("releaseReadMeBelarusian"));
    }

    [TestMethod]
    public void Load_InstallerProfileVersion1_IsRejected()
    {
        var project = CreateProject();
        project.InstallerProfile = CreateInstallerProfile();
        var path = Path.Combine(_testDirectory, "installer-v1.byx");
        _serializer.Save(path, project);

        MutateInstallerProfile(path, profile => profile["version"] = 1);

        Assert.Throws<InvalidDataException>(() => _serializer.Load(path));
    }

    [TestMethod]
    public void Load_InstallerProfileVersion2_MigratesAdditionalFilesToRequiredMod()
    {
        var project = CreateProject();
        project.InstallerProfile = CreateInstallerProfile();
        var path = Path.Combine(_testDirectory, "installer-v2.byx");
        _serializer.Save(path, project);
        var legacyFile = project.InstallerProfile.Mods[0].Files[0];

        MutateInstallerProfile(path, profile =>
        {
            profile["version"] = 2;
            profile["mods"]!.AsArray().Clear();
            profile.Remove("mods");
            profile["assets"]!.AsArray().Add(new JsonObject
            {
                ["id"] = legacyFile.Id,
                ["role"] = nameof(InstallerAssetRole.Additional),
                ["destinationPath"] = legacyFile.DestinationPath,
            });
        });

        var loaded = _serializer.Load(path);
        var loadedAgain = _serializer.Load(path);

        Assert.IsNotNull(loaded.InstallerProfile);
        Assert.HasCount(3, loaded.InstallerProfile.Assets);
        Assert.HasCount(1, loaded.InstallerProfile.Mods);
        Assert.AreEqual("Legacy additional files", loaded.InstallerProfile.Mods[0].Name);
        Assert.IsTrue(loaded.InstallerProfile.Mods[0].IsRequired);
        Assert.AreEqual(loaded.InstallerProfile.Mods[0].Id, loadedAgain.InstallerProfile!.Mods[0].Id);
        Assert.AreEqual(legacyFile.Id, loaded.InstallerProfile.Mods[0].Files[0].Id);
        CollectionAssert.AreEqual(legacyFile.Data, loaded.InstallerProfile.Mods[0].Files[0].Data);
    }

    [TestMethod]
    public void Load_InstallerProfileVersion4_IsRejected()
    {
        var project = CreateProject();
        project.InstallerProfile = CreateInstallerProfile();
        var path = Path.Combine(_testDirectory, "installer-v4.byx");
        _serializer.Save(path, project);

        MutateInstallerProfile(path, profile => profile["version"] = 4);

        Assert.Throws<InvalidDataException>(() => _serializer.Load(path));
    }

    [TestMethod]
    public void SaveAndLoad_EmptyInstallerProfile_PreservesDraftWithoutAssetEntries()
    {
        var project = CreateProject();
        project.InstallerProfile = CreateInstallerProfile();
        project.InstallerProfile.Assets.Clear();
        project.InstallerProfile.Mods.Clear();
        var path = Path.Combine(_testDirectory, "empty-installer-profile.byx");

        _serializer.Save(path, project);
        var loaded = _serializer.Load(path);

        Assert.IsNotNull(loaded.InstallerProfile);
        Assert.AreEqual(project.InstallerProfile.ProductId, loaded.InstallerProfile.ProductId);
        Assert.AreEqual(project.InstallerProfile.Name, loaded.InstallerProfile.Name);
        Assert.HasCount(0, loaded.InstallerProfile.Assets);
        using var archive = ZipFile.OpenRead(path);
        var manifest = ReadJsonObject(archive, "manifest.json");
        Assert.IsNotNull(manifest["installer"]);
        Assert.HasCount(0, manifest["installer"]!["assets"]!.AsArray());
        Assert.IsNotNull(archive.GetEntry("installer/profile.json"));
        Assert.IsFalse(archive.Entries.Any(entry =>
            entry.FullName.StartsWith("installer/assets/", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Load_InstallerAssetWithWrongHash_IsRejected()
    {
        var project = CreateProject();
        project.InstallerProfile = CreateInstallerProfile();
        var path = Path.Combine(_testDirectory, "corrupt-installer.byx");
        _serializer.Save(path, project);
        var asset = project.InstallerProfile.Assets[0];

        ReplaceEntry(path, $"installer/assets/{asset.Id:N}.bin", [1, 2, 3]);

        Assert.Throws<InvalidDataException>(() => _serializer.Load(path));
    }

    [TestMethod]
    public void Load_MissingInstallerAsset_IsRejected()
    {
        var project = CreateProject();
        project.InstallerProfile = CreateInstallerProfile();
        var path = Path.Combine(_testDirectory, "missing-installer.byx");
        _serializer.Save(path, project);
        var asset = project.InstallerProfile.Assets[0];

        DeleteEntry(path, $"installer/assets/{asset.Id:N}.bin");

        Assert.Throws<InvalidDataException>(() => _serializer.Load(path));
    }

    [TestMethod]
    [DataRow("gxt/main.gxt")]
    [DataRow("txd/fonts.txd")]
    [DataRow("mapping/characters.json")]
    [DataRow("metadata/entries.json")]
    public void Load_WrongEmbeddedFileHash_IsRejected(string entryName)
    {
        var path = SaveProject();
        ReplaceEntry(path, entryName, [1, 2, 3, 4]);

        var exception = Assert.Throws<InvalidDataException>(() => _serializer.Load(path));

        StringAssert.Contains(exception.Message, entryName);
    }

    [TestMethod]
    public void Load_MissingReferencedEntry_IsRejected()
    {
        var path = SaveProject();
        DeleteEntry(path, "metadata/entries.json");

        Assert.Throws<InvalidDataException>(() => _serializer.Load(path));
    }

    [TestMethod]
    public void Load_UnexpectedEntry_IsRejected()
    {
        var path = SaveProject();
        AddEntry(path, "extra/data.bin", [1]);

        Assert.Throws<InvalidDataException>(() => _serializer.Load(path));
    }

    [TestMethod]
    public void Load_UnsafeArchivePath_IsRejected()
    {
        var path = SaveProject();
        AddEntry(path, "../outside.txt", [1]);

        Assert.Throws<InvalidDataException>(() => _serializer.Load(path));
    }

    [TestMethod]
    public void Load_CaseInsensitiveDuplicateEntry_IsRejected()
    {
        var path = SaveProject();
        AddEntry(path, "GXT/MAIN.GXT", [1]);

        Assert.Throws<InvalidDataException>(() => _serializer.Load(path));
    }

    [TestMethod]
    public void Load_ArchiveEntryLimit_IsEnforced()
    {
        var path = SaveProject();
        using (var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update))
        {
            for (var index = archive.Entries.Count; index <= 64; index++)
            {
                using var entryStream = archive.CreateEntry($"extra/{index}.bin").Open();
                entryStream.WriteByte(0);
            }
        }

        Assert.Throws<InvalidDataException>(() => _serializer.Load(path));
    }

    [TestMethod]
    public void Load_TruncatedZipAtStructuralBoundaries_ThrowsInvalidData()
    {
        var seedPath = SaveProject();
        var seed = File.ReadAllBytes(seedPath);
        var lengths = new[] { 0, 1, 4, seed.Length / 4, seed.Length / 2, seed.Length - 22, seed.Length - 1 };

        foreach (var length in lengths.Distinct())
        {
            var path = Path.Combine(_testDirectory, $"truncated-{length}.byx");
            File.WriteAllBytes(path, seed[..length]);

            Assert.Throws<InvalidDataException>(() => _serializer.Load(path),
                $"A BYX document truncated to {length} bytes leaked another exception.");
        }
    }

    [TestMethod]
    public void Load_CorruptCentralDirectoryCountsAndOffsets_ThrowInvalidData()
    {
        var seedPath = SaveProject();
        var seed = File.ReadAllBytes(seedPath);
        var endOfCentralDirectory = seed.Length - 22;
        var mutations = new (int Offset, uint Value, int Width)[]
        {
            (endOfCentralDirectory + 10, ushort.MaxValue, sizeof(ushort)),
            (endOfCentralDirectory + 16, uint.MaxValue, sizeof(uint)),
        };

        for (var index = 0; index < mutations.Length; index++)
        {
            var mutation = mutations[index];
            var data = seed.ToArray();
            if (mutation.Width == sizeof(ushort))
            {
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(mutation.Offset), (ushort)mutation.Value);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(mutation.Offset), mutation.Value);
            }

            var path = Path.Combine(_testDirectory, $"central-directory-{index}.byx");
            File.WriteAllBytes(path, data);
            Assert.Throws<InvalidDataException>(() => _serializer.Load(path),
                $"Central-directory mutation {index} was accepted.");
        }
    }

    [TestMethod]
    public void Load_NonCanonicalManifestPath_IsRejected()
    {
        var path = SaveProject();
        MutateManifest(path, manifest =>
            manifest["metadata"]!["entry"] = "metadata/other.json");

        Assert.Throws<InvalidDataException>(() => _serializer.Load(path));
    }

    [TestMethod]
    public void Load_MissingMetadataManifestItem_IsRejected()
    {
        var path = SaveProject();
        MutateManifest(path, manifest => manifest["metadata"] = null);

        Assert.Throws<InvalidDataException>(() => _serializer.Load(path));
    }

    [TestMethod]
    public void Load_DuplicateMetadataIdentity_IsRejected()
    {
        var path = SaveProject();
        MutateMetadata(path, metadata =>
            metadata["entries"]!.AsArray().Add(metadata["entries"]![0]!.DeepClone()));

        Assert.Throws<InvalidDataException>(() => _serializer.Load(path));
    }

    [TestMethod]
    public void Load_MetadataOccurrenceWithUnknownBlock_IsRejected()
    {
        var path = SaveProject();
        MutateMetadata(path, metadata =>
            metadata["entries"]![0]!["occurrences"]![0]!["blockId"] = "missing.block");

        Assert.Throws<InvalidDataException>(() => _serializer.Load(path));
    }

    [TestMethod]
    public void Save_DuplicateMetadataBlockId_IsRejected()
    {
        var project = CreateProject();
        project.Metadata.Blocks.Add(new ProjectMetadataBlock
        {
            Id = project.Metadata.Blocks[0].Id,
            Type = "mission",
            Name = "Duplicate",
            Order = 999,
        });

        Assert.Throws<InvalidDataException>(() =>
            _serializer.Save(Path.Combine(_testDirectory, "invalid.byx"), project));
    }

    [TestMethod]
    public void Save_GtaIIIProjectWithTxd_IsRejected()
    {
        var project = CreateProject();
        project = new EditorProject
        {
            GxtSourceName = "american.gxt",
            GameType = GXTType.GtaIII,
            GxtManager = GxtManagerFactory.Create(GXTType.GtaIII),
            AttachedTxd = project.AttachedTxd,
            CharacterMap = project.CharacterMap,
            Metadata = project.Metadata,
        };

        Assert.Throws<InvalidOperationException>(() =>
            _serializer.Save(Path.Combine(_testDirectory, "invalid.byx"), project));
    }

    private string SaveProject()
    {
        var path = Path.Combine(_testDirectory, "project.byx");
        _serializer.Save(path, CreateProject());
        return path;
    }

    private EditorProject CreateProject()
    {
        var manager = GxtManagerFactory.Create(
            GXTType.GtaViceCity,
            sourceName: "american.gxt",
            sourceTexts: ["ЖЖ"],
            language: GxtLanguage.Belarusian);
        var characterMap = new CharacterMapProfile
        {
            IsVerified = true,
            Mappings =
            [
                new CharacterMapEntry
                {
                    Character = 'Ж',
                    Codes = [200],
                    PreferredCode = 200,
                },
            ],
        };
        manager.CharacterMap = characterMap;
        manager.AddGXTEntry("HELLO", "Ж");
        manager.EditGXTEntry("HELLO", "ЖЖ");

        return new EditorProject
        {
            GxtSourceName = "american.gxt",
            GxtSourcePath = Path.Combine(_testDirectory, "american.gxt"),
            GameType = GXTType.GtaViceCity,
            GxtManager = manager,
            UsesCustomDictionary = true,
            IsDirty = true,
            AttachedTxd = CreateAttachment(),
            CharacterMap = characterMap.Clone(),
            FontMetrics = CreateFontMetrics(),
            Metadata = CreateMetadata(),
        };
    }

    private EditorProject CreateProjectWithBelarusianPreset()
    {
        var characterMap = CharacterMapPresets.Belarusian;
        var manager = GxtManagerFactory.Create(
            GXTType.GtaViceCity,
            sourceName: "american.gxt",
            sourceTexts: ["Ж"],
            language: GxtLanguage.Belarusian);
        manager.CharacterMap = characterMap.Clone();
        manager.AddGXTEntry("HELLO", "Ж");

        return new EditorProject
        {
            GxtSourceName = "american.gxt",
            GameType = GXTType.GtaViceCity,
            GxtManager = manager,
            UsesCustomDictionary = true,
            CharacterMap = characterMap.Clone(),
            FontMetrics = FontMetricsPresets.BelarusianViceCity,
        };
    }

    private static FontMetricsProfile CreateFontMetrics() => new()
    {
        Font2 = new FontMetricsTable
        {
            Advances = Enumerable.Range(0, FontMetricsTable.MetricCount)
                .Select(index => (ushort)(index % 31))
                .ToArray(),
        },
        Font1 = new FontMetricsTable
        {
            Advances = Enumerable.Range(0, FontMetricsTable.MetricCount)
                .Select(index => (ushort)(30 - index % 31))
                .ToArray(),
        },
        Overrides =
        [
            new FontMetricOverride
            {
                Context = FontRenderContext.MainMenu,
                Font = FontTextureKind.Font2,
                Code = 0x91,
                Advance = 17,
            },
            new FontMetricOverride
            {
                Context = FontRenderContext.SaveLoad,
                Font = FontTextureKind.Font1,
                Code = 0xA8,
                Advance = 9,
            },
        ],
    };

    private EditorProject CreatePlainProject()
    {
        var manager = GxtManagerFactory.Create(
            GXTType.GtaViceCity,
            sourceName: "american.gxt",
            sourceTexts: ["Hello"],
            language: GxtLanguage.English);
        manager.AddGXTEntry("HELLO", "Hello");
        return new EditorProject
        {
            GxtSourceName = "american.gxt",
            GameType = GXTType.GtaViceCity,
            GxtManager = manager,
        };
    }

    private ProjectMetadata CreateMetadata() => new()
    {
        Blocks =
        [
            new ProjectMetadataBlock
            {
                Id = "mission.the-party",
                Type = "mission",
                Name = "The Party",
                Description = "Opening cutscene",
                Order = 3000,
            },
            new ProjectMetadataBlock
            {
                Id = "ui.general",
                Type = "interface",
                Name = "General interface",
                Order = 900000,
            },
        ],
        Entries =
        [
            new ProjectEntryMetadata
            {
                Table = "MAIN",
                Key = "HELLO",
                Comment = "Праверыць голас Кена.",
                Occurrences =
                [
                    new ProjectEntryOccurrence
                    {
                        BlockId = "mission.the-party",
                        Order = 10,
                        Context = "Ken's office",
                    },
                    new ProjectEntryOccurrence
                    {
                        BlockId = "mission.the-party",
                        Order = 20,
                        Context = "On the yacht",
                    },
                ],
            },
            new ProjectEntryMetadata
            {
                Table = "FRENCH",
                Key = "HELLO",
                Occurrences =
                [
                    new ProjectEntryOccurrence
                    {
                        BlockId = "ui.general",
                        Order = 30,
                    },
                ],
            },
        ],
    };

    private TxdAttachment CreateAttachment()
    {
        var data = TestTxdFactory.Create(
            TestTxdFactory.Bgra32("font1", 1, 1, [1, 2, 3, 255]));
        return new TxdAttachment
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            OriginalFileName = "fonts.txd",
            DisplayName = "fonts",
            SourcePath = Path.Combine(_testDirectory, "fonts.txd"),
            Data = data,
            Document = _txdReader.Read(data, "fonts.txd"),
        };
    }

    private static InstallerProfile CreateInstallerProfile() => new()
    {
        ProductId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
        Name = "GTA Vice City — Беларусізатар",
        Version = "1.2.3",
        Publisher = "Belarusian Games",
        OutputFileName = "Belarusian_Setup.exe",
        Assets =
        [
            CreateInstallerAsset(InstallerAssetRole.MainAsi, "BelarusianLanguage.asi", "main.asi", 1),
            CreateInstallerAsset(InstallerAssetRole.AsiLoader, "dinput8.dll", "dinput8.dll", 2),
            CreateInstallerAsset(InstallerAssetRole.SilentPatch, "SilentPatchVC.asi", "SilentPatchVC.asi", 3),
        ],
        Mods =
        [
            new InstallerMod
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                Name = "Optional configuration",
                IsRequired = false,
                Files =
                [
                    new InstallerModFile
                    {
                        Id = Guid.Parse("00000000-0000-0000-0000-000000000004"),
                        OriginalFileName = "config.ini",
                        DestinationPath = "plugins\\config.ini",
                        Data = "enabled=1"u8.ToArray(),
                    },
                ],
            },
        ],
    };

    private static InstallerAsset CreateInstallerAsset(
        InstallerAssetRole role,
        string destination,
        string sourceName,
        int id) => new()
    {
        Id = new Guid(id, 0, 0, new byte[8]),
        Role = role,
        OriginalFileName = sourceName,
        DestinationPath = destination,
        Data = CreateX86PeImage(),
    };

    private static InstallerReleaseDocument CreateReleaseReadMe(int id, string fileName, string text) => new()
    {
        Id = new Guid(id, 0, 0, new byte[8]),
        OriginalFileName = fileName,
        Data = Encoding.UTF8.GetBytes(text),
    };

    private static byte[] CreateX86PeImage()
    {
        var data = new byte[128];
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x3C, 4), 0x40);
        "PE\0\0"u8.CopyTo(data.AsSpan(0x40));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x44, 2), 0x014C);
        return data;
    }

    private static JsonObject ReadJsonObject(ZipArchive archive, string entryName)
    {
        using var reader = new StreamReader(archive.GetEntry(entryName)!.Open());
        return JsonNode.Parse(reader.ReadToEnd())!.AsObject();
    }

    private static void AssertManifestHashMatches(
        ZipArchive archive,
        JsonObject manifest,
        string propertyName)
    {
        var item = manifest[propertyName]!;
        var entryName = item["entry"]!.GetValue<string>();
        using var stream = archive.GetEntry(entryName)!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        Assert.AreEqual(
            item["sha256"]!.GetValue<string>(),
            Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray())));
    }

    private static void AssertManifestHashMatches(ZipArchive archive, JsonObject item)
    {
        var entryName = item["entry"]!.GetValue<string>();
        using var stream = archive.GetEntry(entryName)!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        Assert.AreEqual(
            item["sha256"]!.GetValue<string>(),
            Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray())));
    }

    private static void AssertFontMetricsAreEqual(
        FontMetricsProfile expected,
        FontMetricsProfile actual)
    {
        Assert.AreEqual(expected.Version, actual.Version);
        CollectionAssert.AreEqual(expected.Font2.Advances, actual.Font2.Advances);
        CollectionAssert.AreEqual(expected.Font1.Advances, actual.Font1.Advances);
        Assert.AreEqual(expected.Overrides.Count, actual.Overrides.Count);
        for (var index = 0; index < expected.Overrides.Count; index++)
        {
            Assert.AreEqual(expected.Overrides[index].Context, actual.Overrides[index].Context);
            Assert.AreEqual(expected.Overrides[index].Font, actual.Overrides[index].Font);
            Assert.AreEqual(expected.Overrides[index].Code, actual.Overrides[index].Code);
            Assert.AreEqual(expected.Overrides[index].Advance, actual.Overrides[index].Advance);
        }
    }

    private static void ConvertToLegacyVersion(string path, int version)
    {
        RemoveFontMetrics(path);
        MutateManifest(path, manifest =>
        {
            manifest["version"] = version;
            if (version == 3)
            {
                manifest.Remove("installer");
            }
        });
    }

    private static void RemoveFontMetrics(string path)
    {
        DeleteEntry(path, "font/metrics.json");
        MutateManifest(path, manifest => manifest.Remove("fontMetrics"));
    }

    private static void ReplaceFontMetricsAndUpdateHash(string path, byte[] data)
    {
        ReplaceEntry(path, "font/metrics.json", data);
        MutateManifest(path, manifest =>
            manifest["fontMetrics"]!["sha256"] =
                Convert.ToHexStringLower(SHA256.HashData(data)));
    }

    private static void MutateManifest(string path, Action<JsonObject> mutation)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update);
        var manifest = ReadJsonObject(archive, "manifest.json");
        mutation(manifest);
        archive.GetEntry("manifest.json")!.Delete();
        WriteJsonEntry(archive, "manifest.json", manifest);
    }

    private static void MutateInstallerProfile(string path, Action<JsonObject> mutation)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update);
        var profile = ReadJsonObject(archive, "installer/profile.json");
        mutation(profile);
        var profileData = JsonSerializer.SerializeToUtf8Bytes(
            profile,
            new JsonSerializerOptions { WriteIndented = true });
        archive.GetEntry("installer/profile.json")!.Delete();
        WriteArchiveEntry(archive, "installer/profile.json", profileData);

        var manifest = ReadJsonObject(archive, "manifest.json");
        manifest["installer"]!["sha256"] = Convert.ToHexStringLower(SHA256.HashData(profileData));
        archive.GetEntry("manifest.json")!.Delete();
        WriteJsonEntry(archive, "manifest.json", manifest);
    }

    private static void MutateMetadata(string path, Action<JsonObject> mutation)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update);
        var metadata = ReadJsonObject(archive, "metadata/entries.json");
        mutation(metadata);
        var metadataData = JsonSerializer.SerializeToUtf8Bytes(
            metadata,
            new JsonSerializerOptions { WriteIndented = true });
        archive.GetEntry("metadata/entries.json")!.Delete();
        WriteArchiveEntry(archive, "metadata/entries.json", metadataData);

        var manifest = ReadJsonObject(archive, "manifest.json");
        manifest["metadata"]!["sha256"] = Convert.ToHexStringLower(SHA256.HashData(metadataData));
        archive.GetEntry("manifest.json")!.Delete();
        WriteJsonEntry(archive, "manifest.json", manifest);
    }

    private static void WriteJsonEntry(ZipArchive archive, string name, JsonObject value)
    {
        using var writer = new Utf8JsonWriter(archive.CreateEntry(name).Open());
        value.WriteTo(writer, new JsonSerializerOptions { WriteIndented = true });
    }

    private static void WriteArchiveEntry(ZipArchive archive, string name, byte[] data)
    {
        using var entryStream = archive.CreateEntry(name).Open();
        entryStream.Write(data);
    }

    private static void ReplaceEntry(string path, string name, byte[] data)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update);
        archive.GetEntry(name)!.Delete();
        WriteArchiveEntry(archive, name, data);
    }

    private static void DeleteEntry(string path, string name)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update);
        archive.GetEntry(name)!.Delete();
    }

    private static void AddEntry(string path, string name, byte[] data)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update);
        WriteArchiveEntry(archive, name, data);
    }
}
