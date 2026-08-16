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
    public void DefaultCatalog_ContainsEnglishValuesAndIsSelectedByDefault()
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
        Assert.AreEqual(
            "Settings",
            resources.GetString("Settings.Button", CultureInfo.InvariantCulture));
        Assert.AreEqual(
            "Interface language",
            resources.GetString("Settings.InterfaceLanguage", CultureInfo.InvariantCulture));
        var localization = new LocalizationService();
        Assert.AreEqual(2, localization.SupportedLanguages.Count);
        Assert.AreEqual("en-US", localization.CurrentLanguage.CultureName);
        Assert.AreEqual("English", localization.CurrentLanguage.NativeName);
        Assert.AreEqual("Settings", localization.Get("Settings.Button"));
        Assert.AreEqual("Interface language", localization.Get("Settings.InterfaceLanguage"));
    }

    [TestMethod]
    public void BelarusianCatalog_MatchesDefaultCatalogAndCanBeSelected()
    {
        var resources = new ResourceManager(
            "GTA_GXT_Editor.Localization.Strings",
            typeof(LocalizationService).Assembly);
        var defaultSet = resources.GetResourceSet(
            CultureInfo.InvariantCulture,
            createIfNotExists: true,
            tryParents: false);
        var belarusianSet = resources.GetResourceSet(
            CultureInfo.GetCultureInfo("be-BY"),
            createIfNotExists: true,
            tryParents: false);

        Assert.IsNotNull(defaultSet);
        Assert.IsNotNull(belarusianSet);

        var defaultKeys = defaultSet.Cast<DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .OrderBy(key => key)
            .ToArray();
        var belarusianEntries = belarusianSet.Cast<DictionaryEntry>().ToArray();
        var belarusianKeys = belarusianEntries
            .Select(entry => (string)entry.Key)
            .OrderBy(key => key)
            .ToArray();

        CollectionAssert.AreEqual(defaultKeys, belarusianKeys);
        Assert.IsTrue(belarusianEntries.All(entry =>
            entry.Value is string { Length: > 0 }));

        var localization = new LocalizationService();
        Assert.IsTrue(localization.SetLanguage("be-BY"));
        Assert.AreEqual("Беларуская", localization.CurrentLanguage.NativeName);
        Assert.AreEqual("Налады", localization.Get("Settings.Button"));
        Assert.AreEqual("Мова інтэрфейсу", localization.Get("Settings.InterfaceLanguage"));
    }

    [TestMethod]
    [DataRow("xx-XX")]
    [DataRow("ru-RU")]
    public void UnknownResourceKey_ReturnsKeyAndUnsupportedCultureFallsBackToEnglish(
        string unsupportedCulture)
    {
        var localization = new LocalizationService();

        Assert.IsFalse(localization.SetLanguage(unsupportedCulture));
        Assert.AreEqual(LocalizationService.DefaultCultureName, localization.CurrentLanguage.CultureName);
        Assert.AreEqual("Missing.Resource", localization.Get("Missing.Resource"));
    }

    [TestMethod]
    public void SetLanguage_RaisesNotificationsAndChangesLocalizedValuesImmediately()
    {
        var belarusian = new UiLanguageOption("be-BY", "Беларуская");
        var english = new UiLanguageOption("en-US", "English");
        var localization = new LocalizationService(
            new TestResourceManager(),
            [english, belarusian]);
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

        Assert.AreEqual("en-US:Sample", localization.Get("Sample"));
        Assert.IsTrue(localization.SetLanguage("be-BY"));

        Assert.AreEqual("be-BY:Sample", localization.Get("Sample"));
        Assert.AreSame(belarusian, localization.CurrentLanguage);
        Assert.IsTrue(belarusian.IsSelected);
        Assert.IsFalse(english.IsSelected);
        Assert.AreEqual(1, languageChanged);
        Assert.AreEqual(1, indexerChanged);
    }

    [TestMethod]
    public void EntryListLanguageChange_RebuildsLabelsAndPreservesSemanticSelections()
    {
        var localization = new LocalizationService(
            new TestResourceManager(),
            [
                new UiLanguageOption("en-US", "English"),
                new UiLanguageOption("be-BY", "Беларуская"),
            ]);
        localization.SetLanguage("be-BY");
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

    [TestMethod]
    public void EntryListTransientNullSelections_DoNotCrashAndLanguageChangeRestoresDefaults()
    {
        var localization = new LocalizationService(
            new TestResourceManager(),
            [
                new UiLanguageOption("en-US", "English"),
                new UiLanguageOption("be-BY", "Беларуская"),
            ]);
        var viewModel = new EntryListViewModel(new EditorSession(), localization);

        viewModel.SelectedSearchColumn = null!;
        viewModel.SelectedMetadataType = null!;
        viewModel.SelectedMetadataBlock = null!;
        viewModel.SelectedSortOption = null!;

        Assert.IsTrue(localization.SetLanguage("be-BY"));
        Assert.AreEqual(SearchColumn.All, viewModel.SelectedSearchColumn.Column);
        Assert.IsNull(viewModel.SelectedMetadataType.Type);
        Assert.IsNull(viewModel.SelectedMetadataBlock.Id);
        Assert.AreEqual(EntrySortMode.GxtOrder, viewModel.SelectedSortOption.Mode);
    }

    private sealed class TestResourceManager : ResourceManager
    {
        public override string? GetString(string name, CultureInfo? culture) =>
            $"{culture?.Name}:{name}";
    }
}
