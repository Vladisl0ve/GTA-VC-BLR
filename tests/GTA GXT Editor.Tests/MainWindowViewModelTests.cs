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

    private MainWindowViewModel CreateViewModel(FakeDialogService dialogs) =>
        new(new GxtManagerFactory(), dialogs);

    private string CreateGxt(string fileName, string? dictionaryPath = null, string text = "Hello")
    {
        var path = Path.Combine(_testDirectory, fileName);
        var manager = GxtManagerFactory.Create(
            GXTType.GtaIII,
            dictionaryPath,
            sourceName: fileName,
            sourceTexts: [text]);
        manager.AddGXTEntry("HELLO", text);
        manager.SaveGXTChanges(path);
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

        public Queue<string?> SaveFileResults { get; } = new();

        public List<(string Title, string Filter)> OpenFileCalls { get; } = [];

        public List<(string Title, string Filter, string SuggestedPath)> SaveFileCalls { get; } = [];

        public int ConfirmCallCount { get; private set; }

        public string? OpenFile(string title, string filter)
        {
            OpenFileCalls.Add((title, filter));
            return OpenFileResults.Count > 0 ? OpenFileResults.Dequeue() : null;
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
            Assert.Fail($"Unexpected error dialog '{title}': {message}");
        }

        public EntryEditorResult? EditEntry(EntryEditorRequest request) => null;
    }
}
