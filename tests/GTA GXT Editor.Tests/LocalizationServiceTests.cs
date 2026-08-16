using System.Collections;
using System.Globalization;
using System.Resources;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;
using GTA_GXT_Editor.ViewModels;

namespace GTA_GXT_Editor.Tests;

[TestClass]
public sealed class LocalizationServiceTests
{
    [TestMethod]
    public void RussianCatalog_ContainsNonEmptyValuesAndCriticalUiKeys()
    {
        var resources = new ResourceManager(
            "GTA_GXT_Editor.Localization.Strings",
            typeof(LocalizationService).Assembly);
        var resourceSet = resources.GetResourceSet(
            CultureInfo.InvariantCulture,
            createIfNotExists: true,
            tryParents: true);

        Assert.IsNotNull(resourceSet);
        Assert.IsTrue(resourceSet.Cast<DictionaryEntry>().Count() > 100);
        Assert.IsTrue(resourceSet.Cast<DictionaryEntry>().All(entry =>
            entry.Key is string { Length: > 0 } && entry.Value is string { Length: > 0 }));
        Assert.AreEqual("Настройки", resources.GetString("Settings.Button"));
        Assert.AreEqual("Язык интерфейса", resources.GetString("Settings.InterfaceLanguage"));
        Assert.AreEqual("Русский", new LocalizationService().CurrentLanguage.NativeName);
    }

    [TestMethod]
    public void EnglishCatalog_MatchesRussianCatalogAndCanBeSelected()
    {
        var resources = new ResourceManager(
            "GTA_GXT_Editor.Localization.Strings",
            typeof(LocalizationService).Assembly);
        var russianSet = resources.GetResourceSet(
            CultureInfo.InvariantCulture,
            createIfNotExists: true,
            tryParents: false);
        var englishSet = resources.GetResourceSet(
            CultureInfo.GetCultureInfo("en-US"),
            createIfNotExists: true,
            tryParents: false);

        Assert.IsNotNull(russianSet);
        Assert.IsNotNull(englishSet);

        var russianKeys = russianSet.Cast<DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .OrderBy(key => key)
            .ToArray();
        var englishEntries = englishSet.Cast<DictionaryEntry>().ToArray();
        var englishKeys = englishEntries
            .Select(entry => (string)entry.Key)
            .OrderBy(key => key)
            .ToArray();

        CollectionAssert.AreEqual(russianKeys, englishKeys);
        Assert.IsTrue(englishEntries.All(entry =>
            entry.Value is string { Length: > 0 }));

        var localization = new LocalizationService();
        Assert.AreEqual(2, localization.SupportedLanguages.Count);
        Assert.IsTrue(localization.SetLanguage("en-US"));
        Assert.AreEqual("English", localization.CurrentLanguage.NativeName);
        Assert.AreEqual("Settings", localization.Get("Settings.Button"));
        Assert.AreEqual("Interface language", localization.Get("Settings.InterfaceLanguage"));
    }

    [TestMethod]
    public void UnknownResourceKey_ReturnsKeyAndUnsupportedCultureFallsBackToRussian()
    {
        var localization = new LocalizationService();

        Assert.IsFalse(localization.SetLanguage("xx-XX"));
        Assert.AreEqual(LocalizationService.DefaultCultureName, localization.CurrentLanguage.CultureName);
        Assert.AreEqual("Missing.Resource", localization.Get("Missing.Resource"));
    }

    [TestMethod]
    public void SetLanguage_RaisesNotificationsAndChangesLocalizedValuesImmediately()
    {
        var russian = new UiLanguageOption("ru-RU", "Русский");
        var english = new UiLanguageOption("en-US", "English");
        var localization = new LocalizationService(
            new TestResourceManager(),
            [russian, english]);
        var languageChanged = 0;
        var indexerChanged = 0;
        localization.LanguageChanged += (_, _) => languageChanged++;
        localization.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == "Item[]")
            {
                indexerChanged++;
            }
        };

        Assert.AreEqual("ru-RU:Sample", localization.Get("Sample"));
        Assert.IsTrue(localization.SetLanguage("en-US"));

        Assert.AreEqual("en-US:Sample", localization.Get("Sample"));
        Assert.AreSame(english, localization.CurrentLanguage);
        Assert.IsTrue(english.IsSelected);
        Assert.IsFalse(russian.IsSelected);
        Assert.AreEqual(1, languageChanged);
        Assert.AreEqual(1, indexerChanged);
    }

    [TestMethod]
    public void EntryListLanguageChange_RebuildsLabelsAndPreservesSemanticSelections()
    {
        var localization = new LocalizationService(
            new TestResourceManager(),
            [
                new UiLanguageOption("ru-RU", "Русский"),
                new UiLanguageOption("en-US", "English"),
            ]);
        var viewModel = new EntryListViewModel(new EditorSession(), localization);
        viewModel.SelectedSearchColumn = viewModel.SearchColumns.Single(option =>
            option.Column == SearchColumn.Comment);
        viewModel.SelectedMetadataType = viewModel.MetadataTypeOptions.Single(option =>
            option.Type == "mission");
        viewModel.SelectedSortOption = viewModel.SortOptions.Single(option =>
            option.Mode == EntrySortMode.EncounterOrder);

        localization.SetLanguage("en-US");

        Assert.AreEqual(SearchColumn.Comment, viewModel.SelectedSearchColumn.Column);
        Assert.AreEqual("mission", viewModel.SelectedMetadataType.Type);
        Assert.AreEqual(EntrySortMode.EncounterOrder, viewModel.SelectedSortOption.Mode);
        StringAssert.StartsWith(viewModel.SelectedSearchColumn.Title, "en-US:");
        StringAssert.StartsWith(viewModel.SelectedMetadataType.Name, "en-US:");
        StringAssert.StartsWith(viewModel.SelectedSortOption.Name, "en-US:");
    }

    private sealed class TestResourceManager : ResourceManager
    {
        public override string? GetString(string name, CultureInfo? culture) =>
            $"{culture?.Name}:{name}";
    }
}
