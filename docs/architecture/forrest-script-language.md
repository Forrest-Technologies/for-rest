# For-Rest Script Language

Date: 2026-03-26

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

The language is intentionally declarative for the request shape, with code-first flow layered on top. It compiles into an execution payload that the current HTTP runtime can execute immediately.

## Current Flow Capabilities

The language already supports more flow/runtime behavior than the original request-shape overview implies.

Today you can:

- write top-level flow directly in the `.frs` document or place it inside an explicit `flow { ... }` block
- call `request.send()` multiple times in a single document, bounded by `max_send_iterations`
- capture each send into a variable such as `let sent = request.send()`
- mutate the request between sends by updating `request.Url`, `request.Method`, `request.Body`, `request.ContentType`, and `request.headers[...]`
- read the latest response from the global `response`
- read a specific send result from the captured variable returned by `request.send()`
- access JSON response members dynamically, including arrays and top-level collections
- pass data between sends with `runtime` variables and direct local variables
- use built-in `regex`, `encoding`, `crypto`, `json`, `random`, `time`, `tests`, and `console` helpers in flow code

This makes ForRest workable today for chained request automation, response probing, token scraping, request replay, and bounded iterative workflows without introducing a separate workflow graph format.

## Document Shape

Every document is a sequence of top-level sections or editor-first directives. Supported sections:

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

Comments start with `#`. Blank lines are ignored.

The language also supports top-level directives such as:

```frs
name "Get Users"
method GET
url "https://api.example.test/users"
auth mode = bearer
auth token = "{{access_token}}"
header "Accept" = "application/json"
expect status == 200 "returns 200"
```

## Example

```frs
name "Create Echo"
method POST
url "https://api.example.test/echo/{{resource_id}}?trace={{trace_id}}"
timeout 15000
redirects false
ssl true
history true
content_type "application/json"

request resource_id = "42"
runtime trace_id = guid()
runtime attempt = random(1, 4)

auth mode = oauth_client_credentials
auth token_url = "https://login.example.test/oauth2/v2.0/token"
auth client_id = "{{client_id}}"
auth client_secret = "{{client_secret}}"
auth scopes = "api://forrest/.default"

header "Accept" = "application/json"
header "X-Trace-Id" = "{{trace_id}}"

body json """
{
  "id": "{{resource_id}}",
  "trace": "{{trace_id}}"
}
"""

extract runtime echoed_id = json "$.payload.id"

expect status == 201 "returns 201"
expect header "Content-Type" contains "json" "json response"
expect json "$.payload.trace" exists "trace exists"

retry count = 2
retry interval = 500
```

## Flow Example

The request document can also act like a bounded script:

```frs
name "Replay Session"
method GET
url "https://target.example.test/login"
max_send_iterations 3

request.headers["User-Agent"] = "ForRest-Probe"

let login = request.send()
if login.status != 200 {
  error $"login page returned {login.status}"
}

let csrf = regex.Match(login.body, "\"csrf\":\"([^\"]+)\"", 1)
runtime csrf_token = csrf

request.Method = "POST"
request.Url = "https://target.example.test/session"
request.ContentType = "application/json"
request.Body = json.stringify(json.parse("""
{
  "username": "demo",
  "password": "demo",
  "csrf": "{{csrf_token}}"
}
"""))

let session = request.send()
if session.status == 200 {
  runtime session_cookie = regex.Match(session.body, "\"session\":\"([^\"]+)\"", 1)
  log $"session established: {session.status}"
} else {
  warn $"session create failed: {session.status}"
}
```

Notes:

- each call to `request.send()` updates the global `response`
- the returned value from `request.send()` remains independently usable as `login`, `session`, or any other chosen variable name
- runtime variables written with `runtime key = value` are available to later template interpolation in URL, headers, auth, and body content

## Section Rules

### `meta`

Simple document metadata.

Currently supported keys:

- `name`

### `vars`

Declares editable variables in the script.

Supported scopes:

- `request`
- `runtime`

`request` variables become request-local templating values.

`runtime` variables are evaluated immediately before request compilation and execution. They can therefore participate in URL, header, query, body, and auth interpolation.

Supported runtime seed expressions:

- string literals
- number literals
- boolean literals
- `guid()`
- `now()`
- `utc_now()`
- `random(min, max)`

### `request`

Required section for executable request documents.

Supported keys:

- `method`
- `url`
- `timeout`
- `redirects`
- `ssl`
- `history`
- `content_type`
- `max_send_iterations`

### `auth`

Optional request auth section.

Supported modes:

- `none`
- `bearer`
- `basic`
- `api_key`
- `header`
- `digest`
- `ntlm`
- `negotiate`
- `oauth_client_credentials`
- `oauth_device_code`
- `oauth_integrated_windows`

Supported keys:

- `mode`
- `username`
- `password`
- `token`
- `name`
- `value`
- `location`
- `scheme`
- `header_name`
- `header_value`
- `query_name`
- `use_default_credentials`
- `domain`
- `authority`
- `token_url`
- `client_id`
- `client_secret`
- `scopes`
- `resource`
- `audience`

