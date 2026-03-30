using System.Text;

namespace ForRest.Scripting;

internal static class ForRestLanguageReference
{
    private static readonly IReadOnlyList<string> RequestDirectiveKeys =
    [
        "method",
        "url",
        "timeout",
        "redirects",
        "ssl",
        "history",
        "content-type",
        "max-send-iterations",
    ];

    private static readonly IReadOnlyList<string> SupportedAuthModes =
    [
        "none",
        "bearer",
        "basic",
        "api_key",
        "header",
        "digest",
        "ntlm",
        "negotiate",
        "oauth_client_credentials",
        "oauth_device_code",
        "oauth_integrated_windows",
    ];

    private static readonly IReadOnlyList<ForRestLanguageHelpEntry> Entries =
    [
        new(
            "request",
            "request",
            "Request",
            "Group request metadata under a dedicated section.",
            "Use `request { ... }` when you want to keep request settings together. The parser also accepts top-level aliases for `method`, `url`, `timeout`, `redirects`, `ssl`, `history`, `content_type`, and `max_send_iterations`.",
            """
            request {
              method = GET
              url = "https://api.example.test/users"
            }
            """,
            ["request section", "request settings", "top-level request"],
            ["request"],
            "Keyword",
            "request {\n  $0\n}",
            true),
        new(
            "method",
            "method",
            "Request",
            "Choose the HTTP method.",
            "Supported request verbs include `GET`, `POST`, `PUT`, `PATCH`, `DELETE`, `OPTIONS`, and `HEAD`.",
            "method GET",
            ["verb", "http method"],
            ["method"],
            "Keyword",
            "method ${1|GET,POST,PUT,PATCH,DELETE,OPTIONS,HEAD|}",
            true),
        new(
            "url",
            "url",
            "Request",
            "Set the target URL template.",
            "URLs can interpolate workspace, environment, request, and runtime variables using `{{variable_name}}`.",
            "url \"https://httpbin.org/anything/{{resource_id}}?trace={{trace_id}}\"",
            ["target", "endpoint", "uri", "template"],
            ["url"],
            "Keyword",
            "url \"${1:https://httpbin.org/anything}\"",
            true),
        new(
            "timeout",
            "timeout",
            "Request",
            "Set the request timeout in milliseconds.",
            "Use `timeout` to cap how long the transport waits before failing the request.",
            "timeout 15000",
            ["request timeout", "milliseconds", "deadline"],
            ["timeout"],
            "Keyword",
            "timeout ${1:15000}",
            true),
        new(
            "redirects",
            "redirects",
            "Request",
            "Control whether redirects are followed.",
            "Set `redirects true` to follow redirect responses, or `redirects false` to inspect the first hop.",
            "redirects true",
            ["redirect", "follow redirects", "http redirect"],
            ["redirects"],
            "Keyword",
            "redirects ${1|true,false|}",
            true),
        new(
            "ssl",
            "ssl",
            "Request",
            "Control certificate validation for the request transport.",
            "Use `ssl true` for normal certificate validation. Disable it only for local or explicitly trusted environments.",
            "ssl true",
            ["tls", "certificate", "validation"],
            ["ssl", "request.ssl"],
            "Keyword",
            "ssl ${1|true,false|}",
            true),
        new(
            "history",
            "history",
            "Request",
            "Persist the response to execution history.",
            "Use `history true` when you want the run to appear in the history pane. Set it to `false` for transient probe requests that should not be retained.",
            "history true",
            ["execution history", "retain run", "history pane"],
            ["history", "request.history"],
            "Keyword",
            "history ${1|true,false|}",
            true),
        new(
            "content-type",
            "content_type",
            "Request",
            "Set the request content type.",
            "Use `content_type` to shape the outgoing body header without having to set `Content-Type` manually.",
            "content_type \"application/json\"",
            ["content type", "mime type", "body type"],
            ["content_type", "request.content_type"],
            "Keyword",
            "content_type \"${1:application/json}\"",
            true),
        new(
            "max-send-iterations",
            "max_send_iterations",
            "Request",
            "Bound how many times the script can send the request.",
            "Use `max_send_iterations` to keep `request.send()` loops finite and safe. The runtime rejects sends that exceed the configured maximum.",
            "max_send_iterations 3",
            ["send loop", "bounded loop", "retry budget"],
            ["max_send_iterations", "request.max_send_iterations"],
            "Keyword",
            "max_send_iterations ${1:3}",
            true),
        new(
            "runtime",
            "runtime",
            "Variables",
            "Create or update runtime variables from code.",
            "Runtime variables are available later in the script and in URL, header, auth, and body templates.",
            "runtime trace_id = guid()",
            ["variable", "seed", "trace", "state"],
            ["runtime"],
            "Keyword",
            "runtime ${1:name} = ${2:value}",
            true),
        new(
            "header",
            "header",
            "Request",
            "Add a request header declaration.",
            "Use `header` for declarative headers and `request.headers[...]` when you want to mutate headers from code.",
            "header \"Accept\" = \"application/json\"",
            ["headers", "request header"],
            ["header"],
            "Keyword",
            "header \"${1:Header-Name}\" = ${2:\"value\"}",
            true),
        new(
            "auth",
            "auth",
            "Auth",
            "Configure declarative request authentication.",
            "Use either an `auth { ... }` block or top-level `auth key = value` directives for bearer tokens, API keys, custom headers, challenge-based Windows auth, and OAuth token acquisition.",
            """
            auth {
              mode = oauth_client_credentials
              token_url = "https://login.example.test/oauth2/v2.0/token"
              client_id = "{{client_id}}"
              client_secret = "{{client_secret}}"
              scopes = "api://forrest/.default"
            }
            """,
            ["authorization", "oauth", "bearer", "ntlm", "negotiate", "token"],
            ["auth"],
            "Snippet",
            "auth {\n  mode = ${1|bearer,basic,api_key,header,digest,ntlm,negotiate,oauth_client_credentials,oauth_device_code,oauth_integrated_windows|}\n  $0\n}",
            true),
        new(
            "auth-mode",
            "auth mode",
            "Auth",
            "Select the authentication strategy.",
            "Supported auth modes are `none`, `bearer`, `basic`, `api_key`, `header`, `digest`, `ntlm`, `negotiate`, `oauth_client_credentials`, `oauth_device_code`, and `oauth_integrated_windows`.",
            "auth mode = bearer",
            ["auth mode", "authentication mode", "auth strategy"],
            ["auth", "auth mode"],
            "Snippet",
            "auth mode = ${1|none,bearer,basic,api_key,header,digest,ntlm,negotiate,oauth_client_credentials,oauth_device_code,oauth_integrated_windows|}",
            true),
        new(
            "auth-client-credentials",
            "oauth_client_credentials",
            "Auth",
            "Acquire an app token from a token endpoint and inject it into the request.",
            "Supports generic OAuth 2.0 client credentials against a `token_url`, or derives the v2 token endpoint from `authority` when you are targeting Microsoft identity endpoints.",
            """
            auth mode = oauth_client_credentials
            auth token_url = "https://login.example.test/oauth2/v2.0/token"
            auth client_id = "{{client_id}}"
            auth client_secret = "{{client_secret}}"
            auth scopes = "api://forrest/.default"
            """,
            ["client credentials", "machine token", "service principal"],
            ["oauth_client_credentials", "client_credentials"],
            "Snippet",
            "auth mode = oauth_client_credentials\nauth token_url = \"${1:https://login.example.test/oauth2/v2.0/token}\"\nauth client_id = \"${2:client-id}\"\nauth client_secret = \"${3:client-secret}\"\nauth scopes = \"${4:api://forrest/.default}\"",
            true),
        new(
            "auth-negotiate",
            "negotiate / ntlm / digest",
            "Auth",
            "Use challenge-based HTTP auth with default Windows credentials or an explicit account.",
            "Use `auth mode = negotiate`, `ntlm`, or `digest`. Add `use_default_credentials = true` for pass-through, or provide `username`, `password`, and optional `domain`.",
            """
            auth mode = negotiate
            auth use_default_credentials = true
            """,
            ["windows auth", "integrated auth", "ntlm", "digest"],
            ["negotiate", "ntlm", "digest"],
            "Snippet",
            "auth mode = ${1|negotiate,ntlm,digest|}\nauth use_default_credentials = true",
            true),
        new(
            "body-json",
            "body json",
            "Request",
            "Write a structured JSON body.",
            "Triple-quoted JSON blocks keep request payloads readable and work well with runtime interpolation.",
            "body json \"\"\"\n{\n  \"traceId\": \"{{trace_id}}\"\n}\n\"\"\"",
            ["payload", "json body", "request body"],
            ["body"],
            "Snippet",
            "body json \"\"\"\n${1:{\n  \\\"traceId\\\": \\\"{{trace_id}}\\\"\n}}\n\"\"\"",
            true),
        new(
            "expect",
            "expect",
            "Assertions",
            "Assert on status, headers, body content, or JSON selectors.",
            "Use `expect` to keep scripts self-checking. Failed expectations are surfaced in the debug pane and history. `expect` is a top-level assertion form, not a flow statement, so keep it outside `if`, `else`, `foreach`, and `while` blocks.",
            "expect status == 200 \"returns 200\"",
            ["assert", "test", "status", "json"],
            ["expect"],
            "Keyword",
            "expect status == ${1:200} \"${2:returns 200}\"",
            true),
        new(
            "comments",
            "# comments",
            "Flow",
            "Write comments with leading `#` lines only.",
            "ForRest treats lines starting with `#` as comments. `//` comments are not part of the language, and trailing inline comments after code are not supported.",
            """
            # Explain the next flow block.
            request.headers["X-Trace"] = trace_id
            """,
            ["comment", "comments", "#", "//"],
            ["comments", "#"],
            "Snippet",
            "# ${1:comment}",
            true),
        new(
            "if",
            "if / else if / else",
            "Flow",
            "Run code conditionally.",
            "ForRest supports single-line and multiline conditions. Use `and`, `or`, and `not (...)` for readable branching.",
            """
            if response.status == 200
               and response.uuid.length() > 10
            {
              log response.uuid
            } else {
              warn response.status
            }
            """,
            ["condition", "branch", "else", "or", "and", "not"],
            ["if", "else"],
            "Snippet",
            "if ${1:condition} {\n  $0\n}",
            true),
        new(
            "while",
            "while",
            "Flow",
            "Repeat while a condition remains truthy.",
            "Use `while` with `request.remaining_send_iterations` or response state when you need bounded scripted retries.",
            """
            while request.remaining_send_iterations > 0 {
              let sent = request.send()
              if sent.status == 200 {
                break
              }
            }
            """,
            ["loop", "repeat", "retry"],
            ["while"],
            "Snippet",
            "while ${1:condition} {\n  $0\n}",
            true),
        new(
            "foreach",
            "foreach",
            "Flow",
            "Iterate arrays, ranges, header collections, or JSON lists.",
            "Use `foreach item in source { }` for arrays and generated ranges. `for item in source { }` is also accepted as a shorthand loop header.",
            """
            let attempts = [0..2]
            foreach attempt in attempts {
              log attempt
            }
            """,
            ["loop", "iterate", "collection", "range"],
            ["foreach", "for"],
            "Snippet",
            "foreach ${1:item} in ${2:[0..2]} {\n  $0\n}",
            true),
        new(
            "request-send",
            "request.send()",
            "Request",
            "Send the current request, update the global response, and return the current response snapshot.",
            "`request.send()` is await-aware in ForRest flow and is guarded by `max_send_iterations` so scripts cannot send forever.",
            """
            let sent = request.send()
            if sent.status == 200 {
              runtime last_status = sent.status
            }
            """,
            ["send", "run", "execute", "dispatch"],
            ["request.send", "send"],
            "Method",
            "request.send()",
            false),
        new(
            "workspace-execute",
            "workspace.execute()",
            "Workspace",
            "Run another request from the current workspace and return its latest response snapshot.",
            "`workspace.execute(\"Request Name\")` reuses the current variable context, merges runtime variables from the nested request back into the caller, updates the global response, and suppresses standalone history entries for the nested helper run.",
            """
            let auth = workspace.execute("/requests/auth/token")
            request.headers["Authorization"] = $"Bearer {auth.token}"
            """,
            ["workspace", "nested request", "token helper", "helper script"],
            ["workspace.execute", "workspace.run"],
            "Method",
            "workspace.execute(\"${1:/requests/auth/token}\")",
            false),
        new(
            "stash",
            "stash",
            "Response",
            "Capture structured columns and rows for the stash tab.",
            "Assign stash columns with `stash.Column = value` or `stash[\"Column Name\"] = value`. Call `stash.Commit()` or `stash.Push()` to finalize the current row. If a branch exits before those lines run, no stash row is created for that branch.",
            """
            let sent = request.send()
            stash.Attempt = 0
            stash.Status = sent.status
            stash["UUID"] = sent.uuid
            stash.Commit()
            """,
            ["stash", "table", "csv", "response stash", "capture rows"],
            ["stash", "stash.commit", "stash.push"],
            "Property",
            "stash.${1:Column} = ${2:value}",
            true),
        new(
            "request-headers",
            "request.headers",
            "Request",
            "Mutate outgoing headers from code.",
            "Use the request API when headers depend on prior responses, loops, signatures, or runtime variables.",
            "request.headers[\"X-Trace\"] = trace_id",
            ["header mutation", "set header"],
            ["request", "request.headers"],
            "Property",
            "request.headers[\"${1:Header-Name}\"] = ${2:\"value\"}",
            true),
        new(
            "request-url",
            "request.url",
            "Request",
            "Mutate the outgoing request URL from flow code before calling `request.send()`.",
            "Use `request.url` when you need to iterate ids, paginate, or probe multiple endpoints from one bounded request script. The rendered value still needs to be an absolute URL.",
            """
            foreach todoId in [1..3] {
              request.url = $"https://jsonplaceholder.typicode.com/todos/{todoId}"
              let sent = request.send()
              log sent.id
            }
            """,
            ["request url", "mutate url", "paginate", "iterate ids", "todos", "batch url"],
            ["request.url", "request.Url"],
            "Property",
            "request.url = \"${1:https://api.example.test/items/1}\"",
            true),
        new(
            "response",
            "response",
            "Response",
            "Access the latest response snapshot and JSON properties directly.",
            "The latest response is always available through `response`. JSON properties are exposed as dynamic members, so `response.user.name` and `response.items[0].id` work directly.",
            """
            let sent = request.send()
            if sent.status == 200 {
              log sent.user.name
            }
            """,
            ["json", "response body", "dynamic"],
            ["response"],
            "Property",
            "response.status",
            false),
        new(
            "response-json",
            "response.json()",
            "Response",
            "Parse the latest response body as JSON.",
            "Use `response.json()` when you need explicit JSON node access instead of dynamic members.",
            "let payload = response.json()",
            ["json", "parse response", "body"],
            ["response.json"],
            "Method",
            "response.json()",
            false),
        new(
            "range-literal",
            "[0..9]",
            "Flow",
            "Create an inclusive numeric range literal.",
            "Range literals are inclusive and work well with `foreach`. Descending ranges such as `[3..0]` are also supported.",
            """
            let attempts = [0..2]
            foreach attempt in attempts {
              log attempt
            }
            """,
            ["range", "literal", "sequence", "loop counter"],
            ["range", "[0..9]"],
            "Snippet",
            "[${1:0}..${2:2}]",
            true),
        new(
            "range-function",
            "range(start, end)",
            "Flow",
            "Create a half-open range.",
            "`range(start, end)` behaves like `start <= value < end`. Use `[start..end]` when you want an inclusive literal.",
            "foreach index in range(0, 3) { log index }",
            ["range function", "sequence", "loop counter"],
            ["range"],
            "Function",
            "range(${1:0}, ${2:3})",
            true),
        new(
            "batch-stash-loop",
            "loop + stash pattern",
            "Flow",
            "Iterate request variants, send each one, and stash selected rows while keeping `expect` top-level.",
            "For batched probes, raise `max_send_iterations`, mutate `request.url` or headers inside `foreach`, call `request.send()`, and only `stash.Commit()` when the branch matches your condition. Leave `expect` statements after the flow block.",
            """
            max_send_iterations 20

            foreach todoId in [1..20] {
              request.url = $"https://jsonplaceholder.typicode.com/todos/{todoId}"
              let sent = request.send()
              if sent.completed {
                stash.UserId = sent.userId
                stash.TodoId = sent.id
                stash.Title = sent.title
                stash.Commit()
              }
            }

            expect status == 200 "returns 200"
            """,
            ["enumerate", "iterate", "batch", "stash rows", "completed todos", "userId", "title", "request.url"],
            ["foreach", "stash", "request.send", "request.url", "max_send_iterations"],
            "Snippet",
            "max_send_iterations 20\n\nforeach ${1:todoId} in [1..20] {\n  request.url = $\"https://jsonplaceholder.typicode.com/todos/{todoId}\"\n  let sent = request.send()\n  if sent.completed {\n    stash.UserId = sent.userId\n    stash.TodoId = sent.id\n    stash.Title = sent.title\n    stash.Commit()\n  }\n}\n\nexpect status == 200 \"returns 200\"",
            true),
        new(
            "count-alias",
            "value.length()",
            "Data",
            "Measure string length, array size, or collection count.",
            "ForRest accepts `.length()`, `.count()`, and `.size()` on strings, arrays, response collections, and JSON arrays.",
            """
            if response.items.length() > 0
               and response.user.name.length() > 2
            {
              log "response has data"
            }
            """,
            ["length", "count", "size", "collection"],
            ["length", "count", "size"],
            "Function",
            "${1:response.items}.length()",
            true),
        new(
            "logic-aliases",
            "and / or / not",
            "Flow",
            "Readable boolean operators for flow conditions.",
            "Use `and` and `or` directly. Use `not (...)` for grouped negation and simple `not value` for straightforward truthy checks.",
            """
            if response.status == 200
               or not (response.error.length() > 0)
            {
              log "continuing"
            }
            """,
            ["boolean", "operators", "logic"],
            ["and", "or", "not"],
            "Keyword",
            "and",
            false),
        new(
            "extract-regex",
            "extract runtime = regex",
            "Variables",
            "Capture response values with declarative regex selectors.",
            "Regex extraction supports `body`, `header`, and `json` sources. Use an optional capture-group index when you want a specific group instead of the full match.",
            "extract runtime token = regex body \"Bearer ([A-Za-z0-9-]+)\" 1",
            ["extract", "regex", "capture group", "response extraction"],
            ["extract", "regex"],
            "Snippet",
            "extract runtime ${1:name} = regex body \"${2:pattern}\" ${3:1}",
            true),
        new(
            "expect-regex",
            "expect ... regex",
            "Assertions",
            "Assert that response content matches a regex pattern.",
            "Regex assertions work against `body`, `header \"Name\"`, and `json \"$.path\"` targets. Use them when equality or `contains` is too weak for payload validation.",
            "expect json \"$.payload.id\" regex \"^[0-9]+$\" \"id is numeric\"",
            ["expect", "regex", "assert", "pattern match"],
            ["expect", "regex"],
            "Snippet",
            "expect body regex \"${1:pattern}\" \"${2:matches body}\"",
            true),
        new(
            "regex-match",
            "regex.Match",
            "Security",
            "Extract a regex match or capture group.",
            "Useful for token extraction, header inspection, and response probing workflows.",
            "let token = regex.Match(response.body, \"Bearer\\\\s+([\\\\w-]+)\", 1)",
            ["security", "capture", "extract", "regex"],
            ["regex"],
            "Function",
            "regex.Match(${1:input}, ${2:pattern}, ${3:0})",
            true),
        new(
            "crypto-sha256",
            "crypto.Sha256",
            "Security",
            "Hash values directly from ForRest scripts.",
            "Built-in crypto helpers make request signing and verification workflows straightforward.",
            "let signature = crypto.Sha256(trace_id)",
            ["security", "hash", "signature", "sha256"],
            ["crypto"],
            "Function",
            "crypto.Sha256(${1:value})",
            true),
        new(
            "encoding-base64",
            "encoding.Base64Encode",
            "Data",
            "Encode or decode payload values.",
            "Encoding helpers are useful for auth headers, binary payload shims, and API compatibility testing.",
            "request.headers[\"Authorization\"] = $\"Basic {encoding.Base64Encode(\\\"user:pass\\\")}\"",
            ["base64", "encoding", "auth"],
            ["encoding"],
            "Function",
            "encoding.Base64Encode(${1:value})",
            true),
        new(
            "strings-replace",
            "strings.Replace / strings.Split",
            "Data",
            "Trim, slice, replace, split, and join script strings.",
            "Use `strings` helpers to normalize values without dropping into verbose C# string manipulation. The runtime includes `Length`, `Trim`, `Upper`, `Lower`, `Contains`, `StartsWith`, `EndsWith`, `Replace`, `Substring`, `Split`, and `Join`.",
            "let slug = strings.Lower(strings.Replace(strings.Trim(\"  Alpha Beta  \"), \" \", \"-\"))",
            ["strings", "replace", "split", "join", "trim"],
            ["strings"],
            "Function",
            "strings.Replace(${1:value}, ${2:oldValue}, ${3:newValue})",
            true),
        new(
            "convert-to-bool",
            "convert.ToBool / convert.ToInt",
            "Data",
            "Convert strings, numbers, booleans, and JSON scalars safely.",
            "Use `convert` helpers when request variables or response fields arrive as strings and you need stable numeric or boolean coercion. The runtime includes `ToString`, `ToBool`, `ToInt`, `ToLong`, `ToDouble`, and `ToDecimal`.",
            "let enabled = convert.ToBool(variables.Get(\"feature_enabled\"))",
            ["convert", "cast", "bool", "int", "number"],
            ["convert"],
            "Function",
            "convert.ToBool(${1:value})",
            true),
        new(
            "time-parse",
            "time.Parse / time.Format",
            "Data",
            "Parse, format, shift, and convert timestamps.",
            "Use `time.UtcNow` or `time.Now` for the current clock, `time.Parse(...)` for ISO-like inputs, `time.Format(...)` for stable output, `time.AddDays/Hours/Minutes/Seconds(...)` for offsets, and `time.UnixSeconds/FromUnixSeconds(...)` when an API speaks Unix time.",
            "let expires = time.Format(time.AddMinutes(time.Parse(\"2026-03-29T10:15:00Z\"), 30), \"yyyy-MM-dd HH:mm\")",
            ["time", "date", "timestamp", "unix", "utc"],
            ["time"],
            "Function",
            "time.Parse(\"${1:2026-03-29T10:15:00Z}\")",
            true),
    ];

