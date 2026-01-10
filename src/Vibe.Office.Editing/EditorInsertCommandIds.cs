namespace Vibe.Office.Editing;

public static class EditorInsertCommandIds
{
    public static class Pages
    {
        public const string CoverPage = "insert.pages.cover";
        public const string BlankPage = "insert.pages.blank";
        public const string PageBreak = "insert.pages.break";
    }

    public static class Tables
    {
        public const string InsertTable = "insert.tables.table";
    }

    public static class Illustrations
    {
        public const string Pictures = "insert.illustrations.pictures";
        public const string Shapes = "insert.illustrations.shapes";
        public const string Icons = "insert.illustrations.icons";
        public const string Models3D = "insert.illustrations.models3d";
        public const string SmartArt = "insert.illustrations.smartart";
        public const string Chart = "insert.illustrations.chart";
        public const string Screenshot = "insert.illustrations.screenshot";
    }

    public static class Links
    {
        public const string Hyperlink = "insert.links.hyperlink";
        public const string Bookmark = "insert.links.bookmark";
        public const string CrossReference = "insert.links.crossReference";
    }

    public static class HeaderFooter
    {
        public const string Header = "insert.headerFooter.header";
        public const string Footer = "insert.headerFooter.footer";
        public const string PageNumber = "insert.headerFooter.pageNumber";
    }

    public static class Text
    {
        public const string TextBox = "insert.text.textBox";
        public const string QuickParts = "insert.text.quickParts";
        public const string WordArt = "insert.text.wordArt";
        public const string DropCap = "insert.text.dropCap";
        public const string SignatureLine = "insert.text.signatureLine";
        public const string DateTime = "insert.text.dateTime";
        public const string Object = "insert.text.object";
    }

    public static class Symbols
    {
        public const string Equation = "insert.symbols.equation";
        public const string Symbol = "insert.symbols.symbol";
    }
}
