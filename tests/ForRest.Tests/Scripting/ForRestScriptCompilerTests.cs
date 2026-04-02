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
    public void Compile_supports_regex_extractions_and_assertions()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            """
            name "Regex Probe"
            method GET
            url "https://api.example.test/echo"

            extract runtime body_token = regex body "Bearer ([A-Za-z0-9-]+)"
            extract runtime session_id = regex header "Set-Cookie" "session=([^;]+)"
            extract runtime payload_id = regex json "$.payload.id" "([0-9]+)"

            expect body regex "Bearer ([A-Za-z0-9-]+)" "body matches token"
            expect header "Content-Type" regex "json" "header matches json"
            expect json "$.payload.id" regex "^[0-9]+$" "json matches id"
            """;

        var result = compiler.Compile(
            source,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(static item => item.Message)));
        Assert.IsNotNull(result.Payload);
        Assert.AreEqual(3, result.Payload.Request.Extractions.Count);
        Assert.AreEqual(ExtractionSource.Body, result.Payload.Request.Extractions[0].Source);
        Assert.AreEqual(@"Bearer ([A-Za-z0-9-]+)", result.Payload.Request.Extractions[0].Pattern);
        Assert.AreEqual(ExtractionSource.Header, result.Payload.Request.Extractions[1].Source);
        Assert.AreEqual("Set-Cookie", result.Payload.Request.Extractions[1].Selector);
        Assert.AreEqual(ExtractionSource.Json, result.Payload.Request.Extractions[2].Source);
        Assert.AreEqual("$.payload.id", result.Payload.Request.Extractions[2].Selector);
        StringAssert.Contains(result.Payload.Request.TestsScript, "regex.IsMatch(response.Body ?? string.Empty, @\"Bearer ([A-Za-z0-9-]+)\")");
        StringAssert.Contains(result.Payload.Request.TestsScript, "regex.IsMatch(__headerValue2, @\"json\")");
        StringAssert.Contains(result.Payload.Request.TestsScript, "regex.IsMatch(__jsonValue3 ?? string.Empty, @\"^[0-9]+$\")");
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
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "dynamic sent = (await request.send());");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "if (response.Status == 200) {");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "variables.Set(\"last_attempt\"");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "foreach (dynamic item in __flow.Range(0, 2)) {");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "console.Log(item);");
    }

    [TestMethod]
    public void Compile_treats_dotted_request_members_as_flow_mutations_instead_of_top_level_request_aliases()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            """
            name "Mutation Probe"
            method GET
            url "https://api.example.test/objects"
            content_type "application/json"

            request.method = "POST"
            request.url = "https://api.example.test/objects/7"
            request.content_type = "application/merge-patch+json"
            request.body = "{\"name\":\"patched\"}"
            let sent = request.send()
            """;

        var result = compiler.Compile(
            source,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(static item => item.Message)));
        Assert.IsNotNull(result.Payload);
        Assert.AreEqual(HttpMethodKind.Get, result.Payload.Request.Method);
        Assert.AreEqual("https://api.example.test/objects", result.Payload.Request.UrlTemplate);
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "request.Method = \"POST\";");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "request.Url = \"https://api.example.test/objects/7\";");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "request.ContentType = \"application/merge-patch+json\";");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "request.Body = \"{\\\"name\\\":\\\"patched\\\"}\";");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "dynamic sent = (await request.send());");
    }

    [TestMethod]
    public void Compile_supports_multiline_let_and_request_assignments()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            """
            name "Continuation Probe"
            method POST
            url "https://api.example.test/objects"
            content_type "application/json"

            let prefix =
              "Alpha"

            let starts_ok =
              strings.StartsWith(prefix, "A")
              or strings.StartsWith(prefix, "B")
              or strings.StartsWith(prefix, "C")

            request.body =
              "{\"name\":\"patched\"}"

            let sent = request.send()
            """;

        var result = compiler.Compile(
            source,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(static item => item.Message)));
        Assert.IsNotNull(result.Payload);
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "dynamic prefix = \"Alpha\";");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "dynamic starts_ok = strings.StartsWith(prefix, \"A\") || strings.StartsWith(prefix, \"B\") || strings.StartsWith(prefix, \"C\");");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "request.Body = \"{\\\"name\\\":\\\"patched\\\"}\";");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "dynamic sent = (await request.send());");
    }

    [TestMethod]
    public void Compile_supports_raw_json_request_body_object_assignments()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            """
            name "Raw JSON Body Probe"
            method POST
            url "https://api.example.test/objects"
            content_type "application/json"

            request.body =
            {
              "name": "patched",
              "data": {
                "color": "silver",
                "year": 2026
              }
            }

            let sent = request.send()
            """;

        var result = compiler.Compile(
            source,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(static item => item.Message)));
        Assert.IsNotNull(result.Payload);
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "request.Body = \"{");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "\\u0022name\\u0022: \\u0022patched\\u0022");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "\\u0022color\\u0022: \\u0022silver\\u0022");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "dynamic sent = (await request.send());");
    }

    [TestMethod]
    public void Compile_supports_dynamic_request_body_object_literal_assignments()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            """
            name "Dynamic JSON Body Probe"
            method POST
            url "https://api.example.test/objects"
            content_type "application/json"

            let created_name = "Validation Widget"
            request.body =
            {
              name = created_name,
              data = {
                price = 1849.99,
                "CPU model" = "Trace CPU",
                "Hard disk size": "1 TB"
              }
            }

            let sent = request.send()
            """;

        var result = compiler.Compile(
            source,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(static item => item.Message)));
        Assert.IsNotNull(result.Payload);
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "request.Body = json.Stringify(new JsonObject");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "[\"name\"] = __flow.J(created_name)");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "[\"price\"] = __flow.J(1849.99)");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "[\"CPU model\"] = __flow.J(\"Trace CPU\")");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "dynamic sent = (await request.send());");
    }

    [TestMethod]
    public void Compile_supports_bare_request_directives_inside_flow_blocks()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            """
            name "Directive Flow Probe"
            method GET
            url "https://api.example.test/objects"

            foreach attempt in [0..0] {
              method POST
              url "https://api.example.test/objects/7"
              content_type "application/json"
              header "X-Trace" = "enabled"
              request.body =
              {
                "name": "patched"
              }
              let sent = request.send()
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
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "request.Method = \"POST\";");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "request.Url = \"https://api.example.test/objects/7\";");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "request.ContentType = \"application/json\";");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "request.Headers[\"X-Trace\"] = \"enabled\";");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "dynamic sent = (await request.send());");
    }

    [TestMethod]
    public void Compile_reports_misplaced_expect_inside_flow_blocks_with_actionable_diagnostic()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            """
            name "Misplaced Expect Probe"
            method GET
            url "https://api.example.test/objects"

            if true {
              expect status == 200 "returns 200"
            }
            """;

        var result = compiler.Compile(
            source,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(1, result.Diagnostics.Count);
        StringAssert.Contains(result.Diagnostics[0].Message, "top-level assertion");
        StringAssert.Contains(result.Diagnostics[0].Message, "tests.Assert");
    }

    [TestMethod]
    public void Compile_supports_restful_api_surface_qa_script()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            """
            name "restful-api QA"
            method GET
            url "https://api.restful-api.dev/objects"
            timeout 15000
            max_send_iterations 8
            redirects true
            ssl true
            history true

            runtime trace_id = guid()

            header "Accept" = "application/json"
            header "X-Workspace" = "{{workspace_name}}"
            header "X-Environment" = "{{environment_name}}"
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
            """;

        var result = compiler.Compile(
            source,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(static item => item.Message)));
        Assert.IsNotNull(result.Payload);
        Assert.AreEqual("restful-api QA", result.Payload.Request.Name);
        Assert.AreEqual(HttpMethodKind.Get, result.Payload.Request.Method);
        Assert.AreEqual("https://api.restful-api.dev/objects", result.Payload.Request.UrlTemplate);
        Assert.AreEqual(15_000, result.Payload.Request.TimeoutMilliseconds);
        Assert.AreEqual(8, result.Payload.Request.MaxSendIterations);
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "tests.Assert(sent.status >= 200 && sent.status < 300, \"list returns 2xx\");");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "dynamic sample_id_a = convert.ToString(sent[0].id);");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "tests.Equal(3, __flow.Count(sent), \"filtered list returns requested ids\");");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "dynamic created_id = sent.id;");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "request.Method = \"DELETE\";");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "stash.DeleteMessage = sent.message;");
        StringAssert.Contains(result.Payload.Request.TestsScript, "response.Status == 200");
        StringAssert.Contains(result.Payload.Request.TestsScript, "Content-Type");
    }

    [TestMethod]
    public void Compile_preserves_convert_and_strings_helpers_in_flow()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            """
            request {
              method = GET
              url = "https://api.example.test/objects"
            }

            flow {
              let price = convert.ToDouble("1849.99")
              let prefix = strings.Upper(strings.Substring("apple", 0, 1))
              tests.Equal(true, strings.StartsWith(prefix, "A"), "prefix is A")
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
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "dynamic price = convert.ToDouble(\"1849.99\");");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "dynamic prefix = strings.Upper(strings.Substring(\"apple\", 0, 1));");
        Assert.IsFalse(result.Payload.Request.PreRequestScript.Contains("__flow.V(\"convert\")", StringComparison.Ordinal));
        Assert.IsFalse(result.Payload.Request.PreRequestScript.Contains("__flow.V(\"strings\")", StringComparison.Ordinal));
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
    public void Compile_auto_awaits_workspace_execute_in_flow()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            """
            request {
              method = GET
              url = "https://api.example.test/secure"
            }

            flow {
              let auth = workspace.execute("/requests/auth/token")
              request.headers["Authorization"] = $"Bearer {auth.token}"
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
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "dynamic auth = (await workspace.execute(\"/requests/auth/token\"));");
        Assert.IsFalse(result.Payload.Request.PreRequestScript.Contains("await await", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Compile_binds_template_referenced_workspace_execute_result_for_request_templates()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            """"
            request {
              method = POST
              url = "https://api.example.test/echo"
              content_type = "application/json"
            }

            body json """
            {
              "guid": "{{uuid.uuid}}"
            }
            """

            flow {
              let uuid = workspace.execute("Get UUID")
            }
            """";

        var result = compiler.Compile(
            source,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(static item => item.Message)));
        Assert.IsNotNull(result.Payload);
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "__flow.Bind(\"uuid\", uuid);");
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
    public void Compile_supports_top_level_auth_directives_and_extended_auth_aliases()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            """
            name "Auth Probe"
            method GET
            url "https://api.example.test/items"

            auth mode = oauth_client_credentials
            auth token_url = "https://login.example.test/oauth2/v2.0/token"
            auth client_id = "{{client_id}}"
            auth client_secret = "{{client_secret}}"
            auth scopes = "api://forrest/.default offline_access"
            auth location = header
            auth header_name = "X-Access-Token"
            auth scheme = ""
            """;

        var result = compiler.Compile(
            source,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(static item => item.Message)));
        Assert.IsNotNull(result.Payload);
        Assert.AreEqual(AuthMode.OAuthClientCredentials, result.Payload.Request.Auth.Mode);
        Assert.AreEqual("https://login.example.test/oauth2/v2.0/token", result.Payload.Request.Auth.TokenUrl);
        Assert.AreEqual("{{client_id}}", result.Payload.Request.Auth.ClientId);
        Assert.AreEqual("{{client_secret}}", result.Payload.Request.Auth.ClientSecret);
        Assert.AreEqual("api://forrest/.default offline_access", result.Payload.Request.Auth.Scopes);
        Assert.AreEqual("X-Access-Token", result.Payload.Request.Auth.HeaderName);
        Assert.AreEqual(string.Empty, result.Payload.Request.Auth.Scheme);
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
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "dynamic sent = (await request.send());");
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
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "dynamic sent = (await request.send());");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "if (sent.status == 200) {");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "dynamic __runtimeValue1 = sent[0].email;");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "foreach (dynamic index in __flow.Range(0, 2)) {");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "console.Log(sent[index].username);");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "console.Warn(sent.status);");
    }

    [TestMethod]
    public void Compile_supports_multiline_conditions_range_literals_and_length_aliases()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            """
            name "Natural Flow"
            method POST
            url "https://api.example.test/anything"
            max_send_iterations 3

            request.headers["X-Request-Source"] = "maui"
            let iter = [0..9]
            let sent = null

            foreach loop in iter
            {
              sent = request.send()
              if sent.status == 200 and not (sent.body.length() == 0)
              {
                log $"Response status {sent.status}."
                break
              }
            }

            if response.Id == 1
               or response.Id.length() > 32
            {
              log $"Error condition failed."
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
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "dynamic iter = __flow.RangeClosed(0, 9);");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "foreach (dynamic loop in iter) {");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "sent = (await request.send());");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "if (sent.status == 200 && !(__flow.Count(sent.body) == 0)) {");
        StringAssert.Contains(result.Payload.Request.PreRequestScript, "if (response.Id == 1 || __flow.Count(response.Id) > 32) {");
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

    [TestMethod]
    public void Compile_reports_actionable_guidance_for_malformed_expectations()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            """
            name "Bad Expect"
            method GET
            url "https://api.example.test/items"

            expect header "Content-Type" "json response"
            """;

        var result = compiler.Compile(
            source,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsFalse(result.Succeeded);
        string messages = string.Join(Environment.NewLine, result.Diagnostics.Select(static item => item.Message));
        StringAssert.Contains(messages, "Could not parse the expectation.");
        StringAssert.Contains(messages, "expect status == 200");
        StringAssert.Contains(messages, "expect header \"Content-Type\" contains \"json\"");
    }
}
