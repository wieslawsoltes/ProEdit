# ProEdit

[![CI](https://github.com/wieslawsoltes/ProEdit/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/ProEdit/actions/workflows/build.yml)
[![Docs](https://github.com/wieslawsoltes/ProEdit/actions/workflows/docs.yml/badge.svg)](https://github.com/wieslawsoltes/ProEdit/actions/workflows/docs.yml)
[![Release](https://github.com/wieslawsoltes/ProEdit/actions/workflows/release.yml/badge.svg)](https://github.com/wieslawsoltes/ProEdit/actions/workflows/release.yml)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

ProEdit is a modular .NET 10 document editing, rendering, reporting, and collaboration platform for desktop and embedded application scenarios. It combines a rich document object model, layout and rendering engines, import/export pipelines, Avalonia controls, reporting components, printing services, real-time collaboration infrastructure, and automation integrations in a package-oriented repository.

The codebase is designed as a set of composable libraries rather than a single monolithic application. Hosts can adopt the low-level document model, the FlowDocument/RichTextBox controls, the Word-style editor shell, the reporting designer/viewer, the collaboration stack, or the format conversion services independently.

## Capabilities

- Rich document model with sections, paragraphs, runs, tables, lists, styles, shapes, fields, notes, content controls, mail merge data, document protection, comments, citations, metadata, and revision-oriented structures.
- Layout and rendering pipelines for pagination, line breaking, tables, floating objects, shapes, proofing decorations, and Skia-backed output.
- Import/export and conversion support across Open XML DOCX, RTF, HTML, Markdown, PDF, PostScript/EPS, XPS/OXPS, and internal document/FlowDocument models.
- Avalonia UI components including `FlowDocumentView`, editable `RichTextBox`, ribbon controls, Word-style editor surfaces, print UI, and reporting designer/viewer controls.
- Editing infrastructure for selection, undo/redo, clipboard formats, proofing, formatting state, table editing, content controls, headers/footers, comments, track changes, and command routing.
- Reporting stack with report definitions, data connectors, expression evaluation, materialization, document composition, export, RDL serialization, service orchestration, and desktop authoring/viewing.
- Collaboration core with operation batches, position tokens, snapshots, persistence, protocol models, transports, WebSocket relay support, and UI state/view models.
- Automation and integration components for VBA-style runtime support, macro orchestration, grammar proofing plugins, and Model Context Protocol (MCP) tools/resources.
- Compatibility layers for WinUI/Uno rich text APIs and RichEditBox-style host scenarios.

## Repository Structure

| Path | Purpose |
| --- | --- |
| `src/` | Production libraries, Avalonia controls, desktop apps, reporting components, collaboration services, and format providers. |
| `tests/` | xUnit unit tests plus Avalonia headless UI tests for editor, reporting, FlowDocument, RichTextBox, collaboration, and compatibility layers. |
| `samples/` | Small runnable hosts that showcase reusable controls across Avalonia, Uno Platform, and .NET MAUI. |
| `benchmarks/` | BenchmarkDotNet performance benchmarks for layout and document synchronization paths. |
| `docfx/` | DocFX documentation source for architecture, project map, getting started, FlowDocument, RichTextBox, and contribution guidance. |
| `.github/workflows/` | Cross-platform CI, documentation deployment, release packaging, and NuGet publish automation. |

## Architecture Map

| Area | Primary Projects | Responsibility |
| --- | --- | --- |
| Core document model | `ProEdit.Primitives`, `ProEdit.Documents`, `ProEdit.Documents.Data`, `ProEdit.Documents.Rtf` | Shared primitives, document structures, data binding, and RTF support. |
| Layout and rendering | `ProEdit.Layout`, `ProEdit.Rendering`, `ProEdit.Rendering.Skia` | Pagination, layout records, rendering abstractions, and Skia rendering backend. |
| FlowDocument and rich text | `ProEdit.FlowDocument`, `ProEdit.FlowDocument.Documents`, `ProEdit.FlowDocument.Avalonia`, `ProEdit.RichText.Avalonia` | WPF-style FlowDocument model, conversion to the core document model, read-only viewing, and editable Avalonia rich text control. |
| Word editor | `ProEdit.Editing`, `ProEdit.Word.Editor`, `ProEdit.Word.Avalonia`, `ProEdit.Word.App` | Editing services, command maps, proofing, ribbon-backed editor UI, and desktop host application. |
| Shared controls | `ProEdit.Controls.Skia`, `ProEdit.Controls.Skia.Avalonia`, `ProEdit.Controls.Skia.Uno`, `ProEdit.Controls.Skia.Maui` | Packable read-only viewer and editor controls for Avalonia, Uno Platform, and .NET MAUI, backed by the shared layout/rendering/editor host. |
| Formats | `ProEdit.OpenXml`, `ProEdit.Html`, `ProEdit.Markdown`, `ProEdit.Pdf.*`, `ProEdit.PostScript`, `ProEdit.Xps`, `ProEdit.FlowDocument.IO` | Document import/export, multi-format conversion, and backend-specific PDF/PS/XPS integrations. |
| Printing | `ProEdit.Printing`, `ProEdit.Printing.Documents`, `ProEdit.Printing.Skia`, `ProEdit.Printing.System`, `ProEdit.Printing.Avalonia` | Print contracts, document print conversion, Skia/system printing, and Avalonia print UI. |
| Reporting | `ProEdit.Reporting.*` | Report model, data, expressions, RDL, serialization, materialization, exports, services, Avalonia designer/viewer, and desktop reporting app. |
| Collaboration | `ProEdit.Collaboration.*`, `ProEdit.RichText.Avalonia.Collaboration`, `ProEdit.WinUICompat.Collaboration` | Operation model, snapshot persistence, protocol, transports, relay server, UI state, and editor/control integrations. |
| UI foundation | `ProEdit.Ribbon`, `ProEdit.Ribbon.Avalonia` | Ribbon model and Avalonia ribbon controls/themes. |
| Automation and extensibility | `ProEdit.Vba`, `ProEdit.Vba.Runtime`, `ProEdit.Macros`, `ProEdit.Mcp.*`, `ProEdit.Proofing.GrammarApi.Plugin` | Macro execution, VBA host/runtime services, MCP integration, and grammar proofing extension point. |
| Compatibility | `ProEdit.WinUICompat`, `ProEdit.WinUICompat.Uno`, `ProEdit.WinUICompat.App` | WinUI/Uno compatible rich text APIs and sample host. |

## NuGet Packages

The table lists packable library projects in this repository. Desktop/sample hosts such as `ProEdit.Word.App`, `ProEdit.Reporting.App`, `ProEdit.FlowDocument.App`, and `ProEdit.WinUICompat.App` are source-built applications and are intentionally not listed as packages.

| Area | Package | NuGet | Purpose |
| --- | --- | --- | --- |
| Core | [ProEdit.Primitives](https://www.nuget.org/packages/ProEdit.Primitives) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Primitives.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Primitives) | Shared primitive types for geometry and document infrastructure. |
| Core | [ProEdit.Documents](https://www.nuget.org/packages/ProEdit.Documents) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Documents.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Documents) | Core document object model. |
| Core | [ProEdit.Documents.Data](https://www.nuget.org/packages/ProEdit.Documents.Data) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Documents.Data.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Documents.Data) | Document data binding and reporting data integration. |
| Core | [ProEdit.Documents.Rtf](https://www.nuget.org/packages/ProEdit.Documents.Rtf) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Documents.Rtf.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Documents.Rtf) | RTF parsing and serialization. |
| Layout | [ProEdit.Layout](https://www.nuget.org/packages/ProEdit.Layout) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Layout.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Layout) | Pagination and document layout engine. |
| Rendering | [ProEdit.Rendering](https://www.nuget.org/packages/ProEdit.Rendering) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Rendering.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Rendering) | Rendering abstractions and composition contracts. |
| Rendering | [ProEdit.Rendering.Skia](https://www.nuget.org/packages/ProEdit.Rendering.Skia) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Rendering.Skia.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Rendering.Skia) | Skia-backed renderer. |
| FlowDocument | [ProEdit.FlowDocument](https://www.nuget.org/packages/ProEdit.FlowDocument) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.FlowDocument.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.FlowDocument) | Avalonia-friendly FlowDocument object model. |
| FlowDocument | [ProEdit.FlowDocument.Documents](https://www.nuget.org/packages/ProEdit.FlowDocument.Documents) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.FlowDocument.Documents.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.FlowDocument.Documents) | Conversion between FlowDocument and core documents. |
| FlowDocument | [ProEdit.FlowDocument.Avalonia](https://www.nuget.org/packages/ProEdit.FlowDocument.Avalonia) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.FlowDocument.Avalonia.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.FlowDocument.Avalonia) | Avalonia `FlowDocumentView` control. |
| FlowDocument | [ProEdit.FlowDocument.IO](https://www.nuget.org/packages/ProEdit.FlowDocument.IO) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.FlowDocument.IO.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.FlowDocument.IO) | Multi-format FlowDocument/document conversion services. |
| Rich Text | [ProEdit.RichText.Avalonia](https://www.nuget.org/packages/ProEdit.RichText.Avalonia) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.RichText.Avalonia.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.RichText.Avalonia) | Editable Avalonia rich text control. |
| Rich Text | [ProEdit.RichText.Avalonia.Collaboration](https://www.nuget.org/packages/ProEdit.RichText.Avalonia.Collaboration) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.RichText.Avalonia.Collaboration.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.RichText.Avalonia.Collaboration) | Collaboration integration for the Avalonia rich text control. |
| Shared Controls | [ProEdit.Controls.Skia](https://www.nuget.org/packages/ProEdit.Controls.Skia) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Controls.Skia.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Controls.Skia) | Shared Skia document host and framework-neutral control contracts. |
| Shared Controls | [ProEdit.Controls.Skia.Avalonia](https://www.nuget.org/packages/ProEdit.Controls.Skia.Avalonia) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Controls.Skia.Avalonia.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Controls.Skia.Avalonia) | Avalonia read-only viewer and editable document controls. |
| Shared Controls | [ProEdit.Controls.Skia.Uno](https://www.nuget.org/packages/ProEdit.Controls.Skia.Uno) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Controls.Skia.Uno.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Controls.Skia.Uno) | Uno Platform read-only viewer and editable document controls. |
| Shared Controls | [ProEdit.Controls.Skia.Maui](https://www.nuget.org/packages/ProEdit.Controls.Skia.Maui) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Controls.Skia.Maui.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Controls.Skia.Maui) | .NET MAUI read-only viewer and editable document controls. |
| Editing | [ProEdit.Editing](https://www.nuget.org/packages/ProEdit.Editing) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Editing.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Editing) | Editing, selection, clipboard, proofing, and command services. |
| Word | [ProEdit.Word.Editor](https://www.nuget.org/packages/ProEdit.Word.Editor) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Word.Editor.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Word.Editor) | Word-processing editor engine. |
| Word | [ProEdit.Word.Avalonia](https://www.nuget.org/packages/ProEdit.Word.Avalonia) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Word.Avalonia.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Word.Avalonia) | Avalonia editor UI and resources. |
| Ribbon | [ProEdit.Ribbon](https://www.nuget.org/packages/ProEdit.Ribbon) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Ribbon.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Ribbon) | Ribbon model and command abstractions. |
| Ribbon | [ProEdit.Ribbon.Avalonia](https://www.nuget.org/packages/ProEdit.Ribbon.Avalonia) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Ribbon.Avalonia.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Ribbon.Avalonia) | Avalonia ribbon controls and theme resources. |
| Formats | [ProEdit.OpenXml](https://www.nuget.org/packages/ProEdit.OpenXml) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.OpenXml.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.OpenXml) | Open XML/DOCX import and export. |
| Formats | [ProEdit.Html](https://www.nuget.org/packages/ProEdit.Html) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Html.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Html) | HTML import/export helpers. |
| Formats | [ProEdit.Markdown](https://www.nuget.org/packages/ProEdit.Markdown) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Markdown.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Markdown) | Markdown import/export helpers. |
| Formats | [ProEdit.Pdf](https://www.nuget.org/packages/ProEdit.Pdf) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Pdf.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Pdf) | PDF domain abstractions. |
| Formats | [ProEdit.Pdf.Documents](https://www.nuget.org/packages/ProEdit.Pdf.Documents) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Pdf.Documents.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Pdf.Documents) | PDF integration with the document model. |
| Formats | [ProEdit.Pdf.PdfPig](https://www.nuget.org/packages/ProEdit.Pdf.PdfPig) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Pdf.PdfPig.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Pdf.PdfPig) | PDF parsing via PdfPig. |
| Formats | [ProEdit.Pdf.PdfSharp](https://www.nuget.org/packages/ProEdit.Pdf.PdfSharp) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Pdf.PdfSharp.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Pdf.PdfSharp) | PDF generation via PDFsharp. |
| Formats | [ProEdit.PostScript](https://www.nuget.org/packages/ProEdit.PostScript) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.PostScript.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.PostScript) | PostScript/EPS conversion support. |
| Formats | [ProEdit.Xps](https://www.nuget.org/packages/ProEdit.Xps) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Xps.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Xps) | XPS/OXPS conversion support. |
| Printing | [ProEdit.Printing](https://www.nuget.org/packages/ProEdit.Printing) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Printing.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Printing) | Printing contracts and shared models. |
| Printing | [ProEdit.Printing.Documents](https://www.nuget.org/packages/ProEdit.Printing.Documents) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Printing.Documents.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Printing.Documents) | Document-to-print conversion. |
| Printing | [ProEdit.Printing.Skia](https://www.nuget.org/packages/ProEdit.Printing.Skia) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Printing.Skia.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Printing.Skia) | Skia print pipeline. |
| Printing | [ProEdit.Printing.System](https://www.nuget.org/packages/ProEdit.Printing.System) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Printing.System.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Printing.System) | System print integration. |
| Printing | [ProEdit.Printing.Avalonia](https://www.nuget.org/packages/ProEdit.Printing.Avalonia) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Printing.Avalonia.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Printing.Avalonia) | Avalonia print UI. |
| Collaboration | [ProEdit.Collaboration](https://www.nuget.org/packages/ProEdit.Collaboration) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Collaboration.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Collaboration) | Collaboration operation model, engine, persistence, and snapshots. |
| Collaboration | [ProEdit.Collaboration.Protocol](https://www.nuget.org/packages/ProEdit.Collaboration.Protocol) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Collaboration.Protocol.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Collaboration.Protocol) | Collaboration wire protocol types and codecs. |
| Collaboration | [ProEdit.Collaboration.Transports](https://www.nuget.org/packages/ProEdit.Collaboration.Transports) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Collaboration.Transports.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Collaboration.Transports) | Local, shared-file, loopback, and WebSocket transport implementations. |
| Collaboration | [ProEdit.Collaboration.Server](https://www.nuget.org/packages/ProEdit.Collaboration.Server) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Collaboration.Server.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Collaboration.Server) | WebSocket relay server components. |
| Collaboration | [ProEdit.Collaboration.UI](https://www.nuget.org/packages/ProEdit.Collaboration.UI) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Collaboration.UI.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Collaboration.UI) | Collaboration UI state and view models. |
| Reporting | [ProEdit.Reporting.Core](https://www.nuget.org/packages/ProEdit.Reporting.Core) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Reporting.Core.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Reporting.Core) | Report definitions, items, parameters, diagnostics, and execution contracts. |
| Reporting | [ProEdit.Reporting.Data](https://www.nuget.org/packages/ProEdit.Reporting.Data) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Reporting.Data.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Reporting.Data) | Report data sources, data sets, and runtime data access. |
| Reporting | [ProEdit.Reporting.Expressions](https://www.nuget.org/packages/ProEdit.Reporting.Expressions) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Reporting.Expressions.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Reporting.Expressions) | Report expression evaluation. |
| Reporting | [ProEdit.Reporting.Materialization](https://www.nuget.org/packages/ProEdit.Reporting.Materialization) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Reporting.Materialization.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Reporting.Materialization) | Materialized report generation. |
| Reporting | [ProEdit.Reporting.DocumentComposition](https://www.nuget.org/packages/ProEdit.Reporting.DocumentComposition) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Reporting.DocumentComposition.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Reporting.DocumentComposition) | Report-to-document composition. |
| Reporting | [ProEdit.Reporting.Export](https://www.nuget.org/packages/ProEdit.Reporting.Export) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Reporting.Export.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Reporting.Export) | Report export to document and page formats. |
| Reporting | [ProEdit.Reporting.Rdl](https://www.nuget.org/packages/ProEdit.Reporting.Rdl) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Reporting.Rdl.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Reporting.Rdl) | RDL/SSRS report serialization support. |
| Reporting | [ProEdit.Reporting.Serialization](https://www.nuget.org/packages/ProEdit.Reporting.Serialization) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Reporting.Serialization.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Reporting.Serialization) | JSON report serialization. |
| Reporting | [ProEdit.Reporting.Service](https://www.nuget.org/packages/ProEdit.Reporting.Service) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Reporting.Service.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Reporting.Service) | Report repository, execution, scheduling, and service orchestration. |
| Reporting | [ProEdit.Reporting.Avalonia.Viewer](https://www.nuget.org/packages/ProEdit.Reporting.Avalonia.Viewer) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Reporting.Avalonia.Viewer.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Reporting.Avalonia.Viewer) | Avalonia report viewer and preview surface. |
| Reporting | [ProEdit.Reporting.Avalonia.Designer](https://www.nuget.org/packages/ProEdit.Reporting.Avalonia.Designer) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Reporting.Avalonia.Designer.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Reporting.Avalonia.Designer) | Avalonia report designer and authoring panes. |
| Automation | [ProEdit.Vba](https://www.nuget.org/packages/ProEdit.Vba) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Vba.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Vba) | VBA-style automation abstractions. |
| Automation | [ProEdit.Vba.Runtime](https://www.nuget.org/packages/ProEdit.Vba.Runtime) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Vba.Runtime.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Vba.Runtime) | VBA runtime support. |
| Automation | [ProEdit.Macros](https://www.nuget.org/packages/ProEdit.Macros) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Macros.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Macros) | Macro orchestration. |
| MCP | [ProEdit.Mcp](https://www.nuget.org/packages/ProEdit.Mcp) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Mcp.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Mcp) | Model Context Protocol server, tools, and resources. |
| MCP | [ProEdit.Mcp.Documents](https://www.nuget.org/packages/ProEdit.Mcp.Documents) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Mcp.Documents.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Mcp.Documents) | MCP document tools for content controls and mail merge workflows. |
| MCP | [ProEdit.Mcp.Reporting](https://www.nuget.org/packages/ProEdit.Mcp.Reporting) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Mcp.Reporting.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Mcp.Reporting) | MCP reporting tools, exports, and connector workflows. |
| Proofing | [ProEdit.Proofing.GrammarApi.Plugin](https://www.nuget.org/packages/ProEdit.Proofing.GrammarApi.Plugin) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.Proofing.GrammarApi.Plugin.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.Proofing.GrammarApi.Plugin) | Grammar API proofing plugin. |
| Compatibility | [ProEdit.WinUICompat](https://www.nuget.org/packages/ProEdit.WinUICompat) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.WinUICompat.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.WinUICompat) | WinUI-style rich text compatibility layer. |
| Compatibility | [ProEdit.WinUICompat.Uno](https://www.nuget.org/packages/ProEdit.WinUICompat.Uno) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.WinUICompat.Uno.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.WinUICompat.Uno) | Uno integration for WinUI compatibility APIs. |
| Compatibility | [ProEdit.WinUICompat.Collaboration](https://www.nuget.org/packages/ProEdit.WinUICompat.Collaboration) | [![NuGet](https://img.shields.io/nuget/v/ProEdit.WinUICompat.Collaboration.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/ProEdit.WinUICompat.Collaboration) | Collaboration integration for WinUI/Uno compatibility controls. |

## Installation

Install only the packages needed by the host application. For example, an Avalonia editor host typically starts with:

```bash
dotnet add package ProEdit.Word.Avalonia
dotnet add package ProEdit.RichText.Avalonia
dotnet add package ProEdit.OpenXml
```

A reporting host typically starts with:

```bash
dotnet add package ProEdit.Reporting.Avalonia.Viewer
dotnet add package ProEdit.Reporting.Service
dotnet add package ProEdit.Reporting.Export
```

Smaller embedded document hosts can use the shared Skia controls:

```bash
dotnet add package ProEdit.Controls.Skia.Avalonia
dotnet add package ProEdit.Controls.Skia.Uno
dotnet add package ProEdit.Controls.Skia.Maui
```

## Build From Source

Prerequisites:

- .NET SDK `10.0.100` or newer compatible SDK, as defined in `global.json`.
- Optional Uno and MAUI workloads for WinUI/Uno compatibility projects and the MAUI shared-control sample on clean machines:

```bash
dotnet workload install wasm-tools wasm-experimental maui-android
# On macOS, use the full MAUI workload when building iOS or Mac Catalyst heads.
dotnet workload install wasm-tools wasm-experimental maui
```

Build and test the full solution:

```bash
dotnet restore ProEdit.slnx
dotnet build ProEdit.slnx
dotnet test ProEdit.slnx
```

Run the primary desktop applications:

```bash
dotnet run --project src/ProEdit.Word.App/ProEdit.Word.App.csproj
dotnet run --project src/ProEdit.Reporting.App/ProEdit.Reporting.App.csproj
dotnet run --project src/ProEdit.FlowDocument.App/ProEdit.FlowDocument.App.csproj
```

Run the shared control samples:

```bash
dotnet run --project samples/ProEdit.Controls.Skia.Avalonia.Sample/ProEdit.Controls.Skia.Avalonia.Sample.csproj
dotnet run --project samples/ProEdit.Controls.Skia.Uno.Sample/ProEdit.Controls.Skia.Uno.Sample.csproj
dotnet build -f net10.0-maccatalyst samples/ProEdit.Controls.Skia.Maui.Sample/ProEdit.Controls.Skia.Maui.Sample.csproj
```

Run benchmarks:

```bash
dotnet run -c Release --project benchmarks/ProEdit.Layout.Benchmarks/ProEdit.Layout.Benchmarks.csproj
```

Build and serve documentation:

```bash
dotnet tool restore
./build-docs.sh
./serve-docs.sh
```

On Windows:

```powershell
dotnet tool restore
./build-docs.ps1
./serve-docs.ps1
```

## Development Quality Bar

- `Directory.Build.props` centralizes .NET 10, nullable reference types, implicit usings, package metadata, symbol packages, repository metadata, and MIT licensing.
- CI builds and tests on Ubuntu, Windows, and macOS, then packs NuGet artifacts.
- The test suite covers document model behavior, layout, FlowDocument conversion, rich text editing, Open XML, Markdown, HTML, PDF integration, printing, reporting, collaboration, VBA runtime, WinUI compatibility, and Avalonia headless UI surfaces.
- BenchmarkDotNet coverage exists for layout and large-document synchronization paths.
- DocFX documentation is built from the repository and deployed through the docs workflow.

## Documentation

- [Getting Started](docfx/articles/getting-started.md)
- [Architecture](docfx/articles/architecture.md)
- [Project Map](docfx/articles/projects.md)
- [FlowDocument](docfx/articles/flow-document.md)
- [RichTextBox](docfx/articles/richtextbox.md)
- [Shared Skia Controls](docfx/articles/shared-controls.md)
- [Build and Test](docfx/articles/build-and-test.md)
- [Contributing](docfx/articles/contributing.md)

## License

ProEdit is licensed under the [MIT License](LICENSE).
