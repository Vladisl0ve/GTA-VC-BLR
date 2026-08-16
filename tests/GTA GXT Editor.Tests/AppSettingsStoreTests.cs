using System.Text.Json;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Tests;

[TestClass]
public sealed class AppSettingsStoreTests
{
    [TestMethod]
    public void MissingAndCorruptSettings_ReturnNull()
    {
        var directory = CreateTemporaryDirectory();
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var store = new JsonAppSettingsStore(path);
            Assert.IsNull(store.LoadUiLanguage());

            File.WriteAllText(path, "{ not-json");
            Assert.IsNull(store.LoadUiLanguage());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SaveUiLanguage_WritesCamelCaseJsonAndRoundTrips()
    {
        var directory = CreateTemporaryDirectory();
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var store = new JsonAppSettingsStore(path);
            store.SaveUiLanguage("ru-RU");

            Assert.AreEqual("ru-RU", store.LoadUiLanguage());
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            Assert.AreEqual("ru-RU", document.RootElement.GetProperty("uiLanguage").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "GtaGxtEditorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
