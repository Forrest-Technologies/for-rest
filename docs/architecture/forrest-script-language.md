# For-Rest Script Language

Date: 2026-03-28

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

```frs
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
| `oauth_integrated_windows` | Windows integrated token acquisition |

The docs and catalog should keep the following auth shape explicit:

```frs
auth {
  mode = oauth_client_credentials
  token_url = "https://login.example.test/oauth2/v2.0/token"
  client_id = "{{client_id}}"
  client_secret = "{{client_secret}}"
  scopes = "api://forrest/.default"
}
```

## Flow Control

Supported flow forms today:

- `if / else if / else`
- `while`
- `foreach`
- `range(start, end)`
- inclusive range literals like `[0..9]`

Examples:

```frs
while request.remaining_send_iterations > 0 {
  let sent = request.send()
  if sent.status == 200 {
    break
  }
}
```

```frs
foreach index in range(0, 3) {
  log index
}
```

`switch`, `case`, and `default` are not currently part of the flow compiler. Keep the docs honest about that gap until the compiler supports it.

## Request Sending

`request.send()` updates the global `response` and also returns the latest response snapshot so the caller can keep a local handle to each send.

```frs
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

```frs
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

## Built-In Helpers

The runtime currently includes these helper surfaces:

- `regex`
- `encoding`
- `crypto`
- `json`
- `random`
- `time`
- `tests`
- `console`

Common patterns they already support:

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
- OAuth token acquisition (`oauth_client_credentials`, `oauth_device_code`, `oauth_integrated_windows`)
- JSON extraction
- regex extraction and regex-backed expectations
- structured response stash data
- `while`, `foreach`, `range(start, end)`, and `if` flow forms
- `workspace.execute()` nested request execution
- `ssl`, `history`, `timeout`, `redirects`, `content_type`, and `max_send_iterations` request settings

Not yet included:

- browser-based authorization code + PKCE helpers
- nested multi-request workflow graphs in a single document
- user-defined functions
- persistent writes back into global/workspace/environment variable stores
- plugin-provided language extensions
- cloud signing helpers such as AWS SigV4 or bespoke HMAC schemes
- `switch` / `case` / `default` flow syntax

Those are future language/runtime expansions, not parser bugs.