Supported forms:

```frs
auth {
  mode = bearer
  token = "{{access_token}}"
}
```

```frs
auth mode = bearer
auth token = "{{access_token}}"
```

#### Auth Patterns

Static bearer:

```frs
auth mode = bearer
auth token = "{{access_token}}"
```

Raw token in a custom header:

```frs
auth mode = header
auth header_name = "X-Session-Token"
auth header_value = "{{session_token}}"
auth scheme = ""
```

API key in query:

```frs
auth mode = api_key
auth name = "api_key"
auth value = "{{api_key}}"
auth location = query
```

Windows pass-through via Negotiate:

```frs
auth mode = negotiate
auth use_default_credentials = true
```

Explicit NTLM credentials:

```frs
auth mode = ntlm
auth username = "{{username}}"
auth password = "{{password}}"
auth domain = "{{domain}}"
```

Digest:

```frs
auth mode = digest
auth username = "{{username}}"
auth password = "{{password}}"
```

OAuth 2.0 client credentials:

```frs
auth mode = oauth_client_credentials
auth token_url = "https://login.example.test/oauth2/v2.0/token"
auth client_id = "{{client_id}}"
auth client_secret = "{{client_secret}}"
auth scopes = "api://forrest/.default"
```

OAuth 2.0 device code:

```frs
auth mode = oauth_device_code
auth authority = "https://login.microsoftonline.com/contoso.onmicrosoft.com"
auth client_id = "{{public_client_id}}"
auth scopes = "api://forrest/.default offline_access"
```

Integrated Windows token acquisition:

```frs
auth mode = oauth_integrated_windows
auth authority = "https://login.microsoftonline.com/contoso.com"
auth client_id = "{{public_client_id}}"
auth scopes = "api://forrest/.default"
```

Notes:

- Leave `scheme` empty when you want the raw token copied into a custom header or query parameter.
- When `location = query`, the runtime updates the request URL before send.
- `oauth_client_credentials` can derive the Microsoft v2 token endpoint from `authority`, but `token_url` is still the most explicit option.
- Browser-based authorization code + PKCE is not first-class yet. Use `oauth_device_code`, `oauth_integrated_windows`, or static token injection when you need an interactive flow today.

### `query`

Declares request query parameters as `key = value`.

### `headers`

Declares request headers as `key = value`.

Header keys may be bare tokens or quoted strings.

### `body`

Declares raw request body content.

Supported forms:

- `body json """ ... """`
- `body raw """ ... """`
- `body text """ ... """`

### `form`

Declares `application/x-www-form-urlencoded` body entries.

### `multipart`

Declares multipart field entries.

### `extract`

Declares response-driven variable capture.

Supported targets:

- `runtime`
- `request`

Supported selector type:

- `json "$.path"`

Example:

```frs
extract {
  runtime created_id = json "$.payload.id"
}
```

### `tests`

Declares post-response assertions.

Supported assertion targets:

- `status`
- `body`
- `header "Name"`
- `json "$.path"`

Supported operators:

- `==`
- `!=`
- `contains`
- `exists`
- `>`
- `>=`
- `<`
- `<=`

Examples:

```frs
tests {
  status == 200 "returns 200"
  body contains "Ada" "body mentions Ada"
  header "Content-Type" contains "json" "json response"
  json "$.payload.id" == "42" "id matches"
  json "$.payload.trace" exists "trace exists"
}
```

### `repeat`

Declares repeat execution metadata.

Supported keys:

- `count`
- `delay`
- `interval`

### `retry`

Declares retry metadata for transport failures and `5xx` responses.

Supported keys:

- `count`
- `interval`

## Value Rules

String values must be quoted:

```frs
url = "https://api.example.test"
```

Booleans use `true` and `false`.

Numbers are parsed as integers.

Bare identifiers are used for enums such as HTTP methods and auth modes:

```frs
method = POST
mode = bearer
```

## Variable Resolution

The execution pipeline resolves variables in this order:

1. system variables
2. global variables
3. workspace variables
4. environment variables
5. request-local variables from `vars`
6. runtime variables seeded from `vars`
7. runtime variables captured from response extraction

Templated values use `{{name}}`.

## Response Access

The runtime exposes response data in two related ways:

- `response`
  - always points at the latest response snapshot
- `let sent = request.send()`
  - keeps a specific send result available even if later sends replace the global `response`

Supported response access patterns include:

```frs
let sent = request.send()
log sent.status
log sent.body
log sent.headers["Content-Type"]
log sent.user.name
log sent.items[0].id

if response.status == 200 {
  log response.payload.traceId
}
```

For explicit JSON parsing you can also call:

```frs
let payload = response.json()
let id = json.select(payload, "$.items[0].id")
```

The global `response` is the correct fallback root for copied response expressions. `request` remains the mutable outbound request API, not the response API.

## Built-In Helpers

