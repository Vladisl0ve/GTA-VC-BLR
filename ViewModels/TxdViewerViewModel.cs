using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.ViewModels;

public partial class TxdViewerViewModel : ObservableObject
{
    private const string CharacterMapImportFilter =
        "Маппинг (*.json;*.txt)|*.json;*.txt|JSON (*.json)|*.json|Словарь (*.txt)|*.txt";
    private const string CharacterMapExportFilter =
        "Маппинг JSON (*.gxtmap.json)|*.gxtmap.json|JSON (*.json)|*.json";

    private readonly CharacterMapEditorRequest _request;
    private readonly IDialogService? _dialogs;
    private CharacterMapProfile _profile;

    public TxdViewerViewModel(CharacterMapEditorRequest request, IDialogService? dialogs = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        _request = request;
        _dialogs = dialogs;
        _profile = request.Profile.Clone();
        Attachment = request.Attachment;
        ApplyModes =
        [
            new CharacterMapApplyModeOption(
                CharacterMapApplyMode.Interpret,
                "Интерпретировать исходные байты"),
            new CharacterMapApplyModeOption(
                CharacterMapApplyMode.Reencode,
                "Перекодировать текущий текст"),
        ];
        selectedApplyMode = ApplyModes[0];

        foreach (var texture in Attachment.Document.Textures.Where(IsVisibleFontAtlas))
        {
            Textures.Add(texture);
        }

        SelectedTexture = Textures.FirstOrDefault(texture => texture.IsFontAtlas) ??
                          Textures.FirstOrDefault();
        UpdateAnalysis();
    }

    public TxdAttachment Attachment { get; }

    public ObservableCollection<TxdTexture> Textures { get; } = [];

    public ObservableCollection<GlyphPreviewItem> Glyphs { get; } = [];

    public IReadOnlyList<CharacterMapApplyModeOption> ApplyModes { get; }

    public CharacterMapProfile Profile => _profile;

    public CharacterMapEditorResult? Result { get; private set; }

    public event EventHandler? ApplySucceeded;

    public string GlyphAtlasHeading => _request.GameType == GXTType.GtaViceCity
        ? "Ячейки font1 / font2"
        : "Ячейки font1 / font2 / pager";

    public string VerificationText => _profile.IsVerified
        ? "Профиль проверен для текущей пары"
        : "Черновик: проверьте назначения по глифам TXD";

