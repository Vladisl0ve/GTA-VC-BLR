using System.Text;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Tests;

[TestClass]
public sealed class ProjectMetadataJsonSerializerTests
{
    private string _testDirectory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"gta-gxt-metadata-tests-{Guid.NewGuid():N}");
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
    public void SaveAndLoad_RoundTripsFullMetadataDocument()
    {
        var path = Path.Combine(_testDirectory, "metadata.json");
        var metadata = CreateMetadata();

        ProjectMetadataJsonSerializer.Save(path, metadata, GXTType.GtaViceCity);
        var loaded = ProjectMetadataJsonSerializer.Load(path, GXTType.GtaViceCity);

        Assert.AreEqual("GXT_ENTRY_METADATA", loaded.Format);
        Assert.AreEqual(ProjectMetadata.CurrentVersion, loaded.Version);
        Assert.HasCount(1, loaded.Blocks);
        Assert.AreEqual("mission.party", loaded.Blocks[0].Id);
        Assert.AreEqual("Opening cutscene", loaded.Blocks[0].Description);
        Assert.AreEqual(100, loaded.Blocks[0].Order);
        Assert.HasCount(1, loaded.Entries);
        Assert.AreEqual("MAIN", loaded.Entries[0].Table);
        Assert.AreEqual("HELLO", loaded.Entries[0].Key);
        Assert.AreEqual("Праверыць інтанацыю.", loaded.Entries[0].Comment);
        Assert.HasCount(1, loaded.Entries[0].Occurrences);
        Assert.AreEqual("mission.party", loaded.Entries[0].Occurrences[0].BlockId);
        Assert.AreEqual("Ken's office", loaded.Entries[0].Occurrences[0].Context);

        var json = File.ReadAllText(path);
        StringAssert.Contains(json, "Праверыць інтанацыю.");
        Assert.IsFalse(json.Contains("\\u041f", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Deserialize_UnknownProperty_IsRejected()
    {
        var data = Encoding.UTF8.GetBytes(
            """
            {
              "format": "GXT_ENTRY_METADATA",
              "version": 1,
              "blocks": [],
              "entries": [],
              "unexpected": true
            }
            """);

        Assert.Throws<InvalidDataException>(() =>
            ProjectMetadataJsonSerializer.Deserialize(data));
    }

    [TestMethod]
    public void Deserialize_ViceCityDuplicateTableAndKey_IsRejected()
    {
        var data = Encoding.UTF8.GetBytes(
            """
            {
              "format": "GXT_ENTRY_METADATA",
              "version": 1,
              "blocks": [],
              "entries": [
                { "table": "MAIN", "key": "HELLO", "occurrences": [] },
                { "table": "MAIN", "key": "HELLO", "occurrences": [] }
              ]
            }
            """);

        Assert.Throws<InvalidDataException>(() =>
            ProjectMetadataJsonSerializer.Deserialize(data, GXTType.GtaViceCity));
    }

    [TestMethod]
    public void Deserialize_GtaIIIUsesKeyOnlyIdentity()
    {
        var data = Encoding.UTF8.GetBytes(
            """
            {
              "format": "GXT_ENTRY_METADATA",
              "version": 1,
              "blocks": [],
              "entries": [
                { "key": "HELLO", "occurrences": [] },
                { "key": "HELLO", "occurrences": [] }
              ]
            }
            """);

        Assert.Throws<InvalidDataException>(() =>
            ProjectMetadataJsonSerializer.Deserialize(data, GXTType.GtaIII));
    }

    [TestMethod]
    public void Deserialize_UnknownOccurrenceBlock_IsRejected()
    {
        var data = Encoding.UTF8.GetBytes(
            """
            {
              "format": "GXT_ENTRY_METADATA",
              "version": 1,
              "blocks": [],
              "entries": [
                {
                  "table": "MAIN",
                  "key": "HELLO",
                  "occurrences": [
                    { "blockId": "missing", "order": 1 }
                  ]
                }
              ]
            }
            """);

        Assert.Throws<InvalidDataException>(() =>
            ProjectMetadataJsonSerializer.Deserialize(data, GXTType.GtaViceCity));
    }

    [TestMethod]
    public void Serialize_GtaIIIEntryWithTable_IsRejected()
    {
        var metadata = new ProjectMetadata
        {
            Entries =
            [
                new ProjectEntryMetadata
                {
                    Table = "MAIN",
                    Key = "HELLO",
                },
            ],
        };

        Assert.Throws<InvalidDataException>(() =>
            ProjectMetadataJsonSerializer.Serialize(metadata, GXTType.GtaIII));
    }

    private static ProjectMetadata CreateMetadata() => new()
    {
        Blocks =
        [
            new ProjectMetadataBlock
            {
                Id = "mission.party",
                Type = "mission",
                Name = "The Party",
                Description = "Opening cutscene",
                Order = 100,
            },
        ],
        Entries =
        [
            new ProjectEntryMetadata
            {
                Table = "MAIN",
                Key = "HELLO",
                Comment = "Праверыць інтанацыю.",
                Occurrences =
                [
                    new ProjectEntryOccurrence
                    {
                        BlockId = "mission.party",
                        Order = 10,
                        Context = "Ken's office",
                    },
                ],
            },
        ],
    };
}
