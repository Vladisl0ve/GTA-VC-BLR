using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public interface IReleaseZipExportService
{
    Task BuildAsync(
        EditorProject projectSnapshot,
        string targetPath,
        CancellationToken cancellationToken);
}

public sealed class ReleaseZipExportService : IReleaseZipExportService
{
    public const string EnglishReadMeFileName = "ReadMe.txt";
    public const string BelarusianReadMeFileName = "ПрачытайМяне.txt";

    private readonly IInstallerExportService _installerExportService;

    public ReleaseZipExportService(IInstallerExportService? installerExportService = null)
    {
        _installerExportService = installerExportService ?? new InnoInstallerExportService();
    }

    public static string CreateChecksumFileName(string installerFileName) =>
        installerFileName + ".sha256";

    public static byte[] CreateChecksumFile(string installerFileName, byte[] installerData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerFileName);
        ArgumentNullException.ThrowIfNull(installerData);
        var hash = Convert.ToHexStringLower(SHA256.HashData(installerData));
        return Encoding.UTF8.GetBytes($"{hash}  {installerFileName}\n");
    }

    public async Task BuildAsync(
        EditorProject projectSnapshot,
        string targetPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projectSnapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        if (projectSnapshot.GameType != GXTType.GtaViceCity ||
            projectSnapshot.AttachedTxd is null ||
            projectSnapshot.InstallerProfile is null)
        {
            throw new InvalidOperationException(LocalizationProvider.Current.Get("Installer.ViceCityProjectRequired"));
        }

        var profile = projectSnapshot.InstallerProfile.Clone();
        InstallerProfileValidator.ValidateForReleaseZip(profile);
        var fullTargetPath = Path.GetFullPath(targetPath);
        var targetDirectory = Path.GetDirectoryName(fullTargetPath)
            ?? throw new InvalidOperationException(LocalizationProvider.Current.Get("Installer.TargetDirectoryUnknown"));

        Directory.CreateDirectory(targetDirectory);
        var buildDirectory = Path.Combine(
            Path.GetTempPath(),
            "GTA GXT Editor",
            "ReleaseZipBuild",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(buildDirectory);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var installerPath = Path.Combine(buildDirectory, profile.OutputFileName);
            await _installerExportService.BuildAsync(projectSnapshot, installerPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var installerData = await File.ReadAllBytesAsync(installerPath, cancellationToken);
            var checksumName = CreateChecksumFileName(profile.OutputFileName);
            var checksumData = CreateChecksumFile(profile.OutputFileName, installerData);
            var englishReadMe = profile.ReleaseReadMeEnglish!.Data;
            var belarusianReadMe = profile.ReleaseReadMeBelarusian!.Data;

            var temporaryTarget = Path.Combine(
                targetDirectory,
                $".{Path.GetFileName(fullTargetPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var fileStream = new FileStream(
                                 temporaryTarget,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 4096,
                                 FileOptions.Asynchronous))
                {
                    using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: true);
                    WriteEntry(archive, profile.OutputFileName, installerData);
                    WriteEntry(archive, EnglishReadMeFileName, englishReadMe);
                    WriteEntry(archive, BelarusianReadMeFileName, belarusianReadMe);
                    WriteEntry(archive, checksumName, checksumData);
                }

                File.Move(temporaryTarget, fullTargetPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryTarget))
                {
                    File.Delete(temporaryTarget);
                }
            }
        }
        finally
        {
            if (Directory.Exists(buildDirectory))
            {
                Directory.Delete(buildDirectory, recursive: true);
            }
        }
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] data)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(data);
    }
}
