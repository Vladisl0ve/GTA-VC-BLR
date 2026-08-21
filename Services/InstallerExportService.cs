using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public interface IInstallerExportService
{
    Task BuildAsync(
        EditorProject projectSnapshot,
        string targetPath,
        CancellationToken cancellationToken);
}

public sealed class InnoInstallerExportService : IInstallerExportService
{
    private const string TemplateRelativePath = "Assets/Installer/InstallerTemplate.iss";
    private const string BelarusianLanguageRelativePath = "Assets/Installer/Belarusian.isl";
    private const string NoticesRelativePath = "Assets/Installer/THIRD-PARTY-NOTICES.txt";
    private const string WelcomeImageRelativePath = "Assets/Installer/Welcome.png";

    private static readonly byte[] BelarusianLanguageIniBytes =
        "[Belarusian]\r\nEnabled=1\r\n"u8.ToArray();

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly IInstallerCompiler _compiler;
    private readonly string _assetRoot;

    public InnoInstallerExportService(
        IInstallerCompiler? compiler = null,
        string? assetRoot = null)
    {
        _compiler = compiler ?? new InnoInstallerCompiler();
        _assetRoot = assetRoot ?? AppContext.BaseDirectory;
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
        InstallerProfileValidator.Validate(profile);
        var fullTargetPath = Path.GetFullPath(targetPath);
        var targetDirectory = Path.GetDirectoryName(fullTargetPath)
            ?? throw new InvalidOperationException(LocalizationProvider.Current.Get("Installer.TargetDirectoryUnknown"));
        var templatePath = ResolveAsset(TemplateRelativePath);
        var belarusianLanguagePath = ResolveAsset(BelarusianLanguageRelativePath);
        var noticesPath = ResolveAsset(NoticesRelativePath);
        var welcomeImagePath = ResolveAsset(WelcomeImageRelativePath);

        Directory.CreateDirectory(targetDirectory);
        var buildDirectory = Path.Combine(
            Path.GetTempPath(),
            "GTA GXT Editor",
            "InstallerBuild",
            Guid.NewGuid().ToString("N"));
        var stageDirectory = Path.Combine(buildDirectory, "payload");
        var outputDirectory = Path.Combine(buildDirectory, "output");
        Directory.CreateDirectory(stageDirectory);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var package = StagePayload(projectSnapshot, profile, stageDirectory);
            File.Copy(belarusianLanguagePath, Path.Combine(buildDirectory, "Belarusian.isl"));
            File.Copy(noticesPath, Path.Combine(buildDirectory, "THIRD-PARTY-NOTICES.txt"));
            File.Copy(welcomeImagePath, Path.Combine(buildDirectory, "Welcome.png"));
            var template = await File.ReadAllTextAsync(templatePath, Encoding.UTF8, cancellationToken);
            var script = GenerateScript(template, profile, package);
            var scriptPath = Path.Combine(buildDirectory, "installer.iss");
            await File.WriteAllTextAsync(
                scriptPath,
                script,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);

            var outputBaseName = Path.GetFileNameWithoutExtension(profile.OutputFileName);
            var compiledPath = await _compiler.CompileAsync(
                scriptPath,
                outputDirectory,
                outputBaseName,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var temporaryTarget = Path.Combine(
                targetDirectory,
                $".{Path.GetFileName(fullTargetPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.Copy(compiledPath, temporaryTarget, overwrite: false);
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

    private string ResolveAsset(string relativePath)
    {
        var path = Path.Combine(_assetRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                LocalizationProvider.Current.Format("Installer.AssetMissing", relativePath),
                path);
        }

        return path;
    }

    private static StagedInstallerPackage StagePayload(
        EditorProject project,
        InstallerProfile profile,
        string stageDirectory)
    {
        using var gxtStream = new MemoryStream();
        project.GxtManager.WriteGXT(gxtStream);
        var payloads = new List<StagedPayload>
        {
            Stage(
                stageDirectory,
                "generated-gxt.bin",
                "TEXT\\BELARUS.GXT",
                "core",
                backupOriginal: false,
                data: gxtStream.ToArray()),
            Stage(
                stageDirectory,
                "generated-font-models.bin",
                "MODELS\\FONTS.TXD",
                "core",
                backupOriginal: true,
                data: project.AttachedTxd!.Data),
            Stage(
                stageDirectory,
                "generated-belarusian-language-ini.bin",
                "BelarusianLanguage.ini",
                "core",
                backupOriginal: false,
                data: BelarusianLanguageIniBytes),
        };

        foreach (var asset in profile.Assets)
        {
            payloads.Add(Stage(
                stageDirectory,
                $"asset-{asset.Id:N}.bin",
                asset.DestinationPath,
                asset.Role == InstallerAssetRole.SilentPatch ? "silentpatch" : "core",
                asset.Role == InstallerAssetRole.SilentPatch &&
                Path.GetExtension(asset.DestinationPath).Equals(".ipl", StringComparison.OrdinalIgnoreCase),
                asset.Data));
        }

        var coreManifest = StageManifest(
            stageDirectory,
            "manifest-core.json",
            profile,
            payloads.Where(payload => payload.Component == "core").ToArray(),
            ["core"]);
        var fullManifest = StageManifest(
            stageDirectory,
            "manifest-full.json",
            profile,
            payloads,
            ["core", "silentpatch"]);
        return new StagedInstallerPackage(payloads, coreManifest, fullManifest);
    }

    private static StagedPayload Stage(
        string stageDirectory,
        string stageName,
        string destinationPath,
        string component,
        bool backupOriginal,
        byte[] data)
    {
        var path = Path.Combine(stageDirectory, stageName);
        File.WriteAllBytes(path, data);
        return new StagedPayload(
            stageName,
            destinationPath,
            component,
            backupOriginal,
            Convert.ToHexStringLower(SHA256.HashData(data)));
    }

    private static StagedManifest StageManifest(
        string stageDirectory,
        string stageName,
        InstallerProfile profile,
        IReadOnlyList<StagedPayload> payloads,
        string[] selectedComponents)
    {
        var manifest = new
        {
            schemaVersion = 1,
            installId = profile.ProductId,
            product = new
            {
                name = profile.Name,
                version = profile.Version,
                publisher = profile.Publisher,
            },
            selectedComponents,
            files = payloads.Select(payload => new
            {
                path = payload.DestinationPath,
                component = payload.Component,
                sha256 = payload.Sha256,
                backupOriginal = payload.BackupOriginal,
                backupPath = payload.BackupOriginal ? payload.DestinationPath : null,
            }),
        };
        var path = Path.Combine(stageDirectory, stageName);
        var data = JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJsonOptions);
        File.WriteAllBytes(path, data);
        return new StagedManifest(stageName, Convert.ToHexStringLower(SHA256.HashData(data)));
    }

    private static string GenerateScript(
        string template,
        InstallerProfile profile,
        StagedInstallerPackage package)
    {
        var fileEntries = new StringBuilder();
        foreach (var payload in package.Payloads)
        {
            var destinationDirectory = Path.GetDirectoryName(payload.DestinationPath);
            var destinationName = Path.GetFileName(payload.DestinationPath);
            var destination = string.IsNullOrEmpty(destinationDirectory)
                ? "{app}"
                : $"{{app}}\\{Escape(destinationDirectory)}";
            fileEntries.Append("Source: \"payload\\")
                .Append(Escape(payload.StageName))
                .Append("\"; DestDir: \"")
                .Append(destination)
                .Append("\"; DestName: \"")
                .Append(Escape(destinationName))
                .Append("\"; Components: ")
                .Append(payload.Component)
                .Append("; Flags: ignoreversion overwritereadonly uninsneveruninstall; BeforeInstall: BackupPayload('")
                .Append(EscapePascal(payload.DestinationPath))
                .Append("', ")
                .Append(payload.BackupOriginal ? "True" : "False")
                .Append("); AfterInstall: RecordPayload('")
                .Append(EscapePascal(payload.DestinationPath))
                .Append("', '")
                .Append(payload.Sha256)
                .AppendLine("')");
        }

        AppendManifestEntry(
            fileEntries,
            package.CoreManifest,
            profile.ProductId,
            "not IsSilentPatchSelected");
        AppendManifestEntry(
            fileEntries,
            package.FullManifest,
            profile.ProductId,
            "IsSilentPatchSelected");

        var publisherDirective = string.IsNullOrWhiteSpace(profile.Publisher)
            ? string.Empty
            : $"AppPublisher={EscapeDirective(profile.Publisher)}";
        return template
            .Replace("@@APP_ID@@", $"{{{{{profile.ProductId:D}}}", StringComparison.Ordinal)
            .Replace("@@PRODUCT_ID@@", profile.ProductId.ToString("D"), StringComparison.Ordinal)
            .Replace("@@APP_NAME@@", EscapeDirective(profile.Name), StringComparison.Ordinal)
            .Replace("@@APP_VERSION@@", EscapeDirective(profile.Version), StringComparison.Ordinal)
            .Replace("@@PUBLISHER_DIRECTIVE@@", publisherDirective, StringComparison.Ordinal)
            .Replace("@@FILE_ENTRIES@@", fileEntries.ToString().TrimEnd(), StringComparison.Ordinal);
    }

    private static void AppendManifestEntry(
        StringBuilder fileEntries,
        StagedManifest manifest,
        Guid productId,
        string check)
    {
        var destinationPath = $"_BelarusianModBackup\\{productId:D}\\manifest.json";
        fileEntries.Append("Source: \"payload\\")
            .Append(Escape(manifest.StageName))
            .Append("\"; DestDir: \"{app}\\_BelarusianModBackup\\")
            .Append(productId.ToString("D"))
            .Append("\"; DestName: \"manifest.json\"; Components: core; Check: ")
            .Append(check)
            .Append("; Flags: ignoreversion overwritereadonly uninsneveruninstall; BeforeInstall: BackupPayload('")
            .Append(EscapePascal(destinationPath))
            .Append("', False); AfterInstall: RecordPayload('")
            .Append(EscapePascal(destinationPath))
            .Append("', '")
            .Append(manifest.Sha256)
            .AppendLine("')");
    }

    private static string Escape(string value) => value.Replace("\"", "\"\"");

    private static string EscapePascal(string value) => value.Replace("'", "''");

    private static string EscapeDirective(string value) =>
        value.Replace("{", "{{", StringComparison.Ordinal);

    private sealed record StagedPayload(
        string StageName,
        string DestinationPath,
        string Component,
        bool BackupOriginal,
        string Sha256);

    private sealed record StagedInstallerPackage(
        IReadOnlyList<StagedPayload> Payloads,
        StagedManifest CoreManifest,
        StagedManifest FullManifest);

    private sealed record StagedManifest(string StageName, string Sha256);
}
