using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace GTA_GXT_Editor.Utils;

public static class FileStreamExtensions
{
    public static byte[] ReadBytes(this Stream stream, int bytesCount)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegative(bytesCount);

        var bytes = new byte[bytesCount];
        stream.ReadExactly(bytes);
        return bytes;
    }

    public static int ReadInt(this Stream stream)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        stream.ReadExactly(bytes);
        return BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }

    public static string ReadString(this Stream stream, int stringLength) =>
        Encoding.ASCII.GetString(stream.ReadBytes(stringLength));

    public static void WriteBytes(this Stream stream, ReadOnlySpan<byte> inputBytes) =>
        stream.Write(inputBytes);

    public static void WriteInt(this Stream stream, int inputInt)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, inputInt);
        stream.Write(bytes);
    }

    public static void WriteString(this Stream stream, string inputString) =>
        stream.WriteBytes(Encoding.ASCII.GetBytes(inputString));
}
