using System.Security.Cryptography;

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
            File.Exists(Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "Installer",
                "Welcome.png")),
            "Missing installer welcome image: Assets/Installer/Welcome.png");
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

    private static HashSet<string> ReadKeys(string path) => File.ReadLines(path)
        .Where(line => line.Length > 0 && char.IsAsciiLetterOrDigit(line[0]) && line.Contains('='))
        .Select(line => line[..line.IndexOf('=')])
        .ToHashSet(StringComparer.Ordinal);
}
