using System.Text;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;
using GTA_GXT_Editor.Utils;

namespace GTA_GXT_Editor.Tests;

[TestClass]
public sealed class GxtJsonImporterTests
{
    private string _testDirectory = null!;
    private string _dictionaryPath = null!;
    private GxtManagerFactory _factory = null!;

    [TestInitialize]
    public void Initialize()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _testDirectory = Path.Combine(Path.GetTempPath(), $"gta-gxt-json-import-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
        _dictionaryPath = Path.Combine(_testDirectory, "characters.txt");
        File.WriteAllText(_dictionaryPath, string.Empty, Encoding.GetEncoding(1251));
        _factory = new GxtManagerFactory();
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
    public void Import_GtaIII_CreatesReadableGxt()
    {
        var jsonPath = WriteJson(
            "gta3.json",
            """
            {
              "game": "GTA III",
              "source": "american.gxt",
              "entries": [
                { "key": "HELLO", "text": "Hello" },
                { "key": "EMPTY", "text": "" }
              ]
            }
            """);
        var gxtPath = Path.Combine(_testDirectory, "gta3.gxt");

        var result = GxtJsonImporter.Import(jsonPath, gxtPath, _dictionaryPath);

        Assert.AreEqual(GXTType.GtaIII, result.Type);
        Assert.AreEqual(2, result.EntryCount);
        Assert.AreEqual(GXTType.GtaIII, _factory.DetectType(gxtPath));
        var manager = _factory.Open(gxtPath, _dictionaryPath);
        Assert.AreEqual("Hello", GetText(manager, "HELLO"));
        Assert.AreEqual(string.Empty, GetText(manager, "EMPTY"));
    }

    [TestMethod]
    public void ExportThenImport_RoundTripsCompatibleJsonSchema()
    {
        var jsonPath = Path.Combine(_testDirectory, "round-trip.json");
        var gxtPath = Path.Combine(_testDirectory, "round-trip.gxt");
        GxtEntryRow[] entries =
        [
            new("HELLO", "Round trip", string.Empty, null),
        ];
        GxtJsonExporter.Export(jsonPath, "source.gxt", "GTA III", entries);

        GxtJsonImporter.Import(jsonPath, gxtPath, _dictionaryPath);

        var manager = _factory.Open(gxtPath, _dictionaryPath);
        Assert.AreEqual("Round trip", GetText(manager, "HELLO"));
    }

    [TestMethod]
    public void Import_ViceCity_PreservesTablesAndDefaultsToMain()
    {
        var jsonPath = WriteJson(
            "vice-city.json",
            """
            {
              "game": "GTA Vice City",
              "source": "american.gxt",
              "entries": [
                { "key": "HELLO", "text": "Hello" },
                { "key": "CAR", "text": "Cheetah", "table": "CARS" }
              ]
            }
            """);
        var gxtPath = Path.Combine(_testDirectory, "vice-city.gxt");

        GxtJsonImporter.Import(jsonPath, gxtPath, _dictionaryPath);

        Assert.AreEqual(GXTType.GtaViceCity, _factory.DetectType(gxtPath));
        var manager = _factory.Open(gxtPath, _dictionaryPath);
        var entries = manager.GXTEntries.Cast<GTAVC.GXTEntry>().ToList();
        Assert.HasCount(2, entries);
        Assert.IsTrue(entries.Any(entry =>
            entry.DatName.GetClearName() == "HELLO" &&
            entry.TableName.GetClearName() == "MAIN"));
        Assert.IsTrue(entries.Any(entry =>
            entry.DatName.GetClearName() == "CAR" &&
            entry.TableName.GetClearName() == "CARS"));
    }

    [TestMethod]
    public void Import_ViceCity_DetectsBuiltInUkrainianEncoding()
    {
        var jsonPath = WriteJson(
            "ukrainian.json",
            """
            {
              "game": "GTA Vice City",
              "source": "ukrainian.gxt",
              "entries": [
                { "key": "HELLO", "text": "Увійди всередину" }
              ]
            }
            """);
        var gxtPath = Path.Combine(_testDirectory, "ukrainian.gxt");

        GxtJsonImporter.Import(jsonPath, gxtPath);

        var manager = _factory.Open(gxtPath);
        Assert.AreEqual("Увійди всередину", GetText(manager, "HELLO"));
    }

    [TestMethod]
    public void Import_GtaIII_UsesBuiltInBelarusianEncoding()
    {
        var jsonPath = WriteJson(
            "belarusian-gta3.json",
            """
            {
              "game": "GTA III",
              "language": "be",
              "entries": [
                { "key": "HELLO", "text": "Прывітанне, свет!" }
              ]
            }
            """);
        var gxtPath = Path.Combine(_testDirectory, "belarusian-gta3.gxt");

        var result = GxtJsonImporter.Import(jsonPath, gxtPath);

        var manager = _factory.Open(gxtPath, language: result.Language);
        Assert.AreEqual(GxtLanguage.Belarusian, manager.Language);
        Assert.AreEqual("Прывітанне, свет!", GetText(manager, "HELLO"));
    }

    [TestMethod]
    public void Import_ViceCity_UsesExplicitBelarusianEncoding()
    {
        var jsonPath = WriteJson(
            "belarusian-vice-city.json",
            """
            {
              "game": "GTA Vice City",
              "source": "localized.gxt",
              "language": "be",
              "entries": [
                { "key": "HELLO", "text": "Прывітанне, свет!" }
              ]
            }
            """);
        var gxtPath = Path.Combine(_testDirectory, "localized.gxt");

        var result = GxtJsonImporter.Import(jsonPath, gxtPath);

        var manager = _factory.Open(gxtPath, language: result.Language);
        Assert.AreEqual(GxtLanguage.Belarusian, manager.Language);
        Assert.AreEqual("Прывітанне, свет!", GetText(manager, "HELLO"));
    }

    [TestMethod]
    public void Import_ViceCity_UsesCanonicalJsonCharacterMap()
    {
        var jsonPath = WriteJson(
            "belarusian-canonical-map.json",
            """
            {
              "game": "GTA Vice City",
              "language": "be",
              "entries": [
                { "key": "LETTERS", "text": "ЁІЎёіў" }
              ]
            }
            """);
        var gxtPath = Path.Combine(_testDirectory, "canonical-map.gxt");
        var mapPath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "ViceCity",
            "belarusian.gxtmap.json");

        var result = GxtJsonImporter.Import(jsonPath, gxtPath, mapPath);
        var manager = _factory.Open(gxtPath, mapPath, result.Language);
        var value = manager.GXTEntries.Single().Value;

        CollectionAssert.AreEqual(
            new byte[] { 0x96, 0x49, 0x86, 0xAF, 0x69, 0x9D },
            value.Where((_, index) => index % 2 == 0).Take(6).ToArray());
        Assert.AreEqual("ЁІЎёіў", GetText(manager, "LETTERS"));
    }