The flow runtime currently includes these helper surfaces:

- `regex`
  - `regex.IsMatch(input, pattern, ignoreCase = false)`
  - `regex.Match(input, pattern, group = 0, ignoreCase = false)`
  - `regex.Matches(input, pattern, group = 0, ignoreCase = false)`
- `encoding`
  - base64 encode/decode
  - URL encode/decode
- `crypto`
  - `Md5`
  - `Sha1`
  - `Sha256`
- `json`
  - parse, stringify, selector lookup
- `random`
  - GUID and numeric generation
- `time`
  - local and UTC timestamps
- `tests`
  - script-driven assertions
- `console`
  - info, warning, and error log capture

These helpers are already enough for a large class of security-oriented workflows such as:

- token scraping from HTML or JSON fragments
- replaying server-issued values into later requests
- chained login / probe / follow-up sends
- HMAC or digest-like preprocessing using built-in hash helpers
- base64 or URL-encoded payload shaping

## Security Workflow Patterns

For cybersecurity specialists, the current language/runtime is already suitable for:

- exploit payload staging where the request body, content type, headers, and URL are mutated between sends
- chained reconnaissance or validation requests with bounded iteration
- extracting anti-CSRF tokens, opaque IDs, or session markers with regex or JSON access
- carrying values between requests through locals or runtime variables
- instrumenting scripted checks with `tests` and `console`

Example pattern:

```frs
name "Probe Chain"
method GET
url "https://target.example.test/bootstrap"
max_send_iterations 4

let bootstrap = request.send()
let csrf = regex.Match(bootstrap.body, "csrf=([A-Za-z0-9_-]+)", 1)
runtime csrf_token = csrf

request.Method = "POST"
request.Url = "https://target.example.test/api/probe"
request.ContentType = "application/json"
request.headers["X-CSRF"] = "{{csrf_token}}"
request.Body = """
{
  "payload": "test"
}
"""

let probe = request.send()
expect status == 200 "probe returned 200"
```

## Compiler Output

The parser produces a structured document model.

The compiler lowers that document into an execution payload:

- `RequestDefinition`
- runtime variable seed operations
- generated response assertions
- extraction definitions
- repeat schedule
- retry policy

The current runtime then:

1. evaluates runtime seeds
2. compiles the request with variable interpolation
3. resolves auth metadata and tokens
4. sends the HTTP request
5. retries on transport failure or `5xx` when configured
6. extracts variables from the response
7. runs generated assertions
8. returns execution runs, response snapshots, console output, tests, and runtime variables

## Response Stash Roadmap

The next major response-analysis feature is a first-class stash model.

Planned direction:

- flow code will be able to stash dynamically chosen columns and values during execution
- the response pane will expose a dedicated `stash` tab
- stash data will render as a horizontally scrollable table regardless of the current inspector pane width
- users will be able to export the realized stash table to CSV

This is intentionally planned as a model-backed feature, not a UI-only table:

- execution results need a structured stash payload
- history replay needs to preserve stash rows per run
- the UI needs a stable column/value model instead of scraping console output

## Regex Extraction And Assertion Roadmap

The next language/runtime expansion for response analysis is first-class regex selectors in declarative extraction and expectation syntax.

Planned additions:

- `extract runtime token = regex body "pattern" 1`
- `extract runtime cookie = regex header "Set-Cookie" "pattern" 1`
- regex-backed expectations against body, headers, and eventually named response variables

This will complement, not replace, the current flow-level `regex.*` helper surface.

## Power-User Patterns Driving The Next Expansion

The current auth/runtime work was chosen to cover the most common request-runner patterns first:

- declarative auth attached directly to the request document
- challenge-based enterprise auth (`digest`, `ntlm`, `negotiate`)
- token acquisition that can still target non-standard header/query placement
- flow-friendly request execution where a pre-request script can still own retries, extraction, and follow-up sends

The next priority set for the language/runtime is:

- browser-based OAuth authorization code + PKCE, especially for Microsoft Entra / Azure AD B2C style interactive sign-in
- request-session controls such as persistent cookie jars and reusable authenticated sessions across chained sends
- cloud and custom signing helpers such as AWS SigV4 and common HMAC patterns
- client-certificate / mTLS request configuration
- richer multipart ergonomics for file-driven request bodies

Those items reflect the request patterns that advanced API users repeatedly rely on when they move beyond simple bearer-token calls.

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
- regex, encoding, crypto, time, random, console, and tests helper APIs
- repeat scheduling
- retry metadata
- structured post-response assertions
- compile diagnostics

Not yet included:

- browser-based authorization code + PKCE helpers
- first-class declarative regex extraction/assertion syntax
- structured response stash data and stash-panel export
- nested multi-request workflow graphs in a single document
- user-defined functions
- persistent writes back into global/workspace/environment variable stores
- plugin-provided language extensions
- cloud signing helpers such as AWS SigV4 or bespoke HMAC schemes

Those are future language/runtime expansions, not parser bugs.
