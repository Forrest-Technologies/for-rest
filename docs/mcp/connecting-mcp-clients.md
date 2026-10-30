# Connecting an MCP Client to For-Rest

Date: 2026-06-04

For-Rest can expose itself to external AI agents through the
[Model Context Protocol](https://modelcontextprotocol.io). On a desktop build,
the app self-hosts an MCP server over the **Streamable HTTP** transport so
clients like Claude Desktop, Claude Code, Cursor, or any `mcp-remote`-capable
tool can read the language docs, list workspaces, and edit the active request.

> The MCP server is **desktop-only** and **disabled by default**. You opt in
> intentionally. There is no MCP server on the Android/mobile build.

## 1. Enable the server

Open `settings.toml` (the **CFG** tab) and set `mcp.enabled = true`. The full
section, with its defaults, looks like this:

```toml
# MCP server (desktop-only). Exposes a Model Context Protocol endpoint over
# Streamable HTTP (http://<bind_address>:<port>/) so external agents can read
# the docs, list workspaces, and edit the active request.
[mcp]
enabled = true
bind_address = "127.0.0.1"
port = 7341
auth_token = ""
max_concurrent_sessions = 4
```

| Key | Default | Purpose |
| --- | --- | --- |
| `enabled` | `false` | Master switch. The server never starts while this is false. |
| `bind_address` | `127.0.0.1` | Interface to bind. Keep it loopback unless you understand the exposure; `0.0.0.0` accepts LAN connections. |
| `port` | `7341` | TCP port the Streamable HTTP endpoint listens on. |
| `auth_token` | `""` | Optional shared secret. When set, every request must carry `Authorization: Bearer <token>`. |
| `max_concurrent_sessions` | `4` | Hard cap on simultaneous in-flight requests. |

With the defaults above, the endpoint is:

```text
http://127.0.0.1:7341/
```

## 2. Add it to your client's `mcpServers`

Most modern MCP clients accept a Streamable HTTP server directly. Paste this
into your client's MCP configuration (for Claude Desktop, that's
`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "for-rest": {
      "type": "http",
      "url": "http://127.0.0.1:7341/"
    }
  }
}
```

If you set an `auth_token`, add the bearer header:

```json
{
  "mcpServers": {
    "for-rest": {
      "type": "http",
      "url": "http://127.0.0.1:7341/",
      "headers": {
        "Authorization": "Bearer YOUR_TOKEN_HERE"
      }
    }
  }
}
```

### Claude Code (CLI)

```bash
claude mcp add --transport http for-rest http://127.0.0.1:7341/
# with an auth token:
claude mcp add --transport http for-rest http://127.0.0.1:7341/ \
  --header "Authorization: Bearer YOUR_TOKEN_HERE"
```

### stdio-only clients (via `mcp-remote`)

Some clients only speak the stdio transport. Bridge them to the HTTP endpoint
with [`mcp-remote`](https://www.npmjs.com/package/mcp-remote):

```json
{
  "mcpServers": {
    "for-rest": {
      "command": "npx",
      "args": ["mcp-remote", "http://127.0.0.1:7341/"]
    }
  }
}
```

Append `--header "Authorization: Bearer YOUR_TOKEN_HERE"` to the `args` when an
`auth_token` is configured.

## 3. Restart the client

After saving the config, restart (or reconnect) your MCP client. For-Rest must
be running with `mcp.enabled = true` for the connection to succeed. Once
connected, the agent can browse the For-Rest script-language docs, enumerate
workspaces and requests, and edit/execute the active request through the
exposed tools.

## Curl input limits

Curl imports accept inline input. File and stdin inputs, multipart forms, and
file uploads return an import error so they cannot silently become a different
request. Paste input contents directly; use `--data-raw` when a body starts with
a literal `@`.

## Repeated secret names

MCP and in-app AI reads mask each secret declaration. An unchanged redacted
copy restores the original document, including separate values that share a
name. Source edits are rejected when the document has repeated secret names;
rename those declarations locally to distinct names before editing through an
agent. This prevents one scope's credential from overwriting another's.

## Browser automation

Open the visible browser pane with `show_in_app` using `target: "browser"`, then
call `browser_snapshot` to inspect the page and choose targets. Windows and Mac
Catalyst connect this pane to the desktop MCP tools. A snapshot reports
`Available: false` while the pane is unavailable or human takeover is active.

Use the pane's takeover control to suspend synthetic input and resume it when
ready. Mac Catalyst uses JavaScript events; websites that require trusted OS
input can reject those events.

## Imported Postman variables

Collection-variable values are copied into each imported request so its URL,
headers, and body can resolve without running a separate variables document.
The "Collection variables" document is a reference copy; edit the declarations
in each request to change those copies. Disabled entries and names outside the
script language's ASCII identifier syntax are skipped. Credential names and variables marked with `type: "secret"` use secret declarations for
source redaction. Values still follow the app head's storage rules.

## Security notes

Curl imports put passwords, bearer tokens, and recognized credential-header
values in explicit `secret` declarations. Request fields reference those
variables so shared source and MCP reads can mask their values while request
preparation resolves the original credentials. This does not encrypt MAUI's
workbench JSON or detect every credential in arbitrary request text.

Source-sharing helpers mask declaration-like `secret name = value` lines even
when the name is temporarily invalid during editing. The compiler still
validates identifier syntax; masking a line does not make that script runnable.

The MAUI workbench writes script text to `request-workbench-state.json` and
keeps execution history in memory for the app session. Inline credentials can
remain in that JSON file and in `settings.toml`. Provider OAuth tokens use the
separate MAUI SecureStorage token store. The Windows WinUI head uses SQLite,
where specific authentication fields and marked variables receive DPAPI
protection. See the [storage details in the README](../../README.md).

Native MCP clients may omit `Origin`. When that header is present, its scheme,
host, and port must match the configured endpoint. Requests with other origins,
credentials, non-root paths, queries, fragments, or multiple origin values receive
HTTP 403. Wildcard bindings reject requests that carry `Origin`. Bearer
authentication is checked separately. This follows the
[MCP transport requirements](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports#security-warning).

Script reads mask `secret` declarations in the editable `.frs` source. The
`get_script` response represents nonempty cached pre-request code with
`preRequestScript: "***"` and `preRequestScriptRedacted: true`. Cached code can
contain credentials from compilation or earlier edits. The editable source
remains available in `source`.

The Windows SQLite repository protects OAuth client secrets and custom
authentication-header values with DPAPI, alongside passwords, bearer tokens,
API-key values, and marked variables. Existing plaintext authentication fields
are protected on the next save. MAUI's JSON workbench uses separate storage;
this SQLite change does not encrypt those files.

- Bind to `127.0.0.1` unless you have a specific reason to expose the server on
  the network. On `0.0.0.0`, always set an `auth_token`.
- The raw `settings.toml` holds secrets (`auth_token` and the AI `api_key`).
  The MCP server redacts these values when it surfaces settings to a client,
  but treat the file itself as sensitive.
