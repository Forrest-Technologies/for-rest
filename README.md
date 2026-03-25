# For-Rest

For-Rest is a local-first REST client and API testing workbench built with .NET 10 and .NET MAUI. The current app targets Windows (`x64`, `arm64`) and Android (`arm64`, `x64` emulator builds).

## Build

```powershell
dotnet build ForRest.slnx
dotnet test tests\ForRest.Tests\ForRest.Tests.csproj
dotnet test tests\ForRest.Maui.Tests\ForRest.Maui.Tests.csproj
```

## Run

```powershell
Start-Process .\src\ForRest.App\bin\Debug\net10.0-windows10.0.19041.0\ForRest.App.exe
```

## Run Android

```powershell
dotnet build .\src\ForRest.Maui\ForRest.Maui.csproj -f net10.0-android
dotnet publish .\src\ForRest.Maui\ForRest.Maui.csproj -f net10.0-android -c Release
```

GitHub Actions:

- `windows-maui-build.yml` builds and releases Windows artifacts.
- `android-maui-build.yml` builds Android packages (`.apk`, `.aab`), runs MAUI unit tests, uploads artifacts, and appends Android assets to the tagged GitHub release on `master`.

## Documentation

- [`CODEX.md`](CODEX.md)
- [`docs/architecture/for-rest-architecture.md`](docs/architecture/for-rest-architecture.md)
- [`docs/architecture/mvp-progress.md`](docs/architecture/mvp-progress.md)
- [`docs/dependencies/dependency-ledger.md`](docs/dependencies/dependency-ledger.md)
