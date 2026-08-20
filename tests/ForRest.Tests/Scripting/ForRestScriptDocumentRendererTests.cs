namespace ForRest.Tests.Scripting;

[TestClass]
public sealed class ForRestScriptDocumentRendererTests
{
    #region Extraction Round Trips

    [TestMethod]
    public void Regex_body_extraction_with_group_survives_the_round_trip()
    {
        var document = ParseValid(
            """
            name "Regex Body"
            method GET
            url "https://api.example.test/items"

            extract runtime csrf_token = regex body "token=(\\w+)" 2
            """);

        var reparsed = RenderAndReparse(document);

        Assert.AreEqual(1, reparsed.Extractions.Count);
        var extraction = reparsed.Extractions[0];
        Assert.AreEqual(ForRestScriptExtractionSource.Body, extraction.Source);
        Assert.AreEqual("csrf_token", extraction.TargetVariableName);
        Assert.AreEqual("token=(\\w+)", extraction.Pattern);
        Assert.AreEqual(2, extraction.Group);
        AssertCanonicallyEqual(document, reparsed);
    }

    [TestMethod]
    public void Regex_body_extraction_without_group_survives_the_round_trip()
    {
        var document = ParseValid(
            """
            name "Regex Body Default Group"
            method GET
            url "https://api.example.test/items"

            extract request session_id = regex body "session=([a-z0-9]+)"
            """);

        var reparsed = RenderAndReparse(document);

        var extraction = reparsed.Extractions[0];
        Assert.AreEqual(ForRestScriptExtractionSource.Body, extraction.Source);
        Assert.AreEqual(VariableScope.RequestLocal, extraction.TargetScope);
        Assert.AreEqual("session=([a-z0-9]+)", extraction.Pattern);
        Assert.AreEqual(1, extraction.Group);
        AssertCanonicallyEqual(document, reparsed);
    }

    [TestMethod]
    public void Regex_header_extraction_survives_the_round_trip()
    {
        var document = ParseValid(
            """
            name "Regex Header"
            method GET
            url "https://api.example.test/items"

            extract runtime trace_id = regex header "X-Trace-Id" "id-(\\d+)" 1
            """);

        var reparsed = RenderAndReparse(document);

        var extraction = reparsed.Extractions[0];
        Assert.AreEqual(ForRestScriptExtractionSource.Header, extraction.Source);
        Assert.AreEqual("X-Trace-Id", extraction.Selector);
        Assert.AreEqual("id-(\\d+)", extraction.Pattern);
        Assert.AreEqual(1, extraction.Group);
        AssertCanonicallyEqual(document, reparsed);
    }

    [TestMethod]
    public void Regex_json_extraction_survives_the_round_trip()
    {
        var document = ParseValid(
            """
            name "Regex Json"
            method GET
            url "https://api.example.test/items"

            extract runtime short_token = regex json "$.auth.token" "^([A-Za-z0-9]{8})" 1
            """);

        var reparsed = RenderAndReparse(document);

        var extraction = reparsed.Extractions[0];
        Assert.AreEqual(ForRestScriptExtractionSource.Json, extraction.Source);
        Assert.AreEqual("$.auth.token", extraction.Selector);
        Assert.AreEqual("^([A-Za-z0-9]{8})", extraction.Pattern);
        AssertCanonicallyEqual(document, reparsed);
    }

    [TestMethod]
    public void Plain_json_extraction_survives_the_round_trip()
    {
        var document = ParseValid(
            """
            name "Plain Json"
            method GET
            url "https://api.example.test/items"

            extract runtime item_id = json "$.items[0].id"
            """);

        var reparsed = RenderAndReparse(document);

        var extraction = reparsed.Extractions[0];
        Assert.AreEqual(ForRestScriptExtractionSource.Json, extraction.Source);
        Assert.AreEqual("$.items[0].id", extraction.Selector);
        Assert.AreEqual(string.Empty, extraction.Pattern);
        AssertCanonicallyEqual(document, reparsed);
    }

    #endregion

    #region Import Round Trips

