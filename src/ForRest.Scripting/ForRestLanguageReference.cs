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
            "Use `request { ... }` when you want to keep request settings together. At the top level, configure request metadata with bare directives like `method`, `url`, `timeout`, `redirects`, `ssl`, `history`, `content_type`, and `max_send_iterations`. Dotted members like `request.method` and `request.url` are reserved for flow mutations between `request.send()` calls.",
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
            "user_agent",
            "user_agent",
            "Request",
            "Set the User-Agent header from a preset or custom value.",
            "Available presets: `None`, `Chrome`, `Firefox`, `Safari`, `Edge`, `Curl`, `Custom`. When set to `Custom`, provide a `custom_user_agent` value. The header is applied automatically during request compilation.",
            """
            user_agent Chrome
            """,
            ["user agent", "browser", "ua", "chrome", "firefox", "safari", "edge", "curl"],
            ["user_agent"],
            "Keyword",
            "user_agent ${1|None,Chrome,Firefox,Safari,Edge,Curl,Custom|}",
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
            ["content_type"],
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
            "switch",
            "switch / case / default",
            "Flow",
            "Branch on a value with multiple cases.",
            "Use `switch expression { case value { ... } default { ... } }` to match a value against multiple cases. Compiles to an if/else chain. Each `case` tests equality against the switch expression. The `default` block runs when no case matches.",
            """
            switch response.status {
              case 200 {
                log "OK"
              }
              case 404 {
                warn "Not found"
              }
              default {
                error "Unexpected status"
              }
            }
            """,
            ["switch", "case", "default", "branch", "match"],
            ["switch", "case", "default"],
            "Snippet",
            "switch ${1:expression} {\n  case ${2:value} {\n    $0\n  }\n  default {\n    \n  }\n}",
            true),
        new(
            "secret",
            "secret",
            "Flow",
            "Declare a secret variable that is encrypted at rest and hidden from AI.",
            "Use `secret` in the vars section or flow code to store sensitive values like tokens and API keys. Secrets are encrypted with DPAPI, marked `IsSecret` on the variable, and redacted from AI context. In flow code, `secret name = expression` compiles like `runtime` but with secret protection.",
            """
            secret api_key = "bearer sk-abc123"
            secret token = response.json().access_token
            """,
            ["secret", "sensitive", "encrypt", "hidden", "token", "credential"],
            ["secret"],
            "Snippet",
            "secret ${1:name} = ${2:value}",
            true),
        new(
            "collection-methods",
            "collection methods",
            "Flow",
            "Chain LINQ-like operations on arrays and lists.",
            "Use dot-chained methods on collections returned from JSON responses, ranges, or variables. Available methods: `.where(x => condition)`, `.select(x => x.field)`, `.first()`, `.firstOrDefault()`, `.last()`, `.any()`, `.all(x => condition)`, `.count()`, `.orderBy(x => x.field)`, `.orderByDesc(x => x.field)`, `.take(n)`, `.skip(n)`, `.distinct()`, `.flatten()`, `.groupBy(x => x.field)`, `.sum()`, `.min()`, `.max()`, `.average()`, `.toList()`, `.reverse()`, `.contains(value)`. Lambda expressions use `x => expr` syntax. Positional access also works through indexing — `parts[0]` is the first element and `.first()` / `.last()` are the predicate-friendly equivalents — so the result of `strings.Split(...)`, a JSON array, or a range can be read either way.",
            """
            let users = response.json().data
            let active = users.where(x => x.active).select(x => x.name)
            let total = users.count()
            let first = users.first(x => x.role == "admin")
            let sorted = users.orderBy(x => x.created_at).take(5)
            """,
            ["where", "select", "first", "last", "any", "all", "count", "orderBy", "take", "skip", "distinct", "flatten", "groupBy", "sum", "min", "max", "average", "toList", "reverse", "contains", "linq", "filter", "map", "lambda", "collection", "chain"],
            ["where", "select", "first", "last", "any", "all", "orderBy", "take", "skip", "distinct", "flatten", "groupBy", "toList"],
            "Snippet",
            "${1:items}.where(${2:x} => ${3:condition})",
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
            "request-method",
            "request.method",
            "Request",
            "Mutate the outgoing HTTP method from flow code.",
            "Use `request.method` when one scripted request needs to probe `GET`, `POST`, `PUT`, `PATCH`, or `DELETE` variants between `request.send()` calls.",
            """
            request.method = "POST"
            request.content_type = "application/json"
            request.body = "{\"name\":\"Validation Widget\"}"
            let sent = request.send()
            log sent.status
            """,
            ["request method", "mutate method", "post put patch delete", "crud"],
            ["request.method", "request.Method"],
            "Property",
            "request.method = \"${1|GET,POST,PUT,PATCH,DELETE,OPTIONS,HEAD|}\"",
            true),
        new(
            "request-body",
            "request.body",
            "Request",
            "Mutate the outgoing raw request body from flow code.",
            "Use `request.body` when POST, PUT, or PATCH payloads depend on earlier responses, runtime variables, or multi-step scripted workflows. Pair it with `request.content_type` for JSON payloads.",
            """
            runtime item_name = "Validation Widget"
            request.body = $"{{\"name\":\"{item_name}\"}}"
            """,
            ["request body", "payload mutation", "post body", "patch body"],
            ["request.body", "request.Body"],
            "Property",
            "request.body = \"${1:{\\\"name\\\":\\\"demo\\\"}}\"",
            true),
        new(
            "request-content-type",
            "request.content_type",
            "Request",
            "Mutate the outgoing content type from flow code.",
            "Use `request.content_type` when a scripted workflow changes the body format before the next `request.send()` call.",
            "request.content_type = \"application/json\"",
            ["request content type", "content type mutation", "json payload"],
            ["request.content_type", "request.ContentType"],
            "Property",
            "request.content_type = \"${1:application/json}\"",
            true),
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
            "Use `response.json()` when you need explicit JsonNode access such as indexers or `AsArray()`. Do not use dot-member access on the returned value.",
            "let payload = response.json()",
            ["json", "parse response", "body"],
            ["response.json"],
            "Method",
            "response.json()",
            false),
        new(
            "response-array",
            "foreach item in response",
            "Response",
            "Iterate array-root JSON bodies directly from `response`.",
            "When the latest response body root is a JSON array, iterate `response` directly or use `response[index]` instead of inventing wrapper properties.",
            """
            let sent = request.send()
            foreach item in response {
              log item.name
            }
            """,
            ["json array", "root array", "response[0]", "iterate response"],
            ["response", "response[0]", "foreach item in response"],
            "Snippet",
            """
            foreach ${1:item} in response {
              $0
            }
            """,
            true),
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
            "api-surface-crud",
            "CRUD send loop",
            "Flow",
            "Exercise multiple API methods from one request script while verifying and stashing each step.",
            "For API-surface tests, mutate `request.method`, `request.url`, `request.content_type`, and `request.body` between `request.send()` calls. Use `tests.Assert(...)` or `tests.Equal(...)` inside flow when you need per-step verification, stash the interesting fields per step, and leave `expect` statements top-level for the final response snapshot.",
            """
            timeout 15000
            max_send_iterations 8
            redirects true
            ssl true
            history true

            runtime trace_id = guid()
            header "Accept" = "application/json"
            header "X-Correlation-Id" = "{{trace_id}}"

            let created_name = $"ForRest Widget {trace_id}"
            let patched_name = $"ForRest Widget Updated {trace_id}"

            log $"Trace {trace_id}: GET /objects"
            request.method = "GET"
            request.url = "https://api.restful-api.dev/objects"
            let sent = request.send()
            tests.Assert(sent.status >= 200 and sent.status < 300, "list returns 2xx")
            tests.Assert(sent.length() >= 3, "list returns at least 3 objects")
            let sample_id_a = convert.ToString(sent[0].id)
            let sample_id_b = convert.ToString(sent[1].id)
            let sample_id_c = convert.ToString(sent[2].id)
            stash.Step = "list"
            stash.Status = sent.status
            stash.Count = sent.length()
            stash.SampleIds = $"{sample_id_a},{sample_id_b},{sample_id_c}"
            stash.Trace = trace_id
            stash.Commit()

            log $"Trace {trace_id}: GET /objects?id=..."
            request.url = $"https://api.restful-api.dev/objects?id={sample_id_a}&id={sample_id_b}&id={sample_id_c}"
            sent = request.send()
            tests.Assert(sent.status >= 200 and sent.status < 300, "filtered list returns 2xx")
            tests.Equal(3, sent.length(), "filtered list returns requested ids")
            stash.Step = "filtered-list"
            stash.Status = sent.status
            stash.Count = sent.length()
            stash.FirstId = sent[0].id
            stash.Commit()

            log $"Trace {trace_id}: GET /objects/{sample_id_c}"
            request.url = $"https://api.restful-api.dev/objects/{sample_id_c}"
            sent = request.send()
            tests.Assert(sent.status >= 200 and sent.status < 300, "single object returns 2xx")
            tests.Equal(sample_id_c, convert.ToString(sent.id), "single object returns requested id")
            stash.Step = "single"
            stash.Status = sent.status
            stash.ObjectId = sent.id
            stash.Name = sent.name
            stash.Commit()

            log $"Trace {trace_id}: POST /objects"
            request.method = "POST"
            request.url = "https://api.restful-api.dev/objects"
            request.content_type = "application/json"
            request.body = $"{{\"name\":\"{created_name}\",\"data\":{{\"year\":2026,\"price\":1849.99,\"CPU model\":\"Trace CPU\",\"Hard disk size\":\"1 TB\"}}}}"
            sent = request.send()
            tests.Assert(sent.status >= 200 and sent.status < 300, "create returns 2xx")
            tests.Equal(created_name, convert.ToString(sent.name), "create echoes name")
            let created_id = sent.id
            stash.Step = "create"
            stash.Status = sent.status
            stash.ObjectId = created_id
            stash.Name = sent.name
            stash.CreatedAt = sent.createdAt
            stash.Commit()

            log $"Trace {trace_id}: PUT /objects/{created_id}"
            request.method = "PUT"
            request.url = $"https://api.restful-api.dev/objects/{created_id}"
            request.body = $"{{\"name\":\"{created_name}\",\"data\":{{\"year\":2026,\"price\":2049.99,\"CPU model\":\"Trace CPU\",\"Hard disk size\":\"1 TB\",\"color\":\"silver\"}}}}"
            sent = request.send()
            tests.Assert(sent.status >= 200 and sent.status < 300, "put returns 2xx")
            tests.Equal(2049.99, convert.ToDouble(sent.data.price), "put replaces price")
            tests.Equal("silver", convert.ToString(sent.data.color), "put adds color")
            stash.Step = "put"
            stash.Status = sent.status
            stash.ObjectId = sent.id
            stash.Price = sent.data.price
            stash.Color = sent.data.color
            stash.UpdatedAt = sent.updatedAt
            stash.Commit()

            log $"Trace {trace_id}: PATCH /objects/{created_id}"
            request.method = "PATCH"
            request.url = $"https://api.restful-api.dev/objects/{created_id}"
            request.body = $"{{\"name\":\"{patched_name}\"}}"
            sent = request.send()
            tests.Assert(sent.status >= 200 and sent.status < 300, "patch returns 2xx")
            tests.Equal(patched_name, convert.ToString(sent.name), "patch updates name")
            stash.Step = "patch"
            stash.Status = sent.status
            stash.ObjectId = sent.id
            stash.Name = sent.name
            stash.UpdatedAt = sent.updatedAt
            stash.Commit()

            log $"Trace {trace_id}: DELETE /objects/{created_id}"
            request.method = "DELETE"
            request.url = $"https://api.restful-api.dev/objects/{created_id}"
            request.body = ""
            sent = request.send()
            tests.Assert(sent.status >= 200 and sent.status < 300, "delete returns 2xx")
            tests.Assert(strings.Contains(convert.ToString(sent.message), convert.ToString(created_id)), "delete message includes id")
            stash.Step = "delete"
            stash.Status = sent.status
            stash.ObjectId = created_id
            stash.DeleteMessage = sent.message
            stash.Commit()

            expect status == 200 "final delete returns 200"
            expect header "Content-Type" contains "json" "json response"
            """,
            ["crud", "api surface", "post put patch delete", "request.method", "request.body", "request.content_type", "restful-api.dev", "tests.assert", "tests.equal", "query ids"],
            ["request.method", "request.body", "request.content_type", "request.send", "tests", "api.restful-api.dev/objects"],
            "Snippet",
            "max_send_iterations 8\n\nrequest.method = \"GET\"\nrequest.url = \"https://api.restful-api.dev/objects\"\nlet sent = request.send()\ntests.Assert(sent.status >= 200 and sent.status < 300, \"list returns 2xx\")\nstash.Step = \"list\"\nstash.Status = sent.status\nstash.Count = sent.length()\nstash.Commit()\n\nrequest.method = \"POST\"\nrequest.url = \"https://api.restful-api.dev/objects\"\nrequest.content_type = \"application/json\"\nrequest.body = \"{\\\"name\\\":\\\"Validation Widget\\\"}\"\nsent = request.send()\nlet created_id = sent.id\ntests.Assert(sent.status >= 200 and sent.status < 300, \"create returns 2xx\")\nstash.Step = \"create\"\nstash.ObjectId = created_id\nstash.Commit()\n\nrequest.method = \"DELETE\"\nrequest.url = $\"https://api.restful-api.dev/objects/{created_id}\"\nrequest.body = \"\"\nsent = request.send()\ntests.Assert(sent.status >= 200 and sent.status < 300, \"delete returns 2xx\")\nstash.Step = \"delete\"\nstash.ObjectId = created_id\nstash.DeleteMessage = sent.message\nstash.Commit()\n\nexpect header \"Content-Type\" contains \"json\" \"json response\"",
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
            "Built-in crypto helpers (`crypto.Md5`, `crypto.Sha1`, `crypto.Sha256`) make request signing and verification workflows straightforward. They accept any flow value — strings, numbers, booleans, or JSON scalars — and coerce it to its stable text form before hashing, so you can pass a runtime variable like `trace_id` or a numeric `response.Status` without converting it first.",
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
        new(
            "retry-flow",
            "retry N with backoff { ... }",
            "Flow",
            "Retry a block of flow code with optional backoff or fixed delay.",
            "Use `retry` to wrap sends in automatic retry logic. `retry N { }` retries up to N times. `retry N with backoff { }` adds exponential backoff (100ms, 200ms, 400ms, ...). `retry N with delay M { }` waits M milliseconds between retries. Use `break` inside the block to exit early on success.",
            """
            retry 3 with backoff {
              let sent = request.send()
              if sent.status >= 200 and sent.status < 500 { break }
              warn $"Attempt returned {sent.status}, retrying..."
            }
            """,
            ["retry", "backoff", "delay", "resilience", "transient", "retry loop"],
            ["retry"],
            "Snippet",
            "retry ${1:3} with backoff {\n  let sent = request.send()\n  if sent.status >= 200 and sent.status < 500 { break }\n  warn $\"Attempt returned {sent.status}, retrying...\"\n}",
            true),
        new(
            "on-error",
            "on error { ... }",
            "Flow",
            "Declare a handler that runs when the request flow throws an exception.",
            "Place `on error { }` at the top level of your script. If any unhandled exception occurs during flow execution, the handler block runs instead of crashing the script. You can log, set stash values, or perform cleanup inside the handler. The handler shares the same runtime variables and seeds as the main flow, so you can reference values like `trace_id` directly inside the block.",
            """
            on error {
              error "Request failed unexpectedly"
              stash.Error = "true"
              stash.Commit()
            }
            """,
            ["on error", "error handler", "exception", "catch", "try"],
            ["on"],
            "Snippet",
            "on error {\n  error \"${1:Request failed}\"\n}",
            true),
        new(
            "on-status",
            "on status N { ... }",
            "Flow",
            "Declare a handler that runs when the response returns a specific HTTP status code.",
            "Place `on status <code> { }` at the top level. After the main flow completes, if the response status matches, the handler block runs. Useful for 401 re-auth, 429 rate-limit backoff, or custom error reporting. The handler shares the same runtime variables and seeds as the main flow, so values like `trace_id` are in scope without redeclaring them.",
            """
            on status 429 {
              warn "Rate limited — backing off"
              retry 2 with backoff {
                request.send()
                if response.status != 429 { break }
              }
            }

            on status 401 {
              error "Unauthorized — check your token"
            }
            """,
            ["on status", "status handler", "429", "401", "rate limit", "unauthorized"],
            ["on"],
            "Snippet",
            "on status ${1:429} {\n  warn \"${2:Rate limited}\"\n}",
            true),
        new(
            "extract-flow",
            "extract json / header / regex",
            "Flow",
            "Extract values from a response using JSON path, header name, or regex pattern.",
            "Use `let x = extract json \"$.path\" from source` to pull a JSON value, `let x = extract header \"Name\" from source` for a response header, or `let x = extract regex \"pattern\" from source.body` for regex matching. The `from` keyword specifies which response variable to extract from.",
            """
            let sent = request.send()
            let token = extract json "$.access_token" from sent
            let reqId = extract header "X-Request-Id" from sent
            let orderId = extract regex "order-(\d+)" from sent.body
            """,
            ["extract", "json path", "header", "regex", "from", "response extraction"],
            ["extract", "from"],
            "Snippet",
            "let ${1:value} = extract json \"${2:\\$.data}\" from ${3:sent}",
            true),
        new(
            "parallel-sends",
            "parallel { GET ... }",
            "Flow",
            "Send multiple requests concurrently and collect their responses.",
            "Use `let [a, b] = parallel { }` to fire multiple requests at the same time. Inside the block, each line is a request shorthand: `METHOD \"url\"`. All requests run concurrently via `Task.WhenAll`. Destructure the results into named variables for downstream use. You can also use a bare `parallel { }` without destructuring.",
            """
            let [users, posts] = parallel {
              GET "https://api.example.test/users"
              GET "https://api.example.test/posts"
            }
            log $"Users: {users.status}, Posts: {posts.status}"
            """,
            ["parallel", "concurrent", "simultaneous", "Task.WhenAll", "fan out"],
            ["parallel"],
            "Snippet",
            "let [${1:a}, ${2:b}] = parallel {\n  GET \"${3:/endpoint-a}\"\n  GET \"${4:/endpoint-b}\"\n}",
            true),
        new(
            "pipe-syntax",
            "pipe { METHOD \"url\" -> let name }",
            "Flow",
            "Chain sequential requests in a concise pipeline syntax.",
            "Use `pipe { }` to express a multi-step request workflow. Each line specifies a method, URL, optional body, and optional result binding with `-> let name`. Requests execute sequentially, and each step can reference variables from previous steps. Use `body json \"...\"` to set a JSON payload.",
            """
            pipe {
              GET "{{baseUrl}}/users" -> let users
              POST "{{baseUrl}}/report" body json "{ \"count\": 10 }" -> let report
              DELETE "{{baseUrl}}/cleanup/{{report.id}}"
            }
            """,
            ["pipe", "pipeline", "chain", "sequential", "multi-step", "workflow"],
            ["pipe"],
            "Snippet",
            "pipe {\n  GET \"${1:/endpoint}\" -> let ${2:result}\n  POST \"${3:/next}\" body json \"${4:{}}\" -> let ${5:created}\n}",
            true),
        new(
            "define-call",
            "define / call",
            "Flow",
            "Define reusable subroutines with parameters and call them by name.",
            "Use `define name with param1, param2 { }` to create a named subroutine. Call it with `call name with arg1, arg2`. Parameters are passed as dynamic values. Subroutines can contain any flow code including sends, conditionals, and loops. Define without parameters using just `define name { }`.",
            """
            define setup_auth with token_url, client_id {
              request.method = "POST"
              request.url = token_url
              let sent = request.send()
              secret access_token = sent.json().access_token
            }

            call setup_auth with "https://login.example.test/token", "{{client_id}}"
            """,
            ["define", "call", "subroutine", "function", "reuse", "parameterized"],
            ["define", "call"],
            "Snippet",
            "define ${1:name} with ${2:param1}, ${3:param2} {\n  ${4:// body}\n}\n\ncall ${1:name} with ${5:arg1}, ${6:arg2}",
            true),
        new(
            "named-send",
            "request.send() as \"label\"",
            "Flow",
            "Label a send call to tag the response snapshot with a meaningful name.",
            "Append `as \"label\"` after `request.send()` to attach a label to the response snapshot. Labels make it easy to identify specific sends in history and snapshot views. Combine with `snapshot` for persistent response storage.",
            """
            let baseline = request.send() as "baseline"
            log $"Baseline status: {baseline.status}"
            """,
            ["as", "label", "named send", "send label", "snapshot"],
            ["as"],
            "Snippet",
            "let ${1:result} = request.send() as \"${2:label}\"",
            true),
        new(
            "snapshot-save",
            "snapshot \"name\" from source",
            "Flow",
            "Save a response snapshot for later comparison or inspection.",
            "Use `snapshot \"name\" from variable` to persist a labeled copy of a response. Snapshots can be retrieved later for diff, regression testing, or audit trails. Combine with named sends for a complete response archival workflow.",
            """
            let sent = request.send() as "v1"
            snapshot "user-list-v1" from sent
            """,
            ["snapshot", "save", "persist", "archive", "response"],
            ["snapshot"],
            "Snippet",
            "snapshot \"${1:name}\" from ${2:sent}",
            true),
        new(
            "stash-columns",
            "stash columns [...]",
            "Flow",
            "Pre-declare the column layout for stash table output.",
            "Use `stash columns [\"Col1\", \"Col2\", ...]` to define the table structure before committing rows. This ensures consistent column ordering in the stash output grid, especially useful when building comparison tables across multiple requests or loop iterations.",
            """
            stash columns ["Endpoint", "Status", "Duration"]
            stash.Endpoint = "users"
            stash.Status = response.status
            stash.Duration = response.elapsed
            stash.Commit()
            """,
            ["stash", "columns", "table", "declare", "grid"],
            ["stash"],
            "Snippet",
            "stash columns [\"${1:Column1}\", \"${2:Column2}\", \"${3:Column3}\"]",
            true),
        new(
            "import-use",
            "import / use",
            "Flow",
            "Include definitions and variables from external .frs files.",
            "Use `import \"path.frs\"` to merge variables, subroutines (defines), and headers from another file. Use `use \"path.frs\"` for a lighter-weight inclusion that merges only variables. Both directives detect and prevent circular imports.",
            """
            import "shared/auth-helpers.frs"
            use "shared/variables.frs"
            """,
            ["import", "use", "include", "shared", "reuse", "module"],
            ["import", "use"],
            "Snippet",
            "import \"${1:shared/helpers.frs}\"",
            true),
        new(
            "scenario-block",
            "scenario \"name\" { ... }",
            "Flow",
            "Define named test scenarios that share the base request configuration.",
            "Use `scenario \"name\" { }` to define a named variant of the base request. Each scenario inherits the parent auth, headers, and URL, but can override them. Inside a scenario, use `auth { }`, headers, flow code, and `expect` assertions. Scenarios compile to independent execution payloads, enabling one script to test multiple cases.",
            """
            url "https://api.example.test/users"
            auth { mode = bearer; token = "{{valid_token}}" }

            scenario "happy path" {
              expect status == 200
              expect json "$.data" != null "returns data"
            }

            scenario "expired token" {
              auth { mode = bearer; token = "{{expired_token}}" }
              expect status == 401
            }
            """,
            ["scenario", "test case", "variant", "parameterized test", "matrix"],
            ["scenario"],
            "Snippet",
            "scenario \"${1:happy path}\" {\n  expect status == ${2:200} \"${3:returns expected status}\"\n}",
            true),
        new(
            "test-description",
            "tests { \"description\" }",
            "Assertions",
            "Add plain-text test descriptions alongside expect assertions.",
            "Inside a `tests { }` section, you can write bare quoted strings as human-readable test descriptions. These are emitted as comments in the compiled output and serve as documentation or future AI-generated assertion placeholders.",
            """
            tests {
              "user list returns 200 with at least one active user"
              expect status == 200 "returns 200"
            }
            """,
            ["test description", "plain text test", "documentation", "test spec"],
            ["tests"],
            "Snippet",
            "tests {\n  \"${1:description of expected behavior}\"\n  expect status == ${2:200} \"${3:assertion label}\"\n}",
            true),
        new(
            "let",
            "let",
            "Flow",
            "Declare a local variable inside flow code.",
            "Use `let` to declare a variable. Assignment can be literal, an expression, or the result of `request.send()`. Re-assignment after the initial declaration omits `let`.",
            """
            let sent = request.send()
            let name = "ForRest Widget"
            let count = sent.length()
            sent = request.send()
            """,
            ["let", "variable", "assign", "declare", "local"],
            ["let"],
            "Keyword",
            "let ${1:name} = ${0}",
            true),
        new(
            "log-warn-error",
            "log / warn / error",
            "Flow",
            "Write messages to the execution console.",
            "`log` writes an informational message, `warn` writes a warning, and `error` writes an error. All three accept a literal string or interpolated expression. These are useful for tracing script execution.",
            """
            log "Starting request"
            log $"Status: {sent.status}"
            warn $"Unexpected status {sent.status}"
            error "Request failed"
            """,
            ["log", "warn", "error", "console", "trace", "debug", "print"],
            ["log", "warn", "error"],
            "Keyword",
            "log ${0}",
            true),
        new(
            "tests-api",
            "tests.Assert / tests.Equal",
            "Assertions",
            "Programmatic test assertions inside flow code.",
            "Use `tests.Assert(condition, label)` for boolean assertions and `tests.Equal(expected, actual, label)` for equality assertions. These run inside flow code (not top-level `expect` statements) and are useful in loops or conditional branches.",
            """
            tests.Assert(sent.status >= 200 and sent.status < 300, "returns 2xx")
            tests.Equal(3, sent.length(), "returns 3 items")
            tests.Equal("silver", convert.ToString(sent.data.color), "color matches")
            """,
            ["tests", "assert", "equal", "assertion", "programmatic test"],
            ["tests.Assert", "tests.Equal"],
            "Method",
            "tests.Assert(${1:condition}, \"${0:label}\")",
            true),
        new(
            "runtime-functions",
            "guid() / now() / utc_now() / random()",
            "Variables",
            "Built-in runtime variable seed functions.",
            "These functions can only be used in `runtime` variable declarations. `guid()` produces a unique identifier, `now()` and `utc_now()` produce timestamps, and `random(min, max)` produces a random integer.",
            """
            runtime trace_id = guid()
            runtime started_at = now()
            runtime timestamp = utc_now()
            runtime attempt = random(1, 100)
            """,
            ["guid", "now", "utc_now", "random", "uuid", "timestamp", "unique"],
            ["guid()", "now()", "utc_now()", "random("],
            "Function",
            "runtime ${1:name} = guid()",
            true),
        new(
            "string-interpolation",
            "$\"...{expression}...\"",
            "Flow",
            "Interpolated string literals with embedded expressions.",
            "Use `$\"...\"` to embed variables and expressions inside string literals. Indexer and member access are supported inside `{...}` holes. Available in flow code, `log`/`warn`/`error`, and `request.body` assignments.",
            """
            log $"User {sent[0].email}"
            request.body = $"{{\"name\":\"{created_name}\"}}"
            log $"Status: {response.status}"
            """,
            ["interpolation", "string", "template", "format", "$\""],
            ["$\""],
            "Value",
            "$\"${0}\"",
            true),
        new(
            "break-continue",
            "break / continue",
            "Flow",
            "Exit or skip iterations in loops and retry blocks.",
            "Use `break` to exit the nearest enclosing `foreach`, `while`, or `retry` block. Use `continue` to skip to the next iteration. Both are commonly used inside retry blocks to stop retrying on success.",
            """
            retry 3 with backoff {
              let sent = request.send()
              if sent.status == 200 { break }
            }

            foreach item in [0..9] {
              if item == 5 { continue }
              log item
            }
            """,
            ["break", "continue", "exit loop", "stop", "skip iteration"],
            ["break", "continue"],
            "Keyword",
            "break",
            false),
        new(
            "delay",
            "delay",
            "Flow",
            "Pause execution for N milliseconds (true async delay).",
            "Use `delay <expression>` to pause the script for a precise number of milliseconds. The expression may be a literal number, a variable, or an arithmetic expression that evaluates to a number. The compiler emits `await Task.Delay((int)...)` so the pause is a real async wait — no busy-looping — and values are clamped to be non-negative. Handy for manual backoffs, rate-limit throttling, replay timing tests, or spacing out fuzzer iterations.",
            """
            # Literal pause
            delay 1000

            # Expression pause based on a runtime variable
            runtime backoff_ms = 250
            foreach attempt in [0..4] {
              delay backoff_ms * (attempt + 1)
              let sent = request.send()
              if sent.status == 200 { break }
            }
            """,
            ["delay", "sleep", "wait", "backoff", "pause", "throttle", "timing"],
            ["delay"],
            "Keyword",
            "delay ${1:1000}",
            true),
        new(
            "payloads",
            "payloads",
            "Security",
            "Curated bug bounty / security test payload catalog with custom category support.",
            "`payloads` is a script-facing helper that exposes built-in fuzzing corpora for common web vulnerability classes. Every list is documented publicly (SecLists, OWASP WSTG, Burp Intruder) and intended for *authorized* testing only. Use the named properties (`payloads.sqli`, `payloads.xss`, `payloads.path_traversal`, `payloads.command_injection`, `payloads.ssti`, `payloads.open_redirect`, `payloads.xxe`, `payloads.nosqli`, `payloads.crlf_injection`, `payloads.ssrf`) or call `payloads.Category(name)` / `payloads.Combine(...)` for dynamic lookups. Custom categories can be injected via the script host so teams can override the defaults with their own wordlists.",
            """
            # Straightforward SQLi fuzz of a query parameter
            foreach p in payloads.sqli {
              request.url = $"https://api.example.test/search?q={p}"
              let sent = request.send()
              if sent.status >= 500 { warn $"possible sqli: {p}" }
            }

            # Combine multiple categories with an extra literal, then stash findings
            foreach p in payloads.Combine("xss", "ssti", "custom-payload") {
              request.body = $"{{\"name\":\"{p}\"}}"
              let sent = request.send()
              if sent.body contains "error in template" {
                stash row = { "category": "ssti", "payload": p, "status": sent.status }
              }
            }
            """,
            ["payloads", "fuzz", "fuzzing", "sqli", "xss", "ssti", "path traversal", "command injection", "ssrf", "xxe", "bug bounty", "security", "pentest", "owasp"],
            ["payloads"],
            "Value",
            "payloads.${1|sqli,xss,path_traversal,command_injection,ssti,open_redirect,xxe,nosqli,crlf_injection,ssrf|}",
            true),
        new(
            "fuzz-loop",
            "fuzz loop",
            "Security",
            "Iterate a payload category against a named target location.",
            "ForRest does not (yet) have a dedicated `fuzz` block; use a documented `foreach` + `payloads` pattern instead. The loop can target any mutation surface: `request.url`, `request.body`, `request.headers[\"X-Name\"]`, or a nested JSON path. Pair it with `retry` for flaky hosts, `delay` for rate-limited targets, and `stash` for capturing findings.",
            """
            # Fuzz a header with an SSRF corpus and stash any reflected URLs
            foreach p in payloads.ssrf {
              request.headers["X-Forwarded-For"] = p
              delay 250                        # courteous rate limit
              let sent = request.send()
              if sent.status == 200 and sent.body contains p {
                stash row = { "header": "X-Forwarded-For", "payload": p }
              }
            }
            """,
            ["fuzz", "fuzzer", "attack", "payload loop", "intruder", "pen test"],
            ["fuzz"],
            "Snippet",
            "foreach p in payloads.${1|sqli,xss,path_traversal,command_injection,ssti,open_redirect,xxe,nosqli,crlf_injection,ssrf|} {\n  request.${2|url,body,headers[\"X-Test\"]|} = p\n  delay ${3:250}\n  let sent = request.send()\n  if sent.status >= 500 { warn $\"possible hit: {p}\" }\n}",
            true),
        new(
            "ai-providers",
            "AI providers",
            "AI",
            "Every AI provider For-Rest supports out of the box, with default endpoints and transports.",
            """
            For-Rest talks to every AI provider through the OpenAI .NET SDK, so any host that exposes an OpenAI-compatible /chat/completions (and, for some, /responses) endpoint works. Configure under `[ai]` in settings.toml:

            Supported `provider =` values (with aliases):
            - `openai`                                     default https://api.openai.com/v1, transport responses
            - `azure_openai` (alias `azure`)               endpoint required, transport responses, needs deployment_name
            - `grok` (aliases `xai`, `x-ai`, `x_ai`)       default https://api.x.ai/v1, transport responses
            - `groq`                                       default https://api.groq.com/openai/v1, transport chat
            - `deepseek`                                   default https://api.deepseek.com/v1, transport chat
            - `mistral`                                    default https://api.mistral.ai/v1, transport chat
            - `openrouter`                                 default https://openrouter.ai/api/v1, transport chat
            - `gemini` (aliases `google`, `google-gemini`) default https://generativelanguage.googleapis.com/v1beta/openai, transport responses
            - `anthropic` (alias `claude`)                 default https://api.anthropic.com/v1, transport chat
            - `custom` (alias `openai-compatible`)         endpoint required, transport chat

            Paste-the-full-URL is safe: For-Rest strips trailing `/responses` or `/chat/completions` so a base URI is what the SDK actually gets. `api =` accepts `responses`, `chat`, or `chat_completions`; leave it blank and For-Rest picks the right default for the selected provider. `custom_headers` lets you add provider-specific auth/versioning headers (see the AI custom headers entry).
            """,
            """
            [ai]
            enabled = true
            provider = "openrouter"
            api = "chat"
            model = "anthropic/claude-3.5-sonnet"
            api_key = "or-..."
            custom_headers = "HTTP-Referer: https://for-rest.dev; X-Title: For-Rest"
            """,
            [
                "ai", "provider", "openai", "azure", "azure_openai", "grok", "xai", "x.ai",
                "groq", "deepseek", "mistral", "openrouter", "gemini", "google", "anthropic",
                "claude", "custom", "openai-compatible", "settings", "endpoint", "model",
            ],
            ["provider", "ai", "openai", "grok", "groq", "deepseek", "mistral", "openrouter", "gemini", "anthropic", "claude", "custom"],
            "Keyword",
            "provider = \"${1|openai,azure_openai,grok,groq,deepseek,mistral,openrouter,gemini,anthropic,custom|}\"",
            true),
        new(
            "ai-custom-headers",
            "ai custom_headers",
            "AI",
            "Inject extra HTTP headers into every outbound AI request (OpenRouter, Anthropic, internal gateways).",
            """
            `custom_headers` under `[ai]` is a single string of `Name: value` pairs separated by `;` or newlines. For-Rest parses them into a dictionary and installs a per-try pipeline policy on the OpenAIClient that calls `request.Headers.Set(name, value)` on every outbound AI call.

            Use it when a provider or gateway needs headers that are not the bearer token:
            - OpenRouter ranking credit: `HTTP-Referer: https://your.app; X-Title: Your App`
            - Anthropic API versioning: `anthropic-version: 2023-06-01`
            - Internal gateways / tenancy: `X-Tenant: acme; X-Trace: for-rest`

            Header names are case-insensitive; later entries overwrite earlier ones. Empty value strings are allowed for headers that just need to exist.
            """,
            """
            [ai]
            enabled = true
            provider = "anthropic"
            api = "chat"
            model = "claude-3-5-sonnet-latest"
            api_key = "sk-ant-..."
            custom_headers = "anthropic-version: 2023-06-01"
            """,
            [
                "ai", "custom", "headers", "custom_headers", "anthropic-version",
                "HTTP-Referer", "X-Title", "openrouter", "anthropic", "gateway",
                "pipeline", "policy",
            ],
            ["custom_headers", "headers"],
            "Keyword",
            "custom_headers = \"${1:Header-Name}: ${2:value}\"",
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
        builder.AppendLine("- Dotted members like `request.method`, `request.url`, `request.body`, `request.content_type`, and `request.headers` belong to flow code and mutate the next `request.send()` call.");
        builder.AppendLine("- Compatibility aliases like `request.ssl`, `request.history`, and `request.max_send_iterations` still map to request metadata, but prefer the bare directives in new scripts.");
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
        return builder.ToString().Trim();
    }

    public static string BuildPromptContext()
    {
        StringBuilder builder = new();
        builder.AppendLine("ForRest language reference.");
        builder.AppendLine("Use the exact syntax from the canonical catalog below.");
        builder.AppendLine();
        AppendPromptSection(builder, "Request surface", Entries.Where(entry => entry.Category == "Request"));
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
        AppendPromptSection(builder, "Assertions", Entries.Where(entry => entry.Category == "Assertions"));
        builder.AppendLine();
        AppendPromptSection(builder, "Helpers", Entries.Where(entry => entry.Category is "Data" or "Security" or "Variables"));
        builder.AppendLine();
        AppendPromptSection(builder, "AI providers", Entries.Where(entry => entry.Category == "AI"));
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
            "AI",
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
