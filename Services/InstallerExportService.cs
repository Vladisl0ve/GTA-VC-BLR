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
    private const string WelcomeImageRelativePath = "Assets/Installer/Welcome.bmp";

    private static readonly byte[] BelarusianLanguageIniBytes =
        "[Belarusian]\r\nEnabled=1\r\n"u8.ToArray();

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
            File.Copy(welcomeImagePath, Path.Combine(buildDirectory, "Welcome.bmp"));
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
                ShouldBackupOriginal(asset),
                asset.Data));
        }

        foreach (var mod in profile.Mods)
        {
            var component = GetModComponentName(mod.Id);
            foreach (var file in mod.Files)
            {
                payloads.Add(Stage(
                    stageDirectory,
                    $"mod-{file.Id:N}.bin",
                    file.DestinationPath,
                    component,
                    backupOriginal: true,
                    file.Data));
            }
        }

        return new StagedInstallerPackage(payloads);
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

        var publisherDirective = string.IsNullOrWhiteSpace(profile.Publisher)
            ? string.Empty
            : $"AppPublisher={EscapeDirective(profile.Publisher)}";
        var modComponents = GenerateModComponents(profile);
        var modMessages = GenerateModMessages(profile);
        var manifestBuilder = GenerateManifestBuilder(profile, package.Payloads);
        return template
            .Replace("@@APP_ID@@", $"{{{{{profile.ProductId:D}}}", StringComparison.Ordinal)
            .Replace("@@PRODUCT_ID@@", profile.ProductId.ToString("D"), StringComparison.Ordinal)
            .Replace("@@APP_NAME@@", EscapeDirective(profile.Name), StringComparison.Ordinal)
            .Replace("@@APP_VERSION@@", EscapeDirective(profile.Version), StringComparison.Ordinal)
            .Replace("@@PUBLISHER_DIRECTIVE@@", publisherDirective, StringComparison.Ordinal)
            .Replace("@@FILE_ENTRIES@@", fileEntries.ToString().TrimEnd(), StringComparison.Ordinal)
            .Replace("@@MOD_COMPONENT_ENTRIES@@", modComponents, StringComparison.Ordinal)
            .Replace("@@MOD_CUSTOM_MESSAGES@@", modMessages, StringComparison.Ordinal)
            .Replace("@@MANIFEST_BUILDER@@", manifestBuilder, StringComparison.Ordinal);
    }

    private static string GenerateModComponents(InstallerProfile profile)
    {
        var builder = new StringBuilder();
        foreach (var mod in profile.Mods)
        {
            var component = GetModComponentName(mod.Id);
            builder.Append("Name: \"")
                .Append(component)
                .Append("\"; Description: \"{cm:")
                .Append(GetModMessageName(mod.Id))
                .Append("}\"; Types: ")
                .Append(mod.IsRequired ? "full compact custom; Flags: fixed" : "full")
                .AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string GenerateModMessages(InstallerProfile profile)
    {
        var builder = new StringBuilder();
        foreach (var mod in profile.Mods)
        {
            var key = GetModMessageName(mod.Id);
            var name = EscapeDirective(mod.Name);
            builder.Append("english.")
                .Append(key)
                .Append('=')
                .Append(name)
                .Append(mod.IsRequired ? " (required)" : " (optional)")
                .AppendLine();
            builder.Append("belarusian.")
                .Append(key)
                .Append('=')
                .Append(name)
                .Append(mod.IsRequired ? " (абавязкова)" : " (неабавязкова)")
                .AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string GenerateManifestBuilder(
        InstallerProfile profile,
        IReadOnlyList<StagedPayload> payloads)
    {
        var prefix = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            installId = profile.ProductId,
            product = new
            {
                name = profile.Name,
                version = profile.Version,
                publisher = profile.Publisher,
            },
        });
        prefix = prefix[..^1];

        var builder = new StringBuilder();
        builder.AppendLine("procedure AppendManifestValue(var Target, Separator: String; const Value: String);")
            .AppendLine("begin")
            .AppendLine("  Target := Target + Separator + Value;")
            .AppendLine("  Separator := ',';")
            .AppendLine("end;")
            .AppendLine()
            .AppendLine("procedure WriteSelectedManifest;")
            .AppendLine("var")
            .AppendLine("  SelectedComponents, Files, Separator, Manifest, RelativePath, FileName: String;")
            .AppendLine("  Lines: TArrayOfString;")
            .AppendLine("begin")
            .AppendLine("  SelectedComponents := '\"core\"';")
            .AppendLine("  Separator := ',';")
            .AppendLine("  if WizardIsComponentSelected('silentpatch') then")
            .AppendLine("    AppendManifestValue(SelectedComponents, Separator, '\"silentpatch\"');");

        foreach (var mod in profile.Mods)
        {
            var component = GetModComponentName(mod.Id);
            builder.Append("  if WizardIsComponentSelected('")
                .Append(component)
                .AppendLine("') then")
                .Append("    AppendManifestValue(SelectedComponents, Separator, '")
                .Append(EscapePascal(JsonSerializer.Serialize(component)))
                .AppendLine("');");
        }

        builder.AppendLine("  Files := '';")
            .AppendLine("  Separator := '';");
        foreach (var payload in payloads)
        {
            var fileJson = JsonSerializer.Serialize(new
            {
                path = payload.DestinationPath,
                component = payload.Component,
                sha256 = payload.Sha256,
                backupOriginal = payload.BackupOriginal,
                backupPath = payload.BackupOriginal ? payload.DestinationPath : null,
            });
            if (payload.Component == "core")
            {
                builder.Append("  AppendManifestValue(Files, Separator, '")
                    .Append(EscapePascal(fileJson))
                    .AppendLine("');");
            }
            else
            {
                builder.Append("  if WizardIsComponentSelected('")
                    .Append(payload.Component)
                    .AppendLine("') then")
                    .Append("    AppendManifestValue(Files, Separator, '")
                    .Append(EscapePascal(fileJson))
                    .AppendLine("');");
            }
        }

        var relativePath = $"_BelarusianModBackup\\{profile.ProductId:D}\\manifest.json";
        builder.Append("  Manifest := '")
            .Append(EscapePascal(prefix))
            .AppendLine(",\"selectedComponents\":[' + SelectedComponents + '],\"files\":[' + Files + ']}';")
            .Append("  RelativePath := '")
            .Append(EscapePascal(relativePath))
            .AppendLine("';")
            .AppendLine("  FileName := DestinationFileFor(RelativePath);")
            .AppendLine("  BackupPayload(RelativePath, False);")
            .AppendLine("  ForceDirectories(ExtractFileDir(FileName));")
            .AppendLine("  SetArrayLength(Lines, 1);")
            .AppendLine("  Lines[0] := Manifest;")
            .AppendLine("  if not SaveStringsToUTF8FileWithoutBOM(FileName, Lines, False) then")
            .AppendLine("    RaiseException(FmtMessage(CustomMessage('BackupFailed'), [RelativePath]));")
            .AppendLine("  RecordPayload(RelativePath, GetSHA256OfFile(FileName));")
            .AppendLine("end;");
        return builder.ToString().TrimEnd();
    }

    private static string GetModComponentName(Guid id) => $"mod_{id:N}";

    private static string GetModMessageName(Guid id) => $"ComponentMod_{id:N}";

    private static bool ShouldBackupOriginal(InstallerAsset asset) =>
        asset.Role is InstallerAssetRole.ModelsArchive or InstallerAssetRole.GameTxd ||
        asset.Role == InstallerAssetRole.SilentPatch &&
        Path.GetExtension(asset.DestinationPath).Equals(".ipl", StringComparison.OrdinalIgnoreCase);

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
        IReadOnlyList<StagedPayload> Payloads);
}