    [TestMethod]
    public void Import_directives_survive_the_round_trip()
    {
        var document = ParseValid(
            """
            import "shared/base.frs"
            use "shared/auth.frs"

            name "With Imports"
            method GET
            url "https://api.example.test/items"
            """);

        var reparsed = RenderAndReparse(document);

        CollectionAssert.AreEqual(new[] { "shared/base.frs", "shared/auth.frs" }, reparsed.Imports.ToArray());
        StringAssert.Contains(ForRestScriptDocumentRenderer.Render(document), "import \"shared/auth.frs\"");
        AssertCanonicallyEqual(document, reparsed);
    }

    #endregion

    #region Handler Round Trips

    [TestMethod]
    public void On_error_handler_survives_the_round_trip()
    {
        var document = ParseValid(
            """
            name "Error Handler"
            method GET
            url "https://api.example.test/items"

            on error {
            log "request failed"
            }
            """);

        var reparsed = RenderAndReparse(document);

        Assert.AreEqual(1, reparsed.Handlers.Count);
        var handler = reparsed.Handlers[0];
        Assert.AreEqual(ForRestScriptHandlerKind.OnError, handler.Kind);
        Assert.IsNull(handler.StatusCode);
        StringAssert.Contains(handler.Body, "log \"request failed\"");
        AssertCanonicallyEqual(document, reparsed);
    }

    [TestMethod]
    public void On_status_handler_survives_the_round_trip()
    {
        var document = ParseValid(
            """
            name "Status Handler"
            method GET
            url "https://api.example.test/items"

            on status 429 {
            wait 1000
            send
            }
            """);

        var reparsed = RenderAndReparse(document);

        Assert.AreEqual(1, reparsed.Handlers.Count);
        var handler = reparsed.Handlers[0];
        Assert.AreEqual(ForRestScriptHandlerKind.OnStatus, handler.Kind);
        Assert.AreEqual(429, handler.StatusCode);
        StringAssert.Contains(handler.Body, "wait 1000");
        StringAssert.Contains(handler.Body, "send");
        AssertCanonicallyEqual(document, reparsed);
    }

    #endregion

    #region Scenario Round Trips

    [TestMethod]
    public void Scenario_with_overrides_survives_the_round_trip()
    {
        var document = ParseValid(
            """
            name "Scenarios"
            method GET
            url "https://api.example.test/items"

            scenario "As Admin" {
              auth type = "bearer"
              auth token = "admin-token"
              header "X-Role" = "admin"
              expect status == 200 "admin can list items"
              send
            }
            """);

        var reparsed = RenderAndReparse(document);

        Assert.AreEqual(1, reparsed.Scenarios.Count);
        var scenario = reparsed.Scenarios[0];
        Assert.AreEqual("As Admin", scenario.Name);
        Assert.AreEqual("bearer", scenario.Auth["type"].AsInvariantString());
        Assert.AreEqual("admin-token", scenario.Auth["token"].AsInvariantString());
        Assert.AreEqual(1, scenario.Headers.Count);
        Assert.AreEqual("X-Role", scenario.Headers[0].Key);
        Assert.AreEqual("admin", scenario.Headers[0].Value.AsInvariantString());
        Assert.AreEqual(1, scenario.Tests.Count);
        Assert.AreEqual("admin can list items", scenario.Tests[0].Message);
        StringAssert.Contains(scenario.Flow, "send");
        AssertCanonicallyEqual(document, reparsed);
    }

    #endregion

    #region Body Mode Round Trips

