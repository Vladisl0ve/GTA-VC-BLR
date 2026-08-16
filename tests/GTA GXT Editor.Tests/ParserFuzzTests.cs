using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Contracts;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;
using GTA_GXT_Editor.Utils;

namespace GTA_GXT_Editor.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ParserFuzzTests
{
    private const string WorkerFormatVariable = "GTA_GXT_FUZZ_WORKER_FORMAT";
    private const string WorkerProgressVariable = "GTA_GXT_FUZZ_PROGRESS_PATH";
    private const string WorkerTempVariable = "GTA_GXT_FUZZ_TEMP_PATH";
    private const string WorkerTestName =
        "GTA_GXT_Editor.Tests.ParserFuzzTests.RunParserFuzzWorker";
    private const int MaximumMutationLength = 64 * 1024;

    [TestMethod]
    public Task GxtParsers_DeterministicFuzz_IsControlled() => RunIsolatedAsync("gxt");

    [TestMethod]
    public Task TxdParser_DeterministicFuzz_IsControlled() => RunIsolatedAsync("txd");

    [TestMethod]
    public Task ByxParser_DeterministicFuzz_IsControlled() => RunIsolatedAsync("byx");

    [TestMethod]
    public Task FuzzWorker_Timeout_KillsProcess() =>
        RunIsolatedAsync("hang", TimeSpan.FromSeconds(2), expectTimeout: true);

    [TestMethod]
    public void RunParserFuzzWorker()
    {
        var format = Environment.GetEnvironmentVariable(WorkerFormatVariable);
        if (string.IsNullOrWhiteSpace(format))
        {
            return;
        }

        var progressPath = Environment.GetEnvironmentVariable(WorkerProgressVariable)
            ?? throw new InvalidOperationException("The fuzz worker has no progress path.");
        var tempPath = Environment.GetEnvironmentVariable(WorkerTempVariable)
            ?? throw new InvalidOperationException("The fuzz worker has no temporary path.");
        Directory.CreateDirectory(tempPath);

        switch (format)
        {
            case "gxt":
                RunGxtCampaigns(progressPath);
                break;
            case "txd":
                RunTxdCampaign(progressPath);
                break;
            case "byx":
                RunByxCampaign(progressPath, tempPath);
                break;
            case "hang":
                File.WriteAllText(progressPath, "intentional timeout probe", Encoding.UTF8);
                Thread.Sleep(Timeout.Infinite);
                break;
            default:
                throw new InvalidOperationException($"Unknown fuzz worker format '{format}'.");
        }
    }

