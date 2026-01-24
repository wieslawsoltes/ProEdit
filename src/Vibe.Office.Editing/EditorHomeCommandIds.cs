namespace Vibe.Office.Editing;

public static class EditorHomeCommandIds
{
    public static class Clipboard
    {
        public const string Paste = "clipboard.paste";
        public const string PasteKeepSource = "clipboard.paste.keepSource";
        public const string PasteMatchDestination = "clipboard.paste.matchDest";
        public const string PasteTextOnly = "clipboard.paste.textOnly";
        public const string Cut = "clipboard.cut";
        public const string Copy = "clipboard.copy";
        public const string FormatPainterToggle = "clipboard.formatPainter.toggle";
    }

    public static class Font
    {
        public const string FamilySet = "font.family.set";
        public const string SizeSet = "font.size.set";
        public const string SizeIncrease = "font.size.increase";
        public const string SizeDecrease = "font.size.decrease";
        public const string ChangeCaseSentence = "font.case.sentence";
        public const string ChangeCaseLower = "font.case.lower";
        public const string ChangeCaseUpper = "font.case.upper";
        public const string ChangeCaseCapitalize = "font.case.capitalize";
        public const string ChangeCaseToggle = "font.case.toggle";
        public const string ClearFormatting = "font.clear";
        public const string BoldToggle = "font.bold.toggle";
        public const string ItalicToggle = "font.italic.toggle";
        public const string UnderlineToggle = "font.underline.toggle";
        public const string UnderlineStyleSet = "font.underline.style.set";
        public const string StrikethroughToggle = "font.strike.toggle";
        public const string SubscriptToggle = "font.subscript.toggle";
        public const string SuperscriptToggle = "font.superscript.toggle";
        public const string TextEffectOutline = "font.effect.outline";
        public const string TextEffectShadow = "font.effect.shadow";
        public const string TextEffectEmboss = "font.effect.emboss";
        public const string TextEffectImprint = "font.effect.imprint";
        public const string TextEffectClear = "font.effect.clear";
        public const string HighlightSet = "font.highlight.set";
        public const string ColorSet = "font.color.set";
        public const string DialogApply = "font.dialog.apply";
    }

    public static class Paragraph
    {
        public const string ListBullets = "para.list.bullets";
        public const string ListNumbering = "para.list.numbering";
        public const string ListMultilevel = "para.list.multilevel";
        public const string IndentIncrease = "para.indent.increase";
        public const string IndentDecrease = "para.indent.decrease";
        public const string TabStopAdd = "para.tab.add";
        public const string TabStopUpdate = "para.tab.update";
        public const string TabStopRemove = "para.tab.remove";
        public const string TabStopClear = "para.tab.clear";
        public const string Sort = "para.sort";
        public const string ShowInvisiblesToggle = "para.showInvisibles.toggle";
        public const string AlignLeft = "para.align.left";
        public const string AlignCenter = "para.align.center";
        public const string AlignRight = "para.align.right";
        public const string AlignJustify = "para.align.justify";
        public const string LineSpacingSet = "para.spacing.set";
        public const string LineSpacingOptions = "para.spacing.options";
        public const string ShadingSet = "para.shading.set";
        public const string BorderSet = "para.border.set";
        public const string DialogApply = "para.dialog.apply";
    }

    public static class Styles
    {
        public const string Apply = "style.apply";
        public const string OpenPane = "style.openPane";
        public const string Manage = "style.manage";
    }

    public static class Editing
    {
        public const string Find = "edit.find";
        public const string Replace = "edit.replace";
        public const string ReplaceAll = "edit.replace.all";
        public const string Undo = "edit.undo";
        public const string Redo = "edit.redo";
        public const string SelectAll = "edit.select.all";
        public const string SelectObjects = "edit.select.objects";
        public const string SelectSimilarFormatting = "edit.select.similar";
    }
}
