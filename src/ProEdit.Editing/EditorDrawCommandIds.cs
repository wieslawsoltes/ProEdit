namespace ProEdit.Editing;

public static class EditorDrawCommandIds
{
    public static class Tools
    {
        public const string Select = "draw.tools.select";
        public const string LassoSelect = "draw.tools.lasso";
        public const string Pen = "draw.tools.pen";
        public const string Pencil = "draw.tools.pencil";
        public const string Highlighter = "draw.tools.highlighter";
        public const string Eraser = "draw.tools.eraser";
    }

    public static class Convert
    {
        public const string InkToShape = "draw.convert.inkToShape";
        public const string InkToMath = "draw.convert.inkToMath";
        public const string InkReplay = "draw.convert.inkReplay";
    }

    public static class AddPen
    {
        public const string Add = "draw.addPen";
    }
}
