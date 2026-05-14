using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ProEdit.Word.Avalonia;

public partial class PickerDialog : Window
{
    private readonly ListBox _itemsList;

    public PickerDialog()
        : this("Select Item", Array.Empty<PickerItem>())
    {
    }

    public PickerDialog(string title, IReadOnlyList<PickerItem> items)
    {
        InitializeComponent();
        Title = title;
        _itemsList = this.FindControl<ListBox>("ItemsList")!;
        _itemsList.ItemsSource = items ?? Array.Empty<PickerItem>();
        if (_itemsList.ItemCount > 0)
        {
            _itemsList.SelectedIndex = 0;
        }
    }

    private void OnInsertClick(object? sender, RoutedEventArgs e)
    {
        Close(_itemsList.SelectedItem as PickerItem);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnItemDoubleTapped(object? sender, RoutedEventArgs e)
    {
        Close(_itemsList.SelectedItem as PickerItem);
    }
}
