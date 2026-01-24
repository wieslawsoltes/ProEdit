namespace Vibe.Office.Editing;

public static class EditorReviewCommandIds
{
    public static class Proofing
    {
        public const string SpellingGrammar = "review.proofing.spelling";
        public const string Thesaurus = "review.proofing.thesaurus";
        public const string WordCount = "review.proofing.wordCount";
    }

    public static class Speech
    {
        public const string ReadAloud = "review.speech.readAloud";
    }

    public static class Accessibility
    {
        public const string CheckAccessibility = "review.accessibility.check";
    }

    public static class Language
    {
        public const string Translate = "review.language.translate";
        public const string SetLanguage = "review.language.set";
    }

    public static class Comments
    {
        public const string NewComment = "review.comments.new";
        public const string DeleteComment = "review.comments.delete";
        public const string PreviousComment = "review.comments.previous";
        public const string NextComment = "review.comments.next";
    }

    public static class Tracking
    {
        public const string TrackChangesToggle = "review.tracking.toggle";
        public const string ShowMarkup = "review.tracking.showMarkup";
        public const string ReviewingPane = "review.tracking.reviewingPane";
    }

    public static class Changes
    {
        public const string Accept = "review.changes.accept";
        public const string Reject = "review.changes.reject";
        public const string PreviousChange = "review.changes.previous";
        public const string NextChange = "review.changes.next";
    }

    public static class Compare
    {
        public const string CompareDocuments = "review.compare.compare";
        public const string Combine = "review.compare.combine";
    }

    public static class Protect
    {
        public const string RestrictEditing = "review.protect.restrictEditing";
    }
}
