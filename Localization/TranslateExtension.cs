using System.Windows.Data;
using System.Windows.Markup;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Localization;

[MarkupExtensionReturnType(typeof(string))]
public sealed class TranslateExtension : MarkupExtension
{
    public TranslateExtension()
    {
    }

    public TranslateExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            return string.Empty;
        }

        return new Binding($"[{Key}]")
        {
            Source = LocalizationProvider.Current,
            Mode = BindingMode.OneWay,
        }.ProvideValue(serviceProvider);
    }
}
