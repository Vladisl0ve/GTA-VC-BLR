using System.Windows;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Views;

public partial class EntryEditorWindow : Window
{
    private readonly EntryEditorRequest _request;

    public EntryEditorWindow(EntryEditorRequest request)
    {
        _request = request;
        InitializeComponent();

        Title = request.IsAdding ? "Добавление ключа" : "Редактирование ключа";
        SaveButton.Content = request.IsAdding ? "Добавить" : "Сохранить";
        NameTextBox.Text = request.Name;
        NameTextBox.IsReadOnly = !request.IsAdding;
        ValueTextBox.Text = request.Text;

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
        var name = NameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowValidationError("Имя ключа не может быть пустым.", NameTextBox);
            return;
        }

        if (name.Length > 8)
        {
            ShowValidationError("Имя ключа может содержать не более 8 символов.", NameTextBox);
            return;
        }

        if (string.IsNullOrWhiteSpace(ValueTextBox.Text))
        {
            ShowValidationError("Текст ключа не может быть пустым.", ValueTextBox);
            return;
        }

        var table = (TableComboBox.SelectedItem as TableOption)?.RawName;
        if (_request.Tables.Count > 0 && table is null)
        {
            ShowValidationError("Выберите таблицу.", TableComboBox);
            return;
        }

        Result = new EntryEditorResult(name, ValueTextBox.Text, table);
        DialogResult = true;
    }

    private void ShowValidationError(string message, System.Windows.Controls.Control control)
    {
        MessageBox.Show(this, message, "Проверка данных", MessageBoxButton.OK, MessageBoxImage.Warning);
        control.Focus();
    }
}
