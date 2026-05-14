# Reporting Desktop App Plan

Date: 2026-03-17

## Goal

Create a dedicated Avalonia desktop application for ProEdit reporting that exposes:

- the native report designer
- the full report viewer
- native template load/save for `*.vreport.json`
- RDL import/export
- a working sample report and sample data environment on startup
- a clean shell for future repository, scheduling, delivery, and MCP integration

## Product Shape

The new app should be a standalone desktop shell, separate from `ProEdit.Word.App`, centered on the existing reporting controls:

- `ProEdit.Reporting.Avalonia.Designer`
- `ProEdit.Reporting.Avalonia.Viewer`

The shell should not reimplement reporting runtime logic. It should orchestrate:

- file operations
- report-source creation
- sample connector registration
- designer/viewer host state
- startup argument handling

## Host Responsibilities

### Shell

Provide a desktop shell with:

- command bar for new/open/save/import/export/refresh
- current document path and dirty-state display
- designer workspace tab
- full viewer workspace tab
- status surface for file/runtime feedback

### Sample Runtime

On startup, the app should open into a working report, not an empty definition. The default sample should demonstrate:

- parameters
- in-memory dataset execution
- tablix rendering
- chart rendering
- document-template narrative content
- drillthrough to a referenced subreport

### File Workflows

Support these user workflows:

- `New Sample`
- `Open Template` from `*.vreport.json`
- `Save Template`
- `Save Template As`
- `Import RDL`
- `Export RDL`
- `Refresh Preview`

### Extensibility

The app should keep seams for future expansion:

- repository browser
- scheduled run management
- delivery target management
- connector setup UI
- MCP host surface

## Architecture

### Projects

- `src/ProEdit.Reporting.App`
  - Avalonia desktop app host
  - shell view model
  - file picker service
  - sample report/data factory
  - window and app resources
- `tests/ProEdit.Reporting.App.Headless.Tests`
  - shell smoke coverage with Avalonia Headless

### Runtime Composition

The shell view model should own:

- one `ReportDesignerViewModel`
- the designer's embedded `PreviewViewModel` as the standalone viewer source
- serializer services
- RDL serializer services
- file picker service

This keeps one live editable report source and one live preview/viewer state instead of duplicating report state across multiple view models.

### MVVM Boundaries

- XAML views remain passive
- the shell view model handles report lifecycle and commands
- file operations stay behind a picker service abstraction
- sample report/data creation stays in a dedicated factory/helper

## Validation

### Focused Runtime Validation

- build the new app project
- run the new headless app tests
- re-run the existing reporting headless tests to ensure the host does not regress viewer/designer behavior

### Minimum Acceptance Bar

- app starts with a working sample report
- designer tab is editable
- viewer tab shows the same report preview
- template save/load works
- RDL import/export works
- the app project builds cleanly
- headless shell smoke tests pass

## Follow-Up After Initial Delivery

- add repository browser and service-layer execution panel
- add named-connector management UI
- add startup command-line switches for open/export/run
- add recent-files and persisted workspace state
- add screenshot-based UI regression coverage
