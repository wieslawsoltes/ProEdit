# FlowDocument

The FlowDocument stack provides a WPF-style document object model for Avalonia XAML, a converter to the VibeOffice document model, and a read-only renderer based on the existing layout + Skia rendering pipeline.

## Projects

- `Vibe.Office.FlowDocument`: FlowDocument object model and collections.
- `Vibe.Office.FlowDocument.Documents`: FlowDocument → `Vibe.Office.Documents.Document` converter.
- `Vibe.Office.FlowDocument.Avalonia`: `FlowDocumentView` control for rendering.
- `Vibe.FlowDocument.App`: Sample app demonstrating `FlowDocumentView`.

## FlowDocumentView

`FlowDocumentView` is a read-only Avalonia control that renders a `FlowDocument` through the shared layout and renderer stack (`DocumentLayouter` + `SkiaDocumentRenderer`).

Example usage:

```xml
<FlowDocumentView
    FlowDocument="{Binding Document}"
    UsePagination="True"
    ZoomFactor="{Binding ZoomFactor}"
    Background="#F8F9FD"
    InlineUiPlaceholderText="[Inline UI]"
    BlockUiPlaceholderText="[Block UI]" />
```

## Conversion Notes

The converter maps FlowDocument blocks and inlines into the VibeOffice document model:

- Paragraph and inline formatting is mapped into `ParagraphProperties` and `TextStyleProperties`.
- Lists map to `ListInfo` with nested levels.
- Tables map to `TableBlock` with column widths and row-span handling via vertical merges.
- Figures/Floaters convert to floating `ShapeInline` objects with a text box containing converted blocks.
- `InlineUIContainer` and `BlockUIContainer` can be converted either to placeholder text (default converter mode) or embedded shape markers with hosted Avalonia controls (`FlowDocumentView` mode).

## Sample App

Run the sample:

```bash
cd /Users/wieslawsoltes/GitHub/VibeOffice

dotnet run --project src/Vibe.FlowDocument.App/Vibe.FlowDocument.App.csproj
```

The app showcases lists, tables with row spans, hyperlinks, figures/floaters, and embedded inline/block UI containers declared in both code and XAML.

## FlowDocument Overview Parity Samples

`Vibe.FlowDocument.App` includes a strict pass of the official WPF `Flow Document Overview` samples.

- Each sample tab is rendered side-by-side as `Verbatim` and `Avalonia-adapted`.
- The tab set includes the XAML snippets and the C# code-only snippet variants referenced by:
  - `flow-document-overview`
  - `flow-document-overview#flow_related_classes`
- Schema walkthrough samples (`SchemaWalkThrough1`, `SchemaWalkThrough2`, `SchemaExample`) and collection-operation snippets (`_SectionBlocksAdd`, `_SpanInlinesRemoveLast`, `_SpanInlinesClear`) are included as dedicated tabs.

Current adaptation note:

- WPF attached typography properties used in `_TextElement_TypogXAML` are represented with equivalent flow content and available FlowDocument styling metadata in the adapted pane.