    [TestMethod]
    public void Form_body_survives_the_round_trip_with_no_diagnostics()
    {
        var document = ParseValid(
            """"
            name "Form Body"
            method POST
            url "https://api.example.test/login"

            body form """
            username=admin&password=secret
            """
            """");

        var rendered = ForRestScriptDocumentRenderer.Render(document);
        var result = new ForRestScriptParser().Parse(rendered);

        Assert.AreEqual(0, result.Diagnostics.Count, $"rendered form body must reparse cleanly, got: {string.Join("; ", result.Diagnostics.Select(static diagnostic => diagnostic.Message))}");
        Assert.IsNotNull(result.Document);
        Assert.IsNotNull(result.Document.Body);
        Assert.AreEqual(RequestBodyMode.FormUrlEncoded, result.Document.Body.Mode);
        StringAssert.Contains(result.Document.Body.Content, "username=admin&password=secret");
    }

    [TestMethod]
    public void Multipart_body_survives_the_round_trip()
    {
        var document = ParseValid(
            """"
            name "Multipart Body"
            method POST
            url "https://api.example.test/upload"

            body multipart """
            part-content
            """
            """");

        var reparsed = RenderAndReparse(document);

        Assert.IsNotNull(reparsed.Body);
        Assert.AreEqual(RequestBodyMode.MultipartFormData, reparsed.Body.Mode);
        StringAssert.Contains(reparsed.Body.Content, "part-content");
        AssertCanonicallyEqual(document, reparsed);
    }

    [TestMethod]
    public void Body_with_a_deliberate_trailing_blank_line_survives_the_round_trip()
    {
        var document = ParseValid(
            """"
            name "Trailing Blank Body"
            method POST
            url "https://api.example.test/items"

            body raw """
            line-one

            """
            """");

        var reparsed = RenderAndReparse(document);

        // The renderer must strip only the parser's single closing-delimiter artifact line,
        // not the user's intentional trailing blank line.
        var rendered = ForRestScriptDocumentRenderer.Render(document);
        StringAssert.Contains(rendered, "line-one\n\n\"\"\"");
        Assert.IsNotNull(reparsed.Body);
        Assert.AreEqual(document.Body!.Content, reparsed.Body.Content, "the body content must be byte-identical after a parse -> render -> parse cycle");
        AssertCanonicallyEqual(document, reparsed);
    }

    #endregion

    #region Renderer Stability

    [TestMethod]
    public void Rendering_a_kitchen_sink_document_is_stable_across_round_trips()
    {
        var document = ParseValid(
            """"
            import "shared/base.frs"

            name "Kitchen Sink"
            method POST
            url "https://api.example.test/items"
            timeout 30

            auth {
              type = "bearer"
              token = "{{api_token}}"
            }

            request item_kind = "widget"
            runtime trace_id = guid()
            secret api_token = "super-secret"

            query "page" = "1"
            header "Accept" = "application/json"

            body json """
            {
              "kind": "{{item_kind}}"
            }
            """

            extract runtime item_id = json "$.id"
            extract runtime csrf = regex body "token=(\\w+)" 2
            extract request trace = regex header "X-Trace-Id" "id-(\\d+)"
            extract runtime short_token = regex json "$.auth.token" "^([A-Za-z0-9]{8})"

            expect status == 201 "created"
            expect json "$.id" exists
            expect body regex "\"kind\"" "body mentions kind"

            repeat count = 2
            retry count = 3

            on error {
            log "failed"
            }

            on status 429 {
            wait 500
            }

            scenario "As Admin" {
              auth token = "admin-token"
              header "X-Role" = "admin"
              expect status == 200 "admin ok"
              send
            }
            """");

        var firstRender = ForRestScriptDocumentRenderer.Render(document);
        var reparsed = ParseValid(firstRender);
        var secondRender = ForRestScriptDocumentRenderer.Render(reparsed);

        Assert.AreEqual(firstRender, secondRender, "rendering must be a fixed point after one parse/render cycle");
    }

    #endregion

    #region Helpers

    private static ForRestScriptDocument ParseValid(string source)
    {
        var result = new ForRestScriptParser().Parse(source);
        Assert.IsTrue(
            result.Succeeded,
            $"expected the source to parse cleanly, got: {string.Join("; ", result.Diagnostics.Select(static diagnostic => diagnostic.Message))}");
        return result.Document!;
    }

    private static ForRestScriptDocument RenderAndReparse(ForRestScriptDocument document)
    {
        var rendered = ForRestScriptDocumentRenderer.Render(document);
        return ParseValid(rendered);
    }

    // Canonical-form equality: two documents are equivalent when the renderer produces the same
    // text for both. This catches anything the renderer drops or rewrites, because the reparsed
    // document would then render differently from the original.
    private static void AssertCanonicallyEqual(ForRestScriptDocument expected, ForRestScriptDocument actual)
    {
        Assert.AreEqual(
            ForRestScriptDocumentRenderer.Render(expected),
            ForRestScriptDocumentRenderer.Render(actual),
            "the reparsed document must render identically to the original");
    }

    #endregion
}
