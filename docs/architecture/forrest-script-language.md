# For-Rest Script Language

Date: 2026-03-19

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

The language is intentionally declarative in v1. It compiles into an execution payload that the current HTTP runtime can execute immediately.

## Document Shape

Every document is a sequence of top-level sections. Supported sections:

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

## Example

```frs
meta {
  name = "Create Echo"
}

vars {
  request resource_id = "42"
  runtime trace_id = guid()
  runtime attempt = random(1, 4)
}

request {
  method = POST
  url = "https://api.example.test/echo/{{resource_id}}?trace={{trace_id}}"
  timeout = 15000
  redirects = false
  ssl = true
  history = true
  content_type = "application/json"
}

auth {
  mode = bearer
  token = "{{api_token}}"
}

headers {
  Accept = "application/json"
  X-Trace-Id = "{{trace_id}}"
}

body json """
{
  "id": "{{resource_id}}",
  "trace": "{{trace_id}}"
}
"""

extract {
  runtime echoed_id = json "$.payload.id"
}

tests {
  status == 201 "returns 201"
  header "Content-Type" contains "json" "json response"
  json "$.payload.trace" exists "trace exists"
}

repeat {
  count = 3
  delay = 250
  interval = 1000
}

retry {
  count = 2
  interval = 500
}
```

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

`runtime` variables are evaluated immediately before request compilation and execution. They can therefore participate in URL, header, query, and body interpolation.

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

### `auth`

Optional request auth section.

Supported modes:

- `none`
- `bearer`
- `basic`
- `apikey`

Supported keys:

- `mode`
- `username`
- `password`
- `token`
- `name`
- `value`
- `location`

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
3. sends the HTTP request
4. retries on transport failure or `5xx` when configured
5. extracts variables from the response
6. runs generated assertions
7. returns execution runs, response snapshots, console output, tests, and runtime variables

## Current v1 Boundaries

Included in v1:

- single-request document compilation
- runtime variable seeding
- request/body/header/query/auth authoring
- JSON extraction
- repeat scheduling
- retry metadata
- structured post-response assertions
- compile diagnostics

Not yet included:

- nested control-flow blocks
- multi-step workflows in a single document
- user-defined functions
- persistent writes back into global/workspace/environment variable stores
- plugin-provided language extensions

Those are future language/runtime expansions, not parser bugs.
