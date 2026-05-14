using ProEdit.Documents;

namespace ProEdit.Markdown;

public readonly record struct MarkdownDowngradeResult(Document Document, MarkdownConversionReport Report);
