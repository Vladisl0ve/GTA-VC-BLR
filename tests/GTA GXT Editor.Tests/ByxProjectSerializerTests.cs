using System.IO.Compression;
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
    public void SaveAndLoad_PreservesEditedGxtDictionaryAndExactTxdBytes()
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
        Assert.AreEqual('Ж', loaded.GxtManager.GetCharacterMap()[200]);
        Assert.AreEqual(
            "ЖЖ",
            loaded.GxtManager.ConvertBytesToText(loaded.GxtManager.GXTEntries.Single().Value));
        Assert.IsNotNull(loaded.AttachedTxd);
        CollectionAssert.AreEqual(project.AttachedTxd!.Data, loaded.AttachedTxd.Data);
        Assert.AreEqual("fonts", loaded.AttachedTxd.DisplayName);
        Assert.IsNull(loaded.AttachedTxd.SourcePath);
        Assert.IsNotNull(loaded.CharacterMap);
        Assert.IsTrue(loaded.CharacterMap.IsVerified);
        using var archive = ZipFile.OpenRead(path);
        using var reader = new StreamReader(archive.GetEntry("manifest.json")!.Open());
        Assert.AreEqual(2, JsonNode.Parse(reader.ReadToEnd())!["version"]!.GetValue<int>());
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
    public void Load_NewerManifestVersion_IsRejected()
    {
        var path = SaveProject();
        MutateManifest(path, manifest => manifest["version"] = 3);

        Assert.Throws<InvalidDataException>(() => _serializer.Load(path));
    }

    [TestMethod]
    public void Load_WrongGxtHash_IsRejectedBeforeOpeningGxt()
    {
        var path = SaveProject();
        ReplaceEntry(path, "gxt/main.gxt", [1, 2, 3, 4]);

        Assert.Throws<InvalidDataException>(() => _serializer.Load(path));
    }

    [TestMethod]
    public void Load_WrongCharacterMapHash_IsRejected()
    {
        var path = SaveProject();
        ReplaceEntry(path, "mapping/characters.json", [1]);

        Assert.Throws<InvalidDataException>(() => _serializer.Load(path));
    }

    [TestMethod]
    public void Load_MissingReferencedEntry_IsRejected()
    {
        var path = SaveProject();
        using (var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update))
        {
            archive.Entries.First(entry => entry.FullName.StartsWith("txd/", StringComparison.Ordinal)).Delete();
        }

        Assert.Throws<InvalidDataException>(() => _serializer.Load(path));
    }

    [TestMethod]
    public void Load_EmptyManifestTxdId_IsRejected()
    {
        var path = SaveProject();
        MutateManifest(path, manifest =>
        {
            manifest["txd"]!["id"] = Guid.Empty.ToString();
        });

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
    public void Load_Version1WithMultipleTxd_IsRejected()
    {
        var path = Path.Combine(_testDirectory, "legacy-multiple.byx");
        CreateVersion1Project(path, includeSecondTxd: true);

        var exception = Assert.Throws<InvalidDataException>(() => _serializer.Load(path));

        StringAssert.Contains(exception.Message, "несколько TXD");
    }

    [TestMethod]
    public void Load_Version1WithSingleTxd_MigratesAndSavesAsVersion2()
    {
        var path = Path.Combine(_testDirectory, "legacy-single.byx");
        var migratedPath = Path.Combine(_testDirectory, "migrated.byx");
        CreateVersion1Project(path, includeSecondTxd: false);

        var project = _serializer.Load(path);
        _serializer.Save(migratedPath, project);

        Assert.IsNotNull(project.AttachedTxd);
        Assert.IsNotNull(project.CharacterMap);
        Assert.AreEqual('Ж', project.CharacterMap.ToDecodeMap()[200]);
        using var archive = ZipFile.OpenRead(migratedPath);
        using var reader = new StreamReader(archive.GetEntry("manifest.json")!.Open());
        Assert.AreEqual(2, JsonNode.Parse(reader.ReadToEnd())!["version"]!.GetValue<int>());
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
        manager.CyrillicCharsDictionary = new Dictionary<int[], char>
        {
            [[200]] = 'Ж',
        };
        manager.AddGXTEntry("HELLO", "Ж");
        manager.EditGXTEntry("HELLO", "ЖЖ");

        var firstBytes = TestTxdFactory.Create(
            TestTxdFactory.Bgra32("font1", 1, 1, [1, 2, 3, 255]));
        return new EditorProject
        {
            GxtSourceName = "american.gxt",
            GxtSourcePath = Path.Combine(_testDirectory, "american.gxt"),
            GameType = GXTType.GtaViceCity,
            GxtManager = manager,
            UsesCustomDictionary = true,
            IsDirty = true,
            AttachedTxd = CreateAttachment(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "fonts",
                firstBytes),
            CharacterMap = CharacterMapProfile.FromDictionary(
                manager.CyrillicCharsDictionary,
                isVerified: true),
        };
    }

    private TxdAttachment CreateAttachment(Guid id, string displayName, byte[] data) => new()
    {
        Id = id,
        OriginalFileName = "fonts.txd",
        DisplayName = displayName,
        SourcePath = Path.Combine(_testDirectory, displayName, "fonts.txd"),
        Data = data,
        Document = _txdReader.Read(data, "fonts.txd"),
    };

    private void CreateVersion1Project(string path, bool includeSecondTxd)
    {
        var project = CreateProject();
        using var gxtStream = new MemoryStream();
        project.GxtManager.WriteGXT(gxtStream);
        var gxtData = gxtStream.ToArray();
        var first = project.AttachedTxd!;
        var secondData = TestTxdFactory.Create(
            TestTxdFactory.Bgra32("font2", 1, 1, [4, 5, 6, 128]));
        var second = CreateAttachment(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "fonts (2)",
            secondData);
        var dictionaryData = JsonSerializer.SerializeToUtf8Bytes(new[]
        {
            new ByxCharacterMapping { Codes = [200], Character = "Ж" },
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var gxtHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(gxtData));
        var dictionaryHash = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(dictionaryData));
        var manifest = new ByxManifestV1
        {
            Format = "BYX",
            Version = 1,
            Game = "GTA Vice City",
            Language = "be",
            Gxt = new ByxGxtItem
            {
                OriginalFileName = "american.gxt",
                Entry = "gxt/main.gxt",
                Sha256 = gxtHash,
            },
            Dictionary = new ByxDictionaryItem
            {
                Entry = "dictionary/characters.json",
                Sha256 = dictionaryHash,
            },
            Txd = includeSecondTxd
                ? [LegacyTxdItem(first), LegacyTxdItem(second)]
                : [LegacyTxdItem(first)],
        };
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteArchiveEntry(archive, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest, options));
        WriteArchiveEntry(archive, "gxt/main.gxt", gxtData);
        WriteArchiveEntry(archive, "dictionary/characters.json", dictionaryData);
        WriteArchiveEntry(archive, manifest.Txd[0].Entry, first.Data);
        if (includeSecondTxd)
        {
            WriteArchiveEntry(archive, manifest.Txd[1].Entry, second.Data);
        }
    }

    private static ByxTxdItem LegacyTxdItem(TxdAttachment attachment) => new()
    {
        Id = attachment.Id,
        OriginalFileName = attachment.OriginalFileName,
        DisplayName = attachment.DisplayName,
        Entry = $"txd/{attachment.Id:N}.txd",
        Sha256 = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(attachment.Data)),
    };

    private static void WriteArchiveEntry(ZipArchive archive, string name, byte[] data)
    {
        using var stream = archive.CreateEntry(name).Open();
        stream.Write(data);
    }

    private static void MutateManifest(string path, Action<JsonObject> mutation)
    {
        JsonObject manifest;
        using (var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("manifest.json")!;
            using (var reader = new StreamReader(entry.Open()))
            {
                manifest = JsonNode.Parse(reader.ReadToEnd())!.AsObject();
            }

            mutation(manifest);
            entry.Delete();
            using var writer = new Utf8JsonWriter(archive.CreateEntry("manifest.json").Open());
            manifest.WriteTo(writer, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    private static void ReplaceEntry(string path, string name, byte[] data)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update);
        archive.GetEntry(name)!.Delete();
        using var entryStream = archive.CreateEntry(name).Open();
        entryStream.Write(data);
    }

    private static void AddEntry(string path, string name, byte[] data)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update);
        using var entryStream = archive.CreateEntry(name).Open();
        entryStream.Write(data);
    }
}
