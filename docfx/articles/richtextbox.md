# RichTextBox

`ProEdit.RichText.Avalonia.RichTextBox` is an editable rich text control for Avalonia that reuses the existing ProEdit editor, layout, and rendering infrastructure.

## Project

- `src/ProEdit.RichText.Avalonia/` control, range/pointer API, bridge, clipboard parsing, and theme.

## Goals

- Use the FlowDocument object model for authoring in XAML and code.
- Reuse the Word editor engine for editing behavior, undo/redo, selection, and command routing.
- Keep conversion between `FlowDocument` and `ProEdit.Documents.Document` synchronized.
- Support embedded inline and block UI containers through existing placeholder/shape marker infrastructure.

## Quick Start

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:fd="clr-namespace:ProEdit.FlowDocument;assembly=ProEdit.FlowDocument"
        xmlns:rich="clr-namespace:ProEdit.RichText.Avalonia;assembly=ProEdit.RichText.Avalonia">
  <rich:RichTextBox>
    <rich:RichTextBox.Document>
      <fd:FlowDocument PagePadding="72,72,72,72">
        <fd:Paragraph>
          <fd:Run Text="Editable rich text content." />
        </fd:Paragraph>
      </fd:FlowDocument>
    </rich:RichTextBox.Document>
  </rich:RichTextBox>
</Window>
```

## Core API

- `Document` (`FlowDocument`) current editable document.
- `Selection` (`FlowTextSelection`) active selection wrapper.
- `CaretPosition` (`FlowTextPointer`) current caret position.
- `AcceptsReturn`, `AcceptsTab` input behavior flags.
- `IsReadOnly`, `IsReadOnlyCaretVisible` editing and caret visibility control.
- `IsDocumentEnabled` interactive embedded UI enable/disable gate.
- `IsProofingEnabled`, `IsSpellingEnabled`, `IsGrammarEnabled`, `IsStyleEnabled` proofing toggles.

Key methods:

- `Copy`, `Cut`, `Paste`, `Undo`, `Redo`, `SelectAll`.
- `BeginChange`, `EndChange` for grouped edits.
- WPF-style helpers: `GetPositionFromPoint`, `GetSpellingError`, `GetSpellingErrorRange`, `GetNextSpellingErrorPosition`, `ShouldSerializeDocument`.

## Input and Editing Behavior

- Keyboard routing includes line/document navigation, page navigation, and select-all shortcuts.
- Read-only mode blocks mutating operations and preserves navigation/selection.
- `AcceptsReturn` and `AcceptsTab` are routed through the same editor input pipeline used by `DocumentView`.
- Clipboard and drag-drop support rich format preference: OOXML, RTF, HTML, then plain text.

## Conversion and Sync

- `FlowDocument -> Document` uses `FlowDocumentConverter`.
- `Document -> FlowDocument` uses `DocumentToFlowDocumentConverter`.
- Incremental mirror updates are used for safe single-paragraph content changes, with fallback to full conversion for complex topologies.

## Testing

- Headless control tests: `tests/ProEdit.RichText.Avalonia.Headless.Tests/`.
- Input router behavior tests: `tests/ProEdit.Word.Editor.Tests/EditorCommandInputRouterTests.cs`.

## Sample

Run the FlowDocument sample app:

```bash
dotnet run --project src/ProEdit.FlowDocument.App/ProEdit.FlowDocument.App.csproj
```

Use the `RichTextBox Smoke` tab to validate editing and selection behavior.
