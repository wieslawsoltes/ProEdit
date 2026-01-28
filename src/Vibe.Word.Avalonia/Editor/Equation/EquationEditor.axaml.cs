using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Vibe.Office.Documents;

namespace Vibe.Word.Avalonia;

public sealed partial class EquationEditor : UserControl
{
    private EquationInline? _equation;
    private MathNode? _selectedNode;
    private bool _suppressUpdates;
    private readonly Dictionary<MathElement, TreeViewItem> _elementMap = new();

    public event EventHandler? EquationEdited;

    public EquationEditor()
    {
        InitializeComponent();

        HookPaletteButtons();
        HookEditors();
        HookMatrixButtons();
        HookTreeSelection();
        SetEditingEnabled(false);
    }

    private T Control<T>(string name) where T : Control => this.FindControl<T>(name)!;

    public void SetEquation(EquationInline? equation)
    {
        _equation = equation;
        RefreshTree();
        SetEditingEnabled(_equation is not null);
    }

    private void HookPaletteButtons()
    {
        Control<Button>("AddRunButton")!.Click += (_, _) => InsertElement(new MathRun { Text = "x" });
        Control<Button>("AddRowButton")!.Click += (_, _) => InsertElement(new MathRow());
        Control<Button>("AddFractionButton")!.Click += (_, _) => InsertElement(new MathFraction(new MathRun { Text = "a" }, new MathRun { Text = "b" }));
        Control<Button>("AddScriptButton")!.Click += (_, _) => InsertElement(new MathScript(new MathRun { Text = "x" }) { Superscript = new MathRun { Text = "2" } });
        Control<Button>("AddRadicalButton")!.Click += (_, _) => InsertElement(new MathRadical(new MathRun { Text = "x" }));
        Control<Button>("AddAccentButton")!.Click += (_, _) => InsertElement(new MathAccent(new MathRun { Text = "x" }));
        Control<Button>("AddDelimiterButton")!.Click += (_, _) => InsertElement(new MathDelimiter(new MathRow()) { BeginChar = "(", EndChar = ")" });
        Control<Button>("AddNaryButton")!.Click += (_, _) => InsertElement(new MathNary(new MathRun { Text = "x" }) { Subscript = new MathRun { Text = "i" }, Superscript = new MathRun { Text = "n" } });
        Control<Button>("AddMatrixButton")!.Click += (_, _) =>
        {
            var matrix = new MathMatrix(new[]
            {
                new MathElement[] { new MathRun { Text = "a" }, new MathRun { Text = "b" } },
                new MathElement[] { new MathRun { Text = "c" }, new MathRun { Text = "d" } }
            });
            InsertElement(matrix);
        };
    }

    private void HookEditors()
    {
        Control<TextBox>("RunTextBox")!.TextChanged += OnRunTextChanged;
        Control<TextBox>("AccentTextBox")!.TextChanged += OnAccentTextChanged;
        Control<TextBox>("DelimiterBeginTextBox")!.TextChanged += OnDelimiterChanged;
        Control<TextBox>("DelimiterEndTextBox")!.TextChanged += OnDelimiterChanged;
        Control<TextBox>("DelimiterSeparatorTextBox")!.TextChanged += OnDelimiterChanged;
        Control<TextBox>("NaryOperatorTextBox")!.TextChanged += OnNaryChanged;
        Control<CheckBox>("NaryHideSubCheckBox")!.IsCheckedChanged += OnNaryChanged;
        Control<CheckBox>("NaryHideSupCheckBox")!.IsCheckedChanged += OnNaryChanged;
        Control<Button>("RemoveNodeButton")!.Click += (_, _) => RemoveSelectedNode();
    }

    private void HookMatrixButtons()
    {
        Control<Button>("AddMatrixRowButton")!.Click += (_, _) => AddMatrixRow();
        Control<Button>("AddMatrixColumnButton")!.Click += (_, _) => AddMatrixColumn();
    }

