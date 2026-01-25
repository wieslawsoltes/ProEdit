namespace Vibe.Office.Editing;

public static class EditorReferencesCommandIds
{
    public static class TableOfContents
    {
        public const string Insert = "refs.toc.insert";
        public const string Update = "refs.toc.update";
        public const string AddText = "refs.toc.addText";
    }

    public static class Notes
    {
        public const string InsertFootnote = "refs.notes.footnote";
        public const string InsertEndnote = "refs.notes.endnote";
        public const string NextFootnote = "refs.notes.next";
        public const string ShowNotes = "refs.notes.show";
    }

    public static class Captions
    {
        public const string InsertCaption = "refs.captions.insert";
    }

    public static class Citations
    {
        public const string InsertCitation = "refs.citations.insert";
        public const string Bibliography = "refs.citations.bibliography";
        public const string ManageSources = "refs.citations.manage";
        public const string Style = "refs.citations.style";
    }

    public static class TableOfFigures
    {
        public const string Insert = "refs.tableOfFigures.insert";
        public const string Update = "refs.tableOfFigures.update";
    }

    public static class Index
    {
        public const string MarkEntry = "refs.index.mark";
        public const string InsertIndex = "refs.index.insert";
    }

    public static class TableOfAuthorities
    {
        public const string MarkCitation = "refs.authorities.mark";
        public const string InsertTable = "refs.authorities.insert";
    }

    public static class Fields
    {
        public const string UpdateCurrent = "refs.fields.updateCurrent";
        public const string UpdateAll = "refs.fields.updateAll";
        public const string UpdatePageNumbers = "refs.fields.updatePageNumbers";
        public const string Lock = "refs.fields.lock";
        public const string Unlock = "refs.fields.unlock";
    }
}
