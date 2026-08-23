using System.Buffers.Binary;
using System.Diagnostics;
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
        StringAssert.Contains(script, "MODELS\\gta3.img");
        StringAssert.Contains(script, "MODELS\\gta3.dir");
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
        StringAssert.Contains(script, "ddraw.dll");
        StringAssert.Contains(script, "data\\maps\\washints\\washints.ipl");
        StringAssert.Contains(script, "Name: \"silentpatch\"");
        StringAssert.Contains(script, "AppendDefaultDirName=no");
        StringAssert.Contains(script, "DisableDirPage=no");
        StringAssert.Contains(script, "DirExistsWarning=no");
        StringAssert.Contains(script, "UsePreviousAppDir=yes");
        StringAssert.Contains(script, "procedure WriteSelectedManifest");
        StringAssert.Contains(script, "WizardIsComponentSelected('silentpatch')");
        StringAssert.Contains(script, "BackupPayload(RelativePath, False)");
        StringAssert.Contains(script, "RecordPayload(RelativePath, GetSHA256OfFile(FileName))");
        StringAssert.Contains(script, "BackupPayload('MODELS\\FONTS.TXD', True)");
        StringAssert.Contains(script, "BackupPayload('MODELS\\gta3.img', True)");
        StringAssert.Contains(script, "BackupPayload('MODELS\\gta3.dir', True)");
        StringAssert.Contains(script, "BackupPayload('SilentPatchVC.asi', False)");
        StringAssert.Contains(script, "BackupPayload('ddraw.dll', False)");
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
                "asset-0000000c000000000000000000000000.bin",
                "asset-0000000d000000000000000000000000.bin",
                "asset-0000000e000000000000000000000000.bin",
                "generated-belarusian-language-ini.bin",
                "generated-font-models.bin",
                "generated-gxt.bin",
            },
            compiler.StagedFiles);
        StringAssert.Contains(script, "\"schemaVersion\":1");
        StringAssert.Contains(script, "\"installId\":\"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\"");
        StringAssert.Contains(script, "\"path\":\"MODELS\\\\FONTS.TXD\",\"component\":\"core\"");
        StringAssert.Contains(script, "\"backupOriginal\":true,\"backupPath\":\"MODELS\\\\gta3.img\"");
        StringAssert.Contains(script, "\"path\":\"TEXT\\\\BELARUS.GXT\",\"component\":\"core\"");
        StringAssert.Contains(script, "\"backupOriginal\":false,\"backupPath\":null");
        CollectionAssert.AreEqual(
            "[Belarusian]\r\nEnabled=1\r\n"u8.ToArray(),
            compiler.StagedContents["generated-belarusian-language-ini.bin"]);
    }

    [TestMethod]
    public async Task BuildAsync_GameTxd_IsCorePayloadWithOriginalBackup()
    {
        var compiler = new InspectingCompiler();
        var service = new InnoInstallerExportService(compiler, AppContext.BaseDirectory);
        var project = CreateProject();
        project.InstallerProfile!.Assets.Add(CreateGameTxd(15, "loadsc0.txd"));
        var target = Path.Combine(_testDirectory, "output", "Belarusian Setup.exe");

        await service.BuildAsync(project, target, CancellationToken.None);

        var script = Encoding.UTF8.GetString(compiler.ScriptBytes!);
        StringAssert.Contains(script, "txd\\loadsc0.txd");
        StringAssert.Contains(script, "BackupPayload('txd\\loadsc0.txd', True)");
        CollectionAssert.Contains(compiler.StagedFiles, "asset-0000000f000000000000000000000000.bin");

        StringAssert.Contains(
            script,
            "\"path\":\"txd\\\\loadsc0.txd\",\"component\":\"core\"");
        StringAssert.Contains(
            script,
            "\"backupOriginal\":true,\"backupPath\":\"txd\\\\loadsc0.txd\"");
    }

    [TestMethod]
    public async Task BuildAsync_CustomMods_AreStableComponentsWithOriginalBackup()
    {
        var compiler = new InspectingCompiler();
        var service = new InnoInstallerExportService(compiler, AppContext.BaseDirectory);
        var project = CreateProject();
        AddTestMods(project);
        var target = Path.Combine(_testDirectory, "output", "Setup.exe");

        await service.BuildAsync(project, target, CancellationToken.None);

        var script = Encoding.UTF8.GetString(compiler.ScriptBytes!);
        StringAssert.Contains(
            script,
            "Name: \"mod_11111111111111111111111111111111\"; Description: \"{cm:ComponentMod_11111111111111111111111111111111}\"; Types: full compact custom; Flags: fixed");
        StringAssert.Contains(
            script,
            "Name: \"mod_22222222222222222222222222222222\"; Description: \"{cm:ComponentMod_22222222222222222222222222222222}\"; Types: full");
        StringAssert.Contains(script, "english.ComponentMod_11111111111111111111111111111111=Required mod (required)");
        StringAssert.Contains(script, "belarusian.ComponentMod_22222222222222222222222222222222=Optional mod (неабавязкова)");
        StringAssert.Contains(script, "Components: mod_11111111111111111111111111111111");
        StringAssert.Contains(script, "Components: mod_22222222222222222222222222222222");
        StringAssert.Contains(script, "BackupPayload('plugins\\required.dat', True)");
        StringAssert.Contains(script, "BackupPayload('ReadMe_Required.txt', True)");
        StringAssert.Contains(script, "BackupPayload('data\\optional.dat', True)");
        StringAssert.Contains(script, "WizardIsComponentSelected('mod_22222222222222222222222222222222')");
        StringAssert.Contains(script, "\"component\":\"mod_11111111111111111111111111111111\"");
        CollectionAssert.Contains(compiler.StagedFiles, "mod-00000014000000000000000000000000.bin");
        CollectionAssert.Contains(compiler.StagedFiles, "mod-00000015000000000000000000000000.bin");
        CollectionAssert.Contains(compiler.StagedFiles, "mod-00000016000000000000000000000000.bin");
        Assert.IsFalse(compiler.StagedFiles.Any(file => file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task BuildAsync_ReleaseReadMes_AreNotStagedInInstallerPayload()
    {
        var compiler = new InspectingCompiler();
        var service = new InnoInstallerExportService(compiler, AppContext.BaseDirectory);
        var project = CreateProject();
        var english = "English README body"u8.ToArray();
        var belarusian = Encoding.UTF8.GetBytes("Беларускі README");
        project.InstallerProfile!.ReleaseReadMeEnglish = new InstallerReleaseDocument
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000064"),
            OriginalFileName = "ReadMe.txt",
            Data = english,
        };
        project.InstallerProfile.ReleaseReadMeBelarusian = new InstallerReleaseDocument
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000065"),
            OriginalFileName = "ПрачытайМяне.txt",
            Data = belarusian,
        };
        var target = Path.Combine(_testDirectory, "output", "Setup.exe");

        await service.BuildAsync(project, target, CancellationToken.None);

        var script = Encoding.UTF8.GetString(compiler.ScriptBytes!);
        Assert.IsFalse(script.Contains("ReadMe.txt", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("ПрачытайМяне.txt", StringComparison.Ordinal));
        Assert.IsFalse(compiler.StagedContents.Values.Any(bytes => bytes.AsSpan().SequenceEqual(english)));
        Assert.IsFalse(compiler.StagedContents.Values.Any(bytes => bytes.AsSpan().SequenceEqual(belarusian)));
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

        var project = CreateProject();
        AddTestMods(project);
        await service.BuildAsync(project, target, CancellationToken.None);

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

    [TestMethod]
    public async Task BuildAsync_SmokeInstallAndUninstall_CustomModsBackupAndRestoreOriginals()
    {
        var compilerPath = Path.Combine(
            AppContext.BaseDirectory,
            "Tools",
            "InnoSetup",
            "7.0.2-x86",
            "ISCC.exe");
        var service = new InnoInstallerExportService(
            new LowestPrivilegesTestCompiler(compilerPath),
            AppContext.BaseDirectory);
        var project = CreateProject();
        project.InstallerProfile!.ProductId = Guid.NewGuid();
        AddTestMods(project);
        var setupPath = Path.Combine(_testDirectory, "smoke-setup.exe");
        var gameDirectory = Path.Combine(_testDirectory, "Fake Vice City");
        var requiredPath = Path.Combine(gameDirectory, "plugins", "required.dat");
        var optionalPath = Path.Combine(gameDirectory, "data", "optional.dat");
        var readMePath = Path.Combine(gameDirectory, "ReadMe_Required.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(requiredPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(optionalPath)!);
        await File.WriteAllBytesAsync(requiredPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(optionalPath, [4, 5, 6]);

        string? uninstallerPath = null;
        try
        {
            await service.BuildAsync(project, setupPath, CancellationToken.None);
            await RunInstallerProcessAsync(
                setupPath,
                "/VERYSILENT",
                "/SUPPRESSMSGBOXES",
                "/NORESTART",
                "/LANG=english",
                "/TYPE=full",
                $"/DIR={gameDirectory}");

            CollectionAssert.AreEqual(new byte[] { 20, 21 }, await File.ReadAllBytesAsync(requiredPath));
            CollectionAssert.AreEqual(new byte[] { 22, 23 }, await File.ReadAllBytesAsync(optionalPath));
            CollectionAssert.AreEqual(new byte[] { 24, 25 }, await File.ReadAllBytesAsync(readMePath));
            var manifestPath = Path.Combine(
                gameDirectory,
                "_BelarusianModBackup",
                project.InstallerProfile.ProductId.ToString("D"),
                "manifest.json");
            var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!.AsObject();
            var selectedComponents = manifest["selectedComponents"]!.AsArray()
                .Select(value => value!.GetValue<string>())
                .ToArray();
            CollectionAssert.Contains(selectedComponents, "mod_11111111111111111111111111111111");
            CollectionAssert.Contains(selectedComponents, "mod_22222222222222222222222222222222");
            Assert.IsTrue(manifest["files"]!.AsArray().Any(value =>
                value!["path"]!.GetValue<string>() == "plugins\\required.dat" &&
                value["backupOriginal"]!.GetValue<bool>()));

            var uninstallDirectory = Path.Combine(
                gameDirectory,
                "_BelarusianMod",
                project.InstallerProfile.ProductId.ToString("D"),
                "uninstall");
            uninstallerPath = Directory.GetFiles(uninstallDirectory, "*.exe").Single();
            await RunInstallerProcessAsync(
                uninstallerPath,
                "/VERYSILENT",
                "/SUPPRESSMSGBOXES",
                "/NORESTART");
            uninstallerPath = null;

            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(requiredPath));
            CollectionAssert.AreEqual(new byte[] { 4, 5, 6 }, await File.ReadAllBytesAsync(optionalPath));
            Assert.IsFalse(File.Exists(readMePath));
            Assert.IsFalse(File.Exists(manifestPath));
        }
        finally
        {
            if (uninstallerPath is not null && File.Exists(uninstallerPath))
            {
                await RunInstallerProcessAsync(
                    uninstallerPath,
                    "/VERYSILENT",
                    "/SUPPRESSMSGBOXES",
                    "/NORESTART");
            }
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
                    CreateModelsArchive(13, InstallerProfileValidator.Gta3ImgDestination),
                    CreateModelsArchive(14, InstallerProfileValidator.Gta3DirDestination),
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
        InstallerProfileValidator.RequiresX86Validation(destination)
            ? CreateBinary(id, InstallerAssetRole.SilentPatch, destination)
            : new InstallerAsset
            {
                Id = new Guid(id, 0, 0, new byte[8]),
                Role = InstallerAssetRole.SilentPatch,
                OriginalFileName = Path.GetFileName(destination),
                DestinationPath = destination,
                Data = Encoding.UTF8.GetBytes(destination),
            };

    private static InstallerAsset CreateModelsArchive(int id, string destination) => new()
    {
        Id = new Guid(id, 0, 0, new byte[8]),
        Role = InstallerAssetRole.ModelsArchive,
        OriginalFileName = Path.GetFileName(destination),
        DestinationPath = destination,
        Data = Encoding.UTF8.GetBytes(destination),
    };

    private static InstallerAsset CreateGameTxd(int id, string fileName) => new()
    {
        Id = new Guid(id, 0, 0, new byte[8]),
        Role = InstallerAssetRole.GameTxd,
        OriginalFileName = fileName,
        DestinationPath = InstallerProfileValidator.GetGameTxdDestination(fileName),
        Data = Encoding.UTF8.GetBytes(fileName),
    };

    private static void AddTestMods(EditorProject project)
    {
        project.InstallerProfile!.Mods =
        [
            new InstallerMod
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Name = "Required mod",
                IsRequired = true,
                Files =
                [
                    new InstallerModFile
                    {
                        Id = new Guid(20, 0, 0, new byte[8]),
                        OriginalFileName = "required.dat",
                        DestinationPath = "plugins\\required.dat",
                        Data = [20, 21],
                    },
                    new InstallerModFile
                    {
                        Id = new Guid(22, 0, 0, new byte[8]),
                        OriginalFileName = "ReadMe_Required.txt",
                        DestinationPath = "ReadMe_Required.txt",
                        Data = [24, 25],
                    },
                ],
            },
            new InstallerMod
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Name = "Optional mod",
                IsRequired = false,
                Files =
                [
                    new InstallerModFile
                    {
                        Id = new Guid(21, 0, 0, new byte[8]),
                        OriginalFileName = "optional.dat",
                        DestinationPath = "data\\optional.dat",
                        Data = [22, 23],
                    },
                ],
            },
        ];
    }

    private static async Task RunInstallerProcessAsync(string executablePath, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new AssertFailedException($"Could not start {executablePath}");
        await process.WaitForExitAsync();
        Assert.AreEqual(0, process.ExitCode, $"Process failed: {executablePath}");
    }

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

    private sealed class LowestPrivilegesTestCompiler(string compilerPath) : IInstallerCompiler
    {
        private readonly InnoInstallerCompiler _inner = new(compilerPath);

        public Task<string> CompileAsync(
            string scriptPath,
            string outputDirectory,
            string outputBaseName,
            CancellationToken cancellationToken)
        {
            var script = File.ReadAllText(scriptPath);
            const string productionDirective = "PrivilegesRequired=admin";
            Assert.IsTrue(script.Contains(productionDirective, StringComparison.Ordinal));
            File.WriteAllText(
                scriptPath,
                script.Replace(productionDirective, "PrivilegesRequired=lowest", StringComparison.Ordinal),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return _inner.CompileAsync(
                scriptPath,
                outputDirectory,
                outputBaseName,
                cancellationToken);
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
