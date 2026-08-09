using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.ViewModels;

public partial class TxdViewerViewModel : ObservableObject
{
    private readonly IReadOnlyDictionary<byte, char> _characterMap;

    public TxdViewerViewModel(TxdViewerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Attachments = request.Attachments;
        _characterMap = request.CharacterMap;
        SelectedAttachment = Attachments.Count > 0 ? Attachments[0] : null;
    }

    public IReadOnlyList<TxdAttachment> Attachments { get; }

    public ObservableCollection<TxdTexture> Textures { get; } = [];

    public ObservableCollection<GlyphPreviewItem> Glyphs { get; } = [];

    [ObservableProperty]
    private TxdAttachment? selectedAttachment;

    [ObservableProperty]
    private TxdTexture? selectedTexture;

    [ObservableProperty]
    private GlyphPreviewItem? selectedGlyph;

    [ObservableProperty]
    private ImageSource? textureImage;

    [ObservableProperty]
    private ImageSource? glyphImage;

    [ObservableProperty]
    private string textureMetadata = "Выберите текстуру";

    partial void OnSelectedAttachmentChanged(TxdAttachment? value)
    {
        Textures.Clear();
        if (value is not null)
        {
            foreach (var texture in value.Document.Textures)
            {
                Textures.Add(texture);
            }
        }

        SelectedTexture = Textures.FirstOrDefault();
    }

    partial void OnSelectedTextureChanged(TxdTexture? value)
    {
        Glyphs.Clear();
        SelectedGlyph = null;
        GlyphImage = null;
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

        foreach (var cell in GlyphAtlasService.CreateCells(value, _characterMap))
        {
            Glyphs.Add(new GlyphPreviewItem(
                cell,
                CreateBitmap(cell.Width, cell.Height, GlyphAtlasService.Crop(value, cell))));
        }

        SelectedGlyph = Glyphs.FirstOrDefault();
    }

    partial void OnSelectedGlyphChanged(GlyphPreviewItem? value)
    {
        GlyphImage = value?.Image;
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

public sealed record GlyphPreviewItem(GlyphCell Cell, ImageSource Image)
{
    public string Label => Cell.Label;
}
