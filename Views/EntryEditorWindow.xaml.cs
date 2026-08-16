using System.Windows;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Views;

public partial class EntryEditorWindow : Window
{
    private readonly EntryEditorRequest _request;
    private readonly ILocalizationService _localization;

    public EntryEditorWindow(EntryEditorRequest request, ILocalizationService? localization = null)
    {
        _request = request;
        _localization = localization ?? LocalizationProvider.Current;
        InitializeComponent();

        UpdateLocalizedText();
        _localization.LanguageChanged += Localization_OnLanguageChanged;
        Closed += (_, _) => _localization.LanguageChanged -= Localization_OnLanguageChanged;
        NameTextBox.Text = request.Name;
        NameTextBox.IsReadOnly = !request.IsAdding;
        ValueTextBox.Text = request.Text;
        SourceTextBox.Text = request.SourceText ?? string.Empty;
        SourcePanel.Visibility = request.SourceText is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        OccurrencesGrid.ItemsSource = request.Occurrences;
        OccurrencesPanel.Visibility = request.Occurrences.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        OccurrencesRow.Height = request.Occurrences.Count == 0
            ? new GridLength(0)
            : new GridLength(1.4, GridUnitType.Star);
        CommentTextBox.Text = request.Comment ?? string.Empty;

        if (request.Tables.Count == 0)
        {
            TablePanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            TableComboBox.ItemsSource = request.Tables;
            TableComboBox.SelectedItem = request.Tables.FirstOrDefault(table =>
                string.Equals(table.RawName, request.RawTableName, StringComparison.Ordinal)) ??
                request.Tables[0];
        }

        Loaded += (_, _) =>
        {
            (request.IsAdding ? NameTextBox : ValueTextBox).Focus();
            ValueTextBox.CaretIndex = ValueTextBox.Text.Length;
        };
    }

    public EntryEditorResult? Result { get; private set; }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        var table = (TableComboBox.SelectedItem as TableOption)?.RawName;
        var validation = EntryEditorValidator.Validate(
            _request,
            NameTextBox.Text,
            ValueTextBox.Text,
            table,
            CommentTextBox.Text);
        if (!validation.IsValid)
        {
            System.Windows.Controls.Control control = validation.ErrorField switch
            {
                EntryEditorField.Text => ValueTextBox,
                EntryEditorField.Table => TableComboBox,
                _ => NameTextBox,
            };
            ShowValidationError(validation.ErrorMessage!, control);
            return;
        }

        Result = validation.Result;
        DialogResult = true;
    }

    private void ShowValidationError(string message, System.Windows.Controls.Control control)
    {
        MessageBox.Show(
            this,
            message,
            _localization.Get("Entry.ValidationTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        control.Focus();
    }

    private void Localization_OnLanguageChanged(object? sender, EventArgs e) =>
        UpdateLocalizedText();

    private void UpdateLocalizedText()
    {
        Title = _localization.Get(_request.IsAdding ? "Entry.Title.Add" : "Entry.Title.Edit");
        SaveButton.Content = _localization.Get(_request.IsAdding ? "Common.Add" : "Common.Save");
    }
}
