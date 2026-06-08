namespace ForRest.Tests.Scripting;

/// <summary>
/// Regression coverage for a bare send statement (e.g. <c>request.send()</c> on its own line, as the
/// default new-request template uses inside its retry/on-status blocks). It translates to an awaited
/// send, which must be emitted as <c>await x;</c> — not <c>(await x);</c>, which is invalid C# (CS0201)
/// and made every freshly created request fail to run.
/// </summary>
[TestClass]
public sealed class BareSendStatementTests
{
    private static void AssertCompilesAndValidates(string flow)
    {
        var diagnostics = new System.Collections.Generic.List<ForRestScriptDiagnostic>();
        var generated = ForRestFlowScriptCompiler.Compile(flow, [], [], diagnostics);

        Assert.IsFalse(
            diagnostics.Exists(static d => d.Severity == ForRestScriptDiagnosticSeverity.Error),
            "flow compile reported errors: " + string.Join("; ", diagnostics.ConvertAll(static d => d.Message)));

        // The bare await must not be parenthesised in statement position.
        Assert.IsFalse(generated.Contains("(await request.send());", StringComparison.Ordinal));

        var engine = new RoslynScriptEngine(NullLogger<RoslynScriptEngine>.Instance);
        var validation = engine.Validate(generated);
        Assert.IsTrue(validation.Succeeded, "generated C# did not compile: " + validation.ErrorMessage);
    }

    [TestMethod]
    public void Bare_send_inside_retry_compiles()
    {
        AssertCompilesAndValidates(
            """
            retry 2 with backoff {
              request.send()
              if response.status != 429 {
                break
              }
            }
            """);
    }

    [TestMethod]
    public void Bare_send_at_top_level_compiles()
    {
        AssertCompilesAndValidates("request.send()");
    }

    [TestMethod]
    public void Default_new_request_template_shape_compiles()
    {
        // Mirrors the structure RequestWorkbenchDocumentFactory installs for a new request: top-level
        // on-error / on-status handlers (extracted by the parser) whose bodies contain a bare
        // request.send(). Compile the full document and Roslyn-validate the generated flow.
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var result = compiler.Compile(
            """
            name "New Request"
            method GET
            url "https://httpbin.org/anything"
            max_send_iterations 5

            runtime trace_id = guid()
            header "Accept" = "application/json"

            on error {
              error "Request failed unexpectedly"
            }

            on status 429 {
              warn "Rate limited — backing off"
              retry 2 with backoff {
                request.send()
                if response.status != 429 {
                  break
                }
              }
            }

            retry 3 with backoff {
              let sent = request.send() as "primary"
              if sent.status >= 200 and sent.status < 500 {
                break
              }
              warn $"Attempt returned {sent.status}, retrying..."
            }

            if response.status == 200 {
              log $"Success — status {response.status}"
            }
            """,
            new() { WorkspaceId = Guid.NewGuid() });

        Assert.IsTrue(
            result.Succeeded,
            "document compile failed: " + string.Join("; ", result.Diagnostics.Select(static d => d.Message)));
        Assert.IsNotNull(result.Payload);

        var engine = new RoslynScriptEngine(NullLogger<RoslynScriptEngine>.Instance);
        var validation = engine.Validate(result.Payload.Request.PreRequestScript);
        Assert.IsTrue(validation.Succeeded, "generated C# did not compile: " + validation.ErrorMessage);
    }
}
