# License Review

Date: 2026-03-16

## Reviewed Outcome

The current dependency set is acceptable for the initial For-Rest foundation with one explicit caveat: the Windows platform stack necessarily brings Microsoft platform license terms that are not purely permissive open-source licenses.

## Approved Without Exception

These families are permissive and compatible with distributing For-Rest as MIT-licensed open source:

- `CommunityToolkit.Mvvm` under MIT
- `Microsoft.Extensions.*` under MIT
- `Microsoft.CodeAnalysis.*` under MIT
- `Microsoft.Data.Sqlite` under MIT
- `System.Security.Cryptography.ProtectedData` under MIT
- `SQLitePCLRaw.*` under Apache-2.0
- `MSTest` and `Microsoft.Testing.*` under MIT
- `Newtonsoft.Json` under MIT

## Approved With Windows Platform Exception

These packages are required by the chosen Windows-native stack and are acceptable for this project, but they are not interchangeable with standard permissive OSS packages:

- `Microsoft.WindowsAppSDK`
- `Microsoft.Windows.SDK.BuildTools`

Decision:

- allow them in the repo because the product is intentionally WinUI 3 and Windows-native
- keep them clearly separated in the dependency ledger
- re-check redistribution obligations before every external release

## Special Review Notes

### `Microsoft.Web.WebView2`

- Current package license text is BSD-style and compatible with open-source distribution.
- The app does not currently depend on WebView-specific features directly.
- Keep monitoring this transitively included package when Windows App SDK versions change.

### Roslyn Scripting

- License is permissive.
- Operationally, this is a higher-risk dependency than its license suggests because it is a product pillar.
- If the script host ever becomes unstable or abandoned, replacement cost is significant.

## Disallowed Classes

The following license families remain disallowed unless explicitly approved in a future ADR and legal review:

- GPL
- AGPL
- LGPL
- SSPL or similar field-of-use restricted licenses

## Release Checklist

Before the first distributable build:

1. Refresh `dotnet list package --include-transitive`.
2. Update the dependency ledger with exact release versions.
3. Refresh `THIRD-PARTY-NOTICES.md`.
4. Verify Windows App SDK and Windows SDK redistribution obligations for the chosen packaging model.
5. Confirm no new non-permissive dependencies entered through transitive updates.
