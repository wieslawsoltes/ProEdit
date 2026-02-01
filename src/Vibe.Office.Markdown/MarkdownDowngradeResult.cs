using Vibe.Office.Documents;

namespace Vibe.Office.Markdown;

public readonly record struct MarkdownDowngradeResult(Document Document, MarkdownConversionReport Report);
