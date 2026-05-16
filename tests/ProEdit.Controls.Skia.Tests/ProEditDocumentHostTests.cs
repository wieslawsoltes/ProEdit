using ProEdit.Controls.Skia;
using ProEdit.Documents;
using ProEdit.Editing;
using ProEdit.Layout;
using Xunit;

namespace ProEdit.Controls.Skia.Tests;

public sealed class ProEditDocumentHostTests
{
    [Fact]
    public void LoadDocumentUpdatesDocumentAndExtent()
    {
        var host = new ProEditDocumentHost();
        var document = CreateDocument("Hello");

        host.LoadDocument(document);
        host.SetViewport(400f, 300f);

        Assert.Same(document, host.Document);
        Assert.True(host.Extent.Width > 0f);
        Assert.True(host.Extent.Height > 0f);
    }

    [Fact]
    public void LoadDocumentPreservesViewLayoutOptions()
    {
        var host = new ProEditDocumentHost
        {
            UsePagination = false,
            PageFlow = PageFlowDirection.Horizontal
        };

        host.LoadDocument(CreateDocument("Replacement"));

        Assert.False(host.UsePagination);
        Assert.Equal(PageFlowDirection.Horizontal, host.PageFlow);
    }

    [Fact]
    public void TextInputMutatesDocumentWhenEditable()
    {
        var host = new ProEditDocumentHost
        {
            IsReadOnly = false
        };

        var handled = host.HandleTextInput("abc".AsSpan(), EditorModifiers.None);

        Assert.True(handled);
        Assert.Equal("abc", DocumentEditHelpers.GetParagraphText(host.Document.GetParagraph(0)));
    }

    [Fact]
    public void TextInputIsIgnoredWhenReadOnly()
    {
        var host = new ProEditDocumentHost
        {
            IsReadOnly = true
        };

        var handled = host.HandleTextInput("abc".AsSpan(), EditorModifiers.None);

        Assert.False(handled);
        Assert.Equal(string.Empty, DocumentEditHelpers.GetParagraphText(host.Document.GetParagraph(0)));
    }

    [Fact]
    public void DirectMutationHelpersAreIgnoredWhenReadOnly()
    {
        var host = new ProEditDocumentHost
        {
            IsReadOnly = true
        };
        host.LoadDocument(CreateDocument("abc"));

        host.InsertText("x");
        host.Backspace();
        host.DeleteForward();

        Assert.Equal("abc", DocumentEditHelpers.GetParagraphText(host.Document.GetParagraph(0)));
    }

    [Fact]
    public void DirectMutationHelpersEditWhenWritable()
    {
        var host = new ProEditDocumentHost
        {
            IsReadOnly = false
        };

        host.InsertText("abc");
        host.Backspace();
        host.HandleKey(EditorKey.Left, EditorKeyEventKind.Down, EditorModifiers.None);
        host.DeleteForward();

        Assert.Equal("a", DocumentEditHelpers.GetParagraphText(host.Document.GetParagraph(0)));
    }

    [Fact]
    public void ScrollIsCoercedToDocumentExtent()
    {
        var host = new ProEditDocumentHost();

        host.SetViewport(800f, 600f);
        host.SetScroll(float.MaxValue, float.MaxValue);

        Assert.InRange(host.ScrollX, 0f, MathF.Max(0f, host.Extent.Width));
        Assert.InRange(host.ScrollY, 0f, MathF.Max(0f, host.Extent.Height));
    }

    [Fact]
    public void HostRegistersWordEditorServices()
    {
        using var host = new ProEditDocumentHost();

        Assert.True(host.TryGetService<IUndoRedoService>(out _));
        Assert.True(host.TryGetService<IProofingService>(out _));
        Assert.True(host.TryGetService<IEditorViewOptionsService>(out var viewOptions));
        Assert.True(host.TryGetService<IEditorZoomService>(out var zoomService));
        Assert.True(host.TryGetService<IEditorCommandRouter>(out var commandRouter));
        Assert.Same(host, viewOptions);
        Assert.Same(host, zoomService);
        Assert.Same(host.CommandRouter, commandRouter);
    }

    [Fact]
    public void ZoomModesUseSharedHostState()
    {
        using var host = new ProEditDocumentHost();
        host.SetViewport(400f, 300f);

        host.ZoomToPageWidth();

        Assert.Equal(ProEditDocumentZoomMode.PageWidth, host.ZoomMode);
        Assert.InRange(host.Zoom, ProEditDocumentHost.MinimumZoom, ProEditDocumentHost.MaximumZoom);
    }

    [Fact]
    public void WheelWithCommandModifierZoomsThroughSharedPolicy()
    {
        using var host = new ProEditDocumentHost();
        host.SetViewport(400f, 300f);
        var initialZoom = host.Zoom;

        var handled = host.HandleWheel(0f, 48f, 100f, 100f, EditorModifiers.Control);

        Assert.True(handled);
        Assert.True(host.Zoom > initialZoom);
        Assert.Equal(ProEditDocumentZoomMode.Custom, host.ZoomMode);
    }

    [Fact]
    public void ScreenScrollAndPanUseZoomAwareOffsets()
    {
        using var host = new ProEditDocumentHost();
        host.SetViewport(200f, 200f);
        host.SetZoom(2f);
        host.SetScreenScroll(100f, 80f);

        Assert.InRange(host.ScrollX, 49.9f, 50.1f);
        Assert.InRange(host.ScrollY, 39.9f, 40.1f);

        Assert.True(host.BeginPan(20f, 20f));
        Assert.True(host.UpdatePan(0f, 0f));
        host.EndPan();

        Assert.True(host.ScrollX > 50f);
        Assert.True(host.ScrollY > 40f);
    }

    [Fact]
    public void ReadModeAppliesWordEditorViewOptions()
    {
        using var host = new ProEditDocumentHost();
        host.SetViewport(400f, 300f);

        host.ViewMode = EditorViewMode.ReadMode;

        Assert.True(host.UsePagination);
        Assert.True(host.ShowLayout);
        Assert.Equal(PageFlowDirection.Horizontal, host.PageFlow);
        Assert.Equal(ProEditDocumentZoomMode.WholePage, host.ZoomMode);
    }

    [Fact]
    public void ViewOptionChangesRaiseViewOptionsEvent()
    {
        using var host = new ProEditDocumentHost();
        var eventCount = 0;
        host.ViewOptionsChanged += (_, _) => eventCount++;

        host.ShowInvisibles = true;
        host.ShowLayout = true;
        host.ShowGridlines = true;

        Assert.Equal(3, eventCount);
    }

    private static Document CreateDocument(string text)
    {
        var document = new Document();
        document.Blocks.Clear();
        document.Blocks.Add(new ParagraphBlock(text));
        return document;
    }
}
