using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Vibe.Office.Editing;

namespace Vibe.Word.Avalonia;

public partial class TableInsertDialog : Window
{
    private const int MaxRows = 8;
    private const int MaxColumns = 10;
    private static readonly IBrush CellBorderBrush = new SolidColorBrush(Color.Parse("#D0D4DB"));
    private static readonly IBrush CellFillBrush = new SolidColorBrush(Color.Parse("#F7F9FC"));
    private static readonly IBrush CellSelectedBrush = new SolidColorBrush(Color.Parse("#DCEBFF"));

    private readonly UniformGrid _gridHost;
    private readonly TextBlock _selectionText;
    private readonly List<Border> _cells = new();
    private int _selectedRows = 1;
    private int _selectedColumns = 1;

    public TableInsertDialog()
    {
        InitializeComponent();
        _gridHost = this.FindControl<UniformGrid>("GridHost")!;
        _selectionText = this.FindControl<TextBlock>("SelectionText")!;
        BuildGrid();
        UpdateSelection(1, 1);
    }

    private void BuildGrid()
    {
        _gridHost.Children.Clear();
        _cells.Clear();

        for (var row = 1; row <= MaxRows; row++)
        {
            for (var column = 1; column <= MaxColumns; column++)
            {
                var cell = new Border
                {
                    Width = 20,
                    Height = 20,
                    Margin = new Thickness(2),
                    BorderBrush = CellBorderBrush,
                    BorderThickness = new Thickness(1),
                    Background = CellFillBrush,
                    Tag = new GridCell(row, column)
                };
                cell.PointerEntered += OnCellPointerEnter;
                cell.PointerPressed += OnCellPointerPressed;
                _gridHost.Children.Add(cell);
                _cells.Add(cell);
            }
        }
    }

    private void OnCellPointerEnter(object? sender, PointerEventArgs e)
    {
        if (sender is Border { Tag: GridCell cell })
        {
            UpdateSelection(cell.Row, cell.Column);
        }
    }

    private void OnCellPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { Tag: GridCell cell })
        {
            UpdateSelection(cell.Row, cell.Column);
            Close(new EditorTableInsertRequest(_selectedRows, _selectedColumns));
        }
    }

    private void OnInsertClick(object? sender, RoutedEventArgs e)
    {
        Close(new EditorTableInsertRequest(_selectedRows, _selectedColumns));
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void UpdateSelection(int rows, int columns)
    {
        _selectedRows = Math.Clamp(rows, 1, MaxRows);
        _selectedColumns = Math.Clamp(columns, 1, MaxColumns);
        _selectionText.Text = $"Insert Table: {_selectedRows} x {_selectedColumns}";

        foreach (var cell in _cells)
        {
            if (cell.Tag is not GridCell info)
            {
                continue;
            }

            var selected = info.Row <= _selectedRows && info.Column <= _selectedColumns;
            cell.Background = selected ? CellSelectedBrush : CellFillBrush;
        }
    }

    private readonly record struct GridCell(int Row, int Column);
}
