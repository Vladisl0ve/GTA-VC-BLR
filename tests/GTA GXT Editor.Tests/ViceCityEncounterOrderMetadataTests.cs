using System.Text.Json;
using System.Text.RegularExpressions;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Tests;

[TestClass]
public sealed class ViceCityEncounterOrderMetadataTests
{
    private const int ExpectedEntryCount = 4707;
    private static readonly Regex CyrillicPattern = new(
        "[\u0400-\u04FF]",
        RegexOptions.CultureInvariant);

    [TestMethod]
    public void CanonicalDataset_LoadsAndExactlyCoversReferenceInventory()
    {
        var metadata = LoadMetadata();
        var referenceIdentities = LoadReferenceIdentities();
        var blockIds = metadata.Blocks
            .Select(block => block.Id)
            .ToHashSet(StringComparer.Ordinal);
        var metadataIdentities = new HashSet<EntryIdentity>();

        Assert.HasCount(ExpectedEntryCount, metadata.Entries);
        Assert.HasCount(metadata.Blocks.Count, blockIds);
        Assert.IsTrue(metadata.Blocks.All(block => block.Order > 0));
        Assert.IsTrue(metadata.Blocks.All(block => !CyrillicPattern.IsMatch(block.Name)));
        Assert.IsTrue(metadata.Blocks.All(block =>
            block.Description is null || !CyrillicPattern.IsMatch(block.Description)));

        foreach (var entry in metadata.Entries)
        {
            Assert.IsNotNull(entry.Table);
            Assert.IsTrue(metadataIdentities.Add(new EntryIdentity(entry.Table, entry.Key)));
            Assert.IsGreaterThan(0, entry.Occurrences.Count, $"{entry.Table}/{entry.Key}");

            foreach (var occurrence in entry.Occurrences)
            {
                Assert.IsTrue(blockIds.Contains(occurrence.BlockId));
                Assert.IsGreaterThan(0, occurrence.Order);
                Assert.IsTrue(occurrence.Context is null || occurrence.Context.Length <= 120);
                Assert.IsTrue(occurrence.Context is null ||
                    !CyrillicPattern.IsMatch(occurrence.Context));
            }
        }

        Assert.HasCount(ExpectedEntryCount, referenceIdentities);
        Assert.IsTrue(metadataIdentities.SetEquals(referenceIdentities));
    }

    [TestMethod]
    public void CanonicalDataset_ContainsExpectedSemanticMappings()
    {
        var metadata = LoadMetadata();

        AssertOccurrence(metadata, "MAIN", "ITBEG", "story.in-the-beginning");
        AssertOccurrence(metadata, "INTRO", "INT1_A", "story.in-the-beginning");
        AssertOccurrence(metadata, "MAIN", "LAW_1", "mission.the-party");
        AssertOccurrence(metadata, "LAWYER1", "LAW1_A", "mission.the-party");
        AssertOccurrence(metadata, "BANKJ1", "BJM1_A", "asset.no-escape");
        AssertOccurrence(metadata, "TAXICUT", "TAXC_A", "asset.kaufman-cabs-purchase");
        AssertOccurrence(metadata, "MAIN", "MOB_01A", "phone.mob-01");
        AssertOccurrence(metadata, "MAIN", "HELP3", "interface.tutorial");
        AssertOccurrence(metadata, "MAIN", "IND_ZON", "world.areas");
        AssertOccurrence(metadata, "MAIN", "CRED001", "credits");
        AssertOccurrence(metadata, "MAIN", "CRD001A", "credits");
        AssertOccurrence(metadata, "MAIN", "GREET", "misc.unclassified");

        Assert.AreEqual("The Party", FindBlock(metadata, "mission.the-party").Name);
        Assert.AreEqual("G-Spotlight", FindBlock(metadata, "asset.g-spotlight").Name);
        Assert.AreEqual(
            "Keep Your Friends Close...",
            FindBlock(metadata, "mission.keep-your-friends-close").Name);

        var introFirst = FindOccurrence(metadata, "INTRO", "INT1_A", "story.in-the-beginning");
        var introSecond = FindOccurrence(metadata, "INTRO", "INT1_B", "story.in-the-beginning");
        Assert.IsTrue(introFirst.Order < introSecond.Order);

        var phoneOccurrence = FindOccurrence(metadata, "MAIN", "MOB_01A", "phone.mob-01");
        StringAssert.Contains(phoneOccurrence.Context!, "trigger varies");
    }

    [TestMethod]
    public void CanonicalDataset_UsesSharedOrdersForParallelBranches()
    {
        var metadata = LoadMetadata();
        var firstAssetStage = new[]
        {
            "asset.no-escape",
            "asset.v-i-p",
            "asset.distribution",
            "asset.recruitment-drive",
            "asset.spilling-the-beans",
        }.Select(id => FindBlock(metadata, id).Order).Distinct().ToArray();
        var firstOptionalStage = new[]
        {
            "mission.love-juice",
            "mission.alloy-wheels-of-steel",
            "mission.stunt-boat-challenge",
            "mission.juju-scramble",
            "mission.road-kill",
        }.Select(id => FindBlock(metadata, id).Order).Distinct().ToArray();

        Assert.HasCount(1, firstAssetStage);
        Assert.HasCount(1, firstOptionalStage);
        Assert.AreEqual(50000, firstAssetStage[0]);
        Assert.AreEqual(30000, firstOptionalStage[0]);
    }

    private static ProjectMetadata LoadMetadata()
    {
        var path = GetAssetPath("encounter-order.metadata.json");
        Assert.IsTrue(File.Exists(path), path);
        return ProjectMetadataJsonSerializer.Load(path, GXTType.GtaViceCity);
    }

    private static HashSet<EntryIdentity> LoadReferenceIdentities()
    {
        var path = GetAssetPath("vice-city-pc.reference-entries.json");
        Assert.IsTrue(File.Exists(path), path);
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;

        Assert.AreEqual("GXT_REFERENCE_ENTRIES", root.GetProperty("format").GetString());
        Assert.AreEqual(1, root.GetProperty("version").GetInt32());
        Assert.AreEqual("GTA Vice City", root.GetProperty("game").GetString());
        Assert.AreEqual("PC", root.GetProperty("platform").GetString());
        Assert.AreEqual(ExpectedEntryCount, root.GetProperty("entryCount").GetInt32());
        Assert.AreEqual(64, root.GetProperty("sourceSha256").GetString()!.Length);

        var result = new HashSet<EntryIdentity>();
        foreach (var entry in root.GetProperty("entries").EnumerateArray())
        {
            var identity = new EntryIdentity(
                entry.GetProperty("table").GetString()!,
                entry.GetProperty("key").GetString()!);
            Assert.IsTrue(result.Add(identity), $"{identity.Table}/{identity.Key}");
        }

        return result;
    }

    private static void AssertOccurrence(
        ProjectMetadata metadata,
        string table,
        string key,
        string blockId) =>
        Assert.IsNotNull(FindOccurrence(metadata, table, key, blockId));

    private static ProjectEntryOccurrence FindOccurrence(
        ProjectMetadata metadata,
        string table,
        string key,
        string blockId) =>
        metadata.Entries.Single(entry => entry.Table == table && entry.Key == key)
            .Occurrences.Single(occurrence => occurrence.BlockId == blockId);

    private static ProjectMetadataBlock FindBlock(ProjectMetadata metadata, string id) =>
        metadata.Blocks.Single(block => block.Id == id);

    private static string GetAssetPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "ViceCity", fileName);

    private readonly record struct EntryIdentity(string Table, string Key);
}
