using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Tests;

[TestClass]
public sealed class InstallerExportServiceTests
{
    private string _testDirectory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"gta-gxt-installer-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task BuildAsync_StagesMinimalPayloadAndWritesUtf8Script()
    {
        var compiler = new InspectingCompiler();
        var service = new InnoInstallerExportService(compiler, AppContext.BaseDirectory);
        var target = Path.Combine(_testDirectory, "output", "Belarusian Setup.exe");

        await service.BuildAsync(CreateProject(), target, CancellationToken.None);

        Assert.IsTrue(File.Exists(target));
        CollectionAssert.AreEqual(InspectingCompiler.CompiledBytes, File.ReadAllBytes(target));
        Assert.IsNotNull(compiler.ScriptBytes);
        Assert.IsFalse(compiler.ScriptBytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        var script = Encoding.UTF8.GetString(compiler.ScriptBytes);
        StringAssert.Contains(script, "TEXT\\BELARUS.GXT");
        StringAssert.Contains(script, "MODELS\\FONTS.TXD");
        StringAssert.Contains(script, "BelarusianLanguage.asi");
        StringAssert.Contains(script, "BelarusianLanguage.ini");
        StringAssert.Contains(script, "BackupPayload('BelarusianLanguage.ini', False)");
        StringAssert.Contains(script, "WizardImageFile=Welcome.bmp");
        StringAssert.Contains(
            script,
            "Name: \"{app}\\uninstall_BLR.exe\"; Filename: \"{uninstallexe}\"");
        StringAssert.Contains(
            script,
            "Type: files; Name: \"{app}\\uninstall_BLR.exe.lnk\"");
        Assert.IsFalse(script.Contains("FileChangedPrompt", StringComparison.Ordinal));
        Assert.IsFalse(script.Contains("ConflictsSaved", StringComparison.Ordinal));
        StringAssert.Contains(script, "if not Changed then");
        StringAssert.Contains(script, "procedure InitializeWizard");
        Assert.IsTrue(compiler.WelcomeImageStaged);
        Assert.IsFalse(script.Contains("FONTB.TXD", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("dinput8.dll", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(script, "SilentPatchVC.asi");
        StringAssert.Contains(script, "SilentPatchVC.ini");
        StringAssert.Contains(script, "data\\maps\\washints\\washints.ipl");
        StringAssert.Contains(script, "Name: \"silentpatch\"");
        StringAssert.Contains(script, "AppendDefaultDirName=no");
        StringAssert.Contains(script, "DisableDirPage=no");
        StringAssert.Contains(script, "DirExistsWarning=no");
        StringAssert.Contains(script, "UsePreviousAppDir=yes");
        StringAssert.Contains(script, "Check: not IsSilentPatchSelected");
        StringAssert.Contains(script, "Check: IsSilentPatchSelected");
        StringAssert.Contains(script, "BackupPayload('MODELS\\FONTS.TXD', True)");
        StringAssert.Contains(script, "BackupPayload('SilentPatchVC.asi', False)");
        StringAssert.Contains(script, "BackupPayload('data\\maps\\club\\CLUB.ipl', True)");
        StringAssert.Contains(
            script,
            "{app}\\_BelarusianModBackup\\aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        StringAssert.Contains(
            script,
            "UninstallFilesDir={app}\\_BelarusianMod\\aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\\uninstall");
        StringAssert.Contains(
            script,
            "{app}\\_BelarusianMod\\aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\\licenses");
        StringAssert.Contains(
            script,
            "Result := ExpandConstant('{app}\\_BelarusianMod\\aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee')");
        Assert.IsFalse(
            script.Contains(
                "UninstallFilesDir={commonappdata}\\GTA GXT Editor\\Installations",
                StringComparison.Ordinal));
        StringAssert.Contains(script, "HasLegacyProgramDataState");
        Assert.IsFalse(
            script.Contains(
                "Type: filesandordirs; Name: \"{app}\\_BelarusianMod\\aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\\conflicts\"",
                StringComparison.Ordinal));
        Assert.IsFalse(script.Contains("gta-vc.exe", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(script, "AppName=Belarusian %7 {{VC}");
        Assert.IsFalse(script.Contains("@@", StringComparison.Ordinal));
        CollectionAssert.AreEquivalent(
            new[]
            {
                "asset-00000001000000000000000000000000.bin",
                "asset-00000002000000000000000000000000.bin",
                "asset-00000003000000000000000000000000.bin",
                "asset-00000004000000000000000000000000.bin",
                "asset-00000005000000000000000000000000.bin",
                "asset-00000006000000000000000000000000.bin",
                "asset-00000007000000000000000000000000.bin",
                "asset-00000008000000000000000000000000.bin",
                "asset-00000009000000000000000000000000.bin",
                "asset-0000000a000000000000000000000000.bin",
                "asset-0000000b000000000000000000000000.bin",
                "generated-belarusian-language-ini.bin",
                "generated-font-models.bin",
                "generated-gxt.bin",
                "manifest-core.json",
                "manifest-full.json",
            },
            compiler.StagedFiles);

        var coreManifest = JsonNode.Parse(compiler.StagedContents["manifest-core.json"])!.AsObject();
        var fullManifest = JsonNode.Parse(compiler.StagedContents["manifest-full.json"])!.AsObject();
        Assert.AreEqual(1, coreManifest["schemaVersion"]!.GetValue<int>());
        Assert.AreEqual("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", coreManifest["installId"]!.GetValue<string>());
        CollectionAssert.AreEqual(
            new[] { "core" },
            coreManifest["selectedComponents"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray());
        Assert.HasCount(4, coreManifest["files"]!.AsArray());
        CollectionAssert.AreEqual(
            new[] { "core", "silentpatch" },
            fullManifest["selectedComponents"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray());
        Assert.HasCount(14, fullManifest["files"]!.AsArray());
        var fullFiles = fullManifest["files"]!.AsArray()
            .Select(value => value!.AsObject())
            .ToDictionary(value => value["path"]!.GetValue<string>(), StringComparer.OrdinalIgnoreCase);
        Assert.IsTrue(fullFiles["MODELS\\FONTS.TXD"]["backupOriginal"]!.GetValue<bool>());
        Assert.AreEqual(
            "MODELS\\FONTS.TXD",
            fullFiles["MODELS\\FONTS.TXD"]["backupPath"]!.GetValue<string>());
        Assert.IsFalse(fullFiles["TEXT\\BELARUS.GXT"]["backupOriginal"]!.GetValue<bool>());
        Assert.IsFalse(fullFiles["BelarusianLanguage.ini"]["backupOriginal"]!.GetValue<bool>());
        CollectionAssert.AreEqual(
            "[Belarusian]\r\nEnabled=1\r\n"u8.ToArray(),
            compiler.StagedContents["generated-belarusian-language-ini.bin"]);
        Assert.IsFalse(fullFiles["SilentPatchVC.asi"]["backupOriginal"]!.GetValue<bool>());
        Assert.IsFalse(fullFiles["SilentPatchVC.ini"]["backupOriginal"]!.GetValue<bool>());
        Assert.HasCount(
            8,
            fullFiles.Values.Where(value =>
                value["component"]!.GetValue<string>() == "silentpatch" &&
                value["backupOriginal"]!.GetValue<bool>()));
    }

    [TestMethod]
    public async Task BuildAsync_EmptyInstallerDraft_IsRejected()
    {
        var compiler = new InspectingCompiler();
        var service = new InnoInstallerExportService(compiler, AppContext.BaseDirectory);
        var project = CreateProject();
        project.InstallerProfile!.Assets.Clear();
        var target = Path.Combine(_testDirectory, "incomplete.exe");

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.BuildAsync(project, target, CancellationToken.None));

        Assert.IsFalse(File.Exists(target));
        Assert.IsNull(compiler.ScriptBytes);
    }

    [TestMethod]
    public async Task BuildAsync_CompilerFailure_PreservesExistingTargetAndCleansStaging()
    {
        var compiler = new FailingCompiler();
        var service = new InnoInstallerExportService(compiler, AppContext.BaseDirectory);
        var target = Path.Combine(_testDirectory, "existing.exe");
        await File.WriteAllBytesAsync(target, [9, 9, 9]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.BuildAsync(CreateProject(), target, CancellationToken.None));

        CollectionAssert.AreEqual(new byte[] { 9, 9, 9 }, File.ReadAllBytes(target));
        Assert.IsNotNull(compiler.BuildDirectory);
        Assert.IsFalse(Directory.Exists(compiler.BuildDirectory));
    }

    [TestMethod]
    public async Task BuildAsync_Cancellation_ProducesNoTargetAndCleansStaging()
    {
        using var cancellation = new CancellationTokenSource();
        var compiler = new CancelingCompiler(cancellation);
        var service = new InnoInstallerExportService(compiler, AppContext.BaseDirectory);
        var target = Path.Combine(_testDirectory, "cancelled.exe");

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.BuildAsync(CreateProject(), target, cancellation.Token));

        Assert.IsFalse(File.Exists(target));
        Assert.IsNotNull(compiler.BuildDirectory);
        Assert.IsFalse(Directory.Exists(compiler.BuildDirectory));
    }

    [TestMethod]
    public async Task BuildAsync_BundledCompiler_CreatesWindowsExecutable()
    {
        var compilerPath = Path.Combine(
            AppContext.BaseDirectory,
            "Tools",
            "InnoSetup",
            "7.0.2-x86",
            "ISCC.exe");
        Assert.IsTrue(File.Exists(compilerPath), $"Missing bundled compiler: {compilerPath}");
        var service = new InnoInstallerExportService(
            new InnoInstallerCompiler(compilerPath),
            AppContext.BaseDirectory);
        var target = Path.Combine(_testDirectory, "compiled-setup.exe");

        await service.BuildAsync(CreateProject(), target, CancellationToken.None);

        var setup = await File.ReadAllBytesAsync(target);
        Assert.IsGreaterThan(1024, setup.Length);
        Assert.AreEqual((byte)'M', setup[0]);
        Assert.AreEqual((byte)'Z', setup[1]);
        var retainedSetupPath = Environment.GetEnvironmentVariable("GTA_GXT_SMOKE_SETUP_PATH");
        if (!string.IsNullOrWhiteSpace(retainedSetupPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(retainedSetupPath))!);
            File.Copy(target, retainedSetupPath, overwrite: true);
        }
    }

    private static EditorProject CreateProject()
    {
        var manager = GxtManagerFactory.Create(
            GXTType.GtaViceCity,
            sourceName: "american.gxt",
            sourceTexts: ["Hello"],
            language: GxtLanguage.Belarusian);
        manager.AddGXTEntry("HELLO", "Прывітанне");
        return new EditorProject
        {
            GxtSourceName = "american.gxt",
            GameType = GXTType.GtaViceCity,
            GxtManager = manager,
            AttachedTxd = new TxdAttachment
            {
                Id = Guid.NewGuid(),
                OriginalFileName = "fonts.txd",
                DisplayName = "fonts",
                Data = [10, 20, 30],
                Document = new TxdDocument { RenderWareVersion = 0, Textures = [] },
            },
            InstallerProfile = new InstallerProfile
            {
                ProductId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                Name = "Belarusian %7 {VC}",
                Version = "1.2.3",
                Publisher = "Belarusian Games",
                OutputFileName = "Setup.exe",
                Assets =
                [
                    CreateBinary(1, InstallerAssetRole.MainAsi, "BelarusianLanguage.asi"),
                    .. InstallerProfileValidator.SilentPatchDestinations.Select((destination, index) =>
                        CreateSilentPatchAsset(index + 2, destination)),
                ],
            },
        };
    }

    private static InstallerAsset CreateBinary(
        int id,
        InstallerAssetRole role,
        string destination) => new()
    {
        Id = new Guid(id, 0, 0, new byte[8]),
        Role = role,
        OriginalFileName = destination,
        DestinationPath = destination,
        Data = CreateX86PeImage(),
    };

    private static InstallerAsset CreateSilentPatchAsset(int id, string destination) =>
        Path.GetExtension(destination).Equals(".asi", StringComparison.OrdinalIgnoreCase)
            ? CreateBinary(id, InstallerAssetRole.SilentPatch, destination)
            : new InstallerAsset
            {
                Id = new Guid(id, 0, 0, new byte[8]),
                Role = InstallerAssetRole.SilentPatch,
                OriginalFileName = Path.GetFileName(destination),
                DestinationPath = destination,
                Data = Encoding.UTF8.GetBytes(destination),
            };

    private static byte[] CreateX86PeImage()
    {
        var data = new byte[128];
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x3C, 4), 0x40);
        "PE\0\0"u8.CopyTo(data.AsSpan(0x40));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x44, 2), 0x014C);
        return data;
    }

    private sealed class InspectingCompiler : IInstallerCompiler
    {
        public static readonly byte[] CompiledBytes = [77, 90, 1, 2, 3];

        public byte[]? ScriptBytes { get; private set; }

        public bool WelcomeImageStaged { get; private set; }

        public string[] StagedFiles { get; private set; } = [];

        public Dictionary<string, byte[]> StagedContents { get; private set; } =
            new(StringComparer.Ordinal);

        public Task<string> CompileAsync(
            string scriptPath,
            string outputDirectory,
            string outputBaseName,
            CancellationToken cancellationToken)
        {
            ScriptBytes = File.ReadAllBytes(scriptPath);
            WelcomeImageStaged = File.Exists(
                Path.Combine(Path.GetDirectoryName(scriptPath)!, "Welcome.bmp"));
            StagedFiles = Directory.GetFiles(
                    Path.Combine(Path.GetDirectoryName(scriptPath)!, "payload"))
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal)
                .ToArray()!;
            StagedContents = Directory.GetFiles(
                    Path.Combine(Path.GetDirectoryName(scriptPath)!, "payload"))
                .ToDictionary(
                    path => Path.GetFileName(path)!,
                    File.ReadAllBytes,
                    StringComparer.Ordinal)!;
            Directory.CreateDirectory(outputDirectory);
            var output = Path.Combine(outputDirectory, $"{outputBaseName}.exe");
            File.WriteAllBytes(output, CompiledBytes);
            return Task.FromResult(output);
        }
    }

    private sealed class FailingCompiler : IInstallerCompiler
    {
        public string? BuildDirectory { get; private set; }

        public Task<string> CompileAsync(
            string scriptPath,
            string outputDirectory,
            string outputBaseName,
            CancellationToken cancellationToken)
        {
            BuildDirectory = Path.GetDirectoryName(scriptPath);
            throw new InvalidOperationException("compiler failed");
        }
    }

    private sealed class CancelingCompiler(CancellationTokenSource cancellation) : IInstallerCompiler
    {
        public string? BuildDirectory { get; private set; }

        public Task<string> CompileAsync(
            string scriptPath,
            string outputDirectory,
            string outputBaseName,
            CancellationToken cancellationToken)
        {
            BuildDirectory = Path.GetDirectoryName(scriptPath);
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            throw new AssertFailedException("Cancellation was not observed.");
        }
    }
}