    private static async Task RunIsolatedAsync(
        string format,
        TimeSpan? timeoutDuration = null,
        bool expectTimeout = false)
    {
        var runDirectory = Path.Combine(
            Path.GetTempPath(),
            $"gta-gxt-fuzz-{format}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(runDirectory);
        var progressPath = Path.Combine(runDirectory, "current-case.txt");
        var executablePath = Path.ChangeExtension(typeof(ParserFuzzTests).Assembly.Location, ".exe");
        Assert.IsTrue(File.Exists(executablePath), $"MSTest executable was not found: {executablePath}");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("--filter");
        process.StartInfo.ArgumentList.Add($"FullyQualifiedName={WorkerTestName}");
        process.StartInfo.ArgumentList.Add("--minimum-expected-tests");
        process.StartInfo.ArgumentList.Add("1");
        process.StartInfo.ArgumentList.Add("--timeout");
        process.StartInfo.ArgumentList.Add("28s");
        process.StartInfo.ArgumentList.Add("--progress");
        process.StartInfo.ArgumentList.Add("off");
        process.StartInfo.ArgumentList.Add("--no-ansi");
        process.StartInfo.ArgumentList.Add("--results-directory");
        process.StartInfo.ArgumentList.Add(Path.Combine(runDirectory, "results"));
        process.StartInfo.Environment[WorkerFormatVariable] = format;
        process.StartInfo.Environment[WorkerProgressVariable] = progressPath;
        process.StartInfo.Environment[WorkerTempVariable] = runDirectory;

        string standardOutput = string.Empty;
        string standardError = string.Empty;
        var timedOut = false;
        try
        {
            Assert.IsTrue(process.Start(), "The parser fuzz worker could not be started.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var effectiveTimeout = timeoutDuration ?? TimeSpan.FromSeconds(30);
            using var timeout = new CancellationTokenSource(effectiveTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                timedOut = true;
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                await process.WaitForExitAsync();
            }

            standardOutput = await outputTask;
            standardError = await errorTask;
            var currentCase = File.Exists(progressPath)
                ? File.ReadAllText(progressPath)
                : "No fuzz case was recorded.";
            if (expectTimeout)
            {
                Assert.IsTrue(
                    timedOut,
                    $"The timeout probe exited instead of being killed. stdout:{Environment.NewLine}{standardOutput}" +
                    $"{Environment.NewLine}stderr:{Environment.NewLine}{standardError}");
                return;
            }

            if (timedOut)
            {
                Assert.Fail(
                    $"The {format} fuzz worker exceeded {effectiveTimeout.TotalSeconds:F0} seconds. " +
                    $"Last case: {currentCase}");
            }

            Assert.AreEqual(
                0,
                process.ExitCode,
                $"The {format} fuzz worker failed. Last case: {currentCase}{Environment.NewLine}" +
                $"stdout:{Environment.NewLine}{standardOutput}{Environment.NewLine}" +
                $"stderr:{Environment.NewLine}{standardError}");
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            TryDeleteDirectory(runDirectory);
        }
    }

    private static void RunGxtCampaigns(string progressPath)
    {
        const int gtaThirdSeed = 0x47585433;
        const int viceCitySeed = 0x47585643;
        var factory = new GxtManagerFactory();
        var characterMap = new CharacterMapProfile();
        var gtaThirdData = CreateGtaThirdSeed();
        var viceCityData = CreateViceCitySeed();

        ExecuteCampaign(
            "gta3",
            GenerateMutations(gtaThirdData, gtaThirdSeed, 512, [4, 8, 24]),
            progressPath,
            data => factory.Open(
                data,
                GXTType.GtaIII,
                "fuzz-gta3.gxt",
                GxtLanguage.English,
                characterMap),
            manager => ValidateGxt(manager, GXTType.GtaIII, factory, characterMap));
        ExecuteCampaign(
            "vice-city",
            GenerateMutations(viceCityData, viceCitySeed, 512, [4, 16, 24, 28, 44]),
            progressPath,
            data => factory.Open(
                data,
                GXTType.GtaViceCity,
                "fuzz-vice-city.gxt",
                GxtLanguage.English,
                characterMap),
            manager => ValidateGxt(manager, GXTType.GtaViceCity, factory, characterMap));
    }

    private static void RunTxdCampaign(string progressPath)
    {
        const int txdSeed = 0x54584431;
        var reader = new TxdReader();
        var data = TestTxdFactory.Create(
            TestTxdFactory.Bgra32("font1", 2, 2, Enumerable.Repeat((byte)255, 16).ToArray()));
        var cases = GenerateMutations(
            data,
            txdSeed,
            512,
            [4, 16, 32, 44, 124, 128, 140],
            [24, 132, 134],
            [136, 137, 139]);

        ExecuteCampaign(
            "txd",
            cases,
            progressPath,
            input => reader.Read(input, "fuzz.txd"),
            ValidateTxd);
    }

    private static void RunByxCampaign(string progressPath, string tempPath)
    {
        const int byxSeed = 0x42595831;
        const int embeddedGxtSeed = 0x42594758;
        const int embeddedTxdSeed = 0x42595458;
        var serializer = new ByxProjectSerializer(new GxtManagerFactory(), new TxdReader());
        var seedData = CreateByxSeed(tempPath, serializer);
        var candidatePath = Path.Combine(tempPath, "candidate.byx");
        var endOfCentralDirectory = seedData.Length - 22;
        var cases = GenerateMutations(
                seedData,
                byxSeed,
                256,
                [endOfCentralDirectory + 12, endOfCentralDirectory + 16],
                [endOfCentralDirectory + 8, endOfCentralDirectory + 10])
            .ToList();

        var gxtData = CreateViceCitySeed();
        foreach (var payloadCase in GenerateMutations(
                     gxtData,
                     embeddedGxtSeed,
                     128,
                     [4, 16, 24, 28, 44]).Take(128))
        {
            cases.Add(new MutationCase(
                UpdateEmbeddedEntry(seedData, "gxt/main.gxt", "gxt", payloadCase.Data),
                $"embedded-gxt/{payloadCase.Operator}",
                payloadCase.Index,
                payloadCase.Seed));
        }

        var txdData = TestTxdFactory.Create(
            TestTxdFactory.Bgra32("font1", 1, 1, [0, 0, 0, 255]));
        foreach (var payloadCase in GenerateMutations(
                     txdData,
                     embeddedTxdSeed,
                     128,
                     [4, 16, 32, 44, 124, 128, 140],
                     [24, 132, 134],
                     [136, 137, 139]).Take(128))
        {
            cases.Add(new MutationCase(
                UpdateEmbeddedEntry(seedData, "txd/fonts.txd", "txd", payloadCase.Data),
                $"embedded-txd/{payloadCase.Operator}",
                payloadCase.Index,
                payloadCase.Seed));
        }

        ExecuteCampaign(
            "byx",
            cases,
            progressPath,
            input =>
            {
                File.WriteAllBytes(candidatePath, input);
                return serializer.Load(candidatePath);
            },
            ValidateByx);
    }

    private static void ExecuteCampaign<T>(
        string format,
        IEnumerable<MutationCase> cases,
        string progressPath,
        Func<byte[], T> parse,
        Action<T> validate)
    {
        foreach (var mutation in cases)
        {
            var descriptor =
                $"format={format}; seed=0x{mutation.Seed:X8}; case={mutation.Index}; " +
                $"operator={mutation.Operator}; length={mutation.Data.Length}; " +
                $"sha256={Convert.ToHexStringLower(SHA256.HashData(mutation.Data))}";
            File.WriteAllText(progressPath, descriptor, Encoding.UTF8);

            T result;
            try
            {
                result = parse(mutation.Data);
            }
            catch (InvalidDataException)
            {
                continue;
            }

            validate(result);
        }
    }

    private static IReadOnlyList<MutationCase> GenerateMutations(
        byte[] seed,
        int randomSeed,
        int randomCaseCount,
        int[]? int32Offsets = null,
        int[]? uint16Offsets = null,
        int[]? byteOffsets = null)
    {
        int32Offsets ??= [];
        uint16Offsets ??= [];
        byteOffsets ??= [];
        var cases = new List<MutationCase>();
        var caseIndex = 0;
        cases.Add(new MutationCase(seed.ToArray(), "valid-seed", caseIndex++, randomSeed));

        var truncationLengths = new[]
        {
            0,
            1,
            3,
            4,
            7,
            8,
            11,
            12,
            seed.Length / 2,
            Math.Max(0, seed.Length - 1),
        };
        foreach (var length in truncationLengths.Where(length => length < seed.Length).Distinct())
        {
            cases.Add(new MutationCase(seed[..length], $"truncate-{length}", caseIndex++, randomSeed));
        }

        foreach (var offset in int32Offsets.Where(offset => offset >= 0 && offset <= seed.Length - sizeof(int)))
        {
            foreach (var value in new[] { int.MinValue, -1, 0, 1, int.MaxValue })
            {
                var data = seed.ToArray();
                BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), value);
                cases.Add(new MutationCase(data, $"int32@{offset}={value}", caseIndex++, randomSeed));
            }
        }

        foreach (var offset in uint16Offsets.Where(offset => offset >= 0 && offset <= seed.Length - sizeof(ushort)))
        {
            foreach (var value in new ushort[] { 0, 1, ushort.MaxValue })
            {
                var data = seed.ToArray();
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), value);
                cases.Add(new MutationCase(data, $"uint16@{offset}={value}", caseIndex++, randomSeed));
            }
        }

        foreach (var offset in byteOffsets.Where(offset => offset >= 0 && offset < seed.Length))
        {
            foreach (var value in new byte[] { 0, 1, byte.MaxValue })
            {
                var data = seed.ToArray();
                data[offset] = value;
                cases.Add(new MutationCase(data, $"byte@{offset}={value}", caseIndex++, randomSeed));
            }
        }

        var random = new Random(randomSeed);
        for (var randomIndex = 0; randomIndex < randomCaseCount; randomIndex++)
        {
            var operation = random.Next(7);
            var (data, name) = operation switch
            {
                0 => RandomTruncation(seed, random),
                1 => RandomBitFlip(seed, random),
                2 => RandomByteOverwrite(seed, random),
                3 => RandomIntegerOverwrite(seed, random, int32Offsets),
                4 => RandomDeletion(seed, random),
                5 => RandomDuplication(seed, random),
                _ => RandomBuffer(random),
            };
            cases.Add(new MutationCase(data, $"random-{randomIndex}/{name}", caseIndex++, randomSeed));
        }

        return cases;
    }

    private static (byte[] Data, string Name) RandomTruncation(byte[] seed, Random random)
    {
        var length = random.Next(seed.Length + 1);
        return (seed[..length], $"truncate-{length}");
    }

    private static (byte[] Data, string Name) RandomBitFlip(byte[] seed, Random random)
    {
        var data = seed.ToArray();
        var offset = random.Next(data.Length);
        var bit = random.Next(8);
        data[offset] ^= (byte)(1 << bit);
        return (data, $"bit-flip@{offset}:{bit}");
    }

    private static (byte[] Data, string Name) RandomByteOverwrite(byte[] seed, Random random)
    {
        var data = seed.ToArray();
        var offset = random.Next(data.Length);
        var value = (byte)random.Next(256);
        data[offset] = value;
        return (data, $"byte@{offset}={value}");
    }

    private static (byte[] Data, string Name) RandomIntegerOverwrite(
        byte[] seed,
        Random random,
        int[] offsets)
    {
        var data = seed.ToArray();
        var validOffsets = offsets.Where(offset => offset >= 0 && offset <= data.Length - sizeof(int)).ToArray();
        var offset = validOffsets.Length == 0
            ? random.Next(data.Length - sizeof(int) + 1)
            : validOffsets[random.Next(validOffsets.Length)];
        var boundaryValues = new[] { int.MinValue, -1, 0, 1, int.MaxValue, random.Next() };
        var value = boundaryValues[random.Next(boundaryValues.Length)];
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), value);
        return (data, $"int32@{offset}={value}");
    }

    private static (byte[] Data, string Name) RandomDeletion(byte[] seed, Random random)
    {
        var start = random.Next(seed.Length);
        var length = random.Next(1, seed.Length - start + 1);
        var data = new byte[seed.Length - length];
        Buffer.BlockCopy(seed, 0, data, 0, start);
        Buffer.BlockCopy(seed, start + length, data, start, seed.Length - start - length);
        return (data, $"delete@{start}+{length}");
    }

    private static (byte[] Data, string Name) RandomDuplication(byte[] seed, Random random)
    {
        var start = random.Next(seed.Length);
        var maximumLength = Math.Min(Math.Min(64, seed.Length - start), MaximumMutationLength - seed.Length);
        if (maximumLength <= 0)
        {
            return RandomBitFlip(seed, random);
        }

        var length = random.Next(1, maximumLength + 1);
        var insertAt = random.Next(seed.Length + 1);
        var data = new byte[seed.Length + length];
        Buffer.BlockCopy(seed, 0, data, 0, insertAt);
        Buffer.BlockCopy(seed, start, data, insertAt, length);
        Buffer.BlockCopy(seed, insertAt, data, insertAt + length, seed.Length - insertAt);
        return (data, $"duplicate@{start}+{length}->{insertAt}");
    }

    private static (byte[] Data, string Name) RandomBuffer(Random random)
    {
        var length = random.Next(MaximumMutationLength + 1);
        var data = new byte[length];
        random.NextBytes(data);
        return (data, $"random-buffer-{length}");
    }

    private static void ValidateGxt(
        CommonGXTManager manager,
        GXTType type,
        GxtManagerFactory factory,
        CharacterMapProfile characterMap)
    {
        Assert.IsTrue(manager.GXTEntries.All(entry => entry.Value.GXTValueIsValid()));
        var expected = GetGxtSignatures(manager, type);
        using var output = new MemoryStream();
        manager.WriteGXT(output);
        var reopened = factory.Open(
            output.ToArray(),
            type,
            type == GXTType.GtaIII ? "roundtrip-gta3.gxt" : "roundtrip-vc.gxt",
            GxtLanguage.English,
            characterMap);

        CollectionAssert.AreEqual(expected, GetGxtSignatures(reopened, type));
    }

    private static string[] GetGxtSignatures(CommonGXTManager manager, GXTType type) =>
        manager.GXTEntries
            .Select(entry =>
            {
                var table = type == GXTType.GtaViceCity && entry is GTAVC.GXTEntry viceCityEntry
                    ? viceCityEntry.TableName
                    : string.Empty;
                return $"{table}|{entry.DatName}|{Convert.ToHexString(entry.Value)}";
            })
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static void ValidateTxd(TxdDocument document)
    {
        Assert.IsTrue(document.Textures.Count <= 4096);
        foreach (var texture in document.Textures)
        {
            var expectedLength = checked(texture.Width * texture.Height * 4);
            Assert.AreEqual(expectedLength, texture.PixelsBgra32.Length);
            Assert.AreEqual(expectedLength, texture.PreviewPixelsBgra32.Length);
        }
    }

    private static void ValidateByx(EditorProject project)
    {
        Assert.AreEqual(GXTType.GtaViceCity, project.GameType);
        Assert.IsFalse(string.IsNullOrWhiteSpace(project.GxtSourceName));
        Assert.IsTrue(project.GxtManager.GXTEntries.All(entry => entry.Value.GXTValueIsValid()));
        if (project.AttachedTxd is not null)
        {
            ValidateTxd(project.AttachedTxd.Document);
        }
    }

    private static byte[] CreateGtaThirdSeed()
    {
        using var stream = new MemoryStream();
        stream.WriteString("TKEY");
        stream.WriteInt(12);
        stream.WriteInt(0);
        stream.WriteString("HELLO\0\0\0");
        stream.WriteString("TDAT");
        stream.WriteInt(4);
        stream.WriteBytes([65, 0, 0, 0]);
        return stream.ToArray();
    }

    private static byte[] CreateViceCitySeed()
    {
        using var stream = new MemoryStream();
        stream.WriteString("TABL");
        stream.WriteInt(12);
        stream.WriteString("MAIN\0\0\0\0");
        stream.WriteInt(20);
        stream.WriteString("TKEY");
        stream.WriteInt(12);
        stream.WriteInt(0);
        stream.WriteString("HELLO\0\0\0");
        stream.WriteString("TDAT");
        stream.WriteInt(4);
        stream.WriteBytes([65, 0, 0, 0]);
        return stream.ToArray();
    }

    private static byte[] CreateByxSeed(string tempPath, ByxProjectSerializer serializer)
    {
        var manager = GxtManagerFactory.Create(
            GXTType.GtaViceCity,
            sourceName: "american.gxt",
            sourceTexts: ["A"],
            language: GxtLanguage.English);
        manager.AddGXTEntry("HELLO", "A");
        var txdData = TestTxdFactory.Create(
            TestTxdFactory.Bgra32("font1", 1, 1, [0, 0, 0, 255]));
        var txdReader = new TxdReader();
        var project = new EditorProject
        {
            GxtSourceName = "american.gxt",
            GameType = GXTType.GtaViceCity,
            GxtManager = manager,
            AttachedTxd = new TxdAttachment
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                OriginalFileName = "fonts.txd",
                DisplayName = "fonts",
                Data = txdData,
                Document = txdReader.Read(txdData, "fonts.txd"),
            },
        };
        var path = Path.Combine(tempPath, "seed.byx");
        serializer.Save(path, project);
        return File.ReadAllBytes(path);
    }

    private static byte[] UpdateEmbeddedEntry(
        byte[] seed,
        string entryName,
        string manifestProperty,
        byte[] data)
    {
        using var stream = new MemoryStream();
        stream.Write(seed);
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            JsonObject manifest;
            using (var reader = new StreamReader(archive.GetEntry("manifest.json")!.Open(), Encoding.UTF8))
            {
                manifest = JsonNode.Parse(reader.ReadToEnd())!.AsObject();
            }

            archive.GetEntry(entryName)!.Delete();
            using (var entryStream = archive.CreateEntry(entryName, CompressionLevel.Optimal).Open())
            {
                entryStream.Write(data);
            }

            manifest[manifestProperty]!["sha256"] =
                Convert.ToHexStringLower(SHA256.HashData(data));
            archive.GetEntry("manifest.json")!.Delete();
            using var manifestStream = archive.CreateEntry("manifest.json", CompressionLevel.Optimal).Open();
            using var writer = new Utf8JsonWriter(manifestStream, new JsonWriterOptions { Indented = true });
            manifest.WriteTo(writer);
        }

        return stream.ToArray();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record MutationCase(byte[] Data, string Operator, int Index, int Seed);
}
