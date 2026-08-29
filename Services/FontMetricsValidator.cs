using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public static class FontMetricsValidator
{
    public const byte MinimumCode = 0x20;
    public const byte MaximumCode = MinimumCode + FontMetricsTable.MetricCount - 1;

    // The extracted Vice City values are at most 33. This deliberately generous
    // ceiling still rejects uint16 values that cannot be meaningful glyph advances.
    public const ushort MaximumAdvance = 1024;

    public static IReadOnlyList<string> Validate(FontMetricsProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var issues = new List<string>();

        if (profile.Version != FontMetricsProfile.CurrentVersion)
        {
            issues.Add($"Font metrics version {profile.Version} is unsupported.");
        }

        ValidateTable(profile.Font2, nameof(profile.Font2), issues);
        ValidateTable(profile.Font1, nameof(profile.Font1), issues);

        if (profile.Overrides is null)
        {
            issues.Add("Font metrics overrides are missing.");
            return issues;
        }

        var overrideKeys = new HashSet<(FontRenderContext Context, FontTextureKind Font, byte Code)>();
        foreach (var item in profile.Overrides)
        {
            if (item is null)
            {
                issues.Add("A font metric override is missing.");
                continue;
            }

            if (!Enum.IsDefined(item.Context))
            {
                issues.Add($"Font render context '{item.Context}' is unsupported.");
            }

            if (!Enum.IsDefined(item.Font))
            {
                issues.Add($"Font texture kind '{item.Font}' is unsupported.");
            }

            if (item.Code is < MinimumCode or > MaximumCode)
            {
                issues.Add($"Font metric code 0x{item.Code:X2} must be in the 0x{MinimumCode:X2}-0x{MaximumCode:X2} range.");
            }

            if (item.Advance > MaximumAdvance)
            {
                issues.Add($"Font metric override for code 0x{item.Code:X2} exceeds the maximum advance of {MaximumAdvance}.");
            }

            if (!overrideKeys.Add((item.Context, item.Font, item.Code)))
            {
                issues.Add($"Font metric override for {item.Context}, {item.Font}, code 0x{item.Code:X2} is duplicated.");
            }
        }

        return issues.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static void ValidateTable(
        FontMetricsTable? table,
        string tableName,
        List<string> issues)
    {
        if (table?.Advances is null)
        {
            issues.Add($"Font metrics table {tableName} is missing.");
            return;
        }

        if (table.Advances.Length != FontMetricsTable.MetricCount)
        {
            issues.Add($"Font metrics table {tableName} must contain exactly {FontMetricsTable.MetricCount} advances.");
        }

        for (var index = 0; index < table.Advances.Length; index++)
        {
            if (table.Advances[index] > MaximumAdvance)
            {
                issues.Add($"Font metrics table {tableName} index {index} exceeds the maximum advance of {MaximumAdvance}.");
            }
        }
    }
}
