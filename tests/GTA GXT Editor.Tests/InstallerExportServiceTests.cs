using System.Buffers.Binary;
using System.Text;
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
        StringAssert.Contains(script, "FONTB.TXD");
        StringAssert.Contains(script, "MODELS\\FONTS.TXD");
        StringAssert.Contains(script, "BelarusianLanguage.asi");
        StringAssert.Contains(script, "dinput8.dll");
        StringAssert.Contains(script, "SilentPatchVC.asi");
        StringAssert.Contains(script, "plugins\\Tommy''s settings.ini");
        StringAssert.Contains(script, "AppName=Belarusian %7 {{VC}");
        Assert.IsFalse(script.Contains("@@", StringComparison.Ordinal));
        CollectionAssert.AreEquivalent(
            new[]
            {
                "asset-00000001000000000000000000000000.bin",
                "asset-00000002000000000000000000000000.bin",
                "asset-00000003000000000000000000000000.bin",
                "asset-00000004000000000000000000000000.bin",
                "generated-font-models.bin",
                "generated-font-root.bin",
                "generated-gxt.bin",
            },
            compiler.StagedFiles);
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
                    CreateBinary(2, InstallerAssetRole.AsiLoader, "dinput8.dll"),
                    CreateBinary(3, InstallerAssetRole.SilentPatch, "SilentPatchVC.asi"),
                    new InstallerAsset
                    {
                        Id = new Guid(4, 0, 0, new byte[8]),
                        Role = InstallerAssetRole.Additional,
                        OriginalFileName = "settings.ini",
                        DestinationPath = "plugins\\Tommy's settings.ini",
                        Data = "enabled=1"u8.ToArray(),
                    },
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

        public string[] StagedFiles { get; private set; } = [];

        public Task<string> CompileAsync(
            string scriptPath,
            string outputDirectory,
            string outputBaseName,
            CancellationToken cancellationToken)
        {
            ScriptBytes = File.ReadAllBytes(scriptPath);
            StagedFiles = Directory.GetFiles(
                    Path.Combine(Path.GetDirectoryName(scriptPath)!, "payload"))
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal)
                .ToArray()!;
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
