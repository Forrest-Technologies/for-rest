# For-Rest Script Language

Date: 2026-08-14

## Canonical Source

The authoritative source of truth for For-Rest language help is `ForRestLanguageCatalog` in `src/ForRest.Scripting`.

That single catalog now feeds:

- Monaco hover and completion help
- human-readable reference docs
- compact prompt context for future AI features

This document is a readable mirror of that catalog. Keep it aligned with the code, not the other way around.

## Purpose

The For-Rest script language is the request-document language for `.frs` work. It is a script-first, editor-first authoring format that describes:

- request identity
- request-local variables
- runtime-seeded variables
- HTTP request metadata
- auth
- query parameters
- headers
- body content
- response extractions
- assertions
- repeat scheduling
- retry policy

The language is intentionally declarative for the request shape, with code-first flow layered on top. It compiles into an execution payload that the runtime can execute immediately.

## Document Shape

Supported top-level sections:

- `meta`
- `vars`
- `request`
- `auth`
- `query`
- `headers`
- `body`
- `form`
- `multipart`
- `extract`
- `tests`
- `repeat`
- `retry`

Supported top-level directives:

```ruby
name "Get Users"
method GET
url "https://api.example.test/users"
timeout 15000
redirects true
ssl true
history true
content_type "application/json"
max_send_iterations 3
```

Comments start with `#`. Blank lines are ignored.

`body` supports three modes: `body json """..."""`, `body form """..."""` (URL-encoded pairs), and `body multipart """..."""`. All three parse, compile, and survive editor round-trips.

The canonical renderer emits sections in a fixed order — imports, meta, request, auth, variables, query, headers, body, form, multipart, extract, flow, tests, repeat, retry, handlers, scenarios — and round-trips every renderable construct (regex extractions, `import` directives, `on error` / `on status` handlers, `scenario` blocks, and `regex` assertions included), so editor rewrites are loss-free.

## Inline AI

Request documents can host an inline AI conversation directly in the editor.

Markers:

- `##` user prompt to the AI
- `#>` latest AI response
- `#~` older faded AI responses

Examples:

```ruby
## Tighten this request and add a bearer auth example.
```

```ruby
## Why is this request failing?
#> The URL is valid, but the request is missing an Authorization header.
#> Add `auth.mode = bearer` and set `auth.token`.
```

### Agent Prompt CMD

Inline AI also supports local shorthand commands that run without sending a turn to the AI runtime.

Currently supported:

- `## reset`
- `## clear responses`
- `## collapse`
- `## help`
- `## commands`

`## reset` clears all inline AI prompt and response lines from the active request document, then reopens a fresh blank `## ` prompt near the current working area so the user can start a new inline thread with no carried history.

`## clear responses` removes inline AI response lines from the active request document, keeps the existing `##` prompt history, and reopens a fresh blank `## ` prompt.

`## collapse` keeps only the latest inline AI exchange in the active request document, removes older inline AI history, and reopens a fresh blank `## ` prompt.

`## help` lists the supported local prompt commands directly in the inline AI pane, then opens a fresh blank `## ` prompt.

`## commands` is an alias for `## help`.

Current editor behavior:

- pressing `Enter` on a non-empty `##` prompt line inserts a continuation line that also starts with `## `
- pressing `Enter` on a blank trailing `## ` continuation line submits the whole multiline prompt block
- pasting multiline text into a `##` prompt automatically prefixes each continuation line with `## `
- pressing the normal send action also routes to AI when the cursor is on an AI conversation block
- if the prompt is the last meaningful line in the document, send still treats it as an AI request even if there is a trailing blank line

Settings behavior:

- for `provider = "openai"`, `endpoint` is optional
- for `provider = "azure_openai"`, `endpoint` and `deployment_name` are required

## Request Surface

The request envelope is available either as a `request { ... }` section or as top-level aliases. The parser accepts both forms, so the docs and catalog need to keep both in sync.

| Setting | Purpose | Example |
| --- | --- | --- |
| `method` | HTTP verb | `method GET` |
| `url` | Request URL template | `url "https://api.example.test/users"` |
| `timeout` | Timeout in milliseconds | `timeout 15000` |
| `redirects` | Follow redirect hops | `redirects true` |
| `ssl` | Control certificate validation | `ssl true` |
| `history` | Persist the run to history | `history true` |
| `content_type` | Set the outgoing content type | `content_type "application/json"` |
| `max_send_iterations` | Bound `request.send()` loops | `max_send_iterations 3` |

