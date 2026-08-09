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

    public CommonGXTManager Open(
        string path,
        string? dictionaryPath = null,
        GxtLanguage language = GxtLanguage.Auto)
    {
        return DetectType(path) switch
        {
            GXTType.GtaIII => new GTAIII.GXTManager(path, dictionaryPath, language),
            GXTType.GtaViceCity => new GTAVC.GXTManager(path, dictionaryPath, language),
            _ => throw new InvalidDataException(
                $"Файл '{Path.GetFileName(path)}' повреждён или не является GXT-файлом GTA III/Vice City."),
        };
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Kept as an instance operation so callers can depend on one factory abstraction.")]
    public CommonGXTManager Open(
        ReadOnlyMemory<byte> data,
        GXTType type,
        string sourceName,
        GxtLanguage language,
        Dictionary<int[], char>? characterDictionary = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        using var stream = new MemoryStream(data.ToArray(), writable: false);
        return type switch
        {
            GXTType.GtaIII => new GTAIII.GXTManager(
                stream,
                sourceName,
                language,
                characterDictionary),
            GXTType.GtaViceCity => new GTAVC.GXTManager(
                stream,
                sourceName,
                language,
                characterDictionary),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Неподдерживаемый тип GXT."),
        };
    }

    public static CommonGXTManager Create(
        GXTType type,
        string? dictionaryPath = null,
        string? sourceName = null,
        IEnumerable<string>? sourceTexts = null,
        GxtLanguage language = GxtLanguage.Auto)
    {
        var texts = sourceTexts ?? [];
        return type switch
        {
            GXTType.GtaIII => GTAIII.GXTManager.Create(
                dictionaryPath,
                sourceName,
                texts,
                language),
            GXTType.GtaViceCity => GTAVC.GXTManager.Create(
                dictionaryPath,
                sourceName,
                texts,
                language),
            _ => throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                "Можно создать только GXT-файл GTA III или Vice City."),
        };
    }
}
