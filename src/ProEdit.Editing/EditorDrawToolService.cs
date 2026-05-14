using ProEdit.Primitives;

namespace ProEdit.Editing;

public enum EditorDrawTool
{
    Select,
    LassoSelect,
    Pen,
    Pencil,
    Highlighter,
    Eraser
}

public interface IDrawToolService
{
    EditorDrawTool ActiveTool { get; set; }
    DocColor PenColor { get; set; }
    float PenThickness { get; set; }
    DocColor PencilColor { get; set; }
    float PencilThickness { get; set; }
    DocColor HighlighterColor { get; set; }
    float HighlighterThickness { get; set; }
}
