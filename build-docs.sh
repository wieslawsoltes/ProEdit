#!/bin/bash
export IsDocFxBuild=true
dotnet build -c Release 
unset IsDocFxBuild
export IsDocFxMetadata=true
dotnet tool restore
dotnet docfx docfx/docfx.json
unset IsDocFxMetadata
rm -rf src/ProEdit.Printing.Avalonia/Generated
rm -rf src/ProEdit.Word.Avalonia/Generated
