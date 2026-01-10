using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Primitives;
using Vibe.Office.Rendering;
using Vibe.Office.Rendering.Skia;
using Vibe.Word.Editor;
using Vibe.Word.Editor.Editing;
using RenderOptions = Vibe.Office.Rendering.RenderOptions;

namespace Vibe.Word.App;

public sealed class DocumentView : Control, ILogicalScrollable
{
    public static readonly StyledProperty<Color> SurfaceColorProperty =
        AvaloniaProperty.Register<DocumentView, Color>(nameof(SurfaceColor), new Color(255, 238, 241, 245));

    private readonly EditorKernel _kernel = new EditorKernel(new LegacyEditorSessionFactory());
    private EditorController _editor;
    private AvaloniaEditorInputAdapter _inputAdapter = null!;
    private readonly SkiaTextMeasurer _textMeasurer = new SkiaTextMeasurer();
    private readonly SkiaDocumentRenderer _renderer = new SkiaDocumentRenderer();
    private SkiaDocumentFontResolver? _fontResolver;
    private readonly RenderOptions _renderOptions = new RenderOptions
    {
        BackgroundColor = new DocColor(238, 241, 245),
        PageColor = DocColor.White,
        PageBorderColor = new DocColor(220, 220, 220),
        PageBorderThickness = 1f,
        TextColor = DocColor.Black,
        SelectionColor = DocColor.SelectionBlue,
        CaretColor = DocColor.Black,
        CaretThickness = 1.5f,
        UseHarfBuzz = true,
        UsePictureCache = true
    };

    private bool _isSelecting;
    private bool _isLoading;
    private Vector _scrollOffset;
    private Size _extent;
    private Size _viewport;
    private EquationInline? _selectedEquation;
    private long _renderVersion;

    public DocumentView()
    {
        Focusable = true;
        _kernel.AddModule(new BasicEditingModule());
        _editor = CreateEditor(CreateSampleDocument());
        ApplyEditorState();
        UpdateSurfaceColor(SurfaceColor);
    }

    public Color SurfaceColor
    {
        get => GetValue(SurfaceColorProperty);
        set => SetValue(SurfaceColorProperty, value);
    }

    public Document Document => _editor.Document;

    public EquationInline? SelectedEquation => _selectedEquation;

    public event EventHandler<EquationInline?>? SelectedEquationChanged;
    public event EventHandler? EditorStateChanged;

    public bool ShowInvisibles
    {
        get => _renderOptions.ShowInvisibles;
        set
        {
            if (_renderOptions.ShowInvisibles == value)
            {
                return;
            }

            _renderOptions.ShowInvisibles = value;
            InvalidateAllPages();
        }
    }

    public bool ShowLayout
    {
        get => _renderOptions.ShowLayout;
        set
        {
            if (_renderOptions.ShowLayout == value)
            {
                return;
            }

            _renderOptions.ShowLayout = value;
            InvalidateAllPages();
        }
    }

    public bool UseHarfBuzz
    {
        get => _renderOptions.UseHarfBuzz;
        set
        {
            if (_renderOptions.UseHarfBuzz == value)
            {
                return;
            }

            _renderOptions.UseHarfBuzz = value;
            _textMeasurer.UseHarfBuzz = value;
            _editor.RefreshLayout();
            UpdateScrollMetrics();
            InvalidateVisual();
        }
    }

    public bool UsePictureCache
    {
        get => _renderOptions.UsePictureCache;
        set
        {
            if (_renderOptions.UsePictureCache == value)
            {
                return;
            }

            _renderOptions.UsePictureCache = value;
            InvalidateVisual();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SurfaceColorProperty)
        {
            UpdateSurfaceColor(change.GetNewValue<Color>());
        }
    }

    private void UpdateSurfaceColor(Color color)
    {
        _renderOptions.BackgroundColor = new DocColor(color.R, color.G, color.B);
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_isLoading)
        {
            return;
        }

