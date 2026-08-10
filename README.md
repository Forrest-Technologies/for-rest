<div align="center">

# For-Rest

### The API workbench for people who already live in an IDE.

**Local-first. Code-first. AI-native. Agent-ready.**
No account, no cloud, no telemetry tax — your requests are real source files and your secrets never leave your machine.

![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![.NET MAUI](https://img.shields.io/badge/.NET-MAUI-5C2D91)
![Windows](https://img.shields.io/badge/Windows-x64%20%7C%20arm64-0078D6?logo=windows&logoColor=white)
![Android](https://img.shields.io/badge/Android-arm64%20%7C%20x64-3DDC84?logo=android&logoColor=white)
![macOS](https://img.shields.io/badge/macOS-Mac%20Catalyst-000000?logo=apple&logoColor=white)
![Local-first](https://img.shields.io/badge/storage-local--first%20SQLite-44883e)
![MCP](https://img.shields.io/badge/MCP-server%20built--in-FF6B35)
![License: MIT](https://img.shields.io/badge/license-MIT-yellow)

</div>

---

> **The 30-second pitch.** A request in For-Rest is a small program in a language built for exactly one job — the HTTP loop: *send → inspect → extract → assert → repeat.* That file is the single artifact a **human reads**, an **AI edits**, and the **runtime replays** — deterministically, offline, with your secrets encrypted on disk. Postman is for filling out forms. For-Rest is for **engineering** API workflows, and for letting an agent run them for you.

---

## Why For-Rest?

The usual question is *"why not just use Postman or Insomnia?"* — but that frames it backwards. The real question is:

> **Why does firing an HTTP request require a login screen, a cloud account, and a pricing tier?**

Postman solved a 2015 problem — *teams need to share collections* — by putting everything on their servers. Insomnia followed. Along the way, your request history, your internal endpoints, and your bearer tokens all became someone else's data. The de facto tools quietly turned into SaaS funnels with a request composer bolted on top.

**For-Rest makes the opposite bet.** It's a fast, text-forward, three-pane desktop and mobile app that treats API work like software engineering — not form-filling. That single decision cascades into everything below.

| | **For-Rest** | Postman / Insomnia |
|---|---|---|
| **Storage** | Local-first SQLite — no account, no sync, no backend | Cloud-synced, account required |
| **Request format** | Plain-text `.frs` source files — git-diffable, code-reviewable | Proprietary JSON/YAML blobs |
| **Authoring** | A real language: loops, branches, retries, extraction, assertions | Forms + a limited JS sandbox |
| **Reuse** | `define`/`call` subroutines, `import`/`use`, variable precedence | Copy-paste + pre-request scripts |
| **Security testing** | Built-in payload corpora + a concurrent fuzz engine with baseline diffing | Add-ons / external tools |
| **Browser flows** | Embedded browser you can **record → replay** with no model in the loop | — |
| **AI** | Inline in the editor, edits your request back, 9+ providers, BYO key | Cloud copilot in a sidebar |
| **Agent access** | **Built-in MCP server** — agents drive the whole workbench | — |
| **Secrets** | Encrypted at the storage boundary; redacted before any AI/agent sees them | Cloud vault |
| **Telemetry** | None | Yes |

---

## Your requests are *code*, not form fields

Open a request and you're not tabbing through dropdowns — you're editing a `.frs` script with full syntax highlighting, completions, and inline diagnostics. Headers, auth, variables, retries, branching, assertions, and response extraction all live as readable text:

```frs
# request.frs — create a user, retry on failure, and verify the result.
method POST
url "{{base_url}}/users"

auth { mode = bearer; token = "{{api_token}}" }
header "Accept" = "application/json"

runtime trace_id = guid()
header "X-Correlation-Id" = "{{trace_id}}"

body json """
{
  "name": "Ada Lovelace",
  "email": "ada@example.com"
}
"""

# Code-first flow: loop, branch, and log like it's a real program.
foreach attempt in [0..2] {
  let sent = request.send()
  if sent.status == 201 {
    log $"Created on attempt {attempt}."
    break
  }
  warn $"Attempt {attempt} returned {sent.status}; retrying."
}

# Self-checking assertions, surfaced in the debug pane and run history.
expect status == 201 "user is created"
expect header "Content-Type" contains "json"

# Pull a value out of the response to feed the next request in your flow.
extract runtime new_user_id = regex body "\"id\":\s*\"([^\"]+)\"" 1
log $"New user id: {new_user_id}"
```

Because a request is just a file, your API calls are **diff-able, git-committable, and code-reviewable**. A Postman collection is an opaque blob you pray merges cleanly. A For-Rest request reads like any other source in your repo.

---

## The `.frs` language — sugar that Python and C# don't give you for API work

You *could* write all of this in Python with `requests`, or in C# with `HttpClient`. People do — and they end up re-implementing the same scaffolding every time: a retry loop, JSON parsing, an assertions helper, a place to stash results, a way to thread a value from one call into the next. `.frs` is that scaffolding, turned into **first-class syntax**. The language is small on purpose: it isn't general-purpose, it's *the HTTP loop, compressed.*

Here's the same "send, retry on failure, pull the id, assert" task three ways:

<table>
<tr><th>Python <code>requests</code></th><th>C# <code>HttpClient</code></th></tr>
<tr><td valign="top">

```python
import requests, time, re

token = os.environ["API_TOKEN"]
body = {"name": "Ada", "email": "ada@x.com"}
new_id = None
for attempt in range(3):
    r = requests.post(
        f"{base}/users",
        json=body,
        headers={"Authorization": f"Bearer {token}"},
        timeout=15,
    )
    if r.status_code == 201:
        break
    time.sleep(2 ** attempt)        # hand-rolled backoff

assert r.status_code == 201, r.text  # crashes the script
m = re.search(r'"id":\s*"([^"]+)"', r.text)
new_id = m.group(1) if m else None
print("new id:", new_id)
```

</td><td valign="top">

```csharp
using var http = new HttpClient();
http.DefaultRequestHeaders.Authorization =
    new("Bearer", token);
HttpResponseMessage? r = null;
for (var attempt = 0; attempt < 3; attempt++)
{
    r = await http.PostAsync($"{baseUrl}/users",
        JsonContent.Create(body));
    if ((int)r.StatusCode == 201) break;
    await Task.Delay(
        TimeSpan.FromSeconds(Math.Pow(2, attempt)));
}
r!.EnsureSuccessStatusCode();
var text = await r.Content.ReadAsStringAsync();
var id = Regex.Match(text,
    "\"id\":\\s*\"([^\"]+)\"").Groups[1].Value;
Console.WriteLine($"new id: {id}");
```

</td></tr>
</table>

…and in `.frs`:

```frs
method POST
url "{{base_url}}/users"
auth { mode = bearer; token = "{{api_token}}" }
body json """{ "name": "Ada", "email": "ada@x.com" }"""

retry 3 with backoff {
  let sent = request.send()
  if sent.status == 201 { break }
}

expect status == 201 "user created"
extract runtime new_user_id = regex body "\"id\":\s*\"([^\"]+)\"" 1
```

The win isn't fewer characters — it's that **the domain is the syntax.** Notice what `.frs` gives you that the others make you build by hand:

- **`retry N with backoff { … }`** and `on status` / `on error` handlers — resilience without a retry library.
- **`expect`** — assertions that *report* into the debug pane and run history instead of throwing and killing the script. Failing a test isn't a crash; it's data.
- **`extract` / `stash`** — pull values out of a response by JSON-path, header, or regex, and capture rows into a table that's saved to history. No glue code.
- **Variable precedence + `{{interpolation}}`** — six resolution scopes (System → Global → Workspace → Environment → Request-local → Runtime). The same script runs against dev, staging, and prod by swapping an environment, not editing the file.
- **`auth { mode = … }` blocks** — bearer, basic, api-key, header, digest, NTLM, negotiate, and OAuth (client-credentials, device-code, integrated Windows) declared in two lines, not assembled from handlers.
- **`parallel { … }` and `pipe { … }`** — fan out concurrent calls or chain sequential ones in a few lines.
- **`define` / `call`, `import` / `use`, `scenario`** — real reuse and named test scenarios that share a base request.
- **LINQ-style collection sugar** — `response.users.where(...).select(...).first()`, ranges (`[0..9]`), and helper namespaces (`json`, `strings`, `regex`, `crypto`, `encoding`, `time`, `convert`).
- **`payloads` / `fuzz`** — a curated security payload catalog and a concurrent fuzz engine, in the language.
- **`browser`** — drive an embedded browser (navigate/click/type/snapshot) for end-to-end flows.

Under the hood there's no sandbox: `.frs` is parsed and **compiled to C# on a Roslyn host**, so it's fast and real — you're getting a focused language *and* the CLR underneath it.

---

## The script is the contract between you and the AI

This is the part that's hard to copy, and the reason For-Rest exists. In most tools the request lives as hidden state — a JSON blob behind a form, with an AI copilot bolted onto the side that can *talk about* your request but can't *be* your request. For-Rest inverts that: **the `.frs` file is the shared surface that both the human and the machine operate on directly.**

```
        you ──edit──▶  ┌─────────────────┐  ◀──edit──  AI / agent
                       │   request.frs   │
        you ──read──▶  │  (plain text)   │  ──drives via MCP──▶ agent
                       └────────┬────────┘
                                │ compile → Roslyn → HTTP
                                ▼
                        deterministic run
                     (snapshot saved to SQLite)
```

Why that middle artifact being *text* changes everything:

- **The AI edits the request, not a chat log.** Type a line beginning with `##` and you've started a conversation inside the document; the model streams its answer inline **and rewrites the script back**. You review a diff, not a paragraph describing a diff.
- **It's a deterministic replay, not a prompt at run time.** The model helps you *author* the script; once written, the runtime executes it with **no LLM in the loop**. A recorded `browser` flow replays the same way every time. That's the difference between "AI that helps you build a test" and "AI you have to trust on every run."
- **Agents drive the real workbench over MCP.** Flip one setting and For-Rest is a **Model Context Protocol** server. Claude (or any MCP client) can `list_workspaces`, `compile_script` to validate an edit *before* running it, `execute_script` against the live pipeline, then `list_runs` / `get_run` / `grep_run_response` to read what came back — the same history you see. The agent isn't simulating your tool; it's using it.
- **Secrets stay yours.** `secret` values are encrypted at the storage boundary and **redacted before any AI provider or MCP client receives the script**. The host restores them only at save/run time. The model can edit a request that *uses* a token without ever seeing the token.
- **The docs are part of the loop.** The same canonical language reference that powers in-editor help is exposed to the AI (`get_instructions`, `search_docs`), with built-in guidance on *how* to wield the language — so the model writes idiomatic `.frs`, not a transliteration of Python.

The future of API testing isn't a human clicking **Send** 200 times — it's an agent running your flows while you read the results. For-Rest is built so the thing in the middle is something you can both understand.

---

## The four pillars

### 🔒 Local-first — your secrets never leave the machine
There's no cloud backend to opt out of, because there isn't one. Workspaces and full execution history persist to local **SQLite**. Secret values are encrypted at the **storage boundary (DPAPI on Windows)** — not hidden behind a UI asterisk — and are **redacted before any AI provider or MCP client ever sees them**. For anyone touching internal or regulated APIs, "the tool physically cannot phone home your tokens" isn't a feature, it's a procurement requirement.

### ⌨️ Code-first — a real scripting language, not a JS escape hatch
The `.frs` language is a first-class DSL compiled through a **Roslyn** host, not a sandbox afterthought. Directives, `auth` blocks, `{{variable}}` interpolation, `let`/`if`/`foreach`/`while`/`switch`, string interpolation, ranges, LINQ-style collection operators, `retry … with backoff`, `on error`/`on status` handlers, `parallel`/`pipe`, `define`/`call`, regex/JSON-path extraction, and helper namespaces for `crypto`, `time`, `encoding`, `strings`, `json`, and `regex`.

### 🤖 AI-native — the model lives *inside* the editor
Type a line that starts with `##` and you've started a conversation, right in the document:

```frs
## add pagination query params and an If-None-Match header for caching
```

Hit send and the answer streams in inline — and the AI can **edit the request back**, not just talk about it. Bring your own key for **OpenAI, Azure OpenAI, Anthropic, Google Gemini, xAI/Grok, Groq, DeepSeek, Mistral, OpenRouter, or any custom endpoint**.

### 🛰️ Agent-ready — For-Rest is itself an MCP server
Flip one setting and For-Rest exposes a **Model Context Protocol** endpoint over Streamable HTTP. Any MCP client can read your request docs, list and edit workspaces, compose, compile, and **execute** requests, and inspect run results — *through your workbench*.

→ See **[docs/mcp/connecting-mcp-clients.md](docs/mcp/connecting-mcp-clients.md)** for the copy-paste client config.

---

## Take it for a spin — what to test first

Want to evaluate For-Rest properly? Here's a guided tour that exercises the things that actually set it apart. Paste each into a new request (the **CFG**/request editor) and hit run.

**1 · Code-first basics — variables, interpolation, assertions**
```frs
method GET
url "https://httpbin.org/get?trace={{trace_id}}"
runtime trace_id = guid()
let r = request.send()
expect status == 200 "reachable"
log $"echoed trace: {r.args.trace}"
```
*What you're testing:* runtime variables, `{{interpolation}}`, dynamic response access, and assertions that report instead of throw.

**2 · Resilience — retry with backoff + handlers**
```frs
method GET
url "https://httpbin.org/status/503"
on status 503 { warn "service flaky — backing off" }
retry 3 with backoff {
  let sent = request.send()
  if sent.status == 200 { break }
}
```
*What you're testing:* `retry … with backoff`, `on status` handlers, and `break` — resilience as syntax.

**3 · Chaining — extract a value and feed the next call**
```frs
method GET
url "https://httpbin.org/uuid"
let first = request.send()
extract runtime token = json "$.uuid"
request.url = $"https://httpbin.org/anything?id={token}"
let second = request.send()
expect json "$.args.id" == "{{token}}" "id threaded through"
```
*What you're testing:* `extract` by JSON-path, mutating the request mid-flow, and chaining requests in one script.

**4 · Tables — stash structured findings into run history**
```frs
method GET
url "https://httpbin.org/get"
let r = request.send()
stash columns ["check", "value"]
stash.check = "status";  stash.value = convert.ToString(r.status);  stash.Commit()
stash.check = "type";    stash.value = r.headers["Content-Type"];   stash.Commit()
```
*What you're testing:* the `stash` table — every run is snapshotted to SQLite, so this grid is queryable later (and over MCP).

**5 · Concurrency — fan out in parallel**
```frs
method GET
url "https://httpbin.org/get"
let [a, b, c] = parallel {
  GET "https://httpbin.org/uuid"
  GET "https://httpbin.org/ip"
  GET "https://httpbin.org/user-agent"
}
log $"uuid={a.uuid} ip={b.origin}"
```
*What you're testing:* `parallel { }` — concurrent sends collected into one result.

**6 · Security — payload corpora + fuzzing** *(authorized targets only)*
```frs
method GET
url "https://httpbin.org/anything"
foreach p in payloads.Category("xss") {
  request.url = $"https://httpbin.org/anything?q={encoding.UrlEncode(p)}"
  let r = request.send()
  if r.status >= 500 { warn $"5xx for {p}" }
}
```
*What you're testing:* the built-in `payloads` catalog (SQLi, XSS, SSRF, SSTI, path traversal, command injection, and more). For the concurrent baseline-diffing engine, ask the AI to `build_fuzz_script`.

**7 · AI in the editor** — type `## turn this into a paginated GET with caching headers` above a request and watch it rewrite the script.

**8 · Agent control** — enable the MCP server (below), point Claude at it, and ask it to *"create a workspace, write a request that fetches a UUID and asserts 200, run it, and show me the result."* It will do all of that through the live workbench.

> **Benefits to weigh while you test:** no account/cloud to fight; requests that diff cleanly in git; resilience/extraction/assertions you'd otherwise hand-roll; secrets that never reach the model; one artifact a human, an AI, and an agent all operate on; and security/browser tooling built in rather than bolted on.

---

## Capability catalog

<details>
<summary><b>The <code>.frs</code> language surface</b> (click to expand)</summary>

- **Request directives:** `method`, `url`, `timeout`, `user_agent`, `redirects`, `ssl`, `history`, `content_type`, `max_send_iterations`, `header`, `body json`/raw.
- **Auth modes:** `none`, `bearer`, `basic`, `api_key`, `header`, `digest`, `ntlm`, `negotiate`, `oauth_client_credentials`, `oauth_device_code`, `oauth_authorization_code`, `oauth_integrated_windows`.
- **Variables:** `runtime`, `secret`, request-local; `{{interpolation}}`; precedence System → Global → Workspace → Environment → Request-local → Runtime; seed funcs `guid()`, `now()`, `utc_now()`, `random()`.
- **Flow:** `let`, `if`/`else if`/`else`, `while`, `foreach`, `switch`/`case`/`default`, `break`/`continue`, `delay`, ranges `[0..n]` & `range()`, `$"interpolated {strings}"`, `and`/`or`/`not`.
- **Sending:** `request.send()` (guarded, with `max_send_iterations`), `request.method`/`url`/`body`/`content_type`/`headers` mutation, named sends, `workspace.execute()`.
- **Resilience:** `retry N with backoff`/`with delay`, `on error { }`, `on status N { }`.
- **Composition:** `parallel { }`, `pipe { → }`, `define`/`call`, `import`/`use`, `scenario "name" { }`.
- **Verify & capture:** `expect` (status/header/body/json-path/regex), `extract` (json/header/regex), `stash` / `stash columns`, `snapshot`, `tests { }` / `tests.Assert`.
- **Helper namespaces:** `json`, `strings`, `regex`, `crypto`, `encoding`, `convert`, `time`, plus `.where`/`.select`/`.first`/`.length()` collection operators.
- **Security:** `payloads` (sqli, xss, path_traversal, command_injection, ssti, open_redirect, xxe, nosqli, crlf, ssrf, ldap, header_injection, prototype_pollution + custom) with `.Mutate()` WAF-evasion variants; `fuzz` engine with bounded concurrency, baseline diffing, host allowlists, and anomaly findings.
- **Browser:** `browser.navigate/click/type/press/hover/select/scrollTo/getText/getAttribute/exists/waitFor/find/snapshot/screenshot/evaluate`, with record → replay.

</details>

<details>
<summary><b>The MCP tool surface</b> (what an agent can do — 30+ tools)</summary>

- **Learn:** `get_instructions`, `search_docs`, `list_ai_providers`, `list_payload_categories`, `get_payloads`.
- **Workspaces & scripts:** `list_workspaces`, `get_workspace`, `create_workspace`, `rename_workspace`, `delete_workspace`, `get_script`, `create_script`, `update_script`, `rename_script`, `delete_script`.
- **Active document:** `get_active_request`, `set_active_request`, `replace_active_request`.
- **Run:** `compile_script` (validate without sending), `execute_script`, `execute_stored_script`.
- **Inspect:** `list_runs`, `get_run`, `get_run_response`, `grep_run_response`.
- **Security:** `build_attack_script`, `build_fuzz_script`, `analyze_responses`.
- **Browser:** `browser_navigate`, `browser_snapshot`, `browser_screenshot`, `browser_query`, `browser_click`, `browser_type`, `browser_press`, `browser_eval`.
- **Focus:** `show_in_app` (bring a pane forward for the user).

Secret values are redacted on read and restored by the host on write, so an agent can edit and run requests that *use* credentials without ever seeing them.

</details>

---

## Install

**macOS (Homebrew):**
```bash
brew install --cask --no-quarantine forrest-technologies/tap/for-rest
```
The app is currently unsigned, so `--no-quarantine` skips the Gatekeeper block; without it, right-click `For-Rest.app` → **Open** on first launch. Windows and Android builds are attached to each [GitHub release](https://github.com/Forrest-Technologies/for-rest/releases), or build from source below.

---

## Quick start

> **Baseline:** .NET `10.0.200-preview` (pinned in [`global.json`](global.json)). Solution format is `ForRest.slnx`.

```bash
# Build & test
dotnet build ForRest.slnx
dotnet test tests/ForRest.Tests/ForRest.Tests.csproj
```

**Run on Windows**
```powershell
Start-Process .\src\ForRest.App\bin\Debug\net10.0-windows10.0.19041.0\ForRest.App.exe
```

**Build for Android**
```bash
dotnet build   src/ForRest.Maui/ForRest.Maui.csproj -f net10.0-android
dotnet publish src/ForRest.Maui/ForRest.Maui.csproj -f net10.0-android -c Release
```

On a macOS host the app project defaults to the Mac Catalyst head, so add
`-p:TargetFrameworks=net10.0-android` to the commands above (and install the
Android SDK) to build the Android head from a Mac.

**Build for macOS** (unsigned — no Apple Developer license required)
```bash
dotnet publish src/ForRest.Maui/ForRest.Maui.csproj -f net10.0-maccatalyst -c Release -p:ForRestMacUnsigned=true
# Launch via right-click → Open the first time to get past Gatekeeper.
```

The desktop build uses the **Monaco** editor; Android ships a **native (Sora) editor** tuned for touch. Both speak the same `.frs` language and inline-AI surface.

---

## Configuration

For-Rest is configured through a human-editable `settings.toml` (the **CFG** tab in-app). Enable AI by dropping in a provider and key:

```toml
[ai]
enabled = true
stream_responses = true
provider = "xai"          # openai | azure-openai | anthropic | gemini | grok/xai | groq | deepseek | mistral | openrouter | custom
model = "grok-build-0.1"
api_key = "••••••••"

# MCP server is desktop-only and off by default.
[mcp]
enabled = true
bind_address = "127.0.0.1"
port = 7341
auth_token = ""           # set to require an Authorization: Bearer header
```

---

## Architecture

For-Rest follows a strict, downward-only dependency flow. Business rules stay out of views and XAML; scripting stays behind its host contract; secrets are secret by storage boundary, not UI convention.

```
App  →  Services  →  Repositories  →  Infrastructure (SQLite)
                    ↘ Domain · Models · Scripting · Plugin abstractions (isolated)
```

```text
src/
  ForRest.App/                    WinUI host and presentation view models
  ForRest.Maui/                   Cross-platform MAUI shell (Windows · Android · macOS)
  ForRest.Domain/                 Variable resolution, request compilation, extraction
  ForRest.Models/                 DTOs, enums, persisted & execution records
  ForRest.Services/               Application services, HTTP pipeline, AI runtime
  ForRest.Scripting/              Roslyn script host and the .frs language reference
  ForRest.Browser/                Embedded-browser automation + CDP driver
  ForRest.Repositories/           Repository contracts
  ForRest.Infrastructure.Sqlite/  SQLite persistence + DPAPI secret protection
  ForRest.Mcp/                    MCP server and tool catalog
  ForRest.Plugins.Abstractions/   Versionable extension contracts
  ForRest.Plugins.Host/           Plugin discovery & loading
  ForRest.Shared/                 Shared result helpers

tests/
  ForRest.Tests/                  Unit & integration tests (MSTest)
  ForRest.Maui.Tests/             Platform-specific MAUI tests
```

**Engine room:** the `.frs` source is parsed and compiled to C#, then executed on a **Roslyn** scripting host with a curated global surface (`request`, `response`, `variables`, `tests`, `stash`, `payloads`, `fuzz`, `browser`, `crypto`, `time`, `encoding`, `strings`, `json`, `regex`, …). Every run is captured as a snapshot and written to local SQLite, so history, tests, logs, and stash tables are all queryable after the fact — including over MCP.

---

## Roadmap

For-Rest is built to grow into a serious, commercial-ready developer platform — not just a request sender. Shipping today: the `.frs` language, variables/environments, response extraction & assertions, run history, security payloads + fuzz engine, an embedded record/replay browser, inline AI across 9+ providers, and the MCP server. On the horizon:

- **Plugin ecosystem** — the contracts and loader (`IForRestPlugin`, `IAuthProviderPlugin`, `IResponseViewerPlugin`) are already in place; a discoverable marketplace and bundled plugins are next.
- **Scheduling & repeatable workflows** — building on the guarded multi-send/retry engine.
- **A `fuzz { }` flow-block grammar** and richer response viewers.

---

## Documentation

- [`docs/mcp/connecting-mcp-clients.md`](docs/mcp/connecting-mcp-clients.md) — connect Claude, Cursor, or any MCP client
- [`docs/architecture/forrest-script-language.md`](docs/architecture/forrest-script-language.md) — the `.frs` language guide
- [`docs/architecture/for-rest-architecture.md`](docs/architecture/for-rest-architecture.md) — system design
- [`docs/architecture/mvp-progress.md`](docs/architecture/mvp-progress.md) — status & milestones
- [`docs/dependencies/dependency-ledger.md`](docs/dependencies/dependency-ledger.md) — dependency review
- [`CODEX.md`](CODEX.md) · [`CLAUDE.md`](CLAUDE.md) — contributor conventions

---

## License

For-Rest is free and open source under the [MIT License](LICENSE).

---

<div align="center">

**Postman is great for filling out forms. For-Rest is for engineering API workflows.**

*Different verb. That's the whole pitch.*

</div>
