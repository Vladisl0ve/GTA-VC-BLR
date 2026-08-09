using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public static class GlyphAtlasService
{
    private const int FirstPrintableCode = 0x20;

    public static IReadOnlyList<GlyphCell> CreateCells(
        TxdTexture texture,
        IReadOnlyDictionary<byte, char> customCharacters,
        GXTType gameType = GXTType.None)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(customCharacters);

        if (!texture.IsFontAtlas || texture.Width <= 0 || texture.Height <= 0)
        {
            return [];
        }

        const int columns = 16;
        var cellWidth = Math.Max(1, texture.Width / columns);
        var usesViceCityFontRows = gameType == GXTType.GtaViceCity &&
            (texture.Name.Equals("font1", StringComparison.OrdinalIgnoreCase) ||
             texture.Name.Equals("font2", StringComparison.OrdinalIgnoreCase));
        var cellHeight = usesViceCityFontRows ||
                         texture.Name.Equals("font2", StringComparison.OrdinalIgnoreCase)
            ? Math.Max(1, (int)Math.Round(texture.Height / 12.8, MidpointRounding.AwayFromZero))
            : Math.Max(1, texture.Height / 16);
        var rows = (texture.Height + cellHeight - 1) / cellHeight;
        var result = new List<GlyphCell>(columns * rows);

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var x = column * cellWidth;
                var y = row * cellHeight;
                if (x >= texture.Width || y >= texture.Height)
                {
                    continue;
                }

                var index = (row * columns) + column;
                var numericCode = index + FirstPrintableCode;
                byte? code = numericCode <= byte.MaxValue ? (byte)numericCode : null;
                char? character = code is { } value
                    ? ResolveCharacter(value, customCharacters)
                    : null;
                result.Add(new GlyphCell(
                    index,
                    code,
                    character,
                    x,
                    y,
                    Math.Min(cellWidth, texture.Width - x),
                    Math.Min(cellHeight, texture.Height - y)));
            }
        }

        return result;
    }

    public static byte[] Crop(TxdTexture texture, GlyphCell cell)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(cell);

        var source = texture.PreviewPixelsBgra32.Length > 0
            ? texture.PreviewPixelsBgra32
            : texture.PixelsBgra32;
        var target = new byte[checked(cell.Width * cell.Height * 4)];
        for (var row = 0; row < cell.Height; row++)
        {
            Buffer.BlockCopy(
                source,
                checked(((cell.Y + row) * texture.Width + cell.X) * 4),
                target,
                row * cell.Width * 4,
                cell.Width * 4);
        }

        return target;
    }

    private static char? ResolveCharacter(
        byte code,
        IReadOnlyDictionary<byte, char> customCharacters)
    {
        if (customCharacters.TryGetValue(code, out var character))
        {
            return character;
        }

        return code is >= 0x20 and <= 0x7E ? (char)code : null;
    }
}