    [TestMethod]
    public void Import_ViceCity_ReplacesEmDashWithAsciiHyphen()
    {
        var jsonPath = WriteJson(
            "belarusian-em-dash.json",
            """
            {
              "game": "GTA Vice City",
              "language": "be",
              "entries": [
                { "key": "HELLO", "text": "Слова — словы" }
              ]
            }
            """);
        var gxtPath = Path.Combine(_testDirectory, "belarusian-em-dash.gxt");

        var result = GxtJsonImporter.Import(jsonPath, gxtPath);

        var manager = _factory.Open(gxtPath, language: result.Language);
        Assert.AreEqual("Слова - словы", GetText(manager, "HELLO"));
    }

    [TestMethod]
    public void Import_DuplicateKey_DoesNotOverwriteTarget()
    {
        var jsonPath = WriteJson(
            "duplicate.json",
            """
            {
              "game": "GTA III",
              "entries": [
                { "key": "HELLO", "text": "One" },
                { "key": "HELLO", "text": "Two" }
              ]
            }
            """);
        var gxtPath = Path.Combine(_testDirectory, "existing.gxt");
        File.WriteAllText(gxtPath, "keep me");

        var exception = Assert.Throws<InvalidDataException>(() =>
            GxtJsonImporter.Import(jsonPath, gxtPath, _dictionaryPath));

        StringAssert.Contains(exception.Message, "дублирует ключ");
        Assert.AreEqual("keep me", File.ReadAllText(gxtPath));
    }

    [TestMethod]
    public void Import_InvalidJsonCharacterMap_DoesNotOverwriteTarget()
    {
        var jsonPath = WriteJson(
            "invalid-map-source.json",
            """
            {
              "game": "GTA Vice City",
              "entries": [
                { "key": "HELLO", "text": "А" }
              ]
            }
            """);
        var mapPath = WriteJson(
            "invalid.gxtmap.json",
            """
            {
              "А": "0x80",
              "Б": "0x80"
            }
            """);
        var gxtPath = Path.Combine(_testDirectory, "existing-invalid-map.gxt");
        File.WriteAllText(gxtPath, "keep me");

        Assert.Throws<InvalidDataException>(() =>
            GxtJsonImporter.Import(jsonPath, gxtPath, mapPath));

        Assert.AreEqual("keep me", File.ReadAllText(gxtPath));
    }

    [TestMethod]
    public void Import_CharacterMissingFromJsonMap_DoesNotOverwriteTarget()
    {
        var jsonPath = WriteJson(
            "missing-character-source.json",
            """
            {
              "game": "GTA Vice City",
              "language": "be",
              "entries": [
                { "key": "HELLO", "text": "Ў" }
              ]
            }
            """);
        var mapPath = WriteJson("incomplete.gxtmap.json", "{\"А\":\"0x80\"}");
        var gxtPath = Path.Combine(_testDirectory, "existing-missing-character.gxt");
        File.WriteAllText(gxtPath, "keep me");

        Assert.Throws<InvalidDataException>(() =>
            GxtJsonImporter.Import(jsonPath, gxtPath, mapPath));

        Assert.AreEqual("keep me", File.ReadAllText(gxtPath));
    }

    private string WriteJson(string fileName, string contents)
    {
        var path = Path.Combine(_testDirectory, fileName);
        File.WriteAllText(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static string GetText(Contracts.CommonGXTManager manager, string key)
    {
        var entry = manager.GXTEntries.Single(entry => entry.DatName.GetClearName() == key);
        return manager.ConvertBytesToText(entry.Value);
    }
}
