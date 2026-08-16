using System.Windows;
using System.Windows.Media;

namespace GTA_GXT_Editor.Tests;

[STATestClass]
public sealed class FluentThemeResourceTests
{
    [TestMethod]
    public void CheckBoxFallbackBrushes_ResolveFromFluentUncheckedKeys()
    {
        var application = EnsureApplication();
        var fluent = new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml",
                UriKind.Absolute)
        };
        application.Resources.MergedDictionaries.Add(fluent);

        try
        {
            var fillColor = (Color)application.FindResource("SubtleFillColorTransparent");
            application.Resources["CheckBoxBackground"] = new SolidColorBrush(fillColor);
            application.Resources["CheckBoxBorderBrush"] = new SolidColorBrush(fillColor);

            Assert.IsInstanceOfType<Brush>(application.FindResource("CheckBoxBackgroundUnchecked"));
            Assert.IsInstanceOfType<Brush>(application.FindResource("CheckBoxBorderBrushUnchecked"));
            Assert.IsInstanceOfType<Brush>(application.FindResource("CheckBoxBackground"));
            Assert.IsInstanceOfType<Brush>(application.FindResource("CheckBoxBorderBrush"));
        }
        finally
        {
            application.Resources.Remove("CheckBoxBackground");
            application.Resources.Remove("CheckBoxBorderBrush");
            application.Resources.MergedDictionaries.Remove(fluent);
        }
    }

    private static Application EnsureApplication()
    {
        return Application.Current ?? new Application();
    }
}
