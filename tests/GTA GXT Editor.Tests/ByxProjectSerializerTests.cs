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
        Assert.HasCount(2, loaded.TxdAttachments);
        CollectionAssert.AreEqual(project.TxdAttachments[0].Data, loaded.TxdAttachments[0].Data);
        CollectionAssert.AreEqual(project.TxdAttachments[1].Data, loaded.TxdAttachments[1].Data);
        CollectionAssert.AreEqual(
            new[] { "fonts", "fonts (2)" },
            loaded.TxdAttachments.Select(attachment => attachment.DisplayName).ToArray());
        Assert.IsTrue(loaded.TxdAttachments.All(attachment => attachment.SourcePath is null));
    }

    [TestMethod]
    public void Load_NewerManifestVersion_IsRejected()
    {
        var path = SaveProject();
        MutateManifest(path, manifest => manifest["version"] = 2);

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
    public void Load_WrongDictionaryHash_IsRejected()
    {
        var path = SaveProject();
        ReplaceEntry(path, "dictionary/characters.json", [1]);

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
    public void Load_DuplicateManifestTxdId_IsRejected()
    {
        var path = SaveProject();
        MutateManifest(path, manifest =>
        {
            var txd = manifest["txd"]!.AsArray();
            txd[1]!["id"] = txd[0]!["id"]!.GetValue<string>();
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
    public void Save_GtaIIIProjectWithTxd_IsRejected()
    {
        var project = CreateProject();
        project = new EditorProject
        {
            GxtSourceName = "american.gxt",
            GameType = GXTType.GtaIII,
            GxtManager = GxtManagerFactory.Create(GXTType.GtaIII),
            TxdAttachments = project.TxdAttachments,
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
        var secondBytes = TestTxdFactory.Create(
            TestTxdFactory.Bgra32("font2", 1, 1, [4, 5, 6, 128]));
        return new EditorProject
        {
            GxtSourceName = "american.gxt",
            GxtSourcePath = Path.Combine(_testDirectory, "american.gxt"),
            GameType = GXTType.GtaViceCity,
            GxtManager = manager,
            UsesCustomDictionary = true,
            IsDirty = true,
            TxdAttachments =
            [
                CreateAttachment(Guid.Parse("11111111-1111-1111-1111-111111111111"), "fonts", firstBytes),
                CreateAttachment(Guid.Parse("22222222-2222-2222-2222-222222222222"), "fonts (2)", secondBytes),
            ],
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
