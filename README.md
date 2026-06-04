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

</div>

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
| **Authoring** | A real language: loops, branches, retries, extraction | Forms + a limited JS sandbox |
| **AI** | Inline in the editor, edits your request back, 9+ providers, BYO key | Cloud copilot in a sidebar |
| **Agent access** | **Built-in MCP server** — agents drive the workbench | — |
| **Secrets** | Encrypted at the storage boundary; redacted before AI/agents see them | Cloud vault |
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
let attempts = [0..2]
foreach attempt in attempts {
  sent = request.send()
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

## The four pillars

### 🔒 Local-first — your secrets never leave the machine
There's no cloud backend to opt out of, because there isn't one. Workspaces and full execution history persist to local **SQLite**. Secret values are encrypted at the **storage boundary (DPAPI on Windows)** — not hidden behind a UI asterisk — and are **redacted before any AI provider or MCP client ever sees them**. For anyone touching internal or regulated APIs, "the tool physically cannot phone home your tokens" isn't a feature, it's a procurement requirement.

### ⌨️ Code-first — a real scripting language, not a JS escape hatch
The `.frs` language is a first-class DSL compiled through a **Roslyn** host, not a sandbox afterthought. You get directives, `auth` blocks (bearer, basic, API-key, digest, NTLM, negotiate, and OAuth flows), `{{variable}}` interpolation, `let`/`if`/`foreach`/`while`/`switch`, string interpolation, ranges, LINQ-style collection operators (`.where`, `.select`, `.first`, `.groupBy`, …), `retry … with backoff`, `on error`/`on status` handlers, regex/JSON-path extraction, and helper namespaces for `crypto`, `time`, `encoding`, `strings`, `json`, and `regex`.

### 🤖 AI-native — the model lives *inside* the editor
Type a line that starts with `##` and you've started a conversation, right in the document:

```frs
## add pagination query params and an If-None-Match header for caching
```

Hit send and the answer streams in inline — and the AI can **edit the request back**, not just talk about it. It's not a chat panel in the corner; it's part of the editor surface, and the conversation travels with the file. Bring your own key for **OpenAI, Azure OpenAI, Anthropic, Google Gemini, xAI/Grok, Groq, DeepSeek, Mistral, OpenRouter, or any custom endpoint**.

### 🛰️ Agent-ready — For-Rest is itself an MCP server
This is the part the incumbents can't easily copy. Flip one setting and For-Rest exposes a **Model Context Protocol** endpoint over Streamable HTTP. Claude (or any MCP client) can then read your request docs, list and edit workspaces, compose and **execute** requests, and inspect run results — *through your workbench*. The future of API testing isn't a human clicking **Send** 200 times; it's an agent running your flows. For-Rest is built for that world.

→ See **[docs/mcp/connecting-mcp-clients.md](docs/mcp/connecting-mcp-clients.md)** for the copy-paste client config.

---

## Feature highlights

<table>
<tr><td valign="top" width="50%">

**Request authoring**
- `.frs` DSL with highlighting, completion & diagnostics
- Methods, URLs, headers, query, JSON/raw bodies
- `auth` block: bearer, basic, api_key, header, digest, NTLM, negotiate, OAuth (client-credentials, device-code, integrated Windows)
- `secret` values encrypted at rest (DPAPI)
- Guarded `request.send()` with `retry … with backoff/delay`

**Variables & environments**
- Six resolution scopes with strict precedence:
  System → Global → Workspace → Environment → Request-local → Runtime
- `{{variable}}` interpolation, case-insensitive
- `runtime` values set mid-flow override everything below them

</td><td valign="top" width="50%">

**Response & verification**
- Dynamic access: `response.user.name`, `response[0]`, `response.json()`
- `expect` assertions on status, headers, body, JSON-path, regex
- `extract` runtime values from body/header/json via regex or path
- `stash` rows into a table, persisted to run history
- Full run snapshots saved to SQLite (request, response, logs, tests, stash)

**AI & automation**
- Inline `##` conversations with streaming
- AI tool-calling: patch the active doc, run scripts, inspect results, search docs
- Built-in security payload catalog (SQLi, XSS, SSRF, SSTI, path traversal, …) + attack-script generation
- 30+ MCP tools for full agent control

</td></tr>
</table>

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
  ForRest.Repositories/           Repository contracts
  ForRest.Infrastructure.Sqlite/  SQLite persistence + DPAPI secret protection
  ForRest.Mcp/                    MCP server and tool catalog
  ForRest.Plugins.Abstractions/   Versionable extension contracts
  ForRest.Plugins.Host/           Plugin discovery & loading
  ForRest.Licensing*/             Offline-first licensing (lease tokens, grace periods)
  ForRest.Shared/                 Shared result helpers

tests/
  ForRest.Tests/                  Unit & integration tests (MSTest)
  ForRest.Maui.Tests/             Platform-specific MAUI tests
```

**Engine room:** the `.frs` source is parsed and compiled to C#, then executed on a **Roslyn** scripting host with a curated global surface (`request`, `response`, `variables`, `tests`, `stash`, `crypto`, `time`, `encoding`, `strings`, `json`, `regex`, …). Every run is captured as a snapshot and written to local SQLite, so history, tests, logs, and stash tables are all queryable after the fact — including over MCP.

---

## Roadmap

For-Rest is built to grow into a serious, commercial-ready developer platform — not just a request sender. Shipping today: the `.frs` language, variables/environments, response extraction & assertions, run history, inline AI across 9+ providers, and the MCP server. On the horizon:

- **Plugin ecosystem** — the contracts and loader (`IForRestPlugin`, `IAuthProviderPlugin`, `IResponseViewerPlugin`) are already in place; a discoverable marketplace and bundled plugins are next.
- **Scheduling & repeatable workflows** — building on the guarded multi-send/retry engine.
- **Richer response viewers** and deeper three-pane fluidity.

---

## Documentation

- [`docs/mcp/connecting-mcp-clients.md`](docs/mcp/connecting-mcp-clients.md) — connect Claude, Cursor, or any MCP client
- [`docs/architecture/for-rest-architecture.md`](docs/architecture/for-rest-architecture.md) — system design
- [`docs/architecture/mvp-progress.md`](docs/architecture/mvp-progress.md) — status & milestones
- [`docs/dependencies/dependency-ledger.md`](docs/dependencies/dependency-ledger.md) — dependency review
- [`CODEX.md`](CODEX.md) · [`CLAUDE.md`](CLAUDE.md) — contributor conventions

---

<div align="center">

**Postman is great for filling out forms. For-Rest is for engineering API workflows.**

*Different verb. That's the whole pitch.*

</div>
