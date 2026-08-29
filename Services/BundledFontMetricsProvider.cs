using System.IO;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

internal static class BundledFontMetricsProvider
{
    private const string BelarusianViceCityResourceName =
        "GTA_GXT_Editor.Assets.ViceCity.belarusian.fontmetrics.json";

    private static readonly Lazy<FontMetricsProfile> BelarusianViceCityProfile =
        new(LoadBelarusianViceCity, LazyThreadSafetyMode.ExecutionAndPublication);

    public static FontMetricsProfile BelarusianViceCity =>
        BelarusianViceCityProfile.Value.Clone();

    private static FontMetricsProfile LoadBelarusianViceCity()
    {
        using var stream = typeof(BundledFontMetricsProvider).Assembly
            .GetManifestResourceStream(BelarusianViceCityResourceName)
            ?? throw new InvalidDataException(
                $"Bundled font metrics resource '{BelarusianViceCityResourceName}' is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return FontMetricsFileSerializer.Deserialize(buffer.ToArray());
    }
}
