# Benchmarks

Run layout performance baselines with:

```bash
dotnet run -c Release -p benchmarks/Vibe.Office.Layout.Benchmarks/Vibe.Office.Layout.Benchmarks.csproj
```

Run RichText mirror sync benchmarks with:

```bash
dotnet run -c Release --project benchmarks/Vibe.Office.Layout.Benchmarks/Vibe.Office.Layout.Benchmarks.csproj -- --filter "*FlowDocumentSyncBenchmarks*"
```
