namespace ForRest.Tests.Scripting;

[TestClass]
public sealed class ForRestScriptCompilerTests
{
    [TestMethod]
    public void Compile_builds_request_runtime_seeds_tests_and_retry_metadata()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            """"
            name "Create Echo"
            method POST
            url "https://api.example.test/echo/{{resource_id}}?trace={{trace_id}}"
            timeout 15000
            redirects false
            ssl true
            history false
            content_type "application/json"

            request resource_id = "42"
            runtime trace_id = "seed-123"
            runtime attempt = random(1, 3)

            header "Accept" = "application/json"
            header "X-Trace-Id" = "{{trace_id}}"

            body json """
            {
              "id": "{{resource_id}}",
              "trace": "{{trace_id}}"
            }
            """

            extract runtime created_id = json "$.payload.id"

            expect status == 201 "returns 201"
            expect json "$.payload.id" == "42" "payload id matches"
            expect header "Content-Type" contains "json" "json content"

            repeat count = 2
            repeat delay = 10
            repeat interval = 25

            retry count = 1
            retry interval = 50
            """";

        var result = compiler.Compile(
            source,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Payload);
        Assert.AreEqual("Create Echo", result.Payload.Request.Name);
        Assert.AreEqual(HttpMethodKind.Post, result.Payload.Request.Method);
        Assert.AreEqual("https://api.example.test/echo/{{resource_id}}?trace={{trace_id}}", result.Payload.Request.UrlTemplate);
        Assert.AreEqual(2, result.Payload.Request.Schedule.RepeatCount);
        Assert.AreEqual(1, result.Payload.Request.Retry.Count);
        Assert.AreEqual("application/json", result.Payload.Request.Body.ContentType);
        Assert.AreEqual("42", result.Payload.Request.Variables.Single(static item => item.Key == "resource_id").Value);
        Assert.AreEqual("seed-123", result.Payload.RuntimeSeeds.Single(static item => item.Key == "trace_id").LiteralValue);
        Assert.AreEqual(ForRestRuntimeSeedKind.RandomNumber, result.Payload.RuntimeSeeds.Single(static item => item.Key == "attempt").Kind);
        StringAssert.Contains(result.Payload.Request.TestsScript, "response.Status == 201");
        StringAssert.Contains(result.Payload.Request.TestsScript, "json.Select(response.Json(), @\"$.payload.id\")");
        StringAssert.Contains(result.Payload.Request.TestsScript, "out string __headerRaw3");
    }

    [TestMethod]
    public void Compile_translates_top_level_code_into_pre_request_script()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            """
            name "Flow Echo"
            method GET
            url "https://api.example.test/echo?trace={{trace_id}}"
            max_send_iterations 2

            runtime trace_id = "trace-123"

            request.headers["X-Flow"] = "enabled"
            let sent = request.send()
            if response.status == 200 {
              runtime last_attempt = response.attempt
            }
            foreach item in range(0, 2) {
              log item
            }
            """;

        var result = compiler.Compile(
            source,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Payload);
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "request.Headers[\"X-Flow\"] = \"enabled\";");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "dynamic sent = await request.send();");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "if (response.Status == 200) {");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "variables.Set(\"last_attempt\"");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "foreach (dynamic item in __flow.Range(0, 2)) {");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "console.Log(item);");
    }

    [TestMethod]
    public void Compile_tolerates_explicit_await_on_request_send_in_flow()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            """
            request {
              method = GET
              url = "https://api.example.test/echo"
              max_send_iterations = 1
            }

            flow {
              await request.send()
            }
            """;

        var result = compiler.Compile(
            source,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Payload);
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "await request.send();");
        Assert.IsFalse(result.Payload.Request.PreRequestScript.Contains("await await", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Compile_rejects_classic_c_style_for_loops_with_clear_diagnostic()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            """
            request {
              method = GET
              url = "https://api.example.test/echo"
            }

            flow {
              for (var i = 0; i < 3; i++) {
                log i
              }
            }
            """;

        var result = compiler.Compile(
            source,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(
            string.Join(Environment.NewLine, result.Diagnostics.Select(static item => item.Message)),
            "Classic C-style for loops are not supported");
    }

    [TestMethod]
    public void Compile_accepts_legacy_section_syntax_for_headers_and_retry()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            """
            meta {
              name = "Legacy Echo"
            }

            vars {
              runtime trace_id = "trace-123"
            }

            request {
              method = GET
              url = "https://api.example.test/echo?trace={{trace_id}}"
              history = false
            }

            headers {
              Accept = "application/json"
              X-Trace-Id = "{{trace_id}}"
            }

            tests {
              status == 200 "returns 200"
            }

            retry {
              count = 1
              interval = 25
            }
            """;

        var result = compiler.Compile(
            source,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(static item => item.Message)));
        Assert.IsNotNull(result.Payload);
        Assert.AreEqual(1, result.Payload.Request.Retry.Count);
        Assert.AreEqual(25, result.Payload.Request.Retry.IntervalMilliseconds);
        Assert.HasCount(2, result.Payload.Request.Headers);
        Assert.AreEqual("X-Trace-Id", result.Payload.Request.Headers[1].Key);
    }

    [TestMethod]
    public void Compile_tolerates_semicolons_and_mixed_legacy_code_first_flow()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            """
            name "Sync Profile";
            method POST;
            url "https://api.example.test/users/{{user_id}}";
            max_send_iterations 1;

            runtime request_name = "Sync Profile";
            request user_id = "42";

            header "Accept" = "application/json";
            retry count = 1;
            retry interval = 250;

            request.SetHeader("X-Shell-Surface", "editor-first");
            console.Log("Prepared request before send.");
            let sent = request.send();

            expect status == 200 "returns 200";
            expect header "Content-Type" contains "json" "json response";
            """;

        var result = compiler.Compile(
            source,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(static item => item.Message)));
        Assert.IsNotNull(result.Payload);
        Assert.AreEqual("Sync Profile", result.Payload.Request.Name);
        Assert.AreEqual(1, result.Payload.Request.Retry.Count);
        Assert.AreEqual(250, result.Payload.Request.Retry.IntervalMilliseconds);
        Assert.AreEqual("Sync Profile", result.Payload.RuntimeSeeds.Single(static item => item.Key == "request_name").LiteralValue);
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "request.SetHeader(\"X-Shell-Surface\", \"editor-first\");");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "console.Log(\"Prepared request before send.\");");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "dynamic sent = await request.send();");
    }

    [TestMethod]
    public void Compile_supports_parenthesized_foreach_headers_and_preserves_string_literals()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            """
            name "Loop Echo"
            method GET
            url "https://api.example.test/echo"

            log "response.status request.send() range(0, 2)"
            foreach (var item in range(0, 2)) {
              log item
            }
            """;

        var result = compiler.Compile(
            source,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(static item => item.Message)));
        Assert.IsNotNull(result.Payload);
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "console.Log(\"response.status request.send() range(0, 2)\");");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "foreach (dynamic item in __flow.Range(0, 2)) {");
    }

    [TestMethod]
    public void Compile_supports_indexed_collection_access_on_request_send_results()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            """
            name "List Users"
            method GET
            url "https://api.example.test/users"
            max_send_iterations 3

            let sent = request.send()
            if sent.status == 200 {
              runtime first_user_email = sent[0].email
              foreach index in range(0, 2) {
                log sent[index].username
              }
            } else {
              warn sent.status
            }
            """;

        var result = compiler.Compile(
            source,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(static item => item.Message)));
        Assert.IsNotNull(result.Payload);
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "dynamic sent = await request.send();");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "if (sent.status == 200) {");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "dynamic __runtimeValue1 = sent[0].email;");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "foreach (dynamic index in __flow.Range(0, 2)) {");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "console.Log(sent[index].username);");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "console.Warn(sent.status);");
    }

    [TestMethod]
    public void Compile_supports_interpolated_strings_with_indexers_and_member_access()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            """
            name "Interpolated Probe"
            method GET
            url "https://api.example.test/users"
            max_send_iterations 2

            let sent = request.send()
            log $"user {sent[0].email}"
            log $"accept {request.headers["Accept"]}"
            """;

        var result = compiler.Compile(
            source,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(static item => item.Message)));
        Assert.IsNotNull(result.Payload);
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "console.Log($\"user {sent[0].email}\");");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "console.Log($\"accept {request.Headers[\"Accept\"]}\");");
    }

    [TestMethod]
    public void Compile_reports_source_diagnostic_for_malformed_legacy_helper_calls()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            """
            name "Broken Users Feed"
            method GET
            url "https://api.example.test/users"

            request.headers["X-Request-Source"] = "maui"
            console.Log("Prepared request before send."
            """;

        var result = compiler.Compile(
            source,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Diagnostics.Count > 0);
        StringAssert.Contains(
            string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => diagnostic.Message)),
            "Malformed legacy helper call");
    }

    [TestMethod]
    public void Compile_reports_precise_line_for_top_level_variable_parse_errors()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            """
            name "Broken"
            method PUT
            url "https://api.example.test/broken"

            request resource_id = "42
            """;

        var result = compiler.Compile(
            source,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Diagnostics.Count > 0);
        Assert.AreEqual(5, result.Diagnostics[0].Line);
        Assert.IsTrue(result.Diagnostics[0].Column >= 1);
        StringAssert.Contains(result.Diagnostics[0].Message, "resource_id");
    }

    [TestMethod]
    public void Compile_accepts_smart_quoted_string_literals_in_variables()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            """
            name "Smart Quote Probe"
            method GET
            url "https://api.example.test/items/{{resource_id}}"

            request resource_id = “42”
            """;

        var result = compiler.Compile(
            source,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(static item => item.Message)));
        Assert.AreEqual("42", result.Payload?.Request.Variables.Single(static item => item.Key == "resource_id").Value);
    }

    [TestMethod]
    public void Compile_accepts_compact_flow_headers_smart_quoted_selectors_and_escaped_assertion_messages()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source = """
            name "Compact Script"
            method GET
            url "https://api.example.test/items"
            max_send_iterations 2

            runtime trace_id = "trace-123"

            request.headers["X-Test"] = "ok"
            let sent = request.send()
            if(sent.status == 200){
              log [[open]]brace { payload }[[close]]
            }else if(sent.status == 202){
              warn sent.status
            }else{
              error sent.status
            }
            while(request.remaining_send_iterations > 0){
              break
            }
            foreach(step in range(0, 2)){
              log step
            }

            expect status == 200 "said \\\"ok\\\""
            expect header [[open]]Content-Type[[close]] contains "json" "content type is json"
            expect json [[open]]$.payload.id[[close]] exists "payload id exists"
            """
            .Replace("[[open]]", "\u201C", StringComparison.Ordinal)
            .Replace("[[close]]", "\u201D", StringComparison.Ordinal);

        var result = compiler.Compile(
            source,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(static item => item.Message)));
        Assert.IsNotNull(result.Payload);
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "if (sent.status == 200) {");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "else if (sent.status == 202) {");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "else {");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "while (request.RemainingSendIterations > 0) {");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "foreach (dynamic step in __flow.Range(0, 2)) {");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "console.Log(\"brace { payload }\");");
        StringAssert.Contains(result.Payload.Request.TestsScript, "said ");
        StringAssert.Contains(result.Payload.Request.TestsScript, "ok");
        StringAssert.Contains(result.Payload.Request.TestsScript, "Content-Type");
        StringAssert.Contains(result.Payload.Request.TestsScript, "@\"$.payload.id\"");
    }

    [TestMethod]
    public void Compile_rejects_variable_names_that_are_not_valid_forrest_identifiers()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            """
            name "Bad Identifier"
            method GET
            url "https://api.example.test/items"

            runtime trace-id = "trace-123"
            """;

        var result = compiler.Compile(
            source,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(
            string.Join(Environment.NewLine, result.Diagnostics.Select(static item => item.Message)),
            "not a valid ForRest identifier");
    }
}
