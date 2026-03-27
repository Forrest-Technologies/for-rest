# Dependency Ledger

Reviewed from `dotnet list ForRest.slnx package --include-transitive` on 2026-03-16.

## Runtime and Build Dependencies

| Package or family | Purpose | Version | License | Source | Commercial suitability | Replacement risk |
| --- | --- | --- | --- | --- | --- | --- |
| `CommunityToolkit.Mvvm` | MVVM base types and commands for the WinUI shell | 8.4.0 | MIT | [github.com/CommunityToolkit/dotnet](https://github.com/CommunityToolkit/dotnet) | Approved | Low |
| `Microsoft.Extensions.*` family | hosting, DI, logging, configuration, options, primitives, diagnostics | 10.0.0 | MIT | [github.com/dotnet/dotnet](https://github.com/dotnet/dotnet) | Approved | Low to medium |
| `Microsoft.CodeAnalysis.*` scripting family | Roslyn-based script compilation and execution | 4.14.0 | MIT | [github.com/dotnet/roslyn](https://github.com/dotnet/roslyn) | Approved | Medium to high because the scripting API is core to the product |
| `Microsoft.Data.Sqlite` and `Microsoft.Data.Sqlite.Core` | SQLite data access layer | 10.0.0 | MIT | [docs.microsoft.com/dotnet/standard/data/sqlite](https://docs.microsoft.com/dotnet/standard/data/sqlite/) | Approved | Medium |
| `monaco-editor` | packaged local Monaco assets hosted inside the MAUI WebView editor surface | 0.38.0 | MIT | [github.com/microsoft/monaco-editor](https://github.com/microsoft/monaco-editor) | Approved | Medium because the editor UI and custom language integration depend on Monaco APIs |
| `SQLitePCLRaw.*` family | native SQLite interop for `Microsoft.Data.Sqlite` | 2.1.11 | Apache-2.0 | [github.com/ericsink/SQLitePCL.raw](https://github.com/ericsink/SQLitePCL.raw) | Approved | Medium |
| `System.Security.Cryptography.ProtectedData` | DPAPI wrapper for local secret protection | 10.0.0 | MIT | [github.com/dotnet/dotnet](https://github.com/dotnet/dotnet) | Approved | Low |
| `Microsoft.WindowsAppSDK` | WinUI 3 runtime and Windows desktop platform APIs | 1.7.250909003 | Microsoft Windows App SDK license terms | [github.com/microsoft/windowsappsdk](https://github.com/microsoft/windowsappsdk) | Acceptable for Windows-native commercial distribution, but not a permissive OSS license | High |
| `Microsoft.Web.WebView2` | transitively available browser/runtime surface pulled by the Windows app stack | 1.0.2903.40 | BSD-style Microsoft license text in package | [aka.ms/webview](https://aka.ms/webview) | Approved | Medium |
| `Microsoft.Windows.SDK.BuildTools` | Windows SDK packaging and build tooling | 10.0.22621.756 | Windows SDK license terms | [aka.ms/WinSDKProjectURL](https://aka.ms/WinSDKProjectURL) | Acceptable as a Windows build dependency, but not a permissive OSS license | High |

## Test-Only Dependencies

| Package or family | Purpose | Version | License | Source | Commercial suitability | Replacement risk |
| --- | --- | --- | --- | --- | --- | --- |
| `MSTest`, `MSTest.TestFramework`, `MSTest.TestAdapter`, `MSTest.Analyzers` | unit test framework and adapters | 4.0.2 | MIT | [github.com/microsoft/testfx](https://github.com/microsoft/testfx) | Approved, test-only | Low |
| `Microsoft.Testing.*` family and `Microsoft.NET.Test.Sdk` | test execution platform and MSBuild integration | 2.0.2 / 18.0.1 | MIT | [github.com/microsoft/testfx](https://github.com/microsoft/testfx) | Approved, test-only | Low |
| `Microsoft.CodeCoverage` and `Microsoft.Testing.Extensions.CodeCoverage` | code coverage collection | 18.0.1 / 18.1.0 | MIT | [github.com/microsoft/testfx](https://github.com/microsoft/testfx) | Approved, test-only | Low |
| `Microsoft.ApplicationInsights` | telemetry plumbing brought by the modern MSTest stack | 2.23.0 | MIT | [github.com/microsoft/ApplicationInsights-dotnet](https://github.com/microsoft/ApplicationInsights-dotnet) | Approved, test-only, not shipped in app runtime | Low |
| `Newtonsoft.Json` | test-platform serialization dependency | 13.0.3 | MIT | [github.com/JamesNK/Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) | Approved, test-only | Low |

## Notes

- No GPL, AGPL, or LGPL dependencies are currently resolved.
- The only non-permissive entries are Windows platform packages required to build and run a WinUI 3 application on Windows.
- Before shipping, refresh this ledger against the exact locked package graph for the release branch.
