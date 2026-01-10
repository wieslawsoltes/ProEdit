using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Layout;
using Vibe.Office.Primitives;
using Vibe.Office.Rendering;
using Vibe.Office.Rendering.Skia;
using Vibe.Word.Editor;
using Vibe.Word.Editor.Editing;
using RenderOptions = Vibe.Office.Rendering.RenderOptions;

namespace Vibe.Word.App;

public sealed class DocumentView : Control, ILogicalScrollable
{
    private const float MinZoom = 0.1f;
    private const float MaxZoom = 5f;
    private const float ZoomStep = 0.1f;

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
    private float _zoomFactor = 1f;
    private DocumentZoomMode _zoomMode = DocumentZoomMode.Custom;
    private EquationInline? _selectedEquation;
    private long _renderVersion;

    public DocumentView()
    {
        Focusable = true;
        _renderOptions.ZoomFactor = _zoomFactor;
        _renderOptions.SvgRasterizationScale = _zoomFactor;
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
    public DocumentLayout Layout => _editor.Layout;
    public TextPosition Caret => _editor.Caret;
    public float ZoomFactor => _zoomFactor;
    public DocumentZoomMode ZoomMode => _zoomMode;

    public EquationInline? SelectedEquation => _selectedEquation;

    public event EventHandler<EquationInline?>? SelectedEquationChanged;
    public event EventHandler? EditorStateChanged;
    public event EventHandler? ZoomChanged;

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
            if (_zoomMode != DocumentZoomMode.Custom)
            {
                ApplyZoomMode(_zoomMode, preserveCenter: false);
                return;
            }

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

        if (HandleZoomShortcut(e))
        {
            e.Handled = true;
            return;
        }

        if (_inputAdapter.HandleKeyDown(e))
        {
            e.Handled = true;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_isLoading)
        {
            return;
        }

        if (HasZoomModifier(e.KeyModifiers))
        {
            if (e.Delta.Y > 0)
            {
                ZoomIn();
            }
            else if (e.Delta.Y < 0)
            {
                ZoomOut();
            }

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

        var effectiveOffset = GetEffectiveScrollOffset();
        if (_inputAdapter.HandlePointerPressed(e, effectiveOffset, _zoomFactor, this))
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

        var effectiveOffset = GetEffectiveScrollOffset();
        if (_inputAdapter.HandlePointerMoved(e, effectiveOffset, _zoomFactor, this))
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
            var effectiveOffset = GetEffectiveScrollOffset();
            _inputAdapter.HandlePointerReleased(e, effectiveOffset, _zoomFactor, this);
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var effectiveOffset = GetEffectiveScrollOffset();
        context.Custom(new SkiaDrawOperation(Bounds, _editor, _renderer, _renderOptions, effectiveOffset));
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
        _kernel.Services.TryGet<IClipboardService>(out var clipboard);
        _kernel.Services.TryGet<ISelectionTextService>(out var selectionText);
        var commandRouter = new EditorCommandInputRouter(_kernel.Commands, editor, undoRedo, clipboard, selectionText);
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

    public Size ScrollSize => new Size(0, _editor.Layout.LineHeight * _zoomFactor);
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

    private void UpdateScrollMetrics(Vector? targetOffset = null)
    {
        _viewport = Bounds.Size;
        var extentHeight = Math.Max(_viewport.Height, _editor.Layout.ContentHeight * _zoomFactor);
        var extentWidth = _viewport.Width;
        if (TryGetContentHorizontalBounds(out var minX, out var maxX))
        {
            var contentWidth = Math.Max(0f, (maxX - minX) * _zoomFactor);
            extentWidth = Math.Max(_viewport.Width, contentWidth);
        }

        _extent = new Size(extentWidth, extentHeight);
        _scrollOffset = ClampOffset(targetOffset ?? _scrollOffset);
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

    private Vector GetEffectiveScrollOffset()
    {
        var alignmentOffset = GetHorizontalAlignmentOffset();
        if (MathF.Abs(alignmentOffset) < 0.5f)
        {
            return _scrollOffset;
        }

        return new Vector(_scrollOffset.X + alignmentOffset, _scrollOffset.Y);
    }

    private float GetHorizontalAlignmentOffset()
    {
        if (_viewport.Width <= 0 || _editor.Layout.Pages.Count == 0)
        {
            return 0f;
        }

        if (!TryGetContentHorizontalBounds(out var minX, out var maxX))
        {
            return 0f;
        }

        var contentWidth = (maxX - minX) * _zoomFactor;
        if (contentWidth <= 0f)
        {
            return 0f;
        }

        var viewportWidth = (float)_viewport.Width;
        var contentLeft = minX * _zoomFactor;
        if (contentWidth >= viewportWidth - 0.5f)
        {
            return contentLeft;
        }

        var centeredLeft = (viewportWidth - contentWidth) / 2f;
        return contentLeft - centeredLeft;
    }

    private bool TryGetContentHorizontalBounds(out float minX, out float maxX)
    {
        var pages = _editor.Layout.Pages;
        if (pages.Count == 0)
        {
            minX = 0f;
            maxX = 0f;
            return false;
        }

        minX = pages[0].Bounds.Left;
        maxX = pages[0].Bounds.Right;
        for (var i = 1; i < pages.Count; i++)
        {
            var bounds = pages[i].Bounds;
            if (bounds.Left < minX)
            {
                minX = bounds.Left;
            }

            if (bounds.Right > maxX)
            {
                maxX = bounds.Right;
            }
        }

        return true;
    }

    public void ZoomIn()
    {
        SetZoom(_zoomFactor + ZoomStep, DocumentZoomMode.Custom);
    }

    public void ZoomOut()
    {
        SetZoom(_zoomFactor - ZoomStep, DocumentZoomMode.Custom);
    }

    public void ZoomToDefault()
    {
        SetZoom(1f, DocumentZoomMode.Custom);
    }

    public void ZoomToPercent(float percent)
    {
        SetZoom(percent / 100f, DocumentZoomMode.Custom);
    }

    public void ZoomToPageWidth()
    {
        SetZoom(ComputePageWidthZoom(), DocumentZoomMode.PageWidth, preserveCenter: false);
    }

    public void ZoomToWholePage()
    {
        SetZoom(ComputeWholePageZoom(), DocumentZoomMode.WholePage, preserveCenter: false);
    }

    private void SetZoom(float value, DocumentZoomMode mode, bool preserveCenter = true)
    {
        var clamped = Math.Clamp(value, MinZoom, MaxZoom);
        if (MathF.Abs(_zoomFactor - clamped) < 0.001f && _zoomMode == mode)
        {
            return;
        }

        var previousZoom = _zoomFactor;
        var previousOffset = GetEffectiveScrollOffset();
        var centerDoc = preserveCenter && _viewport.Width > 0 && _viewport.Height > 0
            ? new Point((previousOffset.X + _viewport.Width / 2f) / previousZoom,
                (previousOffset.Y + _viewport.Height / 2f) / previousZoom)
            : new Point(0, 0);

        _zoomFactor = clamped;
        _zoomMode = mode;
        _renderOptions.ZoomFactor = _zoomFactor;
        _renderOptions.SvgRasterizationScale = _zoomFactor;

        if (preserveCenter)
        {
            var targetEffectiveOffset = new Vector(centerDoc.X * _zoomFactor - _viewport.Width / 2f,
                centerDoc.Y * _zoomFactor - _viewport.Height / 2f);

            if (TryGetContentHorizontalBounds(out var minX, out var maxX))
            {
                var contentWidth = (maxX - minX) * _zoomFactor;
                var viewportWidth = (float)_viewport.Width;
                if (contentWidth >= viewportWidth - 0.5f)
                {
                    targetEffectiveOffset = new Vector(minX * _zoomFactor, targetEffectiveOffset.Y);
                }
            }

            var alignmentOffset = GetHorizontalAlignmentOffset();
            var targetOffset = new Vector(targetEffectiveOffset.X - alignmentOffset, targetEffectiveOffset.Y);
            UpdateScrollMetrics(targetOffset);
        }
        else
        {
            UpdateScrollMetrics();
        }

        InvalidateVisual();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyZoomMode(DocumentZoomMode mode, bool preserveCenter)
    {
        switch (mode)
        {
            case DocumentZoomMode.PageWidth:
                SetZoom(ComputePageWidthZoom(), DocumentZoomMode.PageWidth, preserveCenter);
                break;
            case DocumentZoomMode.WholePage:
                SetZoom(ComputeWholePageZoom(), DocumentZoomMode.WholePage, preserveCenter);
                break;
            default:
                SetZoom(_zoomFactor, DocumentZoomMode.Custom, preserveCenter);
                break;
        }
    }

    private float ComputePageWidthZoom()
    {
        if (_viewport.Width <= 0 || _editor.Layout.Pages.Count == 0)
        {
            return _zoomFactor;
        }

        var pageWidth = _editor.Layout.Pages.Max(page => page.Bounds.Width);
        if (pageWidth <= 0f)
        {
            return _zoomFactor;
        }

        var gap = _editor.Layout.Settings.PageGap;
        var targetWidth = pageWidth + gap;
        var viewportWidth = (float)_viewport.Width;
        return viewportWidth / Math.Max(1f, targetWidth);
    }

    private float ComputeWholePageZoom()
    {
        if (_viewport.Width <= 0 || _viewport.Height <= 0 || _editor.Layout.Pages.Count == 0)
        {
            return _zoomFactor;
        }

        var firstPage = _editor.Layout.Pages[0];
        var width = Math.Max(1f, firstPage.Bounds.Width + _editor.Layout.Settings.PageGap);
        var height = Math.Max(1f, firstPage.Bounds.Height + _editor.Layout.Settings.PageGap);
        var viewportWidth = (float)_viewport.Width;
        var viewportHeight = (float)_viewport.Height;
        var zoomX = viewportWidth / width;
        var zoomY = viewportHeight / height;
        return Math.Min(zoomX, zoomY);
    }

    private static bool HasZoomModifier(KeyModifiers modifiers)
    {
        return modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);
    }

    private bool HandleZoomShortcut(KeyEventArgs e)
    {
        if (!HasZoomModifier(e.KeyModifiers))
        {
            return false;
        }

        switch (e.Key)
        {
            case Key.Add:
            case Key.OemPlus:
                ZoomIn();
                return true;
            case Key.Subtract:
            case Key.OemMinus:
                ZoomOut();
                return true;
            case Key.D0:
            case Key.NumPad0:
                ZoomToDefault();
                return true;
            default:
                return false;
        }
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
            canvas.Scale(_options.ZoomFactor);
            var offsetDocX = (float)(_offset.X / _options.ZoomFactor);
            var offsetDocY = (float)(_offset.Y / _options.ZoomFactor);
            canvas.Translate(-offsetDocX, -offsetDocY);

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

public enum DocumentZoomMode
{
    Custom,
    PageWidth,
    WholePage
}
