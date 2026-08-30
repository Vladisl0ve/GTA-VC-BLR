using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;
using GTA_GXT_Editor.Utils;

namespace GTA_GXT_Editor.Tests;

[TestClass]
public sealed class BinaryContractTests
{
    private const string GtaThirdOriginalHash =
        "f2f80427cfb918bda8cbbc95c5837c5f8b97b80ec9531ce176700d14b094d79e";
    private const string GtaThirdEditedHash =
        "c731f4099b7f7b9adf4710c7568a65e9fa7e084dcd316d6ef7ccd94f522554e8";
    private const string ViceCityOriginalHash =
        "5bc605a404369429d41da82d214b90ae740bf7129488dc08634253e598ed8b3b";
    private const string ViceCityEditedHash =
        "878a61b5e9deab56254c3d76841c40e67684af2d361be2f7f951eba64342041f";
    private const string TxdHash =
        "8af49bc69b36c533e46e9c959ec085eb976a791adc4c6836802a07ce5d2f58fa";
    private const string ByxHash =
        "5442e451b213d182c1b85b27ce452f766b01620e5a21400df3a324cebf4e9f70";

    private string _testDirectory = null!;
    private GxtManagerFactory _managerFactory = null!;
    private TxdReader _txdReader = null!;
    private ByxProjectSerializer _byxSerializer = null!;

    [TestInitialize]
    public void Initialize()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"gta-gxt-contract-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
        _managerFactory = new GxtManagerFactory();
        _txdReader = new TxdReader();
        _byxSerializer = new ByxProjectSerializer(_managerFactory, _txdReader);
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
    public void GtaIII_GoldenFile_OpenEditSaveReopen_MatchesIndependentBytes()
    {
        var sourcePath = FixturePath("gta3-original.gxt", GtaThirdOriginalHash);
        var expectedPath = FixturePath("gta3-edited.gxt", GtaThirdEditedHash);
        var sourceData = File.ReadAllBytes(sourcePath);
        Assert.AreEqual(0, BinaryPrimitives.ReadInt32LittleEndian(sourceData.AsSpan(8, 4)));
        Assert.AreEqual(12, BinaryPrimitives.ReadInt32LittleEndian(sourceData.AsSpan(20, 4)));
        Assert.AreEqual(GXTType.GtaIII, _managerFactory.DetectType(sourcePath));

        var manager = _managerFactory.Open(sourcePath, language: GxtLanguage.English);
        var sourceEntries = manager.GXTEntries.ToDictionary(
            entry => entry.DatName.GetClearName(),
            entry => manager.ConvertBytesToText(entry.Value));
        CollectionAssert.AreEquivalent(new[] { "HELLO", "SECOND" }, sourceEntries.Keys.ToArray());
        Assert.AreEqual("Hello", sourceEntries["HELLO"]);
        Assert.AreEqual("Second", sourceEntries["SECOND"]);

        manager.EditGXTEntry("HELLO", "Updated III");
        var outputPath = Path.Combine(_testDirectory, "gta3-output.gxt");
        manager.SaveGXTChanges(outputPath);

        CollectionAssert.AreEqual(File.ReadAllBytes(expectedPath), File.ReadAllBytes(outputPath));
        var reopened = _managerFactory.Open(outputPath, language: GxtLanguage.English);
        var reopenedEntries = reopened.GXTEntries.ToDictionary(
            entry => entry.DatName.GetClearName(),
            entry => reopened.ConvertBytesToText(entry.Value));
        Assert.AreEqual("Updated III", reopenedEntries["HELLO"]);
        Assert.AreEqual("Second", reopenedEntries["SECOND"]);
    }

