using System.Xml.Linq;
using Vibe.Office.Documents;

namespace Vibe.Office.Reporting.DocumentComposition;

internal static class ReportTemplateDocumentBinder
{
    public static void Bind(
        Document document,
        IReadOnlyDictionary<string, string> bindings,
        string storeItemId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeItemId);

        if (bindings.Count == 0)
        {
            return;
        }

        PopulateBindingXml(document, bindings, storeItemId);
        BindBlocks(document.Blocks, document, bindings);
    }

    private static void PopulateBindingXml(
        Document document,
        IReadOnlyDictionary<string, string> bindings,
        string storeItemId)
    {
        var root = new XElement("reportBindings");
        foreach (var pair in bindings)
        {
            root.Add(new XElement("binding", new XAttribute("key", pair.Key), pair.Value));
        }

        document.CustomXmlParts[storeItemId] = new XDocument(root);
    }

    private static void BindBlocks(
        IReadOnlyList<Block> blocks,
        Document document,
        IReadOnlyDictionary<string, string> bindings)
    {
        for (var blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
        {
            switch (blocks[blockIndex])
            {
                case ParagraphBlock paragraph:
                    BindParagraph(paragraph, document, bindings);
                    break;
                case TableBlock table:
                    BindTable(table, document, bindings);
                    break;
                case ContentControlStartBlock startBlock:
                    ApplyStructuredContentControlValue(startBlock.Properties, document, bindings);
                    break;
            }
        }
    }

    private static void BindTable(
        TableBlock table,
        Document document,
        IReadOnlyDictionary<string, string> bindings)
    {
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            if (row.ContentControl is not null)
            {
                ApplyStructuredContentControlValue(row.ContentControl, document, bindings);
            }

            for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
            {
                var cell = row.Cells[cellIndex];
                if (cell.ContentControl is not null)
                {
                    ApplyStructuredContentControlValue(cell.ContentControl, document, bindings);
                }

                BindBlocks(cell.Blocks, document, bindings);
            }
        }
    }

    private static void BindParagraph(
        ParagraphBlock paragraph,
        Document document,
        IReadOnlyDictionary<string, string> bindings)
    {
        paragraph.Text = ReplaceTokens(paragraph.Text, bindings);

        for (var inlineIndex = 0; inlineIndex < paragraph.Inlines.Count; inlineIndex++)
        {
            switch (paragraph.Inlines[inlineIndex])
            {
                case RunInline run:
                {
                    var replaced = ReplaceTokens(run.GetText(), bindings);
                    if (!string.Equals(replaced, run.GetText(), StringComparison.Ordinal))
                    {
                        paragraph.Inlines[inlineIndex] = CloneRun(run, replaced);
                    }

                    break;
                }
                case ContentControlStartInline controlStart:
                    ApplyStructuredContentControlValue(controlStart.Properties, document, bindings);
                    break;
            }
        }

        ReplaceMergeFields(paragraph, bindings);
    }

    private static void ReplaceMergeFields(
        ParagraphBlock paragraph,
        IReadOnlyDictionary<string, string> bindings)
    {
        for (var inlineIndex = 0; inlineIndex < paragraph.Inlines.Count; inlineIndex++)
        {
            if (paragraph.Inlines[inlineIndex] is not FieldStartInline fieldStart)
            {
                continue;
            }

            var definition = fieldStart.Definition ?? FieldInstructionParser.Parse(fieldStart.Instruction);
            if (definition?.Kind != FieldKind.MergeField || definition.Arguments.Count == 0)
            {
                continue;
            }

            var fieldName = definition.Arguments[0].Value;
            if (!bindings.TryGetValue(fieldName, out var value))
            {
                continue;
            }

            var endIndex = FindFieldEnd(paragraph.Inlines, inlineIndex + 1);
            if (endIndex < 0)
            {
                continue;
            }

            paragraph.Inlines.RemoveRange(inlineIndex, endIndex - inlineIndex + 1);
            paragraph.Inlines.Insert(inlineIndex, new RunInline(value));
        }
    }

    private static int FindFieldEnd(
        IReadOnlyList<Inline> inlines,
        int startIndex)
    {
        for (var index = startIndex; index < inlines.Count; index++)
        {
            if (inlines[index] is FieldEndInline)
            {
                return index;
            }
        }

        return -1;
    }

    private static RunInline CloneRun(
        RunInline source,
        string text)
    {
        var clone = new RunInline(text, source.Style?.Clone())
        {
            StyleId = source.StyleId,
            Hyperlink = source.Hyperlink is null
                ? null
                : new HyperlinkInfo(source.Hyperlink.Uri, source.Hyperlink.Anchor, source.Hyperlink.Tooltip),
            NodeId = source.NodeId
        };
        return clone;
    }

    private static void ApplyStructuredContentControlValue(
        ContentControlProperties properties,
        Document document,
        IReadOnlyDictionary<string, string> bindings)
    {
        if (!TryResolveBindingKey(properties, out var bindingKey)
            || !bindings.TryGetValue(bindingKey, out var value))
        {
            return;
        }

        if (properties.DataBinding is not null)
        {
            ContentControlValueResolver.TryUpdateContentControlBinding(properties.DataBinding, document, value);
        }

        switch (properties.DataType)
        {
            case ContentControlDataType.CheckBox:
                if (bool.TryParse(value, out var isChecked))
                {
                    properties.IsChecked = isChecked;
                }

                break;
            case ContentControlDataType.Date:
                properties.FullDate = value;
                break;
            case ContentControlDataType.ComboBox:
            case ContentControlDataType.DropDownList:
                properties.SelectedValue = value;
                break;
        }
    }

    private static bool TryResolveBindingKey(
        ContentControlProperties properties,
        out string key)
    {
        if (!string.IsNullOrWhiteSpace(properties.Alias))
        {
            key = properties.Alias;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(properties.Tag))
        {
            key = properties.Tag;
            return true;
        }

        if (properties.DataBinding is not null
            && TryExtractXPathLeaf(properties.DataBinding.XPath, out key))
        {
            return true;
        }

        key = string.Empty;
        return false;
    }

    private static bool TryExtractXPathLeaf(
        string? xpath,
        out string key)
    {
        key = string.Empty;
        if (string.IsNullOrWhiteSpace(xpath))
        {
            return false;
        }

        var trimmed = xpath.Trim();
        var lastSlash = trimmed.LastIndexOf('/');
        var leaf = lastSlash >= 0 ? trimmed[(lastSlash + 1)..] : trimmed;
        var predicateIndex = leaf.IndexOf('[');
        if (predicateIndex >= 0)
        {
            leaf = leaf[..predicateIndex];
        }

        var colonIndex = leaf.LastIndexOf(':');
        if (colonIndex >= 0 && colonIndex + 1 < leaf.Length)
        {
            leaf = leaf[(colonIndex + 1)..];
        }

        key = leaf.Trim();
        return key.Length > 0;
    }

    private static string ReplaceTokens(
        string? text,
        IReadOnlyDictionary<string, string> bindings)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var resolved = text;
        foreach (var pair in bindings)
        {
            resolved = resolved.Replace("{{" + pair.Key + "}}", pair.Value, StringComparison.OrdinalIgnoreCase);
        }

        return resolved;
    }
}
