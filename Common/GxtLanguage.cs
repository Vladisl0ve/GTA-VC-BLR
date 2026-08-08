using System.IO;

namespace GTA_GXT_Editor.Common;

public enum GxtLanguage
{
    Auto,
    English,
    Belarusian,
    Russian,
    Ukrainian,
}

internal static class GxtLanguageDetector
{
    public static GxtLanguage DetectForText(
        string? sourceName,
        IEnumerable<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var nameLanguage = DetectFromName(sourceName);
        if (nameLanguage != GxtLanguage.Auto)
        {
            return nameLanguage;
        }

        var text = string.Concat(texts);
        if (!text.Any(IsCyrillic))
        {
            return GxtLanguage.English;
        }

        if (text.Any("Ўў".Contains))
        {
            return GxtLanguage.Belarusian;
        }

        if (text.Any("ҐґЄєЇї".Contains))
        {
            return GxtLanguage.Ukrainian;
        }

        // І/і is shared by Belarusian and Ukrainian. Without an explicit JSON
        // language or a recognizable source name, keep the historical
        // Ukrainian fallback for backward compatibility.
        if (text.Any("Іі".Contains))
        {
            return GxtLanguage.Ukrainian;
        }

        return GxtLanguage.Russian;
    }

    public static GxtLanguage DetectFromName(string? sourceName)
    {
        var fileName = Path.GetFileNameWithoutExtension(sourceName ?? string.Empty);
        if (fileName.Contains("belarus", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("bel", StringComparison.OrdinalIgnoreCase))
        {
            return GxtLanguage.Belarusian;
        }

        if (fileName.Contains("ukrain", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("ukr", StringComparison.OrdinalIgnoreCase))
        {
            return GxtLanguage.Ukrainian;
        }

        if (fileName.Contains("russian", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("rus", StringComparison.OrdinalIgnoreCase))
        {
            return GxtLanguage.Russian;
        }

        if (fileName.Contains("american", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("english", StringComparison.OrdinalIgnoreCase))
        {
            return GxtLanguage.English;
        }

        return GxtLanguage.Auto;
    }

    public static GxtLanguage ParseJsonLanguage(string? language)
    {
        return language?.Trim().ToUpperInvariant() switch
        {
            null or "" or "AUTO" => GxtLanguage.Auto,
            "EN" or "ENG" or "ENGLISH" => GxtLanguage.English,
            "BE" or "BEL" or "BELARUSIAN" or "БЕЛАРУСКАЯ" => GxtLanguage.Belarusian,
            "RU" or "RUS" or "RUSSIAN" or "РУССКИЙ" => GxtLanguage.Russian,
            "UK" or "UKR" or "UKRAINIAN" or "УКРАЇНСЬКА" => GxtLanguage.Ukrainian,
            _ => throw new InvalidDataException(
                $"Язык '{language}' не поддерживается. Ожидается 'be', 'en', 'ru' или 'uk'."),
        };
    }

    public static string? ToJsonCode(GxtLanguage language) => language switch
    {
        GxtLanguage.English => "en",
        GxtLanguage.Belarusian => "be",
        GxtLanguage.Russian => "ru",
        GxtLanguage.Ukrainian => "uk",
        _ => null,
    };

    private static bool IsCyrillic(char character) =>
        character is >= '\u0400' and <= '\u04ff';
}