    private void HookTreeSelection()
    {
        var tree = Control<TreeView>("EquationTree")!;
        tree.SelectionChanged += OnTreeSelectionChanged;
    }

    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var tree = (TreeView)sender!;
        if (tree.SelectedItem is TreeViewItem item && item.Tag is MathNode node)
        {
            _selectedNode = node;
        }
        else
        {
            _selectedNode = null;
        }

        UpdateSelectionUI();
    }

    private void RefreshTree()
    {
        var tree = Control<TreeView>("EquationTree")!;
        tree.ItemsSource = null;
        _elementMap.Clear();

        if (_equation is null)
        {
            _selectedNode = null;
            UpdateSelectionUI();
            return;
        }

        var rootItem = BuildElementNode(_equation.Root, null, "Root");
        tree.ItemsSource = new[] { rootItem };
        rootItem.IsExpanded = true;

        if (_selectedNode?.Element is not null && _elementMap.TryGetValue(_selectedNode.Element, out var selectedItem))
        {
            selectedItem.IsSelected = true;
            selectedItem.BringIntoView();
        }
        else
        {
            rootItem.IsSelected = true;
        }
    }

    private void UpdateSelectionUI()
    {
        var selectedText = Control<TextBlock>("SelectedNodeText")!;
        var runPanel = Control<StackPanel>("RunEditorPanel")!;
        var accentPanel = Control<StackPanel>("AccentEditorPanel")!;
        var delimiterPanel = Control<StackPanel>("DelimiterEditorPanel")!;
        var naryPanel = Control<StackPanel>("NaryEditorPanel")!;
        var matrixPanel = Control<StackPanel>("MatrixEditorPanel")!;

        runPanel.IsVisible = false;
        accentPanel.IsVisible = false;
        delimiterPanel.IsVisible = false;
        naryPanel.IsVisible = false;
        matrixPanel.IsVisible = false;

        _suppressUpdates = true;

        if (_selectedNode is null)
        {
            selectedText.Text = _equation is null ? "No equation selected" : "Select a node to edit.";
            _suppressUpdates = false;
            return;
        }

        selectedText.Text = _selectedNode.Label;

        if (_selectedNode.Element is MathRun run)
        {
            runPanel.IsVisible = true;
            Control<TextBox>("RunTextBox")!.Text = run.Text;
        }
        else if (_selectedNode.Element is MathAccent accent)
        {
            accentPanel.IsVisible = true;
            Control<TextBox>("AccentTextBox")!.Text = accent.AccentChar;
        }
        else if (_selectedNode.Element is MathDelimiter delimiter)
        {
            delimiterPanel.IsVisible = true;
            Control<TextBox>("DelimiterBeginTextBox")!.Text = delimiter.BeginChar ?? string.Empty;
            Control<TextBox>("DelimiterEndTextBox")!.Text = delimiter.EndChar ?? string.Empty;
            Control<TextBox>("DelimiterSeparatorTextBox")!.Text = delimiter.SeparatorChar ?? string.Empty;
        }
        else if (_selectedNode.Element is MathNary nary)
        {
            naryPanel.IsVisible = true;
            Control<TextBox>("NaryOperatorTextBox")!.Text = nary.OperatorChar;
            Control<CheckBox>("NaryHideSubCheckBox")!.IsChecked = nary.HideSub;
            Control<CheckBox>("NaryHideSupCheckBox")!.IsChecked = nary.HideSup;
        }

        if (_selectedNode.Element is MathMatrix || _selectedNode.Kind == MathNodeKind.MatrixRow || _selectedNode.Kind == MathNodeKind.MatrixCell)
        {
            matrixPanel.IsVisible = true;
        }

        _suppressUpdates = false;
    }

    private void InsertElement(MathElement element)
    {
        if (_equation is null)
        {
            return;
        }

        if (_selectedNode is null)
        {
            _equation.Root = element;
            OnEquationEdited();
            RefreshTree();
            return;
        }

        if (_selectedNode.Kind == MathNodeKind.MatrixRow && _selectedNode.Matrix is not null)
        {
            _selectedNode.Matrix.Rows[_selectedNode.RowIndex].Add(element);
            OnEquationEdited();
            RefreshTree();
            return;
        }

        if (_selectedNode.Kind == MathNodeKind.MatrixCell && _selectedNode.Matrix is not null)
        {
            _selectedNode.Matrix.Rows[_selectedNode.RowIndex][_selectedNode.ColumnIndex] = element;
            OnEquationEdited();
            RefreshTree();
            return;
        }

        if (_selectedNode.Element is MathRow row)
        {
            row.Elements.Add(element);
            OnEquationEdited();
            RefreshTree();
            return;
        }

        if (_selectedNode.Element is MathMatrix matrix)
        {
            matrix.Rows.Add(new List<MathElement> { element });
            OnEquationEdited();
            RefreshTree();
            return;
        }

        ReplaceSelectedNode(element);
        OnEquationEdited();
        RefreshTree();
    }

    private void ReplaceSelectedNode(MathElement element)
    {
        if (_equation is null || _selectedNode is null)
        {
            return;
        }

        if (_selectedNode.Kind == MathNodeKind.MatrixCell && _selectedNode.Matrix is not null)
        {
            _selectedNode.Matrix.Rows[_selectedNode.RowIndex][_selectedNode.ColumnIndex] = element;
            return;
        }

        if (_selectedNode.ParentElement is null)
        {
            _equation.Root = element;
            return;
        }

        TryReplaceChild(_selectedNode.ParentElement, _selectedNode.Element!, element);
    }

    private void RemoveSelectedNode()
    {
        if (_equation is null || _selectedNode is null)
        {
            return;
        }

        if (_selectedNode.Kind == MathNodeKind.MatrixRow && _selectedNode.Matrix is not null)
        {
            if (_selectedNode.Matrix.Rows.Count > 1)
            {
                _selectedNode.Matrix.Rows.RemoveAt(_selectedNode.RowIndex);
            }
            else
            {
                _selectedNode.Matrix.Rows[0] = new List<MathElement> { new MathRun() };
            }

            OnEquationEdited();
            RefreshTree();
            return;
        }

        if (_selectedNode.Kind == MathNodeKind.MatrixCell && _selectedNode.Matrix is not null)
        {
            _selectedNode.Matrix.Rows[_selectedNode.RowIndex][_selectedNode.ColumnIndex] = new MathRun();
            OnEquationEdited();
            RefreshTree();
            return;
        }

        if (_selectedNode.ParentElement is null)
        {
            _equation.Root = new MathRun();
            OnEquationEdited();
            RefreshTree();
            return;
        }

        if (_selectedNode.ParentElement is MathRow row)
        {
            row.Elements.Remove(_selectedNode.Element!);
            if (row.Elements.Count == 0)
            {
                row.Elements.Add(new MathRun());
            }
        }
        else if (_selectedNode.ParentElement is MathScript script)
        {
            if (ReferenceEquals(script.Base, _selectedNode.Element))
            {
                script.Base = new MathRun();
            }
            else if (ReferenceEquals(script.Subscript, _selectedNode.Element))
            {
                script.Subscript = null;
            }
            else if (ReferenceEquals(script.Superscript, _selectedNode.Element))
            {
                script.Superscript = null;
            }
        }
        else if (_selectedNode.ParentElement is MathNary nary)
        {
            if (ReferenceEquals(nary.Base, _selectedNode.Element))
            {
                nary.Base = new MathRun();
            }
            else if (ReferenceEquals(nary.Subscript, _selectedNode.Element))
            {
                nary.Subscript = null;
            }
            else if (ReferenceEquals(nary.Superscript, _selectedNode.Element))
            {
                nary.Superscript = null;
            }
        }
        else
        {
            TryReplaceChild(_selectedNode.ParentElement, _selectedNode.Element!, new MathRun());
        }

        OnEquationEdited();
        RefreshTree();
    }

    private void AddMatrixRow()
    {
        var matrix = GetSelectedMatrix();
        if (matrix is null)
        {
            return;
        }

        var columnCount = matrix.Rows.Count > 0 ? matrix.Rows.Max(row => row.Count) : 0;
        if (columnCount == 0)
        {
            matrix.Rows.Add(new List<MathElement> { new MathRun() });
        }
        else
        {
            var row = new List<MathElement>();
            for (var i = 0; i < columnCount; i++)
            {
                row.Add(new MathRun());
            }

            matrix.Rows.Add(row);
        }

        OnEquationEdited();
        RefreshTree();
    }

    private void AddMatrixColumn()
    {
        var matrix = GetSelectedMatrix();
        if (matrix is null)
        {
            return;
        }

        if (matrix.Rows.Count == 0)
        {
            matrix.Rows.Add(new List<MathElement> { new MathRun() });
        }
        else
        {
            foreach (var row in matrix.Rows)
            {
                row.Add(new MathRun());
            }
        }

        OnEquationEdited();
        RefreshTree();
    }

    private MathMatrix? GetSelectedMatrix()
    {
        if (_selectedNode?.Element is MathMatrix matrix)
        {
            return matrix;
        }

        return _selectedNode?.Matrix;
    }

    private void OnRunTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressUpdates || _selectedNode?.Element is not MathRun run)
        {
            return;
        }

        run.Text = Control<TextBox>("RunTextBox")!.Text ?? string.Empty;
        OnEquationEdited();
        RefreshTree();
    }

    private void OnAccentTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressUpdates || _selectedNode?.Element is not MathAccent accent)
        {
            return;
        }

        accent.AccentChar = Control<TextBox>("AccentTextBox")!.Text ?? string.Empty;
        OnEquationEdited();
        RefreshTree();
    }

    private void OnDelimiterChanged(object? sender, EventArgs e)
    {
        if (_suppressUpdates || _selectedNode?.Element is not MathDelimiter delimiter)
        {
            return;
        }

        delimiter.BeginChar = Control<TextBox>("DelimiterBeginTextBox")!.Text;
        delimiter.EndChar = Control<TextBox>("DelimiterEndTextBox")!.Text;
        delimiter.SeparatorChar = Control<TextBox>("DelimiterSeparatorTextBox")!.Text;
        OnEquationEdited();
        RefreshTree();
    }

    private void OnNaryChanged(object? sender, EventArgs e)
    {
        if (_suppressUpdates || _selectedNode?.Element is not MathNary nary)
        {
            return;
        }

        nary.OperatorChar = Control<TextBox>("NaryOperatorTextBox")!.Text ?? string.Empty;
        nary.HideSub = Control<CheckBox>("NaryHideSubCheckBox")!.IsChecked == true;
        nary.HideSup = Control<CheckBox>("NaryHideSupCheckBox")!.IsChecked == true;
        OnEquationEdited();
        RefreshTree();
    }

    private void OnEquationEdited()
    {
        EquationEdited?.Invoke(this, EventArgs.Empty);
    }

    private void SetEditingEnabled(bool enabled)
    {
        Control<Button>("AddRunButton")!.IsEnabled = enabled;
        Control<Button>("AddRowButton")!.IsEnabled = enabled;
        Control<Button>("AddFractionButton")!.IsEnabled = enabled;
        Control<Button>("AddScriptButton")!.IsEnabled = enabled;
        Control<Button>("AddRadicalButton")!.IsEnabled = enabled;
        Control<Button>("AddAccentButton")!.IsEnabled = enabled;
        Control<Button>("AddDelimiterButton")!.IsEnabled = enabled;
        Control<Button>("AddNaryButton")!.IsEnabled = enabled;
        Control<Button>("AddMatrixButton")!.IsEnabled = enabled;
        Control<Button>("RemoveNodeButton")!.IsEnabled = enabled;
        Control<Button>("AddMatrixRowButton")!.IsEnabled = enabled;
        Control<Button>("AddMatrixColumnButton")!.IsEnabled = enabled;
    }

    private TreeViewItem BuildElementNode(MathElement element, MathElement? parent, string? role)
    {
        if (element is MathMatrix matrix)
        {
            return BuildMatrixNode(matrix, parent, role);
        }

        var label = BuildLabel(element, role);
        var node = MathNode.ForElement(element, parent, label);
        var item = new TreeViewItem { Header = label, Tag = node };
        _elementMap[element] = item;
        AppendElementChildren(item, element);

        return item;
    }

    private TreeViewItem BuildMatrixNode(MathMatrix matrix, MathElement? parent, string? role)
    {
        var label = BuildLabel(matrix, role);
        var node = MathNode.ForElement(matrix, parent, label);
        var item = new TreeViewItem { Header = label, Tag = node };
        _elementMap[matrix] = item;

        for (var r = 0; r < matrix.Rows.Count; r++)
        {
            var rowNode = MathNode.ForMatrixRow(matrix, r, $"Row {r + 1}");
            var rowItem = new TreeViewItem { Header = rowNode.Label, Tag = rowNode };
            var row = matrix.Rows[r];
            for (var c = 0; c < row.Count; c++)
            {
                var cell = row[c];
                var cellLabel = $"Cell {r + 1},{c + 1}: {DescribeElement(cell)}";
                var cellNode = MathNode.ForMatrixCell(matrix, r, c, cell, cellLabel);
                var cellItem = new TreeViewItem { Header = cellLabel, Tag = cellNode };
                _elementMap[cell] = cellItem;
                AppendElementChildren(cellItem, cell);
                rowItem.Items.Add(cellItem);
            }

            item.Items.Add(rowItem);
        }

        return item;
    }

    private void AppendElementChildren(TreeViewItem item, MathElement element)
    {
        switch (element)
        {
            case MathRow row:
                foreach (var child in row.Elements)
                {
                    item.Items.Add(BuildElementNode(child, element, null));
                }

                break;
            case MathFraction fraction:
                item.Items.Add(BuildElementNode(fraction.Numerator, element, "Numerator"));
                item.Items.Add(BuildElementNode(fraction.Denominator, element, "Denominator"));
                break;
            case MathScript script:
                item.Items.Add(BuildElementNode(script.Base, element, "Base"));
                if (script.Subscript is not null)
                {
                    item.Items.Add(BuildElementNode(script.Subscript, element, "Subscript"));
                }

                if (script.Superscript is not null)
                {
                    item.Items.Add(BuildElementNode(script.Superscript, element, "Superscript"));
                }

                break;
            case MathRadical radical:
                item.Items.Add(BuildElementNode(radical.Radicand, element, "Radicand"));
                if (radical.Degree is not null)
                {
                    item.Items.Add(BuildElementNode(radical.Degree, element, "Degree"));
                }

                break;
            case MathAccent accent:
                item.Items.Add(BuildElementNode(accent.Base, element, "Base"));
                break;
            case MathDelimiter delimiter:
                item.Items.Add(BuildElementNode(delimiter.Body, element, "Body"));
                break;
            case MathNary nary:
                item.Items.Add(BuildElementNode(nary.Base, element, "Base"));
                if (nary.Subscript is not null)
                {
                    item.Items.Add(BuildElementNode(nary.Subscript, element, "Subscript"));
                }

                if (nary.Superscript is not null)
                {
                    item.Items.Add(BuildElementNode(nary.Superscript, element, "Superscript"));
                }

                break;
        }
    }

    private static string BuildLabel(MathElement element, string? role)
    {
        var label = DescribeElement(element);
        return string.IsNullOrWhiteSpace(role) ? label : $"{role}: {label}";
    }

    private static string DescribeElement(MathElement element)
    {
        return element switch
        {
            MathRun run => $"Run \"{TrimText(run.Text)}\"",
            MathRow => "Row",
            MathFraction => "Fraction",
            MathScript => "Script",
            MathRadical => "Radical",
            MathAccent => "Accent",
            MathDelimiter => "Delimiter",
            MathNary => "N-ary",
            MathMatrix => "Matrix",
            _ => element.GetType().Name
        };
    }

    private static string TrimText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= 12 ? text : $"{text.Substring(0, 12)}...";
    }

    private static bool TryReplaceChild(MathElement parent, MathElement oldChild, MathElement replacement)
    {
        switch (parent)
        {
            case MathRow row:
            {
                var index = row.Elements.IndexOf(oldChild);
                if (index >= 0)
                {
                    row.Elements[index] = replacement;
                    return true;
                }

                break;
            }
            case MathFraction fraction:
                if (ReferenceEquals(fraction.Numerator, oldChild))
                {
                    fraction.Numerator = replacement;
                    return true;
                }

                if (ReferenceEquals(fraction.Denominator, oldChild))
                {
                    fraction.Denominator = replacement;
                    return true;
                }

                break;
            case MathScript script:
                if (ReferenceEquals(script.Base, oldChild))
                {
                    script.Base = replacement;
                    return true;
                }

                if (ReferenceEquals(script.Subscript, oldChild))
                {
                    script.Subscript = replacement;
                    return true;
                }

                if (ReferenceEquals(script.Superscript, oldChild))
                {
                    script.Superscript = replacement;
                    return true;
                }

                break;
            case MathRadical radical:
                if (ReferenceEquals(radical.Radicand, oldChild))
                {
                    radical.Radicand = replacement;
                    return true;
                }

                if (ReferenceEquals(radical.Degree, oldChild))
                {
                    radical.Degree = replacement;
                    return true;
                }

                break;
            case MathAccent accent:
                if (ReferenceEquals(accent.Base, oldChild))
                {
                    accent.Base = replacement;
                    return true;
                }

                break;
            case MathDelimiter delimiter:
                if (ReferenceEquals(delimiter.Body, oldChild))
                {
                    delimiter.Body = replacement;
                    return true;
                }

                break;
            case MathNary nary:
                if (ReferenceEquals(nary.Base, oldChild))
                {
                    nary.Base = replacement;
                    return true;
                }

                if (ReferenceEquals(nary.Subscript, oldChild))
                {
                    nary.Subscript = replacement;
                    return true;
                }

                if (ReferenceEquals(nary.Superscript, oldChild))
                {
                    nary.Superscript = replacement;
                    return true;
                }

                break;
        }

        return false;
    }

    private enum MathNodeKind
    {
        Element,
        MatrixRow,
        MatrixCell
    }

    private sealed class MathNode
    {
        public MathNodeKind Kind { get; }
        public MathElement? Element { get; }
        public MathElement? ParentElement { get; }
        public MathMatrix? Matrix { get; }
        public int RowIndex { get; }
        public int ColumnIndex { get; }
        public string Label { get; }

        private MathNode(MathNodeKind kind, MathElement? element, MathElement? parentElement, MathMatrix? matrix, int rowIndex, int columnIndex, string label)
        {
            Kind = kind;
            Element = element;
            ParentElement = parentElement;
            Matrix = matrix;
            RowIndex = rowIndex;
            ColumnIndex = columnIndex;
            Label = label;
        }

        public static MathNode ForElement(MathElement element, MathElement? parent, string label)
        {
            return new MathNode(MathNodeKind.Element, element, parent, null, -1, -1, label);
        }

        public static MathNode ForMatrixRow(MathMatrix matrix, int rowIndex, string label)
        {
            return new MathNode(MathNodeKind.MatrixRow, null, null, matrix, rowIndex, -1, label);
        }

        public static MathNode ForMatrixCell(MathMatrix matrix, int rowIndex, int columnIndex, MathElement element, string label)
        {
            return new MathNode(MathNodeKind.MatrixCell, element, null, matrix, rowIndex, columnIndex, label);
        }
    }
}
