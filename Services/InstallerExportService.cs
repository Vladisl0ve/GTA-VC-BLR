using System.IO;
using System.Security.Cryptography;
using System.Text;
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
            var payloads = StagePayload(projectSnapshot, profile, stageDirectory);
            File.Copy(belarusianLanguagePath, Path.Combine(buildDirectory, "Belarusian.isl"));
            File.Copy(noticesPath, Path.Combine(buildDirectory, "THIRD-PARTY-NOTICES.txt"));
            var template = await File.ReadAllTextAsync(templatePath, Encoding.UTF8, cancellationToken);
            var script = GenerateScript(template, profile, payloads);
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

    private static List<StagedPayload> StagePayload(
        EditorProject project,
        InstallerProfile profile,
        string stageDirectory)
    {
        using var gxtStream = new MemoryStream();
        project.GxtManager.WriteGXT(gxtStream);
        var payloads = new List<StagedPayload>
        {
            Stage(stageDirectory, "generated-gxt.bin", "TEXT\\BELARUS.GXT", gxtStream.ToArray()),
            Stage(stageDirectory, "generated-font-root.bin", "FONTB.TXD", project.AttachedTxd!.Data),
            Stage(stageDirectory, "generated-font-models.bin", "MODELS\\FONTS.TXD", project.AttachedTxd.Data),
        };

        foreach (var asset in profile.Assets)
        {
            payloads.Add(Stage(
                stageDirectory,
                $"asset-{asset.Id:N}.bin",
                asset.DestinationPath,
                asset.Data));
        }

        return payloads;
    }

    private static StagedPayload Stage(
        string stageDirectory,
        string stageName,
        string destinationPath,
        byte[] data)
    {
        var path = Path.Combine(stageDirectory, stageName);
        File.WriteAllBytes(path, data);
        return new StagedPayload(
            stageName,
            destinationPath,
            Convert.ToHexStringLower(SHA256.HashData(data)));
    }

    private static string GenerateScript(
        string template,
        InstallerProfile profile,
        IReadOnlyList<StagedPayload> payloads)
    {
        var fileEntries = new StringBuilder();
        foreach (var payload in payloads)
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
                .Append("\"; Flags: ignoreversion overwritereadonly uninsneveruninstall; BeforeInstall: BackupPayload('")
                .Append(EscapePascal(payload.DestinationPath))
                .Append("'); AfterInstall: RecordPayload('")
                .Append(EscapePascal(payload.DestinationPath))
                .Append("', '")
                .Append(payload.Sha256)
                .AppendLine("')");
        }

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

    private static string Escape(string value) => value.Replace("\"", "\"\"");

    private static string EscapePascal(string value) => value.Replace("'", "''");

    private static string EscapeDirective(string value) =>
        value.Replace("{", "{{", StringComparison.Ordinal);

    private sealed record StagedPayload(
        string StageName,
        string DestinationPath,
        string Sha256);
}
