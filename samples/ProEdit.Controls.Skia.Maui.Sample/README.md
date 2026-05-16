# ProEdit.Controls.Skia.Maui.Sample

.NET MAUI sample for `ProEdit.Controls.Skia.Maui`.

Build a platform head:

```bash
dotnet build samples/ProEdit.Controls.Skia.Maui.Sample/ProEdit.Controls.Skia.Maui.Sample.csproj -f net10.0-maccatalyst
dotnet build samples/ProEdit.Controls.Skia.Maui.Sample/ProEdit.Controls.Skia.Maui.Sample.csproj -f net10.0-android
dotnet build samples/ProEdit.Controls.Skia.Maui.Sample/ProEdit.Controls.Skia.Maui.Sample.csproj -f net10.0-ios
```

The sample displays `ProEditDocumentViewer` and `ProEditDocumentEditor` in one scrollable page and binds the same shared sample ViewModel used by the Avalonia and Uno heads.
