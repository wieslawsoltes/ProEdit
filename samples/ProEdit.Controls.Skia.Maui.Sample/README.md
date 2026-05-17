# ProEdit.Controls.Skia.Maui.Sample

.NET MAUI sample for `ProEdit.Controls.Skia.Maui`.

Build a platform head:

```bash
dotnet build samples/ProEdit.Controls.Skia.Maui.Sample/ProEdit.Controls.Skia.Maui.Sample.csproj -f net10.0-maccatalyst
dotnet build samples/ProEdit.Controls.Skia.Maui.Sample/ProEdit.Controls.Skia.Maui.Sample.csproj -f net10.0-android
dotnet build samples/ProEdit.Controls.Skia.Maui.Sample/ProEdit.Controls.Skia.Maui.Sample.csproj -f net10.0-ios
```

Apple target frameworks are enabled by default on macOS. Linux and Windows default to `net10.0-android`; pass `-p:EnableMauiAppleTargets=true` on macOS to force the full MAUI target set when a build script overrides defaults.

The sample displays `ProEditDocumentViewer` and `ProEditDocumentEditor` in one scrollable page and binds the same shared sample ViewModel used by the Avalonia and Uno heads.
