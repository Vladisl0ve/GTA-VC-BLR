using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public static class GxtJsonExporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Export(
        string targetPath,
        string sourcePath,
        string game,
        IEnumerable<GxtEntryRow> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(game);
        ArgumentNullException.ThrowIfNull(entries);

        var document = new GxtJsonDocument(
            game,
            Path.GetFileName(sourcePath),
            entries.Select(entry => new GxtJsonEntry(
                entry.Name,
                entry.Text,
                entry.RawTableName is null ? null : entry.Table)));

        var json = JsonSerializer.Serialize(document, SerializerOptions);
        File.WriteAllText(targetPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private sealed record GxtJsonDocument(
        [property: JsonPropertyName("game")] string Game,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("entries")] IEnumerable<GxtJsonEntry> Entries);

    private sealed record GxtJsonEntry(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("table")] string? Table);
}
