using ProEdit.FlowDocument.Documents;

namespace ProEdit.WinUICompat.Bridges;

public interface ICompatEmbeddedUiBridge : ICompatDocumentBridge
{
    IReadOnlyDictionary<string, EmbeddedFlowUiElement> EmbeddedUiElementsById { get; }

    string EmbeddedUiShapePrefix { get; }

    bool ConfigureEmbeddedUiElements(
        bool enabled,
        string shapePrefix,
        Func<object?, bool>? elementPredicate,
        Func<object, bool, (double Width, double Height)?>? sizeResolver);
}
