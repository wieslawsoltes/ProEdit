#!/bin/bash
export IsDocFxBuild=true
dotnet build -c Release 
unset IsDocFxBuild
export IsDocFxMetadata=true
dotnet tool restore
dotnet docfx docfx/docfx.json
unset IsDocFxMetadata
rm -rf src/Vibe.Office.Printing.Avalonia/Generated
rm -rf src/Vibe.Word.Avalonia/Generated
