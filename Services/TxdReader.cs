using System.Buffers.Binary;
using System.IO;
using System.Text;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public sealed class TxdReader : ITxdReader
{
    private const uint StructChunk = 0x01;
    private const uint ExtensionChunk = 0x03;
    private const uint TextureNativeChunk = 0x15;
    private const uint TextureDictionaryChunk = 0x16;
    private const uint RasterFormatMask = 0x0F00;
    private const uint Palette8Flag = 0x2000;
    private const uint Palette4Flag = 0x4000;
    private const int MaxTextureDimension = 8192;
    private const int MaxTextureCount = 4096;

    public TxdDocument Read(ReadOnlyMemory<byte> data, string sourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        if (data.Length < 16)
        {
            throw Invalid(sourceName, LocalizationProvider.Current.Get("TxdError.TooSmall"));
        }

        using var stream = new MemoryStream(data.ToArray(), writable: false);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        var dictionary = ReadChunk(reader, stream.Length, sourceName);
        if (dictionary.Type != TextureDictionaryChunk || dictionary.End != stream.Length)
        {
            throw Invalid(sourceName, LocalizationProvider.Current.Get("TxdError.RootMissing"));
        }

        var textureCount = -1;
        var textures = new List<TxdTexture>();
        while (stream.Position < dictionary.End)
        {
            var child = ReadChunk(reader, dictionary.End, sourceName);
            switch (child.Type)
            {
                case StructChunk:
                    if (textureCount >= 0 || child.Length < 4)
                    {
                        throw Invalid(sourceName, LocalizationProvider.Current.Get("TxdError.HeaderCorrupt"));
                    }

                    textureCount = reader.ReadUInt16();
                    _ = reader.ReadUInt16();
                    if (textureCount > MaxTextureCount)
                    {
                        throw Invalid(sourceName, LocalizationProvider.Current.Get("TxdError.TooManyTextures"));
                    }

                    break;
                case TextureNativeChunk:
                    if (textureCount < 0)
                    {
                        throw Invalid(sourceName, LocalizationProvider.Current.Get("TxdError.DictionaryOrder"));
                    }

                    textures.Add(ReadTexture(reader, child, sourceName));
                    break;
                case ExtensionChunk:
                    break;
            }

            stream.Position = child.End;
        }

        if (textureCount < 0 || textureCount != textures.Count)
        {
            throw Invalid(sourceName, LocalizationProvider.Current.Get("TxdError.TextureCount"));
        }

        ApplyMasks(textures);
        return new TxdDocument
        {
            RenderWareVersion = dictionary.Version,
            Textures = textures,
        };
    }

    private static TxdTexture ReadTexture(BinaryReader reader, Chunk textureChunk, string sourceName)
    {
        var stream = reader.BaseStream;
        var structure = ReadChunk(reader, textureChunk.End, sourceName);
        if (structure.Type != StructChunk || structure.Length < 88)
        {
            throw Invalid(sourceName, LocalizationProvider.Current.Get("TxdError.TextureCorrupt"));
        }

        var platformValue = reader.ReadUInt32();
        if (platformValue is not ((uint)TxdPlatform.D3D8) and not ((uint)TxdPlatform.D3D9))
        {
            throw Invalid(sourceName, LocalizationProvider.Current.Format("TxdError.Platform", platformValue));
        }

        var platform = (TxdPlatform)platformValue;
        _ = reader.ReadUInt32(); // filter flags
        var name = ReadFixedString(reader, 32);
        var maskName = ReadFixedString(reader, 32);
        var rasterFormat = reader.ReadUInt32();
        var alphaOrFourCc = reader.ReadUInt32();
        var width = reader.ReadUInt16();
        var height = reader.ReadUInt16();
        var depth = reader.ReadByte();
        var mipmapCount = reader.ReadByte();
        _ = reader.ReadByte(); // raster type
        var compressionFlags = reader.ReadByte();

        if (string.IsNullOrWhiteSpace(name) || width == 0 || height == 0 ||
            width > MaxTextureDimension || height > MaxTextureDimension || mipmapCount == 0)
        {
            throw Invalid(sourceName, LocalizationProvider.Current.Format("TxdError.Parameters", name));
        }

        _ = checked(width * height * 4);
        var compression = DetectCompression(platform, alphaOrFourCc, compressionFlags, sourceName, name);
        var hasAlpha = platform == TxdPlatform.D3D8
            ? alphaOrFourCc == 1
            : (compressionFlags & 1) != 0;

        byte[]? palette = null;
        var paletteEntries = (rasterFormat & Palette8Flag) != 0
            ? 256
            : (rasterFormat & Palette4Flag) != 0 ? 16 : 0;
        if (paletteEntries > 0)
        {
            palette = ReadBytes(reader, checked(paletteEntries * 4), structure.End, sourceName);
        }

        byte[]? firstMipmap = null;
        for (var index = 0; index < mipmapCount; index++)
        {
            EnsureAvailable(stream, structure.End, sizeof(uint), sourceName);
            var size = reader.ReadUInt32();
            if (size > int.MaxValue)
            {
                throw Invalid(sourceName, LocalizationProvider.Current.Format("TxdError.MipmapTooLarge", name));
            }

            var mipmap = ReadBytes(reader, (int)size, structure.End, sourceName);
            firstMipmap ??= mipmap;
        }

        if (firstMipmap is null)
        {
            throw Invalid(sourceName, LocalizationProvider.Current.Format("TxdError.NoImage", name));
        }

        var pixels = DecodePixels(
            firstMipmap,
            palette,
            width,
            height,
            depth,
            rasterFormat,
            compression,
            sourceName,
            name);
        return new TxdTexture
        {
            Name = name,
            MaskName = maskName,
            Platform = platform,
            Width = width,
            Height = height,
            Depth = depth,
            MipmapCount = mipmapCount,
            RasterFormat = rasterFormat,
            Compression = compression,
            HasAlpha = hasAlpha || ContainsTransparency(pixels),
            PixelsBgra32 = pixels,
            PreviewPixelsBgra32 = pixels.ToArray(),
        };
    }

    private static TxdCompression DetectCompression(
        TxdPlatform platform,
        uint alphaOrFourCc,
        byte flags,
        string sourceName,
        string textureName)
    {
        if (platform == TxdPlatform.D3D8)
        {
            return flags switch
            {
                0 => TxdCompression.None,
                1 => TxdCompression.Dxt1,
                3 => TxdCompression.Dxt3,
                _ => throw Invalid(sourceName, LocalizationProvider.Current.Format("TxdError.Compression", flags, textureName)),
            };
        }

        if ((flags & 8) == 0)
        {
            return TxdCompression.None;
        }

        return alphaOrFourCc switch
        {
            0x31545844 => TxdCompression.Dxt1, // DXT1
            0x33545844 => TxdCompression.Dxt3, // DXT3
            _ => throw Invalid(sourceName, LocalizationProvider.Current.Format("TxdError.FourCc", textureName)),
        };
    }

    private static byte[] DecodePixels(
        byte[] data,
        byte[]? palette,
        int width,
        int height,
        int depth,
        uint rasterFormat,
        TxdCompression compression,
        string sourceName,
        string textureName)
    {
        if (compression != TxdCompression.None)
        {
            var format = compression == TxdCompression.Dxt1
                ? CompressionFormat.Bc1WithAlpha
                : CompressionFormat.Bc2;
            var colors = new BcDecoder().DecodeRaw(data, width, height, format);
            var result = new byte[checked(width * height * 4)];
            for (var index = 0; index < colors.Length; index++)
            {
                result[index * 4] = colors[index].b;
                result[(index * 4) + 1] = colors[index].g;
                result[(index * 4) + 2] = colors[index].r;
                result[(index * 4) + 3] = colors[index].a;
            }

            return result;
        }

        if (palette is not null)
        {
            return DecodePalette(data, palette, width, height, (rasterFormat & Palette4Flag) != 0, sourceName, textureName);
        }

        return DecodeUncompressed(data, width, height, depth, rasterFormat & RasterFormatMask, sourceName, textureName);
    }

    private static byte[] DecodePalette(
        byte[] data,
        byte[] palette,
        int width,
        int height,
        bool fourBit,
        string sourceName,
        string textureName)
    {
        var pixelCount = checked(width * height);
        var required = fourBit ? (pixelCount + 1) / 2 : pixelCount;
        if (data.Length < required)
        {
            throw Invalid(sourceName, LocalizationProvider.Current.Format("TxdError.PaletteIncomplete", textureName));
        }

        var result = new byte[checked(pixelCount * 4)];
        for (var index = 0; index < pixelCount; index++)
        {
            var paletteIndex = fourBit
                ? (index % 2 == 0 ? data[index / 2] & 0x0F : data[index / 2] >> 4)
                : data[index];
            var paletteOffset = paletteIndex * 4;
            Buffer.BlockCopy(palette, paletteOffset, result, index * 4, 4);
        }

        return result;
    }

    private static byte[] DecodeUncompressed(
        byte[] data,
        int width,
        int height,
        int depth,
        uint baseFormat,
        string sourceName,
        string textureName)
    {
        var pixelCount = checked(width * height);
        var bytesPerPixel = depth switch
        {
            32 => 4,
            24 => 3,
            16 => 2,
            8 => 1,
            _ => throw Invalid(sourceName, LocalizationProvider.Current.Format("TxdError.Depth", depth, textureName)),
        };
        if (data.Length < checked(pixelCount * bytesPerPixel))
        {
            throw Invalid(sourceName, LocalizationProvider.Current.Format("TxdError.DataIncomplete", textureName));
        }

        var result = new byte[checked(pixelCount * 4)];
        for (var index = 0; index < pixelCount; index++)
        {
            var sourceOffset = index * bytesPerPixel;
            var targetOffset = index * 4;
            switch (baseFormat)
            {
                case 0x0500 when depth == 32: // B8G8R8A8
                    Buffer.BlockCopy(data, sourceOffset, result, targetOffset, 4);
                    break;
                case 0x0600 when depth == 24: // B8G8R8
                    result[targetOffset] = data[sourceOffset];
                    result[targetOffset + 1] = data[sourceOffset + 1];
                    result[targetOffset + 2] = data[sourceOffset + 2];
                    result[targetOffset + 3] = 255;
                    break;
                case 0x0400 when depth == 8: // LUM8
                    result[targetOffset] = data[sourceOffset];
                    result[targetOffset + 1] = data[sourceOffset];
                    result[targetOffset + 2] = data[sourceOffset];
                    result[targetOffset + 3] = 255;
                    break;
                case 0x0100 when depth == 16: // A1R5G5B5
                    WriteA1R5G5B5(ReadUInt16(data, sourceOffset), result, targetOffset);
                    break;
                case 0x0200 when depth == 16: // R5G6B5
                    WriteR5G6B5(ReadUInt16(data, sourceOffset), result, targetOffset);
                    break;
                case 0x0300 when depth == 16: // R4G4B4A4
                    WriteR4G4B4A4(ReadUInt16(data, sourceOffset), result, targetOffset);
                    break;
                case 0x0A00 when depth == 16: // R5G5B5
                    WriteR5G5B5(ReadUInt16(data, sourceOffset), result, targetOffset);
                    break;
                default:
                    throw Invalid(sourceName, LocalizationProvider.Current.Format("TxdError.Format", baseFormat, depth, textureName));
            }
        }

        return result;
    }

    private static void ApplyMasks(List<TxdTexture> textures)
    {
        var byName = textures
            .GroupBy(texture => texture.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var texture in textures)
        {
            if (string.IsNullOrWhiteSpace(texture.MaskName) ||
                !byName.TryGetValue(texture.MaskName, out var mask) ||
                mask.Width != texture.Width || mask.Height != texture.Height)
            {
                continue;
            }

            var combined = texture.PixelsBgra32.ToArray();
            var maskHasAlpha = ContainsTransparency(mask.PixelsBgra32);
            for (var pixel = 0; pixel < texture.Width * texture.Height; pixel++)
            {
                var offset = pixel * 4;
                var maskAlpha = maskHasAlpha
                    ? mask.PixelsBgra32[offset + 3]
                    : (byte)((mask.PixelsBgra32[offset] + mask.PixelsBgra32[offset + 1] + mask.PixelsBgra32[offset + 2]) / 3);
                combined[offset + 3] = (byte)(((combined[offset + 3] * maskAlpha) + 127) / 255);
            }

            texture.PreviewPixelsBgra32 = combined;
        }
    }

    private static bool ContainsTransparency(byte[] pixels)
    {
        for (var offset = 3; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset] < byte.MaxValue)
            {
                return true;
            }
        }

        return false;
    }

    private static ushort ReadUInt16(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));

    private static void WriteA1R5G5B5(ushort value, byte[] target, int offset)
    {
        target[offset] = Expand5(value & 0x1F);
        target[offset + 1] = Expand5((value >> 5) & 0x1F);
        target[offset + 2] = Expand5((value >> 10) & 0x1F);
        target[offset + 3] = (value & 0x8000) != 0 ? (byte)255 : (byte)0;
    }

    private static void WriteR5G6B5(ushort value, byte[] target, int offset)
    {
        target[offset] = Expand5(value & 0x1F);
        target[offset + 1] = Expand6((value >> 5) & 0x3F);
        target[offset + 2] = Expand5((value >> 11) & 0x1F);
        target[offset + 3] = 255;
    }

    private static void WriteR4G4B4A4(ushort value, byte[] target, int offset)
    {
        target[offset] = Expand4(value & 0x0F);
        target[offset + 1] = Expand4((value >> 4) & 0x0F);
        target[offset + 2] = Expand4((value >> 8) & 0x0F);
        target[offset + 3] = Expand4((value >> 12) & 0x0F);
    }

    private static void WriteR5G5B5(ushort value, byte[] target, int offset)
    {
        target[offset] = Expand5(value & 0x1F);
        target[offset + 1] = Expand5((value >> 5) & 0x1F);
        target[offset + 2] = Expand5((value >> 10) & 0x1F);
        target[offset + 3] = 255;
    }

    private static byte Expand4(int value) => (byte)((value << 4) | value);

    private static byte Expand5(int value) => (byte)((value << 3) | (value >> 2));

    private static byte Expand6(int value) => (byte)((value << 2) | (value >> 4));

    private static string ReadFixedString(BinaryReader reader, int length)
    {
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
        {
            throw new EndOfStreamException();
        }

        var terminator = Array.IndexOf(bytes, (byte)0);
        return Encoding.ASCII.GetString(bytes, 0, terminator < 0 ? bytes.Length : terminator);
    }

    private static byte[] ReadBytes(
        BinaryReader reader,
        int count,
        long end,
        string sourceName)
    {
        EnsureAvailable(reader.BaseStream, end, count, sourceName);
        var result = reader.ReadBytes(count);
        if (result.Length != count)
        {
            throw Invalid(sourceName, LocalizationProvider.Current.Get("TxdError.UnexpectedEnd"));
        }

        return result;
    }

    private static void EnsureAvailable(Stream stream, long end, long count, string sourceName)
    {
        if (count < 0 || stream.Position > end - count)
        {
            throw Invalid(sourceName, LocalizationProvider.Current.Get("TxdError.ChunkBounds"));
        }
    }

    private static Chunk ReadChunk(BinaryReader reader, long parentEnd, string sourceName)
    {
        EnsureAvailable(reader.BaseStream, parentEnd, 12, sourceName);
        var type = reader.ReadUInt32();
        var length = reader.ReadUInt32();
        var version = reader.ReadUInt32();
        var end = checked(reader.BaseStream.Position + length);
        if (end > parentEnd)
        {
            throw Invalid(sourceName, LocalizationProvider.Current.Get("TxdError.ParentBounds"));
        }

        return new Chunk(type, length, version, end);
    }

    private static InvalidDataException Invalid(string sourceName, string reason) =>
        new(LocalizationProvider.Current.Format(
            "TxdError.Invalid",
            Path.GetFileName(sourceName),
            reason));

    private readonly record struct Chunk(uint Type, uint Length, uint Version, long End);
}
