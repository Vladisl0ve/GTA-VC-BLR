using System.Text;

namespace GTA_GXT_Editor.Tests;

internal static class TestTxdFactory
{
    private const uint Version = 0x1003FFFF;

    public static byte[] Create(params TextureSpec[] textures)
    {
        return Chunk(0x16, writer =>
        {
            WriteChunk(writer, 0x01, structure =>
            {
                structure.Write((ushort)textures.Length);
                structure.Write((ushort)0);
            });

            foreach (var texture in textures)
            {
                WriteChunk(writer, 0x15, native =>
                {
                    WriteChunk(native, 0x01, structure => WriteTextureStructure(structure, texture));
                    WriteChunk(native, 0x03, _ => { });
                });
            }

            WriteChunk(writer, 0x03, _ => { });
        });
    }

    public static TextureSpec Bgra32(
        string name,
        int width,
        int height,
        byte[] pixels,
        string maskName = "",
        bool hasAlpha = true) =>
        new(name, maskName, 8, 0x0500, checked((ushort)width), checked((ushort)height), 32, 0, hasAlpha ? 1u : 0u, null, pixels);

    public static TextureSpec Lum8(string name, int width, int height, byte[] pixels) =>
        new(name, "", 8, 0x0400, checked((ushort)width), checked((ushort)height), 8, 0, 0, null, pixels);

    public static TextureSpec Pal8(
        string name,
        int width,
        int height,
        byte[] palette,
        byte[] indexes) =>
        new(name, "", 8, 0x2500, checked((ushort)width), checked((ushort)height), 8, 0, 1, palette, indexes);

    public static TextureSpec Pal4(
        string name,
        int width,
        int height,
        byte[] palette,
        byte[] indexes) =>
        new(name, "", 8, 0x4500, checked((ushort)width), checked((ushort)height), 4, 0, 1, palette, indexes);

    public static TextureSpec Uncompressed(
        string name,
        uint platform,
        uint rasterFormat,
        int width,
        int height,
        byte depth,
        byte[] pixels,
        uint alphaOrFourCc = 0) =>
        new(
            name,
            "",
            platform,
            rasterFormat,
            checked((ushort)width),
            checked((ushort)height),
            depth,
            0,
            alphaOrFourCc,
            null,
            pixels);

    public static TextureSpec Dxt(string name, int compression, byte[] data) =>
        new(name, "", 8, 0x0500, 4, 4, 16, compression, 1, null, data);

    public static TextureSpec DxtD3D9(string name, uint fourCc, byte[] data) =>
        new(name, "", 9, 0x0500, 4, 4, 16, 8, fourCc, null, data);

    private static void WriteTextureStructure(BinaryWriter writer, TextureSpec texture)
    {
        writer.Write(texture.Platform);
        writer.Write(0x1101u);
        WriteFixedString(writer, texture.Name, 32);
        WriteFixedString(writer, texture.MaskName, 32);
        writer.Write(texture.RasterFormat);
        writer.Write(texture.AlphaOrFourCc);
        writer.Write(texture.Width);
        writer.Write(texture.Height);
        writer.Write(texture.Depth);
        writer.Write(checked((byte)(1 + texture.AdditionalMipmaps.Length)));
        writer.Write((byte)4);
        writer.Write((byte)texture.Compression);
        if (texture.Palette is not null)
        {
            writer.Write(texture.Palette);
        }

        writer.Write(texture.Pixels.Length);
        writer.Write(texture.Pixels);
        foreach (var mipmap in texture.AdditionalMipmaps)
        {
            writer.Write(mipmap.Length);
            writer.Write(mipmap);
        }
    }

    private static byte[] Chunk(uint type, Action<BinaryWriter> writePayload)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        WriteChunk(writer, type, writePayload);
        return stream.ToArray();
    }

    private static void WriteChunk(BinaryWriter writer, uint type, Action<BinaryWriter> writePayload)
    {
        using var payloadStream = new MemoryStream();
        using (var payloadWriter = new BinaryWriter(payloadStream, Encoding.ASCII, leaveOpen: true))
        {
            writePayload(payloadWriter);
        }

        writer.Write(type);
        writer.Write(checked((uint)payloadStream.Length));
        writer.Write(Version);
        writer.Write(payloadStream.ToArray());
    }

    private static void WriteFixedString(BinaryWriter writer, string value, int length)
    {
        var bytes = new byte[length];
        Encoding.ASCII.GetBytes(value.AsSpan(0, Math.Min(value.Length, length - 1)), bytes);
        writer.Write(bytes);
    }

    internal sealed record TextureSpec(
        string Name,
        string MaskName,
        uint Platform,
        uint RasterFormat,
        ushort Width,
        ushort Height,
        byte Depth,
        int Compression,
        uint AlphaOrFourCc,
        byte[]? Palette,
        byte[] Pixels)
    {
        public byte[][] AdditionalMipmaps { get; init; } = [];
    }
}
