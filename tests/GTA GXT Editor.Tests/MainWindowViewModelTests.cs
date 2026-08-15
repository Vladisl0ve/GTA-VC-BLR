using System.Text;
using System.Text.Json;
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
        Assert.AreEqual(
            "English source — first comparison",
            viewModel.ComparisonColumns[0].Name);
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
            new[] { "English source — shared", "shared (2)" },
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
    public void ImportJsonWithDictionary_UsesSelectedJsonMapping()
    {
        var mappingPath = Path.Combine(_testDirectory, "characters.gxtmap.json");
        File.WriteAllText(
            mappingPath,
            "{\"Ў\":\"0xC8\"}",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        var jsonPath = WriteJson("custom.json", "Ў", "be");
        var targetPath = Path.Combine(_testDirectory, "custom-import.gxt");
        var dialogs = new FakeDialogService();
        dialogs.OpenFileResults.Enqueue(jsonPath);
        dialogs.OpenFileResults.Enqueue(mappingPath);
        dialogs.SaveFileResults.Enqueue(targetPath);
        var viewModel = CreateViewModel(dialogs);

        viewModel.ImportJsonWithDictionaryCommand.Execute(null);

        Assert.IsTrue(File.Exists(targetPath));
        Assert.HasCount(1, viewModel.Entries);
        Assert.AreEqual("Ў", viewModel.Entries[0].Text);
        Assert.HasCount(2, dialogs.OpenFileCalls);
        StringAssert.Contains(dialogs.OpenFileCalls[1].Title, "маппинг");
        StringAssert.Contains(dialogs.OpenFileCalls[1].Filter, "*.gxtmap.json");
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

    [TestMethod]
    public void ViceCity_UsesCanonicalEncounterOrderAndMetadataFilters()
    {
        var path = CreateViceCityGxtWithEntries(
            "encounter.gxt",
            ("MAIN", "CRED001", "Credits"),
            ("MAIN", "LAW_1", "The Party"),
            ("MAIN", "ITBEG", "In the beginning..."));
        var viewModel = CreateViewModel(new FakeDialogService());

        viewModel.OpenFromCommandLine(path);

        Assert.AreEqual(EntrySortMode.EncounterOrder, viewModel.SelectedSortOption.Mode);
        CollectionAssert.AreEqual(
            new[] { "ITBEG", "LAW_1", "CRED001" },
            viewModel.EntriesView.Cast<GxtEntryRow>().Select(entry => entry.Name).ToArray());
        Assert.AreEqual(
            "story.in-the-beginning",
            viewModel.Entries.Single(entry => entry.Name == "ITBEG").PrimaryOccurrence?.BlockId);
        Assert.AreEqual(
            EncounterMetadataSource.Canonical,
            viewModel.Entries.Single(entry => entry.Name == "LAW_1").PrimaryOccurrence?.Source);
        Assert.IsFalse(viewModel.IsProjectDirty);
        var sourceOrder = viewModel.Entries
            .OrderBy(entry => entry.SourceIndex)
            .Select(entry => entry.Name)
            .ToArray();

        viewModel.SelectedSortOption = viewModel.SortOptions.Single(option =>
            option.Mode == EntrySortMode.GxtOrder);
        CollectionAssert.AreEqual(
            sourceOrder,
            viewModel.EntriesView.Cast<GxtEntryRow>().Select(entry => entry.Name).ToArray());
        viewModel.SelectedSortOption = viewModel.SortOptions.Single(option =>
            option.Mode == EntrySortMode.EncounterOrder);

        viewModel.SelectedMetadataType = viewModel.MetadataTypeOptions.Single(option =>
            option.Type == "mission");
        Assert.AreEqual(
            "LAW_1",
            viewModel.EntriesView.Cast<GxtEntryRow>().Single().Name);

        viewModel.SelectedMetadataBlock = viewModel.MetadataBlockOptions.Single(option =>
            option.Id == "mission.the-party");
        Assert.AreEqual(
            "LAW_1",
            viewModel.EntriesView.Cast<GxtEntryRow>().Single().Name);

        viewModel.ClearSearchCommand.Execute(null);
        viewModel.SelectedSearchColumn = viewModel.SearchColumns.Single(option =>
            option.Column == SearchColumn.Metadata);
        viewModel.SearchText = "The Party";
        Assert.AreEqual(
            "LAW_1",
            viewModel.EntriesView.Cast<GxtEntryRow>().Single().Name);
    }

    [TestMethod]
    public void ProjectOccurrencesOverrideCanonicalWhileCommentOnlyEntryUsesFallback()
    {
        var path = CreateViceCityGxtWithEntries(
            "project.gxt",
            ("MAIN", "LAW_1", "Party"),
            ("MAIN", "ITBEG", "Beginning"));
        var factory = new GxtManagerFactory();
        var project = new EditorProject
        {
            ProjectPath = "virtual.byx",
            GxtSourceName = "project.gxt",
            GameType = GXTType.GtaViceCity,
            GxtManager = factory.Open(path),
            Metadata = new ProjectMetadata
            {
                Blocks =
                [
                    new ProjectMetadataBlock
                    {
                        Id = "project.review",
                        Type = "mission",
                        Name = "Project review",
                        Order = 1,
                    },
                ],
                Entries =
                [
                    new ProjectEntryMetadata
                    {
                        Table = "MAIN",
                        Key = "LAW_1",
                        Comment = "Project comment",
                        Occurrences =
                        [
                            new ProjectEntryOccurrence
                            {
                                BlockId = "project.review",
                                Order = 1,
                                Context = "Custom review",
                            },
                        ],
                    },
                    new ProjectEntryMetadata
                    {
                        Table = "MAIN",
                        Key = "ITBEG",
                        Comment = "Comment only",
                    },
                ],
            },
        };
        var serializer = new StubProjectSerializer(project);
        var viewModel = new MainWindowViewModel(factory, new FakeDialogService(), projectSerializer: serializer);

        viewModel.OpenFromCommandLine("virtual.byx");

        var projectRow = viewModel.Entries.Single(entry => entry.Name == "LAW_1");
        Assert.HasCount(1, projectRow.Occurrences);
        Assert.AreEqual("project.review", projectRow.PrimaryOccurrence?.BlockId);
        Assert.AreEqual(EncounterMetadataSource.Project, projectRow.PrimaryOccurrence?.Source);
        Assert.AreEqual("Project comment", projectRow.Comment);

        var fallbackRow = viewModel.Entries.Single(entry => entry.Name == "ITBEG");
        Assert.AreEqual("story.in-the-beginning", fallbackRow.PrimaryOccurrence?.BlockId);
        Assert.AreEqual(EncounterMetadataSource.Canonical, fallbackRow.PrimaryOccurrence?.Source);
        Assert.AreEqual("Comment only", fallbackRow.Comment);
        Assert.HasCount(2, project.Metadata.Entries);
        Assert.HasCount(1, project.Metadata.Blocks);
    }

    [TestMethod]
    public void FirstComparisonIsEnglishSourceAndIsPassedToEntryEditor()
    {
        var targetPath = CreateViceCityGxtWithEntries(
            "target.gxt",
            ("MAIN", "LAW_1", "Translated"));
        var sourcePath = CreateViceCityGxtWithEntries(
            "american.gxt",
            ("MAIN", "LAW_1", "The Party"));
        var dialogs = new FakeDialogService();
        dialogs.OpenFilesResults.Enqueue([sourcePath]);
        dialogs.EditEntryResults.Enqueue(new EntryEditorResult("LAW_1", "Updated translation", "MAIN")
        {
            Comment = "Check timing",
        });
        var viewModel = CreateViewModel(dialogs);
        viewModel.OpenFromCommandLine(targetPath);
        viewModel.OpenComparisonFileCommand.Execute(null);
        viewModel.SelectedEntry = viewModel.Entries.Single();

        viewModel.EditEntryCommand.Execute(null);

        Assert.AreEqual("English source — american", viewModel.ComparisonColumns[0].Name);
        Assert.AreEqual("The Party", dialogs.EntryEditorRequests.Single().SourceText);
        Assert.IsNotEmpty(dialogs.EntryEditorRequests.Single().Occurrences);
        Assert.AreEqual("The Party", viewModel.Entries.Single().SourceText);
        Assert.AreEqual("Check timing", viewModel.Entries.Single().Comment);

        viewModel.SelectedSearchColumn = viewModel.SearchColumns.Single(option =>
            option.Column == SearchColumn.Source);
        viewModel.SearchText = "The Party";
        Assert.AreEqual("LAW_1", viewModel.EntriesView.Cast<GxtEntryRow>().Single().Name);
    }

    [TestMethod]
    public void ReusedCanonicalKeyExposesEveryOccurrence()
    {
        var path = CreateViceCityGxtWithEntries(
            "reused.gxt",
            ("MAIN", "ICC1_O", "Shared prompt"));
        var viewModel = CreateViewModel(new FakeDialogService());

        viewModel.OpenFromCommandLine(path);

        var row = viewModel.Entries.Single();
        Assert.HasCount(2, row.Occurrences);
        StringAssert.Contains(row.EncounterSummary, "(+1)");
    }

    [TestMethod]
    public void ClearCommentRemovesSparseProjectMetadataFromRow()
    {
        var path = CreateViceCityGxtWithEntries(
            "clear-comment.gxt",
            ("MAIN", "LAW_1", "Party"));
        var viewModel = CreateViewModel(new FakeDialogService());
        viewModel.OpenFromCommandLine(path);
        viewModel.SelectedEntry = viewModel.Entries.Single();
        viewModel.CommentDraft = "Temporary note";
        viewModel.SaveCommentCommand.Execute(null);

        viewModel.ClearCommentCommand.Execute(null);

        Assert.IsNull(viewModel.Entries.Single().Comment);
        Assert.IsTrue(viewModel.IsProjectDirty);
        Assert.IsFalse(viewModel.ClearCommentCommand.CanExecute(null));
    }

    [TestMethod]
    public void CommentOnPlainGxtSavesAsSparseByxMetadata()
    {
        var gxtPath = CreateViceCityGxtWithEntries(
            "comments.gxt",
            ("MAIN", "LAW_1", "Party"));
        var byxPath = Path.Combine(_testDirectory, "comments.byx");
        var dialogs = new FakeDialogService();
        dialogs.SaveFileResults.Enqueue(byxPath);
        var viewModel = CreateViewModel(dialogs);
        viewModel.OpenFromCommandLine(gxtPath);
        viewModel.SelectedEntry = viewModel.Entries.Single();
        viewModel.CommentDraft = "Review against gameplay";

        viewModel.SaveCommentCommand.Execute(null);
        viewModel.SaveCommand.Execute(null);

        Assert.IsTrue(File.Exists(byxPath));
        StringAssert.Contains(dialogs.SaveFileCalls.Single().Filter, "*.byx");
        var loaded = new ByxProjectSerializer(new GxtManagerFactory(), new TxdReader()).Load(byxPath);
        Assert.IsEmpty(loaded.Metadata.Blocks);
        Assert.HasCount(1, loaded.Metadata.Entries);
        Assert.AreEqual("Review against gameplay", loaded.Metadata.Entries[0].Comment);
        Assert.IsEmpty(loaded.Metadata.Entries[0].Occurrences);
    }

    [TestMethod]
    public void CommentImportAndExportCommandsRefreshRowsAndReportTextMismatch()
    {
        var gxtPath = CreateViceCityGxtWithEntries(
            "import-comments.gxt",
            ("MAIN", "LAW_1", "Current text"));
        var importPath = Path.Combine(_testDirectory, "incoming.comments.json");
        var exportPath = Path.Combine(_testDirectory, "outgoing.comments.json");
        File.WriteAllText(
            importPath,
            """
            {
              "format": "GXT_COMMENTS",
              "version": 1,
              "game": "GTA Vice City",
              "entries": [
                {
                  "key": "LAW_1",
                  "table": "MAIN",
                  "text": "Different text",
                  "comment": "Imported note"
                }
              ]
            }
            """,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        var dialogs = new FakeDialogService();
        dialogs.OpenFileResults.Enqueue(importPath);
        dialogs.SaveFileResults.Enqueue(exportPath);
        var viewModel = CreateViewModel(dialogs);
        viewModel.OpenFromCommandLine(gxtPath);

        viewModel.ImportCommentsCommand.Execute(null);
        viewModel.ExportCommentsCommand.Execute(null);

        Assert.AreEqual("Imported note", viewModel.Entries.Single().Comment);
        Assert.IsTrue(viewModel.IsProjectDirty);
        StringAssert.Contains(dialogs.InfoMessages.Single().Message, "текст отличается: 1");
        using var document = JsonDocument.Parse(File.ReadAllBytes(exportPath));
        Assert.AreEqual(
            "Imported note",
            document.RootElement.GetProperty("entries")[0].GetProperty("comment").GetString());
    }

    [TestMethod]
    public void BrokenCanonicalProviderDoesNotPreventOpeningDocument()
    {
        var path = CreateViceCityGxt("fallback.gxt");
        var dialogs = new FakeDialogService { AllowErrors = true };
        var viewModel = new MainWindowViewModel(
            new GxtManagerFactory(),
            dialogs,
            encounterMetadataProvider: new ThrowingEncounterMetadataProvider());

        viewModel.OpenFromCommandLine(path);

        Assert.IsTrue(viewModel.IsDocumentLoaded);
        Assert.IsEmpty(viewModel.Entries.Single().Occurrences);
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
        return CreateViceCityGxtWithEntries(fileName, ("MAIN", "HELLO", text));
    }

    private string CreateViceCityGxtWithEntries(
        string fileName,
        params (string Table, string Key, string Text)[] entries)
    {
        var path = Path.Combine(_testDirectory, fileName);
        var manager = GxtManagerFactory.Create(
            GXTType.GtaViceCity,
            sourceName: fileName,
            sourceTexts: entries.Select(entry => entry.Text),
            language: GxtLanguage.English);
        foreach (var entry in entries)
        {
            manager.AddGXTEntry(entry.Key, entry.Text, entry.Table);
        }

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

        public Queue<EntryEditorResult?> EditEntryResults { get; } = new();

        public List<(string Title, string Filter)> OpenFileCalls { get; } = [];

        public List<(string Title, string Filter)> OpenFilesCalls { get; } = [];

        public List<(string Title, string Filter, string SuggestedPath)> SaveFileCalls { get; } = [];

        public List<EntryEditorRequest> EntryEditorRequests { get; } = [];

        public List<(string Message, string Title)> InfoMessages { get; } = [];

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
            InfoMessages.Add((message, title));
        }

        public void ShowError(string message, string title = "Ошибка")
        {
            Errors.Add((message, title));
            if (!AllowErrors)
            {
                Assert.Fail($"Unexpected error dialog '{title}': {message}");
            }
        }

        public EntryEditorResult? EditEntry(EntryEditorRequest request)
        {
            EntryEditorRequests.Add(request);
            return EditEntryResults.Count > 0 ? EditEntryResults.Dequeue() : null;
        }

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

    private sealed class StubProjectSerializer(EditorProject project) : IProjectSerializer
    {
        public EditorProject Load(string path) => project;

        public void Save(string path, EditorProject value)
        {
        }
    }

    private sealed class ThrowingEncounterMetadataProvider : IEncounterMetadataProvider
    {
        public EncounterMetadataIndex? GetIndex(GXTType gameType) =>
            throw new InvalidDataException("Broken canonical resource.");
    }
}
