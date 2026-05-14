# Benchmarks

Run layout performance baselines with:

```bash
dotnet run -c Release -p benchmarks/ProEdit.Layout.Benchmarks/ProEdit.Layout.Benchmarks.csproj
```

Run RichText mirror sync benchmarks with:

```bash
dotnet run -c Release --project benchmarks/ProEdit.Layout.Benchmarks/ProEdit.Layout.Benchmarks.csproj -- --filter "*FlowDocumentSyncBenchmarks*"
```
