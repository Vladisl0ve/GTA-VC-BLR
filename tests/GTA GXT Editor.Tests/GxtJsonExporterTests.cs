using System.Text.Json;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Tests;

[TestClass]
public sealed class GxtJsonExporterTests
{
    private string _testDirectory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"gta-gxt-json-tests-{Guid.NewGuid():N}");
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
    public void Export_GtaIII_WritesReadableUtf8JsonWithoutTable()
    {
        var targetPath = Path.Combine(_testDirectory, "gta3.json");
        GxtEntryRow[] entries =
        [
            new("HELLO", "Ніхто не казав мені цього з тих пір, як я з в'язниці вийшов.", string.Empty, null),
            new("QUOTE", "Строка \"два\"", string.Empty, null),
        ];

        GxtJsonExporter.Export(targetPath, @"C:\games\gta3.gxt", "GTA III", entries);

        var json = File.ReadAllText(targetPath);
        StringAssert.Contains(json, "в'язниці");
        Assert.IsFalse(json.Contains("\\u0027", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(json, "\\\"два\\\"");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.AreEqual("GTA III", root.GetProperty("game").GetString());
        Assert.AreEqual("gta3.gxt", root.GetProperty("source").GetString());
        Assert.HasCount(2, root.GetProperty("entries").EnumerateArray());
        Assert.IsFalse(root.GetProperty("entries")[0].TryGetProperty("table", out _));
    }

    [TestMethod]
    public void Export_ViceCity_IncludesClearTableName()
    {
        var targetPath = Path.Combine(_testDirectory, "vice-city.json");
        GxtEntryRow[] entries =
        [
            new("HELLO", "World", "MAIN", "MAIN\0\0\0\0"),
        ];

        GxtJsonExporter.Export(targetPath, "vice-city.gxt", "GTA Vice City", entries);

        using var document = JsonDocument.Parse(File.ReadAllText(targetPath));
        var entry = document.RootElement.GetProperty("entries")[0];

        Assert.AreEqual("HELLO", entry.GetProperty("key").GetString());
        Assert.AreEqual("World", entry.GetProperty("text").GetString());
        Assert.AreEqual("MAIN", entry.GetProperty("table").GetString());
    }

    [TestMethod]
    public void Export_Belarusian_WritesLanguageAndReadableUtf8Text()
    {
        var targetPath = Path.Combine(_testDirectory, "belarusian.json");
        GxtEntryRow[] entries =
        [
            new("HELLO", "Прывітанне! Мой аўтамабіль тут.", string.Empty, null),
        ];

        GxtJsonExporter.Export(
            targetPath,
            "localized.gxt",
            "GTA III",
            entries,
            GxtLanguage.Belarusian);

        var json = File.ReadAllText(targetPath);
        StringAssert.Contains(json, "Прывітанне");
        StringAssert.Contains(json, "аўтамабіль");
        using var document = JsonDocument.Parse(json);
        Assert.AreEqual("be", document.RootElement.GetProperty("language").GetString());
        Assert.AreEqual(
            "Прывітанне! Мой аўтамабіль тут.",
            document.RootElement.GetProperty("entries")[0].GetProperty("text").GetString());
    }
}
