using System.IO;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

internal static class BundledCharacterMapProvider
{
    private const string BelarusianViceCityResourceName =
        "GTA_GXT_Editor.Assets.ViceCity.belarusian.gxtmap.json";

    private static readonly Lazy<CharacterMapProfile> BelarusianViceCityProfile =
        new(LoadBelarusianViceCity, LazyThreadSafetyMode.ExecutionAndPublication);

    public static CharacterMapProfile BelarusianViceCity =>
        BelarusianViceCityProfile.Value.Clone();

    private static CharacterMapProfile LoadBelarusianViceCity()
    {
        using var stream = typeof(BundledCharacterMapProvider).Assembly
            .GetManifestResourceStream(BelarusianViceCityResourceName)
            ?? throw new InvalidDataException(LocalizationProvider.Current.Format(
                "Resource.CharacterMapMissing",
                BelarusianViceCityResourceName));
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return CharacterMapFileSerializer.Deserialize(buffer.ToArray());
    }
}
