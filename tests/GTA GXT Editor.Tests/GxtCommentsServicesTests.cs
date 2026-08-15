using System.Text;
using System.Text.Json;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Tests;

[TestClass]
public sealed class GxtCommentsServicesTests
{
    private string _testDirectory = null!;

    [TestInitialize]
    public void Initialize()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"gta-gxt-comments-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
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
    public void Export_ViceCity_WritesCanonicalDocumentWithTextAndNullableComments()
    {
        var project = CreateViceCityProject();
        project.Metadata.Entries.Add(new ProjectEntryMetadata
        {
            Table = "MAIN",
            Key = "HELLO",
            Comment = "Праверыць зварот.",
        });
        var path = Path.Combine(_testDirectory, "comments.json");

        GxtCommentsExporter.Export(path, project);

        var json = File.ReadAllText(path);
        StringAssert.Contains(json, "Праверыць зварот.");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.AreEqual("GXT_COMMENTS", root.GetProperty("format").GetString());
        Assert.AreEqual(1, root.GetProperty("version").GetInt32());
        Assert.AreEqual("GTA Vice City", root.GetProperty("game").GetString());
        Assert.HasCount(3, root.GetProperty("entries").EnumerateArray());

        var hello = root.GetProperty("entries")[0];
        Assert.AreEqual("HELLO", hello.GetProperty("key").GetString());
        Assert.AreEqual("MAIN", hello.GetProperty("table").GetString());
        Assert.AreEqual("Hello", hello.GetProperty("text").GetString());
        Assert.AreEqual("Праверыць зварот.", hello.GetProperty("comment").GetString());

        var car = root.GetProperty("entries")[2];
        Assert.AreEqual("CARS", car.GetProperty("table").GetString());
        Assert.AreEqual(JsonValueKind.Null, car.GetProperty("comment").ValueKind);
    }

    [TestMethod]
    public void Export_GtaIII_OmitsTableAndCanOmitContextText()
    {
        var project = CreateGtaIIIProject();

        var data = GxtCommentsExporter.Serialize(project, includeText: false);

        using var document = JsonDocument.Parse(data);
        var entry = document.RootElement.GetProperty("entries")[0];
        Assert.IsFalse(entry.TryGetProperty("table", out _));
        Assert.IsFalse(entry.TryGetProperty("text", out _));
        Assert.AreEqual(JsonValueKind.Null, entry.GetProperty("comment").ValueKind);
    }

    [TestMethod]
    public void Import_MergesListedEntriesAndReportsMissingAndTextMismatch()
    {
        var project = CreateViceCityProject();
        var occurrence = new ProjectEntryOccurrence
        {
            BlockId = "mission.party",
            Order = 10,
            Context = "Office",
        };
        project.Metadata.Blocks.Add(new ProjectMetadataBlock
        {
            Id = "mission.party",
            Type = "mission",
            Name = "The Party",
            Order = 100,
        });
        project.Metadata.Entries.AddRange(
        [
            new ProjectEntryMetadata
            {
                Table = "MAIN",
                Key = "HELLO",
                Comment = "Old comment",
                Occurrences = [occurrence],
            },
            new ProjectEntryMetadata
            {
                Table = "MAIN",
                Key = "KEEP",
                Comment = "Must stay",
            },
        ]);
        var path = WriteJson(
            "merge.json",
            """
            {
              "format": "GXT_COMMENTS",
              "version": 1,
              "game": "GTA Vice City",
              "entries": [
                {
                  "key": "HELLO",
                  "table": "MAIN",
                  "text": "Outdated text",
                  "comment": "New comment"
                },
                {
                  "key": "CAR",
                  "table": "CARS",
                  "text": "Cheetah",
                  "comment": "Vehicle name"
                },
                {
                  "key": "MISSING",
                  "table": "MAIN",
                  "comment": "Not imported"
                }
              ]
            }
            """);

        var result = GxtCommentsImporter.Import(path, project);

        Assert.AreEqual(3, result.ListedEntryCount);
        Assert.AreEqual(2, result.UpdatedEntryCount);
        Assert.AreEqual(0, result.ClearedEntryCount);
        Assert.AreEqual(0, result.UnchangedEntryCount);
        Assert.AreEqual(2, result.ChangedEntryCount);
        Assert.HasCount(1, result.MissingEntries);
        Assert.AreEqual(new GxtEntryIdentity("MISSING", "MAIN"), result.MissingEntries[0]);
        Assert.HasCount(1, result.TextMismatches);
        Assert.AreEqual(new GxtEntryIdentity("HELLO", "MAIN"), result.TextMismatches[0].Identity);
        Assert.AreEqual("Outdated text", result.TextMismatches[0].ImportedText);
        Assert.AreEqual("Hello", result.TextMismatches[0].CurrentText);
        Assert.IsTrue(project.IsDirty);

        var hello = FindMetadata(project, "MAIN", "HELLO");
        Assert.AreEqual("New comment", hello.Comment);
        Assert.AreSame(occurrence, hello.Occurrences.Single());
        Assert.AreEqual("Must stay", FindMetadata(project, "MAIN", "KEEP").Comment);
        Assert.AreEqual("Vehicle name", FindMetadata(project, "CARS", "CAR").Comment);
        Assert.IsFalse(project.Metadata.Entries.Any(entry => entry.Key == "MISSING"));
    }