    [TestMethod]
    public void ViceCity_GoldenFile_OpenEditSaveReopen_MatchesIndependentBytes()
    {
        var sourcePath = FixturePath("vice-city-original.gxt", ViceCityOriginalHash);
        var expectedPath = FixturePath("vice-city-edited.gxt", ViceCityEditedHash);
        var sourceData = File.ReadAllBytes(sourcePath);
        Assert.AreEqual(32, BinaryPrimitives.ReadInt32LittleEndian(sourceData.AsSpan(16, 4)));
        Assert.AreEqual(72, BinaryPrimitives.ReadInt32LittleEndian(sourceData.AsSpan(28, 4)));
        Assert.AreEqual(GXTType.GtaViceCity, _managerFactory.DetectType(sourcePath));

        var manager = _managerFactory.Open(sourcePath, language: GxtLanguage.English);
        var sourceEntries = manager.GXTEntries
            .Cast<GTAVC.GXTEntry>()
            .ToDictionary(
                entry => $"{entry.TableName.GetClearName()}/{entry.DatName.GetClearName()}",
                entry => manager.ConvertBytesToText(entry.Value));
        CollectionAssert.AreEquivalent(
            new[] { "MAIN/HELLO", "MISSION/BRIEF" },
            sourceEntries.Keys.ToArray());
        Assert.AreEqual("Hello", sourceEntries["MAIN/HELLO"]);
        Assert.AreEqual("Go there", sourceEntries["MISSION/BRIEF"]);

        manager.EditGXTEntry("HELLO", "Updated VC", "MAIN");
        var outputPath = Path.Combine(_testDirectory, "vice-city-output.gxt");
        manager.SaveGXTChanges(outputPath);

        CollectionAssert.AreEqual(File.ReadAllBytes(expectedPath), File.ReadAllBytes(outputPath));
        var outputData = File.ReadAllBytes(outputPath);
        Assert.AreEqual(82, BinaryPrimitives.ReadInt32LittleEndian(outputData.AsSpan(28, 4)));
        var reopened = _managerFactory.Open(outputPath, language: GxtLanguage.English);
        var reopenedEntries = reopened.GXTEntries
            .Cast<GTAVC.GXTEntry>()
            .ToDictionary(
                entry => $"{entry.TableName.GetClearName()}/{entry.DatName.GetClearName()}",
                entry => reopened.ConvertBytesToText(entry.Value));
        Assert.AreEqual("Updated VC", reopenedEntries["MAIN/HELLO"]);
        Assert.AreEqual("Go there", reopenedEntries["MISSION/BRIEF"]);
    }

    [TestMethod]
    public void Txd_GoldenFile_OpenExportReopen_PreservesBytes()
    {
        var sourcePath = FixturePath("font1.txd", TxdHash);
        var workflow = new CharacterMapWorkflow(_txdReader);

        var attachment = workflow.LoadAttachment(sourcePath);
        AssertTxdContract(attachment.Document);

        var outputPath = Path.Combine(_testDirectory, "font1-exported.txd");
        workflow.ExportAttachment(outputPath, attachment);

        CollectionAssert.AreEqual(File.ReadAllBytes(sourcePath), File.ReadAllBytes(outputPath));
        AssertTxdContract(workflow.LoadAttachment(outputPath).Document);
    }

