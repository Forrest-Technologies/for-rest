# Third-Party Notices

This repository currently uses the third-party components listed below. Exact versions and review notes are tracked in [`docs/dependencies/dependency-ledger.md`](docs/dependencies/dependency-ledger.md).

## MIT Licensed Components

- CommunityToolkit.Mvvm
- Microsoft.Extensions.* family (including Microsoft.Extensions.Http and Microsoft.Extensions.Hosting)
- Microsoft.Extensions.AI, Microsoft.Extensions.AI.Abstractions, Microsoft.Extensions.AI.OpenAI
- Microsoft.Agents.AI.OpenAI
- Microsoft.CodeAnalysis.* family
- Microsoft.Data.Sqlite
- Microsoft.Identity.Client
- Microsoft.Maui.Controls and Microsoft.Maui.Essentials
- ModelContextProtocol
- monaco-editor
- Azure.AI.OpenAI
- OpenAI (official .NET SDK)
- System.Security.Cryptography.ProtectedData
- MSTest and Microsoft.Testing.* families
- Newtonsoft.Json
- Vendored .NET runtime and Roslyn reference assemblies in `src/ForRest.Maui/roslyn-runtime/` — MIT, from the [dotnet/runtime](https://github.com/dotnet/runtime) and [dotnet/roslyn](https://github.com/dotnet/roslyn) projects (see the README in that directory)

## Apache-2.0 Licensed Components

- SQLitePCLRaw.* family

## CC-BY-4.0 Licensed Components

- Codicon icon font (`codicon.ttf`, shipped with the bundled Monaco editor at `src/ForRest.Maui/Resources/Raw/monaco/vs/base/browser/ui/codicons/codicon/codicon.ttf`): (c) Microsoft Corporation, from [microsoft/vscode-codicons](https://github.com/microsoft/vscode-codicons), licensed under the [Creative Commons Attribution 4.0 International License](https://creativecommons.org/licenses/by/4.0/). Attribution is required and is provided by this notice.

## SIL Open Font License 1.1 Components

- Open Sans fonts (`src/ForRest.Maui/Resources/Fonts/OpenSans-Regular.ttf`, `OpenSans-Semibold.ttf`): (c) The Open Sans Project Authors, licensed under the [SIL Open Font License, Version 1.1](https://openfontlicense.org/) (see [Open Sans on Google Fonts](https://fonts.google.com/specimen/Open+Sans/license)).

## Other Approved Licenses and Platform Terms

- Microsoft.Web.WebView2: BSD-style Microsoft license text included in the package
- Microsoft.WindowsAppSDK: Microsoft Windows App SDK license terms
- Microsoft.Windows.SDK.BuildTools: Windows SDK license terms

## Notice

Third-party software is provided under its respective license terms. The current project review has not identified copyleft runtime dependencies in the app deliverable.
