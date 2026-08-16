using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Contracts;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;
using GTA_GXT_Editor.Utils;

namespace GTA_GXT_Editor.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly IDocumentWorkflow _documentWorkflow;
    private readonly ICharacterMapWorkflow _characterMapWorkflow;
    private readonly IEncounterMetadataProvider _encounterMetadataProvider;
    private readonly EditorSession _session;
    private readonly ILocalizationService _localization;
    private readonly IAppSettingsStore _appSettings;
    private readonly IInstallerExportService _installerExportService;

    private string DocumentFileFilter => _localization.Get("Filter.Document");
    private string GxtFileFilter => _localization.Get("Filter.Gxt");
    private string TxdFileFilter => _localization.Get("Filter.Txd");
    private string ByxFileFilter => _localization.Get("Filter.Byx");
    private string JsonFileFilter => _localization.Get("Filter.Json");
    private string CharacterMapFileFilter => _localization.Get("Filter.CharacterMap");
    private string CommentsFileFilter => _localization.Get("Filter.Comments");
    private string InstallerFileFilter => _localization.Get("Filter.Installer");

    private CommonGXTManager? _manager => _session.Manager;
    private EditorProject? _project => _session.Project;
    private GXTType _loadedType => _session.LoadedType;
    private EncounterMetadataIndex? _canonicalMetadata
    {
        get => _session.CanonicalMetadata;
        set => _session.CanonicalMetadata = value;
    }

    private bool _canonicalMetadataWarningShown;
    private CancellationTokenSource? _operationCancellation;
    private string? _statusResourceKey;
    private object?[] _statusArguments = [];
    private string? _busyResourceKey;

    public MainWindowViewModel(
        GxtManagerFactory managerFactory,
        IDialogService dialogs,
        ITxdReader? txdReader = null,
        IProjectSerializer? projectSerializer = null,
        IEncounterMetadataProvider? encounterMetadataProvider = null,
        IDocumentWorkflow? documentWorkflow = null,
        ICharacterMapWorkflow? characterMapWorkflow = null,
        EditorSession? session = null,
        ILocalizationService? localization = null,
        IAppSettingsStore? appSettings = null,
        IInstallerExportService? installerExportService = null)
    {
        _dialogs = dialogs;
        _localization = localization ?? LocalizationProvider.Current;
        _appSettings = appSettings ?? new JsonAppSettingsStore();
        _installerExportService = installerExportService ?? new InnoInstallerExportService();
        var resolvedTxdReader = txdReader ?? new TxdReader();
        var resolvedProjectSerializer = projectSerializer ??
            new ByxProjectSerializer(managerFactory, resolvedTxdReader);
        _documentWorkflow = documentWorkflow ??
            new DocumentWorkflow(managerFactory, resolvedProjectSerializer);
        _characterMapWorkflow = characterMapWorkflow ??
            new CharacterMapWorkflow(resolvedTxdReader);
        _encounterMetadataProvider = encounterMetadataProvider ?? new BundledEncounterMetadataProvider();
        _session = session ?? new EditorSession();
        EntryList = new EntryListViewModel(_session, _localization);
        documentType = _localization.Get("Status.NoFile");
        _statusResourceKey = "Status.OpenPrompt";
        statusText = _localization.Get(_statusResourceKey);
        _localization.LanguageChanged += OnLanguageChanged;
        EntryList.PropertyChanged += OnEntryListPropertyChanged;
        EntryList.ComparisonColumns.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsComparisonLoaded));
            OnPropertyChanged(nameof(HasEnglishSource));
        };
    }

    public EntryListViewModel EntryList { get; }

    public IReadOnlyList<UiLanguageOption> SupportedUiLanguages =>
        _localization.SupportedLanguages;

    public UiLanguageOption CurrentUiLanguage => _localization.CurrentLanguage;

    public ObservableCollection<GxtEntryRow> Entries => EntryList.Entries;

    public ObservableCollection<GxtComparisonColumn> ComparisonColumns => EntryList.ComparisonColumns;

    public ObservableCollection<MetadataBlockFilterOption> MetadataBlockOptions =>
        EntryList.MetadataBlockOptions;

    public ICollectionView EntriesView => EntryList.EntriesView;

    public IReadOnlyList<SearchColumnOption> SearchColumns => EntryList.SearchColumns;

    public IReadOnlyList<MetadataTypeFilterOption> MetadataTypeOptions =>
        EntryList.MetadataTypeOptions;

    public IReadOnlyList<EntrySortOption> SortOptions => EntryList.SortOptions;

    public bool IsComparisonLoaded => ComparisonColumns.Count > 0;

    public bool HasEnglishSource => ComparisonColumns.Count > 0;

    public bool HasEncounterMetadata => EntryList.HasEncounterMetadata;

    public int FilteredEntryCount => EntryList.FilteredEntryCount;

    public string FilteredEntriesText => EntryList.FilteredEntriesText;

    public bool HasTxdAttachment => AttachedTxd is not null;

    public bool CanAttachTxd => IsDocumentLoaded && _loadedType == GXTType.GtaViceCity;

    [ObservableProperty]
    private string gxtPath = string.Empty;

    [ObservableProperty]
    private string projectPath = string.Empty;

    [ObservableProperty]
    private string gxtSourceName = string.Empty;

    public bool IsProjectDirty => _session.IsDirty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string busyText = string.Empty;

    public string SearchText
    {
        get => EntryList.SearchText;
        set => EntryList.SearchText = value;
    }

    public SearchColumnOption SelectedSearchColumn
    {
        get => EntryList.SelectedSearchColumn;
        set => EntryList.SelectedSearchColumn = value;
    }

    public MetadataTypeFilterOption SelectedMetadataType
    {
        get => EntryList.SelectedMetadataType;
        set => EntryList.SelectedMetadataType = value;
    }

    public MetadataBlockFilterOption SelectedMetadataBlock
    {
        get => EntryList.SelectedMetadataBlock;
        set => EntryList.SelectedMetadataBlock = value;
    }

    public EntrySortOption SelectedSortOption
    {
        get => EntryList.SelectedSortOption;
        set => EntryList.SelectedSortOption = value;
    }

    public string CommentDraft
    {
        get => EntryList.CommentDraft;
        set => EntryList.CommentDraft = value;
    }

    public bool CaseSensitive
    {
        get => EntryList.CaseSensitive;
        set => EntryList.CaseSensitive = value;
    }

    public bool LiveSearch
    {
        get => EntryList.LiveSearch;
        set => EntryList.LiveSearch = value;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportJsonCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddEntryCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenComparisonFileCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddMissingEntriesCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConvertDictionaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddTxdCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveProjectAsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportGxtCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommentsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommentsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportInstallerCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleReviewedCommand))]
    private bool isDocumentLoaded;

    public GxtEntryRow? SelectedEntry
    {
        get => EntryList.SelectedEntry;
        set => EntryList.SelectedEntry = value;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveTxdCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportTxdCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportInstallerCommand))]
    private TxdAttachment? attachedTxd;

    [ObservableProperty]
    private string documentType = string.Empty;

    [ObservableProperty]
    private string statusText = string.Empty;

    [RelayCommand]
    private void SelectUiLanguage(UiLanguageOption? language)
    {
        if (language is null)
        {
            return;
        }

        _localization.SetLanguage(language.CultureName);
        try
        {
            _appSettings.SaveUiLanguage(_localization.CurrentLanguage.CultureName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError(
                _localization.Format("Settings.SaveError", exception.Message),
                _localization.Get("Common.Error"));
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_statusResourceKey is not null)
        {
            StatusText = _localization.Format(_statusResourceKey, _statusArguments);
        }

        if (_busyResourceKey is not null)
        {
            BusyText = _localization.Get(_busyResourceKey);
        }

        OnPropertyChanged(nameof(CurrentUiLanguage));
        OnPropertyChanged(nameof(SupportedUiLanguages));
        OnPropertyChanged(nameof(AttachedTxdDisplayName));
        OnPropertyChanged(nameof(TxdAttachmentStatus));
    }

    private void SetStatus(string resourceKey, params object?[] arguments)
    {
        _statusResourceKey = resourceKey;
        _statusArguments = arguments;
        StatusText = _localization.Format(resourceKey, arguments);
    }

    public string AttachedTxdDisplayName =>
        AttachedTxd?.DisplayName ?? _localization.Get("Main.TxdNotAttached");

    public string TxdAttachmentStatus =>
        _localization.Format("Main.TxdAttached", HasTxdAttachment);

    public void OpenFromCommandLine(string path)
    {
        if (TryContinueAfterUnsavedChanges())
        {
            OpenDocument(path);
        }
    }

    public async Task OpenFromCommandLineAsync(string path)
    {
        if (TryContinueAfterUnsavedChanges())
        {
            await RunBusyAsync("Busy.OpenDocument", token => OpenDocumentAsync(path, token));
        }
    }

    private void OnEntryListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
        if (e.PropertyName is nameof(EntryListViewModel.SelectedEntry))
        {
            EditEntryCommand.NotifyCanExecuteChanged();
            DeleteEntryCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName is nameof(EntryListViewModel.SelectedEntry) or
            nameof(EntryListViewModel.CommentDraft))
        {
            SaveCommentCommand.NotifyCanExecuteChanged();
            ClearCommentCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName == nameof(EntryListViewModel.SelectedSortOption))
        {
            UpdateStatus();
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    private async Task OpenFileAsync()
    {
        var path = _dialogs.OpenFile(_localization.Get("Dialog.OpenDocument"), DocumentFileFilter);
        if (path is not null && TryContinueAfterUnsavedChanges())
        {
            await RunBusyAsync("Busy.OpenDocument", token => OpenDocumentAsync(path, token));
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    private async Task OpenFileWithDictionaryAsync()
    {
        var path = _dialogs.OpenFile(_localization.Get("Dialog.OpenWithMapping"), GxtFileFilter);
        if (path is null)
        {
            return;
        }

        var dictionaryPath = _dialogs.OpenFile(_localization.Get("Dialog.SelectMapping"), CharacterMapFileFilter);
        if (dictionaryPath is not null && TryContinueAfterUnsavedChanges())
        {
            await RunBusyAsync(
                "Busy.OpenGxt",
                token => LoadDocumentAsync(path, dictionaryPath, cancellationToken: token));
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private async Task OpenComparisonFileAsync()
    {
        var paths = _dialogs.OpenFiles(_localization.Get("Dialog.AddComparison"), GxtFileFilter);
        if (paths.Count == 0)
        {
            return;
        }

        await RunBusyAsync("Busy.OpenComparisons", async token =>
        {
            var loaded = new List<(string Path, Dictionary<GxtEntryIdentity, string> Texts)>();
            foreach (var path in paths)
            {
                token.ThrowIfCancellationRequested();
                if (await TryLoadComparisonFileAsync(path, token) is { } texts)
                {
                    loaded.Add((path, texts));
                }
            }

            if (loaded.Count == 0)
            {
                return;
            }

            token.ThrowIfCancellationRequested();
            foreach (var (path, texts) in loaded)
            {
                EntryList.AddOrReplaceComparison(path, texts);
            }

            OnPropertyChanged(nameof(IsComparisonLoaded));
            OnPropertyChanged(nameof(HasEnglishSource));
            var selected = SelectedEntry;
            RefreshEntries(selected?.Name, selected?.RawTableName);
            SetStatus("Status.ComparisonsLoaded", loaded.Count, ComparisonColumns.Count);
        });
    }

    private async Task<Dictionary<GxtEntryIdentity, string>?> TryLoadComparisonFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var type = _documentWorkflow.DetectType(path);
            if (type != _loadedType)
            {
                _dialogs.ShowError(_localization.Format(
                    "Message.FileTypeMismatch",
                    Path.GetFileName(path)));
                return null;
            }

            var comparisonManager = await _documentWorkflow.OpenRelatedGxtAsync(
                path,
                _session,
                cancellationToken);
            var comparisonTexts = new Dictionary<GxtEntryIdentity, string>();
            foreach (var entry in comparisonManager.GXTEntries)
            {
                var identity = GxtDomainRules.CreateIdentity(
                    _loadedType,
                    entry.DatName.GetClearName(),
                    GxtDomainRules.GetTableName(_loadedType, entry));
                comparisonTexts[identity] = comparisonManager
                    .ConvertBytesToText(entry.Value)
                    .GetClearName();
            }

            return comparisonTexts;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _dialogs.ShowError(
                _localization.Format(
                    "Message.OpenComparisonFailed",
                    Path.GetFileName(path),
                    exception.Message),
                _localization.Get("Dialog.OpenErrorTitle"));
            return null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private async Task ReloadAsync()
    {
        if (!TryContinueAfterUnsavedChanges())
        {
            return;
        }

        if (_project?.ProjectPath is { Length: > 0 } projectPath)
        {
            await RunBusyAsync(
                "Busy.ReloadProject",
                token => LoadProjectAsync(projectPath, token));
            return;
        }

        if (_project?.GxtSourcePath is not { Length: > 0 } sourcePath)
        {
            _dialogs.ShowError(_localization.Get("Message.SourceUnavailable"));
            return;
        }

        var selected = SelectedEntry;
        var dictionaryPath = _manager?.CharacterMapPath;

        await RunBusyAsync(
            "Busy.ReloadGxt",
            token => ReloadGxtInProjectAsync(
                sourcePath,
                dictionaryPath,
                selected?.Name,
                selected?.RawTableName,
                _manager?.Language ?? GxtLanguage.Auto,
                token));
    }

    [RelayCommand]
    private void ApplyFilter()
    {
        EntryList.ApplyFilter();
        UpdateStatus();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        EntryList.ClearSearch();
        UpdateStatus();
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private void AddEntry()
    {
        if (_manager is null)
        {
            return;
        }

        var tables = GetTableOptions();
        var result = _dialogs.EditEntry(new EntryEditorRequest(
            IsAdding: true,
            Name: string.Empty,
            Text: string.Empty,
            RawTableName: tables.Count > 0 ? tables[0].RawName : null,
            Tables: tables));

        if (result is null)
        {
            return;
        }

        if (EntryExists(result.Name, result.RawTableName))
        {
            _dialogs.ShowError(_localization.Format("Message.DuplicateKey", result.Name));
            return;
        }

        _manager.AddGXTEntry(result.Name, result.Text, result.RawTableName);
        SetProjectComment(result.Name, result.RawTableName, result.Comment);
        SetDirty(true);
        RefreshEntries(result.Name, result.RawTableName);
        SetStatus("Status.EntryAdded", result.Name);
    }

    [RelayCommand(CanExecute = nameof(CanUseSelection))]
    private void EditEntry()
    {
        if (_manager is null || SelectedEntry is not { } selected)
        {
            return;
        }

        var result = _dialogs.EditEntry(new EntryEditorRequest(
            IsAdding: false,
            Name: selected.Name,
            Text: selected.Text,
            RawTableName: selected.RawTableName,
            Tables: GetTableOptions())
        {
            SourceText = selected.SourceText,
            Comment = selected.Comment,
            Occurrences = selected.Occurrences,
        });

        if (result is null)
        {
            return;
        }

        _manager.EditGXTEntry(
            selected.Name,
            result.Text,
            selected.RawTableName,
            result.RawTableName);
        MoveProjectEntryMetadata(selected.Name, selected.RawTableName, result.RawTableName);
        SetProjectComment(selected.Name, result.RawTableName, result.Comment);
        SetDirty(true);
        RefreshEntries(selected.Name, result.RawTableName);
        SetStatus("Status.EntryChanged", selected.Name);
    }

    [RelayCommand(CanExecute = nameof(CanSaveComment))]
    private void SaveComment()
    {
        if (SelectedEntry is not { } selected ||
            !SetProjectComment(selected.Name, selected.RawTableName, CommentDraft))
        {
            return;
        }

        SetDirty(true);
        RefreshEntries(selected.Name, selected.RawTableName);
        SetStatus("Status.CommentSaved", selected.Name);
    }

    [RelayCommand(CanExecute = nameof(CanClearComment))]
    private void ClearComment()
    {
        if (SelectedEntry is not { } selected ||
            !SetProjectComment(selected.Name, selected.RawTableName, null))
        {
            return;
        }

        SetDirty(true);
        RefreshEntries(selected.Name, selected.RawTableName);
        SetStatus("Status.CommentCleared", selected.Name);
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private void ToggleReviewed(GxtEntryRow? entry)
    {
        if (entry is null ||
            !EntryList.SetProjectReviewed(entry.Name, entry.RawTableName, !entry.IsReviewed))
        {
            return;
        }

        SetDirty(true);
        RefreshEntries(entry.Name, entry.RawTableName);
    }

    [RelayCommand(CanExecute = nameof(CanUseSelection))]
    private void DeleteEntry()
    {
        if (_manager is null || SelectedEntry is not { } selected)
        {
            return;
        }

        if (!_dialogs.Confirm(
                _localization.Format("Message.DeleteEntry", selected.Name),
                _localization.Get("Message.DeleteEntryTitle")))
        {
            return;
        }

        _manager.RemoveGXTEntry(selected.Name, selected.RawTableName);
        SetDirty(true);
        RefreshEntries();
        SetStatus("Status.EntryDeleted", selected.Name);
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private Task SaveAsync() => RunBusyAsync(
        "Busy.SaveDocument",
        async token => _ = await SaveCurrentDocumentAsync(token));

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private Task ExportGxtAsync() => RunBusyAsync(
        "Busy.ExportGxt",
        async token => _ = await SaveGxtAsAsync(markProjectSaved: false, token));

    [RelayCommand(CanExecute = nameof(CanExportInstaller))]
    private Task ExportInstallerAsync()
    {
        if (_project is null || _project.AttachedTxd is null)
        {
            return Task.CompletedTask;
        }

        var sourcePath = _project.ProjectPath ?? _project.GxtSourcePath ?? GxtPath;
        var directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
        var profile = _project.InstallerProfile?.Clone() ?? new InstallerProfile();
        var result = _dialogs.EditInstallerProfile(new InstallerProfileEditorRequest(profile, directory));
        if (result is null)
        {
            return Task.CompletedTask;
        }

        _project.InstallerProfile = result.Profile.Clone();
        SetDirty(true);
        var targetPath = _dialogs.SaveFile(
            _localization.Get("Dialog.ExportInstaller"),
            InstallerFileFilter,
            Path.Combine(directory, result.Profile.OutputFileName));
        if (targetPath is null)
        {
            return Task.CompletedTask;
        }

        return RunBusyAsync("Busy.ExportInstaller", async token =>
        {
            try
            {
                var snapshot = await _documentWorkflow.CreateSnapshotAsync(_project, token);
                await _installerExportService.BuildAsync(snapshot, targetPath, token);
                SetStatus("Status.InstallerExported", targetPath);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _dialogs.ShowError(_localization.Format("Message.ExportInstallerFailed", exception.Message));
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private Task SaveProjectAsAsync() => RunBusyAsync(
        "Busy.SaveProject",
        async token => _ = await SaveProjectAsync(forceSaveAs: true, token));

    private bool SaveCurrentDocument()
    {
        if (_project is null || _manager is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(_project.ProjectPath) ||
            AttachedTxd is not null ||
            HasPersistableProjectMetadata())
        {
            return SaveProject(forceSaveAs: false);
        }

        return SaveGxtAs(markProjectSaved: true);
    }

    private bool SaveGxtAs(bool markProjectSaved)
    {
        if (_manager is null || _project is null)
        {
            return false;
        }

        var sourcePath = _project.GxtSourcePath ?? GxtPath;
        var directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
        var suggestedPath = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(_project.GxtSourceName)}_modified.gxt");
        var targetPath = _dialogs.SaveFile(_localization.Get("Dialog.SaveGxt"), GxtFileFilter, suggestedPath);

        if (targetPath is null)
        {
            return false;
        }

        try
        {
            var snapshot = _documentWorkflow.CreateSnapshot(_project);
            _documentWorkflow.SaveGxt(targetPath, snapshot);
            if (markProjectSaved)
            {
                _project.GxtSourcePath = targetPath;
                _project.GxtSourceName = Path.GetFileName(targetPath);
                GxtPath = targetPath;
                GxtSourceName = _project.GxtSourceName;
                SetDirty(false);
            }

            SetStatus("Status.Saved", targetPath);
            OfferCharacterMapExport(targetPath);
            return true;
        }
        catch (Exception exception)
        {
            _dialogs.ShowError(_localization.Format("Message.SaveFailed", exception.Message));
            return false;
        }
    }

    private bool SaveProject(bool forceSaveAs)
    {
        if (_project is null)
        {
            return false;
        }

        var targetPath = forceSaveAs ? null : _project.ProjectPath;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            var sourcePath = _project.GxtSourcePath ?? GxtPath;
            var directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
            var suggestedPath = Path.Combine(
                directory,
                $"{Path.GetFileNameWithoutExtension(_project.GxtSourceName)}.byx");
            targetPath = _dialogs.SaveFile(_localization.Get("Dialog.SaveProject"), ByxFileFilter, suggestedPath);
        }

        if (targetPath is null)
        {
            return false;
        }

        try
        {
            var snapshot = _documentWorkflow.CreateSnapshot(_project);
            _documentWorkflow.SaveProject(targetPath, snapshot);
            _project.ProjectPath = targetPath;
            ProjectPath = targetPath;
            SetDirty(false);
            SetStatus("Status.ProjectSaved", targetPath);
            return true;
        }
        catch (Exception exception)
        {
            _dialogs.ShowError(_localization.Format("Message.SaveProjectFailed", exception.Message));
            return false;
        }
    }

    private Task<bool> SaveCurrentDocumentAsync(CancellationToken cancellationToken)
    {
        if (_project is null || _manager is null)
        {
            return Task.FromResult(false);
        }

        return !string.IsNullOrWhiteSpace(_project.ProjectPath) ||
               AttachedTxd is not null ||
               HasPersistableProjectMetadata()
            ? SaveProjectAsync(forceSaveAs: false, cancellationToken)
            : SaveGxtAsAsync(markProjectSaved: true, cancellationToken);
    }

    private async Task<bool> SaveGxtAsAsync(
        bool markProjectSaved,
        CancellationToken cancellationToken)
    {
        if (_manager is null || _project is null)
        {
            return false;
        }

        var sourcePath = _project.GxtSourcePath ?? GxtPath;
        var directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
        var suggestedPath = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(_project.GxtSourceName)}_modified.gxt");
        var targetPath = _dialogs.SaveFile(_localization.Get("Dialog.SaveGxt"), GxtFileFilter, suggestedPath);
        if (targetPath is null)
        {
            return false;
        }

        try
        {
            var snapshot = await _documentWorkflow.CreateSnapshotAsync(
                _project,
                cancellationToken);
            await _documentWorkflow.SaveGxtAsync(
                targetPath,
                snapshot,
                cancellationToken);
            if (markProjectSaved)
            {
                _project.GxtSourcePath = targetPath;
                _project.GxtSourceName = Path.GetFileName(targetPath);
                GxtPath = targetPath;
                GxtSourceName = _project.GxtSourceName;
                SetDirty(false);
            }

            SetStatus("Status.Saved", targetPath);
            OfferCharacterMapExport(targetPath);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _dialogs.ShowError(_localization.Format("Message.SaveFailed", exception.Message));
            return false;
        }
    }

    private async Task<bool> SaveProjectAsync(
        bool forceSaveAs,
        CancellationToken cancellationToken)
    {
        if (_project is null)
        {
            return false;
        }

        var targetPath = forceSaveAs ? null : _project.ProjectPath;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            var sourcePath = _project.GxtSourcePath ?? GxtPath;
            var directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
            var suggestedPath = Path.Combine(
                directory,
                $"{Path.GetFileNameWithoutExtension(_project.GxtSourceName)}.byx");
            targetPath = _dialogs.SaveFile(_localization.Get("Dialog.SaveProject"), ByxFileFilter, suggestedPath);
        }

        if (targetPath is null)
        {
            return false;
        }

        try
        {
            var snapshot = await _documentWorkflow.CreateSnapshotAsync(
                _project,
                cancellationToken);
            await _documentWorkflow.SaveProjectAsync(
                targetPath,
                snapshot,
                cancellationToken);
            _project.ProjectPath = targetPath;
            ProjectPath = targetPath;
            SetDirty(false);
            SetStatus("Status.ProjectSaved", targetPath);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _dialogs.ShowError(_localization.Format("Message.SaveProjectFailed", exception.Message));
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddTxd))]
    private async Task AddTxdAsync()
    {
        var path = _dialogs.OpenFile(_localization.Get("Dialog.AttachTxd"), TxdFileFilter);
        if (path is null)
        {
            return;
        }

        var existing = AttachedTxd;
        if (existing is not null &&
            !string.Equals(existing.SourcePath, path, StringComparison.OrdinalIgnoreCase) &&
            !_dialogs.Confirm(
                _localization.Format(
                    "Message.ReplaceTxd",
                    existing.DisplayName,
                    Path.GetFileName(path)),
                _localization.Get("Message.ReplaceTxdTitle")))
        {
            return;
        }

        await RunBusyAsync("Busy.ReadTxd", async token =>
        {
            try
            {
                var attachment = await _characterMapWorkflow.LoadAttachmentAsync(
                    path,
                    existing?.Id,
                    token);

                if (_project is not null)
                {
                    _project.CharacterMap ??=
                        _manager?.CharacterMap.Clone() ?? new CharacterMapProfile();
                    _project.CharacterMap.IsVerified = false;
                    _project.AttachedTxd = attachment;
                }

                AttachedTxd = attachment;
                OnTxdAttachmentChanged();
                SetDirty(true);
                SetStatus(
                    "Status.TxdAttached",
                    attachment.OriginalFileName,
                    attachment.Document.Textures.Count);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _dialogs.ShowError(_localization.Format("Message.AttachTxdFailed", exception.Message));
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanUseAttachedTxd))]
    private void RemoveTxd()
    {
        if (AttachedTxd is not { } selected ||
            !_dialogs.Confirm(
                _localization.Format("Message.RemoveTxd", selected.DisplayName),
                _localization.Get("Message.RemoveTxdTitle")))
        {
            return;
        }

        AttachedTxd = null;
        if (_project is not null)
        {
            _project.AttachedTxd = null;
            if (_project.CharacterMap is not null)
            {
                _project.UsesCustomDictionary = true;
            }
        }

        OnTxdAttachmentChanged();
        SetDirty(true);
        SetStatus("Status.TxdRemoved", selected.OriginalFileName);
    }

    [RelayCommand(CanExecute = nameof(CanUseAttachedTxd))]
    private async Task ExportTxdAsync()
    {
        if (AttachedTxd is not { } selected)
        {
            return;
        }

        var source = selected.SourcePath ?? _project?.ProjectPath ?? Environment.CurrentDirectory;
        var directory = Path.GetDirectoryName(source) ?? Environment.CurrentDirectory;
        var targetPath = _dialogs.SaveFile(
            _localization.Get("Dialog.ExportTxd"),
            TxdFileFilter,
            Path.Combine(directory, selected.OriginalFileName));
        if (targetPath is null)
        {
            return;
        }

        await RunBusyAsync("Busy.ExportTxd", async token =>
        {
            try
            {
                await _characterMapWorkflow.ExportAttachmentAsync(targetPath, selected, token);
                SetStatus("Status.TxdExported", targetPath);
                OfferCharacterMapExport(targetPath);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _dialogs.ShowError(_localization.Format("Message.ExportTxdFailed", exception.Message));
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanViewTxd))]
    private void ViewTxd()
    {
        if (_manager is null || _project is null || AttachedTxd is null)
        {
            return;
        }

        var profile = _project.CharacterMap?.Clone() ?? _manager.CharacterMap.Clone();
        var result = _dialogs.EditCharacterMap(new CharacterMapEditorRequest(
            AttachedTxd,
            _project.GameType,
            profile,
            Entries.Select(entry => entry.Text).ToArray(),
            _manager.GXTEntries.Select(entry => entry.Value.ToArray()).ToArray(),
            _manager.Language));
        if (result is null)
        {
            return;
        }

        var preview = _characterMapWorkflow.Preview(_manager, result.Profile, result.ApplyMode);
        if (!preview.CanApply)
        {
            _dialogs.ShowError(
                _localization.Format(
                    "Message.MappingCannotApply",
                    string.Join(Environment.NewLine, preview.Issues)),
                _localization.Get("Message.MappingValidationTitle"));
            return;
        }

        var operation = _localization.Get(result.ApplyMode == CharacterMapApplyMode.Interpret
            ? "Message.MappingOperationInterpret"
            : "Message.MappingOperationReencode");
        var changeDetails = preview.Changes.Count == 0
            ? _localization.Get("Message.NoDifferences")
            : string.Join(Environment.NewLine, preview.Changes.Take(10)) +
              (preview.Changes.Count > 10
                  ? _localization.Format("Message.MoreChanges", preview.Changes.Count - 10)
                  : string.Empty);
        if (!_dialogs.Confirm(
                _localization.Format(
                    "Message.ApplyMapping",
                    operation,
                    preview.ChangedEntryCount,
                    preview.ChangedByteCount,
                    changeDetails + Environment.NewLine + Environment.NewLine +
                    _localization.Get("Message.ApplyMappingQuestion")),
                _localization.Get("Message.ApplyMappingTitle")))
        {
            return;
        }

        try
        {
            _characterMapWorkflow.Apply(_manager, result.Profile, result.ApplyMode);
            _project.CharacterMap = result.Profile.Clone();
            _project.CharacterMap.IsVerified = true;
            _project.UsesCustomDictionary = true;
            SetDirty(true);
            RefreshEntries();
            if (result.ApplyMode == CharacterMapApplyMode.Interpret)
            {
                SetStatus("Status.MappingApplied");
            }
            else
            {
                SetStatus("Status.GxtReencoded", preview.ChangedByteCount);
            }
        }
        catch (Exception exception)
        {
            _dialogs.ShowError(_localization.Format("Message.ApplyMappingFailed", exception.Message));
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private async Task ExportJsonAsync()
    {
        var sourcePath = _project?.GxtSourcePath ?? _project?.ProjectPath ?? GxtPath;
        var directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
        var sourceName = _project?.GxtSourceName ?? Path.GetFileName(GxtPath);
        var suggestedPath = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(sourceName)}.json");
        var targetPath = _dialogs.SaveFile(
            _localization.Get("Dialog.ExportJson"),
            JsonFileFilter,
            suggestedPath);

        if (targetPath is null)
        {
            return;
        }

        var entries = Entries.ToArray();
        await RunBusyAsync("Busy.ExportJson", async token =>
        {
            try
            {
                await BackgroundOperation.Run(
                    () => GxtJsonExporter.Export(
                        targetPath,
                        sourceName,
                        DocumentType,
                        entries,
                        _manager?.Language ?? GxtLanguage.Auto),
                    token);
                SetStatus("Status.JsonExported", targetPath);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _dialogs.ShowError(_localization.Format("Message.ExportJsonFailed", exception.Message));
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private async Task ImportCommentsAsync()
    {
        if (_project is null)
        {
            return;
        }

        var sourcePath = _dialogs.OpenFile(_localization.Get("Dialog.ImportComments"), CommentsFileFilter);
        if (sourcePath is null)
        {
            return;
        }

        var selected = SelectedEntry;
        await RunBusyAsync("Busy.ImportComments", async token =>
        {
            try
            {
                var snapshot = await _documentWorkflow.CreateSnapshotAsync(_project, token);
                var result = await BackgroundOperation.Run(
                    () => GxtCommentsImporter.Import(sourcePath, snapshot),
                    token);
                token.ThrowIfCancellationRequested();
                if (result.ChangedEntryCount > 0)
                {
                    _project.Metadata = snapshot.Metadata;
                    RemoveEmptyProjectMetadataEntries();
                    SetDirty(true);
                    RefreshEntries(selected?.Name, selected?.RawTableName);
                }

                var summary = _localization.Format(
                    "Message.CommentsSummary",
                    result.UpdatedEntryCount,
                    result.ClearedEntryCount,
                    result.UnchangedEntryCount,
                    result.MissingEntries.Count,
                    result.TextMismatches.Count);
                SetStatus("Status.CommentsImported", summary);
                _dialogs.ShowInfo(summary, _localization.Get("Dialog.ImportComments"));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _dialogs.ShowError(_localization.Format("Message.ImportCommentsFailed", exception.Message));
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private async Task ExportCommentsAsync()
    {
        if (_project is null)
        {
            return;
        }

        var sourcePath = _project.GxtSourcePath ?? _project.ProjectPath ?? GxtPath;
        var directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
        var targetPath = _dialogs.SaveFile(
            _localization.Get("Dialog.ExportComments"),
            CommentsFileFilter,
            Path.Combine(
                directory,
                $"{Path.GetFileNameWithoutExtension(_project.GxtSourceName)}.comments.json"));
        if (targetPath is null)
        {
            return;
        }

        await RunBusyAsync("Busy.ExportComments", async token =>
        {
            try
            {
                var snapshot = await _documentWorkflow.CreateSnapshotAsync(_project, token);
                await BackgroundOperation.Run(
                    () => GxtCommentsExporter.Export(targetPath, snapshot, includeText: true),
                    token);
                SetStatus("Status.CommentsExported", targetPath);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _dialogs.ShowError(_localization.Format("Message.ExportCommentsFailed", exception.Message));
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    private Task ImportJsonAsync() => ImportJsonAsync(useCustomDictionary: false);

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    private Task ImportJsonWithDictionaryAsync() => ImportJsonAsync(useCustomDictionary: true);

    private async Task ImportJsonAsync(bool useCustomDictionary)
    {
        if (!TryContinueAfterUnsavedChanges())
        {
            return;
        }

        var sourcePath = _dialogs.OpenFile(_localization.Get("Dialog.ConvertJson"), JsonFileFilter);
        if (sourcePath is null)
        {
            return;
        }

        string? dictionaryPath = null;
        if (useCustomDictionary)
        {
            dictionaryPath = _dialogs.OpenFile(_localization.Get("Dialog.SelectMapping"), CharacterMapFileFilter);
            if (dictionaryPath is null)
            {
                return;
            }
        }

        var directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
        var suggestedPath = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(sourcePath)}.gxt");
        var targetPath = _dialogs.SaveFile(
            _localization.Get("Dialog.SaveConvertedGxt"),
            GxtFileFilter,
            suggestedPath);
        if (targetPath is null)
        {
            return;
        }

        await RunBusyAsync("Busy.ConvertJson", async token =>
        {
            try
            {
                var result = await BackgroundOperation.Run(
                    () => GxtJsonImporter.Import(sourcePath, targetPath, dictionaryPath),
                    token);
                if (await LoadDocumentAsync(
                        targetPath,
                        dictionaryPath: dictionaryPath,
                        language: result.Language,
                        cancellationToken: token))
                {
                    var game = GxtDomainRules.ToGameName(result.Type);
                    SetStatus("Status.JsonConverted", game, result.EntryCount);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _dialogs.ShowError(_localization.Format("Message.ConvertJsonFailed", exception.Message));
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private async Task AddMissingEntriesAsync()
    {
        if (_manager is null || _project is null)
        {
            return;
        }

        var gameName = GxtDomainRules.ToGameName(_loadedType);
        var path = _dialogs.OpenFile(
            _localization.Format("Dialog.AddMissing", gameName),
            _localization.Format("Filter.GameGxt", gameName));
        if (path is null)
        {
            return;
        }

        await RunBusyAsync("Busy.ReadSourceGxt", async token =>
        {
            try
            {
                if (_documentWorkflow.DetectType(path) != _loadedType)
                {
                    _dialogs.ShowError(_localization.Get("Message.SelectedGxtTypeMismatch"));
                    return;
                }

                var sourceManager = await _documentWorkflow.OpenRelatedGxtAsync(
                    path,
                    _session,
                    token);
                var workingProject = await _documentWorkflow.CreateSnapshotAsync(
                    _project,
                    token);
                var workingManager = workingProject.GxtManager;
                var missingEntries = sourceManager.GXTEntries
                    .Except(workingManager.GXTEntries, new GXTEntryEqualityComparer())
                    .ToList();

                foreach (var entry in missingEntries)
                {
                    var text = sourceManager.ConvertBytesToText(entry.Value);
                    var table = (entry as GTAVC.GXTEntry)?.TableName;
                    workingManager.AddGXTEntry(entry.DatName.GetClearName(), text, table);
                }

                if (missingEntries.Count > 0)
                {
                    token.ThrowIfCancellationRequested();
                    workingProject.IsDirty = true;
                    var selected = SelectedEntry;
                    CommitProject(
                        workingProject,
                        selected?.Name,
                        selected?.RawTableName,
                        clearTransientState: false);
                }
                _dialogs.ShowInfo(_localization.Format("Message.MissingEntriesAdded", missingEntries.Count));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _dialogs.ShowError(_localization.Format("Message.AddEntriesFailed", exception.Message));
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private async Task ConvertDictionaryAsync()
    {
        if (_manager is null || _project is null)
        {
            return;
        }

        var targetPath = _dialogs.OpenFile(
            _localization.Get("Dialog.SelectTargetMapping"),
            CharacterMapFileFilter);
        if (targetPath is null)
        {
            return;
        }

        await RunBusyAsync("Busy.ConvertMapping", async token =>
        {
            try
            {
                var workingProject = await _documentWorkflow.CreateSnapshotAsync(
                    _project,
                    token);
                var convertedProfile = await _characterMapWorkflow.ConvertAsync(
                    workingProject.GxtManager,
                    targetPath,
                    token);
                token.ThrowIfCancellationRequested();
                workingProject.UsesCustomDictionary = true;
                workingProject.CharacterMap = convertedProfile;
                workingProject.IsDirty = true;
                var selected = SelectedEntry;
                CommitProject(
                    workingProject,
                    selected?.Name,
                    selected?.RawTableName,
                    clearTransientState: false);
                _dialogs.ShowInfo(_localization.Get("Message.MappingConverted"));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _dialogs.ShowError(_localization.Format("Message.ConvertMappingFailed", exception.Message));
            }
        });
    }

    private bool LoadDocument(
        string path,
        string? dictionaryPath = null,
        string? selectedName = null,
        string? selectedTable = null,
        GxtLanguage language = GxtLanguage.Auto)
    {
        try
        {
            var project = _documentWorkflow.OpenGxt(path, dictionaryPath, language);
            CommitProject(project, selectedName, selectedTable, clearTransientState: true);
            SetStatus("Status.Opened", Path.GetFileName(path), Entries.Count);
            return true;
        }
        catch (Exception exception)
        {
            _dialogs.ShowError(
                _localization.Format("Message.OpenFailed", Path.GetFileName(path), exception.Message),
                _localization.Get("Dialog.OpenErrorTitle"));
            return false;
        }
    }

    private async Task<bool> LoadDocumentAsync(
        string path,
        string? dictionaryPath = null,
        string? selectedName = null,
        string? selectedTable = null,
        GxtLanguage language = GxtLanguage.Auto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var project = await _documentWorkflow.OpenGxtAsync(
                path,
                dictionaryPath,
                language,
                cancellationToken);
            CommitProject(project, selectedName, selectedTable, clearTransientState: true);
            SetStatus("Status.Opened", Path.GetFileName(path), Entries.Count);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _dialogs.ShowError(
                _localization.Format("Message.OpenFailed", Path.GetFileName(path), exception.Message),
                _localization.Get("Dialog.OpenErrorTitle"));
            return false;
        }
    }

    private void RefreshEntries(string? selectedName = null, string? selectedTable = null)
    {
        EntryList.RefreshEntries(selectedName, selectedTable);
        OnPropertyChanged(nameof(HasEncounterMetadata));
        UpdateStatus();
    }

    private List<TableOption> GetTableOptions()
        => EntryList.GetTableOptions();

    private bool EntryExists(string name, string? rawTableName)
        => EntryList.EntryExists(name, rawTableName);

    private bool SetProjectComment(string key, string? rawTableName, string? comment)
        => EntryList.SetProjectComment(key, rawTableName, comment);

    private void MoveProjectEntryMetadata(
        string key,
        string? oldRawTableName,
        string? newRawTableName)
        => EntryList.MoveProjectEntryMetadata(key, oldRawTableName, newRawTableName);

    private void RemoveEmptyProjectMetadataEntries()
        => EntryList.RemoveEmptyProjectMetadataEntries();

    private bool HasPersistableProjectMetadata() =>
        EntryList.HasPersistableProjectMetadata();

    private bool CanSaveComment() => !IsBusy && EntryList.CanSaveComment();

    private bool CanClearComment() => !IsBusy && EntryList.CanClearComment();

    private void ClearComparisons()
    {
        EntryList.ClearComparisons();
        OnPropertyChanged(nameof(IsComparisonLoaded));
        OnPropertyChanged(nameof(HasEnglishSource));
    }

    private void OpenDocument(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".byx":
                LoadProject(path);
                break;
            case ".gxt":
                LoadDocument(path);
                break;
            default:
                _dialogs.ShowError(_localization.Get("Message.UnsupportedDocument"));
                break;
        }
    }

    private async Task OpenDocumentAsync(string path, CancellationToken cancellationToken)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".byx":
                _ = await LoadProjectAsync(path, cancellationToken);
                break;
            case ".gxt":
                _ = await LoadDocumentAsync(path, cancellationToken: cancellationToken);
                break;
            default:
                _dialogs.ShowError(_localization.Get("Message.UnsupportedDocument"));
                break;
        }
    }

    private bool LoadProject(string path)
    {
        try
        {
            var project = _documentWorkflow.OpenProject(path);
            CommitProject(project, clearTransientState: true);
            SetStatus(
                "Status.ProjectOpened",
                Path.GetFileName(path),
                Entries.Count,
                _localization.Format("Status.TxdCount", AttachedTxd is null ? 0 : 1));
            return true;
        }
        catch (Exception exception)
        {
            _dialogs.ShowError(
                _localization.Format("Message.OpenProjectFailed", Path.GetFileName(path), exception.Message),
                _localization.Get("Message.OpenProjectErrorTitle"));
            return false;
        }
    }

    private async Task<bool> LoadProjectAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var project = await _documentWorkflow.OpenProjectAsync(path, cancellationToken);
            CommitProject(project, clearTransientState: true);
            SetStatus(
                "Status.ProjectOpened",
                Path.GetFileName(path),
                Entries.Count,
                _localization.Format("Status.TxdCount", AttachedTxd is null ? 0 : 1));
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _dialogs.ShowError(
                _localization.Format("Message.OpenProjectFailed", Path.GetFileName(path), exception.Message),
                _localization.Get("Message.OpenProjectErrorTitle"));
            return false;
        }
    }

    private async Task<bool> ReloadGxtInProjectAsync(
        string path,
        string? dictionaryPath,
        string? selectedName,
        string? selectedTable,
        GxtLanguage language,
        CancellationToken cancellationToken)
    {
        try
        {
            var project = await _documentWorkflow.OpenGxtAsync(
                path,
                dictionaryPath,
                language,
                cancellationToken);
            CommitProject(project, selectedName, selectedTable, clearTransientState: false);
            SetStatus("Status.Reloaded", Path.GetFileName(path));
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _dialogs.ShowError(_localization.Format("Message.ReloadFailed", exception.Message));
            return false;
        }
    }

    private void CommitProject(
        EditorProject project,
        string? selectedName = null,
        string? selectedTable = null,
        bool clearTransientState = false)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (clearTransientState)
        {
            ClearComparisons();
        }

        _session.Commit(project, clearComparisons: false);
        try
        {
            _canonicalMetadata = _encounterMetadataProvider.GetIndex(project.GameType);
        }
        catch (Exception exception)
        {
            _canonicalMetadata = null;
            if (!_canonicalMetadataWarningShown)
            {
                _canonicalMetadataWarningShown = true;
                _dialogs.ShowError(
                    _localization.Format("Message.CanonicalMetadataUnavailable", exception.Message),
                    _localization.Get("Message.MetadataTitle"));
            }
        }

        if (clearTransientState)
        {
            SelectedMetadataType = MetadataTypeOptions[0];
            SelectedMetadataBlock = MetadataBlockOptions[0];
            SelectedSortOption = project.GameType == GXTType.GtaViceCity
                ? SortOptions[0]
                : SortOptions[1];
        }

        GxtSourceName = project.GxtSourceName;
        GxtPath = project.GxtSourcePath ?? project.GxtSourceName;
        ProjectPath = project.ProjectPath ?? string.Empty;
        DocumentType = GxtDomainRules.ToGameName(project.GameType);
        IsDocumentLoaded = true;
        SetDirty(project.IsDirty);
        AttachedTxd = project.AttachedTxd;
        OnTxdAttachmentChanged();
        RefreshEntries(selectedName, selectedTable);
    }

    private void UpdateStatus()
    {
        if (!IsDocumentLoaded)
        {
            return;
        }

        var visibleCount = EntriesView.Cast<object>().Count();
        if (visibleCount == Entries.Count)
        {
            SetStatus("Status.AllEntries", DocumentType, Entries.Count);
        }
        else
        {
            SetStatus("Status.FilteredEntries", visibleCount, Entries.Count);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelOperation))]
    private void CancelOperation() => _operationCancellation?.Cancel();

    private async Task RunBusyAsync(
        string operationName,
        Func<CancellationToken, Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        _busyResourceKey = operationName;
        BusyText = _localization.Get(operationName);
        IsBusy = true;
        try
        {
            await operation(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Status.Cancelled");
        }
        finally
        {
            _operationCancellation = null;
            IsBusy = false;
            _busyResourceKey = null;
            BusyText = string.Empty;
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        OpenFileCommand.NotifyCanExecuteChanged();
        OpenFileWithDictionaryCommand.NotifyCanExecuteChanged();
        OpenComparisonFileCommand.NotifyCanExecuteChanged();
        ReloadCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        ExportGxtCommand.NotifyCanExecuteChanged();
        ExportInstallerCommand.NotifyCanExecuteChanged();
        SaveProjectAsCommand.NotifyCanExecuteChanged();
        AddEntryCommand.NotifyCanExecuteChanged();
        EditEntryCommand.NotifyCanExecuteChanged();
        DeleteEntryCommand.NotifyCanExecuteChanged();
        SaveCommentCommand.NotifyCanExecuteChanged();
        ClearCommentCommand.NotifyCanExecuteChanged();
        AddTxdCommand.NotifyCanExecuteChanged();
        RemoveTxdCommand.NotifyCanExecuteChanged();
        ExportTxdCommand.NotifyCanExecuteChanged();
        ViewTxdCommand.NotifyCanExecuteChanged();
        ExportJsonCommand.NotifyCanExecuteChanged();
        ImportCommentsCommand.NotifyCanExecuteChanged();
        ExportCommentsCommand.NotifyCanExecuteChanged();
        ToggleReviewedCommand.NotifyCanExecuteChanged();
        ImportJsonCommand.NotifyCanExecuteChanged();
        ImportJsonWithDictionaryCommand.NotifyCanExecuteChanged();
        AddMissingEntriesCommand.NotifyCanExecuteChanged();
        ConvertDictionaryCommand.NotifyCanExecuteChanged();
        CancelOperationCommand.NotifyCanExecuteChanged();
    }

    private bool CanStartOperation() => !IsBusy;

    private bool CanUseDocument() => !IsBusy && IsDocumentLoaded && _manager is not null;

    private bool CanExportInstaller() =>
        CanUseDocument() && _loadedType == GXTType.GtaViceCity && AttachedTxd is not null;

    private bool CanAddTxd() => !IsBusy && CanAttachTxd;

    private bool CanUseAttachedTxd() => !IsBusy && AttachedTxd is not null;

    private bool CanViewTxd() => !IsBusy && AttachedTxd is not null;

    private bool CanUseSelection() => CanUseDocument() && SelectedEntry is not null;

    private bool CanCancelOperation() => IsBusy && _operationCancellation is not null;

    public bool CanClose()
    {
        if (IsBusy)
        {
            _operationCancellation?.Cancel();
            return false;
        }

        return TryContinueAfterUnsavedChanges();
    }

    private bool TryContinueAfterUnsavedChanges()
    {
        if (!IsProjectDirty)
        {
            return true;
        }

        return _dialogs.ConfirmUnsavedChanges() switch
        {
            UnsavedChangesChoice.Save => SaveCurrentDocument(),
            UnsavedChangesChoice.Discard => true,
            _ => false,
        };
    }

    private void SetDirty(bool value)
    {
        _session.SetDirty(value);
        OnPropertyChanged(nameof(IsProjectDirty));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void OnTxdAttachmentChanged()
    {
        OnPropertyChanged(nameof(HasTxdAttachment));
        OnPropertyChanged(nameof(CanAttachTxd));
        OnPropertyChanged(nameof(AttachedTxdDisplayName));
        OnPropertyChanged(nameof(TxdAttachmentStatus));
        ViewTxdCommand.NotifyCanExecuteChanged();
        AddTxdCommand.NotifyCanExecuteChanged();
        RemoveTxdCommand.NotifyCanExecuteChanged();
        ExportTxdCommand.NotifyCanExecuteChanged();
    }

    private void OfferCharacterMapExport(string exportedPath)
    {
        if (_project?.CharacterMap is not { } profile ||
            !_dialogs.Confirm(
                _localization.Get("Message.ExportMappingOffer"),
                _localization.Get("Message.ExportMappingTitle")))
        {
            return;
        }

        var suggestedPath = Path.ChangeExtension(exportedPath, ".gxtmap.json");
        var targetPath = _dialogs.SaveFile(
            _localization.Get("Dialog.ExportMapping"),
            _localization.Get("Filter.GxtTxdMapping"),
            suggestedPath);
        if (targetPath is null)
        {
            return;
        }

        try
        {
            CharacterMapFileSerializer.Save(targetPath, profile);
            SetStatus("Status.FileAndMappingExported", exportedPath);
        }
        catch (Exception exception)
        {
            _dialogs.ShowError(_localization.Format("Message.ExportMappingFailed", exception.Message));
        }
    }

}
