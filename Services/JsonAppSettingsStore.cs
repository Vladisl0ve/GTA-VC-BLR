using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GTA_GXT_Editor.Services;

public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _path;

    public JsonAppSettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GTA GXT Editor",
            "settings.json");
    }

    public string? LoadUiLanguage()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(_path),
                JsonOptions);
            return settings?.UiLanguage;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void SaveUiLanguage(string cultureName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cultureName);
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("Settings path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = _path + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(new AppSettings { UiLanguage = cultureName }, JsonOptions));
        File.Move(temporaryPath, _path, overwrite: true);
    }

    private sealed class AppSettings
    {
        [JsonPropertyName("uiLanguage")]
        public string? UiLanguage { get; init; }
    }
}
