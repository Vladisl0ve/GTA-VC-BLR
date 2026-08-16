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
    public void SaveAndLoad_FullV3Project_PreservesEveryProjectPart()
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
                "metadata/entries.json",
            },
            archive.Entries.Select(entry => entry.FullName).ToArray());

        var manifest = ReadJsonObject(archive, "manifest.json");
        Assert.AreEqual(3, manifest["version"]!.GetValue<int>());
        Assert.AreEqual("txd/fonts.txd", manifest["txd"]!["entry"]!.GetValue<string>());
        Assert.AreEqual(
            "mapping/characters.json",
            manifest["characterMap"]!["entry"]!.GetValue<string>());
        Assert.AreEqual(
            "metadata/entries.json",
            manifest["metadata"]!["entry"]!.GetValue<string>());
        AssertManifestHashMatches(archive, manifest, "gxt");
        AssertManifestHashMatches(archive, manifest, "txd");
        AssertManifestHashMatches(archive, manifest, "characterMap");
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
        MutateManifest(path, manifest => manifest["version"] = 4);

        var exception = Assert.Throws<InvalidDataException>(() => _serializer.Load(path));

        StringAssert.Contains(exception.Message, "not supported yet");
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
            Metadata = CreateMetadata(),
        };
    }

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

    private static void MutateManifest(string path, Action<JsonObject> mutation)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update);
        var manifest = ReadJsonObject(archive, "manifest.json");
        mutation(manifest);
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
