namespace ForRest.Tests.Scripting;

[TestClass]
public sealed class ImportMergeTests
{
    #region Merge Surfaces

    [TestMethod]
    public void Compile_merges_imported_headers_into_the_document()
    {
        var source =
            """
            import "shared/common.frs"

            name "Header Merge"
            method GET
            url "https://api.example.test/items"

            header "X-Root" = "root"
            """;

        var imports = new Dictionary<string, string>
        {
            ["shared/common.frs"] =
                """
                header "X-Shared" = "shared"
                """,
        };

        var result = Compile(source, imports);

        Assert.IsTrue(result.Succeeded, Describe(result));
        Assert.AreEqual("root", GetEntry(result.Payload!.Request.Headers, "X-Root"));
        Assert.AreEqual("shared", GetEntry(result.Payload.Request.Headers, "X-Shared"));
    }

    [TestMethod]
    public void Compile_merges_imported_extractions_and_skips_duplicate_variable_names()
    {
        var source =
            """
            import "shared/extracts.frs"

            name "Extraction Merge"
            method GET
            url "https://api.example.test/items"

            extract runtime doc_id = json "$.doc.id"
            """;

        var imports = new Dictionary<string, string>
        {
            ["shared/extracts.frs"] =
                """
                extract runtime shared_id = json "$.shared.id"
                extract runtime doc_id = json "$.other.id"
                """,
        };

        var result = Compile(source, imports);

        Assert.IsTrue(result.Succeeded, Describe(result));
        var extractions = result.Payload!.Request.Extractions;
        Assert.AreEqual(2, extractions.Count);
        Assert.AreEqual("$.doc.id", extractions.Single(static item => item.TargetVariableName == "doc_id").Selector);
        Assert.AreEqual("$.shared.id", extractions.Single(static item => item.TargetVariableName == "shared_id").Selector);
    }

    [TestMethod]
    public void Compile_fills_missing_auth_keys_from_import_while_document_keys_win()
    {
        var source =
            """
            import "shared/auth.frs"

            name "Auth Merge"
            method GET
            url "https://api.example.test/items"

            auth token = "doc-token"
            """;

        var imports = new Dictionary<string, string>
        {
            ["shared/auth.frs"] =
                """
                auth mode = bearer
                auth token = "import-token"
                """,
        };

        var result = Compile(source, imports);

        Assert.IsTrue(result.Succeeded, Describe(result));
        Assert.AreEqual(AuthMode.BearerToken, result.Payload!.Request.Auth.Mode);
        Assert.AreEqual("doc-token", result.Payload.Request.Auth.BearerToken);
    }

    [TestMethod]
    public void Compile_merges_imported_query_parameters_and_skips_duplicate_names()
    {
        var source =
            """
            import "shared/query.frs"

            name "Query Merge"
            method GET
            url "https://api.example.test/items"

            query "page" = "1"
            """;

        var imports = new Dictionary<string, string>
        {
            ["shared/query.frs"] =
                """
                query "page" = "99"
                query "page_size" = "50"
                """,
        };

        var result = Compile(source, imports);

        Assert.IsTrue(result.Succeeded, Describe(result));
        Assert.AreEqual(2, result.Payload!.Request.QueryParameters.Count);
        Assert.AreEqual("1", GetEntry(result.Payload.Request.QueryParameters, "page"));
        Assert.AreEqual("50", GetEntry(result.Payload.Request.QueryParameters, "page_size"));
    }

    [TestMethod]
    public void Compile_merges_imported_form_values_into_the_body()
    {
        var source =
            """
            import "shared/form.frs"

            name "Form Merge"
            method POST
            url "https://api.example.test/items"

            form "grant_type" = "password"
            """;

        var imports = new Dictionary<string, string>
        {
            ["shared/form.frs"] =
                """
                form "grant_type" = "client_credentials"
                form "client_id" = "shared-client"
                """,
        };

        var result = Compile(source, imports);

        Assert.IsTrue(result.Succeeded, Describe(result));
        Assert.AreEqual(RequestBodyMode.FormUrlEncoded, result.Payload!.Request.Body.Mode);
        Assert.AreEqual("password", GetEntry(result.Payload.Request.Body.FormValues, "grant_type"));
        Assert.AreEqual("shared-client", GetEntry(result.Payload.Request.Body.FormValues, "client_id"));
    }

    #endregion

    #region Precedence

