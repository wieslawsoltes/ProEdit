using ProEdit.Editing;
using ProEdit.Primitives;

namespace ProEdit.Word.Avalonia;

public sealed class DrawToolService : IDrawToolService
{
    public EditorDrawTool ActiveTool { get; set; } = EditorDrawTool.Select;
    public DocColor PenColor { get; set; } = DocColor.Black;
    public float PenThickness { get; set; } = 2f;
    public DocColor PencilColor { get; set; } = new DocColor(60, 60, 60, 200);
    public float PencilThickness { get; set; } = 1.5f;
    public DocColor HighlighterColor { get; set; } = new DocColor(255, 239, 0, 128);
    public float HighlighterThickness { get; set; } = 8f;
}
