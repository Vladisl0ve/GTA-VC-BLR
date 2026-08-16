using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public interface ILocalizationService : INotifyPropertyChanged
{
    IReadOnlyList<UiLanguageOption> SupportedLanguages { get; }

    UiLanguageOption CurrentLanguage { get; }

    string this[string key] { get; }

    event EventHandler? LanguageChanged;

    [SuppressMessage(
        "Naming",
        "CA1716:Identifiers should not match keywords",
        Justification = "Get is the intentionally specified localization API.")]
    string Get(string key);

    string Format(string key, params object?[] arguments);

    bool SetLanguage(string? cultureName);
}