    public static IReadOnlyList<ForRestLanguageHelpEntry> GetEntries()
    {
        return Entries;
    }

    public static string BuildMarkdownReference()
    {
        StringBuilder builder = new();
        builder.AppendLine("# ForRest Script Language");
        builder.AppendLine();
        builder.AppendLine("Canonical source: `ForRestLanguageCatalog`.");
        builder.AppendLine("This document mirrors the Monaco help catalog and the prompt context built from the same entry list.");
        builder.AppendLine();
        builder.AppendLine("## Request Surface");
        builder.AppendLine();
        builder.AppendLine("| Setting | Notes |");
        builder.AppendLine("| --- | --- |");
        builder.AppendLine("| `method` | HTTP verb for the request |");
        builder.AppendLine("| `url` | Request target template |");
        builder.AppendLine("| `timeout` | Timeout in milliseconds |");
        builder.AppendLine("| `redirects` | Follow redirect hops |");
        builder.AppendLine("| `ssl` | Control certificate validation |");
        builder.AppendLine("| `history` | Persist the run to history |");
        builder.AppendLine("| `content_type` | Set the outgoing content type |");
        builder.AppendLine("| `max_send_iterations` | Bound `request.send()` loops |");
        builder.AppendLine();
        builder.AppendLine("Supported top-level aliases:");
        builder.AppendLine();
        builder.AppendLine("- `method`, `url`, `timeout`, `redirects`, `ssl`, `history`, `content_type`, and `max_send_iterations` can be written at the top level or under `request`.");
        builder.AppendLine("- `request.ssl`, `request.history`, and `request.max_send_iterations` are valid aliases and are kept in the same source of truth.");
        builder.AppendLine();
        builder.AppendLine("## Auth Surface");
        builder.AppendLine();
        builder.AppendLine("| Mode | Notes |");
        builder.AppendLine("| --- | --- |");
        builder.AppendLine("| `none` | No auth |");
        builder.AppendLine("| `bearer` | Bearer token in the request |");
        builder.AppendLine("| `basic` | Basic auth username and password |");
        builder.AppendLine("| `api_key` | API key in a header or query parameter |");
        builder.AppendLine("| `header` | Raw token copied to a custom header |");
        builder.AppendLine("| `digest` | Digest auth handshake |");
        builder.AppendLine("| `ntlm` | NTLM challenge auth |");
        builder.AppendLine("| `negotiate` | Integrated Windows auth handshake |");
        builder.AppendLine("| `oauth_client_credentials` | Client credentials token acquisition |");
        builder.AppendLine("| `oauth_device_code` | Device code auth flow |");
        builder.AppendLine("| `oauth_integrated_windows` | Windows integrated token acquisition |");
        builder.AppendLine();
        builder.AppendLine("## Catalog Entries");
        builder.AppendLine();
        AppendEntriesByCategory(builder);
        builder.AppendLine();
        builder.AppendLine("## Current Gap");
        builder.AppendLine();
        builder.AppendLine("- `switch` / `case` / `default` are not currently part of the flow compiler. Use `if`, `while`, `foreach`, and `range(...)` instead.");
        return builder.ToString().Trim();
    }