Aliases accepted by the runtime:

- `request.method`
- `request.url`
- `request.timeout`
- `request.redirects`
- `request.ssl`
- `request.history`
- `request.content_type`
- `request.max_send_iterations`

Notes:

- `ssl true` is the default posture for real endpoints. Disable it only for local or otherwise trusted test environments.
- `history true` is what makes a request appear in the history pane.
- `max_send_iterations` is the guardrail around `request.send()` loops.

## Auth Surface

Use `auth { ... }` or top-level `auth key = value` directives.

| Mode | Notes |
| --- | --- |
| `none` | No auth |
| `bearer` | Bearer token in the request |
| `basic` | Basic auth username and password |
| `api_key` | API key in a header or query parameter |
| `header` | Raw token copied to a custom header |
| `digest` | Digest auth handshake |
| `ntlm` | NTLM challenge auth |
| `negotiate` | Integrated Windows auth handshake |
| `oauth_client_credentials` | Client credentials token acquisition |
| `oauth_device_code` | Device code auth flow |
| `oauth_authorization_code` | Interactive browser sign-in (authorization code + PKCE) |
| `oauth_integrated_windows` | Windows integrated token acquisition |

The docs and catalog should keep the following auth shape explicit:

```ruby
auth {
  mode = oauth_client_credentials
  token_url = "https://login.example.test/oauth2/v2.0/token"
  client_id = "{{client_id}}"
  client_secret = "{{client_secret}}"
  scopes = "api://forrest/.default"
}
```

`oauth_authorization_code` is the interactive grant: it opens the system browser to
`authorization_url` (with a PKCE challenge and `state`), captures the redirect on a
localhost loopback (or accepts a registered `redirect_uri`), exchanges the code at
`token_url`, caches the `refresh_token`, and auto-refreshes. The acquired bearer token
is exposed to post-response scripts as the runtime variable `accessToken`.

```ruby
auth {
  mode = oauth_authorization_code
  authorization_url = "https://login.example.test/authorize"
  token_url = "https://login.example.test/token"
  client_id = "{{client_id}}"
  redirect_uri = "http://127.0.0.1:5005/callback"
  scopes = "openid offline_access api"
  use_pkce = true
  code_challenge_method = "S256"
}
```

For Azure AD B2C, put the policy in the `authorization_url` (path or query) and use a
public client with PKCE; a loopback `redirect_uri` or `https://oauth.pstmn.io/v1/callback`
both work (the latter requires a paste-the-redirect broker on headless hosts).

## Flow Control

Supported flow forms today:

- `if / else if / else`
- `while`
- `foreach` (preferred) / `for` (accepted alias), with an optional zero-based loop index: `foreach item, i in source { }` (the parenthesized `foreach (item, i) in source { }` form also parses). The index is a real integer usable in expressions and stays in scope after the loop holding the last index.
- `switch / case / default`, including comma-separated multi-value cases — `case 200, 201 { }` matches when the expression equals any listed value (OR semantics); commas inside strings or parentheses do not split values
- `range(start, end)`
- inclusive range literals like `[0..9]`, with expression endpoints (`[low..high]`, `[1..size - 1]`); reversed endpoints count down. Endpoints cannot contain top-level commas or a nested `..`.
- `retry <count> { ... }` with optional `with backoff` (exponential, 100ms doubling) or `with delay <ms>`. Both the count and the delay accept expressions — variables, member access, arithmetic — with the count clamped to at least 1. At the document top level (outside an explicit flow block) a non-literal count needs the `{` on the same line as the `retry` header.
- `stop` — ends the main flow early and successfully (already-set variables and tests are kept). Inside a `define` subroutine, `stop` returns from the subroutine only and the caller continues. `stop` is a reserved word and cannot be used as a variable name.
- `define name with param1, param2 { ... }` subroutines invoked via `call name with arg1, arg2`
- `parallel { ... }` fan-out, including `let [a, b] = parallel { ... }` destructuring
- `pipe { ... }` sequential request chains

At the document level, `scenario "name" { ... }` sections declare named test scenarios that share the base request, and `import` / `use` directives pull in shared modules. An import merges the imported file's variables, headers, query/form/multipart entries, auth keys, and extractions into the importing document. The importing document always wins on a name clash, and between multiple imports the first import wins. Flow code, tests, defines, scenarios, and handlers are never imported — executable behavior stays in the file that declares it. Imports resolve transitively, and circular imports are detected and surfaced as warnings.

