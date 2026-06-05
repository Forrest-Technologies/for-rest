namespace ForRest.Tests.Scripting;

/// <summary>
/// Regression coverage for single-line inline blocks such as <c>if cond { stmt }</c>.
///
/// Before the inline-block expansion pass, an inline block (a line that ends in <c>}</c> rather
/// than <c>{</c>) broke the line-oriented flow compiler two ways:
/// <list type="number">
/// <item>The block-header collector greedily scanned forward to the next line ending in <c>{</c>,
/// swallowing intervening statements and a later block's braces. That surfaced as a spurious
/// "Flow block is missing a closing brace." — exactly what the security fuzzing scripts hit
/// whenever two <c>foreach</c> loops each contained an inline <c>if</c>.</item>
/// <item>When nothing was swallowed, the statement fell through to a raw, paren-less <c>if</c>
/// (<c>if cond { ... }</c>) which is invalid C#. The document compile never caught this because
/// it does not run Roslyn, so the failure only showed up at execution time as CS1003.</item>
/// </list>
/// These tests assert both that the document compiles and that the emitted C# is accepted by the
/// Roslyn engine.
/// </summary>
[TestClass]
public sealed class InlineBlockCompilationTests
{
    private static void AssertCompilesAndValidates(string source)
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var result = compiler.Compile(source, new() { WorkspaceId = Guid.NewGuid() });

        Assert.IsTrue(
            result.Succeeded,
            "Document compile failed: " + string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(static item => $"[{item.Severity}] {item.Message}")));
        Assert.IsNotNull(result.Payload);

        var engine = new RoslynScriptEngine(NullLogger<RoslynScriptEngine>.Instance);
        var validation = engine.Validate(result.Payload.Request.PreRequestScript);
        Assert.IsTrue(
            validation.Succeeded,
            "Generated C# did not compile under Roslyn: " + validation.ErrorMessage);
    }

    [TestMethod]
    public void Two_foreach_loops_each_with_an_inline_if_compile()
    {
        // The exact shape that produced "Flow block is missing a closing brace." There is no blank
        // line between the loops, so the second foreach's opening brace was previously swallowed by
        // the first loop's inline-if header collection.
        AssertCompilesAndValidates(
            """
            name "XSS Fuzzer"
            method GET
            url "https://httpbin.org/anything"

            foreach p in payloads.Category("xss") {
              request.url = $"https://httpbin.org/anything?q={encoding.UrlEncode(p)}"
              let r = request.send()
              let flag = "ok"
              if r.status >= 500 { flag = "5xx" }
              stash.Commit()
            }
            foreach p in payloads.Category("xss") {
              request.method = "POST"
              request.body = $"{{\"name\":\"{p}\"}}"
              let r = request.send()
              let flag = "ok"
              if r.status >= 500 { flag = "5xx" }
              stash.Commit()
            }
            """);
    }

    [TestMethod]
    public void Top_level_inline_if_emits_parenthesised_condition()
    {
        // Auth-boundary style: a sequence of top-level inline ifs. These previously emitted
        // paren-less `if cond { ... }` that Roslyn rejected with CS1003.
        AssertCompilesAndValidates(
            """
            name "Auth Boundary"
            method GET
            url "https://httpbin.org/anything"

            let r5 = request.send()
            let flag5 = "ok"
            if r5.status == 200 { flag5 = "accepted" }
            if r5.status >= 500 { flag5 = "server-error" }
            """);
    }

    [TestMethod]
    public void Inline_if_with_boolean_operators_compiles()
    {
        AssertCompilesAndValidates(
            """
            name "RCE probe"
            method GET
            url "https://httpbin.org/anything"

            foreach p in payloads.Category("command_injection") {
              let r = request.send()
              let body = convert.ToString(r.body)
              let flag = "ok"
              if strings.Contains(body, "uid=") or strings.Contains(body, "root") { flag = "rce" }
              stash.Commit()
            }
            """);
    }

    [TestMethod]
    public void Inline_if_followed_by_inline_else_compiles()
    {
        AssertCompilesAndValidates(
            """
            name "Branching"
            method GET
            url "https://httpbin.org/anything"

            let r = request.send()
            let flag = "ok"
            if r.status == 200 { flag = "ok" } else { flag = "bad" }
            """);
    }

    [TestMethod]
    public void Assignment_with_braces_in_a_string_is_left_intact()
    {
        // The inline-block expander must never touch ordinary statements whose values happen to
        // contain braces (interpolated JSON bodies are the common case).
        AssertCompilesAndValidates(
            """
            name "Body braces"
            method POST
            url "https://httpbin.org/anything"

            runtime trace_id = guid()
            let p = "x"
            request.content_type = "application/json"
            request.body = $"{{\"name\":\"{p}\",\"trace\":\"{trace_id}\"}}"
            let r = request.send()
            """);
    }

    [TestMethod]
    public void Conventional_multiline_blocks_still_compile()
    {
        // Guard against the expander disturbing the canonical multi-line form.
        AssertCompilesAndValidates(
            """
            name "Multiline"
            method GET
            url "https://httpbin.org/anything"

            foreach p in payloads.Category("xss") {
              let r = request.send()
              if r.status >= 500 {
                warn $"server error for {p}"
              }
              stash.Commit()
            }
            """);
    }
}