    [TestMethod]
    public void ByxV3_GoldenFile_OpenEditSaveReopen_PreservesContract()
    {
        var sourcePath = FixturePath("project.byx", ByxHash);
        var originalArchive = ReadArchiveEntries(sourcePath);
        AssertByxManifestAndHashes(originalArchive, expectedVersion: 3);
        CollectionAssert.AreEqual(
            File.ReadAllBytes(FixturePath("vice-city-original.gxt", ViceCityOriginalHash)),
            originalArchive["gxt/main.gxt"]);
        CollectionAssert.AreEqual(
            File.ReadAllBytes(FixturePath("font1.txd", TxdHash)),
            originalArchive["txd/fonts.txd"]);

        var project = _byxSerializer.Load(sourcePath);
        Assert.AreEqual("contract-vc.gxt", project.GxtSourceName);
        Assert.AreEqual(GXTType.GtaViceCity, project.GameType);
        Assert.AreEqual(GxtLanguage.English, project.GxtManager.Language);
        Assert.IsTrue(project.UsesCustomDictionary);
        Assert.IsNotNull(project.CharacterMap);
        Assert.IsTrue(project.CharacterMap.IsVerified);
        Assert.AreEqual('Ж', project.CharacterMap.ToDecodeMap()[0x80]);
        Assert.IsNotNull(project.AttachedTxd);
        AssertTxdContract(project.AttachedTxd.Document);
        Assert.AreEqual("Original note", project.Metadata.Entries.Single().Comment);
        Assert.AreEqual(
            "Hello",
            project.GxtManager.ConvertBytesToText(
                project.GxtManager.GXTEntries
                    .Cast<GTAVC.GXTEntry>()
                    .Single(entry => entry.DatName.GetClearName() == "HELLO")
                    .Value));

        project.GxtManager.EditGXTEntry("HELLO", "Updated VC", "MAIN");
        project.Metadata.Entries.Single().Comment = "Updated note";
        project.IsDirty = true;
        var outputPath = Path.Combine(_testDirectory, "project-output.byx");
        _byxSerializer.Save(outputPath, project);

        var outputArchive = ReadArchiveEntries(outputPath);
        AssertByxManifestAndHashes(outputArchive, expectedVersion: 6);
        CollectionAssert.AreEqual(
            File.ReadAllBytes(FixturePath("vice-city-edited.gxt", ViceCityEditedHash)),
            outputArchive["gxt/main.gxt"]);
        CollectionAssert.AreEqual(
            originalArchive["txd/fonts.txd"],
            outputArchive["txd/fonts.txd"]);
        AssertJsonEquivalent(
            ParseJson(originalArchive["mapping/characters.json"]),
            ParseJson(outputArchive["mapping/characters.json"]));
        var expectedMetadata = ParseJson(originalArchive["metadata/entries.json"]);
        expectedMetadata["entries"]![0]!["comment"] = "Updated note";
        AssertJsonEquivalent(
            expectedMetadata,
            ParseJson(outputArchive["metadata/entries.json"]));

        var reopened = _byxSerializer.Load(outputPath);
        Assert.IsFalse(reopened.IsDirty);
        Assert.AreEqual("Updated note", reopened.Metadata.Entries.Single().Comment);
        Assert.AreEqual('Ж', reopened.CharacterMap!.ToDecodeMap()[0x80]);
        Assert.IsNotNull(reopened.AttachedTxd);
        AssertTxdContract(reopened.AttachedTxd.Document);
        Assert.AreEqual(
            "Updated VC",
            reopened.GxtManager.ConvertBytesToText(
                reopened.GxtManager.GXTEntries
                    .Cast<GTAVC.GXTEntry>()
                    .Single(entry => entry.DatName.GetClearName() == "HELLO")
                    .Value));
        Assert.AreEqual(
            "Go there",
            reopened.GxtManager.ConvertBytesToText(
                reopened.GxtManager.GXTEntries
                    .Cast<GTAVC.GXTEntry>()
                    .Single(entry => entry.DatName.GetClearName() == "BRIEF")
                    .Value));
    }

    private static void AssertTxdContract(TxdDocument document)
    {
        Assert.AreEqual(0x1003FFFFu, document.RenderWareVersion);
        Assert.HasCount(1, document.Textures);
        var texture = document.Textures.Single();
        Assert.AreEqual("font1", texture.Name);
        Assert.AreEqual(string.Empty, texture.MaskName);
        Assert.AreEqual(TxdPlatform.D3D8, texture.Platform);
        Assert.AreEqual(2, texture.Width);
        Assert.AreEqual(1, texture.Height);
        Assert.AreEqual(32, texture.Depth);
        Assert.AreEqual(1, texture.MipmapCount);
        Assert.AreEqual(0x0500u, texture.RasterFormat);
        Assert.AreEqual(TxdCompression.None, texture.Compression);
        Assert.IsTrue(texture.HasAlpha);
        CollectionAssert.AreEqual(
            new byte[] { 1, 2, 3, 255, 10, 20, 30, 128 },
            texture.PixelsBgra32);
    }

