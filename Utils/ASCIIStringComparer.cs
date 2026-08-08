namespace GTA_3_GXT_Editor.Utils;

public sealed class ASCIIStringComparer : IComparer<string>
{
    public int Compare(string? x, string? y) => StringComparer.Ordinal.Compare(x, y);
}
