namespace GTA_GXT_Editor.Models;

public enum TxdPlatform : uint
{
    D3D8 = 8,
    D3D9 = 9,
}

public enum TxdCompression
{
    None,
    Dxt1,
    Dxt3,
}

public sealed class TxdTexture
{
    public required string Name { get; init; }

    public required string MaskName { get; init; }

    public required TxdPlatform Platform { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required int Depth { get; init; }

    public required int MipmapCount { get; init; }

    public required uint RasterFormat { get; init; }

    public required TxdCompression Compression { get; init; }

    public required bool HasAlpha { get; init; }

    public required byte[] PixelsBgra32 { get; init; }

    public byte[] PreviewPixelsBgra32 { get; internal set; } = [];

    public bool IsFontAtlas =>
        Name.Equals("font1", StringComparison.OrdinalIgnoreCase) ||
        Name.Equals("font2", StringComparison.OrdinalIgnoreCase) ||
        Name.Equals("pager", StringComparison.OrdinalIgnoreCase);

    public string FormatDescription => Compression switch
    {
        TxdCompression.Dxt1 => "DXT1",
        TxdCompression.Dxt3 => "DXT3",
        _ => $"Raster 0x{RasterFormat & 0x0F00:X4}",
    };
}

public sealed class TxdDocument
{
    public required uint RenderWareVersion { get; init; }

    public required IReadOnlyList<TxdTexture> Textures { get; init; }
}

public sealed class TxdAttachment
{
    public required Guid Id { get; init; }

    public required string OriginalFileName { get; init; }

    public required string DisplayName { get; set; }

    public string? SourcePath { get; init; }

    public required byte[] Data { get; init; }

    public required TxdDocument Document { get; init; }

    public override string ToString() => DisplayName;
}

public sealed record GlyphCell(
    int Index,
    byte? Code,
    char? Character,
    int X,
    int Y,
    int Width,
    int Height)
{
    public string Label
    {
        get
        {
            var code = Code is { } value ? $"0x{value:X2}" : $"#{Index:D3}";
            return Character is { } character && !char.IsControl(character)
                ? $"{code}  {character}"
                : code;
        }
    }
}

public sealed class EditorProject
{
    public string? ProjectPath { get; set; }

    public required string GxtSourceName { get; set; }

    public string? GxtSourcePath { get; set; }

    public required Common.GXTType GameType { get; init; }

    public required Contracts.CommonGXTManager GxtManager { get; set; }

    public bool UsesCustomDictionary { get; set; }

    public TxdAttachment? AttachedTxd { get; set; }

    public CharacterMapProfile? CharacterMap { get; set; }

    public bool IsDirty { get; set; }
}
