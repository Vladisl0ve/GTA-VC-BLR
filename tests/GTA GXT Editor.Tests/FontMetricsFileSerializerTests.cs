using System.Text;
using System.Text.Json;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Tests;

[TestClass]
public sealed class FontMetricsFileSerializerTests
{
    [TestMethod]
    public void SerializeDeserialize_RoundTripsBothTablesOverridesAndHexCodes()
    {
        var profile = CreateValidProfile();
        profile.Overrides.Add(new FontMetricOverride
        {
            Context = FontRenderContext.Gameplay,
            Font = FontTextureKind.Font1,
            Code = 0x91,
            Advance = 13,
        });

        var data = FontMetricsFileSerializer.Serialize(profile);
        var json = Encoding.UTF8.GetString(data);
        var loaded = FontMetricsFileSerializer.Deserialize(data);

        Assert.Contains("\"code\": \"0x91\"", json, StringComparison.Ordinal);
        Assert.AreEqual(FontMetricsProfile.CurrentVersion, loaded.Version);
        Assert.HasCount(FontMetricsTable.MetricCount, loaded.Font2.Advances);
        Assert.HasCount(FontMetricsTable.MetricCount, loaded.Font1.Advances);
        CollectionAssert.AreEqual(profile.Font2.Advances, loaded.Font2.Advances);
        CollectionAssert.AreEqual(profile.Font1.Advances, loaded.Font1.Advances);
        Assert.HasCount(1, loaded.Overrides);
        Assert.AreEqual(FontRenderContext.Gameplay, loaded.Overrides[0].Context);
        Assert.AreEqual(FontTextureKind.Font1, loaded.Overrides[0].Font);
        Assert.AreEqual((byte)0x91, loaded.Overrides[0].Code);
        Assert.AreEqual((ushort)13, loaded.Overrides[0].Advance);
    }

    [TestMethod]
    public void SaveLoad_RoundTripsFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"font-metrics-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "metrics.json");
            var profile = CreateValidProfile();

            FontMetricsFileSerializer.Save(path, profile);
            var loaded = FontMetricsFileSerializer.Load(path);

            CollectionAssert.AreEqual(profile.Font2.Advances, loaded.Font2.Advances);
            CollectionAssert.AreEqual(profile.Font1.Advances, loaded.Font1.Advances);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Deserialize_RejectsUnknownFieldsAtEverySchemaLevel()
    {
        var valid = Encoding.UTF8.GetString(FontMetricsFileSerializer.Serialize(CreateValidProfile()));
        var unknownRoot = valid.Replace("\"version\": 1", "\"version\": 1, \"mystery\": true", StringComparison.Ordinal);
        var unknownTable = valid.Replace("\"advances\": [", "\"mystery\": true, \"advances\": [", StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => FontMetricsFileSerializer.Deserialize(Encoding.UTF8.GetBytes(unknownRoot)));
        Assert.Throws<JsonException>(() => FontMetricsFileSerializer.Deserialize(Encoding.UTF8.GetBytes(unknownTable)));
    }

