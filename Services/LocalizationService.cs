using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Resources;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public sealed class LocalizationService : ILocalizationService
{
    public const string DefaultCultureName = "en-US";

    private readonly ResourceManager _resources;
    private readonly IReadOnlyList<UiLanguageOption> _languages;
    private CultureInfo _culture;

    public LocalizationService(
        ResourceManager? resources = null,
        IReadOnlyList<UiLanguageOption>? supportedLanguages = null)
    {
        _resources = resources ?? new ResourceManager(
            "GTA_GXT_Editor.Localization.Strings",
            Assembly.GetExecutingAssembly());
        _languages = supportedLanguages ??
        [
            new UiLanguageOption(DefaultCultureName, "English"),
            new UiLanguageOption("be-BY", "Беларуская"),
        ];
        if (_languages.Count == 0 || !_languages.Any(language =>
                string.Equals(
                    language.CultureName,
                    DefaultCultureName,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                $"Supported languages must include {DefaultCultureName}.",
                nameof(supportedLanguages));
        }

        _culture = CultureInfo.GetCultureInfo(DefaultCultureName);
        CurrentLanguage = _languages.First(language => string.Equals(
            language.CultureName,
            DefaultCultureName,
            StringComparison.OrdinalIgnoreCase));
        CurrentLanguage.IsSelected = true;
        ApplyCulture();
    }

    public IReadOnlyList<UiLanguageOption> SupportedLanguages => _languages;

    public UiLanguageOption CurrentLanguage { get; private set; }

    public string this[string key] => Get(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? LanguageChanged;

    public string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _resources.GetString(key, _culture) ?? key;
    }

    public string Format(string key, params object?[] arguments) =>
        string.Format(_culture, Get(key), arguments);

    public bool SetLanguage(string? cultureName)
    {
        var requested = _languages.FirstOrDefault(language =>
            string.Equals(language.CultureName, cultureName, StringComparison.OrdinalIgnoreCase));
        var isSupported = requested is not null;
        var selected = requested ?? _languages.First(language => string.Equals(
            language.CultureName,
            DefaultCultureName,
            StringComparison.OrdinalIgnoreCase));
        var changed = !string.Equals(
            CurrentLanguage.CultureName,
            selected.CultureName,
            StringComparison.OrdinalIgnoreCase);

        CurrentLanguage = selected;
        foreach (var language in _languages)
        {
            language.IsSelected = ReferenceEquals(language, selected);
        }
        _culture = CultureInfo.GetCultureInfo(selected.CultureName);
        ApplyCulture();

        if (changed)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        return isSupported;
    }

    private void ApplyCulture()
    {
        CultureInfo.CurrentUICulture = _culture;
        CultureInfo.DefaultThreadCurrentUICulture = _culture;
    }
}

public static class LocalizationProvider
{
    private static ILocalizationService _current = new LocalizationService();

    public static ILocalizationService Current => _current;

    public static void Initialize(ILocalizationService localization) =>
        _current = localization ?? throw new ArgumentNullException(nameof(localization));
}
