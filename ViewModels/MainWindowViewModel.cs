using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GTA_3_GXT_Editor.Utils;
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

    private readonly GxtManagerFactory _managerFactory;
    private readonly IDialogService _dialogs;
    private readonly ITxdReader _txdReader;
    private readonly IProjectSerializer _projectSerializer;
    private readonly IEncounterMetadataProvider _encounterMetadataProvider;

    private CommonGXTManager? _manager;
    private EditorProject? _project;
    private GXTType _loadedType = GXTType.None;
    private readonly List<ComparisonDocument> _comparisonDocuments = [];
    private EncounterMetadataIndex? _canonicalMetadata;
    private bool _canonicalMetadataWarningShown;

    public MainWindowViewModel(
        GxtManagerFactory managerFactory,
        IDialogService dialogs,
        ITxdReader? txdReader = null,
        IProjectSerializer? projectSerializer = null,
        IEncounterMetadataProvider? encounterMetadataProvider = null)
    {
        _managerFactory = managerFactory;
        _dialogs = dialogs;
        _txdReader = txdReader ?? new TxdReader();
        _projectSerializer = projectSerializer ?? new ByxProjectSerializer(managerFactory, _txdReader);
        _encounterMetadataProvider = encounterMetadataProvider ?? new BundledEncounterMetadataProvider();

        EntriesView = CollectionViewSource.GetDefaultView(Entries);
        EntriesView.Filter = FilterEntry;

        SearchColumns =
        [
            SearchColumnOption.All,
            new SearchColumnOption(SearchColumn.Name, "Ключ"),
            new SearchColumnOption(SearchColumn.Text, "Перевод"),
            new SearchColumnOption(SearchColumn.Source, "Английский оригинал"),
            new SearchColumnOption(SearchColumn.Comparison, "Файлы сравнения"),
            new SearchColumnOption(SearchColumn.Table, "Таблица"),
            new SearchColumnOption(SearchColumn.Metadata, "Блок и контекст"),
            new SearchColumnOption(SearchColumn.Comment, "Комментарий"),
        ];
        selectedSearchColumn = SearchColumns[0];

        MetadataTypeOptions =
        [
            MetadataTypeFilterOption.All,
            new MetadataTypeFilterOption("story", "Сюжет"),
            new MetadataTypeFilterOption("mission", "Миссии"),
            new MetadataTypeFilterOption("asset", "Активы"),
            new MetadataTypeFilterOption("phone", "Телефон"),
            new MetadataTypeFilterOption("interface", "Интерфейс"),
            new MetadataTypeFilterOption("world", "Мир"),
            new MetadataTypeFilterOption("credits", "Титры"),
            new MetadataTypeFilterOption("misc", "Прочее"),
        ];
        selectedMetadataType = MetadataTypeOptions[0];
        MetadataBlockOptions.Add(MetadataBlockFilterOption.All);
        selectedMetadataBlock = MetadataBlockOptions[0];

        SortOptions =
        [
            new EntrySortOption(EntrySortMode.EncounterOrder, "Encounter order"),
            new EntrySortOption(EntrySortMode.GxtOrder, "GXT order"),
        ];
        selectedSortOption = SortOptions[1];
    }

    public ObservableCollection<GxtEntryRow> Entries { get; } = [];

    public ObservableCollection<GxtComparisonColumn> ComparisonColumns { get; } = [];

    public ObservableCollection<MetadataBlockFilterOption> MetadataBlockOptions { get; } = [];

    public ICollectionView EntriesView { get; }

    public IReadOnlyList<SearchColumnOption> SearchColumns { get; }

    public IReadOnlyList<MetadataTypeFilterOption> MetadataTypeOptions { get; }

    public IReadOnlyList<EntrySortOption> SortOptions { get; }

    public bool IsComparisonLoaded => ComparisonColumns.Count > 0;

    public bool HasEnglishSource => ComparisonColumns.Count > 0;

    public bool HasEncounterMetadata => Entries.Any(entry => entry.Occurrences.Count > 0);

    public bool HasTxdAttachment => AttachedTxd is not null;

    public bool CanAttachTxd => IsDocumentLoaded && _loadedType == GXTType.GtaViceCity;

    [ObservableProperty]
    private string gxtPath = string.Empty;

    [ObservableProperty]
    private string projectPath = string.Empty;

    [ObservableProperty]
    private string gxtSourceName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool isProjectDirty;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private SearchColumnOption selectedSearchColumn;

    [ObservableProperty]
    private MetadataTypeFilterOption selectedMetadataType;

    [ObservableProperty]
    private MetadataBlockFilterOption selectedMetadataBlock;

    [ObservableProperty]
    private EntrySortOption selectedSortOption;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommentCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommentCommand))]
    private string commentDraft = string.Empty;

    [ObservableProperty]
    private bool caseSensitive;

    [ObservableProperty]
    private bool liveSearch = true;

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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditEntryCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteEntryCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommentCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommentCommand))]
    private GxtEntryRow? selectedEntry;

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

    partial void OnSearchTextChanged(string value)
    {
        if (LiveSearch)
        {
            ApplyFilter();
        }
    }

    partial void OnSelectedSearchColumnChanged(SearchColumnOption value)
    {
        if (LiveSearch)
        {
            ApplyFilter();
        }
    }

    partial void OnCaseSensitiveChanged(bool value)
    {
        if (LiveSearch)
        {
            ApplyFilter();
        }
    }

    partial void OnLiveSearchChanged(bool value)
    {
        if (value)
        {
            ApplyFilter();
        }
    }

    partial void OnSelectedMetadataTypeChanged(MetadataTypeFilterOption value)
    {
        RebuildMetadataBlockOptions();
        ApplySort();
        ApplyFilter();
    }

    partial void OnSelectedMetadataBlockChanged(MetadataBlockFilterOption value)
    {
        ApplySort();
        ApplyFilter();
    }

    partial void OnSelectedSortOptionChanged(EntrySortOption value)
    {
        ApplySort();
        UpdateStatus();
    }

    partial void OnSelectedEntryChanged(GxtEntryRow? value)
    {
        CommentDraft = value?.Comment ?? string.Empty;
    }

    [RelayCommand]
    private void OpenFile()
    {
        var path = _dialogs.OpenFile("Открыть GXT или проект BYX", DocumentFileFilter);
        if (path is not null && TryContinueAfterUnsavedChanges())
        {
            OpenDocument(path);
        }
    }

    [RelayCommand]
    private void OpenFileWithDictionary()
    {
        var path = _dialogs.OpenFile("Открыть GXT-файл с маппингом", GxtFileFilter);
        if (path is null)
        {
            return;
        }

        var dictionaryPath = _dialogs.OpenFile("Выбрать маппинг символов", CharacterMapFileFilter);
        if (dictionaryPath is not null && TryContinueAfterUnsavedChanges())
        {
            LoadDocument(path, dictionaryPath);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private void OpenComparisonFile()
    {
        var paths = _dialogs.OpenFiles("Добавить английский оригинал или GXT для сравнения", GxtFileFilter);
        if (paths.Count == 0)
        {
            return;
        }

        var loadedCount = 0;
        foreach (var path in paths)
        {
            if (TryAddComparisonFile(path))
            {
                loadedCount++;
            }
        }

        if (loadedCount == 0)
        {
            return;
        }

        OnPropertyChanged(nameof(IsComparisonLoaded));
        OnPropertyChanged(nameof(HasEnglishSource));
        var selected = SelectedEntry;
        RefreshEntries(selected?.Name, selected?.RawTableName);
        StatusText = $"Загружено файлов для сравнения: {loadedCount}; всего: {ComparisonColumns.Count}";
    }

    private bool TryAddComparisonFile(string path)
    {
        try
        {
            var type = _managerFactory.DetectType(path);
            if (type != _loadedType)
            {
                _dialogs.ShowError(
                    $"Тип файла '{Path.GetFileName(path)}' не соответствует открытому GXT.");
                return false;
            }

            var comparisonManager = OpenRelatedGxt(path);
            var comparisonTexts = new Dictionary<EntryIdentity, string>();
            foreach (var entry in comparisonManager.GXTEntries)
            {
                comparisonTexts[GetEntryIdentity(entry)] = comparisonManager
                    .ConvertBytesToText(entry.Value)
                    .GetClearName();
            }

            var existingIndex = _comparisonDocuments.FindIndex(document =>
                string.Equals(document.Path, path, StringComparison.OrdinalIgnoreCase));
            var columnName = existingIndex >= 0
                ? ComparisonColumns[existingIndex].Name
                : CreateComparisonColumnName(path, isEnglishSource: ComparisonColumns.Count == 0);
            var comparisonDocument = new ComparisonDocument(path, comparisonTexts);

            if (existingIndex >= 0)
            {
                _comparisonDocuments[existingIndex] = comparisonDocument;
                ComparisonColumns[existingIndex] = new GxtComparisonColumn(columnName, path);
            }
            else
            {
                _comparisonDocuments.Add(comparisonDocument);
                ComparisonColumns.Add(new GxtComparisonColumn(columnName, path));
            }

            return true;
        }
        catch (Exception exception)
        {
            _dialogs.ShowError(
                $"Не удалось открыть '{Path.GetFileName(path)}' для сравнения.\n\n{exception.Message}",
                "Ошибка открытия");
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private void Reload()
    {
        if (!TryContinueAfterUnsavedChanges())
        {
            return;
        }

        if (_project?.ProjectPath is { Length: > 0 } projectPath)
        {
            LoadProject(projectPath);
            return;
        }

        if (_project?.GxtSourcePath is not { Length: > 0 } sourcePath)
        {
            _dialogs.ShowError("Исходный GXT недоступен для перезагрузки.");
            return;
        }

        var selected = SelectedEntry;
        var dictionaryPath = _manager?.CyrillicCharsDictionaryPath;

        ReloadGxtInProject(
            sourcePath,
            dictionaryPath: dictionaryPath,
            selectedName: selected?.Name,
            selectedTable: selected?.RawTableName,
            language: _manager?.Language ?? GxtLanguage.Auto);
    }

    [RelayCommand]
    private void ApplyFilter()
    {
        EntriesView.Refresh();
        UpdateStatus();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        SelectedMetadataType = MetadataTypeOptions[0];
        SelectedMetadataBlock = MetadataBlockOptions[0];
        ApplyFilter();
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
    private void Save()
    {
        _ = SaveCurrentDocument();
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private void ExportGxt()
    {
        _ = SaveGxtAs(markProjectSaved: false);
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private void SaveProjectAs()
    {
        _ = SaveProject(forceSaveAs: true);
    }

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
            _manager.SaveGXTChanges(targetPath);
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
            _projectSerializer.Save(targetPath, _project);
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

    [RelayCommand(CanExecute = nameof(CanAddTxd))]
    private void AddTxd()
    {
        var path = _dialogs.OpenFile("Подключить или заменить TXD GTA Vice City", TxdFileFilter);
        if (path is null)
        {
            return;
        }

        try
        {
            var data = File.ReadAllBytes(path);
            var document = _txdReader.Read(data, path);
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

            var attachment = new TxdAttachment
            {
                Id = existing?.Id ?? Guid.NewGuid(),
                OriginalFileName = Path.GetFileName(path),
                DisplayName = Path.GetFileNameWithoutExtension(path),
                SourcePath = path,
                Data = data,
                Document = document,
            };

            if (_project is not null)
            {
                _project.CharacterMap ??= CharacterMapProfile.FromDictionary(
                    _manager?.CyrillicCharsDictionary ?? [],
                    isVerified: false);
                _project.CharacterMap.IsVerified = false;
                _project.AttachedTxd = attachment;
            }

            AttachedTxd = attachment;
            OnTxdAttachmentChanged();
            SetDirty(true);
            StatusText = $"Подключён TXD: {attachment.OriginalFileName} — {document.Textures.Count} текстур";
        }
        catch (Exception exception)
        {
            _dialogs.ShowError($"Не удалось подключить TXD.\n\n{exception.Message}");
        }
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
    private void ExportTxd()
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

        try
        {
            File.WriteAllBytes(targetPath, selected.Data);
            StatusText = $"TXD экспортирован: {targetPath}";
            OfferCharacterMapExport(targetPath);
        }
        catch (Exception exception)
        {
            _dialogs.ShowError($"Не удалось экспортировать TXD.\n\n{exception.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanViewTxd))]
    private void ViewTxd()
    {
        if (_manager is null || _project is null || AttachedTxd is null)
        {
            return;
        }

        var profile = _project.CharacterMap?.Clone() ?? CharacterMapProfile.FromDictionary(
            _manager.CyrillicCharsDictionary,
            isVerified: false);
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

        var preview = CharacterMapService.Preview(_manager, result.Profile, result.ApplyMode);
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
            CharacterMapService.Apply(_manager, result.Profile, result.ApplyMode);
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
    private void ExportJson()
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

        try
        {
            GxtJsonExporter.Export(
                targetPath,
                sourceName,
                DocumentType,
                Entries,
                _manager?.Language ?? GxtLanguage.Auto);
            StatusText = $"Экспортировано в JSON: {targetPath}";
        }
        catch (Exception exception)
        {
            _dialogs.ShowError($"Не удалось экспортировать JSON.\n\n{exception.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private void ImportComments()
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

        try
        {
            var selected = SelectedEntry;
            var result = GxtCommentsImporter.Import(sourcePath, _project);
            if (result.ChangedEntryCount > 0)
            {
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
        catch (Exception exception)
        {
            _dialogs.ShowError($"Не удалось импортировать комментарии.\n\n{exception.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private void ExportComments()
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

        try
        {
            GxtCommentsExporter.Export(targetPath, _project, includeText: true);
            StatusText = $"Комментарии экспортированы: {targetPath}";
        }
        catch (Exception exception)
        {
            _dialogs.ShowError($"Не удалось экспортировать комментарии.\n\n{exception.Message}");
        }
    }

    [RelayCommand]
    private void ImportJson()
    {
        ImportJson(useCustomDictionary: false);
    }

    [RelayCommand]
    private void ImportJsonWithDictionary()
    {
        ImportJson(useCustomDictionary: true);
    }

    private void ImportJson(bool useCustomDictionary)
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

        try
        {
            var result = GxtJsonImporter.Import(sourcePath, targetPath, dictionaryPath);
            if (LoadDocument(
                    targetPath,
                    dictionaryPath: dictionaryPath,
                    language: result.Language))
            {
                var game = result.Type == GXTType.GtaIII ? "GTA III" : "GTA Vice City";
                StatusText = $"JSON преобразован в {game} GXT: {result.EntryCount} ключей";
            }
        }
        catch (Exception exception)
        {
            _dialogs.ShowError($"Не удалось преобразовать JSON в GXT.\n\n{exception.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private void AddMissingEntries()
    {
        if (_manager is null)
        {
            return;
        }

        var gameName = _loadedType == GXTType.GtaIII ? "GTA III" : "GTA Vice City";
        var path = _dialogs.OpenFile(
            $"Добавить отсутствующие ключи из {gameName}",
            $"{gameName} GXT (*.gxt)|*.gxt|Все файлы (*.*)|*.*");
        if (path is null)
        {
            return;
        }

        try
        {
            if (_managerFactory.DetectType(path) != _loadedType)
            {
                _dialogs.ShowError("Тип выбранного GXT-файла не соответствует открытому файлу.");
                return;
            }

            var sourceManager = OpenRelatedGxt(path);
            var missingEntries = sourceManager.GXTEntries
                .Except(_manager.GXTEntries, new GXTEntryEqualityComparer())
                .ToList();

            foreach (var entry in missingEntries)
            {
                var text = sourceManager.ConvertBytesToText(entry.Value);
                var table = (entry as GTAVC.GXTEntry)?.TableName;
                _manager.AddGXTEntry(entry.DatName.GetClearName(), text, table);
            }

            if (missingEntries.Count > 0)
            {
                SetDirty(true);
            }

            RefreshEntries();
            _dialogs.ShowInfo($"Добавлено отсутствующих ключей: {missingEntries.Count}.");
        }
        catch (Exception exception)
        {
            _dialogs.ShowError($"Не удалось добавить ключи.\n\n{exception.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private void ConvertDictionary()
    {
        if (_manager is null)
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

        try
        {
            var sourceDictionary = _manager.CyrillicCharsDictionary;
            var targetDictionary = CharacterMapFileSerializer.LoadDictionary(targetPath);

            if (sourceDictionary.Count != targetDictionary.Count ||
                !sourceDictionary.Values.ToHashSet().SetEquals(targetDictionary.Values))
            {
                _dialogs.ShowError(
                    "Маппинги должны содержать одинаковое количество и одинаковый набор символов.");
                return;
            }

            var targetBytesByCharacter = targetDictionary
                .ToDictionary(pair => pair.Value, pair => checked((byte)pair.Key[0]));
            var byteMap = sourceDictionary
                .SelectMany(pair => pair.Key.Select(index => new
                {
                    Source = checked((byte)index),
                    Target = targetBytesByCharacter[pair.Value],
                }))
                .ToDictionary(pair => pair.Source, pair => pair.Target);

            foreach (var entry in _manager.GXTEntries)
            {
                for (var index = 0; index < entry.Value.Length; index++)
                {
                    if (byteMap.TryGetValue(entry.Value[index], out var replacement))
                    {
                        entry.Value[index] = replacement;
                    }
                }
            }

            _manager.CyrillicCharsDictionaryPath = targetPath;
            _manager.ReloadCyrillicCharsDictionary();
            if (_project is not null)
            {
                _project.UsesCustomDictionary = true;
                _project.CharacterMap = CharacterMapProfile.FromDictionary(
                    _manager.CyrillicCharsDictionary,
                    isVerified: false);
            }

            SetDirty(true);
            RefreshEntries();
            _dialogs.ShowInfo("Маппинг символов успешно преобразован.");
        }
        catch (Exception exception)
        {
            _dialogs.ShowError($"Не удалось преобразовать маппинг.\n\n{exception.Message}");
        }
    }

    private bool LoadDocument(
        string path,
        string? dictionaryPath = null,
        string? selectedName = null,
        string? selectedTable = null,
        GxtLanguage language = GxtLanguage.Auto)
    {
        if (!File.Exists(path))
        {
            _dialogs.ShowError($"Файл '{path}' не существует или недоступен.");
            return false;
        }

        try
        {
            var manager = _managerFactory.Open(path, dictionaryPath, language);
            var type = _managerFactory.DetectType(path);
            var project = new EditorProject
            {
                ProjectPath = null,
                GxtSourceName = Path.GetFileName(path),
                GxtSourcePath = path,
                GameType = type,
                GxtManager = manager,
                UsesCustomDictionary = dictionaryPath is not null,
                AttachedTxd = null,
                CharacterMap = null,
                IsDirty = false,
            };
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

    private void RefreshEntries(string? selectedName = null, string? selectedTable = null)
    {
        Entries.Clear();

        if (_manager is null)
        {
            return;
        }

        var projectEntries = (_project?.Metadata.Entries ?? [])
            .ToDictionary(
                entry => CreateMetadataIdentity(entry.Key, entry.Table),
                entry => entry);
        var projectBlocks = (_project?.Metadata.Blocks ?? [])
            .Select((block, index) => new EncounterMetadataBlockIndex(block, index))
            .ToDictionary(block => block.Block.Id, StringComparer.Ordinal);

        var sourceIndex = 0;
        foreach (var entry in _manager.GXTEntries)
        {
            var rawTable = (entry as GTAVC.GXTEntry)?.TableName;
            var identity = GetEntryIdentity(entry);
            var metadataIdentity = CreateMetadataIdentity(
                entry.DatName.GetClearName(),
                rawTable?.GetClearName());
            projectEntries.TryGetValue(metadataIdentity, out var projectEntry);
            var occurrences = BuildOccurrences(metadataIdentity, projectEntry, projectBlocks);
            Entries.Add(new GxtEntryRow(
                entry.DatName.GetClearName(),
                _manager.ConvertBytesToText(entry.Value).GetClearName(),
                rawTable?.GetClearName() ?? string.Empty,
                rawTable)
            {
                SourceIndex = sourceIndex++,
                ComparisonTexts = _comparisonDocuments
                    .Select(document => document.Texts.GetValueOrDefault(identity))
                    .ToArray(),
                Comment = projectEntry?.Comment,
                Occurrences = occurrences,
                PrimaryOccurrence = occurrences.Length > 0 ? occurrences[0] : null,
            });
        }

        RebuildMetadataBlockOptions();
        ApplySort();
        EntriesView.Refresh();
        SelectedEntry = Entries.FirstOrDefault(entry =>
            string.Equals(entry.Name, selectedName, StringComparison.Ordinal) &&
            string.Equals(entry.RawTableName, selectedTable, StringComparison.Ordinal));
        OnPropertyChanged(nameof(HasEncounterMetadata));
        UpdateStatus();
    }

    private GxtEntryOccurrenceView[] BuildOccurrences(
        EncounterMetadataIdentity identity,
        ProjectEntryMetadata? projectEntry,
        IReadOnlyDictionary<string, EncounterMetadataBlockIndex> projectBlocks)
    {
        ProjectEntryMetadata? occurrenceEntry;
        IReadOnlyDictionary<string, EncounterMetadataBlockIndex> blocks;
        EncounterMetadataSource source;

        if (projectEntry is { Occurrences.Count: > 0 })
        {
            occurrenceEntry = projectEntry;
            blocks = projectBlocks;
            source = EncounterMetadataSource.Project;
        }
        else if (_canonicalMetadata?.Entries.TryGetValue(identity, out var canonicalEntry) == true)
        {
            occurrenceEntry = canonicalEntry;
            blocks = _canonicalMetadata.Blocks;
            source = EncounterMetadataSource.Canonical;
        }
        else
        {
            return [];
        }

        var result = new List<GxtEntryOccurrenceView>(occurrenceEntry.Occurrences.Count);
        foreach (var occurrence in occurrenceEntry.Occurrences)
        {
            if (!blocks.TryGetValue(occurrence.BlockId, out var block))
            {
                continue;
            }

            result.Add(new GxtEntryOccurrenceView(
                block.Block.Id,
                block.Block.Type,
                block.Block.Name,
                block.Block.Description,
                block.Block.Order,
                block.Sequence,
                occurrence.Order,
                occurrence.Context,
                source));
        }

        return result
            .OrderBy(occurrence => occurrence.BlockOrder)
            .ThenBy(occurrence => occurrence.BlockSequence)
            .ThenBy(occurrence => occurrence.OccurrenceOrder)
            .ThenBy(occurrence => occurrence.BlockId, StringComparer.Ordinal)
            .ToArray();
    }

    private EncounterMetadataIdentity CreateMetadataIdentity(string key, string? table) =>
        new(_loadedType == GXTType.GtaViceCity ? table : null, key);

    private void RebuildMetadataBlockOptions()
    {
        var selectedId = SelectedMetadataBlock?.Id;
        var selectedType = SelectedMetadataType?.Type;
        var blocks = Entries
            .SelectMany(entry => entry.Occurrences)
            .Where(occurrence => selectedType is null ||
                string.Equals(occurrence.BlockType, selectedType, StringComparison.Ordinal))
            .GroupBy(occurrence => occurrence.BlockId, StringComparer.Ordinal)
            .Select(group => group
                .OrderBy(occurrence => occurrence.Source == EncounterMetadataSource.Project ? 0 : 1)
                .ThenBy(occurrence => occurrence.BlockSequence)
                .First())
            .OrderBy(occurrence => occurrence.BlockOrder)
            .ThenBy(occurrence => occurrence.BlockSequence)
            .ThenBy(occurrence => occurrence.BlockName, StringComparer.Ordinal)
            .ToArray();

        MetadataBlockOptions.Clear();
        MetadataBlockOptions.Add(MetadataBlockFilterOption.All);
        foreach (var block in blocks)
        {
            MetadataBlockOptions.Add(new MetadataBlockFilterOption(
                block.BlockId,
                $"{block.BlockName} · {block.BlockType}",
                block.BlockType));
        }

        SelectedMetadataBlock = MetadataBlockOptions.FirstOrDefault(option =>
            string.Equals(option.Id, selectedId, StringComparison.Ordinal)) ??
            MetadataBlockOptions[0];
    }

    private void ApplySort()
    {
        if (EntriesView is not ListCollectionView view)
        {
            return;
        }

        view.CustomSort = new GxtEntryRowComparer(
            SelectedSortOption.Mode,
            SelectedMetadataBlock.Id,
            SelectedMetadataType.Type);
    }

    private List<TableOption> GetTableOptions()
    {
        if (_loadedType != GXTType.GtaViceCity || _manager is null)
        {
            return [];
        }

        return _manager.GXTEntries
            .OfType<GTAVC.GXTEntry>()
            .Select(entry => entry.TableName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(table => table, new ASCIIStringComparer())
            .Select(table => new TableOption(table, table.GetClearName()))
            .ToList();
    }

    private bool EntryExists(string name, string? rawTableName)
    {
        return Entries.Any(entry =>
            string.Equals(entry.Name, name, StringComparison.Ordinal) &&
            string.Equals(entry.RawTableName, rawTableName, StringComparison.Ordinal));
    }

    private bool FilterEntry(object item)
    {
        if (item is not GxtEntryRow entry)
        {
            return false;
        }

        if (SelectedMetadataType.Type is { } selectedType &&
            !entry.Occurrences.Any(occurrence =>
                string.Equals(occurrence.BlockType, selectedType, StringComparison.Ordinal)))
        {
            return false;
        }

        if (SelectedMetadataBlock.Id is { } selectedBlockId &&
            !entry.Occurrences.Any(occurrence =>
                string.Equals(occurrence.BlockId, selectedBlockId, StringComparison.Ordinal)))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var comparison = CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        bool Contains(string? value) => value?.Contains(SearchText, comparison) == true;
        bool ContainsMetadata() => entry.Occurrences.Any(occurrence =>
            Contains(occurrence.BlockId) ||
            Contains(occurrence.BlockType) ||
            Contains(occurrence.BlockName) ||
            Contains(occurrence.BlockDescription) ||
            Contains(occurrence.Context));

        return SelectedSearchColumn.Column switch
        {
            SearchColumn.Name => Contains(entry.Name),
            SearchColumn.Text => Contains(entry.Text),
            SearchColumn.Source => Contains(entry.SourceText),
            SearchColumn.Comparison => entry.ComparisonTexts.Any(Contains),
            SearchColumn.Table => Contains(entry.Table),
            SearchColumn.Metadata => ContainsMetadata(),
            SearchColumn.Comment => Contains(entry.Comment),
            _ => Contains(entry.Name) ||
                 Contains(entry.Text) ||
                 entry.ComparisonTexts.Any(Contains) ||
                 Contains(entry.Table) ||
                 ContainsMetadata() ||
                 Contains(entry.Comment),
        };
    }

    private bool SetProjectComment(string key, string? rawTableName, string? comment)
    {
        if (_project is null)
        {
            return false;
        }

        var normalizedComment = string.IsNullOrWhiteSpace(comment) ? null : comment;
        var table = _loadedType == GXTType.GtaViceCity
            ? rawTableName?.GetClearName()
            : null;
        var entry = _project.Metadata.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, key, StringComparison.Ordinal) &&
            string.Equals(candidate.Table, table, StringComparison.Ordinal));

        if (string.Equals(entry?.Comment, normalizedComment, StringComparison.Ordinal))
        {
            return false;
        }

        if (entry is null)
        {
            if (normalizedComment is null)
            {
                return false;
            }

            entry = new ProjectEntryMetadata
            {
                Key = key,
                Table = table,
            };
            _project.Metadata.Entries.Add(entry);
        }

        entry.Comment = normalizedComment;
        if (entry.Comment is null && entry.Occurrences.Count == 0)
        {
            _project.Metadata.Entries.Remove(entry);
        }

        return true;
    }

    private void MoveProjectEntryMetadata(
        string key,
        string? oldRawTableName,
        string? newRawTableName)
    {
        if (_project is null || _loadedType != GXTType.GtaViceCity)
        {
            return;
        }

        var oldTable = oldRawTableName?.GetClearName();
        var newTable = newRawTableName?.GetClearName();
        if (string.Equals(oldTable, newTable, StringComparison.Ordinal))
        {
            return;
        }

        var entry = _project.Metadata.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, key, StringComparison.Ordinal) &&
            string.Equals(candidate.Table, oldTable, StringComparison.Ordinal));
        if (entry is not null)
        {
            entry.Table = newTable;
        }
    }

    private void RemoveEmptyProjectMetadataEntries()
    {
        if (_project is null)
        {
            return;
        }

        _project.Metadata.Entries.RemoveAll(entry =>
            string.IsNullOrWhiteSpace(entry.Comment) && entry.Occurrences.Count == 0);
    }

    private bool HasPersistableProjectMetadata() =>
        _project?.Metadata.Blocks.Count > 0 ||
        _project?.Metadata.Entries.Any(entry =>
            !string.IsNullOrWhiteSpace(entry.Comment) || entry.Occurrences.Count > 0) == true;

    private bool CanSaveComment() =>
        SelectedEntry is not null &&
        !string.Equals(
            string.IsNullOrWhiteSpace(CommentDraft) ? null : CommentDraft,
            SelectedEntry.Comment,
            StringComparison.Ordinal);

    private bool CanClearComment() =>
        SelectedEntry is not null &&
        (!string.IsNullOrWhiteSpace(SelectedEntry.Comment) ||
         !string.IsNullOrWhiteSpace(CommentDraft));

    private void ClearComparisons()
    {
        _comparisonDocuments.Clear();
        ComparisonColumns.Clear();
        OnPropertyChanged(nameof(IsComparisonLoaded));
        OnPropertyChanged(nameof(HasEnglishSource));
    }

    private string CreateComparisonColumnName(string path, bool isEnglishSource)
    {
        var normalizedName = Path.GetFileNameWithoutExtension(path).Trim();
        if (string.IsNullOrEmpty(normalizedName))
        {
            normalizedName = "GXT";
        }

        var usedNames = ComparisonColumns
            .Select(column => column.Name.StartsWith(
                    "English source — ",
                    StringComparison.Ordinal)
                ? column.Name[17..]
                : column.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!usedNames.Contains(normalizedName))
        {
            return isEnglishSource ? $"English source — {normalizedName}" : normalizedName;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{normalizedName} ({suffix})";
            if (!usedNames.Contains(candidate))
            {
                return isEnglishSource ? $"English source — {candidate}" : candidate;
            }
        }
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

    private bool LoadProject(string path)
    {
        try
        {
            var project = _projectSerializer.Load(path);
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

    private bool ReloadGxtInProject(
        string path,
        string? dictionaryPath,
        string? selectedName,
        string? selectedTable,
        GxtLanguage language)
    {
        try
        {
            var manager = _managerFactory.Open(path, dictionaryPath, language);
            var project = new EditorProject
            {
                ProjectPath = null,
                GxtSourceName = Path.GetFileName(path),
                GxtSourcePath = path,
                GameType = _managerFactory.DetectType(path),
                GxtManager = manager,
                UsesCustomDictionary = dictionaryPath is not null,
                AttachedTxd = null,
                CharacterMap = null,
                IsDirty = false,
            };
            CommitProject(project, selectedName, selectedTable, clearTransientState: false);
            StatusText = $"Перезагружен {Path.GetFileName(path)}";
            return true;
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

        _project = project;
        _manager = project.GxtManager;
        _loadedType = project.GameType;
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
        DocumentType = project.GameType == GXTType.GtaIII ? "GTA III" : "GTA Vice City";
        IsDocumentLoaded = true;
        SetDirty(project.IsDirty);
        AttachedTxd = project.AttachedTxd;
        OnTxdAttachmentChanged();
        RefreshEntries(selectedName, selectedTable);
    }

    private static EntryIdentity GetEntryIdentity(GXTBase entry) =>
        new(
            entry.DatName.GetClearName(),
            (entry as GTAVC.GXTEntry)?.TableName);

    private CommonGXTManager OpenRelatedGxt(string path)
    {
        var manager = _managerFactory.Open(
            path,
            _manager?.CyrillicCharsDictionaryPath,
            _manager?.Language ?? GxtLanguage.Auto);
        if (_project?.CharacterMap is not null &&
            _manager is not null)
        {
            manager.CyrillicCharsDictionary = _project.CharacterMap.ToCharacterDictionary();
        }
        else if (_project?.UsesCustomDictionary == true &&
            string.IsNullOrWhiteSpace(_manager?.CyrillicCharsDictionaryPath) &&
            _manager is not null)
        {
            manager.CyrillicCharsDictionary = _manager.CyrillicCharsDictionary.ToDictionary(
                pair => pair.Key.ToArray(),
                pair => pair.Value);
        }

        return manager;
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

    private bool CanUseDocument() => IsDocumentLoaded && _manager is not null;

    private bool CanAddTxd() => CanAttachTxd;

    private bool CanUseAttachedTxd() => AttachedTxd is not null;

    private bool CanViewTxd() => AttachedTxd is not null;

    private bool CanUseSelection() => CanUseDocument() && SelectedEntry is not null;

    public bool CanClose() => TryContinueAfterUnsavedChanges();

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
        IsProjectDirty = value;
        if (_project is not null)
        {
            _project.IsDirty = value;
        }
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

    private readonly record struct EntryIdentity(string Name, string? Table);

    private sealed class GxtEntryRowComparer(
        EntrySortMode mode,
        string? selectedBlockId,
        string? selectedBlockType) : IComparer
    {
        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is not GxtEntryRow left)
            {
                return -1;
            }

            if (y is not GxtEntryRow right)
            {
                return 1;
            }

            if (mode == EntrySortMode.GxtOrder)
            {
                return left.SourceIndex.CompareTo(right.SourceIndex);
            }

            var leftOccurrence = SelectOccurrence(left);
            var rightOccurrence = SelectOccurrence(right);
            if (leftOccurrence is null || rightOccurrence is null)
            {
                if (leftOccurrence is not null)
                {
                    return -1;
                }

                if (rightOccurrence is not null)
                {
                    return 1;
                }

                return left.SourceIndex.CompareTo(right.SourceIndex);
            }

            var result = leftOccurrence.BlockOrder.CompareTo(rightOccurrence.BlockOrder);
            if (result == 0)
            {
                result = leftOccurrence.BlockSequence.CompareTo(rightOccurrence.BlockSequence);
            }

            if (result == 0)
            {
                result = leftOccurrence.OccurrenceOrder.CompareTo(rightOccurrence.OccurrenceOrder);
            }

            return result != 0 ? result : left.SourceIndex.CompareTo(right.SourceIndex);
        }

        private GxtEntryOccurrenceView? SelectOccurrence(GxtEntryRow entry) =>
            selectedBlockId is not null
                ? entry.Occurrences.FirstOrDefault(occurrence =>
                    string.Equals(occurrence.BlockId, selectedBlockId, StringComparison.Ordinal))
                : selectedBlockType is not null
                    ? entry.Occurrences.FirstOrDefault(occurrence =>
                        string.Equals(
                            occurrence.BlockType,
                            selectedBlockType,
                            StringComparison.Ordinal))
                    : entry.PrimaryOccurrence;
    }

    private sealed record ComparisonDocument(
        string Path,
        Dictionary<EntryIdentity, string> Texts);
}
