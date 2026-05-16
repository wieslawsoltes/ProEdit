# Projects

## Core
- `src/ProEdit.Primitives/` shared primitives and low-level types
- `src/ProEdit.Documents/` document model and shared document structures
- `src/ProEdit.Layout/` layout engine and pagination pipeline
- `src/ProEdit.Rendering/` rendering abstractions and composition

## Rendering Backends
- `src/ProEdit.Rendering.Skia/` Skia-based rendering backend

## Editing and Formats
- `src/ProEdit.Editing/` editing services, selection, and proofing utilities
- `src/ProEdit.Html/` HTML conversion and integration helpers
- `src/ProEdit.Markdown/` Markdown conversion and integration helpers
- `src/ProEdit.OpenXml/` OpenXML import and export
- `src/ProEdit.Pdf/` PDF domain abstractions
- `src/ProEdit.Pdf.Documents/` PDF document integration with core models
- `src/ProEdit.Pdf.PdfPig/` PDF parsing via PdfPig
- `src/ProEdit.Pdf.PdfSharp/` PDF generation via PDFsharp

## Printing
- `src/ProEdit.Printing/` printing abstractions
- `src/ProEdit.Printing.Documents/` document-to-print conversion
- `src/ProEdit.Printing.Skia/` Skia-based print pipeline
- `src/ProEdit.Printing.System/` system print integration
- `src/ProEdit.Printing.Avalonia/` Avalonia UI for printing

## Collaboration
- `src/ProEdit.Collaboration/` CRDT collaboration core
- `src/ProEdit.Collaboration.Protocol/` wire protocol types
- `src/ProEdit.Collaboration.Transports/` transport implementations
- `src/ProEdit.Collaboration.Server/` WebSocket relay server
- `src/ProEdit.Collaboration.UI/` collaboration ViewModels and UI state
- `src/ProEdit.Collaboration.Editor/` editor integration for collaboration

## UI and Shells
- `src/ProEdit.Ribbon/` ribbon abstractions
- `src/ProEdit.Ribbon.Avalonia/` Avalonia ribbon controls
- `src/ProEdit.Word.Editor/` word-processing editor core
- `src/ProEdit.Word.Avalonia/` Avalonia-based editor UI
- `src/ProEdit.Word.App/` desktop app host
- `src/ProEdit.Controls.Skia/` shared Skia document control host and framework-neutral control state
- `src/ProEdit.Controls.Skia.Avalonia/` Avalonia viewer and editor controls
- `src/ProEdit.Controls.Skia.Uno/` Uno Platform viewer and editor controls
- `src/ProEdit.Controls.Skia.Maui/` .NET MAUI viewer and editor controls

## Samples
- `samples/ProEdit.Controls.Skia.Sample.Shared/` shared sample document and ViewModel
- `samples/ProEdit.Controls.Skia.Avalonia.Sample/` Avalonia sample app for the shared Skia controls
- `samples/ProEdit.Controls.Skia.Uno.Sample/` Uno Platform sample app for the shared Skia controls
- `samples/ProEdit.Controls.Skia.Maui.Sample/` .NET MAUI sample app for the shared Skia controls

## Automation and Macros
- `src/ProEdit.Vba/` VBA core types
- `src/ProEdit.Vba.Runtime/` VBA runtime support
- `src/ProEdit.Macros/` macro orchestration

## Proofing
- `src/ProEdit.Proofing.GrammarApi.Plugin/` grammar plugin integration

## Tests and Benchmarks
- `tests/` unit and UI tests
- `benchmarks/` performance benchmarks

Next: [Contributing](contributing.md)
