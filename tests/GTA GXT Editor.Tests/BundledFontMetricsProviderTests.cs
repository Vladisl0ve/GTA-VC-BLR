using System.Buffers.Binary;
using System.Security.Cryptography;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Tests;

[TestClass]
public sealed class BundledFontMetricsProviderTests
{
    [TestMethod]
    public void BelarusianViceCity_LoadsCopiedAndEmbeddedCanonicalAsset()
    {
        var assetPath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "ViceCity",
            "belarusian.fontmetrics.json");

        Assert.IsTrue(File.Exists(assetPath));
        var asset = FontMetricsFileSerializer.Load(assetPath);
        var bundled = FontMetricsPresets.BelarusianViceCity;

        CollectionAssert.AreEqual(asset.Font2.Advances, bundled.Font2.Advances);
        CollectionAssert.AreEqual(asset.Font1.Advances, bundled.Font1.Advances);
        Assert.AreEqual(
            "50E8D5CDC3C7904875FC014CB343BDEEC0D8C9CF57C234F4B5AB53B60DABC1EA",
            GetCanonicalTableHash(bundled));
    }

    [TestMethod]
    public void BelarusianViceCity_HasVerifiedRowsAndSparseContextOverrides()
    {
        var profile = FontMetricsPresets.BelarusianViceCity;

        Assert.HasCount(FontMetricsTable.MetricCount, profile.Font2.Advances);
        Assert.HasCount(FontMetricsTable.MetricCount, profile.Font1.Advances);
        Assert.AreEqual((ushort)11, profile.Font2.Advances[0x91 - 0x20]);
        Assert.AreEqual((ushort)22, profile.Font2.Advances[0xA8 - 0x20]);
        Assert.AreEqual((ushort)14, profile.Font1.Advances[0x91 - 0x20]);
        Assert.AreEqual((ushort)14, profile.Font1.Advances[0xA8 - 0x20]);
        Assert.AreEqual((ushort)18, profile.Font1.Advances[198]);
        Assert.HasCount(8, profile.Overrides);
        AssertOverride(profile, FontRenderContext.Gameplay, 0x91, 13);
        AssertOverride(profile, FontRenderContext.Subtitles, 0x91, 13);
        AssertOverride(profile, FontRenderContext.SaveLoad, 0x91, 15);
        AssertOverride(profile, FontRenderContext.SaveLoad, 0xA8, 15);
        AssertOverride(profile, FontRenderContext.ExitConfirmation, 0x91, 16);
        AssertOverride(profile, FontRenderContext.ExitConfirmation, 0xA8, 16);
        AssertOverride(profile, FontRenderContext.Heading, 0x91, 18);
        AssertOverride(profile, FontRenderContext.Heading, 0xA8, 18);
        Assert.IsFalse(profile.Overrides.Any(item => item.Context == FontRenderContext.MainMenu));
    }

    [TestMethod]
    public void BelarusianViceCity_ReturnsIndependentCopies()
    {
        var first = FontMetricsPresets.BelarusianViceCity;
        first.Font2.Advances[0] = 999;
        first.Overrides.Clear();

        var second = FontMetricsPresets.BelarusianViceCity;

        Assert.AreEqual((ushort)5, second.Font2.Advances[0]);
        Assert.HasCount(8, second.Overrides);
    }

    private static string GetCanonicalTableHash(FontMetricsProfile profile)
    {
        var data = new byte[2 * FontMetricsTable.MetricCount * sizeof(ushort)];
        var offset = 0;
        foreach (var advance in profile.Font2.Advances.Concat(profile.Font1.Advances))
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, sizeof(ushort)), advance);
            offset += sizeof(ushort);
        }

        return Convert.ToHexString(SHA256.HashData(data));
    }

    private static void AssertOverride(
        FontMetricsProfile profile,
        FontRenderContext context,
        byte code,
        ushort advance)
    {
        var item = profile.Overrides.Single(candidate =>
            candidate.Context == context &&
            candidate.Font == FontTextureKind.Font1 &&
            candidate.Code == code);
        Assert.AreEqual(advance, item.Advance);
    }
}
