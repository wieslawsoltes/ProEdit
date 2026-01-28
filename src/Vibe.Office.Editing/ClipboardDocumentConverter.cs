using Vibe.Office.Documents;

namespace Vibe.Office.Editing;

public static class ClipboardDocumentConverter
{
    public static Document ToDocument(ClipboardContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var document = CreateEmptyDocument();

        switch (content.Kind)
        {
            case ClipboardContentKind.Blocks:
            {
                if (content.Fragment is null)
                {
                    break;
                }

                foreach (var block in content.Fragment.Blocks)
                {
                    document.Blocks.Add(DocumentClone.CloneBlock(block));
                }

                ApplyResources(document, content.Fragment.Resources);
                break;
            }
            case ClipboardContentKind.FloatingObject:
            {
                var paragraph = new ParagraphBlock();
                if (content.FloatingObjects is not null)
                {
                    foreach (var floating in content.FloatingObjects)
                    {
                        paragraph.FloatingObjects.Add(DocumentClone.CloneFloatingObject(floating));
                    }
                }
                else if (content.FloatingObject is not null)
                {
                    paragraph.FloatingObjects.Add(DocumentClone.CloneFloatingObject(content.FloatingObject));
                }

                document.Blocks.Add(paragraph);
                break;
            }
        }

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(new ParagraphBlock());
        }

        return document;
    }

    public static ClipboardContent FromDocument(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var fragment = new ClipboardDocumentFragment();
        foreach (var block in document.Blocks)
        {
            fragment.Blocks.Add(DocumentClone.CloneBlock(block));
        }

        CopyResources(document, fragment.Resources);
        return ClipboardContent.FromFragment(fragment);
    }

    private static Document CreateEmptyDocument()
    {
        var document = new Document();
        document.Blocks.Clear();
        document.Sections.Clear();
        document.Sections.Add(new DocumentSection(
            document.SectionProperties,
            document.Header,
            document.Footer,
            document.FirstHeader,
            document.FirstFooter,
            document.EvenHeader,
            document.EvenFooter));
        DocumentDefaults.ApplyDefaultPageSetup(document.SectionProperties);
        return document;
    }

    private static void ApplyResources(Document document, ClipboardResourceSet resources)
    {
        CopyStyles(resources.Styles, document.Styles);
        CopyFonts(resources.Fonts, document.Fonts);
        CopyThemeColors(resources.ThemeColors, document.ThemeColors);

        document.ListDefinitions.Clear();
        foreach (var pair in resources.ListDefinitions)
        {
            document.ListDefinitions[pair.Key] = pair.Value.Clone();
        }

        document.Footnotes.Clear();
        foreach (var pair in resources.Footnotes)
        {
            document.Footnotes[pair.Key] = DocumentClone.CloneFootnoteDefinition(pair.Value);
        }

        document.Endnotes.Clear();
        foreach (var pair in resources.Endnotes)
        {
            document.Endnotes[pair.Key] = DocumentClone.CloneEndnoteDefinition(pair.Value);
        }

        document.Comments.Clear();
        foreach (var pair in resources.Comments)
        {
            document.Comments[pair.Key] = DocumentClone.CloneCommentDefinition(pair.Value);
        }
    }

    private static void CopyResources(Document document, ClipboardResourceSet resources)
    {
        CopyStyles(document.Styles, resources.Styles);
        CopyFonts(document.Fonts, resources.Fonts);
        CopyThemeColors(document.ThemeColors, resources.ThemeColors);

        resources.ListDefinitions.Clear();
        foreach (var pair in document.ListDefinitions)
        {
            resources.ListDefinitions[pair.Key] = pair.Value.Clone();
        }

        resources.Footnotes.Clear();
        foreach (var pair in document.Footnotes)
        {
            resources.Footnotes[pair.Key] = DocumentClone.CloneFootnoteDefinition(pair.Value);
        }

        resources.Endnotes.Clear();
        foreach (var pair in document.Endnotes)
        {
            resources.Endnotes[pair.Key] = DocumentClone.CloneEndnoteDefinition(pair.Value);
        }

        resources.Comments.Clear();
        foreach (var pair in document.Comments)
        {
            resources.Comments[pair.Key] = DocumentClone.CloneCommentDefinition(pair.Value);
        }
    }

    private static void CopyStyles(DocumentStyles source, DocumentStyles target)
    {
        target.ParagraphStyles.Clear();
        target.CharacterStyles.Clear();
        target.TableStyles.Clear();

        if (source.ParagraphStyles.Count > 0
            || source.CharacterStyles.Count > 0
            || source.TableStyles.Count > 0)
        {
            var clone = DocumentClone.CloneStyles(source);
            foreach (var pair in clone.ParagraphStyles)
            {
                target.ParagraphStyles[pair.Key] = pair.Value;
            }

            foreach (var pair in clone.CharacterStyles)
            {
                target.CharacterStyles[pair.Key] = pair.Value;
            }

            foreach (var pair in clone.TableStyles)
            {
                target.TableStyles[pair.Key] = pair.Value;
            }

            target.DefaultParagraphStyleId = clone.DefaultParagraphStyleId;
            target.DefaultCharacterStyleId = clone.DefaultCharacterStyleId;
            target.DefaultTableStyleId = clone.DefaultTableStyleId;
        }
        else
        {
            target.DefaultParagraphStyleId = source.DefaultParagraphStyleId;
            target.DefaultCharacterStyleId = source.DefaultCharacterStyleId;
            target.DefaultTableStyleId = source.DefaultTableStyleId;
        }
    }

    private static void CopyFonts(DocumentFonts source, DocumentFonts target)
    {
        target.FontTable.Clear();
        target.Theme.Clear();

        if (source.FontTable.Count > 0 || source.Theme.HasValues)
        {
            var clone = DocumentClone.CloneFonts(source);
            foreach (var pair in clone.FontTable)
            {
                target.FontTable[pair.Key] = pair.Value;
            }

            foreach (var pair in clone.Theme.Entries)
            {
                target.Theme.Set(pair.Key, pair.Value);
            }
        }
    }

    private static void CopyThemeColors(DocumentThemeColorMap source, DocumentThemeColorMap target)
    {
        target.Clear();
        if (!source.HasValues)
        {
            return;
        }

        var clone = DocumentClone.CloneThemeColors(source);
        foreach (var pair in clone.Overrides)
        {
            target.Set(pair.Key, pair.Value);
        }
    }
}
