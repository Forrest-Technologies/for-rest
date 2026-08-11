# Security Policy

For-Rest is local-first by design: workspaces, run history, and secrets live in local SQLite, secret values are encrypted at the storage boundary (DPAPI on Windows), and there is no cloud backend or telemetry. That makes the security of the local boundaries the whole ballgame — reports about them are taken seriously.

## Supported versions

| Version | Supported |
|---|---|
| Latest [GitHub release](https://github.com/Forrest-Technologies/for-rest/releases) | Yes |
| `develop` branch (HEAD) | Yes |
| Older releases | No — please upgrade and re-test before reporting |

## Reporting a vulnerability

**Do not open a public issue for security vulnerabilities.**

Report privately via GitHub's private vulnerability reporting: on the repo's **Security** tab, click **Report a vulnerability** ([direct link](https://github.com/Forrest-Technologies/for-rest/security/advisories/new)). Include reproduction steps, the platform (Windows / Android / macOS), and the commit or release you tested.

You should get an acknowledgment within a few days. Please give us a reasonable window to fix the issue before public disclosure.

## Areas of special interest

Reports touching these boundaries are especially valuable:

- **Secret storage** — DPAPI-backed encryption in `src/ForRest.Infrastructure.Sqlite/`, and the redaction that keeps secret values out of anything an AI provider or MCP client can read.
- **Script execution** — the `.frs` → Roslyn compilation and execution host in `src/ForRest.Scripting/`: escapes from the curated global surface, or leakage of UI/repository types into scripts.
- **MCP server exposure** — the Streamable HTTP endpoint in `src/ForRest.Mcp/`: auth-token bypass, bind-address issues, or tools reachable by unauthorized clients.

## No bounty program

For-Rest does not run a bug bounty. We'll gladly credit you in the advisory and release notes if you'd like.
