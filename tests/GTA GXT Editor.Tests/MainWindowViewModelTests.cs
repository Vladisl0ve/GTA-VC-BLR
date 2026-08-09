using System.Text;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;
using GTA_GXT_Editor.ViewModels;

namespace GTA_GXT_Editor.Tests;

[STATestClass]
public sealed class MainWindowViewModelTests
{
    private string _testDirectory = null!;
    private string _dictionaryPath = null!;

    [TestInitialize]
    public void Initialize()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _testDirectory = Path.Combine(Path.GetTempPath(), $"gta-gxt-view-model-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
        _dictionaryPath = Path.Combine(_testDirectory, "characters.txt");
        File.WriteAllText(_dictionaryPath, string.Empty, Encoding.GetEncoding(1251));
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
    public void OpenFile_UsesAutomaticDictionaryWithoutConfirmation()
    {
        var gxtPath = CreateGxt("automatic.gxt", text: "Hello");
        var dialogs = new FakeDialogService();
        dialogs.OpenFileResults.Enqueue(gxtPath);
        var viewModel = CreateViewModel(dialogs);

        viewModel.OpenFileCommand.Execute(null);

        Assert.IsTrue(viewModel.IsDocumentLoaded);
        Assert.AreEqual(gxtPath, viewModel.GxtPath);
        Assert.HasCount(1, dialogs.OpenFileCalls);
        Assert.AreEqual(0, dialogs.ConfirmCallCount);
    }

    [TestMethod]
    public void OpenFromCommandLine_UsesAutomaticDictionaryWithoutDialogs()
    {
        var gxtPath = CreateGxt("command-line.gxt", text: "Hello");
        var dialogs = new FakeDialogService();
        var viewModel = CreateViewModel(dialogs);

        viewModel.OpenFromCommandLine(gxtPath);

        Assert.IsTrue(viewModel.IsDocumentLoaded);
        Assert.AreEqual(gxtPath, viewModel.GxtPath);
        Assert.IsEmpty(dialogs.OpenFileCalls);
        Assert.AreEqual(0, dialogs.ConfirmCallCount);
    }

    [TestMethod]
    public void OpenFileWithDictionary_UsesSelectedDictionaryAndReloadPreservesIt()
    {
        WriteDictionary("200 Ў");
        var gxtPath = CreateGxt("custom.gxt", _dictionaryPath, "Ў");
        var dialogs = new FakeDialogService();
        dialogs.OpenFileResults.Enqueue(gxtPath);
        dialogs.OpenFileResults.Enqueue(_dictionaryPath);
        var viewModel = CreateViewModel(dialogs);

        viewModel.OpenFileWithDictionaryCommand.Execute(null);
        Assert.HasCount(1, viewModel.Entries);
        Assert.AreEqual("Ў", viewModel.Entries[0].Text);

        viewModel.ReloadCommand.Execute(null);

        Assert.HasCount(1, viewModel.Entries);
        Assert.AreEqual("Ў", viewModel.Entries[0].Text);
        Assert.HasCount(2, dialogs.OpenFileCalls);
        Assert.AreEqual(0, dialogs.ConfirmCallCount);
    }

    [TestMethod]
    public void OpenFileWithDictionary_WhenDictionarySelectionIsCancelled_KeepsCurrentDocument()
    {
        var currentPath = CreateGxt("current.gxt", text: "Current");
        var nextPath = CreateGxt("next.gxt", text: "Next");
        var dialogs = new FakeDialogService();
        dialogs.OpenFileResults.Enqueue(currentPath);
        var viewModel = CreateViewModel(dialogs);
        viewModel.OpenFileCommand.Execute(null);

        dialogs.OpenFileResults.Enqueue(nextPath);
        dialogs.OpenFileResults.Enqueue(null);
        viewModel.OpenFileWithDictionaryCommand.Execute(null);

        Assert.AreEqual(currentPath, viewModel.GxtPath);
        Assert.HasCount(1, viewModel.Entries);
        Assert.AreEqual("Current", viewModel.Entries[0].Text);
        Assert.AreEqual(0, dialogs.ConfirmCallCount);
    }

    [TestMethod]
    public void OpenComparisonFiles_AddColumnsAndMatchingValuesForEveryFile()
    {
        var primaryPath = CreateGxtWithEntries(
            "primary.gxt",
            ("HELLO", "Primary hello"),
            ("ONLYMAIN", "Only in primary"));
        var firstComparisonPath = CreateGxtWithEntries(
            "first comparison.gxt",
            ("HELLO", "First comparison"),
            ("ONLYCOM", "Only in comparison"));
        var secondComparisonPath = CreateGxtWithEntries(
            "second-comparison.gxt",
            ("HELLO", "Second comparison"),
            ("ONLYMAIN", "Second-only match"));
        var dialogs = new FakeDialogService();
        dialogs.OpenFileResults.Enqueue(primaryPath);
        dialogs.OpenFilesResults.Enqueue([firstComparisonPath, secondComparisonPath]);
        var viewModel = CreateViewModel(dialogs);

        viewModel.OpenFileCommand.Execute(null);
        viewModel.OpenComparisonFileCommand.Execute(null);

        Assert.IsTrue(viewModel.IsComparisonLoaded);
        Assert.HasCount(2, viewModel.ComparisonColumns);
        Assert.AreEqual("first comparison", viewModel.ComparisonColumns[0].Name);
        Assert.AreEqual(firstComparisonPath, viewModel.ComparisonColumns[0].Path);
        Assert.AreEqual("second-comparison", viewModel.ComparisonColumns[1].Name);
        Assert.AreEqual(secondComparisonPath, viewModel.ComparisonColumns[1].Path);

        var hello = viewModel.Entries.Single(entry => entry.Name == "HELLO");
        CollectionAssert.AreEqual(
            new string?[] { "First comparison", "Second comparison" },
            hello.ComparisonTexts.ToArray());
        var onlyMain = viewModel.Entries.Single(entry => entry.Name == "ONLYMAIN");
        CollectionAssert.AreEqual(
            new string?[] { null, "Second-only match" },
            onlyMain.ComparisonTexts.ToArray());
        Assert.IsFalse(viewModel.Entries.Any(entry => entry.Name == "ONLYCOM"));
    }

    [TestMethod]
    public void OpenComparisonFiles_NormalizesAndDisambiguatesColumnNames()
    {
        var primaryPath = CreateGxt("primary.gxt", text: "Primary");
        var firstComparisonPath = CreateGxt("shared.gxt", text: "First");
        var secondDirectory = Path.Combine(_testDirectory, "second");
        Directory.CreateDirectory(secondDirectory);
        var secondComparisonPath = Path.Combine(secondDirectory, "shared.gxt");
        File.Copy(firstComparisonPath, secondComparisonPath);
        var dialogs = new FakeDialogService();
        dialogs.OpenFileResults.Enqueue(primaryPath);
        dialogs.OpenFilesResults.Enqueue([firstComparisonPath, secondComparisonPath]);
        var viewModel = CreateViewModel(dialogs);

        viewModel.OpenFileCommand.Execute(null);
        viewModel.OpenComparisonFileCommand.Execute(null);

        CollectionAssert.AreEqual(
            new[] { "shared", "shared (2)" },
            viewModel.ComparisonColumns.Select(column => column.Name).ToArray());
    }

    [TestMethod]
    public void OpenComparisonFile_ReloadPreservesComparisonAndNewDocumentClearsIt()
    {
        var primaryPath = CreateGxt("primary.gxt", text: "Primary");
        var comparisonPath = CreateGxt("comparison.gxt", text: "Comparison");
        var nextPath = CreateGxt("next.gxt", text: "Next");
        var dialogs = new FakeDialogService();
        dialogs.OpenFileResults.Enqueue(primaryPath);
        dialogs.OpenFilesResults.Enqueue([comparisonPath]);
        var viewModel = CreateViewModel(dialogs);
        viewModel.OpenFileCommand.Execute(null);
        viewModel.OpenComparisonFileCommand.Execute(null);

        viewModel.ReloadCommand.Execute(null);

        Assert.IsTrue(viewModel.IsComparisonLoaded);
        Assert.AreEqual("Comparison", viewModel.Entries[0].ComparisonTexts[0]);

        dialogs.OpenFileResults.Enqueue(nextPath);
        viewModel.OpenFileCommand.Execute(null);

        Assert.IsFalse(viewModel.IsComparisonLoaded);
        Assert.IsEmpty(viewModel.ComparisonColumns);
        Assert.IsEmpty(viewModel.Entries[0].ComparisonTexts);
    }

    [TestMethod]
    public void Search_CanFilterByComparisonText()
    {
        var primaryPath = CreateGxtWithEntries(
            "primary.gxt",
            ("FIRST", "One"),
            ("SECOND", "Two"));
        var comparisonPath = CreateGxtWithEntries(
            "comparison.gxt",
            ("FIRST", "Needle"),
            ("SECOND", "Other"));
        var dialogs = new FakeDialogService();
        dialogs.OpenFileResults.Enqueue(primaryPath);
        dialogs.OpenFilesResults.Enqueue([comparisonPath]);
        var viewModel = CreateViewModel(dialogs);
        viewModel.OpenFileCommand.Execute(null);
        viewModel.OpenComparisonFileCommand.Execute(null);

        viewModel.SelectedSearchColumn = viewModel.SearchColumns.Single(
            option => option.Column == SearchColumn.Comparison);
        viewModel.SearchText = "needle";

        Assert.AreEqual("FIRST", viewModel.EntriesView.Cast<GxtEntryRow>().Single().Name);
    }

    [TestMethod]
    public void ImportJson_UsesAutomaticDictionaryWithoutConfirmation()
    {
        var jsonPath = WriteJson("automatic.json", "Hello", "en");
        var targetPath = Path.Combine(_testDirectory, "automatic.gxt");
        var dialogs = new FakeDialogService();
        dialogs.OpenFileResults.Enqueue(jsonPath);
        dialogs.SaveFileResults.Enqueue(targetPath);
        var viewModel = CreateViewModel(dialogs);

        viewModel.ImportJsonCommand.Execute(null);

        Assert.IsTrue(File.Exists(targetPath));
        Assert.AreEqual(targetPath, viewModel.GxtPath);
        Assert.HasCount(1, viewModel.Entries);
        Assert.AreEqual("Hello", viewModel.Entries[0].Text);
        Assert.HasCount(1, dialogs.OpenFileCalls);
        Assert.AreEqual(0, dialogs.ConfirmCallCount);
    }

    [TestMethod]
    public void ImportJsonWithDictionary_UsesSelectedDictionary()
    {
        WriteDictionary("200 Ў");
        var jsonPath = WriteJson("custom.json", "Ў", "be");
        var targetPath = Path.Combine(_testDirectory, "custom-import.gxt");
        var dialogs = new FakeDialogService();
        dialogs.OpenFileResults.Enqueue(jsonPath);
        dialogs.OpenFileResults.Enqueue(_dictionaryPath);
        dialogs.SaveFileResults.Enqueue(targetPath);
        var viewModel = CreateViewModel(dialogs);

        viewModel.ImportJsonWithDictionaryCommand.Execute(null);

        Assert.IsTrue(File.Exists(targetPath));
        Assert.HasCount(1, viewModel.Entries);
        Assert.AreEqual("Ў", viewModel.Entries[0].Text);
        Assert.HasCount(2, dialogs.OpenFileCalls);
        StringAssert.Contains(dialogs.OpenFileCalls[1].Title, "словарь");
        Assert.AreEqual(0, dialogs.ConfirmCallCount);
    }

    [TestMethod]
    public void ImportJsonWithDictionary_WhenDictionarySelectionIsCancelled_DoesNotCreateFile()
    {
        var jsonPath = WriteJson("cancelled.json", "Hello", "en");
        var targetPath = Path.Combine(_testDirectory, "cancelled.gxt");
        var dialogs = new FakeDialogService();
        dialogs.OpenFileResults.Enqueue(jsonPath);
        dialogs.OpenFileResults.Enqueue(null);
        dialogs.SaveFileResults.Enqueue(targetPath);
        var viewModel = CreateViewModel(dialogs);

        viewModel.ImportJsonWithDictionaryCommand.Execute(null);

        Assert.IsFalse(File.Exists(targetPath));
        Assert.IsFalse(viewModel.IsDocumentLoaded);
        Assert.IsEmpty(dialogs.SaveFileCalls);
        Assert.AreEqual(0, dialogs.ConfirmCallCount);
    }

    [TestMethod]
    public void TxdCommands_AreEnabledOnlyForViceCity()
    {
        var gtaIIIPath = CreateGxt("gta3.gxt");
        var viceCityPath = CreateViceCityGxt("vice.gxt");
        var dialogs = new FakeDialogService();
        dialogs.OpenFileResults.Enqueue(gtaIIIPath);
        var viewModel = CreateViewModel(dialogs);

        viewModel.OpenFileCommand.Execute(null);
        Assert.IsFalse(viewModel.AddTxdCommand.CanExecute(null));

        dialogs.OpenFileResults.Enqueue(viceCityPath);
        viewModel.OpenFileCommand.Execute(null);
        Assert.IsTrue(viewModel.AddTxdCommand.CanExecute(null));
    }

    [TestMethod]
    public void AddTxd_ReplacesExistingAttachmentAndKeepsSingleId()
    {
        var viceCityPath = CreateViceCityGxt("vice.gxt");
        var firstPath = WriteTxd(Path.Combine("one", "fonts.txd"), [1, 2, 3, 255]);
        var secondPath = WriteTxd(Path.Combine("two", "fonts.txd"), [4, 5, 6, 255]);
        var dialogs = new FakeDialogService();
        dialogs.OpenFileResults.Enqueue(viceCityPath);
        dialogs.OpenFileResults.Enqueue(firstPath);
        dialogs.OpenFileResults.Enqueue(secondPath);
        var viewModel = CreateViewModel(dialogs);

        viewModel.OpenFileCommand.Execute(null);
        viewModel.AddTxdCommand.Execute(null);
        viewModel.AddTxdCommand.Execute(null);

        Assert.IsNotNull(viewModel.AttachedTxd);
        Assert.AreEqual("fonts", viewModel.AttachedTxd.DisplayName);
        CollectionAssert.AreEqual(File.ReadAllBytes(secondPath), viewModel.AttachedTxd.Data);
        var originalId = viewModel.AttachedTxd.Id;

        var updatedData = TestTxdFactory.Create(
            TestTxdFactory.Bgra32("font1", 1, 1, [9, 8, 7, 128]));
        File.WriteAllBytes(firstPath, updatedData);
        dialogs.OpenFileResults.Enqueue(firstPath);
        viewModel.AddTxdCommand.Execute(null);

        Assert.IsNotNull(viewModel.AttachedTxd);
        Assert.AreEqual(originalId, viewModel.AttachedTxd.Id);
        CollectionAssert.AreEqual(updatedData, viewModel.AttachedTxd.Data);
        Assert.IsTrue(viewModel.IsProjectDirty);
    }

    [TestMethod]
    public void AddInvalidTxd_DoesNotChangeCurrentProject()
    {
        var viceCityPath = CreateViceCityGxt("vice.gxt");
        var invalidPath = Path.Combine(_testDirectory, "invalid.txd");
        File.WriteAllBytes(invalidPath, [1, 2, 3]);
        var dialogs = new FakeDialogService { AllowErrors = true };
        dialogs.OpenFileResults.Enqueue(viceCityPath);
        dialogs.OpenFileResults.Enqueue(invalidPath);
        var viewModel = CreateViewModel(dialogs);

        viewModel.OpenFileCommand.Execute(null);
        viewModel.AddTxdCommand.Execute(null);

        Assert.IsNull(viewModel.AttachedTxd);
        Assert.IsFalse(viewModel.IsProjectDirty);
        Assert.HasCount(1, dialogs.Errors);
    }

    [TestMethod]
    public void Save_WithTxdCreatesByxAndSubsequentSaveUpdatesItWithoutDialog()
    {
        var viceCityPath = CreateViceCityGxt("vice.gxt");
        var txdPath = WriteTxd("fonts.txd", [1, 2, 3, 255]);
        var comparisonPath = CreateViceCityGxt("comparison.gxt", "Comparison");
        var byxPath = Path.Combine(_testDirectory, "translation.byx");
        var dialogs = new FakeDialogService();
        dialogs.OpenFileResults.Enqueue(viceCityPath);
        dialogs.OpenFileResults.Enqueue(txdPath);
        dialogs.OpenFilesResults.Enqueue([comparisonPath]);
        dialogs.SaveFileResults.Enqueue(byxPath);
        var viewModel = CreateViewModel(dialogs);

        viewModel.OpenFileCommand.Execute(null);
        viewModel.OpenComparisonFileCommand.Execute(null);
        viewModel.AddTxdCommand.Execute(null);
        viewModel.SaveCommand.Execute(null);

        Assert.IsTrue(File.Exists(byxPath));
        Assert.AreEqual(byxPath, viewModel.ProjectPath);
        Assert.IsFalse(viewModel.IsProjectDirty);
        Assert.HasCount(1, dialogs.SaveFileCalls);

        dialogs.OpenFileResults.Enqueue(txdPath);
        viewModel.AddTxdCommand.Execute(null);
        Assert.IsTrue(viewModel.IsProjectDirty);
        viewModel.SaveCommand.Execute(null);
        Assert.HasCount(1, dialogs.SaveFileCalls);
        Assert.IsFalse(viewModel.IsProjectDirty);

        var reopened = CreateViewModel(new FakeDialogService());
        reopened.OpenFromCommandLine(byxPath);
        Assert.AreEqual(byxPath, reopened.ProjectPath);
        Assert.AreEqual("vice.gxt", reopened.GxtSourceName);
        Assert.IsNotNull(reopened.AttachedTxd);
        Assert.IsFalse(reopened.IsComparisonLoaded);
    }

    [TestMethod]
    public void ViewAndExportTxd_UseCurrentCharacterMapAndExactBytes()
    {
        WriteDictionary("200 Ж");
        var viceCityPath = CreateViceCityGxt("vice.gxt");
        var txdPath = WriteTxd("fonts.txd", [1, 2, 3, 128]);
        var exportPath = Path.Combine(_testDirectory, "exported.txd");
        var dialogs = new FakeDialogService();
        dialogs.OpenFileResults.Enqueue(viceCityPath);
        dialogs.OpenFileResults.Enqueue(_dictionaryPath);
        dialogs.OpenFileResults.Enqueue(txdPath);
        dialogs.SaveFileResults.Enqueue(exportPath);
        var viewModel = CreateViewModel(dialogs);

        viewModel.OpenFileWithDictionaryCommand.Execute(null);
        viewModel.AddTxdCommand.Execute(null);
        viewModel.ViewTxdCommand.Execute(null);
        viewModel.ExportTxdCommand.Execute(null);

        Assert.IsNotNull(dialogs.CharacterMapRequest);
        Assert.AreEqual(viewModel.AttachedTxd, dialogs.CharacterMapRequest.Attachment);
        Assert.AreEqual(GXTType.GtaViceCity, dialogs.CharacterMapRequest.GameType);
        Assert.IsTrue(dialogs.CharacterMapRequest.Profile.Mappings.Count > 0);
        CollectionAssert.AreEqual(File.ReadAllBytes(txdPath), File.ReadAllBytes(exportPath));
    }

    [TestMethod]
    public void DirtyDocument_CancelPreventsCloseAndReplacement()
    {
        var viceCityPath = CreateViceCityGxt("vice.gxt");
        var nextPath = CreateViceCityGxt("next.gxt", "Next");
        var txdPath = WriteTxd("fonts.txd", [1, 2, 3, 255]);
        var dialogs = new FakeDialogService { UnsavedChoice = UnsavedChangesChoice.Cancel };
        dialogs.OpenFileResults.Enqueue(viceCityPath);
        dialogs.OpenFileResults.Enqueue(txdPath);
        var viewModel = CreateViewModel(dialogs);
        viewModel.OpenFileCommand.Execute(null);
        viewModel.AddTxdCommand.Execute(null);

        Assert.IsFalse(viewModel.CanClose());
        dialogs.OpenFileResults.Enqueue(nextPath);
        viewModel.OpenFileCommand.Execute(null);

        Assert.AreEqual(viceCityPath, viewModel.GxtPath);
        Assert.IsNotNull(viewModel.AttachedTxd);
        Assert.AreEqual(2, dialogs.UnsavedConfirmationCount);
    }

    [TestMethod]
    public void Reload_DiscardRemovesUnsavedTxdAndKeepsComparisonColumns()
    {
        var viceCityPath = CreateViceCityGxt("vice.gxt");
        var comparisonPath = CreateViceCityGxt("comparison.gxt", "Comparison");
        var txdPath = WriteTxd("fonts.txd", [1, 2, 3, 255]);
        var dialogs = new FakeDialogService { UnsavedChoice = UnsavedChangesChoice.Discard };
        dialogs.OpenFileResults.Enqueue(viceCityPath);
        dialogs.OpenFileResults.Enqueue(txdPath);
        dialogs.OpenFilesResults.Enqueue([comparisonPath]);
        var viewModel = CreateViewModel(dialogs);

        viewModel.OpenFileCommand.Execute(null);
        viewModel.OpenComparisonFileCommand.Execute(null);
        viewModel.AddTxdCommand.Execute(null);
        viewModel.ReloadCommand.Execute(null);

        Assert.IsNull(viewModel.AttachedTxd);
        Assert.IsFalse(viewModel.IsProjectDirty);
        Assert.IsTrue(viewModel.IsComparisonLoaded);
        Assert.AreEqual("Comparison", viewModel.Entries[0].ComparisonTexts[0]);
    }

    [TestMethod]
    public void OpenCorruptByx_KeepsCurrentDocumentUntouched()
    {
        var viceCityPath = CreateViceCityGxt("vice.gxt");
        var corruptPath = Path.Combine(_testDirectory, "corrupt.byx");
        File.WriteAllBytes(corruptPath, [1, 2, 3]);
        var dialogs = new FakeDialogService { AllowErrors = true };
        dialogs.OpenFileResults.Enqueue(viceCityPath);
        dialogs.OpenFileResults.Enqueue(corruptPath);
        var viewModel = CreateViewModel(dialogs);

        viewModel.OpenFileCommand.Execute(null);
        viewModel.OpenFileCommand.Execute(null);

        Assert.AreEqual(viceCityPath, viewModel.GxtPath);
        Assert.AreEqual("Hello", viewModel.Entries.Single().Text);
        Assert.HasCount(1, dialogs.Errors);
    }

    private MainWindowViewModel CreateViewModel(FakeDialogService dialogs) =>
        new(new GxtManagerFactory(), dialogs);

    private string CreateGxt(string fileName, string? dictionaryPath = null, string text = "Hello")
    {
        return CreateGxtWithEntries(fileName, dictionaryPath, ("HELLO", text));
    }

    private string CreateGxtWithEntries(
        string fileName,
        params (string Key, string Text)[] entries) =>
        CreateGxtWithEntries(fileName, dictionaryPath: null, entries);

    private string CreateGxtWithEntries(
        string fileName,
        string? dictionaryPath,
        params (string Key, string Text)[] entries)
    {
        var path = Path.Combine(_testDirectory, fileName);
        var manager = GxtManagerFactory.Create(
            GXTType.GtaIII,
            dictionaryPath,
            sourceName: fileName,
            sourceTexts: entries.Select(entry => entry.Text));
        foreach (var entry in entries)
        {
            manager.AddGXTEntry(entry.Key, entry.Text);
        }

        manager.SaveGXTChanges(path);
        return path;
    }

    private string CreateViceCityGxt(string fileName, string text = "Hello")
    {
        var path = Path.Combine(_testDirectory, fileName);
        var manager = GxtManagerFactory.Create(
            GXTType.GtaViceCity,
            sourceName: fileName,
            sourceTexts: [text],
            language: GxtLanguage.English);
        manager.AddGXTEntry("HELLO", text);
        manager.SaveGXTChanges(path);
        return path;
    }

    private string WriteTxd(string relativePath, byte[] pixel)
    {
        var path = Path.Combine(_testDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(
            path,
            TestTxdFactory.Create(TestTxdFactory.Bgra32("font1", 1, 1, pixel)));
        return path;
    }

    private string WriteJson(string fileName, string text, string language)
    {
        var path = Path.Combine(_testDirectory, fileName);
        File.WriteAllText(
            path,
            $$"""
            {
              "game": "GTA III",
              "source": "{{fileName}}",
              "language": "{{language}}",
              "entries": [
                { "key": "HELLO", "text": "{{text}}" }
              ]
            }
            """,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private void WriteDictionary(params string[] lines)
    {
        File.WriteAllLines(_dictionaryPath, lines, Encoding.GetEncoding(1251));
    }

    private sealed class FakeDialogService : IDialogService
    {
        public Queue<string?> OpenFileResults { get; } = new();

        public Queue<IReadOnlyList<string>> OpenFilesResults { get; } = new();

        public Queue<string?> SaveFileResults { get; } = new();

        public List<(string Title, string Filter)> OpenFileCalls { get; } = [];

        public List<(string Title, string Filter)> OpenFilesCalls { get; } = [];

        public List<(string Title, string Filter, string SuggestedPath)> SaveFileCalls { get; } = [];

        public int ConfirmCallCount { get; private set; }

        public int UnsavedConfirmationCount { get; private set; }

        public UnsavedChangesChoice UnsavedChoice { get; set; } = UnsavedChangesChoice.Discard;

        public bool AllowErrors { get; set; }

        public List<(string Message, string Title)> Errors { get; } = [];

        public CharacterMapEditorRequest? CharacterMapRequest { get; private set; }

        public string? OpenFile(string title, string filter)
        {
            OpenFileCalls.Add((title, filter));
            return OpenFileResults.Count > 0 ? OpenFileResults.Dequeue() : null;
        }

        public IReadOnlyList<string> OpenFiles(string title, string filter)
        {
            OpenFilesCalls.Add((title, filter));
            return OpenFilesResults.Count > 0 ? OpenFilesResults.Dequeue() : [];
        }

        public string? SaveFile(string title, string filter, string suggestedPath)
        {
            SaveFileCalls.Add((title, filter, suggestedPath));
            return SaveFileResults.Count > 0 ? SaveFileResults.Dequeue() : null;
        }

        public bool Confirm(string message, string title)
        {
            ConfirmCallCount++;
            return true;
        }

        public void ShowInfo(string message, string title = "GTA GXT Editor")
        {
        }

        public void ShowError(string message, string title = "Ошибка")
        {
            Errors.Add((message, title));
            if (!AllowErrors)
            {
                Assert.Fail($"Unexpected error dialog '{title}': {message}");
            }
        }

        public EntryEditorResult? EditEntry(EntryEditorRequest request) => null;

        public UnsavedChangesChoice ConfirmUnsavedChanges()
        {
            UnsavedConfirmationCount++;
            return UnsavedChoice;
        }

        public CharacterMapEditorResult? EditCharacterMap(CharacterMapEditorRequest request)
        {
            CharacterMapRequest = request;
            return null;
        }
    }
}