    [TestMethod]
    public void Import_NullAndWhitespaceClearCommentsAndSameValueIsUnchanged()
    {
        var project = CreateViceCityProject();
        project.Metadata.Entries.AddRange(
        [
            new ProjectEntryMetadata { Table = "MAIN", Key = "HELLO", Comment = "Remove" },
            new ProjectEntryMetadata { Table = "MAIN", Key = "KEEP", Comment = "Same" },
            new ProjectEntryMetadata { Table = "CARS", Key = "CAR", Comment = "Remove too" },
        ]);
        var path = WriteJson(
            "clear.json",
            """
            {
              "format": "GXT_COMMENTS",
              "version": 1,
              "game": "GTA Vice City",
              "entries": [
                { "key": "HELLO", "table": "MAIN", "comment": null },
                { "key": "KEEP", "table": "MAIN", "comment": "Same" },
                { "key": "CAR", "table": "CARS", "comment": "   " }
              ]
            }
            """);

        var result = GxtCommentsImporter.Import(path, project);

        Assert.AreEqual(0, result.UpdatedEntryCount);
        Assert.AreEqual(2, result.ClearedEntryCount);
        Assert.AreEqual(1, result.UnchangedEntryCount);
        Assert.IsNull(FindMetadata(project, "MAIN", "HELLO").Comment);
        Assert.AreEqual("Same", FindMetadata(project, "MAIN", "KEEP").Comment);
        Assert.IsNull(FindMetadata(project, "CARS", "CAR").Comment);
    }

    [TestMethod]
    public void Import_ViceCity_UsesTableAndKeyAsIdentity()
    {
        var project = CreateViceCityProject();
        project.GxtManager.AddGXTEntry("HELLO", "Bonjour", "FRENCH");
        var path = WriteJson(
            "tables.json",
            """
            {
              "format": "GXT_COMMENTS",
              "version": 1,
              "game": "GTA Vice City",
              "entries": [
                { "key": "HELLO", "table": "MAIN", "comment": "Main" },
                { "key": "HELLO", "table": "FRENCH", "comment": "French" }
              ]
            }
            """);

        var result = GxtCommentsImporter.Import(path, project);

        Assert.AreEqual(2, result.UpdatedEntryCount);
        Assert.AreEqual("Main", FindMetadata(project, "MAIN", "HELLO").Comment);
        Assert.AreEqual("French", FindMetadata(project, "FRENCH", "HELLO").Comment);
    }