**Block style.** The canonical form puts the opening brace on the header line, indents the
body, and closes with `}` on its own line (every example below uses it). A block whose body
is a single short statement may also be written inline — `if sent.status == 200 { break }` —
but prefer the multiline form for anything longer. Both compile to the same thing: the inline
form is expanded to the multiline form before compilation.

Examples:

```ruby
while request.remaining_send_iterations > 0 {
  let sent = request.send()
  if sent.status == 200 {
    break
  }
}
```

```ruby
foreach index in range(0, 3) {
  log index
}
```

```ruby
foreach item, i in [10..12] {
  log $"{i}:{item}"
}
```

```ruby
switch response.status {
  case 200, 201 {
    log "OK"
  }
  case 404 {
    warn "Not found"
  }
  default {
    error "Unexpected status"
  }
}
```

## Assertions

`expect` assertions target `status`, `body`, `header "Name"`, and `json "$.selector"`, either at the top level or inside a `tests { }` section. A trailing quoted string is an optional label on every form.

Supported operators:

- `==` / `!=` — equality
- `contains`, `startswith`, `endswith` — case-sensitive ordinal string matching on `body`, `header`, and `json` targets. The keywords themselves parse case-insensitively. On `status` these raise a parse-time diagnostic pointing at the numeric operators instead.
- `>`, `>=`, `<`, `<=` — numeric comparisons on all four targets. For string targets, both the actual and expected values are trimmed and parsed as invariant-culture decimals (`10`, `3.14`, and `1e3` all parse); when both sides parse the comparison is numeric, and when either does not, the assertion fails with a clear `<label> failed: actual value '<v>' is not numeric` message (or the expected-value variant) instead of throwing.
- `regex "pattern"` — regex matching on `body`, `header`, and `json` targets
- `exists` / `not exists` — json-only presence checks; `not exists` passes when the selector matches nothing. On non-json targets they raise a parse-time diagnostic explaining they are only supported for json assertions.

```ruby
expect status >= 200 "success range"
expect json "$.count" > 5 "count above five"
expect body startswith "hello" "greeting prefix"
expect json "$.missing" not exists "no missing field"
```

## Response and Error Handlers

Two top-level handler blocks run around the main flow:

- `on status <code> { ... }` runs after the main flow when the response status matches the code.
- `on error { ... }` runs when the main flow throws an unhandled exception.

```ruby
runtime trace_id = guid()

on status 429 {
  warn $"rate limited for {trace_id}"
}

on error {
  error $"request {trace_id} failed"
}
```

Handlers share the main flow's runtime variables and seeds. A value like `trace_id` declared as a `runtime` seed (or any known variable) is already in scope inside every handler — you reference it directly and must **not** redeclare it. Handlers can also declare their own local `let`/`runtime` values, which stay scoped to the handler block.

## Security Payloads

`payloads` is a built-in corpus of curated fuzzing wordlists for authorized security testing. Named categories are exposed as properties and there are helpers for dynamic lookups:

- `payloads.sqli`, `payloads.xss`, `payloads.path_traversal`, `payloads.command_injection`, `payloads.ssti`, `payloads.open_redirect`, `payloads.xxe`, `payloads.nosqli`, `payloads.crlf_injection`, `payloads.ssrf`
- `payloads.Category(name)` for a dynamic category lookup
- `payloads.Combine("xss", "ssti", "custom-literal")` to merge categories (and extra literals) with duplicates removed
- `payloads.Categories()` to list every available category name

```ruby
foreach p in payloads.sqli {
  request.url = $"https://api.example.test/search?q={p}"
  let sent = request.send()
  if sent.status >= 500 { warn $"possible sqli: {p}" }
}
```

There is no dedicated `fuzz` block; the documented pattern is `foreach` over a payload category. Each element is a plain string, accessible positionally (`payloads.sqli[0]`) or through the collection helpers.

## Working With Dynamic Values

Flow variables, response members, and extracted values are all dynamic. A few practical rules keep scripts compiling:

- Helper surfaces that take text — including `crypto.Md5/Sha1/Sha256` — coerce any dynamic value (string, number, bool, JSON scalar) to its stable text form, so `crypto.Sha256(trace_id)` works whether or not `trace_id` is typed as a string.
- Collections (JSON arrays, ranges, `strings.Split(...)` results) support both positional indexing — `items[0]` — and the LINQ-like collection helpers such as `.first()`, `.last()`, and `.first(x => ...)`.
- Use string interpolation (`$"...{value}..."`) to splice dynamic values into URLs, headers, and bodies.

