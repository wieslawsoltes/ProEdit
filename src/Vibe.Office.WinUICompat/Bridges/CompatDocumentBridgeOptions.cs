using Vibe.Office.FlowDocument.Documents;

namespace Vibe.Office.WinUICompat.Bridges;

public sealed class CompatDocumentBridgeOptions
{
    public bool EnableEmbeddedUiElements { get; set; }

    public string EmbeddedUiShapePrefix { get; set; } = FlowDocumentConverterOptions.DefaultEmbeddedUiShapePrefix;

    public Func<object?, bool>? EmbeddedUiElementPredicate { get; set; }

    public Func<object, bool, (double Width, double Height)?>? EmbeddedUiSizeResolver { get; set; }
}