    [TestMethod]
    public void Import_GtaIII_DetectsDuplicateKeysEvenWhenTablesDiffer()
    {
        var project = CreateGtaIIIProject();
        var path = WriteJson(
            "duplicate-gta3.json",
            """
            {
              "format": "GXT_COMMENTS",
              "version": 1,
              "game": "GTA III",
              "entries": [
                { "key": "HELLO", "table": "MAIN", "comment": "One" },
                { "key": "HELLO", "table": "OTHER", "comment": "Two" }
              ]
            }
            """);

        Assert.Throws<InvalidDataException>(() => GxtCommentsImporter.Import(path, project));
        Assert.IsEmpty(project.Metadata.Entries);
        Assert.IsFalse(project.IsDirty);
    }

    [TestMethod]
    public void Import_DuplicateViceCityIdentity_IsRejectedBeforeAnyChanges()
    {
        var project = CreateViceCityProject();
        project.Metadata.Entries.Add(new ProjectEntryMetadata
        {
            Table = "MAIN",
            Key = "HELLO",
            Comment = "Original",
        });
        var path = WriteJson(
            "duplicate.json",
            """
            {
              "format": "GXT_COMMENTS",
              "version": 1,
              "game": "GTA Vice City",
              "entries": [
                { "key": "HELLO", "table": "MAIN", "comment": "First" },
                { "key": "HELLO", "table": "MAIN", "comment": "Second" }
              ]
            }
            """);

        Assert.Throws<InvalidDataException>(() => GxtCommentsImporter.Import(path, project));
        Assert.AreEqual("Original", FindMetadata(project, "MAIN", "HELLO").Comment);
        Assert.IsFalse(project.IsDirty);
    }

    [TestMethod]
    public void Import_MissingRequiredComment_IsRejected()
    {
        var project = CreateViceCityProject();
        var path = WriteJson(
            "missing-comment.json",
            """
            {
              "format": "GXT_COMMENTS",
              "version": 1,
              "game": "GTA Vice City",
              "entries": [
                { "key": "HELLO", "table": "MAIN" }
              ]
            }
            """);

        Assert.Throws<InvalidDataException>(() => GxtCommentsImporter.Import(path, project));
    }

    [TestMethod]
    public void Import_DifferentGame_IsRejected()
    {
        var project = CreateViceCityProject();
        var path = WriteJson(
            "wrong-game.json",
            """
            {
              "format": "GXT_COMMENTS",
              "version": 1,
              "game": "GTA III",
              "entries": []
            }
            """);

        Assert.Throws<InvalidDataException>(() => GxtCommentsImporter.Import(path, project));
    }

    private static EditorProject CreateViceCityProject()
    {
        var manager = GxtManagerFactory.Create(
            GXTType.GtaViceCity,
            sourceName: "american.gxt",
            sourceTexts: ["Hello", "Keep", "Cheetah"],
            language: GxtLanguage.English);
        manager.AddGXTEntry("HELLO", "Hello", "MAIN");
        manager.AddGXTEntry("KEEP", "Keep", "MAIN");
        manager.AddGXTEntry("CAR", "Cheetah", "CARS");
        return new EditorProject
        {
            GxtSourceName = "american.gxt",
            GameType = GXTType.GtaViceCity,
            GxtManager = manager,
        };
    }

    private static EditorProject CreateGtaIIIProject()
    {
        var manager = GxtManagerFactory.Create(
            GXTType.GtaIII,
            sourceName: "american.gxt",
            sourceTexts: ["Hello"],
            language: GxtLanguage.English);
        manager.AddGXTEntry("HELLO", "Hello");
        return new EditorProject
        {
            GxtSourceName = "american.gxt",
            GameType = GXTType.GtaIII,
            GxtManager = manager,
        };
    }

    private string WriteJson(string fileName, string contents)
    {
        var path = Path.Combine(_testDirectory, fileName);
        File.WriteAllText(
            path,
            contents,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static ProjectEntryMetadata FindMetadata(
        EditorProject project,
        string table,
        string key) =>
        project.Metadata.Entries.Single(entry =>
            entry.Table == table && entry.Key == key);
}