    [TestMethod]
    public void Compile_document_header_wins_over_import_on_case_insensitive_name_clash()
    {
        var source =
            """
            import "shared/common.frs"

            name "Header Clash"
            method GET
            url "https://api.example.test/items"

            header "X-Api-Key" = "doc"
            """;

        var imports = new Dictionary<string, string>
        {
            ["shared/common.frs"] =
                """
                header "x-api-key" = "import"
                """,
        };

        var result = Compile(source, imports);

        Assert.IsTrue(result.Succeeded, Describe(result));
        var matches = result.Payload!.Request.Headers
            .Where(static entry => string.Equals(entry.Key, "X-Api-Key", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.AreEqual(1, matches.Count);
        Assert.AreEqual("doc", matches[0].Value);
    }

    [TestMethod]
    public void Compile_document_variable_wins_over_imported_variable()
    {
        var source =
            """
            import "shared/variables.frs"

            name "Variable Precedence"
            method GET
            url "https://api.example.test/items"

            runtime token = "doc-value"
            """;

        var imports = new Dictionary<string, string>
        {
            ["shared/variables.frs"] =
                """
                runtime token = "import-value"
                runtime shared_only = "from-import"
                """,
        };

        var result = Compile(source, imports);

        Assert.IsTrue(result.Succeeded, Describe(result));
        Assert.AreEqual("doc-value", result.Payload!.RuntimeSeeds.Single(static seed => seed.Key == "token").LiteralValue);
        Assert.AreEqual("from-import", result.Payload.RuntimeSeeds.Single(static seed => seed.Key == "shared_only").LiteralValue);
    }

    [TestMethod]
    public void Compile_first_import_wins_when_two_imports_define_the_same_header()
    {
        var source =
            """
            import "shared/first.frs"
            import "shared/second.frs"

            name "Import Order"
            method GET
            url "https://api.example.test/items"
            """;

        var imports = new Dictionary<string, string>
        {
            ["shared/first.frs"] =
                """
                header "X-Shared" = "first"
                """,
            ["shared/second.frs"] =
                """
                header "X-Shared" = "second"
                header "X-Second-Only" = "second-only"
                """,
        };

        var result = Compile(source, imports);

        Assert.IsTrue(result.Succeeded, Describe(result));
        var matches = result.Payload!.Request.Headers
            .Where(static entry => string.Equals(entry.Key, "X-Shared", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.AreEqual(1, matches.Count);
        Assert.AreEqual("first", matches[0].Value);
        Assert.AreEqual("second-only", GetEntry(result.Payload.Request.Headers, "X-Second-Only"));
    }

    [TestMethod]
    public void Compile_transitive_imports_merge_surfaces_from_the_whole_chain()
    {
        var source =
            """
            import "shared/b.frs"

            name "Transitive Merge"
            method GET
            url "https://api.example.test/items"
            """;

        var imports = new Dictionary<string, string>
        {
            ["shared/b.frs"] =
                """
                import "shared/c.frs"

                header "X-B" = "b"
                """,
            ["shared/c.frs"] =
                """
                header "X-C" = "c"
                runtime c_var = "c-value"
                """,
        };

        var result = Compile(source, imports);

        Assert.IsTrue(result.Succeeded, Describe(result));
        Assert.AreEqual("b", GetEntry(result.Payload!.Request.Headers, "X-B"));
        Assert.AreEqual("c", GetEntry(result.Payload.Request.Headers, "X-C"));
        Assert.AreEqual("c-value", result.Payload.RuntimeSeeds.Single(static seed => seed.Key == "c_var").LiteralValue);
    }

    #endregion

    #region Import Resolution Diagnostics

    [TestMethod]
    [Timeout(10000)]
    public void Compile_circular_import_produces_a_warning_and_terminates()
    {
        var source =
            """
            import "shared/b.frs"

            name "Circular Import"
            method GET
            url "https://api.example.test/items"
            """;

        var imports = new Dictionary<string, string>
        {
            ["shared/b.frs"] =
                """
                import "shared/a.frs"

                header "X-B" = "b"
                """,
            ["shared/a.frs"] =
                """
                import "shared/b.frs"

                header "X-A" = "a"
                """,
        };

        var result = Compile(source, imports);

        Assert.IsTrue(result.Succeeded, Describe(result));
        Assert.IsTrue(
            result.Diagnostics.Any(static diagnostic =>
                diagnostic.Severity == ForRestScriptDiagnosticSeverity.Warning
                && diagnostic.Message.Contains("Circular import")),
            Describe(result));
        Assert.AreEqual("b", GetEntry(result.Payload!.Request.Headers, "X-B"));
        Assert.AreEqual("a", GetEntry(result.Payload.Request.Headers, "X-A"));
    }

    [TestMethod]
    public void Compile_unresolvable_import_produces_a_warning()
    {
        var source =
            """
            import "shared/missing.frs"

            name "Unresolvable Import"
            method GET
            url "https://api.example.test/items"
            """;

        var result = Compile(source, new());

        Assert.IsTrue(result.Succeeded, Describe(result));
        Assert.IsTrue(
            result.Diagnostics.Any(static diagnostic =>
                diagnostic.Severity == ForRestScriptDiagnosticSeverity.Warning
                && diagnostic.Message.Contains("Could not resolve import 'shared/missing.frs'")),
            Describe(result));
    }

    #endregion

    #region Helpers

    private static ForRestScriptCompilationResult Compile(string source, Dictionary<string, string> imports)
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        return compiler.Compile(
            source,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
                ResolveImport = path => imports.TryGetValue(path, out var imported) ? imported : null,
            });
    }

    private static string GetEntry(IEnumerable<KeyValueDefinition> entries, string key)
    {
        return entries.Single(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
    }

    private static string Describe(ForRestScriptCompilationResult result)
    {
        return string.Join(Environment.NewLine, result.Diagnostics.Select(static item => $"{item.Severity}: {item.Message}"));
    }

    #endregion
}
