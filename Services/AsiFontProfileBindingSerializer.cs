using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public static class AsiFontProfileBindingSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };

    public static byte[] Serialize(AsiFontProfileBinding binding)
    {
        AsiFontProfileBindingService.Validate(binding);
        return JsonSerializer.SerializeToUtf8Bytes(binding, JsonOptions);
    }

    public static AsiFontProfileBinding Deserialize(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var binding = JsonSerializer.Deserialize<AsiFontProfileBinding>(data, JsonOptions)
            ?? throw new InvalidDataException("The ASI font-profile binding is empty.");
        AsiFontProfileBindingService.Validate(binding);
        return binding;
    }
}
