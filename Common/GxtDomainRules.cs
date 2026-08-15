using System.IO;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Utils;

namespace GTA_GXT_Editor.Common;

public enum GxtNameValidationError
{
    None,
    Empty,
    TooLong,
    NonAscii,
}

public static class GxtDomainRules
{
    public const int MaximumNameLength = 8;

    public static string ToGameName(GXTType gameType) => gameType switch
    {
        GXTType.GtaIII => "GTA III",
        GXTType.GtaViceCity => "GTA Vice City",
        _ => throw new ArgumentOutOfRangeException(
            nameof(gameType),
            gameType,
            "Неподдерживаемый тип GXT."),
    };

    public static GXTType ParseCanonicalGameName(string? game, string documentDescription) =>
        game switch
        {
            "GTA III" => GXTType.GtaIII,
            "GTA Vice City" => GXTType.GtaViceCity,
            _ => throw new InvalidDataException(
                $"Игра '{game}' в {documentDescription} не поддерживается."),
        };

    public static GXTType ParseJsonGameName(string? game) =>
        game?.Trim().ToUpperInvariant() switch
        {
            "GTA III" or "GTA 3" or "GTA3" => GXTType.GtaIII,
            "GTA VICE CITY" or "VICE CITY" or "GTAVC" => GXTType.GtaViceCity,
            null or "" => throw new InvalidDataException(
                "В JSON отсутствует обязательное поле 'game'."),
            _ => throw new InvalidDataException(
                $"Игра '{game}' не поддерживается. Ожидается 'GTA III' или 'GTA Vice City'."),
        };

    public static string ToLanguageCode(GxtLanguage language) => language switch
    {
        GxtLanguage.Belarusian => "be",
        GxtLanguage.Russian => "ru",
        GxtLanguage.Ukrainian => "uk",
        _ => "en",
    };

    public static GxtLanguage ParseLanguageCode(string? language) =>
        language?.ToLowerInvariant() switch
        {
            "be" => GxtLanguage.Belarusian,
            "ru" => GxtLanguage.Russian,
            "uk" => GxtLanguage.Ukrainian,
            "en" => GxtLanguage.English,
            _ => throw new InvalidDataException(
                $"Язык '{language}' не поддерживается."),
        };

    public static GxtLanguage ParseFlexibleLanguageCode(string? language) =>
        language?.Trim().ToUpperInvariant() switch
        {
            null or "" or "AUTO" => GxtLanguage.Auto,
            "EN" or "ENG" or "ENGLISH" => GxtLanguage.English,
            "BE" or "BEL" or "BELARUSIAN" or "БЕЛАРУСКАЯ" => GxtLanguage.Belarusian,
            "RU" or "RUS" or "RUSSIAN" or "РУССКИЙ" => GxtLanguage.Russian,
            "UK" or "UKR" or "UKRAINIAN" or "УКРАЇНСЬКА" => GxtLanguage.Ukrainian,
            _ => throw new InvalidDataException(
                $"Язык '{language}' не поддерживается. Ожидается 'be', 'en', 'ru' или 'uk'."),
        };

    public static string? ToOptionalLanguageCode(GxtLanguage language) =>
        language == GxtLanguage.Auto ? null : ToLanguageCode(language);

    public static void ValidateGameType(GXTType gameType)
    {
        if (gameType is not (GXTType.GtaIII or GXTType.GtaViceCity))
        {
            throw new ArgumentOutOfRangeException(
                nameof(gameType),
                gameType,
                "Неподдерживаемый тип GXT.");
        }
    }

    public static GxtNameValidationError GetNameValidationError(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return GxtNameValidationError.Empty;
        }

        if (name.Length > MaximumNameLength)
        {
            return GxtNameValidationError.TooLong;
        }

        return name.Any(character => character is '\0' or > '\u007f')
            ? GxtNameValidationError.NonAscii
            : GxtNameValidationError.None;
    }

    public static GxtEntryIdentity CreateIdentity(
        GXTType gameType,
        string key,
        string? table)
    {
        ValidateGameType(gameType);
        return gameType == GXTType.GtaViceCity
            ? new GxtEntryIdentity(key, table)
            : new GxtEntryIdentity(key);
    }

    public static string? GetTableName(GXTType gameType, GXTBase entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return (gameType, entry) switch
        {
            (GXTType.GtaIII, GTAIII.GXTEntry) => null,
            (GXTType.GtaViceCity, GTAVC.GXTEntry viceCityEntry) =>
                viceCityEntry.TableName.GetClearName(),
            _ => throw new InvalidDataException(
                $"Тип записи '{entry.GetType().Name}' не соответствует типу проекта " +
                $"{ToGameName(gameType)}."),
        };
    }
}
