$env:IsDocFxBuild = 'true'
dotnet tool restore
dotnet build -c Release
$env:IsDocFxBuild = $null
$env:IsDocFxMetadata = 'true'
dotnet docfx docfx/docfx.json
$env:IsDocFxMetadata = $null
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue src/Vibe.Office.Printing.Avalonia/Generated
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue src/Vibe.Word.Avalonia/Generated
