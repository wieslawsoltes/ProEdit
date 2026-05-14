using ProEdit.Documents.Formats;

namespace ProEdit.Html;

public sealed class HtmlOptions : DocumentFormatOptions
{
    public HtmlFlavor Flavor { get; set; } = HtmlFlavor.Html5;
    public bool AllowScripts { get; set; }
    public bool AllowStyles { get; set; } = true;
    public bool NormalizeLineEndings { get; set; } = true;
    public bool PreserveUnknownElements { get; set; } = true;
    public bool PrettyPrint { get; set; }
}
