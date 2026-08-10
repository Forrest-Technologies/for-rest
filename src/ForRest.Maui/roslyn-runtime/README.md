# Vendored .NET Runtime and Roslyn Reference Assemblies

This directory contains reference/runtime assemblies from the .NET base class
library and related packages, vendored from the official .NET SDK and NuGet
packages published by the [dotnet/runtime](https://github.com/dotnet/runtime)
and [dotnet/roslyn](https://github.com/dotnet/roslyn) projects.

They are packaged into the app as Android assets so the Roslyn script engine
(`ForRest.Scripting`) can resolve assembly references at runtime on devices,
where no .NET SDK or reference-assembly pack is available on disk.

All assemblies here are licensed under the MIT License by the .NET Foundation
and Microsoft. See [THIRD-PARTY-NOTICES.md](../../../THIRD-PARTY-NOTICES.md)
at the repository root.
