# Build and Test

## Build

```bash
dotnet build ProEdit.slnx
```

## Test

```bash
dotnet test ProEdit.slnx
```

## Benchmarks

```bash
dotnet run --project benchmarks/ProEdit.Layout.Benchmarks/ProEdit.Layout.Benchmarks.csproj
```

## Docs

```bash
./build-docs.sh
```

```bash
./serve-docs.sh
```

On Windows:

```powershell
./build-docs.ps1
```

```powershell
./serve-docs.ps1
```
