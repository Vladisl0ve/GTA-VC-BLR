using System.IO;
using System.Text;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Contracts;

namespace GTA_GXT_Editor.Services;

public sealed class GxtManagerFactory
{
    public GXTType DetectType(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = File.OpenRead(path);
        if (stream.Length < 4)
        {
            return GXTType.None;
        }

        Span<byte> signature = stackalloc byte[4];
        stream.ReadExactly(signature);

        return Encoding.ASCII.GetString(signature) switch
        {
            "TKEY" => GXTType.GtaIII,
            "TABL" => GXTType.GtaViceCity,
            _ => GXTType.None,
        };
    }

    public CommonGXTManager Open(string path, string? dictionaryPath = null)
    {
        return DetectType(path) switch
        {
            GXTType.GtaIII => new GTAIII.GXTManager(path, dictionaryPath),
            GXTType.GtaViceCity => new GTAVC.GXTManager(path, dictionaryPath),
            _ => throw new InvalidDataException(
                $"Файл '{Path.GetFileName(path)}' повреждён или не является GXT-файлом GTA III/Vice City."),
        };
    }

    public static CommonGXTManager Create(
        GXTType type,
        string? dictionaryPath = null,
        string? sourceName = null,
        IEnumerable<string>? sourceTexts = null)
    {
        return type switch
        {
            GXTType.GtaIII => GTAIII.GXTManager.Create(dictionaryPath),
            GXTType.GtaViceCity => GTAVC.GXTManager.Create(
                dictionaryPath,
                sourceName,
                sourceTexts ?? []),
            _ => throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                "Можно создать только GXT-файл GTA III или Vice City."),
        };
    }
}
