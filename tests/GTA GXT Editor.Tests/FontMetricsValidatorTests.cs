using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Tests;

[TestClass]
public sealed class FontMetricsValidatorTests
{
    [TestMethod]
    public void Validate_AcceptsExactTablesZeroAdvancesAndCodeBoundaries()
    {
        var profile = CreateValidProfile();
        profile.Overrides.AddRange(
        [
            Override(FontMetricsValidator.MinimumCode),
            Override(FontMetricsValidator.MaximumCode),
        ]);

        var issues = FontMetricsValidator.Validate(profile);

        Assert.IsEmpty(issues);
    }

    [TestMethod]
    public void Validate_ReportsUnsupportedVersionAndWrongTableLengths()
    {
        var profile = new FontMetricsProfile
        {
            Version = 2,
            Font2 = new FontMetricsTable { Advances = new ushort[209] },
            Font1 = new FontMetricsTable { Advances = new ushort[211] },
        };

        var issues = FontMetricsValidator.Validate(profile);

        Assert.IsTrue(issues.Any(issue => issue.Contains("version 2", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(2, issues.Count(issue => issue.Contains("exactly 210", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Validate_ReportsMissingTables()
    {
        var profile = new FontMetricsProfile
        {
            Font2 = null!,
            Font1 = new FontMetricsTable { Advances = null! },
        };

        var issues = FontMetricsValidator.Validate(profile);

        Assert.AreEqual(2, issues.Count(issue => issue.Contains("missing", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Validate_ReportsOutOfRangeCodesDuplicateKeysAndOversizedAdvances()
    {
        var profile = CreateValidProfile();
        profile.Font2.Advances[17] = FontMetricsValidator.MaximumAdvance + 1;
        profile.Overrides.AddRange(
        [
            Override(0x1F),
            Override(0xF2),
            Override(0x91, FontMetricsValidator.MaximumAdvance + 1),
            Override(0x91, FontMetricsValidator.MaximumAdvance + 1),
        ]);

        var issues = FontMetricsValidator.Validate(profile);

        Assert.AreEqual(2, issues.Count(issue => issue.Contains("0x20-0xF1", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(issues.Any(issue => issue.Contains("index 17", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(issues.Any(issue => issue.Contains("override", StringComparison.OrdinalIgnoreCase) && issue.Contains("maximum", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(issues.Any(issue => issue.Contains("duplicated", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Validate_ReportsUnknownContextAndFontKind()
    {
        var profile = CreateValidProfile();
        profile.Overrides.Add(new FontMetricOverride
        {
            Context = (FontRenderContext)999,
            Font = (FontTextureKind)999,
            Code = 0x91,
            Advance = 13,
        });

        var issues = FontMetricsValidator.Validate(profile);

        Assert.IsTrue(issues.Any(issue => issue.Contains("context", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(issues.Any(issue => issue.Contains("texture kind", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Clone_DeepCopiesTablesAndOverrideList()
    {
        var profile = CreateValidProfile();
        profile.Overrides.Add(Override(0x91));

        var clone = profile.Clone();
        clone.Font2.Advances[0] = 99;
        clone.Overrides.Clear();

        Assert.AreEqual((ushort)0, profile.Font2.Advances[0]);
        Assert.HasCount(1, profile.Overrides);
    }

    private static FontMetricOverride Override(byte code, ushort advance = 13) => new()
    {
        Context = FontRenderContext.Gameplay,
        Font = FontTextureKind.Font1,
        Code = code,
        Advance = advance,
    };

    private static FontMetricsProfile CreateValidProfile() => new()
    {
        Font2 = new FontMetricsTable { Advances = new ushort[FontMetricsTable.MetricCount] },
        Font1 = new FontMetricsTable { Advances = new ushort[FontMetricsTable.MetricCount] },
    };
}
