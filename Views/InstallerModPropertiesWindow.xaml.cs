using System.Windows;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Views;

public partial class InstallerModPropertiesWindow : Window
{
    private readonly ILocalizationService _localization;

    public InstallerModPropertiesWindow(
        string name,
        bool isRequired,
        bool isNew,
        ILocalizationService localization)
    {
        InitializeComponent();
        _localization = localization;
        Title = localization.Get(isNew ? "Installer.Mod.AddTitle" : "Installer.Mod.EditTitle");
        NameTextBox.Text = name;
        RequiredRadioButton.IsChecked = isRequired;
        OptionalRadioButton.IsChecked = !isRequired;
        Loaded += (_, _) =>
        {
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
    }

    public string ModName { get; private set; } = string.Empty;

    public bool IsRequired { get; private set; }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl))
        {
            MessageBox.Show(
                this,
                _localization.Get("Installer.Validation.ModName"),
                _localization.Get("Common.Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        ModName = name;
        IsRequired = RequiredRadioButton.IsChecked == true;
        DialogResult = true;
    }
}
