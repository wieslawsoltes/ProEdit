# Shared Skia Controls

The `ProEdit.Controls.Skia` packages provide smaller embeddable document controls
for applications that need viewing or editing without hosting the full ProEdit
Word application shell. They reuse the same document model, layout engine,
Skia rendering backend, and Word editor services instead of reimplementing
pagination, selection, pan, zoom, keyboard input, or edit command behavior.

## Packages

| Host framework | Package | Controls |
| --- | --- | --- |
| Shared core | `ProEdit.Controls.Skia` | `ProEditDocumentHost`, options, command/state contracts |
| Avalonia | `ProEdit.Controls.Skia.Avalonia` | `ProEditDocumentViewer`, `ProEditDocumentEditor` |
| Uno Platform | `ProEdit.Controls.Skia.Uno` | `ProEditDocumentViewer`, `ProEditDocumentEditor` |
| .NET MAUI | `ProEdit.Controls.Skia.Maui` | `ProEditDocumentViewer`, `ProEditDocumentEditor` |

Use the viewer control for read-only surfaces and the editor control when the
host should accept text input. Both controls expose the same high-level feature
set through framework-native properties:

- `Document`
- `Zoom`, `ZoomMode`, `MultiplePagesPerRow`
- `ViewMode`, `PageFlow`, `UsePagination`
- `ShowInvisibles`, `ShowLayout`, `ShowGridlines`
- `IsPanEnabled`
- editor-only `IsReadOnly`, `ShowReadOnlyCaret`, `AcceptsReturn`, `AcceptsTab`

## Avalonia

Install the Avalonia package in an Avalonia app:

```bash
dotnet add package ProEdit.Controls.Skia.Avalonia
```

Add the namespace and bind the control to a ViewModel property containing a
`ProEdit.Documents.Document`:

```xml
<controls:ProEditDocumentViewer
    xmlns:controls="clr-namespace:ProEdit.Controls.Skia.Avalonia;assembly=ProEdit.Controls.Skia.Avalonia"
    Document="{Binding Document}"
    Zoom="{Binding Zoom, Mode=TwoWay}"
    ZoomMode="{Binding ZoomMode, Mode=TwoWay}"
    ShowLayout="{Binding ShowLayout}"
    ShowGridlines="{Binding ShowGridlines}"
    IsPanEnabled="{Binding IsPanEnabled}" />
```

Use `ProEditDocumentEditor` for editing:

```xml
<controls:ProEditDocumentEditor
    xmlns:controls="clr-namespace:ProEdit.Controls.Skia.Avalonia;assembly=ProEdit.Controls.Skia.Avalonia"
    Document="{Binding Document}"
    IsReadOnly="{Binding IsReadOnly}"
    AcceptsReturn="True"
    AcceptsTab="True"
    Zoom="{Binding Zoom, Mode=TwoWay}"
    ZoomMode="{Binding ZoomMode, Mode=TwoWay}" />
```

Run the sample:

```bash
dotnet run --project samples/ProEdit.Controls.Skia.Avalonia.Sample/ProEdit.Controls.Skia.Avalonia.Sample.csproj
```

## Uno Platform

Install the Uno package in an Uno Platform app:

```bash
dotnet add package ProEdit.Controls.Skia.Uno
```

Use the WinUI namespace syntax:

```xml
<controls:ProEditDocumentViewer
    xmlns:controls="using:ProEdit.Controls.Skia.Uno"
    Document="{Binding Document}"
    Zoom="{Binding Zoom, Mode=TwoWay}"
    ZoomMode="{Binding ZoomMode, Mode=TwoWay}"
    UsePagination="{Binding UsePagination}"
    PageFlow="{Binding PageFlow}"
    IsPanEnabled="{Binding IsPanEnabled}" />
```

Use `ProEditDocumentEditor` for editing:

```xml
<controls:ProEditDocumentEditor
    xmlns:controls="using:ProEdit.Controls.Skia.Uno"
    Document="{Binding Document}"
    IsReadOnly="{Binding IsReadOnly}"
    AcceptsReturn="True"
    AcceptsTab="True"
    Zoom="{Binding Zoom, Mode=TwoWay}"
    ZoomMode="{Binding ZoomMode, Mode=TwoWay}" />
```

Run the Skia desktop sample:

```bash
dotnet run --project samples/ProEdit.Controls.Skia.Uno.Sample/ProEdit.Controls.Skia.Uno.Sample.csproj
```

## .NET MAUI

Install the MAUI package in a MAUI app:

```bash
dotnet add package ProEdit.Controls.Skia.Maui
```

Use the MAUI CLR namespace syntax:

```xml
<controls:ProEditDocumentViewer
    xmlns:controls="clr-namespace:ProEdit.Controls.Skia.Maui;assembly=ProEdit.Controls.Skia.Maui"
    Document="{Binding Document}"
    HeightRequest="480"
    Zoom="{Binding Zoom, Mode=TwoWay}"
    ZoomMode="{Binding ZoomMode, Mode=TwoWay}"
    ShowLayout="{Binding ShowLayout}"
    IsPanEnabled="{Binding IsPanEnabled}" />
```

Use `ProEditDocumentEditor` for editing:

```xml
<controls:ProEditDocumentEditor
    xmlns:controls="clr-namespace:ProEdit.Controls.Skia.Maui;assembly=ProEdit.Controls.Skia.Maui"
    Document="{Binding Document}"
    HeightRequest="520"
    IsReadOnly="{Binding IsReadOnly}"
    AcceptsReturn="True"
    AcceptsTab="True"
    Zoom="{Binding Zoom, Mode=TwoWay}"
    ZoomMode="{Binding ZoomMode, Mode=TwoWay}" />
```

Build the MAUI sample for Mac Catalyst:

```bash
dotnet build -f net10.0-maccatalyst samples/ProEdit.Controls.Skia.Maui.Sample/ProEdit.Controls.Skia.Maui.Sample.csproj
```

## Sample ViewModel

The sample apps all use `samples/ProEdit.Controls.Skia.Sample.Shared`. The
shared ViewModel demonstrates the intended host contract:

- Keep the document and control state in a framework-neutral ViewModel.
- Bind framework controls to `Document`, zoom, view mode, pagination, and flags.
- Route toolbar actions through `ReactiveCommand`.
- Keep app-specific commands outside the controls; the shared host handles
  rendering, input, selection, and editing behavior internally.

This keeps host applications thin while allowing Avalonia, Uno, and MAUI to
expose idiomatic XAML APIs.

Previous: [RichTextBox](richtextbox.md)

Next: [Projects](projects.md)