    private static void AssertByxManifestAndHashes(
        IReadOnlyDictionary<string, byte[]> entries,
        int expectedVersion)
    {
        CollectionAssert.AreEquivalent(
            new[]
            {
                "gxt/main.gxt",
                "txd/fonts.txd",
                "mapping/characters.json",
                "metadata/entries.json",
                "manifest.json",
            },
            entries.Keys.ToArray());

        var manifest = ParseJson(entries["manifest.json"]);
        var expectedProperties = new List<string>
        {
            "format", "version", "game", "language", "gxt", "txd", "characterMap", "metadata",
        };
        if (expectedVersion >= 4)
        {
            expectedProperties.Add("installer");
        }
        if (expectedVersion >= 5)
        {
            expectedProperties.Add("fontMetrics");
        }
        if (expectedVersion >= 6)
        {
            expectedProperties.Add("asiFontProfileBinding");
        }

        AssertObjectProperties(manifest, expectedProperties.ToArray());
        Assert.AreEqual("BYX", manifest["format"]!.GetValue<string>());
        Assert.AreEqual(expectedVersion, manifest["version"]!.GetValue<int>());
        if (expectedVersion >= 4)
        {
            Assert.IsNull(manifest["installer"]);
        }
        if (expectedVersion >= 5)
        {
            Assert.IsNull(manifest["fontMetrics"]);
        }
        if (expectedVersion >= 6)
        {
            Assert.IsNull(manifest["asiFontProfileBinding"]);
        }
        Assert.AreEqual("GTA Vice City", manifest["game"]!.GetValue<string>());
        Assert.AreEqual("en", manifest["language"]!.GetValue<string>());

        var gxt = manifest["gxt"]!.AsObject();
        AssertObjectProperties(gxt, "originalFileName", "entry", "sha256");
        Assert.AreEqual("contract-vc.gxt", gxt["originalFileName"]!.GetValue<string>());
        var txd = manifest["txd"]!.AsObject();
        AssertObjectProperties(txd, "originalFileName", "entry", "sha256");
        Assert.AreEqual("fonts.txd", txd["originalFileName"]!.GetValue<string>());
        var characterMap = manifest["characterMap"]!.AsObject();
        AssertObjectProperties(characterMap, "entry", "sha256", "isVerified");
        Assert.IsTrue(characterMap["isVerified"]!.GetValue<bool>());
        AssertObjectProperties(manifest["metadata"]!.AsObject(), "entry", "sha256");

        AssertManifestHash(entries, manifest, "gxt");
        AssertManifestHash(entries, manifest, "txd");
        AssertManifestHash(entries, manifest, "characterMap");
        AssertManifestHash(entries, manifest, "metadata");
    }

    private static void AssertManifestHash(
        IReadOnlyDictionary<string, byte[]> entries,
        JsonObject manifest,
        string propertyName)
    {
        var item = manifest[propertyName]!.AsObject();
        var entryName = item["entry"]!.GetValue<string>();
        Assert.IsTrue(entries.ContainsKey(entryName));
        Assert.AreEqual(
            Convert.ToHexStringLower(SHA256.HashData(entries[entryName])),
            item["sha256"]!.GetValue<string>());
    }

    private static Dictionary<string, byte[]> ReadArchiveEntries(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            using var entryStream = entry.Open();
            using var buffer = new MemoryStream();
            entryStream.CopyTo(buffer);
            result.Add(entry.FullName, buffer.ToArray());
        }

        return result;
    }

    private static JsonObject ParseJson(byte[] data) =>
        JsonNode.Parse(data)?.AsObject()
        ?? throw new AssertFailedException("Fixture JSON must contain an object.");

    private static void AssertJsonEquivalent(JsonNode expected, JsonNode actual) =>
        Assert.IsTrue(
            JsonNode.DeepEquals(expected, actual),
            $"JSON differs.{Environment.NewLine}Expected: {expected}{Environment.NewLine}Actual: {actual}");

    private static void AssertObjectProperties(JsonObject value, params string[] expected) =>
        CollectionAssert.AreEquivalent(expected, value.Select(property => property.Key).ToArray());

    private static string FixturePath(string name, string expectedHash)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Contracts", name);
        Assert.IsTrue(File.Exists(path), $"Missing contract fixture: {path}");
        Assert.AreEqual(
            expectedHash,
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))),
            $"Contract fixture changed unexpectedly: {name}");
        return path;
    }
}
