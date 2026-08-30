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

public partial class TxdViewerViewModel : ObservableObject, IDisposable
{
    private readonly CharacterMapEditorRequest _request;
    private readonly IDialogService? _dialogs;
    private readonly ILocalizationService _localization;
    private readonly GameFontPreviewService _previewService = new();
    private readonly FontMetricsProfile? _initialFontMetrics;
    private readonly CharacterMapProfile? _asiBaseCharacterMap;
    private readonly FontMetricsProfile? _asiBaseFontMetrics;
    private CharacterMapProfile _profile;
    private FontMetricsProfile? _fontMetrics;

    public TxdViewerViewModel(
        CharacterMapEditorRequest request,
        IDialogService? dialogs = null,
        ILocalizationService? localization = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        _request = request;
        _dialogs = dialogs;
        _localization = localization ?? LocalizationProvider.Current;
        _profile = request.Profile.Clone();
        _initialFontMetrics = request.FontMetrics?.Clone();
        _asiBaseCharacterMap = request.AsiBaseCharacterMap?.Clone();
        _asiBaseFontMetrics = request.AsiBaseFontMetrics?.Clone();
        _fontMetrics = _initialFontMetrics?.Clone();
        Attachment = request.Attachment;
        ApplyModes = CreateApplyModes();
        FontStyles = CreateFontStyles();
        RenderContexts = CreateRenderContexts();
        PreviewScales = CreatePreviewScales();
        selectedApplyMode = ApplyModes[0];
        selectedFontStyle = FontStyles.Single(option =>
            option.Style == ViceCityFontStyle.Standard);
        selectedRenderContext = RenderContexts[0];
        selectedPreviewScale = PreviewScales[0];
        previewText = _localization.Get("Txd.Preview.DefaultText");
        textureMetadata = _localization.Get("Txd.ChooseTexture");
        previewStatus = _localization.Get(IsGameFontPreviewAvailable
            ? "Txd.Preview.MissingMetrics"
            : "Txd.Preview.UnavailableForGame");
        _localization.LanguageChanged += OnLanguageChanged;

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

    public IReadOnlyList<CharacterMapApplyModeOption> ApplyModes { get; private set; }

    public IReadOnlyList<ViceCityFontStyleOption> FontStyles { get; private set; }

    public IReadOnlyList<FontRenderContextOption> RenderContexts { get; private set; }

    public IReadOnlyList<PreviewScaleOption> PreviewScales { get; private set; }

    public CharacterMapProfile Profile => _profile;

    public FontMetricsProfile? FontMetrics => _fontMetrics;

    public string? AsiProfileStatus => _request.AsiProfileStatus;

    public bool HasAsiProfileStatus => !string.IsNullOrWhiteSpace(AsiProfileStatus);

    public bool IsGameFontPreviewAvailable => _request.GameType == GXTType.GtaViceCity;

    public FontTextureKind? SelectedFont => GetFontTextureKind(SelectedTexture);

    public FontTextureKind PreviewFont =>
        FontMetricsService.GetEffectiveFont(SelectedFontStyle.Style);

    public GameFontLayoutResult? PreviewLayout { get; private set; }

    public char? SelectedGlyphCharacter => SelectedGlyph?.Cell.Character;

    public byte? SelectedGlyphCode => SelectedGlyph?.Cell.Code;

    public int? SelectedMetricIndex => TryGetMetricIndex(SelectedGlyphCode);

    public ushort? SelectedFont1Advance => TryGetAdvance(FontTextureKind.Font1);

    public ushort? SelectedFont2Advance => TryGetAdvance(FontTextureKind.Font2);

    public ushort? SelectedBaseAdvance => TryResolveSelectedMetric()?.BaseAdvance;

    public ushort? SelectedContextOverrideAdvance => TryResolveSelectedMetric()?.ContextOverride;

    public ushort? SelectedEffectiveAdvance => TryResolveSelectedMetric()?.EffectiveAdvance;

    public CharacterMapEditorResult? Result { get; private set; }

    public event EventHandler? ApplySucceeded;

    public string GlyphAtlasHeading => _request.GameType == GXTType.GtaViceCity
        ? _localization.Get("Txd.GlyphHeading.ViceCity")
        : _localization.Get("Txd.GlyphHeading.Other");

    public string VerificationText => _profile.IsVerified
        ? _localization.Get("Txd.Profile.Verified")
        : _localization.Get("Txd.Profile.Draft");

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
    private ImageSource? previewImage;

    [ObservableProperty]
    private string textureMetadata = string.Empty;

    [ObservableProperty]
    private string analysisText = string.Empty;

    [ObservableProperty]
    private string previewText = string.Empty;

    [ObservableProperty]
    private string previewStatus = string.Empty;

    [ObservableProperty]
    private bool showMetricGuides = true;

    [ObservableProperty]
    private CharacterMapApplyModeOption selectedApplyMode;

    [ObservableProperty]
    private ViceCityFontStyleOption selectedFontStyle;

    [ObservableProperty]
    private FontRenderContextOption selectedRenderContext;

    [ObservableProperty]
    private PreviewScaleOption selectedPreviewScale;

    partial void OnSelectedTextureChanged(TxdTexture? value)
    {
        OnPropertyChanged(nameof(SelectedFont));
        RefreshGlyphs();
    }

    partial void OnSelectedGlyphChanged(GlyphPreviewItem? value)
    {
        GlyphImage = value?.Image;
        var mapping = value?.Cell.Code is { } code
            ? _profile.Mappings.SingleOrDefault(item => item.Codes.Contains(code))
            : null;
        AssignmentText = mapping?.Character.ToString() ?? string.Empty;
        IsPreferredCode = mapping is not null && value?.Cell.Code == mapping.PreferredCode;
        NotifySelectedMetricProperties();
    }

    partial void OnPreviewTextChanged(string value) => RefreshGameFontPreview();

    partial void OnShowMetricGuidesChanged(bool value) => RefreshGameFontPreview();

    partial void OnSelectedFontStyleChanged(ViceCityFontStyleOption value)
    {
        OnPropertyChanged(nameof(PreviewFont));
        RefreshGameFontPreview();
    }

    partial void OnSelectedRenderContextChanged(FontRenderContextOption value)
    {
        NotifySelectedMetricProperties();
        RefreshGameFontPreview();
    }

    partial void OnSelectedPreviewScaleChanged(PreviewScaleOption value) =>
        RefreshGameFontPreview();

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
        if (IsGameFontPreviewAvailable)
        {
            SetFontMetrics(FontMetricsPresets.BelarusianViceCity);
        }

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
            _localization.Get("Txd.ImportMapping"),
            _localization.Get("Filter.CharacterMapImport"));
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
            _dialogs!.ShowError(exception.Message, _localization.Get("Txd.ImportError"));
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseDialogs))]
    private void ExportProfile()
    {
        var source = Attachment.SourcePath ?? Environment.CurrentDirectory;
        var directory = Path.GetDirectoryName(source) ?? Environment.CurrentDirectory;
        var path = _dialogs?.SaveFile(
            _localization.Get("Txd.ExportMapping"),
            _localization.Get("Filter.CharacterMapExport"),
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
            _dialogs!.ShowError(exception.Message, _localization.Get("Txd.ExportError"));
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseAsiCharacterMap))]
    private void UseAsiCharacterMap() => ReplaceProfile(_asiBaseCharacterMap!);

    [RelayCommand(CanExecute = nameof(CanImportFontMetrics))]
    private void ImportFontMetrics()
    {
        var path = _dialogs?.OpenFile(
            _localization.Get("Txd.Metrics.ImportTitle"),
            _localization.Get("Filter.FontMetrics"));
        if (path is null)
        {
            return;
        }

        try
        {
            ReplaceFontMetrics(FontMetricsFileSerializer.Load(path));
        }
        catch (Exception exception)
        {
            _dialogs!.ShowError(exception.Message, _localization.Get("Txd.Metrics.ImportError"));
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportFontMetrics))]
    private void ExportFontMetrics()
    {
        if (_fontMetrics is null)
        {
            return;
        }

        var source = Attachment.SourcePath ?? Environment.CurrentDirectory;
        var directory = Path.GetDirectoryName(source) ?? Environment.CurrentDirectory;
        var path = _dialogs?.SaveFile(
            _localization.Get("Txd.Metrics.ExportTitle"),
            _localization.Get("Filter.FontMetrics"),
            Path.Combine(directory, "font.fontmetrics.json"));
        if (path is null)
        {
            return;
        }

        try
        {
            FontMetricsFileSerializer.Save(path, _fontMetrics);
        }
        catch (Exception exception)
        {
            _dialogs!.ShowError(exception.Message, _localization.Get("Txd.Metrics.ExportError"));
        }
    }

    [RelayCommand(CanExecute = nameof(CanManageFontMetrics))]
    private void UseBelarusianFontMetrics() =>
        ReplaceFontMetrics(FontMetricsPresets.BelarusianViceCity);

    [RelayCommand(CanExecute = nameof(CanResetFontMetrics))]
    private void ResetFontMetrics() => ReplaceFontMetrics(_initialFontMetrics);

    [RelayCommand(CanExecute = nameof(CanUseAsiFontMetrics))]
    private void UseAsiFontMetrics() => ReplaceFontMetrics(_asiBaseFontMetrics);

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
            _dialogs?.ShowError(exception.Message, _localization.Get("Txd.ProfileValidation"));
        }
    }

    private bool CanUseDialogs() => _dialogs is not null;

    private bool CanManageFontMetrics() => IsGameFontPreviewAvailable;

    private bool CanImportFontMetrics() => CanManageFontMetrics() && CanUseDialogs();

    private bool CanExportFontMetrics() =>
        CanManageFontMetrics() && CanUseDialogs() && _fontMetrics is not null;

    private bool CanResetFontMetrics() => CanManageFontMetrics();

    private bool CanUseAsiCharacterMap() => _asiBaseCharacterMap is not null;

    private bool CanUseAsiFontMetrics() =>
        CanManageFontMetrics() && _asiBaseFontMetrics is not null;

    public void ReplaceProfile(CharacterMapProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _profile = profile.Clone();
        RefreshGlyphs(SelectedGlyph?.Cell.Code);
        UpdateAnalysis();
        OnPropertyChanged(nameof(Profile));
        OnPropertyChanged(nameof(VerificationText));
    }

    public void ReplaceFontMetrics(FontMetricsProfile? profile)
    {
        if (!IsGameFontPreviewAvailable)
        {
            return;
        }

        SetFontMetrics(profile);
        RefreshGameFontPreview();
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
        if (IsGameFontPreviewAvailable && _fontMetrics is not null)
        {
            var metricIssues = FontMetricsValidator.Validate(_fontMetrics);
            if (metricIssues.Count > 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, metricIssues));
            }
        }

        return new CharacterMapEditorResult(
            result,
            SelectedApplyMode.Mode,
            _fontMetrics?.Clone());
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
            TextureMetadata = _localization.Get("Txd.ChooseTexture");
            RefreshGameFontPreview();
            return;
        }

        var pixels = value.PreviewPixelsBgra32.Length > 0
            ? value.PreviewPixelsBgra32
            : value.PixelsBgra32;
        TextureMetadata = _localization.Format(
            "Txd.TextureMetadata",
            value.Name,
            value.Width,
            value.Height,
            value.FormatDescription,
            value.Depth,
            value.MipmapCount,
            value.Platform);
        if (!HasValidBgraPixels(value.Width, value.Height, pixels))
        {
            TextureImage = null;
            RefreshGameFontPreview();
            return;
        }

        TextureImage = CreateBitmap(value.Width, value.Height, pixels);

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
        RefreshGameFontPreview();
    }

    private void SetFontMetrics(FontMetricsProfile? profile)
    {
        _fontMetrics = profile?.Clone();
        OnPropertyChanged(nameof(FontMetrics));
        NotifySelectedMetricProperties();
        ExportFontMetricsCommand.NotifyCanExecuteChanged();
        ResetFontMetricsCommand.NotifyCanExecuteChanged();
    }

    private void RefreshGameFontPreview()
    {
        PreviewLayout = null;
        OnPropertyChanged(nameof(PreviewLayout));
        PreviewImage = null;
        if (!IsGameFontPreviewAvailable)
        {
            PreviewStatus = _localization.Get("Txd.Preview.UnavailableForGame");
            return;
        }

        if (_fontMetrics is null)
        {
            PreviewStatus = _localization.Get("Txd.Preview.MissingMetrics");
            return;
        }

        var previewFont = PreviewFont;
        var texture = Textures.FirstOrDefault(candidate =>
            GetFontTextureKind(candidate) == previewFont);
        if (texture is null)
        {
            PreviewStatus = _localization.Format(
                "Txd.Preview.MissingTexture",
                FontMetricsService.GetTextureName(previewFont));
            return;
        }

        try
        {
            var layout = _previewService.Layout(
                PreviewText,
                texture,
                _profile,
                _fontMetrics,
                SelectedFontStyle.Style,
                SelectedRenderContext.Context,
                SelectedPreviewScale.Scale);
            PreviewLayout = layout;
            OnPropertyChanged(nameof(PreviewLayout));
            var renderedWidth = layout.PixelWidth;
            if (layout.CanRender && layout.PixelWidth > 0 && layout.PixelHeight > 0)
            {
                var rendered = GameFontPreviewService.Render(texture, layout, ShowMetricGuides);
                renderedWidth = rendered.Width;
                PreviewImage = CreateBitmap(
                    rendered.Width,
                    rendered.Height,
                    rendered.PixelsBgra32,
                    rendered.Stride);
            }

            PreviewStatus = layout.Issues.Count == 0
                ? _localization.Format(
                    "Txd.Preview.Ready",
                    layout.Glyphs.Count,
                    layout.AdvanceWidth,
                    renderedWidth)
                : string.Join(
                    Environment.NewLine,
                    layout.Issues.Select(FormatPreviewIssue).Distinct(StringComparer.Ordinal));
        }
        catch (Exception exception)
        {
            PreviewStatus = _localization.Format("Txd.Preview.Failed", exception.Message);
        }
    }

    private string FormatPreviewIssue(GameFontPreviewIssue issue) => issue.Kind switch
    {
        GameFontPreviewIssueKind.CharacterCannotBeEncoded => _localization.Format(
            "Txd.Preview.Issue.CharacterCannotBeEncoded",
            issue.Character),
        GameFontPreviewIssueKind.MetricUnavailable => _localization.Format(
            "Txd.Preview.Issue.MetricUnavailable",
            issue.Code),
        GameFontPreviewIssueKind.GlyphUnavailable => _localization.Format(
            "Txd.Preview.Issue.GlyphUnavailable",
            issue.Code,
            FontMetricsService.GetTextureName(PreviewFont)),
        GameFontPreviewIssueKind.InvalidMetrics => _localization.Get(
            "Txd.Preview.Issue.InvalidMetrics"),
        GameFontPreviewIssueKind.InvalidMapping => _localization.Get(
            "Txd.Preview.Issue.InvalidMapping"),
        _ => _localization.Get("Txd.Preview.Issue.UnsupportedFont"),
    };

    private void NotifySelectedMetricProperties()
    {
        OnPropertyChanged(nameof(SelectedGlyphCharacter));
        OnPropertyChanged(nameof(SelectedGlyphCode));
        OnPropertyChanged(nameof(SelectedMetricIndex));
        OnPropertyChanged(nameof(SelectedFont1Advance));
        OnPropertyChanged(nameof(SelectedFont2Advance));
        OnPropertyChanged(nameof(SelectedBaseAdvance));
        OnPropertyChanged(nameof(SelectedContextOverrideAdvance));
        OnPropertyChanged(nameof(SelectedEffectiveAdvance));
    }

    private int? TryGetMetricIndex(byte? code)
    {
        if (!IsGameFontPreviewAvailable || code is null)
        {
            return null;
        }

        try
        {
            return FontMetricsService.GetMetricIndex(code.Value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private ushort? TryGetAdvance(FontTextureKind font)
    {
        if (_fontMetrics is null || SelectedGlyphCode is not { } code)
        {
            return null;
        }

        try
        {
            return FontMetricsService.GetBaseAdvance(_fontMetrics, font, code);
        }
        catch (Exception exception) when (
            exception is ArgumentOutOfRangeException or InvalidDataException)
        {
            return null;
        }
    }

    private FontMetricResolution? TryResolveSelectedMetric()
    {
        if (_fontMetrics is null || SelectedGlyphCode is not { } code || SelectedFont is not { } font)
        {
            return null;
        }

        try
        {
            return FontMetricsService.Resolve(
                _fontMetrics,
                FontMetricsService.GetStyle(font),
                code,
                SelectedRenderContext.Context);
        }
        catch (Exception exception) when (
            exception is ArgumentOutOfRangeException or InvalidDataException)
        {
            return null;
        }
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
            .Select(code => _localization.Format("Txd.UnmappedCode", code));
        issues.AddRange(unmappedCodes);
        var mappedCharacters = _profile.Mappings.Select(mapping => mapping.Character).ToHashSet();
        var usage = CharacterMapService.CountCharacters(_request.CurrentTexts)
            .Where(pair => pair.Key > 0x7F || mappedCharacters.Contains(pair.Key))
            .OrderBy(pair => pair.Key)
            .Select(pair => $"{pair.Key}: {pair.Value}")
            .ToArray();
        var validation = issues.Count == 0
            ? _localization.Get("Txd.Analysis.Valid")
            : string.Join(Environment.NewLine, issues.Distinct(StringComparer.Ordinal));
        AnalysisText = usage.Length == 0
            ? validation
            : validation + Environment.NewLine + Environment.NewLine +
              _localization.Get("Txd.Analysis.Usage") + Environment.NewLine +
              string.Join(", ", usage);
    }

    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
        GC.SuppressFinalize(this);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        var selectedMode = SelectedApplyMode.Mode;
        var selectedStyle = SelectedFontStyle.Style;
        var selectedContext = SelectedRenderContext.Context;
        var selectedScale = SelectedPreviewScale.Scale;
        ApplyModes = CreateApplyModes();
        FontStyles = CreateFontStyles();
        RenderContexts = CreateRenderContexts();
        PreviewScales = CreatePreviewScales();
        OnPropertyChanged(nameof(ApplyModes));
        OnPropertyChanged(nameof(FontStyles));
        OnPropertyChanged(nameof(RenderContexts));
        OnPropertyChanged(nameof(PreviewScales));
        SelectedApplyMode = ApplyModes.First(option => option.Mode == selectedMode);
        SelectedFontStyle = FontStyles.First(option => option.Style == selectedStyle);
        SelectedRenderContext = RenderContexts.First(option => option.Context == selectedContext);
        SelectedPreviewScale = PreviewScales.First(option => option.Scale == selectedScale);
        OnPropertyChanged(nameof(GlyphAtlasHeading));
        OnPropertyChanged(nameof(VerificationText));
        RefreshGlyphs(SelectedGlyph?.Cell.Code);
        UpdateAnalysis();
    }

    private CharacterMapApplyModeOption[] CreateApplyModes() =>
    [
        new(
            CharacterMapApplyMode.Interpret,
            _localization.Get("Options.Apply.Interpret")),
        new(
            CharacterMapApplyMode.Reencode,
            _localization.Get("Options.Apply.Reencode")),
    ];

    private ViceCityFontStyleOption[] CreateFontStyles() =>
    [
        CreateFontStyleOption(ViceCityFontStyle.Bank, "Bank"),
        CreateFontStyleOption(ViceCityFontStyle.Standard, "Standard"),
        CreateFontStyleOption(ViceCityFontStyle.Heading, "Heading"),
    ];

    private ViceCityFontStyleOption CreateFontStyleOption(
        ViceCityFontStyle style,
        string resourceSuffix) => new(
            style,
            _localization.Get($"Txd.Preview.Style.{resourceSuffix}"));

    private FontRenderContextOption[] CreateRenderContexts() =>
    [
        CreateRenderContextOption(FontRenderContext.Default, "Default"),
        CreateRenderContextOption(FontRenderContext.Gameplay, "Gameplay"),
        CreateRenderContextOption(FontRenderContext.Subtitles, "Subtitles"),
        CreateRenderContextOption(FontRenderContext.MainMenu, "MainMenu"),
        CreateRenderContextOption(FontRenderContext.SaveLoad, "SaveLoad"),
        CreateRenderContextOption(FontRenderContext.ExitConfirmation, "ExitConfirmation"),
    ];

    private FontRenderContextOption CreateRenderContextOption(
        FontRenderContext context,
        string resourceSuffix) => new(
            context,
            _localization.Get($"Txd.Preview.Context.{resourceSuffix}"));

    private PreviewScaleOption[] CreatePreviewScales() =>
    [
        new(1, _localization.Format("Txd.Preview.ScaleOption", 1)),
        new(2, _localization.Format("Txd.Preview.ScaleOption", 2)),
        new(4, _localization.Format("Txd.Preview.ScaleOption", 4)),
    ];

    private bool IsVisibleFontAtlas(TxdTexture texture) =>
        texture.IsFontAtlas &&
        (_request.GameType != GXTType.GtaViceCity ||
         !texture.Name.Equals("pager", StringComparison.OrdinalIgnoreCase));

    private static FontTextureKind? GetFontTextureKind(TxdTexture? texture)
    {
        if (texture?.Name.Equals("font1", StringComparison.OrdinalIgnoreCase) == true)
        {
            return FontTextureKind.Font1;
        }

        if (texture?.Name.Equals("font2", StringComparison.OrdinalIgnoreCase) == true)
        {
            return FontTextureKind.Font2;
        }

        return null;
    }

    private static bool HasValidBgraPixels(int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        try
        {
            return pixels.Length == checked(width * height * 4);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private string FormatCellLabel(
        byte? code,
        IReadOnlyDictionary<byte, char> decodeMap)
    {
        if (code is not { } value)
        {
            return _localization.Get("Txd.OutOfRange");
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

    private static BitmapSource CreateBitmap(int width, int height, byte[] pixels) =>
        CreateBitmap(width, height, pixels, checked(width * 4));

    private static BitmapSource CreateBitmap(int width, int height, byte[] pixels, int stride)
    {
        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        bitmap.Freeze();
        return bitmap;
    }
}

public sealed record GlyphPreviewItem(GlyphCell Cell, ImageSource Image, string Label);

public sealed record CharacterMapApplyModeOption(CharacterMapApplyMode Mode, string DisplayName);

public sealed record ViceCityFontStyleOption(ViceCityFontStyle Style, string DisplayName);

public sealed record FontRenderContextOption(FontRenderContext Context, string DisplayName);

public sealed record PreviewScaleOption(int Scale, string DisplayName);