## Request Sending

`request.send()` updates the global `response` and also returns the latest response snapshot so the caller can keep a local handle to each send.

```ruby
let sent = request.send()
if sent.status == 200 {
  runtime last_status = sent.status
}
```

The runtime uses `max_send_iterations` to keep these sends bounded.

## Workspace Execution

`workspace.execute("Request Name")` runs another request from the current workspace and reuses the current variable context.

Important behavior:

- runtime variables from the nested request are merged back into the caller
- console entries, tests, and stash rows from the nested run are imported back into the outer execution context
- standalone history entries are suppressed for helper runs

```ruby
let auth = workspace.execute("/requests/auth/token")
request.headers["Authorization"] = $"Bearer {auth.token}"
```

## Stash Behavior

The stash surface captures structured rows for the inspector pane.

Supported operations:

- `stash.Column = value`
- `stash["Column Name"] = value`
- `stash.Commit()`
- `stash.Push()`
- `stash.ClearPending()`
- `stash.Reset()`

Behavior to keep documented:

- rows only appear when the stash lines actually execute
- `Commit()` and `Push()` both finalize the current row
- `ClearPending()` drops the in-progress row without clearing committed rows
- `Reset()` clears both committed rows and the current in-progress row
- if a branch exits before a stash assignment or commit executes, that branch contributes no stash row

## Security & Fuzzing

The runtime ships a defensive security-testing surface for **authorized** testing only.

- `payloads` is a curated catalog of fuzzing corpora for common web vulnerability
  classes: `sqli`, `xss`, `path_traversal`, `command_injection`, `ssti`,
  `open_redirect`, `xxe`, `nosqli`, `crlf_injection`, `ssrf`, `ldap`,
  `header_injection`, and `prototype_pollution`. Use the named properties, or
  `payloads.Category(name)` / `payloads.Combine(...)` for dynamic lookups, and
  `payloads.Mutate(payload)` / `payloads.MutateAll(list)` to expand a payload into
  WAF-evasion variants (url, double-url, base64, upper/lower case). Custom
  categories can be injected through the host.

- `fuzz` is a programmatic fuzz engine. `await fuzz.Run(payloads, send, options, category)`
  drives a payload enumerable against the current request through a `send`
  delegate (which reuses `request.send()`), bounding concurrency with a
  `SemaphoreSlim` (`options.MaxConcurrency`), enforcing a per-attempt timeout via
  a cancellation token (`options.TimeoutMs`), and applying an optional
  `options.DelayMs` throttle. Per-attempt errors are recorded, never thrown. The
  `FuzzResult` exposes `.Attempts`, `.Findings`, `.Clusters`, `.Baseline`, and
  `.Summarize()`.

- Response diffing / fingerprinting is pure and deterministic (no LLM):
  `fuzz.Fingerprint(response)` reduces a response to `(status, size bucket, timing
  bucket)`, `fuzz.Baseline(response)` captures a known-good baseline, and
  `fuzz.Diff(baseline, response)` flags status changes, large size deltas, and
  time-based anomalies (the canonical blind/time-based injection signal). Attempts
  are clustered by fingerprint so outliers stand out.

- Governance: `fuzz.AllowHost(host)` / `fuzz.AllowHosts([...])` declare an
  in-scope host allowlist; once declared, the runner refuses out-of-scope targets.
  Every run writes an audit line (category, payload count, concurrency, timeout,
  and the anomaly summary) into the script `console`.

A dedicated `fuzz { }` flow-block grammar is a future follow-up; today `fuzz` is a
programmatic API object.

## Built-In Helpers

The runtime currently includes these helper surfaces:

- `strings`
- `convert`
- `regex`
- `encoding`
- `crypto`
- `json`
- `random`
- `time`
- `tests`
- `console`
- `payloads`
- `fuzz`
- `browser`
- `workspace`
- `snapshot`
- `stash`

Every helper surface above is a global that is always in scope inside flow code — you reference it directly (`crypto.Sha256(...)`, `payloads.sqli`) without importing or declaring it.

Common patterns they already support:

- trimming, replacing, splitting, and joining request/runtime text
- coercing string or JSON values into booleans, ints, decimals, and stable strings
- parsing and formatting UTC timestamps plus Unix time round-trips
- token scraping from HTML or JSON fragments
- replaying server-issued values into later requests
- chained login / probe / follow-up sends
- request signing or verification helpers
- base64 and URL encoded payload shaping

