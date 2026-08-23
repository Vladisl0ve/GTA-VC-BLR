using System.IO;
using System.IO.Compression;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public static class InstallerModArchiveReader
{
    private const int CopyBufferSize = 81920;

    public static InstallerMod Read(
        string archivePath,
        string name,
        bool isRequired,
        InstallerProfile currentProfile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentNullException.ThrowIfNull(currentProfile);

        var remainingFileCount = InstallerProfileValidator.MaximumAssets - currentProfile.PayloadFileCount;
        var remainingPayloadSize = InstallerProfileValidator.MaximumPayloadSize - GetStoredLength(currentProfile);
        if (remainingFileCount <= 0)
        {
            throw new InvalidDataException(LocalizationProvider.Current.Format(
                "Installer.Validation.AssetCount",
                InstallerProfileValidator.MaximumAssets));
        }

        if (remainingPayloadSize < 0)
        {
            throw new InvalidDataException(LocalizationProvider.Current.Get("Installer.Validation.PayloadSize"));
        }

        var files = new List<InstallerModFile>();
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            foreach (var entry in archive.Entries)
            {
                if (IsDirectory(entry))
                {
                    continue;
                }

                if (IsSymbolicLinkOrReparsePoint(entry))
                {
                    throw new InvalidDataException(LocalizationProvider.Current.Format(
                        "Installer.Validation.ModArchiveUnsafeEntry",
                        entry.FullName));
                }

                if (files.Count >= remainingFileCount)
                {
                    throw new InvalidDataException(LocalizationProvider.Current.Format(
                        "Installer.Validation.AssetCount",
                        InstallerProfileValidator.MaximumAssets));
                }

                var destination = InstallerProfileValidator.NormalizeDestinationPath(entry.FullName);
                if (!destinations.Add(destination))
                {
                    throw new InvalidDataException(LocalizationProvider.Current.Format(
                        "Installer.Validation.DuplicateTarget",
                        destination));
                }

                var data = ReadEntry(entry, remainingPayloadSize);
                remainingPayloadSize -= data.LongLength;
                files.Add(new InstallerModFile
                {
                    Id = Guid.NewGuid(),
                    OriginalFileName = Path.GetFileName(destination),
                    DestinationPath = destination,
                    Data = data,
                });
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new InvalidDataException(
                LocalizationProvider.Current.Format("Installer.Validation.ModArchive", Path.GetFileName(archivePath)),
                exception);
        }

        if (files.Count == 0)
        {
            throw new InvalidDataException(LocalizationProvider.Current.Get("Installer.Validation.EmptyModArchive"));
        }

        var mod = new InstallerMod
        {
            Id = Guid.NewGuid(),
            Name = name,
            IsRequired = isRequired,
            Files = files,
        };
        var candidate = currentProfile.Clone();
        candidate.Mods.Add(mod.Clone());
        InstallerProfileValidator.ValidatePayloadForStorage(candidate);
        return candidate.Mods[^1].Clone();
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry, long maximumLength)
    {
        if (entry.Length > maximumLength || entry.Length > int.MaxValue)
        {
            throw new InvalidDataException(LocalizationProvider.Current.Get("Installer.Validation.PayloadSize"));
        }

        using var input = entry.Open();
        using var output = new MemoryStream((int)entry.Length);
        var buffer = new byte[CopyBufferSize];
        long totalLength = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
        {
            totalLength = checked(totalLength + read);
            if (totalLength > maximumLength)
            {
                throw new InvalidDataException(LocalizationProvider.Current.Get("Installer.Validation.PayloadSize"));
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static long GetStoredLength(InstallerProfile profile)
    {
        long length = 0;
        foreach (var asset in profile.Assets)
        {
            length = checked(length + asset.Data.LongLength);
        }

        foreach (var file in profile.Mods.SelectMany(mod => mod.Files))
        {
            length = checked(length + file.Data.LongLength);
        }

        foreach (var document in profile.EnumerateReleaseDocuments())
        {
            length = checked(length + document.Data.LongLength);
        }

        return length;
    }

    private static bool IsDirectory(ZipArchiveEntry entry) =>
        entry.FullName.EndsWith('/') ||
        entry.FullName.EndsWith('\\');

    private static bool IsSymbolicLinkOrReparsePoint(ZipArchiveEntry entry)
    {
        const int UnixFileTypeMask = 0xF000;
        const int UnixSymbolicLink = 0xA000;
        var unixMode = (entry.ExternalAttributes >> 16) & UnixFileTypeMask;
        return unixMode == UnixSymbolicLink ||
               (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0;
    }
}