        if (Bounds.Width > 0 && Bounds.Height > 0)
        {
            _editor.UpdateLayout((float)Bounds.Width, (float)Bounds.Height);
            UpdateScrollMetrics();
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (_isLoading)
        {
            return;
        }

        if (_inputAdapter.HandleTextInput(e))
        {
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_isLoading)
        {
            return;
        }

        if (_inputAdapter.HandleKeyDown(e))
        {
            e.Handled = true;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_isLoading)
        {
            return;
        }
        Focus();

        if (_inputAdapter.HandlePointerPressed(e, _scrollOffset, this))
        {
            _isSelecting = true;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_isLoading)
        {
            return;
        }
        if (!_isSelecting)
        {
            return;
        }

        var current = e.GetCurrentPoint(this);
        if (!current.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (_inputAdapter.HandlePointerMoved(e, _scrollOffset, this))
        {
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isLoading)
        {
            return;
        }

        if (_isSelecting)
        {
            _isSelecting = false;
            _inputAdapter.HandlePointerReleased(e, _scrollOffset, this);
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.Custom(new SkiaDrawOperation(Bounds, _editor, _renderer, _renderOptions, _scrollOffset));
    }

    public void LoadDocument(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _editor.Changed -= OnEditorChanged;
        _editor = CreateEditor(document);
        _editor.UpdateLayout((float)Bounds.Width, (float)Bounds.Height);
        ApplyEditorState();
    }

    public async Task LoadDocumentAsync(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        _editor.Changed -= OnEditorChanged;
        ConfigureMeasurer(document);

        var width = MathF.Max(1f, (float)Bounds.Width);
        var height = MathF.Max(1f, (float)Bounds.Height);

        var editor = await Task.Run(() =>
        {
            var newEditor = new EditorController(_textMeasurer, document);
            newEditor.UpdateLayout(width, height);
            return newEditor;
        }).ConfigureAwait(true);

        _editor = editor;
        EditorHomeServiceRegistry.Register(
            _kernel.Services,
            _kernel.Commands,
            _editor,
            CreateFontService(_editor),
            CreateClipboardService(_editor),
            CreateViewOptionsService());
        ConfigureInputPipeline(editor);
        ApplyEditorState();
    }

    public void RefreshLayout()
    {
        _editor.RefreshLayout();
    }

    public void SetLoading(bool isLoading)
    {
        _isLoading = isLoading;
        if (isLoading)
        {
            _isSelecting = false;
        }
    }

    private static Document CreateSampleDocument()
    {
        var document = new Document();
        document.Blocks.Clear();
        DefineSampleStyles(document);
        var richParagraph = new ParagraphBlock("Vibe Word MVP - type to edit. This is the first paragraph.");
        richParagraph.Inlines.Add(new RunInline("Vibe Word ", new TextStyleProperties { FontWeight = DocFontWeight.Bold }));
        richParagraph.Inlines.Add(new RunInline("MVP", new TextStyleProperties { FontStyle = DocFontStyle.Italic, Color = new DocColor(0, 102, 204) }));
        richParagraph.Inlines.Add(new RunInline(" - type to edit. This is the first paragraph."));
        document.Blocks.Add(richParagraph);
        document.Blocks.Add(new ParagraphBlock("This editor uses Avalonia UI for the view and SkiaSharp for layout and rendering."));
        document.Blocks.Add(new ParagraphBlock("Arrow keys move the caret. Shift + arrows extend selection. Backspace and delete are supported."));
        document.Blocks.Add(new ParagraphBlock("Bullet item one", new ListInfo(ListKind.Bullet)));
        document.Blocks.Add(new ParagraphBlock("Bullet item two", new ListInfo(ListKind.Bullet)));
        document.Blocks.Add(new ParagraphBlock("Numbered item one", new ListInfo(ListKind.Numbered)));

        var table = new TableBlock
        {
            Rows =
            {
                new TableRow(new[]
                {
                    new TableCell(new[] { new ParagraphBlock("Table cell A1") }),
                    new TableCell(new[] { new ParagraphBlock("Table cell B1") })
                }),
                new TableRow(new[]
                {
                    new TableCell(new[] { new ParagraphBlock("Table cell A2") }),
                    new TableCell(new[] { new ParagraphBlock("Table cell B2") })
                })
            }
        };
        document.Blocks.Add(table);
        return document;
    }

    private static void DefineSampleStyles(Document document)
    {
        var styles = document.Styles;
        styles.ParagraphStyles.Clear();

        void AddStyle(ParagraphStyleDefinition style)
        {
            styles.ParagraphStyles[style.Id] = style;
        }

        styles.DefaultParagraphStyleId = "Normal";

        var normal = new ParagraphStyleDefinition("Normal")
        {
            Name = "Normal"
        };
        normal.RunProperties.FontFamily = "Aptos (Body)";
        normal.RunProperties.FontSize = 12f;
        normal.ParagraphProperties.LineSpacing = 276;
        normal.ParagraphProperties.LineSpacingRule = DocLineSpacingRule.Auto;
        normal.ParagraphProperties.SpacingAfter = 8f;
        AddStyle(normal);

        var noSpacing = new ParagraphStyleDefinition("NoSpacing")
        {
            Name = "No Spacing"
        };
        noSpacing.RunProperties.FontFamily = "Aptos (Body)";
        noSpacing.RunProperties.FontSize = 12f;
        noSpacing.ParagraphProperties.LineSpacing = 240;
        noSpacing.ParagraphProperties.LineSpacingRule = DocLineSpacingRule.Auto;
        noSpacing.ParagraphProperties.SpacingBefore = 0f;
        noSpacing.ParagraphProperties.SpacingAfter = 0f;
        AddStyle(noSpacing);

        var heading1 = new ParagraphStyleDefinition("Heading1")
        {
            Name = "Heading 1"
        };
        heading1.RunProperties.FontFamily = "Aptos Display";
        heading1.RunProperties.FontSize = 16f;
        heading1.RunProperties.FontWeight = DocFontWeight.Bold;
        heading1.RunProperties.Color = new DocColor(46, 85, 153);
        heading1.ParagraphProperties.SpacingBefore = 12f;
        heading1.ParagraphProperties.SpacingAfter = 4f;
        AddStyle(heading1);

        var heading2 = new ParagraphStyleDefinition("Heading2")
        {
            Name = "Heading 2"
        };
        heading2.RunProperties.FontFamily = "Aptos Display";
        heading2.RunProperties.FontSize = 13f;
        heading2.RunProperties.FontWeight = DocFontWeight.Bold;
        heading2.RunProperties.Color = new DocColor(79, 129, 189);
        heading2.ParagraphProperties.SpacingBefore = 10f;
        heading2.ParagraphProperties.SpacingAfter = 2f;
        AddStyle(heading2);

        var title = new ParagraphStyleDefinition("Title")
        {
            Name = "Title"
        };
        title.RunProperties.FontFamily = "Aptos Display";
        title.RunProperties.FontSize = 26f;
        title.RunProperties.FontWeight = DocFontWeight.Bold;
        title.RunProperties.Color = new DocColor(46, 85, 153);
        title.ParagraphProperties.SpacingBefore = 12f;
        title.ParagraphProperties.SpacingAfter = 8f;
        AddStyle(title);
    }

    private EditorController CreateEditor(Document document)
    {
        ConfigureMeasurer(document);
        var editor = new EditorController(_textMeasurer, document);
        EditorHomeServiceRegistry.Register(
            _kernel.Services,
            _kernel.Commands,
            editor,
            CreateFontService(editor),
            CreateClipboardService(editor),
            CreateViewOptionsService());
        ConfigureInputPipeline(editor);
        return editor;
    }

    private IFontService CreateFontService(IEditorSession session)
    {
        return new SkiaFontServiceAdapter(session);
    }

    private IClipboardService CreateClipboardService(IEditorSession session)
    {
        return new AvaloniaClipboardService(
            () => TopLevel.GetTopLevel(this)?.Clipboard,
            () => !session.Selection.IsEmpty,
            () => !session.Selection.IsEmpty);
    }

    private IEditorViewOptionsService CreateViewOptionsService()
    {
        return new EditorViewOptionsService(this);
    }

    private void ConfigureInputPipeline(EditorController editor)
    {
        _kernel.Services.TryGet<IUndoRedoService>(out var undoRedo);
        var commandRouter = new EditorCommandInputRouter(_kernel.Commands, editor, undoRedo);
        _inputAdapter = new AvaloniaEditorInputAdapter(commandRouter);
    }

    private void ConfigureMeasurer(Document document)
    {
        _textMeasurer.UseHarfBuzz = _renderOptions.UseHarfBuzz;
        _fontResolver?.Dispose();
        _fontResolver = new SkiaDocumentFontResolver(document.Fonts);
        _textMeasurer.TypefaceResolver = _fontResolver;
        _renderer.TypefaceResolver = _fontResolver;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _fontResolver?.Dispose();
        _fontResolver = null;
    }

    private void ApplyEditorState()
    {
        _editor.Changed += OnEditorChanged;
        _scrollOffset = default;
        UpdateDirtyPages(GetAllPages());
        UpdateScrollMetrics();
        UpdateSelectedEquation();
        InvalidateVisual();
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnEditorChanged(object? sender, EventArgs e)
    {
        UpdateScrollMetrics();
        UpdateDirtyPages(_editor.DirtyPages);
        UpdateSelectedEquation();
        InvalidateVisual();
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool TryGetService<T>(out T service) where T : class => _kernel.Services.TryGet(out service);

    public bool TryGetService(Type serviceType, out object? service) => _kernel.Services.TryGet(serviceType, out service);

    public void RegisterService<T>(T service) where T : class => _kernel.Services.Register(service);

    private void InvalidateAllPages()
    {
        UpdateDirtyPages(GetAllPages());
        InvalidateVisual();
    }

    private void UpdateDirtyPages(IReadOnlyList<int> pages)
    {
        _renderVersion++;
        _renderOptions.DirtyPages = pages;
        _renderOptions.DirtyVersion = _renderVersion;
    }

    private IReadOnlyList<int> GetAllPages()
    {
        var count = _editor.Layout.Pages.Count;
        if (count <= 0)
        {
            return Array.Empty<int>();
        }

        return Enumerable.Range(0, count).ToArray();
    }

    private void UpdateSelectedEquation()
    {
        var equation = _editor.GetEquationAtCaret();
        if (ReferenceEquals(equation, _selectedEquation))
        {
            return;
        }

        _selectedEquation = equation;
        SelectedEquationChanged?.Invoke(this, equation);
    }

    public bool CanHorizontallyScroll
    {
        get => _extent.Width > _viewport.Width;
        set { }
    }

    public bool CanVerticallyScroll
    {
        get => true;
        set { }
    }

    public bool IsLogicalScrollEnabled => true;

    public Size ScrollSize => new Size(0, _editor.Layout.LineHeight);
    public Size PageScrollSize => new Size(0, Math.Max(0, Bounds.Height));

    public Size Extent => _extent;
    public Size Viewport => _viewport;

    public Vector Offset
    {
        get => _scrollOffset;
        set
        {
            var clamped = ClampOffset(value);
            if (clamped == _scrollOffset)
            {
                return;
            }

            _scrollOffset = clamped;
            RaiseScrollInvalidated(EventArgs.Empty);
            InvalidateVisual();
        }
    }

    public event EventHandler? ScrollInvalidated;

    public bool BringIntoView(Control target, Rect targetRect)
    {
        if (target != this)
        {
            return false;
        }

        if (targetRect.Width <= 0 || targetRect.Height <= 0)
        {
            return false;
        }

        var viewportSize = Bounds.Size;
        if (targetRect.X <= 0.5
            && targetRect.Y <= 0.5
            && targetRect.Width >= viewportSize.Width - 0.5
            && targetRect.Height >= viewportSize.Height - 0.5)
        {
            return false;
        }

        var offsetY = _scrollOffset.Y;
        if (targetRect.Top < _scrollOffset.Y)
        {
            offsetY = targetRect.Top;
        }
        else if (targetRect.Bottom > _scrollOffset.Y + _viewport.Height)
        {
            offsetY = targetRect.Bottom - _viewport.Height;
        }

        Offset = new Vector(_scrollOffset.X, offsetY);
        return true;
    }

    public Control? GetControlInDirection(NavigationDirection direction, Control? from) => null;

    public void RaiseScrollInvalidated(EventArgs e) => ScrollInvalidated?.Invoke(this, e);

    private void UpdateScrollMetrics()
    {
        _viewport = Bounds.Size;
        var extentHeight = Math.Max(_viewport.Height, _editor.Layout.ContentHeight);
        var extentWidth = _viewport.Width;
        if (_editor.Layout.Pages.Count > 0)
        {
            var maxRight = _editor.Layout.Pages.Max(page => page.Bounds.Right);
            extentWidth = Math.Max(_viewport.Width, maxRight + _editor.Layout.Settings.PageGap);
        }

        _extent = new Size(extentWidth, extentHeight);
        _scrollOffset = ClampOffset(_scrollOffset);
        RaiseScrollInvalidated(EventArgs.Empty);
    }

    private Vector ClampOffset(Vector offset)
    {
        var maxX = Math.Max(0, _extent.Width - _viewport.Width);
        var maxY = Math.Max(0, _extent.Height - _viewport.Height);
        var clampedX = Math.Clamp(offset.X, 0, maxX);
        var clampedY = Math.Clamp(offset.Y, 0, maxY);
        return new Vector(clampedX, clampedY);
    }

    private sealed class SkiaDrawOperation : ICustomDrawOperation
    {
        private readonly Rect _bounds;
        private readonly EditorController _editor;
        private readonly SkiaDocumentRenderer _renderer;
        private readonly RenderOptions _options;
        private readonly Vector _offset;

        public SkiaDrawOperation(Rect bounds, EditorController editor, SkiaDocumentRenderer renderer, RenderOptions options, Vector offset)
        {
            _bounds = bounds;
            _editor = editor;
            _renderer = renderer;
            _options = options;
            _offset = offset;
        }

        public Rect Bounds => _bounds;

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) as ISkiaSharpApiLeaseFeature;
            if (leaseFeature is null)
            {
                return;
            }

            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;
            canvas.Save();
            canvas.Translate((float)_bounds.X, (float)_bounds.Y);
            canvas.Translate(-(float)_offset.X, -(float)_offset.Y);

            _options.Caret = _editor.Caret;
            _options.Selection = _editor.Selection.IsEmpty ? null : _editor.Selection;
            _options.SelectedFloatingObjectId = _editor.SelectedFloatingObjectId;

            _renderer.Render(canvas, _editor.Document, _editor.Layout, _options);

            canvas.Restore();
        }

        public bool HitTest(Point p) => _bounds.Contains(p);
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose()
        {
        }
    }
}