## Compiler Output

The parser produces a structured document model.

The compiler lowers that document into an execution payload:

- `RequestDefinition`
- runtime variable seed operations
- generated response assertions
- extraction definitions
- repeat schedule
- retry policy

The runtime then:

1. evaluates runtime seeds
2. compiles the request with variable interpolation
3. resolves auth metadata and tokens
4. sends the HTTP request
5. retries on transport failure or `5xx` when configured
6. extracts variables from the response
7. runs generated assertions
8. returns execution runs, response snapshots, console output, tests, and runtime variables

Compiled flow scripts are cached: each script text gets a deterministic SHA-256-derived assembly/type identity, and a bounded, thread-safe LRU (32 entries) keeps loaded collectible `AssemblyLoadContext`s, unloading them on eviction. Repeated executions of an unchanged script skip Roslyn entirely, and editor Validate reuses the same cached compilations.

Diagnostics are written for `.frs` authors, not C# readers. Unclosed sections report the line where the section opened; a top-level line that closely resembles a known directive (for example `hedaer` for `header`) produces a did-you-mean warning while still compiling as flow code; and script compile failures are rendered as `Script error (line N): ...` with a plain-language explanation for common mistakes (unknown names, missing members, unclosed braces), keeping the raw compiler diagnostic appended after a `| details:` separator for bug reports.

## Current Boundaries

Included now:

- single-request document compilation
- code-first flow inside the request document
- bounded chained sends via `request.send()`
- request mutation between sends
- dynamic response member access and array indexing
- runtime variable seeding
- request/body/header/query/auth authoring
- challenge-based Windows auth (`digest`, `ntlm`, `negotiate`)
- OAuth token acquisition (`oauth_client_credentials`, `oauth_device_code`, `oauth_authorization_code`, `oauth_integrated_windows`)
- JSON extraction
- regex extraction and regex-backed expectations
- numeric (`>` `>=` `<` `<=`), affix (`startswith` / `endswith`), and json-only `exists` / `not exists` assertion operators
- `json`, `form`, and `multipart` body modes with loss-free renderer round-trips
- structured response stash data
- `while`, `foreach` (with optional loop index), `range(start, end)`, `if`, `switch / case / default` (multi-value cases), and `stop` flow forms
- range literals with expression endpoints
- `retry` blocks with expression counts and delays (`with backoff` / `with delay`), `on status` / `on error` handlers
- `define` / `call` subroutines, `parallel` and `pipe` composition
- `scenario` sections and `import` / `use` shared-module directives (merging variables, headers, query/form/multipart entries, auth keys, and extractions — document wins, first import wins)
- `workspace.execute()` nested request execution
- `ssl`, `history`, `timeout`, `redirects`, `content_type`, and `max_send_iterations` request settings
- `payloads` corpora (with mutation) and the `fuzz` engine (bounded concurrency, baseline diffing, fingerprinting, host-scope governance)

Not yet included:

- arbitrary multi-request workflow graphs beyond `pipe` / `parallel` / `workspace.execute()` composition
- persistent writes back into global/workspace/environment variable stores
- plugin-provided language extensions
- cloud signing helpers such as AWS SigV4 or bespoke HMAC schemes
- a dedicated `fuzz { }` flow-block grammar (the programmatic `fuzz` API covers this today)

Those are future language/runtime expansions, not parser bugs.

## Status & Roadmap

The language and runtime surfaces above are implemented and test-covered today, including the 2026-08-14 enhancement pass: foreach loop indexes, multi-value switch cases, expression-based retry counts and range endpoints, the `stop` statement, the widened assertion operator set, loss-free renderer round-trips for all body modes and renderable constructs, and the compiled-script cache. The surrounding product surfaces below are **planned but not yet implemented** — contributors should not expect to find them in the codebase yet:

- **Workspace management.** Basic workspace switching, create/rename/delete, and per-workspace document state exist in the shell (and over MCP). Richer management — folder/collection organization, moving and duplicating requests between workspaces, and bulk operations — still needs to be implemented.
- **Script / collection management.** `.frs` documents live at simple locations (for example `/requests/get-users`) inside a workspace. A fuller script library — collections, tagging, search, and reusable shared-module management to back `import` / `use` at scale — is planned.
- **Import / export.** There is no import or export today. The intended direction is import from Postman collections, OpenAPI specs, and curl commands (compiling each into readable `.frs`), plus a portable workspace export/import format so workspaces can be shared or checked into git as plain text.
