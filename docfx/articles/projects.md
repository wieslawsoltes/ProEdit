# Projects

## Core
- `src/Vibe.Office.Primitives/` shared primitives and low-level types
- `src/Vibe.Office.Documents/` document model and shared document structures
- `src/Vibe.Office.Layout/` layout engine and pagination pipeline
- `src/Vibe.Office.Rendering/` rendering abstractions and composition

## Rendering Backends
- `src/Vibe.Office.Rendering.Skia/` Skia-based rendering backend

## Editing and Formats
- `src/Vibe.Office.Editing/` editing services, selection, and proofing utilities
- `src/Vibe.Office.Html/` HTML conversion and integration helpers
- `src/Vibe.Office.Markdown/` Markdown conversion and integration helpers
- `src/Vibe.Office.OpenXml/` OpenXML import and export
- `src/Vibe.Office.Pdf/` PDF domain abstractions
- `src/Vibe.Office.Pdf.Documents/` PDF document integration with core models
- `src/Vibe.Office.Pdf.PdfPig/` PDF parsing via PdfPig
- `src/Vibe.Office.Pdf.PdfSharp/` PDF generation via PDFsharp

## Printing
- `src/Vibe.Office.Printing/` printing abstractions
- `src/Vibe.Office.Printing.Documents/` document-to-print conversion
- `src/Vibe.Office.Printing.Skia/` Skia-based print pipeline
- `src/Vibe.Office.Printing.System/` system print integration
- `src/Vibe.Office.Printing.Avalonia/` Avalonia UI for printing

## Collaboration
- `src/Vibe.Office.Collaboration/` CRDT collaboration core
- `src/Vibe.Office.Collaboration.Protocol/` wire protocol types
- `src/Vibe.Office.Collaboration.Transports/` transport implementations
- `src/Vibe.Office.Collaboration.Server/` WebSocket relay server
- `src/Vibe.Office.Collaboration.UI/` collaboration ViewModels and UI state
- `src/Vibe.Office.Collaboration.Editor/` editor integration for collaboration

## UI and Shells
- `src/Vibe.Office.Ribbon/` ribbon abstractions
- `src/Vibe.Office.Ribbon.Avalonia/` Avalonia ribbon controls
- `src/Vibe.Word.Editor/` word-processing editor core
- `src/Vibe.Word.Avalonia/` Avalonia-based editor UI
- `src/Vibe.Word.App/` desktop app host

## Automation and Macros
- `src/Vibe.Office.Vba/` VBA core types
- `src/Vibe.Office.Vba.Runtime/` VBA runtime support
- `src/Vibe.Office.Macros/` macro orchestration

## Proofing
- `src/Vibe.Office.Proofing.GrammarApi.Plugin/` grammar plugin integration

## Tests and Benchmarks
- `tests/` unit and UI tests
- `benchmarks/` performance benchmarks

Next: [Contributing](contributing.md)