    [ObservableProperty]
    private TxdTexture? selectedTexture;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AssignCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearAssignmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(MakePreferredCommand))]
    private GlyphPreviewItem? selectedGlyph;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AssignCommand))]
    private string assignmentText = string.Empty;

    [ObservableProperty]
    private bool isPreferredCode;

    [ObservableProperty]
    private ImageSource? textureImage;

    [ObservableProperty]
    private ImageSource? glyphImage;

    [ObservableProperty]
    private string textureMetadata = "Выберите текстуру";

    [ObservableProperty]
    private string analysisText = string.Empty;

    [ObservableProperty]
    private CharacterMapApplyModeOption selectedApplyMode;

    partial void OnSelectedTextureChanged(TxdTexture? value) => RefreshGlyphs();

    partial void OnSelectedGlyphChanged(GlyphPreviewItem? value)
    {
        GlyphImage = value?.Image;
        var mapping = value?.Cell.Code is { } code
            ? _profile.Mappings.SingleOrDefault(item => item.Codes.Contains(code))
            : null;
        AssignmentText = mapping?.Character.ToString() ?? string.Empty;
        IsPreferredCode = mapping is not null && value?.Cell.Code == mapping.PreferredCode;
    }

    [RelayCommand(CanExecute = nameof(CanAssign))]
    private void Assign()
    {
        if (SelectedGlyph?.Cell.Code is not { } code || AssignmentText.Length != 1)
        {
            return;
        }

        var character = AssignmentText[0];
        if (char.IsControl(character) || char.IsSurrogate(character))
        {
            return;
        }

        var previous = _profile.Mappings.SingleOrDefault(mapping => mapping.Codes.Contains(code));
        if (previous is not null)
        {
            previous.Codes.Remove(code);
            if (previous.Codes.Count == 0)
            {
                _profile.Mappings.Remove(previous);
            }
            else if (previous.PreferredCode == code)
            {
                previous.PreferredCode = previous.Codes[0];
            }
        }

        var mapping = _profile.Mappings.SingleOrDefault(item => item.Character == character);
        if (mapping is null)
        {
            mapping = new CharacterMapEntry
            {
                Character = character,
                Codes = [code],
                PreferredCode = code,
            };
            _profile.Mappings.Add(mapping);
        }
        else if (!mapping.Codes.Contains(code))
        {
            mapping.Codes.Add(code);
        }

        if (IsPreferredCode)
        {
            mapping.PreferredCode = code;
        }

        _profile.IsVerified = false;
        OnPropertyChanged(nameof(VerificationText));
        RefreshGlyphs(code);
        UpdateAnalysis();
    }

    private bool CanAssign() =>
        SelectedGlyph?.Cell.Code is not null && AssignmentText.Length == 1 &&
        !char.IsControl(AssignmentText[0]) && !char.IsSurrogate(AssignmentText[0]);

    [RelayCommand(CanExecute = nameof(CanMakePreferred))]
    private void ClearAssignment()
    {
        if (SelectedGlyph?.Cell.Code is not { } code)
        {
            return;
        }

        var mapping = _profile.Mappings.SingleOrDefault(item => item.Codes.Contains(code));
        if (mapping is null)
        {
            return;
        }

        mapping.Codes.Remove(code);
        if (mapping.Codes.Count == 0)
        {
            _profile.Mappings.Remove(mapping);
        }
        else if (mapping.PreferredCode == code)
        {
            mapping.PreferredCode = mapping.Codes[0];
        }

        _profile.IsVerified = false;
        OnPropertyChanged(nameof(VerificationText));
        RefreshGlyphs(code);
        UpdateAnalysis();
    }

    [RelayCommand(CanExecute = nameof(CanEditSelectedCode))]
    private void MakePreferred()
    {
        if (SelectedGlyph?.Cell.Code is not { } code)
        {
            return;
        }

        var mapping = _profile.Mappings.SingleOrDefault(item => item.Codes.Contains(code));
        if (mapping is null)
        {
            return;
        }

        mapping.PreferredCode = code;
        _profile.IsVerified = false;
        OnPropertyChanged(nameof(VerificationText));
        IsPreferredCode = true;
        RefreshGlyphs(code);
        UpdateAnalysis();
    }

    private bool CanEditSelectedCode() => SelectedGlyph?.Cell.Code is not null;

    private bool CanMakePreferred() => SelectedGlyph?.Cell.Code is { } code &&
                                       _profile.Mappings.Any(mapping => mapping.Codes.Contains(code));

    [RelayCommand]
    private void UseBelarusianPreset()
    {
        _profile = CharacterMapPresets.Belarusian;
        RefreshGlyphs(SelectedGlyph?.Cell.Code);
        UpdateAnalysis();
        OnPropertyChanged(nameof(Profile));
        OnPropertyChanged(nameof(VerificationText));
    }

    [RelayCommand]
    private void ResetProfile()
    {
        _profile = _request.Profile.Clone();
        RefreshGlyphs(SelectedGlyph?.Cell.Code);
        UpdateAnalysis();
        OnPropertyChanged(nameof(Profile));
        OnPropertyChanged(nameof(VerificationText));
    }

    [RelayCommand(CanExecute = nameof(CanUseDialogs))]
    private void ImportProfile()
    {
        var path = _dialogs?.OpenFile(
            "Импортировать маппинг символов",
            CharacterMapImportFilter);
        if (path is null)
        {
            return;
        }

        try
        {
            ReplaceProfile(CharacterMapFileSerializer.Load(path));
        }
        catch (Exception exception)
        {
            _dialogs!.ShowError(exception.Message, "Ошибка импорта");
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseDialogs))]
    private void ExportProfile()
    {
        var source = Attachment.SourcePath ?? Environment.CurrentDirectory;
        var directory = Path.GetDirectoryName(source) ?? Environment.CurrentDirectory;
        var path = _dialogs?.SaveFile(
            "Экспортировать маппинг символов",
            CharacterMapExportFilter,
            Path.Combine(directory, "characters.gxtmap.json"));
        if (path is null)
        {
            return;
        }

        try
        {
            CharacterMapFileSerializer.Save(path, Profile);
        }
        catch (Exception exception)
        {
            _dialogs!.ShowError(exception.Message, "Ошибка экспорта");
        }
    }

    [RelayCommand]
    private void Apply()
    {
        try
        {
            Result = CreateResult();
            ApplySucceeded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _dialogs?.ShowError(exception.Message, "Проверка профиля");
        }
    }

    private bool CanUseDialogs() => _dialogs is not null;

    public void ReplaceProfile(CharacterMapProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _profile = profile.Clone();
        RefreshGlyphs(SelectedGlyph?.Cell.Code);
        UpdateAnalysis();
        OnPropertyChanged(nameof(Profile));
        OnPropertyChanged(nameof(VerificationText));
    }

    public CharacterMapEditorResult CreateResult()
    {
        var issues = CharacterMapService.Validate(_profile);
        if (issues.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, issues));
        }

        var result = _profile.Clone();
        result.IsVerified = true;
        return new CharacterMapEditorResult(result, SelectedApplyMode.Mode);
    }

    private void RefreshGlyphs(byte? selectedCode = null)
    {
        Glyphs.Clear();
        SelectedGlyph = null;
        GlyphImage = null;
        var value = SelectedTexture;
        if (value is null)
        {
            TextureImage = null;
            TextureMetadata = "Выберите текстуру";
            return;
        }

        var pixels = value.PreviewPixelsBgra32.Length > 0
            ? value.PreviewPixelsBgra32
            : value.PixelsBgra32;
        TextureImage = CreateBitmap(value.Width, value.Height, pixels);
        TextureMetadata = $"{value.Name} — {value.Width}×{value.Height}, {value.FormatDescription}, " +
                          $"{value.Depth} бит, mipmap: {value.MipmapCount}, {value.Platform}";

        var decodeMap = _profile.ToDecodeMap();
        foreach (var cell in GlyphAtlasService.CreateCells(value, decodeMap, _request.GameType))
        {
            Glyphs.Add(new GlyphPreviewItem(
                cell,
                CreateBitmap(cell.Width, cell.Height, GlyphAtlasService.Crop(value, cell)),
                FormatCellLabel(cell.Code, decodeMap)));
        }

        SelectedGlyph = selectedCode is null
            ? Glyphs.FirstOrDefault()
            : Glyphs.FirstOrDefault(item => item.Cell.Code == selectedCode) ?? Glyphs.FirstOrDefault();
    }

    private void UpdateAnalysis()
    {
        var issues = CharacterMapService.AnalyzeTexts(_profile, _request.CurrentTexts).ToList();
        IReadOnlyDictionary<byte, char> decodeMap;
        try
        {
            decodeMap = _profile.ToDecodeMap();
        }
        catch (ArgumentException)
        {
            decodeMap = new Dictionary<byte, char>();
        }

        var unmappedCodes = _request.RawValues
            .SelectMany(value => CharacterMapCodec.FindUnmappedExtendedCodes(value, decodeMap))
            .Distinct()
            .Order()
            .Select(code => $"Код 0x{code:X2} используется в GXT, но не назначен.");
        issues.AddRange(unmappedCodes);
        var mappedCharacters = _profile.Mappings.Select(mapping => mapping.Character).ToHashSet();
        var usage = CharacterMapService.CountCharacters(_request.CurrentTexts)
            .Where(pair => pair.Key > 0x7F || mappedCharacters.Contains(pair.Key))
            .OrderBy(pair => pair.Key)
            .Select(pair => $"{pair.Key}: {pair.Value}")
            .ToArray();
        var validation = issues.Count == 0
            ? "Профиль не содержит конфликтов; все используемые символы и байты назначены."
            : string.Join(Environment.NewLine, issues.Distinct(StringComparer.Ordinal));
        AnalysisText = usage.Length == 0
            ? validation
            : validation + Environment.NewLine + Environment.NewLine +
              "Использование локализованных символов:" + Environment.NewLine +
              string.Join(", ", usage);
    }

    private bool IsVisibleFontAtlas(TxdTexture texture) =>
        texture.IsFontAtlas &&
        (_request.GameType != GXTType.GtaViceCity ||
         !texture.Name.Equals("pager", StringComparison.OrdinalIgnoreCase));

    private static string FormatCellLabel(
        byte? code,
        IReadOnlyDictionary<byte, char> decodeMap)
    {
        if (code is not { } value)
        {
            return "Вне диапазона";
        }

        var characters = new List<char>();
        if (value is >= 0x20 and <= 0x7E)
        {
            characters.Add((char)value);
        }

        if (decodeMap.TryGetValue(value, out var mapped) && !characters.Contains(mapped))
        {
            characters.Add(mapped);
        }

        return characters.Count == 0
            ? $"0x{value:X2}"
            : $"0x{value:X2}  {string.Join('/', characters)}";
    }

    private static BitmapSource CreateBitmap(int width, int height, byte[] pixels)
    {
        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            checked(width * 4));
        bitmap.Freeze();
        return bitmap;
    }
}

public sealed record GlyphPreviewItem(GlyphCell Cell, ImageSource Image, string Label);

public sealed record CharacterMapApplyModeOption(CharacterMapApplyMode Mode, string DisplayName);
