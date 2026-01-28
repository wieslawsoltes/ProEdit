using Vibe.Office.Documents;
using Vibe.Office.Primitives;

namespace Vibe.Office.Rendering;

public sealed class RenderOptions
{
    public DocColor BackgroundColor { get; set; } = new DocColor(242, 242, 242);
    public DocColor PageColor { get; set; } = DocColor.White;
    public DocColor PageBorderColor { get; set; } = new DocColor(220, 220, 220);
    public float PageBorderThickness { get; set; } = 1f;
    public DocColor HeaderFooterOverlayColor { get; set; } = new DocColor(235, 235, 235, 160);
    public DocColor HeaderFooterBoundsColor { get; set; } = new DocColor(160, 160, 160);
    public float HeaderFooterBoundsThickness { get; set; } = 1f;
    public DocColor ColumnSeparatorColor { get; set; } = new DocColor(200, 200, 200);
    public float ColumnSeparatorThickness { get; set; } = 1f;
    public DocColor TextColor { get; set; } = DocColor.Black;
    public DocColor SelectionColor { get; set; } = DocColor.SelectionBlue;
    public DocColor FloatingSelectionColor { get; set; } = new DocColor(45, 125, 240, 180);
    public DocColor CaretColor { get; set; } = DocColor.Black;
    public DocColor CommentHighlightColor { get; set; } = new DocColor(255, 247, 205, 160);
    public DocColor PlaceholderFillColor { get; set; } = new DocColor(248, 248, 248);
    public DocColor PlaceholderStrokeColor { get; set; } = new DocColor(180, 180, 180);
    public DocColor PlaceholderTextColor { get; set; } = new DocColor(120, 120, 120);
    public float CaretThickness { get; set; } = 1.5f;
    public float ZoomFactor { get; set; } = 1f;
    public bool ShowInvisibles { get; set; }
    public DocColor InvisiblesColor { get; set; } = new DocColor(165, 165, 165);
    public bool ShowLayout { get; set; }
    public DocColor LayoutGuideColor { get; set; } = new DocColor(170, 170, 170, 170);
    public float LayoutGuideThickness { get; set; } = 1f;
    public bool ShowGridlines { get; set; }
    public DocColor GridlineColor { get; set; } = new DocColor(230, 230, 230);
    public float GridlineThickness { get; set; } = 1f;
    public float GridlineSpacing { get; set; } = 12f;
    public bool UseHarfBuzz { get; set; } = true;
    public bool UsePictureCache { get; set; } = true;
    public SvgRenderMode SvgRenderMode { get; set; } = SvgRenderMode.Auto;
    public float SvgRasterizationScale { get; set; } = 1f;
    public DocColor SvgRasterBackgroundColor { get; set; } = DocColor.Transparent;
    public ISvgRasterizer? SvgRasterizer { get; set; }

    public TextRange? Selection { get; set; }
    public IReadOnlyList<TextRange>? SelectionRanges { get; set; }
    public TextPosition Caret { get; set; }
    public bool ShowCaret { get; set; } = true;
    public Guid? SelectedFloatingObjectId { get; set; }
    public IReadOnlyList<Guid>? SelectedFloatingObjectIds { get; set; }
    public HeaderFooterEditMode HeaderFooterMode { get; set; }
    public TextRange? HeaderFooterSelection { get; set; }
    public TextPosition HeaderFooterCaret { get; set; }
    public bool ShowHeaderFooterCaret { get; set; }
    public IReadOnlyList<int>? DirtyPages { get; set; }
    public long DirtyVersion { get; set; }
    public DocRect? VisibleBounds { get; set; }
}
