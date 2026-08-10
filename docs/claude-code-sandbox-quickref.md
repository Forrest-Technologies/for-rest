# Claude Code Sandbox Quick Reference

Scratchpad for fixing the recurring environment issues Claude Code runs into
when iterating on this repo from the cloud sandbox. Written after the Grok
provider fix, so future sessions don't lose the same hour twice.

## Symptom: `dotnet` is not on PATH

The sandbox image does not ship the .NET SDK. Install it from the Ubuntu
universe repo (the Claude Code proxy allow-list already permits
`archive.ubuntu.com`, `security.ubuntu.com`, and `packages.microsoft.com`):

```bash
sudo apt-get install -y dotnet-sdk-10.0
```

That installs the latest feature-band build (e.g. `10.0.105`). The repo's
`global.json` pins a preview feature band (`10.0.200-preview.*`) with
`rollForward: latestFeature`, which will refuse to use the installed SDK.
Temporarily move `global.json` aside for the test run only, then restore it
before committing:

```bash
mv global.json global.json.bak
# build, test, etc.
mv global.json.bak global.json
```

Never commit `global.json` in its moved state.

## Symptom: `apt-get` hangs forever or fails DNS

The sandbox injects an HTTP proxy via `GLOBAL_AGENT_HTTP_PROXY`, but `apt`
does not honour that env var. Without a config file, `apt` tries to talk to
the package mirrors directly, can't resolve DNS, and either hangs or prints
`Temporary failure resolving '...'`. Fix by writing the proxy into apt's own
config:

```bash
echo "Acquire::http::Proxy \"$GLOBAL_AGENT_HTTP_PROXY\";"  | sudo tee /etc/apt/apt.conf.d/99proxy-http  > /dev/null
echo "Acquire::https::Proxy \"$GLOBAL_AGENT_HTTP_PROXY\";" | sudo tee /etc/apt/apt.conf.d/99proxy-https > /dev/null
sudo apt-get update
```

Both files are required because the apt mirror metadata is fetched over
https, while some content URLs are http. The existing user-agent overrides
in `/etc/apt/apt.conf.d/01-vendor-ubuntu` do not conflict.

## Symptom: `dotnet build` fails with `EnableWindowsTargeting` or `maui-tizen`

The solution targets `net10.0-windows10.0.19041.0` and expects MAUI workloads.
The MAUI test project wants Windows targeting, which non-Windows hosts
refuse unless it is opted into explicitly.

`Directory.Build.props` now sets `EnableWindowsTargeting=true` automatically
whenever the build host is not Windows, so the `-p:EnableWindowsTargeting=true`
override below is no longer required — it is kept here because passing it
anyway is harmless and older notes still reference it.

- For the non-MAUI test project, build directly — it works as-is:
  ```bash
  dotnet build tests/ForRest.Tests/ForRest.Tests.csproj
  ```
- For the MAUI test project, install the workload once per session:
  ```bash
  dotnet workload restore tests/ForRest.Maui.Tests/ForRest.Maui.Tests.csproj
  dotnet build tests/ForRest.Maui.Tests/ForRest.Maui.Tests.csproj
  dotnet test  tests/ForRest.Maui.Tests/ForRest.Maui.Tests.csproj --no-build
  ```

## Symptom: `Test Run Aborted` at the end of `ForRest.Maui.Tests`

The MAUI test project pulls WinRT interop assemblies that register a
finalizer (`WinRT.WinRTModule.Finalize`) calling
`CoDecrementMTAUsage`. On Linux, `api-ms-win-core-com-l1-1-0.dll` does not
exist, so the finalizer throws `DllNotFoundException` during process
teardown after the test run has already completed. The harness reports
`Test Run Aborted.` even though every test actually passed.

- Don't chase the abort — check `Passed:` / `Failed:` instead.
- If you need a clean exit (e.g. for a gating script), filter the suite into
  smaller batches so the runner terminates before the WinRT finalizer trips:
  ```bash
  dotnet test tests/ForRest.Maui.Tests/ForRest.Maui.Tests.csproj --no-build \
    -p:EnableWindowsTargeting=true \
    --filter "FullyQualifiedName~ForRest.Maui.Tests.AI"
  ```
- The real CI is Windows (`D:\a\for-rest\...` in the runner logs), where the
  finalizer works correctly and all tests run in a single invocation.

## Symptom: Pre-existing `SqliteRepositoryTests` failure on Linux

`ForRest.Tests.Infrastructure.SqliteRepositoryTests.Workspace_repository_round_trips_state_and_protects_secret_values`
uses `System.Security.Cryptography.ProtectedData`, which is Windows-only
(DPAPI). Expect this single failure on Linux. Do not fix it as part of an
unrelated change — on Windows CI it passes.

## MSTest discovery gotcha

Several test classes in `tests/ForRest.Maui.Tests/AI/*.cs` omit the
`[TestClass]` attribute, so MSTest 4 silently skips them even though builds
succeed and the MSTEST0030 warnings are emitted. If a test you just added
isn't running, check the class attribute before debugging the test filter.

## Useful one-liner: full sanity build + test on Linux

```bash
mv global.json global.json.bak \
  && dotnet build tests/ForRest.Tests/ForRest.Tests.csproj \
  && dotnet test  tests/ForRest.Tests/ForRest.Tests.csproj --no-build \
  && dotnet build tests/ForRest.Maui.Tests/ForRest.Maui.Tests.csproj -p:EnableWindowsTargeting=true \
  && dotnet test  tests/ForRest.Maui.Tests/ForRest.Maui.Tests.csproj --no-build -p:EnableWindowsTargeting=true \
        --filter "FullyQualifiedName~ForRest.Maui.Tests.AI|FullyQualifiedName~Settings|FullyQualifiedName~Theme" \
  ; mv global.json.bak global.json
```

Remember: the trailing `mv` restores `global.json` even if the tests fail,
which is why it's after `;` rather than `&&`.
