using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Tests;

[TestClass]
public sealed class InstallerBundledAssetsTests
{
    [TestMethod]
    public void BelarusianLanguageFile_CoversEveryDefaultInnoMessage()
    {
        var toolRoot = Path.Combine(AppContext.BaseDirectory, "Tools", "InnoSetup", "7.0.2-x86");
        var defaultKeys = ReadKeys(Path.Combine(toolRoot, "Default.isl"));
        var belarusianKeys = ReadKeys(Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Installer",
            "Belarusian.isl"));

        var missing = defaultKeys.Except(belarusianKeys, StringComparer.Ordinal).ToArray();

        Assert.IsEmpty(missing, $"Missing Belarusian Inno messages: {string.Join(", ", missing)}");
    }

    [TestMethod]
    public void WelcomeImage_IsBundledWithInstallerAssets()
    {
        Assert.IsTrue(
            File.Exists(WelcomeImagePath),
            "Missing installer welcome image: Assets/Installer/Welcome.bmp");
    }

    [TestMethod]
    public void WelcomeImage_Is24BitBitmapWithoutColorSpace()
    {
        var data = File.ReadAllBytes(WelcomeImagePath);
        Assert.IsGreaterThanOrEqualTo(54, data.Length);
        Assert.AreEqual((byte)'B', data[0]);
        Assert.AreEqual((byte)'M', data[1]);
        Assert.AreEqual(54, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(10, 4)));
        Assert.AreEqual(40, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(14, 4)));
        Assert.AreEqual((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(26, 2)));
        Assert.AreEqual((ushort)24, BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(28, 2)));
        Assert.AreEqual(0, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(30, 4)));
    }

    [TestMethod]
    public async Task WelcomeImage_SilentSetupLoadsWithoutBitmapError()
    {
        var compilerPath = Path.Combine(
            AppContext.BaseDirectory,
            "Tools",
            "InnoSetup",
            "7.0.2-x86",
            "ISCC.exe");
        Assert.IsTrue(File.Exists(compilerPath), $"Missing bundled compiler: {compilerPath}");

        var workDirectory = Path.Combine(
            Path.GetTempPath(),
            $"gta-gxt-welcome-smoke-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDirectory);
        try
        {
            File.Copy(WelcomeImagePath, Path.Combine(workDirectory, "Welcome.bmp"));
            await File.WriteAllTextAsync(Path.Combine(workDirectory, "payload.txt"), "ok");
            var scriptPath = Path.Combine(workDirectory, "smoke.iss");
            await File.WriteAllTextAsync(
                scriptPath,
                """
                [Setup]
                AppId={{9e3c1d6a-7c2f-4b18-a6d4-2f8e0b1c4a77}}
                AppName=WelcomeImageSmoke
                AppVersion=1.0
                DefaultDirName={tmp}\WelcomeImageSmoke
                DisableProgramGroupPage=yes
                PrivilegesRequired=lowest
                Uninstallable=no
                WizardImageFile=Welcome.bmp
                OutputDir=output
                OutputBaseFilename=welcome-smoke

                [Files]
                Source: "payload.txt"; DestDir: "{app}"
                """);

            var setupPath = await new InnoInstallerCompiler(compilerPath).CompileAsync(
                scriptPath,
                Path.Combine(workDirectory, "output"),
                "welcome-smoke",
                CancellationToken.None);
            var installDirectory = Path.Combine(workDirectory, "installed");
            var logPath = Path.Combine(workDirectory, "setup.log");
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = setupPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            process.StartInfo.ArgumentList.Add("/VERYSILENT");
            process.StartInfo.ArgumentList.Add("/SUPPRESSMSGBOXES");
            process.StartInfo.ArgumentList.Add("/NORESTART");
            process.StartInfo.ArgumentList.Add($"/DIR={installDirectory}");
            process.StartInfo.ArgumentList.Add($"/LOG={logPath}");
            Assert.IsTrue(process.Start(), "Welcome-image smoke setup failed to start.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                Assert.Fail("Welcome-image smoke setup timed out.");
            }

            var log = File.Exists(logPath) ? await File.ReadAllTextAsync(logPath) : string.Empty;
            var processOutput = string.Join(
                Environment.NewLine,
                new[] { (await outputTask).Trim(), (await errorTask).Trim(), log }
                    .Where(value => value.Length > 0));
            Assert.AreEqual(
                0,
                process.ExitCode,
                $"Welcome-image smoke setup failed with exit {process.ExitCode}.{Environment.NewLine}{processOutput}");
            Assert.IsFalse(
                processOutput.Contains("Bitmap image is not valid", StringComparison.OrdinalIgnoreCase),
                $"Welcome-image smoke setup reported an invalid bitmap.{Environment.NewLine}{processOutput}");
            Assert.IsTrue(
                File.Exists(Path.Combine(installDirectory, "payload.txt")),
                "Welcome-image smoke setup did not install the payload.");
        }
        finally
        {
            if (Directory.Exists(workDirectory))
            {
                Directory.Delete(workDirectory, recursive: true);
            }
        }
    }

    [TestMethod]
    public void BundledInnoCompiler_MatchesPinnedSha256Inventory()
    {
        var toolRoot = Path.Combine(AppContext.BaseDirectory, "Tools", "InnoSetup", "7.0.2-x86");
        var inventoryPath = Path.Combine(toolRoot, "SHA256SUMS.txt");
        var expected = File.ReadAllLines(inventoryPath)
            .Select(line => line.Split("  ", 2, StringSplitOptions.None))
            .ToDictionary(parts => parts[1], parts => parts[0], StringComparer.Ordinal);
        var actualFiles = Directory.GetFiles(toolRoot, "*", SearchOption.AllDirectories)
            .Where(path => !string.Equals(path, inventoryPath, StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(toolRoot, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEquivalent(expected.Keys.ToArray(), actualFiles);
        foreach (var relativePath in actualFiles)
        {
            var path = Path.Combine(toolRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.AreEqual(
                expected[relativePath],
                Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))),
                $"Bundled Inno Setup file changed: {relativePath}");
        }

        StringAssert.Contains(File.ReadAllText(Path.Combine(toolRoot, "license.txt")), "Inno Setup License");
        StringAssert.Contains(File.ReadAllText(Path.Combine(toolRoot, "PINNED-VERSION.txt")), "7.0.2 x86");
    }

    private static string WelcomeImagePath => Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "Installer",
        "Welcome.bmp");

    private static HashSet<string> ReadKeys(string path) => File.ReadLines(path)
        .Where(line => line.Length > 0 && char.IsAsciiLetterOrDigit(line[0]) && line.Contains('='))
        .Select(line => line[..line.IndexOf('=')])
        .ToHashSet(StringComparer.Ordinal);
}
