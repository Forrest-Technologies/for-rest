# For-Rest

For-Rest is a local-first, Windows-native REST client and API testing workbench built with WinUI 3 and .NET 10.

## Build

```powershell
dotnet build ForRest.slnx
dotnet test tests\ForRest.Tests\ForRest.Tests.csproj
```

## Run

```powershell
Start-Process .\src\ForRest.App\bin\Debug\net10.0-windows10.0.19041.0\ForRest.App.exe
```

## Documentation

- [`CODEX.md`](CODEX.md)
- [`docs/architecture/for-rest-architecture.md`](docs/architecture/for-rest-architecture.md)
- [`docs/architecture/mvp-progress.md`](docs/architecture/mvp-progress.md)
- [`docs/dependencies/dependency-ledger.md`](docs/dependencies/dependency-ledger.md)
