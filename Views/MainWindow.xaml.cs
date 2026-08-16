using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using GTA_GXT_Editor.ViewModels;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.ComparisonColumns.CollectionChanged += ComparisonColumns_OnCollectionChanged;
        Closing += MainWindow_OnClosing;
    }

    private void EntriesGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            FindVisualParent<CheckBox>(source) is not null)
        {
            return;
        }

        if (DataContext is MainWindowViewModel viewModel && viewModel.EditEntryCommand.CanExecute(null))
        {
            viewModel.EditEntryCommand.Execute(null);
        }
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && !viewModel.CanClose())
        {
            e.Cancel = true;
        }
    }

    private void DropDownButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
        {
            return;
        }

        var placementTarget = button.Tag as FrameworkElement ?? button;
        menu.PlacementTarget = placementTarget;
        menu.Placement = PlacementMode.Bottom;
        menu.MinWidth = Math.Max(menu.MinWidth, placementTarget.ActualWidth);
        menu.IsOpen = true;
    }

    private void LanguageMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: UiLanguageOption language } &&
            DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SelectUiLanguageCommand.Execute(language);
        }
    }

    private void ComparisonColumns_OnCollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        const int fixedColumnCount = 6;
        const int comparisonColumnStartIndex = 3;

        while (EntriesGrid.Columns.Count > fixedColumnCount)
        {
            EntriesGrid.Columns.RemoveAt(comparisonColumnStartIndex);
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        for (var index = 0; index < viewModel.ComparisonColumns.Count; index++)
        {
            var comparisonColumn = viewModel.ComparisonColumns[index];
            EntriesGrid.Columns.Insert(
                comparisonColumnStartIndex + index,
                new DataGridTextColumn
                {
                    Header = comparisonColumn.Name,
                    Width = new DataGridLength(2, DataGridLengthUnitType.Star),
                    Binding = new Binding($"ComparisonTexts[{index}]"),
                });
        }
    }

    private static T? FindVisualParent<T>(DependencyObject child)
        where T : DependencyObject
    {
        for (var current = child; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T parent)
            {
                return parent;
            }
        }

        return null;
    }
}
