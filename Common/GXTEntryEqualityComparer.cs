using GTA_GXT_Editor.Utils;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Common;

public sealed class GXTEntryEqualityComparer : IEqualityComparer<GXTBase>
{
    public bool Equals(GXTBase? x, GXTBase? y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        return x is not null && y is not null &&
               string.Equals(GetIdentity(x), GetIdentity(y), StringComparison.Ordinal);
    }

    public int GetHashCode(GXTBase obj) =>
        StringComparer.Ordinal.GetHashCode(GetIdentity(obj));

    private static string GetIdentity(GXTBase entry)
    {
        return entry switch
        {
            GTAIII.GXTEntry => entry.DatName.GetClearName(),
            GTAVC.GXTEntry viceCityEntry =>
                $"{entry.DatName.GetClearName()}\u001f{viceCityEntry.TableName.GetClearName()}",
            _ => throw new ArgumentOutOfRangeException(
                nameof(entry),
                LocalizationProvider.Current.Get("Domain.UnknownEntryType")),
        };
    }
}
