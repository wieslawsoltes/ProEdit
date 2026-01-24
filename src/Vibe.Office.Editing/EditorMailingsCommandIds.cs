namespace Vibe.Office.Editing;

public static class EditorMailingsCommandIds
{
    public static class Create
    {
        public const string Envelopes = "mailings.create.envelopes";
        public const string Labels = "mailings.create.labels";
    }

    public static class StartMailMerge
    {
        public const string Start = "mailings.start.start";
        public const string SelectRecipients = "mailings.start.selectRecipients";
        public const string EditRecipients = "mailings.start.editRecipients";
    }

    public static class WriteInsert
    {
        public const string HighlightMergeFields = "mailings.write.highlightMergeFields";
        public const string AddressBlock = "mailings.write.addressBlock";
        public const string GreetingLine = "mailings.write.greetingLine";
        public const string InsertMergeField = "mailings.write.insertMergeField";
        public const string Rules = "mailings.write.rules";
        public const string MatchFields = "mailings.write.matchFields";
        public const string UpdateLabels = "mailings.write.updateLabels";
    }

    public static class PreviewResults
    {
        public const string Toggle = "mailings.preview.toggle";
        public const string FirstRecord = "mailings.preview.first";
        public const string PreviousRecord = "mailings.preview.previous";
        public const string NextRecord = "mailings.preview.next";
        public const string LastRecord = "mailings.preview.last";
        public const string FindRecipient = "mailings.preview.find";
        public const string CheckErrors = "mailings.preview.checkErrors";
    }

    public static class Finish
    {
        public const string FinishAndMerge = "mailings.finish.merge";
    }
}
