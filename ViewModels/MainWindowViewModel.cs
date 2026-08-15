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
    private const string DocumentFileFilter =
        "GXT или проект BYX (*.gxt;*.byx)|*.gxt;*.byx|GXT (*.gxt)|*.gxt|Проект BYX (*.byx)|*.byx|Все файлы (*.*)|*.*";
    private const string GxtFileFilter =
        "GTA III/Vice City GXT (*.gxt)|*.gxt|Все файлы (*.*)|*.*";
    private const string TxdFileFilter =
        "Vice City TXD (*.txd)|*.txd|Все файлы (*.*)|*.*";
    private const string ByxFileFilter =
        "Проект BYX (*.byx)|*.byx|Все файлы (*.*)|*.*";
    private const string JsonFileFilter =
        "JSON (*.json)|*.json|Все файлы (*.*)|*.*";
    private const string CharacterMapFileFilter =
        "Маппинг символов (*.gxtmap.json;*.json;*.txt)|*.gxtmap.json;*.json;*.txt|" +
        "Маппинг JSON (*.gxtmap.json;*.json)|*.gxtmap.json;*.json|" +
        "Старый словарь (*.txt)|*.txt|Все файлы (*.*)|*.*";
    private const string CommentsFileFilter =
        "Комментарии GXT (*.comments.json)|*.comments.json|JSON (*.json)|*.json|Все файлы (*.*)|*.*";

    private readonly IDialogService _dialogs;
    private readonly IDocumentWorkflow _documentWorkflow;
    private readonly ICharacterMapWorkflow _characterMapWorkflow;
    private readonly IEncounterMetadataProvider _encounterMetadataProvider;
    private readonly EditorSession _session;

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

    public MainWindowViewModel(
        GxtManagerFactory managerFactory,
        IDialogService dialogs,
        ITxdReader? txdReader = null,
        IProjectSerializer? projectSerializer = null,
        IEncounterMetadataProvider? encounterMetadataProvider = null,
        IDocumentWorkflow? documentWorkflow = null,
        ICharacterMapWorkflow? characterMapWorkflow = null,
        EditorSession? session = null)
    {
        _dialogs = dialogs;
        var resolvedTxdReader = txdReader ?? new TxdReader();
        var resolvedProjectSerializer = projectSerializer ??
            new ByxProjectSerializer(managerFactory, resolvedTxdReader);
        _documentWorkflow = documentWorkflow ??
            new DocumentWorkflow(managerFactory, resolvedProjectSerializer);
        _characterMapWorkflow = characterMapWorkflow ??
            new CharacterMapWorkflow(resolvedTxdReader);
        _encounterMetadataProvider = encounterMetadataProvider ?? new BundledEncounterMetadataProvider();
        _session = session ?? new EditorSession();
        EntryList = new EntryListViewModel(_session);
        EntryList.PropertyChanged += OnEntryListPropertyChanged;
        EntryList.ComparisonColumns.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsComparisonLoaded));
            OnPropertyChanged(nameof(HasEnglishSource));
        };
    }

    public EntryListViewModel EntryList { get; }

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
    private bool isDocumentLoaded;

    public GxtEntryRow? SelectedEntry
    {
        get => EntryList.SelectedEntry;
        set => EntryList.SelectedEntry = value;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveTxdCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportTxdCommand))]
    private TxdAttachment? attachedTxd;

    [ObservableProperty]
    private string documentType = "Файл не открыт";

    [ObservableProperty]
    private string statusText = "Откройте GXT-файл GTA III или Vice City";

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
            await RunBusyAsync("Открытие документа…", token => OpenDocumentAsync(path, token));
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
        var path = _dialogs.OpenFile("Открыть GXT или проект BYX", DocumentFileFilter);
        if (path is not null && TryContinueAfterUnsavedChanges())
        {
            await RunBusyAsync("Открытие документа…", token => OpenDocumentAsync(path, token));
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    private async Task OpenFileWithDictionaryAsync()
    {
        var path = _dialogs.OpenFile("Открыть GXT-файл с маппингом", GxtFileFilter);
        if (path is null)
        {
            return;
        }

        var dictionaryPath = _dialogs.OpenFile("Выбрать маппинг символов", CharacterMapFileFilter);
        if (dictionaryPath is not null && TryContinueAfterUnsavedChanges())
        {
            await RunBusyAsync(
                "Открытие GXT…",
                token => LoadDocumentAsync(path, dictionaryPath, cancellationToken: token));
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private async Task OpenComparisonFileAsync()
    {
        var paths = _dialogs.OpenFiles("Добавить английский оригинал или GXT для сравнения", GxtFileFilter);
        if (paths.Count == 0)
        {
            return;
        }

        await RunBusyAsync("Открытие файлов сравнения…", async token =>
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
            StatusText = $"Загружено файлов для сравнения: {loaded.Count}; " +
                         $"всего: {ComparisonColumns.Count}";
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
                _dialogs.ShowError(
                    $"Тип файла '{Path.GetFileName(path)}' не соответствует открытому GXT.");
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
                $"Не удалось открыть '{Path.GetFileName(path)}' для сравнения.\n\n{exception.Message}",
                "Ошибка открытия");
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
                "Перезагрузка проекта…",
                token => LoadProjectAsync(projectPath, token));
            return;
        }

        if (_project?.GxtSourcePath is not { Length: > 0 } sourcePath)
        {
            _dialogs.ShowError("Исходный GXT недоступен для перезагрузки.");
            return;
        }

        var selected = SelectedEntry;
        var dictionaryPath = _manager?.CharacterMapPath;

        await RunBusyAsync(
            "Перезагрузка GXT…",
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
            _dialogs.ShowError($"Ключ '{result.Name}' уже существует в выбранной таблице.");
            return;
        }

        _manager.AddGXTEntry(result.Name, result.Text, result.RawTableName);
        SetProjectComment(result.Name, result.RawTableName, result.Comment);
        SetDirty(true);
        RefreshEntries(result.Name, result.RawTableName);
        StatusText = $"Добавлен ключ {result.Name}";
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
        StatusText = $"Изменён ключ {selected.Name}";
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
        StatusText = $"Комментарий для {selected.Name} сохранён";
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
        StatusText = $"Комментарий для {selected.Name} удалён";
    }

    [RelayCommand(CanExecute = nameof(CanUseSelection))]
    private void DeleteEntry()
    {
        if (_manager is null || SelectedEntry is not { } selected)
        {
            return;
        }

        if (!_dialogs.Confirm(
                $"Удалить ключ '{selected.Name}'? Это действие попадёт в файл только после сохранения.",
                "Удаление ключа"))
        {
            return;
        }

        _manager.RemoveGXTEntry(selected.Name, selected.RawTableName);
        SetDirty(true);
        RefreshEntries();
        StatusText = $"Удалён ключ {selected.Name}";
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private Task SaveAsync() => RunBusyAsync(
        "Сохранение документа…",
        async token => _ = await SaveCurrentDocumentAsync(token));

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private Task ExportGxtAsync() => RunBusyAsync(
        "Экспорт GXT…",
        async token => _ = await SaveGxtAsAsync(markProjectSaved: false, token));

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private Task SaveProjectAsAsync() => RunBusyAsync(
        "Сохранение проекта…",
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
        var targetPath = _dialogs.SaveFile("Сохранить GXT-файл", GxtFileFilter, suggestedPath);

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

            StatusText = $"Сохранено: {targetPath}";
            OfferCharacterMapExport(targetPath);
            return true;
        }
        catch (Exception exception)
        {
            _dialogs.ShowError($"Не удалось сохранить файл.\n\n{exception.Message}");
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
            targetPath = _dialogs.SaveFile("Сохранить проект BYX", ByxFileFilter, suggestedPath);
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
            StatusText = $"Проект сохранён: {targetPath}";
            return true;
        }
        catch (Exception exception)
        {
            _dialogs.ShowError($"Не удалось сохранить проект BYX.\n\n{exception.Message}");
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
        var targetPath = _dialogs.SaveFile("Сохранить GXT-файл", GxtFileFilter, suggestedPath);
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

            StatusText = $"Сохранено: {targetPath}";
            OfferCharacterMapExport(targetPath);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _dialogs.ShowError($"Не удалось сохранить файл.\n\n{exception.Message}");
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
            targetPath = _dialogs.SaveFile("Сохранить проект BYX", ByxFileFilter, suggestedPath);
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
            StatusText = $"Проект сохранён: {targetPath}";
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _dialogs.ShowError($"Не удалось сохранить проект BYX.\n\n{exception.Message}");
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddTxd))]
    private async Task AddTxdAsync()
    {
        var path = _dialogs.OpenFile("Подключить или заменить TXD GTA Vice City", TxdFileFilter);
        if (path is null)
        {
            return;
        }

        var existing = AttachedTxd;
        if (existing is not null &&
            !string.Equals(existing.SourcePath, path, StringComparison.OrdinalIgnoreCase) &&
            !_dialogs.Confirm(
                $"Заменить подключённый TXD '{existing.DisplayName}' на '{Path.GetFileName(path)}'? " +
                "Текущий маппинг будет сохранён как непроверенный черновик.",
                "Замена TXD"))
        {
            return;
        }

        await RunBusyAsync("Чтение TXD…", async token =>
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
                StatusText = $"Подключён TXD: {attachment.OriginalFileName} — " +
                             $"{attachment.Document.Textures.Count} текстур";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _dialogs.ShowError($"Не удалось подключить TXD.\n\n{exception.Message}");
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanUseAttachedTxd))]
    private void RemoveTxd()
    {
        if (AttachedTxd is not { } selected ||
            !_dialogs.Confirm($"Удалить '{selected.DisplayName}' из проекта?", "Удаление TXD"))
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
        StatusText = $"TXD удалён из проекта: {selected.OriginalFileName}";
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
            "Экспортировать TXD",
            TxdFileFilter,
            Path.Combine(directory, selected.OriginalFileName));
        if (targetPath is null)
        {
            return;
        }

        await RunBusyAsync("Экспорт TXD…", async token =>
        {
            try
            {
                await _characterMapWorkflow.ExportAttachmentAsync(targetPath, selected, token);
                StatusText = $"TXD экспортирован: {targetPath}";
                OfferCharacterMapExport(targetPath);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _dialogs.ShowError($"Не удалось экспортировать TXD.\n\n{exception.Message}");
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
                "Профиль нельзя применить:\n\n" + string.Join(Environment.NewLine, preview.Issues),
                "Проверка маппинга");
            return;
        }

        var operation = result.ApplyMode == CharacterMapApplyMode.Interpret
            ? "заново интерпретировать исходные байты"
            : "перекодировать текущий Unicode-текст";
        var changeDetails = preview.Changes.Count == 0
            ? "Нет различий в текущих данных."
            : string.Join(Environment.NewLine, preview.Changes.Take(10)) +
              (preview.Changes.Count > 10
                  ? $"{Environment.NewLine}…и ещё {preview.Changes.Count - 10}"
                  : string.Empty);
        if (!_dialogs.Confirm(
                $"Будет выполнено действие: {operation}.\n" +
                $"Затронуто строк: {preview.ChangedEntryCount}; изменено байтов: {preview.ChangedByteCount}.\n\n" +
                changeDetails + "\n\n" +
                "Применить профиль?",
                "Применение маппинга"))
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
            StatusText = result.ApplyMode == CharacterMapApplyMode.Interpret
                ? "Маппинг применён без изменения байтов GXT"
                : $"GXT перекодирован под TXD: изменено байтов {preview.ChangedByteCount}";
        }
        catch (Exception exception)
        {
            _dialogs.ShowError($"Не удалось применить маппинг.\n\n{exception.Message}");
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
            "Экспортировать GXT в JSON",
            JsonFileFilter,
            suggestedPath);

        if (targetPath is null)
        {
            return;
        }

        var entries = Entries.ToArray();
        await RunBusyAsync("Экспорт JSON…", async token =>
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
                StatusText = $"Экспортировано в JSON: {targetPath}";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _dialogs.ShowError($"Не удалось экспортировать JSON.\n\n{exception.Message}");
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

        var sourcePath = _dialogs.OpenFile("Импортировать комментарии", CommentsFileFilter);
        if (sourcePath is null)
        {
            return;
        }

        var selected = SelectedEntry;
        await RunBusyAsync("Импорт комментариев…", async token =>
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

                var summary =
                    $"Обновлено: {result.UpdatedEntryCount}; удалено: {result.ClearedEntryCount}; " +
                    $"без изменений: {result.UnchangedEntryCount}; отсутствует в GXT: " +
                    $"{result.MissingEntries.Count}; текст отличается: {result.TextMismatches.Count}.";
                StatusText = $"Импорт комментариев завершён — {summary}";
                _dialogs.ShowInfo(summary, "Импорт комментариев");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _dialogs.ShowError($"Не удалось импортировать комментарии.\n\n{exception.Message}");
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
            "Экспортировать комментарии",
            CommentsFileFilter,
            Path.Combine(
                directory,
                $"{Path.GetFileNameWithoutExtension(_project.GxtSourceName)}.comments.json"));
        if (targetPath is null)
        {
            return;
        }

        await RunBusyAsync("Экспорт комментариев…", async token =>
        {
            try
            {
                var snapshot = await _documentWorkflow.CreateSnapshotAsync(_project, token);
                await BackgroundOperation.Run(
                    () => GxtCommentsExporter.Export(targetPath, snapshot, includeText: true),
                    token);
                StatusText = $"Комментарии экспортированы: {targetPath}";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _dialogs.ShowError($"Не удалось экспортировать комментарии.\n\n{exception.Message}");
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

        var sourcePath = _dialogs.OpenFile("Преобразовать JSON в GXT", JsonFileFilter);
        if (sourcePath is null)
        {
            return;
        }

        string? dictionaryPath = null;
        if (useCustomDictionary)
        {
            dictionaryPath = _dialogs.OpenFile("Выбрать маппинг символов", CharacterMapFileFilter);
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
            "Сохранить преобразованный GXT",
            GxtFileFilter,
            suggestedPath);
        if (targetPath is null)
        {
            return;
        }

        await RunBusyAsync("Преобразование JSON в GXT…", async token =>
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
                    StatusText = $"JSON преобразован в {game} GXT: {result.EntryCount} ключей";
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _dialogs.ShowError($"Не удалось преобразовать JSON в GXT.\n\n{exception.Message}");
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
            $"Добавить отсутствующие ключи из {gameName}",
            $"{gameName} GXT (*.gxt)|*.gxt|Все файлы (*.*)|*.*");
        if (path is null)
        {
            return;
        }

        await RunBusyAsync("Чтение исходного GXT…", async token =>
        {
            try
            {
                if (_documentWorkflow.DetectType(path) != _loadedType)
                {
                    _dialogs.ShowError("Тип выбранного GXT-файла не соответствует открытому файлу.");
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
                _dialogs.ShowInfo($"Добавлено отсутствующих ключей: {missingEntries.Count}.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _dialogs.ShowError($"Не удалось добавить ключи.\n\n{exception.Message}");
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
            "Выбрать целевой маппинг символов",
            CharacterMapFileFilter);
        if (targetPath is null)
        {
            return;
        }

        await RunBusyAsync("Преобразование маппинга…", async token =>
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
                _dialogs.ShowInfo("Маппинг символов успешно преобразован.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _dialogs.ShowError($"Не удалось преобразовать маппинг.\n\n{exception.Message}");
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
            StatusText = $"Открыт {Path.GetFileName(path)} — {Entries.Count} ключей";
            return true;
        }
        catch (Exception exception)
        {
            _dialogs.ShowError(
                $"Не удалось открыть '{Path.GetFileName(path)}'.\n\n{exception.Message}",
                "Ошибка открытия");
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
            StatusText = $"Открыт {Path.GetFileName(path)} — {Entries.Count} ключей";
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _dialogs.ShowError(
                $"Не удалось открыть '{Path.GetFileName(path)}'.\n\n{exception.Message}",
                "Ошибка открытия");
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
                _dialogs.ShowError("Поддерживаются только файлы GXT и проекты BYX.");
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
                _dialogs.ShowError("Поддерживаются только файлы GXT и проекты BYX.");
                break;
        }
    }

    private bool LoadProject(string path)
    {
        try
        {
            var project = _documentWorkflow.OpenProject(path);
            CommitProject(project, clearTransientState: true);
            StatusText = $"Открыт проект {Path.GetFileName(path)} — {Entries.Count} ключей, " +
                         $"TXD: {(AttachedTxd is null ? 0 : 1)}";
            return true;
        }
        catch (Exception exception)
        {
            _dialogs.ShowError(
                $"Не удалось открыть проект '{Path.GetFileName(path)}'.\n\n{exception.Message}",
                "Ошибка открытия BYX");
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
            StatusText = $"Открыт проект {Path.GetFileName(path)} — {Entries.Count} ключей, " +
                         $"TXD: {(AttachedTxd is null ? 0 : 1)}";
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _dialogs.ShowError(
                $"Не удалось открыть проект '{Path.GetFileName(path)}'.\n\n{exception.Message}",
                "Ошибка открытия BYX");
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
            StatusText = $"Перезагружен {Path.GetFileName(path)}";
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _dialogs.ShowError($"Не удалось перезагрузить GXT.\n\n{exception.Message}");
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
                    "Встроенный encounter-order dataset недоступен. Документ открыт без " +
                    $"канонической навигации.\n\n{exception.Message}",
                    "Encounter metadata");
            }
        }

        if (clearTransientState)
        {
            SelectedMetadataType = MetadataTypeOptions[0];
            SelectedMetadataBlock = MetadataBlockFilterOption.All;
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
        StatusText = visibleCount == Entries.Count
            ? $"{DocumentType} — {Entries.Count} ключей"
            : $"Показано {visibleCount} из {Entries.Count} ключей";
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
        BusyText = operationName;
        IsBusy = true;
        try
        {
            await operation(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Операция отменена";
        }
        finally
        {
            _operationCancellation = null;
            IsBusy = false;
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
        ImportJsonCommand.NotifyCanExecuteChanged();
        ImportJsonWithDictionaryCommand.NotifyCanExecuteChanged();
        AddMissingEntriesCommand.NotifyCanExecuteChanged();
        ConvertDictionaryCommand.NotifyCanExecuteChanged();
        CancelOperationCommand.NotifyCanExecuteChanged();
    }

    private bool CanStartOperation() => !IsBusy;

    private bool CanUseDocument() => !IsBusy && IsDocumentLoaded && _manager is not null;

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
        ViewTxdCommand.NotifyCanExecuteChanged();
        AddTxdCommand.NotifyCanExecuteChanged();
        RemoveTxdCommand.NotifyCanExecuteChanged();
        ExportTxdCommand.NotifyCanExecuteChanged();
    }

    private void OfferCharacterMapExport(string exportedPath)
    {
        if (_project?.CharacterMap is not { } profile ||
            !_dialogs.Confirm(
                "Маппинг не хранится внутри отдельных GXT/TXD. Экспортировать рядом файл .gxtmap.json?",
                "Экспорт маппинга"))
        {
            return;
        }

        var suggestedPath = Path.ChangeExtension(exportedPath, ".gxtmap.json");
        var targetPath = _dialogs.SaveFile(
            "Экспортировать маппинг GXT + TXD",
            "Маппинг GXT/TXD (*.gxtmap.json)|*.gxtmap.json|JSON (*.json)|*.json",
            suggestedPath);
        if (targetPath is null)
        {
            return;
        }

        try
        {
            CharacterMapFileSerializer.Save(targetPath, profile);
            StatusText = $"Файл и маппинг экспортированы: {exportedPath}";
        }
        catch (Exception exception)
        {
            _dialogs.ShowError($"Не удалось экспортировать маппинг.\n\n{exception.Message}");
        }
    }

}
