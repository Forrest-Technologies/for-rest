using System.Text.Json;

namespace ForRest.Scripting;

public sealed record ForRestLanguageHelpEntry(
    string Key,
    string Title,
    string Category,
    string Summary,
    string Documentation,
    string Example,
    IReadOnlyList<string> SearchTerms,
    IReadOnlyList<string> HoverTerms,
    string MonacoCompletionKind,
    string InsertText,
    bool InsertAsSnippet);

public static class ForRestLanguageCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly IReadOnlyList<ForRestLanguageHelpEntry> Entries =
    [
        new(
            "name",
            "name",
            "Request",
            "Name the current ForRest program.",
            "Use `name` once near the top of the file to give the script a readable request name.",
            "name \"Get Users Feed\"",
            ["request name", "title", "program name"],
            ["name"],
            "Keyword",
            "name \"${1:Request Name}\"",
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
            "runtime",
            "runtime",
            "Variables",
            "Create or update runtime variables from code.",
            "Runtime variables are available later in the script and in URL, header, and body templates.",
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
            auth mode = oauth_client_credentials
            auth token_url = "https://login.example.test/oauth2/v2.0/token"
            auth client_id = "{{client_id}}"
            auth client_secret = "{{client_secret}}"
            auth scopes = "api://forrest/.default"
            auth header_name = "Authorization"
            """,
            ["authorization", "oauth", "bearer", "ntlm", "negotiate", "token"],
            ["auth"],
            "Snippet",
            "auth mode = ${1|bearer,api_key,header,digest,ntlm,negotiate,oauth_client_credentials,oauth_device_code,oauth_integrated_windows|}",
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
            auth header_name = "X-Access-Token"
            auth scheme = ""
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
            "Use `auth mode = negotiate`, `ntlm`, or `digest`. Add `auth use_default_credentials = true` for pass-through, or provide `username`, `password`, and optional `domain`.",
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
            "Use `expect` to keep scripts self-checking. Failed expectations are surfaced in the debug pane and history.",
            "expect status == 200 \"returns 200\"",
            ["assert", "test", "status", "json"],
            ["expect"],
            "Keyword",
            "expect status == ${1:200} \"${2:returns 200}\"",
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
    ];

    public static IReadOnlyList<ForRestLanguageHelpEntry> GetEntries()
    {
        return Entries;
    }

    public static string BuildMonacoCatalogJson()
    {
        IReadOnlyList<ForRestMonacoLanguageEntry> entries =
        [
            .. Entries.Select(
                entry => new ForRestMonacoLanguageEntry(
                    entry.Key,
                    entry.Title,
                    entry.Category,
                    entry.Summary,
                    entry.Documentation,
                    entry.Example,
                    entry.SearchTerms,
                    entry.HoverTerms,
                    entry.MonacoCompletionKind,
                    entry.InsertText,
                    entry.InsertAsSnippet)),
        ];

        return JsonSerializer.Serialize(entries, JsonOptions);
    }

    private sealed record ForRestMonacoLanguageEntry(
        string Key,
        string Label,
        string Category,
        string Summary,
        string Documentation,
        string Example,
        IReadOnlyList<string> SearchTerms,
        IReadOnlyList<string> HoverTerms,
        string Kind,
        string InsertText,
        bool InsertAsSnippet);
}