    public static string BuildPromptContext()
    {
        StringBuilder builder = new();
        builder.AppendLine("ForRest language reference.");
        builder.AppendLine("Use the exact syntax from the canonical catalog below.");
        builder.AppendLine();
        AppendPromptSection(builder, "Request surface", RequestDirectiveKeys.Select(key => Entries.First(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))));
        builder.AppendLine();
        AppendPromptSection(builder, "Auth modes", SupportedAuthModes.Select(mode => new ForRestLanguageHelpEntry(
            mode,
            mode,
            "Auth",
            $"Support the `{mode}` auth mode.",
            string.Empty,
            string.Empty,
            [mode],
            [mode],
            "Keyword",
            mode,
            false)));
        builder.AppendLine();
        AppendPromptSection(builder, "Core flow", Entries.Where(entry => entry.Category is "Flow" or "Workspace" or "Response"));
        builder.AppendLine();
        AppendPromptSection(builder, "Helpers", Entries.Where(entry => entry.Category is "Data" or "Security" or "Variables"));
        builder.AppendLine();
        builder.AppendLine("If a requested feature is not listed, treat it as unsupported and prefer the documented syntax.");
        return builder.ToString().Trim();
    }

    private static void AppendEntriesByCategory(StringBuilder builder)
    {
        IReadOnlyList<string> categoryOrder =
        [
            "Request",
            "Auth",
            "Variables",
            "Flow",
            "Workspace",
            "Response",
            "Data",
            "Security",
            "Assertions",
        ];

        foreach (string category in categoryOrder)
        {
            List<ForRestLanguageHelpEntry> entries = Entries
                .Where(entry => string.Equals(entry.Category, category, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (entries.Count == 0)
            {
                continue;
            }

            builder.AppendLine($"## {category}");
            builder.AppendLine();
            foreach (ForRestLanguageHelpEntry entry in entries)
            {
                AppendEntry(builder, entry);
            }
        }
    }

    private static void AppendEntry(StringBuilder builder, ForRestLanguageHelpEntry entry)
    {
        builder.AppendLine($"### `{entry.Title}`");
        builder.AppendLine(entry.Summary);
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(entry.Documentation))
        {
            builder.AppendLine(entry.Documentation);
            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(entry.Example))
        {
            builder.AppendLine("Example:");
            builder.AppendLine("```frs");
            builder.AppendLine(entry.Example);
            builder.AppendLine("```");
            builder.AppendLine();
        }

        if (entry.SearchTerms.Count > 0)
        {
            builder.AppendLine($"Search terms: {string.Join(", ", entry.SearchTerms)}");
            builder.AppendLine();
        }
    }

    private static void AppendPromptSection(StringBuilder builder, string title, IEnumerable<ForRestLanguageHelpEntry> entries)
    {
        builder.AppendLine($"## {title}");
        foreach (ForRestLanguageHelpEntry entry in entries)
        {
            builder.AppendLine($"- `{entry.Title}`: {entry.Summary}");
        }
    }
}
