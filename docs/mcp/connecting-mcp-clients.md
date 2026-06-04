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

## Security notes

- Bind to `127.0.0.1` unless you have a specific reason to expose the server on
  the network. On `0.0.0.0`, always set an `auth_token`.
- The raw `settings.toml` holds secrets (`auth_token`, the AI `api_key`, and the
  license key). The MCP server redacts these values when it surfaces settings to
  a client, but treat the file itself as sensitive.