    [TestMethod]
    public void Deserialize_RejectsInvalidVersion()
    {
        var json = Encoding.UTF8.GetString(FontMetricsFileSerializer.Serialize(CreateValidProfile()))
            .Replace("\"version\": 1", "\"version\": 2", StringComparison.Ordinal);

        var exception = Assert.Throws<InvalidDataException>(() =>
            FontMetricsFileSerializer.Deserialize(Encoding.UTF8.GetBytes(json)));

        Assert.Contains("version 2", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void Deserialize_RejectsMissingTable()
    {
        var json = """
            {
              "version": 1,
              "font2": { "advances": [] },
              "overrides": []
            }
            """;

        var exception = Assert.Throws<InvalidDataException>(() =>
            FontMetricsFileSerializer.Deserialize(Encoding.UTF8.GetBytes(json)));

        Assert.Contains("font2 and font1", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void Deserialize_RejectsWrongMetricCount()
    {
        var profile = CreateValidProfile();
        var json = Encoding.UTF8.GetString(FontMetricsFileSerializer.Serialize(profile));
        var firstTwoValues = string.Join(",", profile.Font2.Advances.Take(2));
        using var document = JsonDocument.Parse(json);
        var font1Json = document.RootElement.GetProperty("font1").GetRawText();
        var invalid = $$"""
            {
              "version": 1,
              "font2": { "advances": [{{firstTwoValues}}] },
              "font1": {{font1Json}},
              "overrides": []
            }
            """;

        var exception = Assert.Throws<InvalidDataException>(() =>
            FontMetricsFileSerializer.Deserialize(Encoding.UTF8.GetBytes(invalid)));

        Assert.Contains("exactly 210", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void Deserialize_RejectsDuplicateOverrides()
    {
        var profile = CreateValidProfile();
        var baseJson = Encoding.UTF8.GetString(FontMetricsFileSerializer.Serialize(profile));
        using var document = JsonDocument.Parse(baseJson);
        var font2Json = document.RootElement.GetProperty("font2").GetRawText();
        var font1Json = document.RootElement.GetProperty("font1").GetRawText();
        var invalid = $$"""
            {
              "version": 1,
              "font2": {{font2Json}},
              "font1": {{font1Json}},
              "overrides": [
                { "context": "SaveLoad", "font": "Font1", "code": "0x91", "advance": 15 },
                { "context": "SaveLoad", "font": "Font1", "code": "0x91", "advance": 15 }
              ]
            }
            """;

        var exception = Assert.Throws<InvalidDataException>(() =>
            FontMetricsFileSerializer.Deserialize(Encoding.UTF8.GetBytes(invalid)));

        Assert.Contains("duplicated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    [DataRow("0x1F")]
    [DataRow("0xF2")]
    public void Deserialize_RejectsCodesOutsideMetricRange(string code)
    {
        var profile = CreateValidProfile();
        var baseJson = Encoding.UTF8.GetString(FontMetricsFileSerializer.Serialize(profile));
        using var document = JsonDocument.Parse(baseJson);
        var font2Json = document.RootElement.GetProperty("font2").GetRawText();
        var font1Json = document.RootElement.GetProperty("font1").GetRawText();
        var invalid = $$"""
            {
              "version": 1,
              "font2": {{font2Json}},
              "font1": {{font1Json}},
              "overrides": [
                { "context": "Gameplay", "font": "Font1", "code": "{{code}}", "advance": 13 }
              ]
            }
            """;

        var exception = Assert.Throws<InvalidDataException>(() =>
            FontMetricsFileSerializer.Deserialize(Encoding.UTF8.GetBytes(invalid)));

        Assert.Contains("0x20-0xF1", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void Deserialize_RejectsUnknownEnumValues()
    {
        var profile = CreateValidProfile();
        var baseJson = Encoding.UTF8.GetString(FontMetricsFileSerializer.Serialize(profile));
        using var document = JsonDocument.Parse(baseJson);
        var font2Json = document.RootElement.GetProperty("font2").GetRawText();
        var font1Json = document.RootElement.GetProperty("font1").GetRawText();
        var invalid = $$"""
            {
              "version": 1,
              "font2": {{font2Json}},
              "font1": {{font1Json}},
              "overrides": [
                { "context": "Arcade", "font": "Font1", "code": "0x91", "advance": 13 }
              ]
            }
            """;

        Assert.Throws<JsonException>(() =>
            FontMetricsFileSerializer.Deserialize(Encoding.UTF8.GetBytes(invalid)));
    }

    [TestMethod]
    public void Deserialize_RejectsMalformedJson()
    {
        Assert.Throws<JsonException>(() =>
            FontMetricsFileSerializer.Deserialize("{"u8.ToArray()));
    }

    private static FontMetricsProfile CreateValidProfile() => new()
    {
        Font2 = new FontMetricsTable
        {
            Advances = Enumerable.Range(0, FontMetricsTable.MetricCount)
                .Select(value => (ushort)(value % 34))
                .ToArray(),
        },
        Font1 = new FontMetricsTable
        {
            Advances = Enumerable.Range(0, FontMetricsTable.MetricCount)
                .Select(value => (ushort)((value + 7) % 34))
                .ToArray(),
        },
    };
}
